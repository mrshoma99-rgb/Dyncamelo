using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Nodes;
using CamelGraph.UI.Services;
using CamelGraph.UI.ViewModels;
using CamelGraph.UI.Views;
using Xunit;

namespace CamelGraph.UI.Tests;

/// <summary>
/// Puts one instance of every node the editor can hold on a canvas and renders it. A template, converter or control of a node
/// body that fails only when that node is added (the Color Picker, in the field) is otherwise invisible to the tests: they build
/// a handful of test nodes, and a WPF exception on the test thread is logged and swallowed.
/// </summary>
public class EveryNodeRendersTests
{
    [Theory]
    [InlineData("CamelGraphDark", false)]
    [InlineData("CamelGraphDark", true)]
    [InlineData("Light", false)]
    [InlineData("Light", true)]
    public void EveryNodeKindRendersWithoutAnError(string palette, bool collapsed)
    {
        while (StaHost.Unhandled.TryDequeue(out _))
        {
        }

        var kinds = new List<string>();
        Window? window = null;
        GraphEditorViewModel? vm = null;
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            NodeLibrary.RegisterAll(registry);
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
            vm.PaletteId = palette;

            var nodes = new List<NodeModel>();
            foreach (var type in registry.NodeTypes.OrderBy(t => t, StringComparer.Ordinal))
            {
                var node = registry.CreateNode(type);

                // A node group instance needs a group definition to draw; the group tests cover it.
                if (node != null && type != CamelGraph.Core.Groups.GroupInstanceNode.TypeName)
                {
                    kinds.Add(type);
                    nodes.Add(node);
                }
            }

            // The library nodes of this assembly (the Navisworks ones need Navisworks to load).
            foreach (var definition in registry.Definitions.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                var node = registry.CreateZeroTouchNode(definition.Id);
                if (node != null)
                {
                    kinds.Add(definition.Id);
                    nodes.Add(node);
                }
            }

            for (var i = 0; i < nodes.Count; i++)
            {
                nodes[i].X = 40 + (i % 8) * 340;
                nodes[i].Y = 40 + (i / 8) * 380;
                nodes[i].Ui.Collapsed = collapsed;
                vm.Graph.AddNode(nodes[i]);
            }

            var control = new CamelGraphEditorControl { ViewModel = vm };
            window = new Window
            {
                Width = 1600,
                Height = 1000,
                Content = control,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
            };
            window.Show();
        });
        StaHost.Flush();

        try
        {
            StaHost.Run(() =>
            {
                var views = vm!.Items.OfType<NodeViewModel>().ToList();
                Assert.True(kinds.Count > 50, "expected the whole library, found " + kinds.Count + " kinds");
                Assert.Equal(kinds.Count, views.Count);
            });

            Assert.True(StaHost.Unhandled.IsEmpty, "A node failed to render:\n" + string.Join("\n---\n", StaHost.Unhandled));
        }
        finally
        {
            StaHost.Run(() => window!.Close());
        }
    }
}
