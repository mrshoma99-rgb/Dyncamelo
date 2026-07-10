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
