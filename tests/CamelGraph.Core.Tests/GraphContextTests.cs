using System;
using System.IO;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Files;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Player;
using CamelGraph.Core.Serialization;
using CamelGraph.Core.Loader;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>A node that reports the graph folder the host set for the run.</summary>
public sealed class GraphFolderProbeNode : NodeModel
{
    public GraphFolderProbeNode()
    {
        Name = "Graph Folder";
        AddOutput("folder", typeof(string));
    }

    public override string NodeType => "TestGraphFolderProbe";

    public override NodeFunction Function => NodeFunction.Info;

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new object?[] { GraphContext.Folder ?? "(none)" };
}

/// <summary>
/// Relative paths in file nodes mean "next to the graph" (SYS-04 and the rest): the host names the folder, the resolver uses it.
/// Several tests set the process-wide folder, so they share a collection and do not run at the same time.
/// </summary>
[Collection("GraphContext")]
public class GraphContextTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dyc-context-" + Guid.NewGuid().ToString("N"));
    private readonly string? _before = GraphContext.Folder;

    public GraphContextTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        GraphContext.Folder = _before;
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    // ----- PathResolver -------------------------------------------------------------------------

    [Theory]
    [InlineData("\"{0}\"")]
    [InlineData("  \"{0}\"  ")]
    [InlineData("'{0}'")]
    [InlineData("“{0}”")]
    [InlineData("  {0}  ")]
    public void QuotesAndSpacesFromExplorerAreTrimmed(string pattern)
    {
        var file = Path.Combine(_root, "a b.txt");

        Assert.Equal(file, PathResolver.Resolve(string.Format(pattern, file)));
    }

    [Fact]
    public void AnAbsolutePathIsReturnedAsTyped()
    {
        GraphContext.Folder = Path.Combine(_root, "elsewhere");
        var file = Path.Combine(_root, "x", "..", "y.txt");

        Assert.Equal(file, PathResolver.Resolve(file));
    }

    [Fact]
    public void ARelativePathMeansNextToTheGraph()
    {
        GraphContext.Folder = _root;

        Assert.Equal(Path.Combine(_root, "report.csv"), PathResolver.Resolve("report.csv"));
        Assert.Equal(Path.Combine(_root, "out", "report.csv"), PathResolver.Resolve("\"out" + Path.DirectorySeparatorChar + "report.csv\""));
        Assert.Equal(Path.Combine(_root, "data.xlsx"), PathResolver.Resolve(".." + Path.DirectorySeparatorChar + Path.GetFileName(_root) + Path.DirectorySeparatorChar + "data.xlsx"));
    }

    [Fact]
    public void WithNoGraphFolderARelativePathFallsBackToTheProcessDirectory()
    {
        GraphContext.Folder = null;

        Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "report.csv"), PathResolver.Resolve("report.csv"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData("\t", "\t")]
    public void ABlankPathComesBackUnchangedAndNeverThrows(string? input, string expected)
    {
        GraphContext.Folder = _root;

        Assert.Equal(expected, PathResolver.Resolve(input));
    }

    [Fact]
    public void AnEmptyPairOfQuotesIsBlankToo()
    {
        Assert.Equal(string.Empty, PathResolver.Resolve("\"\""));
        Assert.Equal(string.Empty, PathResolver.Resolve(" \"  \" "));
    }

    [Fact]
    public void APathItCannotMakeSenseOfIsReturnedCleanedNotThrown()
    {
        GraphContext.Folder = _root;

        var result = PathResolver.Resolve("\"bad\0name.txt\"");

        Assert.False(string.IsNullOrEmpty(result));
        Assert.Contains("name.txt", result);
    }

    [Fact]
    public void TheFolderCanBeGivenExplicitly()
    {
        GraphContext.Folder = Path.Combine(_root, "ignored");

        Assert.Equal(Path.Combine(_root, "a.txt"), PathResolver.Resolve("a.txt", _root));
    }

    [Fact]
    public void CleanOnlyTrimsAndLeavesRelativePathsAlone()
    {
        Assert.Equal("a.txt", PathResolver.Clean(" \"a.txt\" "));
        Assert.Equal(string.Empty, PathResolver.Clean(null));
        Assert.Equal("it's", PathResolver.Clean("it's"));
    }

    // ----- GraphContext -------------------------------------------------------------------------

    [Fact]
    public void UseSetsTheFolderForAScopeAndPutsTheOldOneBack()
    {
        GraphContext.Folder = "outer";

        using (GraphContext.Use("inner"))
        {
            Assert.Equal("inner", GraphContext.Folder);
            using (GraphContext.Use(null))
            {
                Assert.Null(GraphContext.Folder);
            }

            Assert.Equal("inner", GraphContext.Folder);
        }

        Assert.Equal("outer", GraphContext.Folder);
    }

    [Fact]
    public void ABlankFolderMeansNone()
    {
        GraphContext.Folder = "  ";

        Assert.Null(GraphContext.Folder);
    }

    [Fact]
    public void TheFolderOfAGraphFileIsItsDirectoryAndAnUnsavedGraphUsesDocumentsCamelGraph()
    {
        Assert.Equal(_root, GraphContext.FolderFor(Path.Combine(_root, "plan.dyc")));
        Assert.Equal(GraphContext.DefaultFolder, GraphContext.FolderFor(null));
        Assert.Equal(GraphContext.DefaultFolder, GraphContext.FolderFor("  "));
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Assert.Equal(
            string.IsNullOrEmpty(documents) ? Directory.GetCurrentDirectory() : Path.Combine(documents, "CamelGraph"),
            GraphContext.DefaultFolder);
    }

    // ----- the hosts ----------------------------------------------------------------------------

    [Fact]
    public void ThePlayerRunsAScriptWithItsOwnFolderAsTheGraphFolder()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterNodeType("TestGraphFolderProbe", () => new GraphFolderProbeNode());
        var graph = new GraphModel();
        var probe = new GraphFolderProbeNode { PlayerExposed = true };
        graph.AddNode(probe);
        var scripts = Path.Combine(_root, "scripts");
        Directory.CreateDirectory(scripts);
        var path = Path.Combine(scripts, "probe.dyc");
        new GraphSerializer(registry).SaveToFile(graph, path);
        GraphContext.Folder = "before";

        var session = ScriptSession.Load(path, registry);
        var result = session.Run(new EvaluationContext());

        Assert.Equal(scripts, Assert.Single(result.Outputs).Text);
        Assert.Equal("before", GraphContext.Folder);
    }
}
