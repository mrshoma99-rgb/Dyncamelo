using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Dyncamelo.UI.Views;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>The start screen of an empty canvas: New, recent scripts, examples, version, update notice and the BIMCamel link.</summary>
public class StartScreenUiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dyc-start-" + Guid.NewGuid().ToString("N"));

    public StartScreenUiTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    private string Script(string name)
    {
        var path = Path.Combine(_root, name + ".dyc");
        File.WriteAllText(path, "{}");
        return path;
    }

    private static string SamplesFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dyncamelo.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "samples");
    }

    private GraphEditorViewModel NewEditor(int recent = 0)
    {
        var settings = new UiSettingsService(Path.Combine(_root, "settings-" + Guid.NewGuid().ToString("N") + ".json"));
        for (var i = 0; i < recent; i++)
        {
            settings.AddRecentFile(Script("script-" + i));
        }

        var vm = new GraphEditorViewModel(NodeRegistry.CreateDefault(), new ScriptedDialogs(), settings);
        vm.UrlOpener = _ => true;
        vm.SamplesDirectoryOverride = SamplesFolder();   // the examples of the repository, whatever folder the test host runs from
        vm.RefreshSampleGraphs();
        return vm;
    }

    [Fact]
    public void AnEmptyCanvasShowsTheStartScreenWithANewCardAndTheExamples()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();

            Assert.True(vm.IsStartScreenVisible);
            Assert.False(vm.IsEmptyHintOnlyVisible);
            Assert.True(vm.IsEmptyCanvasHintVisible);       // still the one switch the older hint used
            var newCard = Assert.Single(vm.StartCards);
            Assert.Equal("New", newCard.Kind);
            Assert.Equal("New script", newCard.Title);
            Assert.False(vm.HasRecentCards);
            Assert.True(vm.HasExampleCards, "the repository's samples folder should be found from the test run");
            Assert.Equal("Getting Started - Math and Watch", vm.ExampleCards[0].Title);   // the gentlest example first
            Assert.True(vm.ExampleCards.Count <= 6);
            Assert.All(vm.ExampleCards, c => Assert.Equal("Example", c.Kind));
        });
    }

    [Fact]
    public void TheNewestRecentScriptsGetCardsAfterTheNewCardAndTheStartScreenGoesWhenANodeIsAdded()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor(recent: 6);

            Assert.True(vm.HasRecentCards);
            Assert.Equal(5, vm.StartCards.Count);          // New + the four newest
            Assert.Equal("New", vm.StartCards[0].Kind);
            Assert.All(vm.StartCards.Skip(1), c =>
            {
                Assert.Equal("Recent", c.Kind);
                Assert.EndsWith(".dyc", c.ToolTip);         // the tooltip is the full path
                Assert.Equal(Path.GetFileName(_root), c.Subtitle);
            });
            Assert.Equal("script-5", vm.StartCards[1].Title);   // most recent first

            vm.Graph.AddNode(new SumNode());

            Assert.False(vm.IsStartScreenVisible);
            Assert.False(vm.IsEmptyCanvasHintVisible);
        });
    }

    [Fact]
    public void OpeningAScriptOrAnExampleFromACardRemovesTheStartScreen()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var example = vm.ExampleCards[0];
            Assert.True(example.Command.CanExecute(example.Parameter));

            example.Command.Execute(example.Parameter);

            Assert.True(vm.NodeCount > 0);
            Assert.False(vm.IsStartScreenVisible);
        });
    }

    [Fact]
    public void TheNewCardStartsAnEmptyScriptAndPutsTheCardsAwayUntilTheCanvasHasNodesAndIsEmptyAgain()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var newCard = vm.StartCards[0];

            newCard.Command.Execute(newCard.Parameter);

            Assert.Equal("Untitled", vm.Graph.Name);
            Assert.False(vm.IsStartScreenVisible);
            Assert.True(vm.IsEmptyHintOnlyVisible);          // the short hint stays so the empty canvas is not a blank
            Assert.True(vm.IsEmptyCanvasHintVisible);

            var node = new SumNode();
            vm.Graph.AddNode(node);
            Assert.False(vm.IsEmptyHintOnlyVisible);
            Assert.False(vm.IsEmptyCanvasHintVisible);

            vm.Graph.RemoveNode(node);                       // empty again: the cards come back
            Assert.True(vm.IsStartScreenVisible);
        });
    }

    [Fact]
    public void SwitchingTheHintsOffRemovesTheStartScreenToo()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();

            vm.ShowEmptyCanvasHints = false;

            Assert.False(vm.IsStartScreenVisible);
            Assert.False(vm.IsEmptyHintOnlyVisible);
        });
    }

    [Fact]
    public void ThePluginVersionIsShownAndAnUpdateNoticeAppearsOnlyWhenOneIsKnown()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            var opened = new List<string>();
            vm.UrlOpener = url =>
            {
                opened.Add(url);
                return true;
            };

            Assert.Matches(@"^v\d+\.\d+\.\d+$", vm.ProductVersionText);
            Assert.False(vm.HasUpdate);
            Assert.Equal(string.Empty, vm.UpdateText);
            Assert.False(vm.OpenUpdateCommand.CanExecute(null));

            vm.SetAvailableUpdate("99.1.0", "https://example.test/download");

            Assert.True(vm.HasUpdate);
            Assert.Contains("99.1.0", vm.UpdateText);
            Assert.Contains(vm.ProductVersionText.TrimStart('v'), vm.UpdateText);
            Assert.True(vm.OpenUpdateCommand.CanExecute(null));
            vm.OpenUpdateCommand.Execute(null);
            vm.OpenWebsiteCommand.Execute(null);
            Assert.Equal(new[] { "https://example.test/download", "https://www.bimcamel.com/plugins/dyncamelo" }, opened.ToArray());
        });
    }

    [Fact]
    public void AnAddressThatCannotBeOpenedIsSaidInTheStatusBar()
    {
        StaHost.Run(() =>
        {
            var vm = NewEditor();
            vm.UrlOpener = _ => false;

            vm.OpenWebsiteCommand.Execute(null);

            Assert.Contains("bimcamel.com", vm.StatusMessage);
        });
    }

    [Fact]
    public void TheWebsiteAndUpdateCommandsAreInTheCatalogueForTheMenuAndThePalette()
    {
        Assert.NotNull(Dyncamelo.Core.Editing.CommandCatalog.Find("help.website"));
        Assert.NotNull(Dyncamelo.Core.Editing.CommandCatalog.Find("help.update"));
        Assert.Equal("Help", Dyncamelo.Core.Editing.CommandCatalog.Find("help.website")!.Category);
    }

    [Fact]
    public void TheCardsAreOnScreenAndDisappearWhenANodeIsAdded()
    {
        Window? window = null;
        GraphEditorViewModel? vm = null;
        StaHost.Run(() =>
        {
            vm = NewEditor(recent: 2);
            vm.SetAvailableUpdate("99.1.0", "https://example.test/download");
            window = new Window
            {
                Width = 1000,
                Height = 700,
                Content = new DyncameloEditorControl { ViewModel = vm },
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
            };
            window.Show();
        });
        StaHost.Flush();
        StaHost.Flush();

        try
        {
            StaHost.Run(() =>
            {
                var cards = Descendants<Button>(window!).Where(b => b.DataContext is StartCardViewModel && b.IsVisible).ToList();
                Assert.Equal(vm!.StartCards.Count + vm.ExampleCards.Count, cards.Count);
                Assert.All(cards, c => Assert.True(c.ActualWidth > 100 && c.ActualHeight > 40, "a card has no size"));
                Assert.Contains(Descendants<TextBlock>(window!), t => t.IsVisible && t.Text == vm.ProductVersionText);
                Assert.Contains(Descendants<TextBlock>(window!), t => t.IsVisible && t.Text == vm.UpdateText);
                Assert.Contains(Descendants<Button>(window!), b => b.IsVisible && b.Content as string == "bimcamel.com ↗");

                vm.Graph.AddNode(new SumNode());
                window!.UpdateLayout();
                Assert.DoesNotContain(Descendants<Button>(window!), b => b.DataContext is StartCardViewModel && b.IsVisible);
            });
        }
        finally
        {
            StaHost.Run(() => window!.Close());
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var inner in Descendants<T>(child))
            {
                yield return inner;
            }
        }
    }
}
