using System.Text.RegularExpressions;
using Xunit;
using static CamelGraph.Nodes.Tests.NavisworksSourceText;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The Navisworks node project cannot be loaded by the Linux test projects, so the fixes of the clash audit (NVC-01...) that sit
/// in the Navisworks calls are pinned by reading the source of the nodes. The logic that can be tested without Navisworks lives
/// in <c>CamelGraph.Nodes.Coordination</c> and has its own tests.
/// </summary>
public class ClashAuditSourceTests
{
    // ----------------------------------------------------------------------------------------------- NVC-01

    [Fact]
    public void ARegroupFindsATestThatSitsInAClashDetectiveFolder()
    {
        var helpers = Source("Internal", "ClashHelpers.cs");
        var commit = Body(helpers, "internal static ClashTest CommitTestTree(");

        Assert.Contains("TryLocateTest(", commit);
        Assert.Contains("TestsReplaceWithCopy(parent, index, editedCopy)", commit);
        Assert.Contains("TestsReplaceWithCopy(index, editedCopy)", commit);
        Assert.Contains("inside a folder", commit);

        // The locator descends into folders (it is the generic, unit-tested SavedTreeLocator).
        var locate = Body(helpers, "internal static bool TryLocateTest(");
        Assert.Contains("SavedTreeLocator.TryFind<SavedItem>(", locate);
        Assert.DoesNotContain("IndexOfTest(", helpers);
    }

    // ----------------------------------------------------------------------------------------------- NVC-02

    [Fact]
    public void TheSetFilterMatchesModelItemsByIdentityNotByWrapperObject()
    {
        var filter = Source("ClashFilterNodes.cs");
        Assert.Contains("new HashSet<ModelItem>(resolved, ModelItemIdentityComparer.Instance)", filter);
        Assert.DoesNotMatch(@"new HashSet<ModelItem>\(\s*(resolved\s*)?\)", filter);

        // ClashResult.Focus de-duplicates the same way.
        var triage = Source("ClashTriageNodes.cs");
        Assert.DoesNotMatch(@"new HashSet<ModelItem>\(\)", triage);
    }
}
