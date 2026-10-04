using System;
using System.Collections.Generic;
using System.Globalization;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Coordination;

/// <summary>
/// What a ClashTest.Edit call wants changed, after the "leave unchanged" sentinels have been
/// read: a null member means "keep the test's current value".
/// </summary>
[IsVisibleInLibrary(false)]
public sealed class ClashEditPlan
{
    /// <summary>Creates a plan.</summary>
    /// <param name="newName">The new display name, or null to keep the current one.</param>
    /// <param name="testType">The canonical test-type name, or null to keep the current one.</param>
    /// <param name="tolerance">The new tolerance, or null to keep the current one.</param>
    /// <param name="mergeComposites">The new merge-composites flag, or null to keep the current one.</param>
    /// <param name="replaceSelectionA">Whether selection A is to be replaced.</param>
    /// <param name="replaceSelectionB">Whether selection B is to be replaced.</param>
    public ClashEditPlan(
        string? newName,
        string? testType,
        double? tolerance,
        bool? mergeComposites,
        bool replaceSelectionA,
        bool replaceSelectionB)
    {
        NewName = newName;
        TestType = testType;
        Tolerance = tolerance;
        MergeComposites = mergeComposites;
        ReplaceSelectionA = replaceSelectionA;
        ReplaceSelectionB = replaceSelectionB;
    }

    /// <summary>The new display name, or null to keep the current one.</summary>
    public string? NewName { get; }

    /// <summary>The canonical test-type name (Hard, HardConservative, Clearance, Duplicate, Custom), or null to keep the current one.</summary>
    public string? TestType { get; }

    /// <summary>The new tolerance in document units, or null to keep the current one.</summary>
    public double? Tolerance { get; }

    /// <summary>The new merge-composites flag, or null to keep the current one.</summary>
    public bool? MergeComposites { get; }

    /// <summary>Whether selection A is to be replaced.</summary>
    public bool ReplaceSelectionA { get; }

    /// <summary>Whether selection B is to be replaced.</summary>
    public bool ReplaceSelectionB { get; }

    /// <summary>Whether anything other than the name changes (those edits are committed in one settings edit).</summary>
    public bool HasSettingsEdit =>
        TestType != null || Tolerance.HasValue || MergeComposites.HasValue || ReplaceSelectionA || ReplaceSelectionB;

    /// <summary>Whether the call changes anything at all.</summary>
    public bool HasChanges => HasSettingsEdit || NewName != null;
}

/// <summary>
/// Input rules for the clash-test maintenance nodes (ClashTest.Edit / Duplicate), free of
/// Navisworks types so they are unit-testable: how the "leave unchanged" defaults are read,
/// which test-type spellings are accepted, and how a copy is named.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ClashEditRules
{
    /// <summary>The canonical clash test types, as Navisworks names them.</summary>
    public static readonly IReadOnlyList<string> TestTypes =
        new[] { "Hard", "HardConservative", "Clearance", "Duplicate", "Custom" };

    /// <summary>The text that means "leave this setting as it is" (an empty text or null means the same).</summary>
    public const string Unchanged = "unchanged";

    /// <summary>Whether a text input says "leave unchanged": null, empty, white space or "unchanged".</summary>
    /// <param name="text">The input.</param>
    /// <returns>True when the setting is to be left alone.</returns>
    public static bool IsUnchanged(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length == 0 || string.Equals(trimmed, Unchanged, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the test-type input. Case, spaces, hyphens and underscores are ignored and "duplicates"
    /// is accepted for "Duplicate".
    /// </summary>
    /// <param name="testType">The input ("", null or "unchanged" keep the current type).</param>
    /// <returns>The canonical name, or null to keep the current type.</returns>
    /// <exception cref="ArgumentException">The text is not a clash test type.</exception>
    public static string? NormalizeTestType(string? testType)
    {
        if (IsUnchanged(testType))
        {
            return null;
        }

        var key = Squash(testType!);
        if (key == "duplicates")
        {
            key = "duplicate";
        }

        foreach (var name in TestTypes)
        {
            if (string.Equals(Squash(name), key, StringComparison.Ordinal))
            {
                return name;
            }
        }

        throw new ArgumentException(
            "'" + testType + "' is not a clash test type. Use one of: " + string.Join(", ", TestTypes) +
            " (or leave it as '" + Unchanged + "').", nameof(testType));
    }

    /// <summary>
    /// Reads the tolerance input: any negative number (the default is -1) keeps the current tolerance.
    /// </summary>
    /// <param name="tolerance">The input, in document units.</param>
    /// <returns>The tolerance to set, or null to keep the current one.</returns>
    /// <exception cref="ArgumentException">The value is NaN or infinite.</exception>
    public static double? ResolveTolerance(double tolerance)
    {
        if (double.IsNaN(tolerance) || double.IsInfinity(tolerance))
        {
            throw new ArgumentException(
                "The tolerance is not a finite number. Wire a number of 0 or more, or leave it at -1 to keep the test's tolerance.",
                nameof(tolerance));
        }

        return tolerance < 0.0 ? (double?)null : tolerance;
    }

    /// <summary>Reads a yes / no / unchanged input.</summary>
    /// <param name="text">The input: yes, true, on or 1 / no, false, off or 0 / unchanged (or empty).</param>
    /// <param name="parameterName">The input's name, for the message.</param>
    /// <returns>The flag to set, or null to keep the current one.</returns>
    /// <exception cref="ArgumentException">The text is none of the above.</exception>
    public static bool? ResolveFlag(string? text, string parameterName)
    {
        if (IsUnchanged(text))
        {
            return null;
        }

        switch (text!.Trim().ToLowerInvariant())
        {
            case "yes":
            case "true":
            case "on":
            case "1":
                return true;
            case "no":
            case "false":
            case "off":
            case "0":
                return false;
            default:
                throw new ArgumentException(
                    "'" + text + "' is not yes or no for '" + parameterName + "'. Use yes, no or " + Unchanged + ".", parameterName);
        }
    }

    /// <summary>Reads every ClashTest.Edit input into one plan.</summary>
    /// <param name="newName">The new display name ("" keeps the current one).</param>
    /// <param name="testType">The test type (see <see cref="NormalizeTestType"/>).</param>
    /// <param name="tolerance">The tolerance (see <see cref="ResolveTolerance"/>).</param>
    /// <param name="mergeComposites">The merge-composites choice (see <see cref="ResolveFlag"/>).</param>
    /// <param name="hasItemsA">Whether items were wired for selection A.</param>
    /// <param name="hasItemsB">Whether items were wired for selection B.</param>
    /// <returns>The plan.</returns>
    public static ClashEditPlan Plan(
        string? newName,
        string? testType,
        double tolerance,
        string? mergeComposites,
        bool hasItemsA,
        bool hasItemsB)
    {
        var name = string.IsNullOrWhiteSpace(newName) ? null : newName;
        return new ClashEditPlan(
            name,
            NormalizeTestType(testType),
            ResolveTolerance(tolerance),
            ResolveFlag(mergeComposites, "mergeComposites"),
            hasItemsA,
            hasItemsB);
    }

    /// <summary>The name a duplicated test gets when none is given: "&lt;name&gt; copy".</summary>
    /// <param name="sourceName">The source test's display name.</param>
    /// <param name="requested">The name the user typed ("" for the default).</param>
    /// <returns>The name to use.</returns>
    public static string CopyName(string? sourceName, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested!;
        }

        return string.IsNullOrWhiteSpace(sourceName) ? "Clash test copy" : sourceName + " copy";
    }

    /// <summary>A short, invariant description of an edit plan, for undo labels and messages.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns>For example "type Clearance, tolerance 0.05".</returns>
    public static string Describe(ClashEditPlan plan)
    {
        if (plan == null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        var parts = new List<string>();
        if (plan.NewName != null)
        {
            parts.Add("name '" + plan.NewName + "'");
        }

        if (plan.TestType != null)
        {
            parts.Add("type " + plan.TestType);
        }

        if (plan.Tolerance.HasValue)
        {
            parts.Add("tolerance " + plan.Tolerance.Value.ToString("0.######", CultureInfo.InvariantCulture));
        }

        if (plan.MergeComposites.HasValue)
        {
            parts.Add("merge composites " + (plan.MergeComposites.Value ? "on" : "off"));
        }

        if (plan.ReplaceSelectionA)
        {
            parts.Add("selection A");
        }

        if (plan.ReplaceSelectionB)
        {
            parts.Add("selection B");
        }

        return parts.Count == 0 ? "no changes" : string.Join(", ", parts);
    }

    private static string Squash(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch) && ch != '-' && ch != '_')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }
}
