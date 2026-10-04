using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Nodes;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>The new fundamentals work through the whole pipeline (save, load, run), not just as static calls.</summary>
public class GapsWaveATests
{
    [Fact]
    public void FormulaIsLacedOverAList()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "formula" };
        var range = Pipeline.ZeroTouch(registry, "Math.Sequence", "Numbers");
        var start = new NumberInputNode { Name = "Start", Value = 1 };
        var count = new NumberInputNode { Name = "Count", Value = 4 };
        var formula = Pipeline.ZeroTouch(registry, "Math.Formula", "Formula");
        var text = new StringInputNode { Name = "Expression", Value = "a * a + 1" };
        foreach (var node in new NodeModel[] { range, start, count, formula, text })
        {
            graph.AddNode(node);
        }

        Pipeline.Connect(graph, start, "value", range, "start");
        Pipeline.Connect(graph, count, "value", range, "count");
        Pipeline.Connect(graph, text, "value", formula, "expression");
        Pipeline.Connect(graph, range, "numbers", formula, "a");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        Assert.True(result.Success);
        Assert.Equal(new[] { 2d, 5d, 10d, 17d }, Pipeline.AsDoubles(Pipeline.Output(run, "Formula")));
    }

    [Fact]
    public void ASumOfAPropertyColumnFeedsAPercentage()
    {
        // the weekly KPI: 37 of 340 items fail a check, shown as a percentage
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "kpi" };
        var list = Pipeline.ZeroTouch(registry, "Math.Sequence", "Numbers");
        var start = new NumberInputNode { Name = "Start", Value = 0 };
        var count = new NumberInputNode { Name = "Count", Value = 10 };
        var threshold = new NumberInputNode { Name = "Threshold", Value = 6 };
        var filter = Pipeline.ZeroTouch(registry, "List.FilterByValue", "Filter");
        var failed = Pipeline.ZeroTouch(registry, "List.Count", "Failed");
        var total = Pipeline.ZeroTouch(registry, "List.Count", "Total");
        var percent = Pipeline.ZeroTouch(registry, "Math.Percent", "Percent");
        foreach (var node in new NodeModel[] { list, start, count, threshold, filter, failed, total, percent })
        {
            graph.AddNode(node);
        }

        Pipeline.Connect(graph, start, "value", list, "start");
        Pipeline.Connect(graph, count, "value", list, "count");
        Pipeline.Connect(graph, list, "numbers", filter, "list");
        Pipeline.Connect(graph, threshold, "value", filter, "value");
        Pipeline.Connect(graph, filter, "matched", failed, "list");
        Pipeline.Connect(graph, list, "numbers", total, "list");
        Pipeline.Connect(graph, failed, "count", percent, "part");
        Pipeline.Connect(graph, total, "count", percent, "total");

        filter.InPorts.First(p => p.Name == "test").SetUserValue(">=");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        Assert.True(result.Success);
        // 6, 7, 8, 9 pass ">= 6": 4 of 10
        Assert.Equal(40d, (double)Pipeline.Output(run, "Percent")!, 6);
    }

    [Fact]
    public void LogicSwitchMapsAStatusToAColourName()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "switch" };
        var status = new StringInputNode { Name = "Status", Value = "active" };
        var delimiter = new StringInputNode { Name = "Delimiter", Value = "," };
        var cases = Pipeline.ZeroTouch(registry, "String.Split", "Cases");
        var caseText = new StringInputNode { Name = "CaseText", Value = "New,Active,Resolved" };
        var results = Pipeline.ZeroTouch(registry, "String.Split", "Results");
        var resultText = new StringInputNode { Name = "ResultText", Value = "red,amber,green" };
        var map = Pipeline.ZeroTouch(registry, "Logic.Switch", "Map");
        foreach (var node in new NodeModel[] { status, delimiter, cases, caseText, results, resultText, map })
        {
            graph.AddNode(node);
        }

        Pipeline.Connect(graph, caseText, "value", cases, "text");
        Pipeline.Connect(graph, delimiter, "value", cases, "separator");
        Pipeline.Connect(graph, resultText, "value", results, "text");
        Pipeline.Connect(graph, delimiter, "value", results, "separator");
        Pipeline.Connect(graph, status, "value", map, "value");
        Pipeline.Connect(graph, cases, "list", map, "cases");
        Pipeline.Connect(graph, results, "list", map, "results");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        Assert.True(result.Success);
        Assert.Equal("amber", Pipeline.Output(run, "Map"));
    }
}
