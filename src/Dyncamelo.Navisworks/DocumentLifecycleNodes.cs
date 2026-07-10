using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Document lifecycle nodes (wishlist #4/#5/#6): open, append, refresh and
/// merge files — the headless Batch-Utility building blocks
/// (Directory.GetFiles → Document.AppendFiles → Export.NWD).
///
/// CRITICAL: OpenFile/AppendFile/UpdateFiles/MergeFile invalidate every cached
/// ModelItem handle from earlier runs — the node host flushes output caches on
/// the corresponding document events (plan item I3); graphs should re-query
/// items downstream of these nodes rather than reusing stale wires.
/// </summary>
[NodeCategory("Navisworks.Document")]
public static class DocumentLifecycleNodes
{
    /// <summary>Opens a file into the active document.</summary>
    /// <param name="filePath">The file to open (.nwd/.nwf/.nwc or any appendable design format).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The document, now holding the opened file.</returns>
    [NodeName("Document.Open")]
    [NodeDescription("Opens a file into the document, REPLACING its current contents (the headless batch driver). Every model item from before the open is invalidated — re-query items downstream of this node.")]
    [NodeSearchTags("document", "open", "file", "load", "batch", "nwd", "nwf")]
    [return: NodeName("document")]
    public static Document Open(string filePath, Document? document = null)
    {
        RequireExistingFile(filePath);
        var doc = NavisworksContext.ResolveDocument(document);
        if (!doc.TryOpenFile(filePath))
        {
            throw new InvalidOperationException(
                "Navisworks could not open '" + filePath +
                "' — the format may be unsupported or the file corrupt/locked.");
        }

        return doc;
    }

    /// <summary>Appends design files to the document.</summary>
    /// <param name="filePaths">The files to append, in order (e.g. from Directory.GetFiles).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The document and the newly appended models.</returns>
    [NodeName("Document.AppendFiles")]
    [NodeDescription("Appends design files to the document — Directory.GetFiles → Document.AppendFiles → Export.NWD is the Navisworks Batch Utility in three nodes. Cached model items from earlier runs are invalidated.")]
    [NodeSearchTags("document", "append", "files", "add", "batch", "federate", "combine")]
    [MultiReturn("document", "models")]
    public static Dictionary<string, object?> AppendFiles(IEnumerable<string> filePaths, Document? document = null)
    {
        var paths = MaterializePaths(filePaths);
        var doc = NavisworksContext.ResolveDocument(document);

        var countBefore = doc.Models.Count;
        var appended = 0;
        foreach (var path in paths)
        {
            if (!doc.TryAppendFile(path))
            {
                throw new InvalidOperationException(
                    "Navisworks could not append '" + path + "' (file " + (appended + 1).ToString() +
                    " of " + paths.Count.ToString() + "; the " + appended.ToString() +
                    " file(s) before it were appended) — the format may be unsupported or the file corrupt/locked.");
            }

            appended++;
        }

        var models = new List<Model>();
        for (int i = countBefore; i < doc.Models.Count; i++)
        {
            models.Add(doc.Models[i]);
        }

        return new Dictionary<string, object?>
        {
            ["document"] = doc,
            ["models"] = models,
        };
    }

    /// <summary>Refreshes all linked/appended files from disk (Home &gt; Refresh).</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when any file was updated from disk.</returns>
    [NodeName("Document.Refresh")]
    [NodeDescription("Refreshes every linked/appended file from disk — Home > Refresh, scriptable. Returns true when anything was updated. Cached model items from earlier runs are invalidated.")]
    [NodeSearchTags("document", "refresh", "update", "reload", "linked", "files")]
    [return: NodeName("updated")]
    public static bool Refresh(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        return doc.UpdateFiles();
    }

    /// <summary>Merges another Navisworks file into the document, resolving duplicates.</summary>
    /// <param name="filePath">The .nwf/.nwd to merge (e.g. a colleague's review file).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The document, with the file's contents merged in.</returns>
    [NodeName("Document.Merge")]
    [NodeDescription("Merges another Navisworks file into the document with duplicate resolution — pulls a colleague's review artifacts (sets, viewpoints, comments) into yours. Not a model-diff tool. Cached model items from earlier runs are invalidated.")]
    [NodeSearchTags("document", "merge", "combine", "review", "duplicate", "nwf")]
    [return: NodeName("document")]
    public static Document Merge(string filePath, Document? document = null)
    {
        RequireExistingFile(filePath);
        var doc = NavisworksContext.ResolveDocument(document);
        if (!doc.TryMergeFile(filePath))
        {
            throw new InvalidOperationException(
                "Navisworks could not merge '" + filePath +
                "' — the format may be unsupported or the file corrupt/locked.");
        }

        return doc;
    }

    // ------------------------------------------------------------- Helpers

    private static void RequireExistingFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The file '" + filePath + "' does not exist.", filePath);
        }
    }

    private static List<string> MaterializePaths(IEnumerable<string>? filePaths)
    {
        if (filePaths == null)
        {
            throw new ArgumentNullException(nameof(filePaths), "No file paths provided.");
        }

        var paths = new List<string>();
        foreach (var path in filePaths)
        {
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path);
            }
        }

        if (paths.Count == 0)
        {
            throw new ArgumentException("No file paths provided.", nameof(filePaths));
        }

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The file '" + path + "' does not exist.", path);
            }
        }

        return paths;
    }
}
