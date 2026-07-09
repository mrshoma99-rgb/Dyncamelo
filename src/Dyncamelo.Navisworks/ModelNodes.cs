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
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model), "No model provided.");
        }

        return model.RootItem;
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
}
