using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;
using Dyncamelo.Nodes.Portable;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Viewpoint transfer between models: exports saved viewpoints (camera +
/// section box + folder location) to a portable JSON package and rebuilds them
/// in another document — including one in different units, converted via
/// <see cref="UnitConversion.ScaleFactor"/>. Element overrides are deliberately
/// out of scope: they are bound to model-item paths inside the source file and
/// cannot survive into a different model (Document.Merge covers that case for
/// same-model federations).
/// </summary>
[NodeCategory("Navisworks.Viewpoints")]
public static class ViewpointTransferNodes
{
    /// <summary>Exports saved viewpoints to a portable package file.</summary>
    /// <param name="filePath">Where to write the package (a .json file, e.g. from a File Path node).</param>
    /// <param name="viewpoints">What to export: nothing = every saved viewpoint; or a viewpoint, a folder, a name (viewpoint first, then folder), or a list of these.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written file path, how many viewpoints it holds, and a summary report.</returns>
    [NodeName("Viewpoints.ExportFile")]
    [NodeDescription(
        "Exports saved viewpoints — camera, section box and folder location — to a portable JSON package " +
        "for Viewpoints.ImportFile to rebuild in ANOTHER model, even one in different units. Leave " +
        "viewpoints empty to export them all, or wire specific viewpoints, folders or names. Element " +
        "overrides are not carried (they only exist relative to the source model's items).")]
    [NodeSearchTags("viewpoints", "export", "transfer", "copy", "between", "models", "package", "camera", "section", "box")]
    [MultiReturn("filePath", "count", "report")]
    public static Dictionary<string, object?> ExportFile(
        string filePath,
        object? viewpoints = null,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var tree = doc.SavedViewpoints;

        var selected = new List<SavedViewpoint>();
        var seen = new HashSet<SavedViewpoint>();
        CollectViews(tree.RootItem, viewpoints, selected, seen);
        if (selected.Count == 0)
        {
            throw new InvalidOperationException(
                viewpoints == null
                    ? "The document has no saved viewpoints to export."
                    : "Nothing matched the viewpoints input — wire saved viewpoints, folders, or their names.");
        }

        var package = new ViewpointPackageFile
        {
            SourceUnits = doc.Units.ToString(),
            SourceDocument = SafeFileName(doc),
        };

        int boxes = 0;
        int planeModeViews = 0;
        foreach (var stored in selected)
        {
            var view = CaptureView(tree.RootItem, stored, ref boxes, ref planeModeViews);
            package.Views.Add(view);
        }

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, package.ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var report = new StringBuilder();
        report.AppendLine("Exported " + package.Views.Count + " viewpoint(s) to " + filePath);
        report.AppendLine("Source units: " + package.SourceUnits);
        report.Append("Section boxes: " + boxes);
#if NAV2024
        if (planeModeViews > 0)
        {
            report.AppendLine();
            report.Append(planeModeViews + " viewpoint(s) use plane sectioning (not a box) — exported camera-only.");
        }
#else
        report.AppendLine();
        report.Append("Section boxes are not captured on Navisworks 2025/2026 yet — cameras exported in full.");
#endif

        return new Dictionary<string, object?>
        {
            ["filePath"] = filePath,
            ["count"] = package.Views.Count,
            ["report"] = report.ToString(),
        };
    }

    /// <summary>Imports viewpoints from a package file into the current model.</summary>
    /// <param name="filePath">The package file written by Viewpoints.ExportFile.</param>
    /// <param name="folderName">Put every imported view under this folder ("A/B" nests); null/empty recreates each view's original folders.</param>
    /// <param name="overwrite">True replaces a same-named viewpoint in the same folder; false always adds.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored viewpoints, how many were imported, and a summary report.</returns>
    [NodeName("Viewpoints.ImportFile")]
    [NodeDescription(
        "Rebuilds the viewpoints from a Viewpoints.ExportFile package in THIS model: camera, section box " +
        "and folder structure. Positions are converted automatically when the source model used different " +
        "units. Re-runs update same-named views instead of duplicating (overwrite).")]
    [NodeSearchTags("viewpoints", "import", "transfer", "copy", "between", "models", "package", "restore")]
    [MultiReturn("viewpoints", "count", "report")]
    public static Dictionary<string, object?> ImportFile(
        string filePath,
        string? folderName = null,
        bool overwrite = true,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The file '" + filePath + "' does not exist.", filePath);
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var package = ViewpointPackageFile.Parse(File.ReadAllText(filePath));
        if (package.Views.Count == 0)
        {
            throw new InvalidOperationException("The package '" + filePath + "' contains no viewpoints.");
        }

        // Convert lengths when the source model's units differ from this one's.
        double scale = 1.0;
        string unitsNote;
        if (!string.IsNullOrEmpty(package.SourceUnits) &&
            Enum.TryParse<Units>(package.SourceUnits, true, out var sourceUnits))
        {
            scale = UnitConversion.ScaleFactor(sourceUnits, doc.Units);
            unitsNote = scale == 1.0
                ? "Units: " + doc.Units + " (no conversion needed)"
                : "Units: converted " + sourceUnits + " → " + doc.Units +
                  " (× " + scale.ToString("0.######", CultureInfo.InvariantCulture) + ")";
        }
        else
        {
            unitsNote = "Units: the package does not name valid source units — lengths imported unconverted.";
        }

        if (scale != 1.0)
        {
            package.ScaleLengths(scale);
        }

        var tree = doc.SavedViewpoints;
        var overrideSegments = SplitFolderName(folderName);

        int created = 0;
        int replaced = 0;
        int boxesRestored = 0;
        int boxesSkipped = 0;
        var stored = new List<object?>();
        foreach (var view in package.Views)
        {
            var navisViewpoint = BuildViewpoint(view, ref boxesRestored, ref boxesSkipped);
            var saved = new SavedViewpoint(navisViewpoint) { DisplayName = view.Name };

            var folder = ResolveTargetFolder(tree, overrideSegments ?? view.FolderPath);
            var children = folder != null ? folder.Children : tree.Value;
            var existingIndex = overwrite
                ? NavisValues.FindTopLevelIndex<SavedViewpoint>(children, view.Name)
                : -1;

            if (existingIndex >= 0)
            {
                if (folder != null)
                {
                    tree.ReplaceWithCopy(folder, existingIndex, saved);
                }
                else
                {
                    tree.ReplaceWithCopy(existingIndex, saved);
                }

                replaced++;
            }
            else
            {
                if (folder != null)
                {
                    tree.AddCopy(folder, saved);
                }
                else
                {
                    tree.AddCopy(saved);
                }

                created++;
            }

            // AddCopy/ReplaceWithCopy store a copy — hand the stored instance downstream.
            children = folder != null ? folder.Children : tree.Value;
            var storedIndex = NavisValues.FindTopLevelIndex<SavedViewpoint>(children, view.Name);
            stored.Add(storedIndex >= 0 ? children[storedIndex] : (object)saved);
        }

        var report = new StringBuilder();
        report.AppendLine("Imported " + package.Views.Count + " viewpoint(s) from " + filePath);
        report.AppendLine(unitsNote);
        report.Append(created + " added, " + replaced + " replaced; section boxes restored: " + boxesRestored);
        if (boxesSkipped > 0)
        {
#if NAV2024
            report.AppendLine();
            report.Append(boxesSkipped + " section box(es) could not be applied — those views imported camera-only.");
#else
            report.AppendLine();
            report.Append(boxesSkipped + " section box(es) skipped — not supported on Navisworks 2025/2026 yet; cameras imported in full.");
#endif
        }

        return new Dictionary<string, object?>
        {
            ["viewpoints"] = stored,
            ["count"] = package.Views.Count,
            ["report"] = report.ToString(),
        };
    }

    // ------------------------------------------------------------- Export side

    /// <summary>
    /// Resolves the flexible viewpoints input into stored viewpoints, in tree
    /// order for whole-tree/folder inputs and wire order for explicit ones.
    /// </summary>
    private static void CollectViews(
        FolderItem root, object? value, List<SavedViewpoint> results, HashSet<SavedViewpoint> seen)
    {
        switch (value)
        {
            case null:
                CollectTree(root.Children, results, seen);
                return;
            case SavedViewpoint viewpoint:
                Add(SavedItemTreeHelpers.ResolveStored<SavedViewpoint>(root, viewpoint, "saved viewpoint"), results, seen);
                return;
            case FolderItem folder:
                var storedFolder = SavedItemTreeHelpers.ResolveStored<FolderItem>(root, folder, "viewpoint folder");
                CollectTree(storedFolder.Children, results, seen);
                return;
            case string name:
                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentException("An empty name cannot identify a viewpoint or folder.", nameof(value));
                }

                var byName = SavedItemTreeHelpers.FindByName<SavedViewpoint>(root.Children, name);
                if (byName != null)
                {
                    Add(byName, results, seen);
                    return;
                }

                var folderByName = SavedItemTreeHelpers.FindByName<FolderItem>(root.Children, name)
                    ?? throw new InvalidOperationException(
                        "No saved viewpoint or folder named '" + name + "' exists in the document.");
                CollectTree(folderByName.Children, results, seen);
                return;
            case IEnumerable list when !(value is string):
                foreach (var item in list)
                {
                    if (item != null)
                    {
                        CollectViews(root, item, results, seen);
                    }
                }

                return;
            default:
                throw new ArgumentException(
                    "Cannot interpret a value of type '" + value.GetType().Name +
                    "' as viewpoints. Wire saved viewpoints, folders, names, or a list of these.", nameof(value));
        }
    }

    private static void CollectTree(
        IEnumerable<SavedItem> items, List<SavedViewpoint> results, HashSet<SavedViewpoint> seen)
    {
        foreach (var item in items)
        {
            if (item is SavedViewpoint viewpoint)
            {
                Add(viewpoint, results, seen);
            }
            else if (item is GroupItem group && !(item is SavedViewpointAnimation))
            {
                CollectTree(group.Children, results, seen);
            }
        }
    }

    private static void Add(SavedViewpoint viewpoint, List<SavedViewpoint> results, HashSet<SavedViewpoint> seen)
    {
        if (seen.Add(viewpoint))
        {
            results.Add(viewpoint);
        }
    }

    private static PortableViewpoint CaptureView(
        FolderItem root, SavedViewpoint stored, ref int boxes, ref int planeModeViews)
    {
        var vp = stored.Viewpoint;
        var rotation = vp.Rotation;
        var view = new PortableViewpoint
        {
            Name = stored.DisplayName ?? "Viewpoint",
            FolderPath = FolderSegments(root, stored),
            RawCamera = vp.GetCamera(),
            Position = new[] { vp.Position.X, vp.Position.Y, vp.Position.Z },
            Rotation = new[] { rotation.A, rotation.B, rotation.C, rotation.D },
            Projection = vp.Projection.ToString(),
            HeightField = vp.HeightField,
            AspectRatio = vp.AspectRatio,
            FocalDistance = vp.HasFocalDistance ? vp.FocalDistance : (double?)null,
            WorldUp = vp.HasWorldUpVector
                ? new[] { vp.WorldUpVector.X, vp.WorldUpVector.Y, vp.WorldUpVector.Z }
                : null,
        };

#if NAV2024
        var clip = vp.InternalClipPlanes;
        if (clip != null && clip.IsEnabled())
        {
            if (clip.GetMode() == LcOaClipPlaneSetMode.eMODE_BOX)
            {
                var box = new BoundingBox3D();
                var boxRotation = new Rotation3D();
                clip.GetOrientedBox(box, boxRotation);
                if (box.Max.X > box.Min.X && box.Max.Y > box.Min.Y && box.Max.Z > box.Min.Z)
                {
                    view.SectionBox = new PortableSectionBox
                    {
                        Enabled = true,
                        Min = new[] { box.Min.X, box.Min.Y, box.Min.Z },
                        Max = new[] { box.Max.X, box.Max.Y, box.Max.Z },
                        Rotation = IsIdentity(boxRotation)
                            ? null
                            : new[] { boxRotation.A, boxRotation.B, boxRotation.C, boxRotation.D },
                    };
                    boxes++;
                }
            }
            else
            {
                planeModeViews++;
            }
        }
#endif

        return view;
    }

    private static List<string> FolderSegments(FolderItem root, SavedViewpoint stored)
    {
        var segments = new List<string>();
        for (var parent = stored.Parent; parent != null && !ReferenceEquals(parent, root); parent = parent.Parent)
        {
            if (!string.IsNullOrEmpty(parent.DisplayName))
            {
                segments.Insert(0, parent.DisplayName);
            }
        }

        return segments;
    }

#if NAV2024
    private static bool IsIdentity(Rotation3D rotation)
    {
        const double epsilon = 1e-9;
        return Math.Abs(rotation.A) < epsilon &&
               Math.Abs(rotation.B) < epsilon &&
               Math.Abs(rotation.C) < epsilon &&
               Math.Abs(Math.Abs(rotation.D) - 1.0) < epsilon;
    }
#endif

    private static string? SafeFileName(Document doc)
    {
        try
        {
            var name = doc.CurrentFileName;
            return string.IsNullOrEmpty(name) ? doc.Title : Path.GetFileName(name);
        }
        catch (Exception)
        {
            return null; // a never-saved document has no file name — informational only
        }
    }

    // ------------------------------------------------------------- Import side

    private static Viewpoint BuildViewpoint(PortableViewpoint view, ref int boxesRestored, ref int boxesSkipped)
    {
        var vp = new Viewpoint();

        // The raw camera string is bit-perfect but only valid unconverted —
        // ScaleLengths clears it, so its presence means it is safe to use.
        bool rawApplied = !string.IsNullOrEmpty(view.RawCamera) && vp.TrySetCamera(view.RawCamera);
        if (!rawApplied)
        {
            if (view.Position == null || view.Position.Length < 3 ||
                view.Rotation == null || view.Rotation.Length < 4)
            {
                throw new InvalidOperationException(
                    "The package's view '" + view.Name + "' has no usable camera — the file is damaged.");
            }

            vp.Position = new Point3D(view.Position[0], view.Position[1], view.Position[2]);
            vp.Rotation = new Rotation3D(view.Rotation[0], view.Rotation[1], view.Rotation[2], view.Rotation[3]);
            if (Enum.TryParse<ViewpointProjection>(view.Projection, true, out var projection))
            {
                vp.Projection = projection;
            }

            if (view.HeightField > 0)
            {
                vp.HeightField = view.HeightField;
            }

            if (view.AspectRatio > 0)
            {
                vp.AspectRatio = view.AspectRatio;
            }

            if (view.FocalDistance.HasValue && view.FocalDistance.Value > 0)
            {
                vp.FocalDistance = view.FocalDistance.Value;
            }

            if (view.WorldUp != null && view.WorldUp.Length >= 3)
            {
                vp.WorldUpVector = new UnitVector3D(view.WorldUp[0], view.WorldUp[1], view.WorldUp[2]);
            }
        }

        if (view.SectionBox != null && view.SectionBox.Enabled)
        {
#if NAV2024
            if (TryApplySectionBox(vp, view.SectionBox))
            {
                boxesRestored++;
            }
            else
            {
                boxesSkipped++;
            }
#else
            boxesSkipped++;
#endif
        }

        return vp;
    }

#if NAV2024
    private static bool TryApplySectionBox(Viewpoint vp, PortableSectionBox sectionBox)
    {
        if (sectionBox.Min == null || sectionBox.Min.Length < 3 ||
            sectionBox.Max == null || sectionBox.Max.Length < 3)
        {
            return false;
        }

        var clip = vp.InternalClipPlanes;
        if (clip == null)
        {
            return false;
        }

        var box = new BoundingBox3D(
            new Point3D(sectionBox.Min[0], sectionBox.Min[1], sectionBox.Min[2]),
            new Point3D(sectionBox.Max[0], sectionBox.Max[1], sectionBox.Max[2]));
        if (sectionBox.Rotation != null && sectionBox.Rotation.Length >= 4)
        {
            clip.SetOrientedBox(box, new Rotation3D(
                sectionBox.Rotation[0], sectionBox.Rotation[1], sectionBox.Rotation[2], sectionBox.Rotation[3]));
        }
        else
        {
            clip.SetBox(box);
        }

        clip.SetMode(LcOaClipPlaneSetMode.eMODE_BOX);
        clip.SetEnabled(true);
        return true;
    }
#endif

    private static List<string>? SplitFolderName(string? folderName)
    {
        if (string.IsNullOrEmpty(folderName))
        {
            return null;
        }

        var segments = new List<string>();
        foreach (var part in folderName!.Split('/'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                segments.Add(trimmed);
            }
        }

        return segments.Count > 0 ? segments : null;
    }

    private static FolderItem? ResolveTargetFolder(
        Autodesk.Navisworks.Api.DocumentParts.DocumentSavedViewpoints tree, List<string> segments)
    {
        FolderItem? current = null;
        foreach (var segment in segments)
        {
            current = SavedItemTreeNodesShared.FindOrCreateFolder(
                tree.RootItem,
                current,
                segment,
                item => tree.AddCopy(item),
                (parent, item) => tree.AddCopy(parent, item),
                "viewpoint");
        }

        return current;
    }
}
