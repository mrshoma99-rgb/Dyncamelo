using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Core.Editing;

/// <summary>Semantic family of a port's data; drives socket colour.</summary>
public enum PortFamily
{
    /// <summary>Unknown or any value.</summary>
    Any,
    /// <summary>Real numbers.</summary>
    Number,
    /// <summary>Integers and enums.</summary>
    Integer,
    /// <summary>True/false.</summary>
    Boolean,
    /// <summary>Text.</summary>
    Text,
    /// <summary>Dates and times.</summary>
    DateTime,
    /// <summary>Colours.</summary>
    Colour,
    /// <summary>Points, vectors, boxes, transforms.</summary>
    Geometry,
    /// <summary>Model items and models.</summary>
    Item,
    /// <summary>Selections and selection sets.</summary>
    Selection,
    /// <summary>Viewpoints and cameras.</summary>
    Viewpoint,
    /// <summary>Clash tests and results.</summary>
    Clash,
    /// <summary>Document context.</summary>
    Document,
    /// <summary>Dictionaries and property data.</summary>
    Data,
    /// <summary>File and folder paths.</summary>
    File,
    /// <summary>Workflow actions.</summary>
    Action,
}

/// <summary>Structure of a port's data; drives socket shape.</summary>
public enum PortDepth
{
    /// <summary>One value.</summary>
    Item,
    /// <summary>A list.</summary>
    List,
    /// <summary>A list of lists (or deeper).</summary>
    Nested,
    /// <summary>Not known until the graph runs.</summary>
    Unknown,
}

/// <summary>How well an output can feed an input (advisory; the engine stays permissive).</summary>
public enum Compat
{
    /// <summary>Same family and structure.</summary>
    Exact,
    /// <summary>A conversion, widening, list wrap or replication makes it work.</summary>
    Convertible,
    /// <summary>Allowed only because a side is untyped or loosely convertible.</summary>
    Loose,
    /// <summary>Not connectable.</summary>
    No,
}

/// <summary>A port's colour family plus structure.</summary>
public readonly struct PortKind : IEquatable<PortKind>
{
    /// <summary>Creates a kind.</summary>
    public PortKind(PortFamily family, PortDepth depth, bool isInferred = false)
    {
        Family = family;
        Depth = depth;
        IsInferred = isInferred;
    }

    /// <summary>Colour family.</summary>
    public PortFamily Family { get; }

    /// <summary>Structure.</summary>
    public PortDepth Depth { get; }

    /// <summary>True when derived from a runtime value rather than declared metadata.</summary>
    public bool IsInferred { get; }

    /// <summary>The neutral unknown kind.</summary>
    public static PortKind Unknown => new PortKind(PortFamily.Any, PortDepth.Unknown);

    /// <inheritdoc />
    public bool Equals(PortKind other) => Family == other.Family && Depth == other.Depth;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PortKind k && Equals(k);

    /// <inheritdoc />
    public override int GetHashCode() => ((int)Family * 397) ^ (int)Depth;

    /// <inheritdoc />
    public override string ToString() => Family + "/" + Depth;
}

/// <summary>Derives <see cref="PortKind"/>s from declared types, hints and runtime values, and compares them.</summary>
public static class PortKinds
{
    private static readonly Dictionary<string, PortFamily> ByTypeName = BuildTypeMap();

    private static Dictionary<string, PortFamily> BuildTypeMap()
    {
        var map = new Dictionary<string, PortFamily>(StringComparer.Ordinal);
        void Add(PortFamily family, params string[] names)
        {
            foreach (var n in names)
            {
                map[n] = family;
            }
        }

        Add(PortFamily.Number, "Double", "Single", "Decimal");
        Add(PortFamily.Integer, "Int32", "Int64", "Int16", "Byte", "SByte", "UInt16", "UInt32", "UInt64");
        Add(PortFamily.Boolean, "Boolean");
        Add(PortFamily.Text, "String", "Char");
        Add(PortFamily.DateTime, "DateTime", "TimeSpan", "DateTimeOffset", "TimelinerTask", "TimelinerTaskCollection", "TimelinerTaskType");
        Add(PortFamily.Colour, "Color", "Colour", "Brush", "SolidColorBrush");
        Add(PortFamily.Geometry, "Point", "Point2D", "Point3D", "Vector", "Vector3D", "Vector3", "BoundingBox", "BoundingBox3D",
            "Box", "Transform3D", "Rotation3D", "Matrix3D", "Line", "Plane");
        Add(PortFamily.Item, "ModelItem", "Model", "Units");
        Add(PortFamily.Selection, "ModelItemCollection", "SelectionSet", "SelectionSource", "Search", "Selection");
        Add(PortFamily.Viewpoint, "Viewpoint", "SavedViewpoint", "SavedItem", "FolderItem", "GroupItem", "Camera", "SavedViewpointAnimation", "SavedViewpointAnimationCut");
        Add(PortFamily.Clash, "ClashResult", "ClashTest", "ClashResultGroup", "ClashResultGroupBase", "ClashResultGroupCollection");
        Add(PortFamily.Document, "Document", "DocumentModels");
        Add(PortFamily.Data, "IDictionary", "Dictionary", "DataProperty", "PropertyCategory", "ParamMapRule", "DyncameloTable");
        Add(PortFamily.Action, "IWorkflowAction");
        return map;
    }

    /// <summary>Kind of a port from its explicit hint, then its declared type, then its name.</summary>
    public static PortKind FromPort(PortModel port)
    {
        if (port == null)
        {
            throw new ArgumentNullException(nameof(port));
        }

        if (TryParse(port.KindHint, out var hinted))
        {
            return hinted;
        }

        var kind = FromType(port.DeclaredType);
        if (kind.Family == PortFamily.Text && LooksLikePath(port.Name))
        {
            return new PortKind(PortFamily.File, kind.Depth);
        }

        return kind;
    }

    /// <summary>Kind of a CLR type (lists and arrays become depth List/Nested).</summary>
    public static PortKind FromType(Type type)
    {
        if (type == null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        var depth = 0;
        var current = Nullable.GetUnderlyingType(type) ?? type;
        while (depth < 3 && TryGetElementType(current, out var element))
        {
            depth++;
            current = Nullable.GetUnderlyingType(element) ?? element;
        }

        var family = FamilyOfScalar(current);
        PortDepth shape;
        if (depth == 0)
        {
            shape = family == PortFamily.Any && current == typeof(object) ? PortDepth.Unknown : PortDepth.Item;
        }
        else
        {
            shape = depth == 1 ? PortDepth.List : PortDepth.Nested;
            if (family == PortFamily.Any && current == typeof(object))
            {
                // A list of untyped items: structure is known, family is not.
                return new PortKind(PortFamily.Any, shape);
            }
        }

        return new PortKind(family, shape);
    }

    /// <summary>
    /// Kind of a type given by name, as written in the generated node catalog
    /// (<c>number</c>, <c>string[]</c>, <c>ModelItem[]</c>, <c>any</c>). Lets
    /// tools and tests classify ports without loading the declaring assembly.
    /// </summary>
    public static PortKind FromTypeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return PortKind.Unknown;
        }

        var text = name!.Trim();
        var depth = 0;
        while (text.EndsWith("[]", StringComparison.Ordinal))
        {
            depth++;
            text = text.Substring(0, text.Length - 2);
        }

        PortFamily family;
        switch (text.ToLowerInvariant())
        {
            case "number": family = PortFamily.Number; break;
            case "integer": family = PortFamily.Integer; break;
            case "boolean": family = PortFamily.Boolean; break;
            case "string": family = PortFamily.Text; break;
            case "datetime": family = PortFamily.DateTime; break;
            case "dict": family = PortFamily.Data; break;
            case "file": family = PortFamily.File; break;
            case "any":
            case "var":
            case "object": family = PortFamily.Any; break;
            default:
                family = ByTypeName.TryGetValue(text, out var f) ? f : PortFamily.Any;
                break;
        }

        var shape = depth == 0 ? (family == PortFamily.Any ? PortDepth.Unknown : PortDepth.Item)
            : depth == 1 ? PortDepth.List : PortDepth.Nested;
        return new PortKind(family, shape);
    }

    /// <summary>The hint string for a kind ("number", "viewpoint*", "text**"); empty when the kind says nothing.</summary>
    public static string ToHint(PortKind kind)
    {
        if (kind.Family == PortFamily.Any)
        {
            return string.Empty;
        }

        var stars = kind.Depth == PortDepth.List ? "*" : kind.Depth == PortDepth.Nested ? "**" : string.Empty;
        return kind.Family.ToString().ToLowerInvariant() + stars;
    }

    /// <summary>Parses a hint such as "viewpoint*" (list) or "text**" (nested). Unknown names fail.</summary>
    public static bool TryParse(string? hint, out PortKind kind)
    {
        kind = PortKind.Unknown;
        if (string.IsNullOrWhiteSpace(hint))
        {
            return false;
        }

        var text = hint!.Trim();
        var stars = 0;
        while (text.EndsWith("*", StringComparison.Ordinal))
        {
            stars++;
            text = text.Substring(0, text.Length - 1);
        }

        if (!Enum.TryParse<PortFamily>(text, ignoreCase: true, out var family))
        {
            return false;
        }

        var depth = stars == 0 ? PortDepth.Item : stars == 1 ? PortDepth.List : PortDepth.Nested;
        if (family == PortFamily.Any && stars == 0)
        {
            depth = PortDepth.Unknown;
        }

        kind = new PortKind(family, depth);
        return true;
    }

    /// <summary>Kind of a runtime value; falls back to <paramref name="declared"/> when the value says nothing.</summary>
    public static PortKind Observe(object? value, PortKind declared)
    {
        if (value == null)
        {
            return declared;
        }

        var depth = 0;
        var current = value;
        while (depth < 3 && current is IEnumerable enumerable && !(current is string) && !(current is IDictionary))
        {
            depth++;
            object? first = null;
            foreach (var element in enumerable)
            {
                if (element != null)
                {
                    first = element;
                    break;
                }
            }

            if (first == null)
            {
                // Empty (or all-null) list: structure known, family from the declaration.
                return new PortKind(declared.Family, depth == 1 ? PortDepth.List : PortDepth.Nested, isInferred: true);
            }

            current = first;
        }

        var family = FamilyOfScalar(current.GetType());
        if (family == PortFamily.Any && declared.Family != PortFamily.Any)
        {
            family = declared.Family;
        }

        var shape = depth == 0 ? PortDepth.Item : depth == 1 ? PortDepth.List : PortDepth.Nested;
        return new PortKind(family, shape, isInferred: true);
    }

    /// <summary>Compares kinds only (never returns <see cref="Compat.No"/>).</summary>
    public static Compat Compare(PortKind from, PortKind to)
    {
        var fromAny = from.Family == PortFamily.Any;
        var toAny = to.Family == PortFamily.Any;
        if (fromAny && toAny)
        {
            return Compat.Exact;
        }

        if (fromAny || toAny)
        {
            return Compat.Loose;
        }

        if (from.Family == to.Family)
        {
            return from.Depth == to.Depth || from.Depth == PortDepth.Unknown || to.Depth == PortDepth.Unknown
                ? Compat.Exact
                : Compat.Convertible;
        }

        if (IsNumeric(from.Family) && IsNumeric(to.Family))
        {
            return Compat.Convertible;
        }

        if ((from.Family == PortFamily.Item && to.Family == PortFamily.Selection) ||
            (from.Family == PortFamily.Selection && to.Family == PortFamily.Item))
        {
            return Compat.Convertible;
        }

        return Compat.Loose;
    }

    /// <summary>Full advisory compatibility of an output port feeding an input port.</summary>
    public static Compat Compare(PortModel output, PortModel input)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (output.Direction != PortDirection.Output || input.Direction != PortDirection.Input ||
            output.Owner == input.Owner ||
            !TypeCoercion.CanConvert(output.DeclaredType, input.DeclaredType))
        {
            return Compat.No;
        }

        return Compare(FromPort(output), FromPort(input));
    }

    private static bool IsNumeric(PortFamily family) => family == PortFamily.Number || family == PortFamily.Integer;

    private static bool LooksLikePath(string name)
    {
        var n = name.ToLowerInvariant();
        return n.EndsWith("path", StringComparison.Ordinal) || n.EndsWith("file", StringComparison.Ordinal) ||
               n.EndsWith("folder", StringComparison.Ordinal) || n.EndsWith("directory", StringComparison.Ordinal) ||
               n.EndsWith("filename", StringComparison.Ordinal);
    }

    private static bool TryGetElementType(Type type, out Type element)
    {
        element = typeof(object);
        if (type == typeof(string) || type == typeof(object))
        {
            return false;
        }

        if (type.IsArray)
        {
            element = type.GetElementType()!;
            return true;
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments();
            if (args.Length == 1 && (definition == typeof(List<>) || definition == typeof(IList<>) ||
                                     definition == typeof(IEnumerable<>) || definition == typeof(ICollection<>) ||
                                     definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>)))
            {
                element = args[0];
                return true;
            }

            if (args.Length == 1 && type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)) &&
                !typeof(IDictionary).IsAssignableFrom(type))
            {
                element = args[0];
                return true;
            }
        }

        return false;
    }

    private static PortFamily FamilyOfScalar(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t.IsEnum)
        {
            return PortFamily.Integer;
        }

        var name = t.Name;
        var tick = name.IndexOf('`');
        if (tick > 0)
        {
            name = name.Substring(0, tick);
        }

        // Dyncamelo's own value types (DyncameloColor, DyncameloPoint, …) are named like their Navisworks counterparts.
        if (name.Length > 9 && name.StartsWith("Dyncamelo", StringComparison.Ordinal))
        {
            name = name.Substring(9);
        }

        if (ByTypeName.TryGetValue(name, out var family))
        {
            return family;
        }

        if (typeof(IDictionary).IsAssignableFrom(t))
        {
            return PortFamily.Data;
        }

        return PortFamily.Any;
    }
}
