using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>The editor side of cancelling a run: the poll between steps, the progress text, the setting and the status line.</summary>
public class RunCancelTests
{
    private sealed class StepNode : NodeModel
    {
        private readonly List<string> _log;
        private readonly int _sleepMilliseconds;

        public StepNode(List<string> log, string name, bool hasInput, int sleepMilliseconds = 0)
        {
            _log = log;
            _sleepMilliseconds = sleepMilliseconds;
            Name = name;
            if (hasInput)
            {
                AddInput("in", typeof(object));
            }

            AddOutput("out", typeof(object));
        }

        public override string NodeType => "TestStep";

        public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
        {
            if (_sleepMilliseconds > 0)
            {
                System.Threading.Thread.Sleep(_sleepMilliseconds);
            }

            _log.Add(Name);
            return new object?[] { Name };
        }
    }

    private static GraphEditorViewModel NewEditor()
    {
        var registry = NodeRegistry.CreateDefault();
        var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
        return new GraphEditorViewModel(registry, new StubDialogs(), settings);
    }

    private static (StepNode A, StepNode B, StepNode C) Chain(GraphEditorViewModel vm, List<string> log, int sleep = 0)
    {
        var a = new StepNode(log, "A", hasInput: false, sleep);
        var b = new StepNode(log, "B", hasInput: true, sleep);
        var c = new StepNode(log, "C", hasInput: true, sleep);
        vm.Graph.AddNode(a);
        vm.Graph.AddNode(b);
        vm.Graph.AddNode(c);
        Assert.True(vm.Graph.Connect(a.OutPorts[0], b.InPorts[0]).Success);
        Assert.True(vm.Graph.Connect(b.OutPorts[0], c.InPorts[0]).Success);
        return (a, b, c);
    }

    [Fact]
    public void EscBetweenNodesStopsTheRunAndTheNextRunCarriesOn()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var log = new List<string>();
            var (_, b, c) = Chain(vm, log);
            vm.CancelPoll = () => vm.RunProgressText.EndsWith("C", StringComparison.Ordinal);   // "Esc" is pressed as C is about to start

            vm.RunGraph();

            Assert.Equal(new[] { "A", "B" }, log.ToArray());
            Assert.False(vm.IsRunning);
            Assert.StartsWith("Run cancelled after 2 of 3 node(s)", vm.StatusMessage);
            Assert.False(b.IsDirty);
            Assert.True(c.IsDirty);

            vm.CancelPoll = () => false;
            vm.RunGraph();

            Assert.Equal(new[] { "A", "B", "C" }, log.ToArray());   // nothing ran twice
            Assert.StartsWith("Run finished", vm.StatusMessage);
            Assert.False(c.IsDirty);
        });
    }

    [Fact]
    public void WithTheSettingOffEscIsNeverRead()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var log = new List<string>();
            Chain(vm, log);
            vm.EscCancelsRun = false;
            var polls = 0;
            vm.CancelPoll = () => { polls++; return true; };

            vm.RunGraph();

            Assert.Equal(0, polls);
            Assert.Equal(new[] { "A", "B", "C" }, log.ToArray());
            Assert.StartsWith("Run finished", vm.StatusMessage);
        });
    }

    [Fact]
    public void TheOverlayTextNamesTheNodeThatIsAboutToRun()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            Chain(vm, new List<string>());
            var seen = new List<string>();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GraphEditorViewModel.RunProgressText))
                {
                    seen.Add(vm.RunProgressText);
                }
            };

            vm.RunGraph();

            Assert.Contains("Running 1 / 3 — A", seen);
            Assert.Contains("Running 2 / 3 — B", seen);
            Assert.Contains("Running 3 / 3 — C", seen);
        });
    }

    [Fact]
    public void ALongRunIsRepaintedWhileItWorks()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            Chain(vm, new List<string>(), sleep: 70);
            var repaints = 0;
            vm.RenderPump = () => repaints++;

            vm.RunGraph();

            Assert.True(repaints >= 1, "the overlay should be repainted during a run of ~200 ms, got " + repaints);
        });
    }

    [Fact]
    public void RequestCancelOutsideARunIsHarmless()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            vm.RequestCancel();
            Assert.False(vm.IsRunning);
            Assert.Equal("Running the graph…", vm.RunProgressText);
        });
    }
}
