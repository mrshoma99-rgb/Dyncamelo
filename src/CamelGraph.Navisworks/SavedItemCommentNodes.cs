using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks;

/// <summary>
/// Comment nodes for saved items — the Review-tab Comments feature, scriptable
/// (wishlist #10, viewpoints/sets half). Writes dispatch to the owning document
/// part (<c>DocumentSavedViewpoints</c> or <c>DocumentSelectionSets</c>) —
/// clash tests/results have their own comment nodes in Navisworks.Clash. Reads
/// work on any saved item (viewpoint, set, folder, clash test).
/// </summary>
[NodeCategory("Navisworks.Comments")]
public static class SavedItemCommentNodes
{
    /// <summary>Adds a comment to a saved viewpoint, set, folder, clash result or result group.</summary>
    /// <param name="item">The saved item (a viewpoint, selection/search set, one of their folders, or a clash result or group).</param>
    /// <param name="body">The comment text.</param>
    /// <param name="status">"New", "Active", "Approved" or "Resolved".</param>
    /// <param name="author">Comment author ("" uses the Navisworks user name).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored item the comment was added to (pass-through for chaining).</returns>
    [NodeName("SavedItem.AddComment")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Adds a comment to a saved viewpoint, selection/search set, folder, clash result or result group — the Review-tab Comments feature, scriptable; lace over results for review notes in bulk. Every run adds another comment: running the graph twice leaves two identical comments (SavedItem.ClearComments empties the thread first, SavedItem.SetCommentStatus changes the status of one that is already there).")]
    [NodeSearchTags("comment", "add", "review", "note", "viewpoint", "set", "annotate")]
    [return: NodeName("item")]
    public static SavedItem AddComment(
        SavedItem item,
        string body,
        [NodeChoices("New", "Active", "Approved", "Resolved")]
        string status = "New",
        string author = "",
        Document? document = null)
    {
        // A clash result or result group lives in Clash Detective's own tree, with its own write path.
        if (item is Autodesk.Navisworks.Api.Clash.IClashResult)
        {
            return ClashEditNodes.AddClashComment(item, body, status, author, document);
        }

        if (string.IsNullOrEmpty(body))
        {
            throw new ArgumentException("No comment body provided.", nameof(body));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var commentStatus = NavisValues.ParseCommentStatus(status);
        var stored = ResolveOwningPart(doc, item, out var inViewpointsTree);

        var comment = string.IsNullOrEmpty(author)
            ? doc.CreateCommentWithUniqueId(body, commentStatus)
            : doc.CreateCommentWithUniqueId(body, commentStatus, author);

        if (inViewpointsTree)
        {
            doc.SavedViewpoints.AddComment(stored, comment);
        }
        else
        {
            doc.SelectionSets.AddComment(stored, comment);
        }

        return stored;
    }

    /// <summary>Reads the comment thread on any saved item.</summary>
    /// <param name="item">The saved item (viewpoint, set, folder or clash test).</param>
    /// <returns>Index-aligned comment bodies, authors, statuses and creation dates.</returns>
    [NodeName("SavedItem.Comments")]
    [NodeDescription("The comment thread on any saved item (viewpoint, set, folder, clash test): bodies, authors, statuses and creation dates, index-aligned.")]
    [NodeSearchTags("comment", "comments", "read", "review", "thread", "notes")]
    [MultiReturn("bodies", "authors", "statuses", "dates")]
    [PortKinds("text*", "text*", "text*", "datetime*")]
    public static Dictionary<string, object?> Comments(SavedItem item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item), "No saved item provided.");
        }

        var bodies = new List<string>();
        var authors = new List<string>();
        var statuses = new List<string>();
        var dates = new List<DateTime>();
        foreach (var comment in item.Comments)
        {
            bodies.Add(comment.Body ?? string.Empty);
            authors.Add(comment.Author ?? string.Empty);
            statuses.Add(comment.Status.ToString());
            dates.Add(comment.CreationDate);
        }

        return new Dictionary<string, object?>
        {
            ["bodies"] = bodies,
            ["authors"] = authors,
            ["statuses"] = statuses,
            ["dates"] = dates,
        };
    }

    /// <summary>Deletes every comment on a saved viewpoint, set, folder, clash result or result group.</summary>
    /// <param name="item">The saved item (a viewpoint, selection/search set, one of their folders, or a clash result or group).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored item (pass-through for chaining).</returns>
    [NodeName("SavedItem.ClearComments")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Deletes every comment on a saved viewpoint, selection/search set, folder, clash result or result group (replace-all with an empty thread). Rebuild the thread afterwards with SavedItem.AddComment. A clash test itself has no comment thread here.")]
    [NodeSearchTags("comment", "clear", "delete", "remove", "review", "reset")]
    [return: NodeName("item")]
    public static SavedItem ClearComments(SavedItem item, Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);

        // A clash result or result group lives in Clash Detective's own tree, with its own write path (the one AddComment uses).
        if (item is Autodesk.Navisworks.Api.Clash.IClashResult clashResult)
        {
            ClashHelpers.RequireClash(doc).TestsData.TestsEditResultComments(clashResult, new CommentCollection());
            return item;
        }

        var stored = ResolveOwningPart(doc, item, out var inViewpointsTree);

        var empty = new CommentCollection();
        if (inViewpointsTree)
        {
            doc.SavedViewpoints.EditComments(stored, empty);
        }
        else
        {
            doc.SelectionSets.EditComments(stored, empty);
        }

        return stored;
    }

    /// <summary>Changes the status of one comment, or of all comments, on a saved item.</summary>
    /// <param name="item">The saved item (a viewpoint, selection/search set, one of their folders, or a clash result or group). Wire a list to change several items.</param>
    /// <param name="status">"New", "Active", "Approved" or "Resolved".</param>
    /// <param name="index">The 0-based position of the comment in the thread (0 is the first); -1 changes every comment.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The item (pass-through for chaining).</returns>
    [NodeName("SavedItem.SetCommentStatus")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Changes the status (New, Active, Approved, Resolved) of one comment, or of every comment, on a saved viewpoint, selection/search set, folder, clash result or result group, keeping its text, its author and its place in the thread — close out review comments without clearing the thread. Navisworks cannot edit a comment in place, so a changed comment is replaced by a copy: its date becomes the time of the change. Comments that already have the status are left alone. Wire a list of items to change several threads.")]
    [NodeSearchTags("comment", "status", "resolve", "resolved", "approve", "approved", "close", "review", "active", "new", "set")]
    [return: NodeName("item")]
    public static SavedItem SetCommentStatus(
        SavedItem item,
        [NodeChoices("New", "Active", "Approved", "Resolved")]
        string status,
        int index = -1,
        Document? document = null)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item), "No saved item provided.");
        }

        if (string.IsNullOrWhiteSpace(status))
        {
            throw new ArgumentException("No status provided. Use \"New\", \"Active\", \"Approved\" or \"Resolved\".", nameof(status));
        }

        var wanted = NavisValues.ParseCommentStatus(status);
        var doc = NavisworksContext.ResolveDocument(document);

        if (item is Autodesk.Navisworks.Api.Clash.IClashResult clashResult)
        {
            var clashEdited = Retag(doc, item.Comments, wanted, index, out var clashChanged);
            if (clashChanged > 0)
            {
                ClashHelpers.RequireClash(doc).TestsData.TestsEditResultComments(clashResult, clashEdited);
            }

            return item;
        }

        var stored = ResolveOwningPart(doc, item, out var inViewpointsTree);
        var edited = Retag(doc, stored.Comments, wanted, index, out var changed);
        if (changed == 0)
        {
            return stored;
        }

        if (inViewpointsTree)
        {
            doc.SavedViewpoints.EditComments(stored, edited);
        }
        else
        {
            doc.SelectionSets.EditComments(stored, edited);
        }

        return stored;
    }

    /// <summary>
    /// A copy of the thread in which the chosen comments have the wanted status. A stored comment cannot be changed, so each
    /// one that differs is replaced by a new comment with the same text and author (and a new date and id).
    /// </summary>
    private static CommentCollection Retag(Document doc, CommentCollection thread, CommentStatus wanted, int index, out int changed)
    {
        var edited = new CommentCollection(thread);
        changed = 0;
        if (edited.Count == 0)
        {
            NodeWarnings.Add("The item has no comments, so there is no status to change.");
            return edited;
        }

        foreach (var position in CommentSelection.Positions(edited.Count, index))
        {
            var old = edited[position];
            if (old.Status == wanted)
            {
                continue;
            }

            var body = old.Body ?? string.Empty;
            edited[position] = string.IsNullOrEmpty(old.Author)
                ? doc.CreateCommentWithUniqueId(body, wanted)
                : doc.CreateCommentWithUniqueId(body, wanted, old.Author);
            changed++;
        }

        return edited;
    }

    /// <summary>
    /// Locates the STORED instance of the item and which document part owns it.
    /// Typed items (viewpoints, sets) resolve against their own tree; folders
    /// and other kinds are located by identity in either tree.
    /// </summary>
    private static SavedItem ResolveOwningPart(Document doc, SavedItem item, out bool inViewpointsTree)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item), "No saved item provided.");
        }

        var viewpointsRoot = doc.SavedViewpoints.RootItem;
        var setsRoot = doc.SelectionSets.RootItem;

        if (item is SavedViewpoint viewpoint)
        {
            inViewpointsTree = true;
            return SavedItemTreeHelpers.FindStoredEquivalent(viewpointsRoot, viewpoint)
                ?? throw new InvalidOperationException(
                    "The saved viewpoint '" + item.DisplayName + "' is not stored in this document.");
        }

        if (item is SelectionSet set)
        {
            inViewpointsTree = false;
            return SavedItemTreeHelpers.FindStoredEquivalent(setsRoot, set)
                ?? throw new InvalidOperationException(
                    "The selection set '" + item.DisplayName + "' is not stored in this document.");
        }

        // Folders (and any other saved-item kind) can live in either tree —
        // dispatch by identity (reference/Guid), never by name, so a same-named
        // folder in the other tree can never receive the comment by mistake.
        if (SavedItemTreeHelpers.TreeContains(viewpointsRoot, item))
        {
            inViewpointsTree = true;
            return SavedItemTreeHelpers.FindStoredEquivalent(viewpointsRoot, item)!;
        }

        if (SavedItemTreeHelpers.TreeContains(setsRoot, item))
        {
            inViewpointsTree = false;
            return SavedItemTreeHelpers.FindStoredEquivalent(setsRoot, item)!;
        }

        throw new InvalidOperationException(
            "The item '" + item.DisplayName + "' is not stored in this document's saved viewpoints or selection sets, " +
            "and it is not a clash result or result group, so these nodes cannot reach its comments.");
    }

}
