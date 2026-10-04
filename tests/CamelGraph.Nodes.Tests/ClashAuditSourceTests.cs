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

    /// <summary>The attributes of a node: from its [NodeName("...")] line to the signature of the method.</summary>
    private static string Header(string file, string nodeName)
    {
        var source = Source(file);
        var start = source.IndexOf("[NodeName(\"" + nodeName + "\")]", System.StringComparison.Ordinal);
        Assert.True(start >= 0, nodeName + " not found in " + file);
        var end = source.IndexOf("    public static", start, System.StringComparison.Ordinal);
        Assert.True(end > start, "no method after " + nodeName);
        return source.Substring(start, end - start);
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

    // ----------------------------------------------------------------------------------------------- NVC-14

    [Theory]
    [InlineData("ClashNodes.cs", "ClashTest.Create")]
    [InlineData("ClashNodes.cs", "ClashTest.Run")]
    [InlineData("ClashNodes.cs", "Clash.RunAllTests")]
    [InlineData("ClashNodes.cs", "Clash.GroupResultsBySameItem")]
    [InlineData("ClashNodes.cs", "Clash.GroupResultsByProximity")]
    [InlineData("ClashNodes.cs", "Clash.GroupResultsByLevel")]
    [InlineData("ClashNodes.cs", "ClashResult.SetStatus")]
    [InlineData("ClashNodes.cs", "ClashResult.Assign")]
    [InlineData("ClashNodes.cs", "ClashResult.SetDescription")]
    [InlineData("ClashEditNodes.cs", "ClashTest.Rename")]
    [InlineData("ClashEditNodes.cs", "ClashResult.Rename")]
    [InlineData("ClashEditNodes.cs", "Clash.GroupResultsByStatus")]
    [InlineData("ClashEditNodes.cs", "Clash.GroupResultsByGridIntersection")]
    [InlineData("ClashTriageNodes.cs", "Clash.GroupResults")]
    [InlineData("ClashTriageNodes.cs", "ClashResult.Focus")]
    [InlineData("ClashTestMaintenanceNodes.cs", "ClashTest.Edit")]
    [InlineData("ClashTestMaintenanceNodes.cs", "ClashTest.Delete")]
    [InlineData("ClashTestMaintenanceNodes.cs", "ClashTest.Duplicate")]
    [InlineData("ClashTestMaintenanceNodes.cs", "ClashTest.ClearResults")]
    public void EveryClashNodeThatWritesToTheDocumentSaysSo(string file, string nodeName)
    {
        Assert.Contains("NodeEffects.ChangesModel", Header(file, nodeName));
    }

    [Theory]
    [InlineData("ClashNodes.cs", "Clash.GroupResultsBySameItem")]
    [InlineData("ClashNodes.cs", "Clash.GroupResultsByProximity")]
    [InlineData("ClashNodes.cs", "Clash.GroupResultsByLevel")]
    [InlineData("ClashEditNodes.cs", "Clash.GroupResultsByStatus")]
    [InlineData("ClashEditNodes.cs", "Clash.GroupResultsByGridIntersection")]
    [InlineData("ClashTriageNodes.cs", "Clash.GroupResults")]
    public void TheGroupingNodesAreModifyNodesSoTheScriptPlayerListsThem(string file, string nodeName)
    {
        // Without the attribute the loader guesses Info from the name "GroupResults...".
        Assert.Contains("NodeFunction.Modify", Header(file, nodeName));
    }

    [Theory]
    [InlineData("ClashNodes.cs", "ClashResult.SaveImage")]
    [InlineData("ClashDeltaNodes.cs", "Clash.SnapshotToFile")]
    public void TheClashFileWritersSayTheyWriteAFileAndPickASaveDialog(string file, string nodeName)
    {
        var source = Source(file);
        Assert.Contains("NodeEffects.WritesFiles", Header(file, nodeName));
        Assert.Contains("[NodePath(NodePathMode.Save", source);
        Assert.Contains("PathResolver.Resolve(filePath)", source);
    }

    // ----------------------------------------------------------------------------------------------- NVC-12

    [Theory]
    [InlineData("ClashNodes.cs", "Clash.Tests")]
    [InlineData("ClashNodes.cs", "ClashTest.ByName")]
    [InlineData("ClashTriageNodes.cs", "Clash.AllGroups")]
    [InlineData("ClashTriageNodes.cs", "ClashGroup.ByName")]
    [InlineData("ClashTriageNodes.cs", "ClashTest.Groups")]
    public void TheNodesThatHandOutLiveClashWrappersRunOnEveryRun(string file, string nodeName)
    {
        // The engine keeps a clean node's output; a regroup or replace disposes the wrappers inside it.
        Assert.Contains("[LiveState]", Header(file, nodeName));
    }

    // ----------------------------------------------------------------------------------------------- NVC-31 (units list), NVC-33, NVC-25

    [Fact]
    public void TheDepthFilterOffersEveryNavisworksUnitNotAShortenedList()
    {
        var body = Method(Source("ClashFilterNodes.cs"), "public static List<ClashResult> FilterByDepth(");

        Assert.Contains("[NodeChoicesFromEnum(typeof(Units), \"document\")]", body);
        Assert.DoesNotContain("\"Meters\", \"Millimeters\"", body);
    }

    [Fact]
    public void ThePropertyFilterOffersTheTabsAndPropertiesOfTheSelection()
    {
        var body = Method(Source("ClashFilterNodes.cs"), "public static List<ClashResult> FilterByItemProperty(");

        Assert.Contains("[NodeTabChoice(NodeDataSource.Selection, IncludeAncestors = true)] string category", body);
        Assert.Contains("[NodePropertyChoice(NodeDataSource.Selection, \"category\", IncludeAncestors = true)] string property", body);
    }

    [Fact]
    public void TheStatusNodesNoLongerPointAtTheRetiredResultsByStatus()
    {
        var header = Header("ClashTriageNodes.cs", "Clash.Status") + Header("ClashTriageNodes.cs", "Clash.Statuses");
        Assert.DoesNotContain("ResultsByStatus", header);
    }

    // ----------------------------------------------------------------------------------------------- NVC-15, NVC-17

    [Theory]
    [InlineData("ClashNodes.cs", "public static List<ClashResult> FilterByStatus(")]
    [InlineData("ClashNodes.cs", "public static List<ClashResult> FilterByAngle(")]
    [InlineData("ClashFilterNodes.cs", "public static List<ClashResult> FilterByItemProperty(")]
    [InlineData("ClashFilterNodes.cs", "public static List<ClashResult> FilterBySet(")]
    [InlineData("ClashFilterNodes.cs", "public static List<ClashResult> FilterByDepth(")]
    [InlineData("ClashTriageNodes.cs", "public static List<ClashResult> FilterByOrientation(")]
    [InlineData("ClashTriageNodes.cs", "public static Dictionary<string, object?> GroupResults(")]
    [InlineData("ClashTriageNodes.cs", "public static List<ModelItem> Focus(")]
    public void TheResultListNodesTakeSeveralWires(string file, string signature)
    {
        Assert.Contains("[MultiInput] IEnumerable<ClashResult> results", Method(Source(file), signature));
    }

    [Fact]
    public void DeduplicateWorksAcrossTestsAndKeepsOldGraphsLoading()
    {
        var source = Source("ClashFilterNodes.cs");
        var body = Method(source, "public static Dictionary<string, object?> Deduplicate(");

        // One flat list whatever the nesting: the mirrored pair of two tests is seen together.
        Assert.Contains("[MultiInput][PortKinds(\"clash*\")] IEnumerable<object> results", body);
        Assert.Contains("ClashInputs.Flatten<ClashResult>(results)", body);

        // An element is recognised by guid or by the scene node, not by the display-name path two siblings can share.
        Assert.Contains("new ModelItemKeyer()", body);
        Assert.DoesNotContain("NavisValues.ItemIdentity", body);
        var keyer = Source("Internal", "ModelItemKeyer.cs");
        Assert.Contains("IsSameInstance(", keyer);
        Assert.Contains("InstanceGuid", keyer);

        var header = source.Substring(source.LastIndexOf("[NodeName(\"Clash.Deduplicate\")]", System.StringComparison.Ordinal));
        header = header.Substring(0, header.IndexOf("public static", System.StringComparison.Ordinal));
        Assert.Contains(
            "[NodeAliases(\"CamelGraph.Navisworks.ClashFilterNodes.Deduplicate@System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.Clash.ClashResult>\")]",
            header);
    }

    // ----------------------------------------------------------------------------------------------- NVC-16, NVC-34

    [Fact]
    public void OnlyAnUnwiredTestsInputMeansEveryTest()
    {
        var summary = Method(Source("ClashEditNodes.cs"), "public static Dictionary<string, object?> SummaryTable(");
        var snapshot = Method(Source("ClashDeltaNodes.cs"), "public static Dictionary<string, object?> SnapshotToFile(");

        Assert.Contains("ClashInputs.SelectedOrAll<ClashTest>(", summary);
        Assert.Contains("ClashInputs.SelectedOrAll<ClashTest>(", snapshot);

        // The old "an empty list also means every test" fallback is gone from both.
        Assert.DoesNotContain("testList.Count == 0)\n        {\n            testList = NavisValues", summary);
        Assert.DoesNotContain("testList = NavisValues.FlattenSavedItems", snapshot.Replace("tests, () => NavisValues.FlattenSavedItems", string.Empty));
    }

    [Fact]
    public void TheSummaryAlsoComesOutAsATable()
    {
        var source = Source("ClashEditNodes.cs");
        var body = Method(source, "public static Dictionary<string, object?> SummaryTable(");

        Assert.Contains("[MultiReturn(\"rows\", \"headers\", \"table\")]", source);
        Assert.Contains("[\"table\"] = new CamelGraphTable(headers, rows)", body);
        Assert.Contains("[\"rows\"] = rows", body);
        Assert.Contains("[\"headers\"] = headers", body);
    }

    [Fact]
    public void AnEmptyResultListIsAnEmptyAnswerForGroupingAndFocus()
    {
        var triage = Source("ClashTriageNodes.cs");

        Assert.DoesNotContain("The clash results list is empty", triage);
        var group = Method(triage, "public static Dictionary<string, object?> GroupResults(");
        Assert.Contains("NodeWarnings.Add(\"No clash results were given", group);
        Assert.Contains("[\"added\"] = 0", group);

        var focus = Method(triage, "public static List<ModelItem> Focus(");
        Assert.Contains("NodeWarnings.Add(\"No clash results were given, so the view was left as it is.\")", focus);
        Assert.Contains("return new List<ModelItem>();", focus);
    }

    // ----------------------------------------------------------------------------------------------- NVC-10

    [Fact]
    public void TheRegroupNodesReportWhatTheyDissolvedAndWhatStaysLoose()
    {
        var regroup = Source("Internal", "ClashRegroup.cs");

        Assert.Contains("ClashRegroupNotes.Build(dissolvedGroups, singleBucketNames, layout.Singles.Count)", regroup);
        Assert.Contains("NodeWarnings.Add(note)", regroup);
    }

    [Theory]
    [InlineData("ClashNodes.cs", "Clash.GroupResultsBySameItem")]
    [InlineData("ClashNodes.cs", "Clash.GroupResultsByProximity")]
    [InlineData("ClashNodes.cs", "Clash.GroupResultsByLevel")]
    [InlineData("ClashEditNodes.cs", "Clash.GroupResultsByStatus")]
    [InlineData("ClashEditNodes.cs", "Clash.GroupResultsByGridIntersection")]
    public void TheRegroupDescriptionsSayEveryExistingGroupIsDissolvedAndSinglesStayLoose(string file, string nodeName)
    {
        var header = Header(file, nodeName);

        Assert.Contains("every group the test already has", header);
        Assert.Contains("a bucket with only one result stays ungrouped", header);
    }

    // ----------------------------------------------------------------------------------------------- NVC-11, NVC-40 (folder input)

    [Fact]
    public void CreateKeepsAnExistingTestUnlessToldOtherwiseAndStillLoadsOldGraphs()
    {
        var source = Source("ClashNodes.cs");
        var body = Method(source, "public static ClashTest Create(");
        var header = Header("ClashNodes.cs", "ClashTest.Create");

        Assert.Contains("[NodeChoices(\"reuse\", \"update\", \"replace\", \"error\")] string ifExists = \"reuse\"", body);
        Assert.Contains("string folder = \"\"", body);
        Assert.Contains("[NodeAliases(\"CamelGraph.Navisworks.ClashNodes.Create@string,System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,string,double,Autodesk.Navisworks.Api.Document\")]", header);

        // reuse: the existing test comes back untouched, with a warning; update keeps the results; replace is explicit.
        Assert.Contains("case ClashIfExists.Reuse:", body);
        Assert.Contains("NodeWarnings.Add(ClashCreateRules.ReusedMessage(name));", body);
        Assert.Contains("return existing;", body);
        Assert.Contains("ClashHelpers.UpdateTestSettings(", body);
        Assert.Contains("TestsReplaceWithCopy(holder, index, test)", body);

        // A test is added to the folder (the call ClashTest.Duplicate already makes) or at the top level.
        Assert.Contains("tests.TestsAddCopy(target, test)", body);
        Assert.Contains("tests.TestsAddCopy(test)", body);
        Assert.Contains("There is no clash test folder named", body);
        Assert.Contains("doc.BeginTransaction(\"Create clash test\")", body);
    }

    [Fact]
    public void TheUpdateKeepsTheResultsByEditingTheStoredTestInPlace()
    {
        var body = Method(Source("Internal", "ClashHelpers.cs"), "internal static ClashTest UpdateTestSettings(");

        Assert.Contains("TestsEditTestFromCopy(stored, copy)", body);
        Assert.DoesNotContain("TestsReplaceWithCopy", body);
    }

    // ----------------------------------------------------------------------------------------------- NVC-31

    [Theory]
    [InlineData("ClashNodes.cs", "ClashTest.Create", "public static ClashTest Create(")]
    [InlineData("ClashNodes.cs", "ClashTest.Info", "public static Dictionary<string, object?> Info(")]
    [InlineData("ClashNodes.cs", "Clash.GroupResultsByProximity", "public static Dictionary<string, object?> GroupResultsByProximity(")]
    [InlineData("ClashNodes.cs", "Clash.GroupResultsByLevel", "public static Dictionary<string, object?> GroupResultsByLevel(")]
    [InlineData("ClashTestMaintenanceNodes.cs", "ClashTest.Edit", "public static ClashTest Edit(")]
    public void TheNodesWithALengthHaveAUnitsInputInTheAdvancedPanel(string file, string nodeName, string signature)
    {
        var body = Method(Source(file), signature);

        Assert.Contains("[NodePanel(\"Advanced\")][NodeChoicesFromEnum(typeof(Units), \"document\")] string units = \"document\"", body);
        Assert.Contains("[NodeAliases(", Header(file, nodeName));
        Assert.Contains("units names another unit", Header(file, nodeName));
    }

    [Fact]
    public void TheLengthsAreConvertedToDocumentUnitsBeforeTheyAreUsed()
    {
        var nodes = Source("ClashNodes.cs");

        Assert.Contains("var scaledTolerance = tolerance * NavisValues.ResolveUnitsScale(doc, units);", Method(nodes, "public static ClashTest Create("));
        Assert.Contains("radius * NavisValues.ResolveUnitsScale(", Method(nodes, "public static Dictionary<string, object?> GroupResultsByProximity("));
        Assert.Contains("elevation * levelScale", Method(nodes, "public static Dictionary<string, object?> GroupResultsByLevel("));
        Assert.Contains("clashTest.Tolerance / scale", Method(nodes, "public static Dictionary<string, object?> Info("));

        // The "keep the current tolerance" sentinel (any negative number) is never scaled.
        var edit = Method(Source("ClashTestMaintenanceNodes.cs"), "public static ClashTest Edit(");
        Assert.Contains("if (tolerance >= 0)", edit);
    }
}
