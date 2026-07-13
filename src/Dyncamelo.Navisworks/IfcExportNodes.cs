using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using BIMCamel.Collect;
using BIMCamel.Data;
using BIMCamel.Geometry;
using BIMCamel.Ifc;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>
/// IFC export nodes — a graph front-end to the BIMCamel IFC exporter engine
/// (github.com/mrshoma99-rgb/bimcamel-ifc-exporter, vendored as BIMCamel.dll). The
/// dock-pane exporter's whole pipeline is headless: resolve items → extract meshes →
/// write IFC. <see cref="ToIfc"/> is the export node; the <c>Export.Ifc*</c> builder
/// nodes assemble the optional coordinate / naming / mapping option objects it accepts,
/// so a graph can reach full parity with the exporter UI while each node stays simple.
/// </summary>
[NodeCategory("Navisworks.Export")]
public static class IfcExportNodes
{
    /// <summary>Exports model items to an IFC file via the BIMCamel exporter engine.</summary>
    /// <param name="items">The model items to export (resolved to leaf geometry, like the exporter's scope).</param>
    /// <param name="filePath">Destination .ifc path; the directory is created when missing.</param>
    /// <param name="schema">"IFC4" (default) or "IFC2x3".</param>
    /// <param name="instancing">Reuse repeated geometry as IfcMappedItem (smaller files); off writes every mesh in full.</param>
    /// <param name="properties">Write Navisworks properties as IfcPropertySets.</param>
    /// <param name="materials">Write element colours as IfcSurfaceStyle / IfcMaterial.</param>
    /// <param name="quantities">Compute base quantities (volume/area/length) from the mesh.</param>
    /// <param name="units">Source units: "Auto" (from the model) or Millimeters/Centimeters/Meters/Feet/Inches.</param>
    /// <param name="quality">"Balanced" (default), "Small file" or "High detail" — sets weld tolerance and coordinate precision.</param>
    /// <param name="coordinates">Base-point / georeferencing options from Export.IfcCoordinates (default: geometry origin + georef).</param>
    /// <param name="spatialNames">Project/Site/Building/Storey names from Export.IfcSpatialNames (default: sensible names).</param>
    /// <param name="roles">Type/Level/Material/Classification source-property mapping from Export.IfcRoles.</param>
    /// <param name="parameterRules">Property rename/relocate rules from Export.IfcParameterRule.</param>
    /// <param name="categoryFilter">Only export these property categories (null = all).</param>
    /// <param name="classMap">Item→IFC-class overrides from Export.IfcSetClassMap.</param>
    /// <param name="splitMegabytes">Split the output into parts near this size (0 = single file).</param>
    /// <param name="validate">Run the built-in structural validator on each written file.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written path(s) and export statistics.</returns>
    [NodeName("Export.ToIfc")]
    [NodeDescription("Exports model items to IFC (IFC4/IFC2x3) via the BIMCamel exporter: spatial tree, instancing, property sets, materials, base quantities and georeferencing.")]
    [NodeSearchTags("export", "ifc", "openbim", "bim", "qto", "ifc4", "ifc2x3", "bimcamel")]
    [MultiReturn("filePath", "fileCount", "elementCount", "triangleCount", "fileSizeKb")]
    public static Dictionary<string, object?> ToIfc(
        IEnumerable<ModelItem> items,
        string filePath,
        [NodeChoices("IFC4", "IFC2X3")]
        string schema = "IFC4",
        bool instancing = true,
        bool properties = true,
        bool materials = true,
        bool quantities = true,
        string units = "Auto",
        string quality = "Balanced",
        CoordOptions? coordinates = null,
        SpatialNames? spatialNames = null,
        PropertyRoles? roles = null,
        IEnumerable<ParamMapRule>? parameterRules = null,
        IEnumerable<string>? categoryFilter = null,
        Dictionary<string, string>? classMap = null,
        double splitMegabytes = 0,
        bool validate = false,
        Document? document = null)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No model items provided.");
        }

        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        if (!string.Equals(Path.GetExtension(filePath), ".ifc", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("'" + filePath + "' must end in .ifc.", nameof(filePath));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var ifcSchema = ParseSchema(schema);
        var (unitScale, _) = ResolveUnits(doc, units);
        var (weldTolMetres, coordDecimals) = ResolveQuality(quality);

        // Resolve to leaf items that carry geometry — the exporter's own scope handling.
        var leaves = ItemCollector.ResolveLeaves(NavisValues.ToItemList(items));
        if (leaves.Count == 0)
        {
            throw new InvalidOperationException(
                "No geometry elements to export. Wire a selection of model items (or their parents) into 'items'.");
        }

        var coords = coordinates ?? new CoordOptions();
        var names = spatialNames ?? new SpatialNames();
        var opts = new ExtractOptions
        {
            Props = properties,
            Materials = materials,
            PsetFilter = categoryFilter != null ? new HashSet<string>(categoryFilter, StringComparer.OrdinalIgnoreCase) : null,
            ClassMap = classMap,
            ParamMap = parameterRules?.Where(r => r != null).ToList(),
            Roles = roles ?? new PropertyRoles(),
        };

        long splitLimit = splitMegabytes > 0 ? (long)(splitMegabytes * 1024 * 1024) : 0;

        EnsureDirectory(filePath);

        var sm = ItemCollector.ScopeMinCorner(leaves);
        var geomMin = (sm.x * unitScale, sm.y * unitScale, sm.z * unitScale);
        var author = Environment.UserName;
        var summary = new ExportSummary();

        // The exporter streams: the extractor yields one element at a time and the writer
        // emits it immediately, so the whole model is never held in memory at once.
        if (instancing)
        {
            opts.WeldTol = weldTolMetres;
            var stream = InstancedExtractor.ExtractStream(leaves, unitScale, opts);
            IfcExporter.ExportInstanced(filePath, ifcSchema, stream, author, unitScale, coords, quantities, coordDecimals, geomMin, names, splitLimit, summary);
        }
        else
        {
            opts.WeldTol = weldTolMetres / unitScale;
            var stream = MeshExtractor.ExtractStream(leaves, opts);
            IfcExporter.Export(filePath, ifcSchema, stream, author, unitScale, coords, quantities, coordDecimals, geomMin, names, splitLimit, summary);
        }

        if (validate && summary.Files.Count > 0)
        {
            var issues = new List<string>();
            foreach (var f in summary.Files)
            {
                foreach (var issue in IfcValidator.Validate(f))
                {
                    issues.Add(Path.GetFileName(f) + ": " + issue);
                }
            }

            if (issues.Count > 0)
            {
                throw new InvalidOperationException(
                    "IFC validation reported " + issues.Count + " issue(s): " + string.Join(" | ", issues));
            }
        }

        return new Dictionary<string, object?>
        {
            ["filePath"] = summary.Files.Count == 1 ? summary.Files[0] : summary.Files.Cast<object?>().ToList(),
            ["fileCount"] = summary.FileCount,
            ["elementCount"] = summary.ElementCount,
            ["triangleCount"] = summary.TriangleCount,
            ["fileSizeKb"] = Math.Round(summary.FileSizeBytes / 1024.0, 1),
        };
    }

    /// <summary>Builds coordinate / georeferencing options for Export.ToIfc.</summary>
    /// <param name="basePoint">"GeometryOrigin" (default), "ModelOrigin" or "Custom".</param>
    /// <param name="eastings">Custom base-point easting in metres (Custom mode).</param>
    /// <param name="northings">Custom base-point northing in metres (Custom mode).</param>
    /// <param name="elevation">Custom base-point elevation in metres (Custom mode).</param>
    /// <param name="rotationDegrees">Grid/true-north rotation, recorded in IFC4 georeferencing.</param>
    /// <param name="writeGeoref">Write IfcMapConversion/IfcProjectedCRS (IFC4 only).</param>
    /// <returns>A coordinate-options object to wire into Export.ToIfc.</returns>
    [NodeName("Export.IfcCoordinates")]
    [NodeDescription("Base-point and georeferencing options for Export.ToIfc: geometry/model/custom origin, rotation and IFC4 georeferencing.")]
    [NodeSearchTags("ifc", "export", "coordinates", "base point", "georeferencing", "origin", "rotation")]
    [return: NodeName("coordinates")]
    public static CoordOptions IfcCoordinates(
        string basePoint = "GeometryOrigin",
        double eastings = 0,
        double northings = 0,
        double elevation = 0,
        double rotationDegrees = 0,
        bool writeGeoref = true)
    {
        return new CoordOptions
        {
            Mode = ParseBasePoint(basePoint),
            CustomEastings = eastings,
            CustomNorthings = northings,
            CustomElevation = elevation,
            RotationDeg = rotationDegrees,
            WriteGeoref = writeGeoref,
        };
    }

    /// <summary>Builds the IFC spatial-skeleton names for Export.ToIfc.</summary>
    /// <param name="project">IfcProject name.</param>
    /// <param name="site">IfcSite name.</param>
    /// <param name="building">IfcBuilding name.</param>
    /// <param name="storey">Default IfcBuildingStorey name (level-less elements).</param>
    /// <returns>A spatial-names object to wire into Export.ToIfc.</returns>
    [NodeName("Export.IfcSpatialNames")]
    [NodeDescription("Names for the IFC spatial tree (Project/Site/Building/default Storey) used by Export.ToIfc.")]
    [NodeSearchTags("ifc", "export", "project", "site", "building", "storey", "spatial")]
    [return: NodeName("spatialNames")]
    public static SpatialNames IfcSpatialNames(
        string project = "BIMCamel Export",
        string site = "Site",
        string building = "Building",
        string storey = "Storey")
    {
        return new SpatialNames
        {
            Project = string.IsNullOrWhiteSpace(project) ? "BIMCamel Export" : project.Trim(),
            Site = string.IsNullOrWhiteSpace(site) ? "Site" : site.Trim(),
            Building = string.IsNullOrWhiteSpace(building) ? "Building" : building.Trim(),
            Storey = string.IsNullOrWhiteSpace(storey) ? "Storey" : storey.Trim(),
        };
    }

    /// <summary>Maps source properties to IFC semantic roles for Export.ToIfc.</summary>
    /// <param name="typeProperty">Property whose value groups occurrences into IfcElementType (+ ObjectType).</param>
    /// <param name="typeCategory">Category carrying the type property (blank = any).</param>
    /// <param name="levelProperty">Property whose value assigns each element to an IfcBuildingStorey.</param>
    /// <param name="levelCategory">Category carrying the level property (blank = any).</param>
    /// <param name="materialProperty">Property whose value becomes the IfcMaterial name.</param>
    /// <param name="materialCategory">Category carrying the material property (blank = any).</param>
    /// <param name="classificationProperty">Property whose value becomes an IfcClassificationReference (IFC4).</param>
    /// <param name="classificationCategory">Category carrying the classification property (blank = any).</param>
    /// <returns>A property-roles object to wire into Export.ToIfc.</returns>
    [NodeName("Export.IfcRoles")]
    [NodeDescription("Maps Navisworks source properties to IFC roles — Type→IfcElementType, Level→IfcBuildingStorey, Material→IfcMaterial, Classification→IfcClassificationReference — for Export.ToIfc.")]
    [NodeSearchTags("ifc", "export", "roles", "type", "level", "storey", "material", "classification", "mapping")]
    [return: NodeName("roles")]
    public static PropertyRoles IfcRoles(
        string typeProperty = "",
        string typeCategory = "",
        string levelProperty = "",
        string levelCategory = "",
        string materialProperty = "",
        string materialCategory = "",
        string classificationProperty = "",
        string classificationCategory = "")
    {
        return new PropertyRoles
        {
            Type = new PropRef(typeCategory, typeProperty),
            Level = new PropRef(levelCategory, levelProperty),
            Material = new PropRef(materialCategory, materialProperty),
            Classification = new PropRef(classificationCategory, classificationProperty),
        };
    }

    /// <summary>Builds one property rename/relocate rule for Export.ToIfc's parameterRules.</summary>
    /// <param name="source">Source property name to match (case-insensitive).</param>
    /// <param name="targetPset">Destination Pset (blank = keep original category).</param>
    /// <param name="targetName">Destination property name (blank = keep original name).</param>
    /// <param name="sourceCategory">Only match this source category (blank = any).</param>
    /// <returns>A parameter-map rule; collect several into a list for Export.ToIfc.</returns>
    [NodeName("Export.IfcParameterRule")]
    [NodeDescription("One property rename/relocate rule for Export.ToIfc — moves/renames a source property into a target IFC Pset/name on export.")]
    [NodeSearchTags("ifc", "export", "parameter", "property", "pset", "rename", "map", "rule")]
    [return: NodeName("rule")]
    public static ParamMapRule IfcParameterRule(
        string source,
        string targetPset = "",
        string targetName = "",
        string sourceCategory = "")
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("A source property name is required.", nameof(source));
        }

        return new ParamMapRule
        {
            Source = source.Trim(),
            TargetPset = (targetPset ?? string.Empty).Trim(),
            TargetName = (targetName ?? string.Empty).Trim(),
            SourceCategory = (sourceCategory ?? string.Empty).Trim(),
        };
    }

    /// <summary>Builds an item→IFC-class map from saved-set → class rules for Export.ToIfc.</summary>
    /// <param name="setNames">Saved/search set display names to map.</param>
    /// <param name="ifcClasses">IFC class per set (friendly names, e.g. "Wall", "Beam"; see Export.IfcClasses).</param>
    /// <param name="predefinedTypes">Optional predefined type per set (parallel list; blank entries allowed).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>An item→class map to wire into Export.ToIfc's classMap.</returns>
    [NodeName("Export.IfcSetClassMap")]
    [NodeDescription("Assigns an IFC class to every item of the named saved/search sets (set→class), producing the classMap for Export.ToIfc.")]
    [NodeSearchTags("ifc", "export", "class", "set", "selection set", "map", "type", "ifcwall")]
    [return: NodeName("classMap")]
    public static Dictionary<string, string> IfcSetClassMap(
        IEnumerable<string> setNames,
        IEnumerable<string> ifcClasses,
        IEnumerable<string>? predefinedTypes = null,
        Document? document = null)
    {
        if (setNames == null)
        {
            throw new ArgumentNullException(nameof(setNames), "No set names provided.");
        }

        if (ifcClasses == null)
        {
            throw new ArgumentNullException(nameof(ifcClasses), "No IFC classes provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var nameList = setNames.ToList();
        var classList = ifcClasses.ToList();
        var predefList = predefinedTypes?.ToList() ?? new List<string>();

        if (nameList.Count != classList.Count)
        {
            throw new ArgumentException(
                "setNames and ifcClasses must be the same length (" + nameList.Count + " vs " + classList.Count + ").");
        }

        var sets = ItemCollector.GetSelectionSets(doc);
        var rules = new List<(SelectionSet set, string classKey)>();
        for (int i = 0; i < nameList.Count; i++)
        {
            var friendly = (classList[i] ?? string.Empty).Trim();
            if (!TypeMapping.Catalog.ContainsKey(friendly))
            {
                throw new ArgumentException(
                    "'" + classList[i] + "' is not a known IFC class. Use one of Export.IfcClasses.");
            }

            var set = sets.FirstOrDefault(s => string.Equals(s.DisplayName, nameList[i], StringComparison.Ordinal));
            if (set == null)
            {
                throw new ArgumentException("No saved/search set named '" + nameList[i] + "' in the document.");
            }

            var predef = i < predefList.Count ? predefList[i] : null;
            rules.Add((set, TypeMapping.Encode(friendly, predef)));
        }

        return ItemCollector.BuildClassMap(doc, rules);
    }

    /// <summary>The friendly IFC class names accepted by Export.IfcSetClassMap.</summary>
    /// <returns>The catalogue of mappable IFC class names.</returns>
    [NodeName("Export.IfcClasses")]
    [NodeDescription("Lists the friendly IFC class names (Wall, Beam, Door, …) accepted by Export.IfcSetClassMap.")]
    [NodeSearchTags("ifc", "export", "class", "catalog", "types")]
    [return: NodeName("classes")]
    public static List<string> IfcClasses() => TypeMapping.Keys();

    // ── option parsing ──────────────────────────────────────────────────────────
    private static IfcSchema ParseSchema(string schema)
    {
        var s = (schema ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty);
        switch (s)
        {
            case "IFC4":
            case "":
                return IfcSchema.Ifc4;
            case "IFC2X3":
                return IfcSchema.Ifc2x3;
            default:
                throw new ArgumentException("Unknown schema '" + schema + "'. Use \"IFC4\" or \"IFC2x3\".", nameof(schema));
        }
    }

    private static BasePointMode ParseBasePoint(string basePoint)
    {
        var s = (basePoint ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty);
        switch (s)
        {
            case "GEOMETRYORIGIN":
            case "GEOMETRY":
            case "":
                return BasePointMode.GeometryOrigin;
            case "MODELORIGIN":
            case "MODEL":
                return BasePointMode.ModelOrigin;
            case "CUSTOM":
                return BasePointMode.Custom;
            default:
                throw new ArgumentException(
                    "Unknown base point '" + basePoint + "'. Use \"GeometryOrigin\", \"ModelOrigin\" or \"Custom\".", nameof(basePoint));
        }
    }

    private static (double weldTolMetres, int coordDecimals) ResolveQuality(string quality)
    {
        var s = (quality ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty);
        switch (s)
        {
            case "SMALLFILE":
            case "SMALL":
                return (1e-3, 3);
            case "HIGHDETAIL":
            case "HIGH":
                return (1e-6, 6);
            case "BALANCED":
            case "":
                return (1e-4, 4);
            default:
                throw new ArgumentException(
                    "Unknown quality '" + quality + "'. Use \"Balanced\", \"Small file\" or \"High detail\".", nameof(quality));
        }
    }

    // Mirrors the exporter UI's unit resolution (forced units, else the model's own units → metres).
    private static (double scale, string name) ResolveUnits(Document doc, string units)
    {
        var s = (units ?? string.Empty).Trim().ToUpperInvariant();
        switch (s)
        {
            case "MILLIMETERS":
            case "MILLIMETRES":
            case "MM":
                return (0.001, "mm (forced)");
            case "CENTIMETERS":
            case "CENTIMETRES":
            case "CM":
                return (0.01, "cm (forced)");
            case "METERS":
            case "METRES":
            case "M":
                return (1.0, "m (forced)");
            case "FEET":
            case "FT":
                return (0.3048, "ft (forced)");
            case "INCHES":
            case "IN":
                return (0.0254, "in (forced)");
            case "AUTO":
            case "":
                return UnitsToMetre(doc.Units);
            default:
                throw new ArgumentException(
                    "Unknown units '" + units + "'. Use \"Auto\", \"Millimeters\", \"Centimeters\", \"Meters\", \"Feet\" or \"Inches\".", nameof(units));
        }
    }

    private static (double scale, string name) UnitsToMetre(Units u)
    {
        switch (u)
        {
            case Units.Meters: return (1.0, "m");
            case Units.Centimeters: return (0.01, "cm");
            case Units.Millimeters: return (0.001, "mm");
            case Units.Kilometers: return (1000.0, "km");
            case Units.Feet: return (0.3048, "ft");
            case Units.Inches: return (0.0254, "in");
            case Units.Yards: return (0.9144, "yd");
            case Units.Miles: return (1609.344, "mi");
            default: return (1.0, u.ToString());
        }
    }

    private static void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
