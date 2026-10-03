using Xunit;
using static Dyncamelo.Nodes.Tests.NavisworksSourceText;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// The Navisworks node project cannot be loaded by the Linux test projects, so this reads the source of Clash.GroupResults and pins
/// that it no longer searches the whole clash tree for every result it moves (the unit tests of the planner and the runner are what
/// show the new way is right).
/// </summary>
public class ClashGroupingSourceTests
{
    [Fact]
    public void GroupResultsReadsTheTreeOnceInsteadOfScanningItForEveryResult()
    {
        var source = Source("ClashTriageNodes.cs");

        Assert.Contains("ClashGroupRunner.Run(", source);
        Assert.Contains("new StoredClashTreeEditor(", source);
        Assert.DoesNotContain("TryLocate(", source);
        Assert.DoesNotContain("ResultRef", source);

        // The editor addresses results by index and checks the child at that index before it moves it.
        var editor = Source("Internal", "StoredClashTreeEditor.cs");
        Assert.Contains("key.Matches(", editor);
        Assert.Contains("TestsMove(parent, index, target, target.Children.Count)", editor);
    }
}
