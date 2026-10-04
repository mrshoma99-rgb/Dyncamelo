using System;
using System.IO;

namespace CamelGraph.TestSupport.StandIns;

/// <summary>Finds the repository the tests run from (the folder that holds <c>CamelGraph.sln</c>).</summary>
internal static class RepoLocator
{
    /// <summary>The repository root, found by walking up from the test assembly's folder.</summary>
    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CamelGraph.sln")))
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            throw new DirectoryNotFoundException("CamelGraph.sln was not found above " + AppContext.BaseDirectory);
        }

        return dir.FullName;
    }

    /// <summary>The generated node catalogue, <c>docs/camelgraph-nodes.json</c>.</summary>
    public static string CataloguePath() => Path.Combine(Root(), "docs", "camelgraph-nodes.json");

    /// <summary>The Navisworks node sources, <c>src/CamelGraph.Navisworks</c>.</summary>
    public static string NavisworksSourceDirectory() => Path.Combine(Root(), "src", "CamelGraph.Navisworks");
}
