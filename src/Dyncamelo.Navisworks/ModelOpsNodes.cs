using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Model-level operations on the appended source files of a document
/// (wishlist #5). Removal works on WHOLE appended files only — no Navisworks
/// API deletes arbitrary sub-items; hide the items (Appearance.Hide) and
/// publish an NWD for anything finer.
/// </summary>
[NodeCategory("Navisworks.Model")]
public static class ModelOpsNodes
{
    /// <summary>Removes a whole appended source model from the document.</summary>
    /// <param name="model">The model to remove: a Model (from Document.Models), its 0-based index, or its file name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when the model was removed; false when Navisworks refused (e.g. the last remaining model).</returns>
    [NodeName("Model.Remove")]
    [NodeDescription("Removes a WHOLE appended source model from the document (accepts a Model, a 0-based index, or a file name). No API deletes individual elements — hide them and publish an NWD instead. Returns false when Navisworks refuses the removal. Cached model items from earlier runs are invalidated.")]
    [NodeSearchTags("model", "remove", "delete", "file", "appended", "detach", "unload")]
    [return: NodeName("removed")]
    public static bool Remove(object model, Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var index = ResolveModelIndex(doc, model);
        return doc.TryRemoveFile(index);
    }

    // ------------------------------------------------------------- Helpers

    private static int ResolveModelIndex(Document doc, object? model)
    {
        var count = doc.Models.Count;
        if (count == 0)
        {
            throw new InvalidOperationException("The document has no models to remove.");
        }

        switch (model)
        {
            case null:
                throw new ArgumentNullException(
                    nameof(model), "No model provided. Wire a Model, a 0-based index, or a file name.");

            case Model resolved:
                for (int i = 0; i < count; i++)
                {
                    if (ReferenceEquals(doc.Models[i], resolved) || doc.Models[i].Equals(resolved))
                    {
                        return i;
                    }
                }

                throw new ArgumentException(
                    "The model '" + (resolved.FileName ?? string.Empty) +
                    "' is not loaded in this document (it may already have been removed).", nameof(model));

            case int i32:
                return RequireIndexInRange(i32, count);
            case long i64:
                return RequireIndexInRange(checked((int)i64), count);
            case double dbl:
                if (Math.Abs(dbl - Math.Round(dbl)) > 1e-9)
                {
                    throw new ArgumentException(
                        "A model index must be a whole number; got " +
                        dbl.ToString(CultureInfo.InvariantCulture) + ".", nameof(model));
                }

                return RequireIndexInRange(checked((int)Math.Round(dbl)), count);

            case string fileName:
                return FindModelIndexByFileName(doc, fileName);

            default:
                throw new ArgumentException(
                    "Cannot interpret a value of type '" + model.GetType().Name +
                    "' as a model. Wire a Model (from Document.Models), a 0-based index, or a file name.",
                    nameof(model));
        }
    }

    private static int RequireIndexInRange(int index, int count)
    {
        if (index < 0 || index >= count)
        {
            throw new ArgumentOutOfRangeException(
                "model",
                "Model index " + index.ToString(CultureInfo.InvariantCulture) + " is out of range — the document has " +
                count.ToString(CultureInfo.InvariantCulture) + " model(s) (indices 0-" +
                (count - 1).ToString(CultureInfo.InvariantCulture) + ").");
        }

        return index;
    }

    private static int FindModelIndexByFileName(Document doc, string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentException("No model file name provided.", "model");
        }

        var available = new List<string>();
        for (int i = 0; i < doc.Models.Count; i++)
        {
            var candidate = doc.Models[i];
            var candidateFile = candidate.FileName ?? string.Empty;
            var candidateSource = candidate.SourceFileName ?? string.Empty;
            available.Add(Path.GetFileName(candidateFile));

            if (MatchesFileName(candidateFile, fileName) || MatchesFileName(candidateSource, fileName))
            {
                return i;
            }
        }

        throw new ArgumentException(
            "No loaded model matches the file name '" + fileName + "'. Loaded models: " +
            string.Join(", ", available) + ".", "model");
    }

    private static bool MatchesFileName(string candidatePath, string requested)
    {
        if (string.IsNullOrEmpty(candidatePath))
        {
            return false;
        }

        return string.Equals(candidatePath, requested, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(candidatePath), requested, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                Path.GetFileNameWithoutExtension(candidatePath), requested, StringComparison.OrdinalIgnoreCase);
    }
}
