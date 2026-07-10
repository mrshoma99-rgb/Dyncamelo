using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;
using ComApi = Autodesk.Navisworks.Api.Interop.ComApi;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Model-item provenance and reference-point nodes (wishlist #3, #4):
/// where an element came from, what it is, and its candidate base points.
/// </summary>
[NodeCategory("Navisworks.ModelItem")]
public static class ModelItemInfoNodes
{
    /// <summary>Candidate base/reference points of a model item.</summary>
    /// <param name="item">The model item.</param>
    /// <returns>Bounding-box center, bounding-box minimum corner, and the local-frame origin (world coordinates; null when the item has no geometry or the value is unavailable).</returns>
    [NodeName("ModelItem.ReferencePoints")]
    [NodeDescription("Candidate base/reference points of an item, in world document units. Navisworks has NO insertion-point API — bboxCenter/bboxMin come from the bounding box; localOrigin is the item's local-frame origin (COM fragment transform), which approximates the source insertion point for many formats (null when unavailable, e.g. non-geometry items or outside a live session). Source properties (Revit \"Location\" etc.) remain readable via Properties.Value.")]
    [NodeSearchTags("item", "reference", "base", "point", "insertion", "origin", "location", "center")]
    [MultiReturn("bboxCenter", "bboxMin", "localOrigin")]
    public static Dictionary<string, object?> ReferencePoints(ModelItem item)
    {
        var modelItem = RequireItem(item);

        var box = modelItem.BoundingBox();
        var hasBox = box != null && !box.IsEmpty;

        return new Dictionary<string, object?>
        {
            ["bboxCenter"] = hasBox ? box!.Center : null,
            ["bboxMin"] = hasBox ? box!.Min : null,
            ["localOrigin"] = TryGetLocalOrigin(modelItem),
        };
    }

    /// <summary>Which file a model item came from and what kind of item it is.</summary>
    /// <param name="item">The model item.</param>
    /// <returns>The source file name, the Item-tab type (falls back to the class display name), and the owning model.</returns>
    [NodeName("ModelItem.SourceInfo")]
    [NodeDescription("One-node answer to \"which file did this element come from and what is it\": the source file name, the Item-tab Type (falling back to the item's class name), and the owning appended model.")]
    [NodeSearchTags("item", "source", "file", "origin", "type", "model", "provenance")]
    [MultiReturn("sourceFileName", "itemType", "model")]
    public static Dictionary<string, object?> SourceInfo(ModelItem item)
    {
        var modelItem = RequireItem(item);

        // The owning appended model: walk up to the nearest ancestor that carries one.
        Model? model = null;
        for (var current = modelItem; current != null; current = current.Parent)
        {
            if (current.HasModel)
            {
                model = current.Model;
                break;
            }
        }

        var sourceFileName = ReadItemProperty(modelItem, DataPropertyNames.ItemSourceFileName);
        if (string.IsNullOrEmpty(sourceFileName))
        {
            sourceFileName = model?.SourceFileName ?? model?.FileName ?? string.Empty;
        }

        var itemType = ReadItemProperty(modelItem, DataPropertyNames.ItemType);
        if (string.IsNullOrEmpty(itemType))
        {
            itemType = modelItem.ClassDisplayName ?? string.Empty;
        }

        return new Dictionary<string, object?>
        {
            ["sourceFileName"] = sourceFileName,
            ["itemType"] = itemType,
            ["model"] = model,
        };
    }

    // ------------------------------------------------------------- Helpers

    /// <summary>An Item-tab property as a display string ("" when absent).</summary>
    private static string ReadItemProperty(ModelItem item, string propertyName)
    {
        var property = item.PropertyCategories.FindPropertyByName(PropertyCategoryNames.Item, propertyName);
        if (property == null)
        {
            return string.Empty;
        }

        var value = NavisValues.ToClrObject(property.Value);
        return value as string
            ?? Convert.ToString(value, CultureInfo.InvariantCulture)
            ?? string.Empty;
    }

    /// <summary>
    /// The world-space origin of the item's local frame, read from the first
    /// geometry fragment's local-to-world matrix via the COM bridge. Null when
    /// the item has no fragments or COM is unavailable (e.g. outside a live
    /// Navisworks session) — this output is best-effort by design.
    /// </summary>
    private static Point3D? TryGetLocalOrigin(ModelItem item)
    {
        ComApi.InwOaPath? path = null;
        ComApi.InwNodeFragsColl? fragments = null;
        try
        {
            path = ComBridge.ToPath(item);
            fragments = path.Fragments();
            foreach (var fragmentObject in fragments)
            {
                var fragment = fragmentObject as ComApi.InwOaFragment3;
                if (fragment == null)
                {
                    ComBridge.Release(fragmentObject);
                    continue;
                }

                ComApi.InwLTransform3f? matrix = null;
                try
                {
                    matrix = fragment.GetLocalToWorldMatrix();
                    var values = ToDoubleArray((matrix as ComApi.InwLTransform3f2)?.Matrix);
                    if (values != null && values.Length >= 16)
                    {
                        // COM matrices are 16 doubles with the translation in
                        // elements 13-15 of the 1-based VARIANT array (= 12-14
                        // once copied to a 0-based array).
                        return new Point3D(values[12], values[13], values[14]);
                    }
                }
                finally
                {
                    ComBridge.Release(matrix, fragment);
                }
            }

            return null;
        }
        catch (Exception)
        {
            // Best-effort output: no COM bridge (outside a live session), no
            // fragments, or an interop failure all degrade to null.
            return null;
        }
        finally
        {
            ComBridge.Release(fragments, path);
        }
    }

    /// <summary>A COM VARIANT array (possibly 1-based) as a 0-based double array.</summary>
    private static double[]? ToDoubleArray(object? value)
    {
        var array = value as Array;
        if (array == null || array.Length < 16)
        {
            return null;
        }

        var result = new double[array.Length];
        var index = 0;
        foreach (var element in array)
        {
            result[index++] = Convert.ToDouble(element, CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static ModelItem RequireItem(ModelItem? item)
    {
        return item ?? throw new ArgumentNullException(nameof(item), "No model item provided.");
    }
}
