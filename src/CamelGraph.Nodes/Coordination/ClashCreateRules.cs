using System;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Coordination;

/// <summary>What ClashTest.Create does when a clash test with the wanted name is already in the document.</summary>
public enum ClashIfExists
{
    /// <summary>Keep the existing test exactly as it is (results, statuses, comments and groups too) and return it.</summary>
    Reuse,

    /// <summary>Keep the existing test and its results, and apply the type, tolerance and selections that were wired.</summary>
    Update,

    /// <summary>Replace it with a brand-new, empty test: results, statuses, assignments, comments and groups are lost.</summary>
    Replace,

    /// <summary>Stop with an error.</summary>
    Error,
}

/// <summary>The choices of ClashTest.Create that need no Navisworks types, so they are unit-tested.</summary>
[IsVisibleInLibrary(false)]
public static class ClashCreateRules
{
    /// <summary>Reads the <c>ifExists</c> choice (any case, surrounding spaces ignored; blank = reuse).</summary>
    /// <param name="text">The text from the node's input.</param>
    /// <returns>The choice.</returns>
    public static ClashIfExists ParseIfExists(string? text)
    {
        switch ((text ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "":
            case "reuse": return ClashIfExists.Reuse;
            case "update": return ClashIfExists.Update;
            case "replace": return ClashIfExists.Replace;
            case "error": return ClashIfExists.Error;
            default:
                throw new ArgumentException(
                    "Unknown ifExists '" + text + "'. Use reuse, update, replace or error.", nameof(text));
        }
    }

    /// <summary>The warning shown when an existing test was reused as it is.</summary>
    /// <param name="testName">The test's name.</param>
    public static string ReusedMessage(string testName) =>
        "A clash test named '" + testName + "' already exists, so it was reused as it is: its type, tolerance and selections were " +
        "not changed and its results were kept. Set ifExists to update to apply the new settings, or to replace to start from an empty test.";

    /// <summary>The error shown when ifExists is error and the test is there.</summary>
    /// <param name="testName">The test's name.</param>
    public static string ExistsMessage(string testName) =>
        "A clash test named '" + testName + "' already exists. Set ifExists to reuse (keep it), update (keep its results and apply " +
        "the new settings) or replace (start from an empty test), or give the new test another name.";
}
