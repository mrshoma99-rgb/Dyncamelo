using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dyncamelo.Core.Loader;
using Dyncamelo.Nodes;
using Dyncamelo.UI.Services;
using Dyncamelo.UI.ViewModels;
using Dyncamelo.UI.Views;
using Xunit;

namespace Dyncamelo.UI.Tests;

/// <summary>
/// An entry of a <see cref="ResourceDictionary"/> is built when it is first looked up, not when the XAML is loaded. One that cannot
/// be built — the node library's selection brushes were a <c>Binding</c> with a <c>DynamicResource</c> source, which WPF rejects —
/// therefore stays invisible until the control that asks for it is used, and then ends the host process (in Navisworks: when an item
/// of the node library was selected). These tests build every entry up front.
/// </summary>
public class ResourceRealisationTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dyncamelo.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void NoXamlGivesABindingPropertyAMarkupExtensionThatNeedsADependencyProperty()
    {
        // Binding, MultiBinding and PriorityBinding are not DependencyObjects: Source, Converter, FallbackValue ... cannot be a
        // DynamicResource, and WPF only says so when the value is built.
        var bad = new List<string>();
        var root = RepoRoot();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "src"), "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\{\s*(?:Binding|MultiBinding|PriorityBinding)\b[^}]*\{\s*DynamicResource\b[^}]*\}"))
            {
                bad.Add(Path.GetFileName(file) + ": " + m.Value);
            }
        }

        Assert.True(bad.Count == 0, "A binding cannot take a DynamicResource (use StaticResource, or set the value in code):\n" + string.Join("\n", bad));
    }

    [Fact]
    public void EveryResourceOfTheEditorAndThePlayerCanBeBuilt()
    {
        while (StaHost.Unhandled.TryDequeue(out _))
        {
        }

        Window? window = null;
        Window? playerWindow = null;
        DyncameloEditorControl? editor = null;
        PlayerControl? player = null;
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            NodeLibrary.RegisterAll(registry);
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            var vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
            editor = new DyncameloEditorControl { ViewModel = vm };
            window = new Window { Width = 1400, Height = 900, Content = editor, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
            window.Show();

            player = new PlayerControl();
            playerWindow = new Window { Width = 420, Height = 700, Content = player, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
            playerWindow.Show();
        });
        StaHost.Flush();

        try
        {
            var errors = new List<string>();
            var realised = 0;
            StaHost.Run(() =>
            {
                realised += BuildAll(editor!, "editor", errors);
                realised += BuildAll(player!, "player", errors);
            });

            Assert.True(realised > 100, "expected the theme's resources to be found, built " + realised);
            Assert.True(errors.Count == 0, "Resources that cannot be built:\n" + string.Join("\n", errors));
            Assert.True(StaHost.Unhandled.IsEmpty, "An exception reached the dispatcher:\n" + string.Join("\n---\n", StaHost.Unhandled));
        }
        finally
        {
            StaHost.Run(() =>
            {
                window!.Close();
                playerWindow!.Close();
            });
        }
    }

    [Fact]
    public void SelectingAnItemInTheNodeLibraryRaisesNoError()
    {
        while (StaHost.Unhandled.TryDequeue(out _))
        {
        }

        Window? window = null;
        GraphEditorViewModel? vm = null;
        StaHost.Run(() =>
        {
            var registry = NodeRegistry.CreateDefault();
            NodeLibrary.RegisterAll(registry);
            var settings = new UiSettingsService(Path.Combine(Path.GetTempPath(), "dyc-ui-tests-" + Guid.NewGuid().ToString("N") + ".json"));
            vm = new GraphEditorViewModel(registry, new StubDialogs(), settings);
            window = new Window
            {
                Width = 1400,
                Height = 900,
                Content = new DyncameloEditorControl { ViewModel = vm },
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
                var categories = vm!.Library.RootItems.OfType<LibraryCategoryViewModel>().ToList();
                Assert.True(categories.Count > 3, "expected the library to list categories");

                // Select one category after another (expanding it), the way clicking in the panel does.
                foreach (var category in categories.Take(6))
                {
                    category.IsExpanded = true;
                    category.IsSelected = true;
                }
            });
            StaHost.Flush();
            StaHost.Flush();

            Assert.True(StaHost.Unhandled.IsEmpty, "Selecting in the library failed:\n" + string.Join("\n---\n", StaHost.Unhandled));
        }
        finally
        {
            StaHost.Run(() => window!.Close());
        }
    }

    // Looks up (and so builds) every entry of every resource dictionary reachable from the element: its own and its descendants'
    // (logical and visual), the dictionaries merged into those, and the resources of the styles and templates they hold.
    private static int BuildAll(FrameworkElement root, string name, List<string> errors)
    {
        var dictionaries = new List<ResourceDictionary>();
        var seenDictionaries = new HashSet<ResourceDictionary>();
        var seenObjects = new HashSet<DependencyObject>();
        Walk(root, dictionaries, seenDictionaries, seenObjects);

        var built = 0;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var dictionary = dictionaries[i];
            foreach (var key in dictionary.Keys.Cast<object>().ToList())
            {
                try
                {
                    var value = dictionary[key];
                    built++;
                    Inspect(value, dictionaries, seenDictionaries);
                }
                catch (Exception ex)
                {
                    errors.Add(name + " resource '" + key + "': " + ex.GetType().Name + ": " + ex.GetBaseException().Message);
                }
            }
        }

        return built;
    }

    private static void Walk(DependencyObject element, List<ResourceDictionary> dictionaries, HashSet<ResourceDictionary> seen, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element))
        {
            return;
        }

        if (element is FrameworkElement fe)
        {
            Collect(fe.Resources, dictionaries, seen);
            if (fe.ContextMenu != null)
            {
                Walk(fe.ContextMenu, dictionaries, seen, visited);
            }
        }
        else if (element is FrameworkContentElement fce)
        {
            Collect(fce.Resources, dictionaries, seen);
        }

        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            Walk(child, dictionaries, seen, visited);
        }

        if (element is Visual || element is System.Windows.Media.Media3D.Visual3D)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                Walk(VisualTreeHelper.GetChild(element, i), dictionaries, seen, visited);
            }
        }
    }

    private static void Collect(ResourceDictionary? dictionary, List<ResourceDictionary> dictionaries, HashSet<ResourceDictionary> seen)
    {
        if (dictionary == null || !seen.Add(dictionary))
        {
            return;
        }

        dictionaries.Add(dictionary);
        foreach (var merged in dictionary.MergedDictionaries)
        {
            Collect(merged, dictionaries, seen);
        }
    }

    private static void Inspect(object? value, List<ResourceDictionary> dictionaries, HashSet<ResourceDictionary> seen)
    {
        switch (value)
        {
            case Style style:
                Collect(style.Resources, dictionaries, seen);
                break;
            case FrameworkTemplate template:
                Collect(template.Resources, dictionaries, seen);
                break;
        }
    }
}
