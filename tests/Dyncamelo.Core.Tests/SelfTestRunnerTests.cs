using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.SelfTest;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>Stand-ins for the host's nodes: the runner is exercised without Navisworks.</summary>
public static class SelfTestFixtures
{
    [NodeName("SelfTest.Number")]
    [return: NodeName("value")]
    public static double Number() => 21;

    [NodeName("SelfTest.Double")]
    [return: NodeName("value")]
    public static double Double(double x) => x * 2;

    [NodeName("SelfTest.Text")]
    [return: NodeName("value")]
    public static string Text() => "hello";

    [NodeName("SelfTest.Throws")]
    [return: NodeName("value")]
    public static double Throws() => throw new InvalidOperationException("no document is open");
}

public class SelfTestRunnerTests
{
    private static readonly ISet<string> Allowed = new HashSet<string> { "SelfTest.Number", "SelfTest.Double", "SelfTest.Text", "SelfTest.Throws", "SelfTest.Missing" };

    private static SelfTestRunner Runner(SelfTestHost? host = null, ISet<string>? allowed = null)
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterDefinitions(AssemblyNodeLoader.LoadType(typeof(SelfTestFixtures)));
        return new SelfTestRunner(registry, () => new EvaluationContext(), host ?? new SelfTestHost(), allowed ?? Allowed);
    }

    private static SelfTestResult RunOne(SelfTestCase testCase, SelfTestHost? host = null) =>
        Runner(host).Run(new[] { testCase }).Results.Single();

    [Fact]
    public void ANodeChainRunsAndTheCheckSeesTheOutputs()
    {
        var testCase = new SelfTestCase("Fixtures", "doubles a number",
            new SelfTestStep("SelfTest.Number"),
            new SelfTestStep("SelfTest.Double").From("x", 0, "value"))
        {
            Check = nodes => Equals(nodes[1].OutPorts[0].Value, 42d) ? null : "got " + nodes[1].OutPorts[0].Value,
        };

        var result = RunOne(testCase);

        Assert.Equal(SelfTestOutcome.Passed, result.Outcome);
        Assert.Equal(string.Empty, result.Message);
    }

    [Fact]
    public void APinnedInputIsUsed()
    {
        var testCase = new SelfTestCase("Fixtures", "pinned", new SelfTestStep("SelfTest.Double").With("x", 4d))
        {
            Check = nodes => Equals(nodes[0].OutPorts[0].Value, 8d) ? null : "wrong",
        };

        Assert.Equal(SelfTestOutcome.Passed, RunOne(testCase).Outcome);
    }

    [Fact]
    public void ANodeThatFailsFailsTheCaseWithItsMessage()
    {
        var result = RunOne(new SelfTestCase("Fixtures", "throws", new SelfTestStep("SelfTest.Throws")));

        Assert.Equal(SelfTestOutcome.Failed, result.Outcome);
        Assert.Contains("'SelfTest.Throws' failed", result.Message);
        Assert.Contains("no document is open", result.Message);
    }

    [Fact]
    public void ACheckThatIsNotSatisfiedFailsTheCase()
    {
        var result = RunOne(new SelfTestCase("Fixtures", "check", new SelfTestStep("SelfTest.Number")) { Check = _ => "the number should have been 7" });

        Assert.Equal(SelfTestOutcome.Failed, result.Outcome);
        Assert.Equal("the number should have been 7", result.Message);
    }

    [Fact]
    public void ANodeThatIsNotOnTheReadOnlyListIsRefusedAndNeverRuns()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterDefinitions(AssemblyNodeLoader.LoadType(typeof(SelfTestFixtures)));
        var runner = new SelfTestRunner(registry, () => new EvaluationContext(), new SelfTestHost(), new HashSet<string> { "SelfTest.Number" });

        var result = runner.Run(new[] { new SelfTestCase("Fixtures", "refused", new SelfTestStep("SelfTest.Text")) }).Results.Single();

        Assert.Equal(SelfTestOutcome.Failed, result.Outcome);
        Assert.Contains("refuses to run 'SelfTest.Text'", result.Message);
    }

    [Fact]
    public void ThePublishedReadOnlyListIsWhatARunnerUsesByDefault()
    {
        var registry = NodeRegistry.CreateDefault();
        var runner = new SelfTestRunner(registry, () => new EvaluationContext(), new SelfTestHost());

        var result = runner.Run(new[] { new SelfTestCase("Fixtures", "default list", new SelfTestStep("Selection.SetCurrent")) }).Results.Single();

        Assert.Equal(SelfTestOutcome.Failed, result.Outcome);
        Assert.Contains("not on the list of nodes that only read", result.Message);
    }

    [Fact]
    public void ANodeMissingFromTheLibraryOrAnInputThatWasRenamedIsSaidPlainly()
    {
        var missing = RunOne(new SelfTestCase("Fixtures", "missing", new SelfTestStep("SelfTest.Missing")));
        Assert.Equal(SelfTestOutcome.Failed, missing.Outcome);
        Assert.Contains("not in the node library", missing.Message);

        var renamed = RunOne(new SelfTestCase("Fixtures", "renamed", new SelfTestStep("SelfTest.Double").With("amount", 1d)));
        Assert.Equal(SelfTestOutcome.Failed, renamed.Outcome);
        Assert.Contains("has no input called 'amount'", renamed.Message);

        var badWire = RunOne(new SelfTestCase("Fixtures", "badwire", new SelfTestStep("SelfTest.Number"), new SelfTestStep("SelfTest.Double").From("x", 0, "nope")));
        Assert.Equal(SelfTestOutcome.Failed, badWire.Outcome);
        Assert.Contains("a port was renamed", badWire.Message);
    }

    [Fact]
    public void ACaseThatNeedsAModelIsSkippedWithoutOneAndRunsWithOne()
    {
        var testCase = new SelfTestCase("Fixtures", "needs a model", new SelfTestStep("SelfTest.Number")) { NeedsModel = true };

        var without = RunOne(testCase, new SelfTestHost { ModelAvailable = () => false });
        Assert.Equal(SelfTestOutcome.Skipped, without.Outcome);
        Assert.Contains("open a model", without.Message);

        Assert.Equal(SelfTestOutcome.Passed, RunOne(testCase, new SelfTestHost { ModelAvailable = () => true }).Outcome);

        // A host that cannot even say counts as having no model.
        Assert.Equal(SelfTestOutcome.Skipped, RunOne(testCase, new SelfTestHost { ModelAvailable = () => throw new InvalidOperationException() }).Outcome);
    }

    [Fact]
    public void ACaseForAnotherEditionOfTheHostIsSkipped()
    {
        var testCase = new SelfTestCase("Fixtures", "manage only", new SelfTestStep("SelfTest.Number")) { NeedsEdition = "Manage" };

        var simulate = RunOne(testCase, new SelfTestHost { Description = "Autodesk Navisworks Simulate 2024 (API 21.0)" });
        Assert.Equal(SelfTestOutcome.Skipped, simulate.Outcome);
        Assert.Contains("needs Navisworks Manage", simulate.Message);

        Assert.Equal(SelfTestOutcome.Passed, RunOne(testCase, new SelfTestHost { Description = "Autodesk Navisworks Manage 2024 (API 21.0)" }).Outcome);
    }

    [Fact]
    public void ProgressIsReportedAndARunCanBeStopped()
    {
        var cases = Enumerable.Range(1, 5).Select(i => new SelfTestCase("Fixtures", "case " + i, new SelfTestStep("SelfTest.Number"))).ToList();
        var seen = new List<string>();
        var stopAfter = 2;

        var report = Runner().Run(cases, (index, total, title) => seen.Add(index + "/" + total + " " + title), () => seen.Count >= stopAfter);

        Assert.Equal(2, report.Results.Count);
        Assert.True(report.Cancelled);
        Assert.Equal(new[] { "0/5 Fixtures: case 1", "1/5 Fixtures: case 2" }, seen);
        Assert.Contains("(stopped early)", report.Summary);
    }

    [Fact]
    public void TheReportTextHasASummaryAndOneLinePerCase()
    {
        var cases = new[]
        {
            new SelfTestCase("Fixtures", "fine", new SelfTestStep("SelfTest.Number")),
            new SelfTestCase("Fixtures", "broken", new SelfTestStep("SelfTest.Throws")),
            new SelfTestCase("Other", "skipped", new SelfTestStep("SelfTest.Number")) { NeedsModel = true },
        };

        var report = Runner(new SelfTestHost { ModelAvailable = () => false }).Run(cases);
        var text = report.ToText("Autodesk Navisworks Manage 2024 (API 21.0)", "0.46.0", new DateTime(2026, 10, 2, 9, 0, 0));

        Assert.Equal("1 passed, 1 failed, 1 skipped", report.Summary);
        Assert.Contains("CamelGraph 0.46.0 in Autodesk Navisworks Manage 2024 (API 21.0), 2026-10-02 09:00", text);
        Assert.Contains("1 passed, 1 failed, 1 skipped", text);
        Assert.Contains("[pass] fine", text);
        Assert.Contains("[FAIL] broken", text);
        Assert.Contains("no document is open", text);
        Assert.Contains("[skip] skipped: open a model first", text);
        Assert.Contains("Other", text);
        Assert.Contains("Nodes that change the model", text);
    }
}
