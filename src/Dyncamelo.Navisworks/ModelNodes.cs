using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>Nodes for working with loaded models.</summary>
[NodeCategory("Navisworks.Model")]
public static class ModelNodes
{
    /// <summary>The root model item of a model.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The model's root item.</returns>
    [NodeName("Model.RootItem")]
    [NodeDescription("The root model item of a model.")]
    [NodeSearchTags("model", "root", "item", "tree")]
    [return: NodeName("rootItem")]
    public static ModelItem RootItem(Model model)
    {
        return RequireModel(model).RootItem;
    }

    /// <summary>The file paths of a model.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The cached (loaded) file path and the original source file path.</returns>
    [NodeName("Model.FileName")]
    [NodeDescription("The cached and original source file paths of a model (federated-file inventory).")]
    [NodeSearchTags("model", "filename", "source", "path", "file")]
    [MultiReturn("fileName", "sourceFileName")]
    public static Dictionary<string, object?> FileName(Model model)
    {
        var resolved = RequireModel(model);
        return new Dictionary<string, object?>
        {
            ["fileName"] = resolved.FileName,
            ["sourceFileName"] = resolved.SourceFileName,
        };
    }

    /// <summary>The native units of a model's source file.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The unit name, e.g. "Meters" or "Millimeters".</returns>
    [NodeName("Model.Units")]
    [NodeDescription("The native units of a model's source file (unit-mismatch audits across appended files).")]
    [NodeSearchTags("model", "units", "native", "meters", "feet")]
    [return: NodeName("units")]
    public static string Units(Model model)
    {
        return RequireModel(model).Units.ToString();
    }

    /// <summary>The root items of every model in a document.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>One root item per loaded model.</returns>
    [NodeName("Models.RootItems")]
    [NodeDescription("The root model items of every model loaded in a document.")]
    [NodeSearchTags("models", "roots", "items", "tree")]
    [return: NodeName("rootItems")]
    public static List<ModelItem> RootItems(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        return NavisValues.ToItemList(doc.Models.RootItems);
    }

    private static Model RequireModel(Model? model)
    {
        return model ?? throw new ArgumentNullException(nameof(model), "No model provided.");
    }
}
