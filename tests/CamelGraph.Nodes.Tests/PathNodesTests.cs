using System;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using Xunit;
using GraphFunction = CamelGraph.Core.Graph.NodeFunction;

namespace CamelGraph.Nodes.Tests;

/// <summary>Path.* nodes: pure text manipulation of paths, never touching the disk.</summary>
[Xunit.Collection("GraphContext")]
public class PathNodesTests
{
    private static readonly char Sep = System.IO.Path.DirectorySeparatorChar;

    /// <summary>Joins parts with this OS's separator (no Path.Combine: it would drop earlier parts after a rooted one).</summary>
    private static string P(params string[] parts) => string.Join(Sep.ToString(), parts);

    /// <summary>An absolute path made of the given parts under the current drive's root.</summary>
    private static string Abs(params string[] parts) => Root + P(parts);

    /// <summary>The root of the current drive: "/" on Linux, "C:\" on Windows.</summary>
    private static string Root => System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(".")) ?? string.Empty;

    // ------------------------------------------------------- file name parts

    [Fact]
    public void GetFileName_ReturnsNameWithExtension()
    {
        Assert.Equal("site.nwd", PathNodes.GetFileName(P("models", "level1", "site.nwd")));
        Assert.Equal("site.nwd", PathNodes.GetFileName("site.nwd"));
    }

    [Fact]
    public void GetFileName_IsEmptyForPathEndingWithSeparator()
    {
        Assert.Equal(string.Empty, PathNodes.GetFileName(P("models", "level1") + Sep));
    }

    [Fact]
    public void GetFileName_DoesNotRequireThePathToExist()
    {
        var missing = Abs("camelgraph-does-not-exist-" + Guid.NewGuid().ToString("N"), "ghost.nwd");
        Assert.Equal("ghost.nwd", PathNodes.GetFileName(missing));
    }

    [Theory]
    [InlineData("site.nwd", "site")]
    [InlineData("archive.tar.gz", "archive.tar")]
    [InlineData("README", "README")]
    [InlineData(".gitignore", "")]
    public void GetFileNameWithoutExtension_DropsOnlyTheLastExtension(string fileName, string expected)
    {
        Assert.Equal(expected, PathNodes.GetFileNameWithoutExtension(P("some", "folder", fileName)));
    }

    [Theory]
    [InlineData("site.nwd", ".nwd")]
    [InlineData("archive.tar.gz", ".gz")]
    [InlineData("README", "")]
    [InlineData("trailing.", "")]
    public void GetExtension_IncludesTheDot_AndIsEmptyWhenMissing(string fileName, string expected)
    {
        Assert.Equal(expected, PathNodes.GetExtension(P("some", "folder", fileName)));
    }

    [Fact]
    public void GetExtension_IgnoresDotsInFolderNames()
    {
        Assert.Equal(string.Empty, PathNodes.GetExtension(P("project.v2", "README")));
    }

    [Fact]
    public void GetDirectory_ReturnsTheFolderPart()
    {
        Assert.Equal(P("models", "level1"), PathNodes.GetDirectory(P("models", "level1", "site.nwd")));
    }

    [Fact]
    public void GetDirectory_IsEmptyForBareFileNameAndForARoot()
    {
        Assert.Equal(string.Empty, PathNodes.GetDirectory("site.nwd"));
        Assert.Equal(string.Empty, PathNodes.GetDirectory(Root));
    }

    // ------------------------------------------------------- ChangeExtension

    [Theory]
    [InlineData(".nwf")]
    [InlineData("nwf")]
    [InlineData(" .nwf ")]
    public void ChangeExtension_AcceptsTheExtensionWithOrWithoutDot(string extension)
    {
        Assert.Equal(P("a", "site.nwf"), PathNodes.ChangeExtension(P("a", "site.nwd"), extension));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    public void ChangeExtension_EmptyRemovesTheExtension(string extension)
    {
        Assert.Equal(P("a", "site"), PathNodes.ChangeExtension(P("a", "site.nwd"), extension));
    }

    [Fact]
    public void ChangeExtension_AddsAnExtensionToAFileWithoutOne()
    {
        Assert.Equal(P("a", "README.txt"), PathNodes.ChangeExtension(P("a", "README"), "txt"));
    }

    [Fact]
    public void ChangeExtension_OnlyChangesTheLastExtension_AndLeavesFolderDotsAlone()
    {
        Assert.Equal(P("v1.0", "pack.tar.zip"), PathNodes.ChangeExtension(P("v1.0", "pack.tar.gz"), ".zip"));
    }

    [Fact]
    public void ChangeExtension_NullExtension_ThrowsNamingTheInput()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => PathNodes.ChangeExtension("a.nwd", null!));
        Assert.Contains("Path.ChangeExtension", ex.Message);
        Assert.Contains("'extension'", ex.Message);
    }

    // ------------------------------------------------------------ GetFullPath

    [Fact]
    public void GetFullPath_ResolvesRelativePathsAgainstTheCurrentFolder()
    {
        var expected = System.IO.Path.GetFullPath(P("sub", "..", "file.txt"));
        Assert.Equal(expected, PathNodes.GetFullPath(P("sub", "..", "file.txt")));
        Assert.EndsWith(Sep + "file.txt", expected);
    }

    [Fact]
    public void GetFullPath_KeepsAnAbsolutePath_AndNeverRequiresItToExist()
    {
        var ghost = "camelgraph-ghost-" + Guid.NewGuid().ToString("N");
        Assert.Equal(Abs(ghost, "b.txt"), PathNodes.GetFullPath(Abs(ghost, "a", "..", "b.txt")));
    }

    // -------------------------------------------------------- GetRelativePath

    [Fact]
    public void GetRelativePath_ChildOfBase()
    {
        var baseDir = Abs("work", "project");
        Assert.Equal(P("models", "a.nwd"), PathNodes.GetRelativePath(P(baseDir, "models", "a.nwd"), baseDir));
    }

    [Fact]
    public void GetRelativePath_SiblingFolderGoesUpFirst()
    {
        var baseDir = Abs("work", "project", "models");
        var target = Abs("work", "project", "reports", "r.csv");
        Assert.Equal(P("..", "reports", "r.csv"), PathNodes.GetRelativePath(target, baseDir));
    }

    [Fact]
    public void GetRelativePath_ParentAndSameFolder()
    {
        var baseDir = Abs("work", "project");
        Assert.Equal("..", PathNodes.GetRelativePath(Abs("work"), baseDir));
        Assert.Equal(".", PathNodes.GetRelativePath(baseDir, baseDir));
        Assert.Equal(".", PathNodes.GetRelativePath(baseDir + Sep, baseDir));
    }

    [Fact]
    public void GetRelativePath_TrailingSeparatorsAndDotSegmentsDoNotMatter()
    {
        var baseDir = Abs("work", "project") + Sep;
        var target = Abs("work", "other", "..", "project", "x.txt");
        Assert.Equal("x.txt", PathNodes.GetRelativePath(target, baseDir));
    }

    [Fact]
    public void GetRelativePath_SegmentsWithSameStartAreNotTreatedAsParents()
    {
        // "project2" must not be seen as inside "project".
        var baseDir = Abs("work", "project");
        Assert.Equal(P("..", "project2", "a.txt"), PathNodes.GetRelativePath(Abs("work", "project2", "a.txt"), baseDir));
    }

    [Fact]
    public void GetRelativePath_RelativeInputsUseTheCurrentFolder()
    {
        Assert.Equal(P("sub", "a.txt"), PathNodes.GetRelativePath(P("sub", "a.txt"), "."));
    }

    [Fact]
    public void GetRelativePath_DifferentRoots_ReturnsTheFullPath()
    {
        if (Sep == '\\')
        {
            Assert.Equal(@"D:\data\a.nwd", PathNodes.GetRelativePath(@"D:\data\a.nwd", @"C:\work"));
            Assert.Equal(@"\\server\share\a.nwd", PathNodes.GetRelativePath(@"\\server\share\a.nwd", @"C:\work"));
            Assert.Equal(@"..\DATA\a.nwd", PathNodes.GetRelativePath(@"c:\DATA\a.nwd", @"C:\work")); // the drive letter ignores case
        }
        else
        {
            // Unix has a single root, so every pair of absolute paths shares it: the result is always relative.
            Assert.Equal(P("..", "data", "a.nwd"), PathNodes.GetRelativePath("/data/a.nwd", "/work"));
        }
    }

    [Fact]
    public void GetRelativePath_BlankInputs_ThrowNamingTheInput()
    {
        Assert.Contains("'path'", Assert.Throws<ArgumentException>(() => PathNodes.GetRelativePath(" ", "x")).Message);
        Assert.Contains("'baseDirectory'", Assert.Throws<ArgumentException>(() => PathNodes.GetRelativePath("x", null!)).Message);
    }

    // ------------------------------------------------------------- IsAbsolute

    [Fact]
    public void IsAbsolute_TrueForARootedPath_FalseForRelativeOnes()
    {
        Assert.True(PathNodes.IsAbsolute(System.IO.Path.GetFullPath(P("some", "file.txt"))));
        Assert.True(PathNodes.IsAbsolute(Root));
        Assert.False(PathNodes.IsAbsolute(P("some", "file.txt")));
        Assert.False(PathNodes.IsAbsolute("file.txt"));
        Assert.False(PathNodes.IsAbsolute(P("..", "file.txt")));
        Assert.False(PathNodes.IsAbsolute("."));
    }

    [Fact]
    public void IsAbsolute_FollowsTheRulesOfTheCurrentOperatingSystem()
    {
        if (Sep == '\\')
        {
            Assert.True(PathNodes.IsAbsolute(@"C:\Models\a.nwd"));
            Assert.True(PathNodes.IsAbsolute("C:/Models/a.nwd"));
            Assert.True(PathNodes.IsAbsolute(@"\\server\share\a.nwd"));
            Assert.False(PathNodes.IsAbsolute(@"C:a.nwd")); // relative to the drive's current folder
            Assert.False(PathNodes.IsAbsolute(@"\Models\a.nwd")); // relative to the current drive
        }
        else
        {
            Assert.True(PathNodes.IsAbsolute("/home/user/a.nwd"));
            Assert.False(PathNodes.IsAbsolute(@"C:\Models\a.nwd")); // not a path on this system
        }
    }

    // -------------------------------------------------------------- Normalize

    [Fact]
    public void Normalize_UsesOneSeparatorStyle_AndCollapsesRepeats()
    {
        Assert.Equal(P("a", "b", "c"), PathNodes.Normalize(@"a/b\c"));
        Assert.Equal(P("a", "b"), PathNodes.Normalize("a//b"));
    }

    [Fact]
    public void Normalize_TrimsTrailingSeparators()
    {
        Assert.Equal(P("a", "b"), PathNodes.Normalize("a/b/"));
        Assert.Equal(P("a", "b"), PathNodes.Normalize(@"a\b\\"));
    }

    [Fact]
    public void Normalize_ResolvesDotAndDotDotTextually()
    {
        Assert.Equal(P("a", "c"), PathNodes.Normalize("a/./b/../c"));
        Assert.Equal(".", PathNodes.Normalize("a/.."));
        Assert.Equal(".", PathNodes.Normalize("./"));
        Assert.Equal(P("..", "a"), PathNodes.Normalize("../a"));
        Assert.Equal("..", PathNodes.Normalize("a/b/../../.."));
        Assert.Equal(P("..", "..", "x"), PathNodes.Normalize("../../x"));
    }

    [Fact]
    public void Normalize_NeverTouchesTheDisk_SoMissingFoldersAreFine()
    {
        Assert.Equal(P("no", "such", "place", "file.txt"), PathNodes.Normalize("no/such/other/../place/file.txt"));
    }

    [Fact]
    public void Normalize_RootedPaths_KeepTheRoot_AndCannotClimbAboveIt()
    {
        Assert.Equal(Root + "b", PathNodes.Normalize(Root + "a/../../b"));
        Assert.Equal(Root, PathNodes.Normalize(Root));
        Assert.Equal(Root, PathNodes.Normalize(Root + "a/.."));
        Assert.Equal(Root + "a", PathNodes.Normalize(Root.Replace('\\', '/') + "a/"));
    }

    [Fact]
    public void Normalize_DriveAndNetworkRoots_OnWindows()
    {
        if (Sep != '\\')
        {
            // Unix: a double slash at the start is just a redundant separator.
            Assert.Equal("/a", PathNodes.Normalize("//a"));
            return;
        }

        Assert.Equal(@"C:\a\c", PathNodes.Normalize("C:/a/./b/../c/"));
        Assert.Equal(@"C:\", PathNodes.Normalize(@"C:\"));
        Assert.Equal(@"C:\b", PathNodes.Normalize(@"C:\a\..\..\b"));
        Assert.Equal(@"C:..\b", PathNodes.Normalize(@"C:..\b")); // drive-relative: ".." must survive
        Assert.Equal(@"\\server\share\a", PathNodes.Normalize(@"\\server\share\x\..\a\"));
        Assert.Equal(@"\\server\share\", PathNodes.Normalize(@"\\server\share\..\.."));
    }

    // ----------------------------------------------------------------- errors

    [Fact]
    public void EveryNode_RejectsNullAndBlankPaths_NamingItself()
    {
        var calls = new (string Node, Action<string> Call)[]
        {
            ("Path.GetFileName", p => PathNodes.GetFileName(p)),
            ("Path.GetFileNameWithoutExtension", p => PathNodes.GetFileNameWithoutExtension(p)),
            ("Path.GetExtension", p => PathNodes.GetExtension(p)),
            ("Path.GetDirectory", p => PathNodes.GetDirectory(p)),
            ("Path.ChangeExtension", p => PathNodes.ChangeExtension(p, ".x")),
            ("Path.GetFullPath", p => PathNodes.GetFullPath(p)),
            ("Path.GetRelativePath", p => PathNodes.GetRelativePath(p, "x")),
            ("Path.IsAbsolute", p => PathNodes.IsAbsolute(p)),
            ("Path.Normalize", p => PathNodes.Normalize(p)),
        };

        foreach (var (node, call) in calls)
        {
            foreach (var bad in new string?[] { null, string.Empty, "   " })
            {
                var ex = Assert.Throws<ArgumentException>(() => call(bad!));
                Assert.Contains(node, ex.Message);
                Assert.Contains("'path'", ex.Message);
            }
        }
    }

    // --------------------------------------------------------------- registry

    [Fact]
    public void Registered_WithExplicitRoles_InTheFileCategory()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        var expected = new[]
        {
            ("Path.GetFileName", GraphFunction.Create),
            ("Path.GetFileNameWithoutExtension", GraphFunction.Create),
            ("Path.GetExtension", GraphFunction.Create),
            ("Path.GetDirectory", GraphFunction.Create),
            ("Path.ChangeExtension", GraphFunction.Create),
            ("Path.GetFullPath", GraphFunction.Create),
            ("Path.GetRelativePath", GraphFunction.Create),
            ("Path.IsAbsolute", GraphFunction.Info),
            ("Path.Normalize", GraphFunction.Create),
        };

        foreach (var (name, role) in expected)
        {
            var definition = registry.Definitions.Single(d => d.Name == name);
            Assert.Equal("File", definition.Category);
            Assert.Equal(role, definition.Function);
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
        }
    }

    [Fact]
    public void Engine_RunsPathNodesInAChain()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var graph = new GraphModel();
        var path = new StringInputNode { Value = P("models", "site.nwd") };
        var extension = new StringInputNode { Value = "nwf" };
        var change = new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == "Path.ChangeExtension"));
        var name = new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == "Path.GetFileName"));
        foreach (var node in new NodeModel[] { path, extension, change, name })
        {
            graph.AddNode(node);
        }

        Assert.True(graph.Connect(path.OutPorts[0], change.InPorts[0]).Success);
        Assert.True(graph.Connect(extension.OutPorts[0], change.InPorts[1]).Success);
        Assert.True(graph.Connect(change.OutPorts[0], name.InPorts[0]).Success);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Executed, name.State);
        Assert.Equal("site.nwf", name.OutPorts[0].Value);
    }
}
