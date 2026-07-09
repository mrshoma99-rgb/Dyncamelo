using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Navisworks;

/// <summary>Nodes for accessing the active Navisworks document.</summary>
[NodeCategory("Navisworks.Document")]
public static class DocumentNodes
{
    /// <summary>Gets the active Navisworks document.</summary>
    /// <returns>The active document.</returns>
    [NodeName("Document.Current")]
    [NodeDescription("The active Navisworks document.")]
    [NodeSearchTags("document", "active", "current", "navisworks")]
    [return: NodeName("document")]
    public static Document Current()
    {
        return NavisworksContext.ResolveDocument(null);
    }

    /// <summary>Basic information about a document.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>File name, title, units and model count.</returns>
    [NodeName("Document.Info")]
    [NodeDescription("File name, title, display units and model count of a document.")]
    [NodeSearchTags("document", "info", "filename", "title", "units")]
    [MultiReturn("fileName", "title", "units", "modelCount")]
    public static Dictionary<string, object?> Info(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        return new Dictionary<string, object?>
        {
            ["fileName"] = doc.FileName,
            ["title"] = doc.Title,
            ["units"] = doc.Units.ToString(),
            ["modelCount"] = doc.Models.Count,
        };
    }

    /// <summary>The models (appended source files) loaded in a document.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>All loaded models.</returns>
    [NodeName("Document.Models")]
    [NodeDescription("The models (appended source files) loaded in a document.")]
    [NodeSearchTags("document", "models", "files", "appended")]
    [return: NodeName("models")]
    public static List<Model> Models(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var models = new List<Model>();
        foreach (var model in doc.Models)
        {
            models.Add(model);
        }

        return models;
    }
}
