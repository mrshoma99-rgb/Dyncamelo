using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CamelGraph.Core.Graph;

namespace CamelGraph.Core.SelfTest;

/// <summary>How one self-test case ended.</summary>
public enum SelfTestOutcome
{
    /// <summary>Every node ran and the check was satisfied.</summary>
    Passed,

    /// <summary>A node failed, did not run, or the check was not satisfied.</summary>
    Failed,

    /// <summary>The case was not run (it needs a model, or an edition of the host that is not running).</summary>
    Skipped,
}

/// <summary>One node of a self-test case: which node, the values pinned on its inputs, and the wires from earlier steps.</summary>
public sealed class SelfTestStep
{
    private readonly Dictionary<string, object?> _inputs = new Dictionary<string, object?>(StringComparer.Ordinal);
    private readonly Dictionary<string, KeyValuePair<int, string>> _wires = new Dictionary<string, KeyValuePair<int, string>>(StringComparer.Ordinal);

    /// <summary>Creates a step.</summary>
    /// <param name="node">The node's name as the library shows it ("Models.RootItems").</param>
    public SelfTestStep(string node)
    {
        Node = node;
    }

    /// <summary>The node's name.</summary>
    public string Node { get; }

    /// <summary>Values pinned on inputs, by input name.</summary>
    public IReadOnlyDictionary<string, object?> Inputs => _inputs;

    /// <summary>Wires into inputs, by input name: the earlier step's index and its output name.</summary>
    public IReadOnlyDictionary<string, KeyValuePair<int, string>> Wires => _wires;

    /// <summary>Pins a value on an input.</summary>
    /// <param name="input">The input's name.</param>
    /// <param name="value">The value.</param>
    public SelfTestStep With(string input, object? value)
    {
        _inputs[input] = value;
        return this;
    }

    /// <summary>Wires an output of an earlier step into an input.</summary>
    /// <param name="input">The input's name.</param>
    /// <param name="fromStep">Index of the earlier step.</param>
    /// <param name="output">The earlier step's output name.</param>
    public SelfTestStep From(string input, int fromStep, string output)
    {
        _wires[input] = new KeyValuePair<int, string>(fromStep, output);
        return this;
    }
}

/// <summary>A small graph of read-only nodes and what to expect of it.</summary>
public sealed class SelfTestCase
{
    /// <summary>Creates a case.</summary>
    /// <param name="area">Group in the report ("Model items").</param>
    /// <param name="title">What is checked ("display names of the root items").</param>
    /// <param name="steps">The nodes, in order; a step may be wired from earlier ones.</param>
    public SelfTestCase(string area, string title, params SelfTestStep[] steps)
    {
        Area = area;
        Title = title;
        Steps = steps;
    }

    /// <summary>Group in the report.</summary>
    public string Area { get; }

    /// <summary>What is checked.</summary>
    public string Title { get; }

    /// <summary>The nodes, in order.</summary>
    public IReadOnlyList<SelfTestStep> Steps { get; }

    /// <summary>True when the case needs at least one model loaded in the host.</summary>
    public bool NeedsModel { get; set; }

    /// <summary>The edition of the host the case needs ("Manage" for Clash Detective), or null for any.</summary>
    public string? NeedsEdition { get; set; }

    /// <summary>Looks at the nodes after they ran (in step order) and returns what is wrong, or null.</summary>
    public Func<IReadOnlyList<NodeModel>, string?>? Check { get; set; }
}

/// <summary>The result of one case.</summary>
public sealed class SelfTestResult
{
    /// <summary>Creates a result.</summary>
    public SelfTestResult(SelfTestCase testCase, SelfTestOutcome outcome, string message, TimeSpan elapsed)
    {
        Case = testCase;
        Outcome = outcome;
        Message = message;
        Elapsed = elapsed;
    }

    /// <summary>The case.</summary>
    public SelfTestCase Case { get; }

    /// <summary>How it ended.</summary>
    public SelfTestOutcome Outcome { get; }

    /// <summary>Why it failed or was skipped, or a note about a pass; may be empty.</summary>
    public string Message { get; }

    /// <summary>How long it took.</summary>
    public TimeSpan Elapsed { get; }
}

/// <summary>Everything a self-test run found, and the text to paste into a bug report.</summary>
public sealed class SelfTestReport
{
    /// <summary>Creates a report.</summary>
    /// <param name="results">One result per case that was reached.</param>
    /// <param name="cancelled">True when the run was stopped before the last case.</param>
    public SelfTestReport(IReadOnlyList<SelfTestResult> results, bool cancelled)
    {
        Results = results;
        Cancelled = cancelled;
    }

    /// <summary>One result per case that was reached.</summary>
    public IReadOnlyList<SelfTestResult> Results { get; }

    /// <summary>True when the run was stopped early.</summary>
    public bool Cancelled { get; }

    /// <summary>Cases that passed.</summary>
    public int Passed => Results.Count(r => r.Outcome == SelfTestOutcome.Passed);

    /// <summary>Cases that failed.</summary>
    public int Failed => Results.Count(r => r.Outcome == SelfTestOutcome.Failed);

    /// <summary>Cases that were not run.</summary>
    public int Skipped => Results.Count(r => r.Outcome == SelfTestOutcome.Skipped);

    /// <summary>One line: "24 passed, 1 failed, 3 skipped".</summary>
    public string Summary =>
        Passed.ToString(CultureInfo.InvariantCulture) + " passed, " + Failed.ToString(CultureInfo.InvariantCulture) + " failed, " +
        Skipped.ToString(CultureInfo.InvariantCulture) + " skipped" + (Cancelled ? " (stopped early)" : string.Empty);

    /// <summary>The report as text.</summary>
    /// <param name="host">The host application as reported by the host, or empty.</param>
    /// <param name="version">The CamelGraph version.</param>
    /// <param name="when">When the run finished (local time).</param>
    public string ToText(string host, string version, DateTime when)
    {
        var text = new StringBuilder();
        text.AppendLine("CamelGraph self-test");
        text.AppendLine("CamelGraph " + (string.IsNullOrWhiteSpace(version) ? "unknown" : version) + " in " + (string.IsNullOrWhiteSpace(host) ? "an unknown host" : host) +
                        ", " + when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        text.AppendLine(Summary);
        text.AppendLine();

        string? area = null;
        foreach (var result in Results)
        {
            if (!string.Equals(area, result.Case.Area, StringComparison.Ordinal))
            {
                area = result.Case.Area;
                text.AppendLine(area);
            }

            var mark = result.Outcome == SelfTestOutcome.Passed ? "pass" : result.Outcome == SelfTestOutcome.Failed ? "FAIL" : "skip";
            text.Append("  [" + mark + "] " + result.Case.Title);
            if (result.Outcome != SelfTestOutcome.Skipped)
            {
                text.Append(" (" + result.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms)");
            }

            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                text.Append(": " + result.Message.Replace("\r\n", " ").Replace("\n", " "));
            }

            text.AppendLine();
        }

        text.AppendLine();
        text.AppendLine("Only nodes that read are run here. Nodes that change the model, the files or the saved items are not covered.");
        return text.ToString();
    }
}
