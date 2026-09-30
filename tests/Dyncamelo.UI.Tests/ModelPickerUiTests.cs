using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>Named like the Navisworks type so the port is treated as a model element.</summary>
public sealed class ModelItem
{
}

public sealed class PickerNode : NodeModel
{
    public PickerNode()
    {
        Name = "Picker";
        AddInput("item", typeof(ModelItem), null);
        AddInput("items", typeof(IEnumerable<ModelItem>), null);
        AddOutput("out", typeof(object));
    }

    public override string NodeType => "TestPicker";

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new object?[] { null };
}

internal sealed class FakePicker : IModelPicker
{
    public int Selected = 3;
    public string? Revealed;

    public string? CaptureSelection(bool single, out int count)
    {
        count = Selected;
        if (Selected == 0)
        {
            return null;
        }

        return single ? "nw:0:1" : "nw:0:1;0:2;0:3";
    }

    public string Describe(string? value) => value == null ? string.Empty : "Pipe (" + ModelPickerHost.CountOf(value) + ")";

    public bool Reveal(string? value)
    {
        Revealed = value;
        return true;
    }
}

public class ModelPickerUiTests
{
    private static (GraphEditorViewModel Vm, NodeViewModel Node) Rig()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterNodeType("TestPicker", () => new PickerNode());
        var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
        var vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
        vm.Graph.AddNode(new PickerNode());
        return (vm, vm.Items.OfType<NodeViewModel>().Single());
    }

    [Fact]
    public void PickingTakesTheSelectionAndClearingRestoresTheDefault()
    {
        StaHost.Run(() =>
        {
            var previous = ModelPickerHost.Current;
            var picker = new FakePicker();
            ModelPickerHost.Current = picker;
            try
            {
                var (vm, node) = Rig();
                var list = node.Inputs.Single(c => c.Port.Name == "items");
                Assert.Equal(PortEditorKind.Model, list.EditorKind);
                Assert.False(list.HasModelValue);

                list.CaptureModelCommand.Execute(null);
                Assert.True(list.HasModelValue);
                Assert.Equal("nw:0:1;0:2;0:3", list.Port.UserValue);
                Assert.Equal("Pipe (3)", list.ModelSummary);

                list.RevealModelCommand.Execute(null);
                Assert.Equal("nw:0:1;0:2;0:3", picker.Revealed);

                list.ResetCommand.Execute(null);
                Assert.False(list.HasModelValue);
                Assert.Equal(string.Empty, list.ModelSummary);
            }
            finally
            {
                ModelPickerHost.Current = previous;
            }
        });
    }

    [Fact]
    public void ASingleItemInputTakesOnlyTheFirstSelectedElement()
    {
        StaHost.Run(() =>
        {
            var previous = ModelPickerHost.Current;
            ModelPickerHost.Current = new FakePicker { Selected = 5 };
            try
            {
                var (vm, node) = Rig();
                var single = node.Inputs.Single(c => c.Port.Name == "item");

                single.CaptureModelCommand.Execute(null);

                Assert.Equal("nw:0:1", single.Port.UserValue);
                Assert.Contains("first of 5", vm.StatusMessage);
            }
            finally
            {
                ModelPickerHost.Current = previous;
            }
        });
    }

    [Fact]
    public void PickingWithNothingSelectedSaysSoAndChangesNothing()
    {
        StaHost.Run(() =>
        {
            var previous = ModelPickerHost.Current;
            ModelPickerHost.Current = new FakePicker { Selected = 0 };
            try
            {
                var (vm, node) = Rig();
                var single = node.Inputs.Single(c => c.Port.Name == "item");

                single.CaptureModelCommand.Execute(null);

                Assert.False(single.HasModelValue);
                Assert.Contains("Select something", vm.StatusMessage);
            }
            finally
            {
                ModelPickerHost.Current = previous;
            }
        });
    }

    [Fact]
    public void WithoutAHostPickerTheCommandsAreDisabled()
    {
        StaHost.Run(() =>
        {
            var previous = ModelPickerHost.Current;
            ModelPickerHost.Current = null;
            try
            {
                var (_, node) = Rig();
                var single = node.Inputs.Single(c => c.Port.Name == "item");
                Assert.False(single.CaptureModelCommand.CanExecute(null));
                Assert.False(single.RevealModelCommand.CanExecute(null));
            }
            finally
            {
                ModelPickerHost.Current = previous;
            }
        });
    }
}
