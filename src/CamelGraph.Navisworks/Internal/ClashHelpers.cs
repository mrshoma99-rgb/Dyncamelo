using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using CamelGraph.Core.Execution;
using CamelGraph.Nodes.Coordination;

namespace CamelGraph.Navisworks.Internal;

/// <summary>
/// Shared plumbing for the Clash Detective nodes. Internal — never surfaced as
/// nodes.
/// </summary>
internal static class ClashHelpers
{
    /// <summary>The Clash Detective document part, or a clear error when the edition lacks it.</summary>
    internal static DocumentClash RequireClash(Document doc)
    {
        return doc.GetClash()
            ?? throw new InvalidOperationException("Clash Detective is not available in this Navisworks edition.");
    }

    /// <summary>Null-checks a clash test input.</summary>
    internal static ClashTest RequireTest(ClashTest? test)
    {
        return test ?? throw new ArgumentNullException(nameof(test), "No clash test provided.");
    }

    /// <summary>Null-checks a clash result input.</summary>
    internal static ClashResult RequireResult(ClashResult? result)
    {
        return result ?? throw new ArgumentNullException(nameof(result), "No clash result provided.");
    }

    /// <summary>
    /// Ensures a clash test is the STORED instance from the document (run/edit
    /// APIs reject detached copies). Stored saved items are read-only in place.
    /// </summary>
    internal static ClashTest RequireStoredTest(ClashTest? test)
    {
        var clashTest = RequireTest(test);
        if (!clashTest.IsReadOnly)
        {
            throw new ArgumentException(
                "The clash test '" + clashTest.DisplayName + "' is a detached copy. Wire a stored test " +
                "from Clash.Tests, ClashTest.ByName or ClashTest.Create.", nameof(test));
        }

        return clashTest;
    }

    /// <summary>Resolves a "test or name" input to the STORED clash test.</summary>
    internal static ClashTest ResolveStoredTest(DocumentClash clash, object? test)
    {
        switch (test)
        {
            case null:
                throw new ArgumentNullException(nameof(test), "No clash test provided.");
            case string name:
                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentException("No clash test name provided.", nameof(test));
                }

                return NavisValues.FindSavedItemByName<ClashTest>(clash.TestsData.Tests, name)
                    ?? throw new InvalidOperationException(
                        "No clash test named '" + name + "' exists in the document.");
            case ClashTest clashTest:
                if (clashTest.IsReadOnly)
                {
                    return clashTest; // already the stored instance
                }

                // A detached copy — re-locate the stored original by Guid/name.
                var byGuid = clashTest.Guid != Guid.Empty
                    ? FindTestByGuid(clash.TestsData.Tests, clashTest.Guid)
                    : null;
                return byGuid
                    ?? NavisValues.FindSavedItemByName<ClashTest>(clash.TestsData.Tests, clashTest.DisplayName)
                    ?? throw new InvalidOperationException(
                        "The clash test '" + clashTest.DisplayName + "' is not stored in this document. " +
                        "Wire a test from Clash.Tests, ClashTest.ByName or ClashTest.Create.");
            default:
                throw new ArgumentException(
                    "Cannot interpret a value of type '" + test.GetType().Name +
                    "' as a clash test. Wire the test itself or its display name.", nameof(test));
        }
    }

    /// <summary>
    /// Commits a rebuilt clash-test tree (results and result groups) back into
    /// the document.
    ///
    /// The commit MUST go through <c>TestsReplaceWithCopy</c>:
    /// <c>TestsEditTestFromCopy</c> applies only the test's own settings
    /// (selections, tolerance, type) and silently ignores the children, so
    /// regrouping through it reports success while the Clash Detective tree
    /// stays untouched. Wrapped in a document transaction so a regroup is one
    /// undo step, matching Navisworks' own grouping commands. A test inside a Clash Detective folder is replaced in its
    /// folder (the parent overload of <c>TestsReplaceWithCopy</c>), not looked for at the top level only.
    /// </summary>
    /// <param name="doc">The document owning the clash data.</param>
    /// <param name="clash">The Clash Detective document part.</param>
    /// <param name="stored">The stored test being replaced.</param>
    /// <param name="editedCopy">The detached copy carrying the desired tree.</param>
    /// <param name="undoLabel">Label for the undo entry.</param>
    /// <returns>The stored test after the commit (re-located; the old wrapper is disposed).</returns>
    internal static ClashTest CommitTestTree(
        Document doc, DocumentClash clash, ClashTest stored, ClashTest editedCopy, string undoLabel)
    {
        // The test may sit in a Clash Detective folder: replace it in the folder that holds it.
        if (!TryLocateTest(clash, stored, out var parent, out var index))
        {
            throw new InvalidOperationException(
                "The clash test '" + stored.DisplayName + "' is no longer in the document (not at the top level and not " +
                "inside a folder) — it may have been deleted or renamed while the graph ran.");
        }

        var guid = stored.Guid;
        var name = stored.DisplayName;
        using (var transaction = doc.BeginTransaction(undoLabel))
        {
            if (parent == null)
            {
                clash.TestsData.TestsReplaceWithCopy(index, editedCopy);
            }
            else
            {
                clash.TestsData.TestsReplaceWithCopy(parent, index, editedCopy);
            }

            transaction.Commit();
        }

        // ReplaceWithCopy disposes the previous instance — hand back the new one.
        return FindStoredTest(clash, guid, name)
            ?? throw new InvalidOperationException(
                "The clash test '" + name + "' could not be found after the edit was committed.");
    }

    /// <summary>
    /// Whether an exception is Navisworks' disposed-native-handle failure —
    /// what a wrapper cached from before a tree-replacing edit throws.
    /// </summary>
    internal static bool IsDisposed(Exception ex)
    {
        return ex is ObjectDisposedException ||
               (ex.Message != null && ex.Message.IndexOf("Disposed", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>The standard "your wired clash items are stale" error.</summary>
    internal static InvalidOperationException StaleInputError(string what, Exception inner)
    {
        return new InvalidOperationException(
            "The wired " + what + " are stale — Navisworks disposed them when the clash tree was last " +
            "edited (a previous run of this graph, or an earlier grouping node in it). Re-fetch them " +
            "between edits: wire ClashTest.ByName (by name, not a cached test) into this node, and use " +
            "Flow.Then to order it after any earlier grouping edit.", inner);
    }

    /// <summary>
    /// Where a stored test sits in the tests tree: the folder that holds it (null for a top-level test) and its position
    /// there. A test is recognised by reference, then by Guid, then (when it has none) by display name, in folders as well
    /// as at the top level.
    /// </summary>
    /// <param name="clash">The Clash Detective document part.</param>
    /// <param name="test">The stored test to find.</param>
    /// <param name="parent">The folder holding the test, or null at the top level.</param>
    /// <param name="index">The test's position in its folder, or -1 when absent.</param>
    /// <returns>True when the test is in the document.</returns>
    internal static bool TryLocateTest(DocumentClash clash, ClashTest test, out GroupItem? parent, out int index)
    {
        var guid = test.Guid;
        var name = test.DisplayName;
        var found = SavedTreeLocator.TryFind<SavedItem>(
            clash.TestsData.Tests,
            item => item is ClashTest candidate &&
                    (ReferenceEquals(candidate, test) ||
                     (guid != Guid.Empty && candidate.Guid == guid) ||
                     (guid == Guid.Empty && string.Equals(candidate.DisplayName, name, StringComparison.Ordinal))),
            item => item is GroupItem folder && !(item is ClashTest) ? folder.Children : null,
            out var holder,
            out index);
        parent = holder as GroupItem;
        return found;
    }

    /// <summary>
    /// The first test with the given name at the top level or inside folders (only folders are descended into, never the
    /// results of a test). Null when there is none.
    /// </summary>
    internal static ClashTest? FindTestByName(IEnumerable<SavedItem> items, string name)
    {
        foreach (var item in items)
        {
            if (item is ClashTest test)
            {
                if (string.Equals(test.DisplayName, name, StringComparison.Ordinal))
                {
                    return test;
                }
            }
            else if (item is FolderItem folder)
            {
                var inner = FindTestByName(folder.Children, name);
                if (inner != null)
                {
                    return inner;
                }
            }
        }

        return null;
    }

    /// <summary>The first Clash Detective folder with the given name, at any depth. Null when there is none.</summary>
    internal static FolderItem? FindFolder(IEnumerable<SavedItem> items, string name)
    {
        foreach (var item in items)
        {
            if (item is FolderItem folder)
            {
                if (string.Equals(folder.DisplayName, name, StringComparison.Ordinal))
                {
                    return folder;
                }

                var inner = FindFolder(folder.Children, name);
                if (inner != null)
                {
                    return inner;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Applies a type, tolerance and the two selections to a stored test IN PLACE (its results, statuses, comments and groups are
    /// kept), the way ClashTest.Edit does: edit a detached copy, then write its settings back with <c>TestsEditTestFromCopy</c>.
    /// </summary>
    /// <returns>The stored test after the edit (re-located; the old wrapper may be stale).</returns>
    internal static ClashTest UpdateTestSettings(
        Document doc,
        DocumentClash clash,
        ClashTest stored,
        ClashTestType testType,
        double tolerance,
        IEnumerable<ModelItem> itemsA,
        IEnumerable<ModelItem> itemsB)
    {
        var guid = stored.Guid;
        var name = stored.DisplayName;
        using (var transaction = doc.BeginTransaction("Update clash test"))
        {
            var copy = (ClashTest)stored.CreateCopy();
            copy.TestType = testType;
            copy.Tolerance = tolerance;
            copy.SelectionA.Selection.CopyFrom(NavisValues.ToItemCollection(itemsA));
            copy.SelectionB.Selection.CopyFrom(NavisValues.ToItemCollection(itemsB));
            clash.TestsData.TestsEditTestFromCopy(stored, copy);
            transaction.Commit();
        }

        return FindStoredTest(clash, guid, name)
            ?? throw new InvalidOperationException(
                "The clash test '" + name + "' could not be found after it was updated.");
    }

    /// <summary>
    /// Re-locates a stored test after an edit by Guid, then name — edits can
    /// invalidate previously handed-out wrappers. Null when the test is gone.
    /// </summary>
    internal static ClashTest? FindStoredTest(DocumentClash clash, Guid guid, string? name)
    {
        var byGuid = guid != Guid.Empty ? FindTestByGuid(clash.TestsData.Tests, guid) : null;
        if (byGuid != null)
        {
            return byGuid;
        }

        return string.IsNullOrEmpty(name)
            ? null
            : NavisValues.FindSavedItemByName<ClashTest>(clash.TestsData.Tests, name!);
    }

    private static ClashTest? FindTestByGuid(IEnumerable<SavedItem> items, Guid guid)
    {
        foreach (var test in NavisValues.FlattenSavedItems<ClashTest>(items))
        {
            if (test.Guid == guid)
            {
                return test;
            }
        }

        return null;
    }

    /// <summary>Every individual result of a test, with grouped results flattened.</summary>
    internal static List<ClashResult> FlattenResults(ClashTest test)
    {
        var results = new List<ClashResult>();
        var groupNames = new List<string>();
        CollectResults(test.Children, string.Empty, results, groupNames);
        return results;
    }

    /// <summary>
    /// Every individual result of a test plus the display name of the group each
    /// belongs to ("" for ungrouped results). The two lists are index-aligned.
    /// </summary>
    internal static void FlattenResultsWithGroups(ClashTest test, List<ClashResult> results, List<string> groupNames)
    {
        CollectResults(test.Children, string.Empty, results, groupNames);
    }

    /// <summary>
    /// The shared body of the nodes that edit one result, one result group, or a whole list of them
    /// (ClashResult.SetStatus, .Assign, .SetDescription). The input is unpacked (a nested list is flattened), every entry must be a
    /// clash result or a result group, and the edits of one call run inside ONE document transaction, so a list handed over whole
    /// is one undo step and one Clash Detective refresh instead of one per result.
    /// </summary>
    /// <param name="input">The wired result(s), as they came in.</param>
    /// <param name="document">The document (null = the active document).</param>
    /// <param name="undoLabel">Label for the undo entry.</param>
    /// <param name="edit">The edit for one result or group.</param>
    /// <returns>The input, as it came in (a single result stays a single result, a list stays a list).</returns>
    internal static object? EditResults(
        object? input, Document? document, string undoLabel, Action<DocumentClash, IClashResult> edit)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input), "No clash result provided.");
        }

        var unpacked = ClashInputs.Flatten<IClashResult>(input);
        if (unpacked.FirstWrong != null)
        {
            throw new ArgumentException(
                DescribeValue(unpacked.FirstWrong) + " is not a clash result or a result group. " +
                "Wire results from ClashTest.Results (or a filter of them) or a group from ClashTest.Groups.", nameof(input));
        }

        if (unpacked.SkippedNulls > 0)
        {
            NodeWarnings.Add(
                unpacked.SkippedNulls + " empty entr" + (unpacked.SkippedNulls == 1 ? "y" : "ies") + " in the result list " +
                (unpacked.SkippedNulls == 1 ? "was" : "were") + " skipped.");
        }

        if (unpacked.Items.Count == 0)
        {
            NodeWarnings.Add("No clash results were given, so nothing was changed.");
            return input;
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = RequireClash(doc);
        try
        {
            using (var transaction = doc.BeginTransaction(undoLabel))
            {
                foreach (var item in unpacked.Items)
                {
                    edit(clash, item);
                }

                transaction.Commit();
            }
        }
        catch (Exception ex) when (IsDisposed(ex))
        {
            throw StaleInputError("clash results", ex);
        }

        return input;
    }

    /// <summary>A wired value in words for an error message: a saved item by its name, a text as it is, anything else by its kind.</summary>
    private static string DescribeValue(object value)
    {
        if (value is SavedItem saved)
        {
            return string.IsNullOrEmpty(saved.DisplayName) ? "An unnamed " + saved.GetType().Name : "'" + saved.DisplayName + "'";
        }

        if (value is string text)
        {
            return "'" + text + "'";
        }

        var name = value.GetType().Name;
        var tick = name.IndexOf('`');
        return "A value of type " + (tick > 0 ? name.Substring(0, tick) : name);
    }

    /// <summary>
    /// The assignee of a clash result as text, on every Navisworks year: a string through 2025, an Assignee object in 2026
    /// (its display name). Empty when nobody is assigned.
    /// </summary>
    internal static string AssigneeText(IClashResult result)
    {
#if NAV2026
        return result.AssignedTo?.ToString() ?? string.Empty;
#else
        return result.AssignedTo ?? string.Empty;
#endif
    }

    /// <summary>Parses a clash result status name (New/Active/Reviewed/Approved/Resolved).</summary>
    internal static ClashResultStatus ParseResultStatus(string? status)
    {
        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<ClashResultStatus>(status, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException(
            "'" + status + "' is not a clash result status. Use one of: " +
            string.Join(", ", Enum.GetNames(typeof(ClashResultStatus))) + ".", nameof(status));
    }

    /// <summary>
    /// Parses one or several clash result statuses from a comma/semicolon
    /// separated text ("New", "New,Active") — the multi-select form every
    /// status input accepts.
    /// </summary>
    internal static HashSet<ClashResultStatus> ParseResultStatuses(string? status)
    {
        var statuses = new HashSet<ClashResultStatus>();
        foreach (var part in (status ?? string.Empty).Split(',', ';'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                statuses.Add(ParseResultStatus(trimmed));
            }
        }

        if (statuses.Count == 0)
        {
            throw new ArgumentException(
                "No clash status provided. Use one of: " +
                string.Join(", ", Enum.GetNames(typeof(ClashResultStatus))) +
                " — or several separated by commas (\"New,Active\").", nameof(status));
        }

        return statuses;
    }

    /// <summary>Parses a clash test type name (Hard/HardConservative/Clearance/Duplicate/Custom).</summary>
    internal static ClashTestType ParseTestType(string? testType)
    {
        if (!string.IsNullOrEmpty(testType) &&
            Enum.TryParse<ClashTestType>(testType, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException(
            "'" + testType + "' is not a clash test type. Use one of: " +
            string.Join(", ", Enum.GetNames(typeof(ClashTestType))) + ".", nameof(testType));
    }

    private static void CollectResults(
        IEnumerable<SavedItem> items,
        string groupName,
        List<ClashResult> results,
        List<string> groupNames)
    {
        foreach (var item in items)
        {
            if (item is ClashResult result)
            {
                results.Add(result);
                groupNames.Add(groupName);
            }
            else if (item is GroupItem group)
            {
                // ClashResultGroup children are the grouped ClashResults.
                CollectResults(group.Children, group.DisplayName ?? string.Empty, results, groupNames);
            }
        }
    }
}
