using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Editing;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Backs the in-node model-element inputs: stores the current Navisworks selection as model-tree paths
/// ("nw:0:1/2;0:4/5"), describes it for the node face, and selects it again on demand.
/// </summary>
public sealed class NavisworksModelPicker : IModelPicker
{
    /// <inheritdoc />
    public string? CaptureSelection(bool single, out int count)
    {
        count = 0;
        try
        {
            var doc = NavisworksContext.ResolveDocument(null);
            var selected = new ModelItemCollection(doc.CurrentSelection.SelectedItems);
            count = selected.Count;
            var paths = ModelItemPaths.ComputePaths(doc, single ? selected.Take(1) : selected);
            return paths.Count == 0 ? null : Encode(paths);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string Describe(string? value)
    {
        var paths = Decode(value);
        if (paths.Count == 0)
        {
            return string.Empty;
        }

        try
        {
            var doc = NavisworksContext.ResolveDocument(null);
            var first = ModelItemPaths.ResolvePath(doc, paths[0]);
            var name = first == null ? "(missing)" : Label(first);
            return paths.Count == 1 ? name : name + " (+" + (paths.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
        catch (Exception)
        {
            return paths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " element(s)";
        }
    }

    /// <inheritdoc />
    public bool Reveal(string? value)
    {
        try
        {
            var doc = NavisworksContext.ResolveDocument(null);
            var items = ModelItemPaths.ResolvePaths(doc, Decode(value));
            if (items.Count == 0)
            {
                return false;
            }

            doc.CurrentSelection.CopyFrom(NavisValues.ToItemCollection(items));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Turns a stored value back into model items (the converters behind the inputs use this). Any other string is
    /// not a stored selection: it is rejected so it is never silently turned into an empty list.
    /// </summary>
    internal static List<ModelItem> Resolve(string? value)
    {
        if (value == null || !value.StartsWith(ModelPickerHost.Prefix, StringComparison.Ordinal))
        {
            throw new InvalidCastException("'" + value + "' is not a picked model selection.");
        }

        var paths = Decode(value);
        if (paths.Count == 0)
        {
            return new List<ModelItem>();
        }

        return ModelItemPaths.ResolvePaths(NavisworksContext.ResolveDocument(null), paths);
    }

    internal static string Encode(IEnumerable<string> paths) =>
        ModelPickerHost.Prefix + string.Join(ModelPickerHost.Separator.ToString(), paths);

    internal static List<string> Decode(string? value)
    {
        if (value == null || !value.StartsWith(ModelPickerHost.Prefix, StringComparison.Ordinal) || value.Length == ModelPickerHost.Prefix.Length)
        {
            return new List<string>();
        }

        return value.Substring(ModelPickerHost.Prefix.Length)
            .Split(ModelPickerHost.Separator)
            .Where(p => p.Length > 0)
            .ToList();
    }

    private static string Label(ModelItem item)
    {
        var name = item.DisplayName;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = item.ClassDisplayName;
        }

        return string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name;
    }
}
