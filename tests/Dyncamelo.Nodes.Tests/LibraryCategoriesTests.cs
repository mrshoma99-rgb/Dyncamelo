using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// The library lists a category for every node. The loader turns any public static method of any public class into a node named
/// after the class, so a hand-written node that keeps a public static helper made a "WatchTableNode" category appear in the
/// library (seen in the first editor screenshot taken by CI). These checks keep such classes out of the category list.
/// </summary>
public class LibraryCategoriesTests
{
    private static NodeRegistry CreateRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    [Fact]
    public void NoCategoryIsTheNameOfAHandWrittenNodeClass()
    {
        var classNames = new HashSet<string>(
            typeof(NodeLibrary).Assembly.GetExportedTypes().Where(t => typeof(NodeModel).IsAssignableFrom(t)).Select(t => t.Name),
            StringComparer.Ordinal);

        var strays = CreateRegistry().Definitions
            .Where(d => !d.IsDeprecated && classNames.Contains(d.Category))
            .Select(d => d.Name + " (category " + d.Category + ")")
            .ToList();

        Assert.True(
            strays.Count == 0,
            "A public static method of a hand-written node class is listed in the library; mark it [IsVisibleInLibrary(false)] or make it internal: " +
            string.Join(", ", strays));
    }

    [Fact]
    public void EveryCategoryIsAReadableName()
    {
        var registry = CreateRegistry();
        var categories = registry.Definitions.Where(d => !d.IsDeprecated).Select(d => d.Category)
            .Concat(registry.NodeTypes.Select(t => registry.CreateNode(t)).Where(n => n != null && n.ShowInLibrary).Select(n => n!.Category))
            .Distinct()
            .ToList();

        Assert.DoesNotContain(categories, c => string.IsNullOrWhiteSpace(c));
        Assert.DoesNotContain(categories, c => c.EndsWith("Node", StringComparison.Ordinal));
    }
}
