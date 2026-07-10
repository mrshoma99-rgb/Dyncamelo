using System;
using System.Collections.Generic;
using System.IO;
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

    /// <summary>Saves the document to a .nwf or .nwd file.</summary>
    /// <param name="filePath">Destination path ending in .nwf or .nwd; the directory is created when missing.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written file path.</returns>
    [NodeName("Document.Save")]
    [NodeDescription("Saves the document as .nwf (references) or .nwd (published snapshot) to the given path.")]
    [NodeSearchTags("document", "save", "nwf", "nwd", "publish", "write")]
    [return: NodeName("filePath")]
    public static string Save(string filePath, Document? document = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        var extension = Path.GetExtension(filePath);
        if (!string.Equals(extension, ".nwf", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".nwd", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "'" + filePath + "' must end in .nwf or .nwd (Navisworks decides the format by extension).",
                nameof(filePath));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        doc.SaveFile(filePath);
        return filePath;
    }
}
