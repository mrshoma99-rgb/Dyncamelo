using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

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
    /// <summary>Adds a comment to a saved viewpoint, set or folder.</summary>
    /// <param name="item">The saved item (a viewpoint, selection/search set, or one of their folders).</param>
    /// <param name="body">The comment text.</param>
    /// <param name="status">"New", "Active", "Approved" or "Resolved".</param>
    /// <param name="author">Comment author ("" uses the Navisworks user name).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored item the comment was added to (pass-through for chaining).</returns>
    [NodeName("SavedItem.AddComment")]
    [NodeDescription("Adds a comment to a saved viewpoint, selection/search set or folder — the Review-tab Comments feature, scriptable. For clash results use ClashResult.AddComment.")]
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
        if (string.IsNullOrEmpty(body))
        {
            throw new ArgumentException("No comment body provided.", nameof(body));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var commentStatus = ParseStatus(status);
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

    /// <summary>Deletes every comment on a saved viewpoint, set or folder.</summary>
    /// <param name="item">The saved item (a viewpoint, selection/search set, or one of their folders).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored item (pass-through for chaining).</returns>
    [NodeName("SavedItem.ClearComments")]
    [NodeDescription("Deletes every comment on a saved viewpoint, selection/search set or folder (replace-all with an empty thread). Rebuild the thread afterwards with SavedItem.AddComment.")]
    [NodeSearchTags("comment", "clear", "delete", "remove", "review", "reset")]
    [return: NodeName("item")]
    public static SavedItem ClearComments(SavedItem item, Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
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
            "The item '" + item.DisplayName + "' is not stored in this document's saved viewpoints or selection sets. " +
            "Comments on clash tests/results use the ClashResult comment nodes instead.");
    }

    /// <summary>Parses a status port value ("" and null mean New).</summary>
    private static CommentStatus ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return CommentStatus.New;
        }

        switch (status!.Trim().ToLowerInvariant())
        {
            case "new": return CommentStatus.New;
            case "active": return CommentStatus.Active;
            case "approved": return CommentStatus.Approved;
            case "resolved": return CommentStatus.Resolved;
            default:
                throw new ArgumentException(
                    "Unknown comment status '" + status + "'. Use \"New\", \"Active\", \"Approved\" or \"Resolved\".",
                    nameof(status));
        }
    }
}
