using System;
using System.Text.RegularExpressions;
using Xunit;
using static CamelGraph.Nodes.Tests.NavisworksSourceText;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Source-reading tests (the Navisworks node project cannot be loaded here) for the export, BCF, IFC and analysis nodes fixed in
/// the node-library audit: what each writer declares, how the merged report picks its format, what the picture node does with a
/// viewpoint, and which analysis nodes write to the document or the disk.
/// </summary>
public class NavisworksExportAnalysisSourceTests
{
    private static string WithoutComments(string text) => Regex.Replace(text, @"//[^\n]*", string.Empty);

    /// <summary>The attributes of one node: from its [NodeName] to the method's "public static".</summary>
    private static string Attributes(string source, string nodeName)
    {
        // Some nodes put [NodeFunction] or [NodeEffects] above [NodeName], so the block starts at the end of the XML comment.
        var at = source.IndexOf("[NodeName(\"" + nodeName + "\")]", StringComparison.Ordinal);
        Assert.True(at >= 0, "node not found: " + nodeName);
        var start = source.LastIndexOf("</returns>", at, StringComparison.Ordinal);
        var end = source.IndexOf("public static", at, StringComparison.Ordinal);
        return source.Substring(Math.Max(start, 0), end - Math.Max(start, 0));
    }

    // ------------------------------------------------------------------ NVC-27

    [Fact]
    public void ExportClashReportPicksCsvOrHtmlByTheExtension()
    {
        var body = WithoutComments(Body(Source("ExportNodes.cs"), "public static Dictionary<string, object?> ClashReport("));

        Assert.Contains("case \".csv\":", body);
        Assert.Contains("return WriteClashCsv(filePath, tests, document);", body);
        Assert.Contains("case \".html\":", body);
        Assert.Contains("case \".htm\":", body);
        Assert.Contains("return WriteClashHtml(filePath, tests, includeImages, imageWidth, imageHeight, document);", body);
        Assert.Contains("must end in .csv", body);
    }

    [Fact]
    public void TheTwoRetiredClashReportsKeepWritingTheirOwnFormatWhateverTheExtension()
    {
        var source = Source("ExportNodes.cs");

        Assert.Contains("return WriteClashCsv(filePath, tests, document);", Body(source, "public static Dictionary<string, object?> ClashReportCsv("));
        Assert.Contains("return WriteClashHtml(filePath, tests, includeImages, imageWidth, imageHeight, document);", Body(source, "public static Dictionary<string, object?> ClashReportHtml("));
        Assert.Matches(new Regex(@"\[NodeName\(""Export\.ClashReportCsv""\)\]\s*\[NodeDeprecated\(""Export\.ClashReport""\)\]"), source);
        Assert.Matches(new Regex(@"\[NodeName\(""Export\.ClashReportHtml""\)\]\s*\[NodeDeprecated\(""Export\.ClashReport""\)\]"), source);
    }

    [Fact]
    public void TheMergedReportKeepsTheNullMeansAllAndEmptyMeansNoneRuleForTests()
    {
        var source = Source("ExportNodes.cs");
        var resolve = Body(source, "private static List<ClashTest> ResolveTests(");

        Assert.Contains("if (tests == null)", resolve); // unwired: every test
        Assert.Contains("FlattenSavedItems<ClashTest>", resolve);
        Assert.Contains("foreach (var test in tests)", resolve); // a list, even an empty one: exactly those tests
        Assert.Contains("an empty list reports none", Attributes(source, "Export.ClashReport"));
    }

    // ------------------------------------------------------------------ NVC-22

    [Fact]
    public void ExportNwdIsTheSameCallAsDocumentSave()
    {
        var source = Source("ExportNodes.cs");
        var body = Body(source, "public static string Nwd(");

        Assert.Contains("return DocumentNodes.Save(filePath, document);", body);
        Assert.Contains("must end in .nwd", body);
        Assert.Matches(new Regex(@"\[NodeName\(""Export\.NWD""\)\]\s*\[NodeDeprecated\(""Document\.Save""\)\]"), source);
        Assert.Contains("\"export nwd\"", Attributes(Source("DocumentNodes.cs"), "Document.Save"));
    }

    // ------------------------------------------------------------------ NVC-09, NVC-43

    [Fact]
    public void ExportViewpointImageCanShowEachViewpointItselfAndHasAnAfterInput()
    {
        var source = Source("ExportNodes.cs");
        Assert.Contains("[ScalarInput] object? viewpoint = null,\n        object? after = null,\n        Document? document = null)", source);

        var body = WithoutComments(Body(source, "public static string ViewpointImage("));
        Assert.Contains("FileNameTemplate.Apply(filePath, viewName)", body);
        Assert.Contains("FileNameTemplate.HasNameToken(filePath)", body);
        Assert.Contains("PathResolver.Resolve(filePath)", body);
        // The viewpoint is shown before the exporter runs, and the extension is checked after the name is filled in.
        Assert.True(body.IndexOf("ShowViewpoint(", StringComparison.Ordinal) < body.IndexOf("ImageFormatCode(filePath)", StringComparison.Ordinal));
        Assert.True(body.IndexOf("ImageFormatCode(filePath)", StringComparison.Ordinal) < body.IndexOf("state.DriveIOPlugin(", StringComparison.Ordinal));

        var show = Body(source, "private static void ShowViewpoint(");
        Assert.Contains("doc.SavedViewpoints.CurrentSavedViewpoint = saved;", show);
        Assert.Contains("doc.CurrentViewpoint.CopyFrom((Viewpoint)view);", show);
    }

    // ------------------------------------------------------------------ NVC-20, NVC-32, NVC-33

    [Fact]
    public void ExportToIfcAlwaysAnswersOneFilePathAndListsEveryFileSeparately()
    {
        var source = Source("IfcExportNodes.cs");
        var toIfc = Attributes(source, "Export.ToIfc");

        Assert.Contains("[MultiReturn(\"filePath\", \"fileCount\", \"elementCount\", \"triangleCount\", \"fileSizeKb\", \"files\")]", toIfc);
        Assert.Contains("[PortKinds(\"file\", \"integer\", \"integer\", \"integer\", \"number\", \"file*\")]", toIfc);
        Assert.Contains("[\"filePath\"] = summary.Files.Count > 0 ? summary.Files[0] : filePath,", source);
        Assert.Contains("[\"files\"] = new List<string>(summary.Files),", source);
        Assert.DoesNotContain("summary.Files.Cast<object?>().ToList()", source);
    }

    [Fact]
    public void ExportToIfcPutsItsRareOptionsInAnAdvancedPanelAndOffersTheQualityChoices()
    {
        var source = Source("IfcExportNodes.cs");
        var start = source.IndexOf("public static Dictionary<string, object?> ToIfc(", StringComparison.Ordinal);
        var signature = source.Substring(start, source.IndexOf(")\n    {", start, StringComparison.Ordinal) - start);

        foreach (var name in new[]
        {
            "instancing", "properties", "materials", "quantities", "quality", "coordinates", "spatialNames", "roles",
            "parameterRules", "categoryFilter", "classMap", "splitMegabytes", "validate",
        })
        {
            Assert.Matches(new Regex(@"\[NodePanel\(""Advanced""\)\][^\n]*\b" + name + @"\b"), signature);
        }

        // The main inputs stay in the main list.
        Assert.DoesNotMatch(new Regex(@"\[NodePanel\(""Advanced""\)\][^\n]*\b(items|filePath|schema|units|document)\b"), signature);
        Assert.Contains("[NodeChoices(\"Balanced\", \"Small file\", \"High detail\")] string quality = \"Balanced\"", signature);
        Assert.Contains("[NodePath(NodePathMode.Save, Filter = \"IFC files (*.ifc)|*.ifc\")] string filePath", signature);
    }

    [Fact]
    public void TheIfcOptionNodesOfferDropdownsAndPickersFromTheSelection()
    {
        var source = Source("IfcExportNodes.cs");

        Assert.Contains("[NodeChoices(\"GeometryOrigin\", \"ModelOrigin\", \"Custom\")] string basePoint = \"GeometryOrigin\"", source);
        Assert.Contains("[NodePanel(\"Custom base point\")] double eastings = 0,", source);
        foreach (var role in new[] { "type", "level", "material", "classification" })
        {
            Assert.Contains("[NodePropertyChoice(NodeDataSource.Selection, \"" + role + "Category\")] string " + role + "Property = \"\",", source);
            Assert.Contains("[NodeTabChoice(NodeDataSource.Selection)] string " + role + "Category = \"\"", source);
        }

        Assert.Contains("[NodePropertyChoice(NodeDataSource.Selection, \"sourceCategory\")] string source,", source);
        Assert.Contains("[NodeTabChoice(NodeDataSource.Selection)] string sourceCategory = \"\"", source);
    }

    [Fact]
    public void ExportToCsvPointsToTheTableFamilyAndOffersItsTabPicker()
    {
        var source = Source("ExportNodes.cs");
        var attributes = Attributes(source, "Export.ToCsv");

        Assert.Contains("Properties.ToTable", attributes);
        Assert.Contains("Table.ToCsvFile", attributes);
        Assert.Contains("[NodeTabChoice(\"items\")] string? categoryName = null,", source);
        Assert.Contains("filePath = PathResolver.Resolve(filePath);", Body(source, "public static string ToCsv("));
    }

    // ------------------------------------------------------------------ NVC-04, NVC-35 (BCF)

    [Fact]
    public void BcfIssueTitlesOfSeveralTestsAreMadeUniqueBeforeTheyAreWritten()
    {
        var export = WithoutComments(Body(Source("BcfNodes.cs"), "public static Dictionary<string, object?> ExportIssues("));

        Assert.Contains("topicTests.Add(ClashNaming.TestNameOf(item));", export);
        Assert.Contains("NameDisambiguator.Make(titles, topicTests)", export);
        Assert.Contains("NodeWarnings.Add(", export);
        Assert.True(export.IndexOf("NameDisambiguator.Make(", StringComparison.Ordinal) < export.IndexOf("BcfFile.Write(", StringComparison.Ordinal));
        Assert.Contains("topics[i].Title = unique.Names[i];", export);
    }

    [Fact]
    public void BcfImportSaysItsModelItemsAreAListOfLists()
    {
        var source = Source("BcfNodes.cs");

        Assert.Contains("[PortKinds(\"data*\", \"item**\")]", Attributes(source, "BCF.ImportIssues"));
        Assert.Contains("[NodePath(NodePathMode.Open,", Attributes(source, "BCF.ImportIssues") + "\n" + source.Substring(source.IndexOf("ImportIssues(", StringComparison.Ordinal), 300));
        Assert.Contains("[NodePath(NodePathMode.Save,", source.Substring(source.IndexOf("ExportIssues(", StringComparison.Ordinal), 300));
    }

    // ------------------------------------------------------------------ NVC-14, SYS-05: every writer says what it writes

    [Theory]
    [InlineData("ExportNodes.cs", "Export.ToCsv", "WritesFiles")]
    [InlineData("ExportNodes.cs", "Export.ClashReport", "WritesFiles")]
    [InlineData("ExportNodes.cs", "Export.ClashReportCsv", "WritesFiles")]
    [InlineData("ExportNodes.cs", "Export.ClashReportHtml", "WritesFiles")]
    [InlineData("ExportNodes.cs", "Export.ViewpointImage", "WritesFiles")]
    [InlineData("ExportNodes.cs", "Export.NWD", "WritesFiles")]
    [InlineData("IfcExportNodes.cs", "Export.ToIfc", "WritesFiles")]
    [InlineData("BcfNodes.cs", "BCF.ExportIssues", "WritesFiles")]
    [InlineData("FallHazardNodes.cs", "FallHazard.EdgeHandrailCheck", "WritesFiles")]
    [InlineData("FallHazardNodes.cs", "FallHazard.FloorOpeningMap", "ChangesModel | CamelGraph.Core.Graph.NodeEffects.WritesFiles")]
    [InlineData("AuditNodes.cs", "Audit.DuplicateItems", "ChangesModel")]
    [InlineData("ClusterNodes.cs", "Proximity.Cluster", "ChangesModel")]
    [InlineData("ZoneNodes.cs", "Zone.AssignByVolumes", "ChangesModel")]
    public void EveryNodeThatWritesAFileOrEditsTheDocumentSaysSo(string file, string nodeName, string effect)
    {
        Assert.Contains("[NodeEffects(CamelGraph.Core.Graph.NodeEffects." + effect + ")]", Attributes(Source(file), nodeName));
    }

    [Theory]
    [InlineData("ExportNodes.cs", "Export.ToCsv")]
    [InlineData("ExportNodes.cs", "Export.ClashReport")]
    [InlineData("ExportNodes.cs", "Export.ViewpointImage")]
    [InlineData("IfcExportNodes.cs", "Export.ToIfc")]
    [InlineData("BcfNodes.cs", "BCF.ExportIssues")]
    [InlineData("BcfNodes.cs", "BCF.ImportIssues")]
    [InlineData("FallHazardNodes.cs", "FallHazard.FloorOpeningMap")]
    [InlineData("FallHazardNodes.cs", "FallHazard.EdgeHandrailCheck")]
    public void TheFileInputsOfTheWritersAndReadersOpenTheRightDialog(string file, string nodeName)
    {
        var source = Source(file);
        var from = source.IndexOf("[NodeName(\"" + nodeName + "\")]", StringComparison.Ordinal);
        var to = source.IndexOf("\n    {", from, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"\[NodePath\(NodePathMode\.(Save|Open), Filter = "), source.Substring(from, to - from));
    }

    // ------------------------------------------------------------------ NVC-08

    [Fact]
    public void FloorOpeningMapNamesItsViewpointsInTheChosenUnitAndReplacesTheOnesOfTheLastRun()
    {
        var source = Source("FallHazardNodes.cs");
        var body = WithoutComments(Body(source, "private static List<SavedViewpoint> CreateViewpoints("));

        Assert.Contains("opening.WidestGap / scale", body);
        Assert.Contains("unitsLabel", body);
        Assert.Contains("ViewpointStore.ResolveFolder(viewpoints, folderName)", body);
        Assert.Contains("ViewpointStore.Put(viewpoints, savedViewpoint, folder)", body);
        Assert.DoesNotContain("AddCopy", body); // the add-only copy is gone
        Assert.Contains("CreateViewpoints(doc, flagged, level, band, cellSize, scale, unitsLabel)", source);
        Assert.Contains("[NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]", Attributes(source, "FallHazard.FloorOpeningMap"));
    }

    [Fact]
    public void AuditDuplicateItemsTakesItsToleranceInANamedUnit()
    {
        var source = Source("AuditNodes.cs");
        var body = WithoutComments(Body(source, "public static Dictionary<string, object?> DuplicateItems("));

        Assert.Contains("[NodePanel(\"Advanced\")] [NodeChoicesFromEnum(typeof(Units), \"document\")] string units = \"document\",", body);
        Assert.Contains("var worldTolerance = tolerance * NavisValues.ResolveUnitsScale(doc, units);", body);
        Assert.Contains("Tolerance = worldTolerance,", body);
        Assert.Contains("tests.TestsRemove(stored);", body); // the temporary test is still always removed
        Assert.Contains("[NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]", Attributes(source, "Audit.DuplicateItems"));
    }

    // ------------------------------------------------------------------ NVC-31

    [Fact]
    public void TheUnitsDropDownsOfTheAnalysisNodesOfferEveryUnitNotAFixedShortList()
    {
        foreach (var file in new[] { "FallHazardNodes.cs", "ClusterNodes.cs", "AuditNodes.cs" })
        {
            var source = Source(file);
            Assert.DoesNotContain("[NodeChoices(\"document\", \"Meters\"", source);
            Assert.Contains("[NodeChoicesFromEnum(typeof(Units), \"document\")]", source);
        }
    }

    [Fact]
    public void ProximityClusterKeepsItsStampingOptionsInAnAdvancedPanel()
    {
        var source = Source("ClusterNodes.cs");

        Assert.Contains("[NodePanel(\"Advanced\")] string propertyName = \"\",", source);
        Assert.Contains("[NodePanel(\"Advanced\")] string tabName = \"CamelGraph Data\",", source);
    }

    // ------------------------------------------------------------------ NVC-34

    [Fact]
    public void TakeoffSumPropertyByGroupAlsoAnswersOneTable()
    {
        var source = Source("TakeoffNodes.cs");
        var attributes = Attributes(source, "Takeoff.SumPropertyByGroup");

        Assert.Contains("[MultiReturn(\"keys\", \"sums\", \"counts\", \"table\")]", attributes);
        Assert.Contains("[PortKinds(\"text*\", \"number*\", \"integer*\", \"data\")]", attributes);
        var body = Body(source, "public static Dictionary<string, object?> SumPropertyByGroup(");
        Assert.Contains("[\"table\"] = new CamelGraphTable(new[] { groupPropertyName, valuePropertyName, \"Count\" }, rows),", body);
        // The three lists the existing graphs wire are still there, first.
        Assert.True(body.IndexOf("[\"keys\"]", StringComparison.Ordinal) < body.IndexOf("[\"table\"]", StringComparison.Ordinal));
    }
}
