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
    /// <summary>
    /// Like <c>Body</c> but tolerates preprocessor lines (#if NAV2026) at the start of a line: the text from the signature to
    /// the closing brace of the method (a line of four spaces and a brace).
    /// </summary>
    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, System.StringComparison.Ordinal);
        Assert.True(start >= 0, "method not found: " + signature);
        var end = source.IndexOf("\n    }\n", start, System.StringComparison.Ordinal);
        Assert.True(end > start, "no closing brace after: " + signature);
        return source.Substring(start, end - start + 7);
    }

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

    // ----------------------------------------------------------------------------------------------- NVC-05, NVC-21

    [Theory]
    [InlineData("SetStatus", "ClashResult.SetStatus")]
    [InlineData("Assign", "ClashResult.Assign")]
    [InlineData("SetDescription", "ClashResult.SetDescription")]
    public void TheResultSettersTakeAResultOrAGroupOrAWholeListAndEditThemInOneTransaction(string method, string nodeName)
    {
        var nodes = Source("ClashNodes.cs");
        var signature = "public static object? " + method + "(";
        var body = Method(nodes, signature);

        // The port takes any clash result, a group included; the typed ClashResult input is gone.
        Assert.Contains("[ScalarInput][PortKinds(\"clash\")] object result", body);
        Assert.DoesNotMatch(@"\bClashResult result\b", body);
        Assert.Contains("ClashHelpers.EditResults(", body);

        // The node still lets old graphs load (the id carried ClashResult) and is tagged as a model change.
        var header = nodes.Substring(nodes.LastIndexOf("[NodeName(\"" + nodeName + "\")]", System.StringComparison.Ordinal));
        header = header.Substring(0, header.IndexOf(signature, System.StringComparison.Ordinal));
        Assert.Contains("[NodeAliases(\"CamelGraph.Navisworks.ClashNodes." + method + "@Autodesk.Navisworks.Api.Clash.ClashResult,string,Autodesk.Navisworks.Api.Document\")]", header);
        Assert.Contains("NodeEffects.ChangesModel", header);
        Assert.Contains("List Levels and @L2", header);
        Assert.Contains("result group", header);
    }

    [Fact]
    public void AListHandedOverWholeIsOneUndoStep()
    {
        var edit = Method(Source("Internal", "ClashHelpers.cs"), "internal static object? EditResults(");

        Assert.Contains("ClashInputs.Flatten<IClashResult>(input)", edit);
        Assert.Contains("doc.BeginTransaction(undoLabel)", edit);
        Assert.Contains("transaction.Commit()", edit);
        Assert.Contains("StaleInputError(", edit);
        Assert.Contains("return input;", edit);
    }

    // ----------------------------------------------------------------------------------------------- NVC-06

    [Theory]
    [InlineData("ClashEditNodes.cs", "public static ClashTest Rename(")]
    [InlineData("ClashTriageNodes.cs", "public static Dictionary<string, object?> Groups(")]
    [InlineData("ClashTriageNodes.cs", "public static Dictionary<string, object?> GroupByName(")]
    public void TheTestOrNameInputsRunOncePerElementOfAList(string file, string signature)
    {
        Assert.Contains("([ScalarInput] object test", Body(Source(file), signature));
    }

    [Fact]
    public void TheDescriptionsNoLongerSendPeopleToListLevelGymnastics()
    {
        var triage = Source("ClashTriageNodes.cs");
        Assert.DoesNotContain("needs List@Level", triage);
        Assert.DoesNotContain("List@Level gymnastics", triage);
    }

    // ----------------------------------------------------------------------------------------------- NVC-30, NVC-18

    [Fact]
    public void TheAngleFilterRangeStopsAtNinetyBecauseTheAngleIsUndirected()
    {
        var body = Body(Source("ClashNodes.cs"), "public static List<ClashResult> FilterByAngle(");

        Assert.Contains("[NodeRange(0, 90, Unit = \"°\")] double minDegrees", body);
        Assert.Contains("[NodeRange(0, 90, Unit = \"°\")] double maxDegrees", body);
        Assert.DoesNotContain("180", body);
    }

    [Fact]
    public void TheResultInfoAssigneeIsTextOnEveryNavisworksYear()
    {
        var info = Body(Source("ClashNodes.cs"), "public static Dictionary<string, object?> ResultInfo(");
        Assert.Contains("[\"assignedTo\"] = ClashHelpers.AssigneeText(clashResult)", info);

        var helper = Method(Source("Internal", "ClashHelpers.cs"), "internal static string AssigneeText(");
        Assert.Contains("#if NAV2026", helper);
        Assert.Contains("AssignedTo?.ToString()", helper);
    }
}
