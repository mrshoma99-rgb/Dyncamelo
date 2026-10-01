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
    /// <summary>Everything a model is made of: where it came from, its units and its root item.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The cached and original file paths, the native units and the root item.</returns>
    [NodeName("Model.Info")]
    [NodeDescription("The cached and original source file paths of a model, its native units (unit-mismatch audits across appended files) and its root model item — one node for the federated-file inventory.")]
    [NodeSearchTags("model", "info", "filename", "source", "path", "file", "units", "native", "root", "item", "tree")]
    [MultiReturn("fileName", "sourceFileName", "units", "rootItem")]
    [PortKinds("file", "file", "text", "item")]
    public static Dictionary<string, object?> Info(Model model)
    {
        var resolved = RequireModel(model);
        return new Dictionary<string, object?>
        {
            ["fileName"] = resolved.FileName,
            ["sourceFileName"] = resolved.SourceFileName,
            ["units"] = resolved.Units.ToString(),
            ["rootItem"] = resolved.RootItem,
        };
    }

    /// <summary>The root model item of a model.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The model's root item.</returns>
    [NodeName("Model.RootItem")]
    [NodeDeprecated("Model.Info")]
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
    [NodeDeprecated("Model.Info")]
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
    [NodeDeprecated("Model.Info")]
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
