using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace Dyncamelo.Navisworks.Internal;

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
    /// undo step, matching Navisworks' own grouping commands.
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
        var index = IndexOfTest(clash, stored);
        if (index < 0)
        {
            throw new InvalidOperationException(
                "The clash test '" + stored.DisplayName + "' is no longer in the document — it may have been " +
                "deleted or renamed while the graph ran.");
        }

        var guid = stored.Guid;
        var name = stored.DisplayName;
        using (var transaction = doc.BeginTransaction(undoLabel))
        {
            clash.TestsData.TestsReplaceWithCopy(index, editedCopy);
            transaction.Commit();
        }

        // ReplaceWithCopy disposes the previous instance — hand back the new one.
        return FindStoredTest(clash, guid, name)
            ?? throw new InvalidOperationException(
                "The clash test '" + name + "' could not be found after the edit was committed.");
    }

    /// <summary>Position of a stored test among the top-level tests, by identity (-1 when absent).</summary>
    internal static int IndexOfTest(DocumentClash clash, ClashTest test)
    {
        var tests = clash.TestsData.Tests;
        for (int i = 0; i < tests.Count; i++)
        {
            if (!(tests[i] is ClashTest candidate))
            {
                continue;
            }

            if (ReferenceEquals(candidate, test) ||
                (test.Guid != Guid.Empty && candidate.Guid == test.Guid) ||
                (test.Guid == Guid.Empty && string.Equals(candidate.DisplayName, test.DisplayName, StringComparison.Ordinal)))
            {
                return i;
            }
        }

        return -1;
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
