using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Serialization;
using CamelGraph.TestSupport.StandIns;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>
/// The model-item, transform, grid, model, document, units, comment and markup nodes after the node-library audit. The Navisworks
/// project cannot be loaded here, so these tests look at the node definitions the stand-ins build from the catalogue and the C#
/// source (the attributes that decide how a port behaves), and load graphs that still name the old definition ids.
/// </summary>
public class NavisworksModelAuditTests
{
    private static readonly Lazy<(NodeRegistry Registry, StandInCatalogue StandIns, SourceScan Source)> Rig =
        new Lazy<(NodeRegistry, StandInCatalogue, SourceScan)>(() =>
        {
            var registry = Pipeline.CreateRegistry();
            var catalogue = NodeCatalogue.Load();
            var source = SourceScan.ReadNavisworks();
            var standIns = StandInCatalogue.Create(catalogue, source, registry);
            standIns.RegisterInto(registry);
            return (registry, standIns, source);
        });

    private static NodeRegistry Registry => Rig.Value.Registry;

    private static SourceScan Source => Rig.Value.Source;

    private static NodeDefinition Def(string nodeName) =>
        Registry.Definitions.Single(d => d.Name == nodeName);

    private static PortDescriptor Input(string nodeName, string port) =>
        Def(nodeName).Inputs.Single(i => i.Name == port);

    /// <summary>The method in the source that carries <c>[NodeName("name")]</c> (the stand-in cannot show run-time attributes).</summary>
    private static SourceMethod Method(string nodeName) =>
        Source.Methods.Single(m => m.Attribute("NodeName")?.Strings().FirstOrDefault() == nodeName);

    private static SourceParameter Parameter(string nodeName, string parameter) =>
        Method(nodeName).Parameters.Single(p => p.Name == parameter);

    private static string FileText(string file) =>
        File.ReadAllText(Path.Combine(RepoLocator.NavisworksSourceDirectory(), file)).Replace("\r\n", "\n");

    // ================================================================= NVM-02, NVM-25: list inputs

    [Fact]
    public void ModelStatisticsItemsIsAnOptionalMultiInput()
    {
        var items = Input("Model.Statistics", "items");

        Assert.True(items.HasDefault);
        Assert.True(items.MultiInput);
    }

    [Fact]
    public void ModelStatisticsTellsAnEmptyWiredListFromAnUnwiredInput()
    {
        var body = Body(FileText("ModelDataNodes.cs"), "public static CamelGraphTable Statistics(");

        Assert.Contains("OptionalScope.Classify(items", body);
        Assert.Contains("ScopeKind.Nothing", body);
        Assert.Contains("NodeWarnings.Add(", body);
        // The old shape fell through to the whole document whenever the list had no entries.
        Assert.DoesNotContain("list.Count > 0", body);
        Assert.Contains("never the whole document by accident", Def("Model.Statistics").Description);
    }

    [Theory]
    [InlineData("ModelItem.ResetTransform", "items")]
    [InlineData("Model.Snapshot", "items")]
    [InlineData("Model.Snapshot", "properties")]
    [InlineData("Document.AppendFiles", "filePaths")]
    public void FlatListInputsTakeSeveralWires(string node, string port)
    {
        Assert.True(Input(node, port).MultiInput, node + "." + port + " should be a multi-input.");
    }

    // ================================================================= NVM-04, NVM-27: ranges

    [Fact]
    public void TheGridSamplesRangeMatchesWhatTheCodeAccepts()
    {
        var range = Input("Grids.Intersections", "samples").Range!;

        Assert.Equal(2, range.Min);
        Assert.Equal(100, range.Max);
        Assert.Equal(100, range.SoftMax);
        Assert.Contains("Math.Min(100, samples)", FileText("GridNodes.cs"));
    }

    [Fact]
    public void TheScaleFactorRangeCannotReachZero()
    {
        var range = Input("ModelItem.Scale", "factor").Range!;

        Assert.True(range.Min > 0, "ModelItem.Scale rejects a factor of 0, so the field must not offer it.");
        Assert.Equal(1000000, range.Max);
    }

    [Fact]
    public void TheRotationAngleIsABoundedFieldInDegrees()
    {
        var range = Input("ModelItem.RotateAboutAxis", "degrees").Range!;

        Assert.Equal(-360, range.Min);
        Assert.Equal(360, range.Max);
        Assert.Equal(-180, range.SoftMin);
        Assert.Equal(180, range.SoftMax);
        Assert.Equal(1, range.Step);
        Assert.Equal("°", range.Unit);
    }

    [Theory]
    [InlineData("Markup.AddText", "x")]
    [InlineData("Markup.AddText", "y")]
    [InlineData("Markup.AddShape", "x1")]
    [InlineData("Markup.AddShape", "y1")]
    [InlineData("Markup.AddShape", "x2")]
    [InlineData("Markup.AddShape", "y2")]
    [InlineData("Markup.AddNumberTag", "x")]
    [InlineData("Markup.AddNumberTag", "y")]
    public void TheMarkupCoordinatesStartInTheRangeTheDescriptionSuggests(string node, string port)
    {
        var range = Input(node, port).Range!;

        Assert.Equal(-1, range.SoftMin);
        Assert.Equal(1, range.SoftMax);
        Assert.Equal(0.05, range.Step, 5);
        Assert.True(range.Min <= -100 && range.Max >= 100, "A coordinate read back with Markup.List must still be typable.");
    }

    [Fact]
    public void TheNumberTagRadiusIsBounded()
    {
        var range = Input("Markup.AddNumberTag", "radius").Range!;

        Assert.Equal(0.01, range.Min, 5);
        Assert.Equal(1, range.Max);
    }

    // ================================================================= NVM-15: bounding box of an item without geometry

    [Fact]
    public void ABoundingBoxOfAnItemWithoutGeometryIsNullWithAWarning()
    {
        var text = FileText("ModelItemNodes.cs");
        var body = Body(text, "public static BoundingBox3D? BoundingBox(");

        Assert.Contains("box.IsEmpty", body);
        Assert.Contains("return null;", body);
        Assert.Contains("NodeWarnings.Add(", body);
        Assert.Contains("List.Clean drops it", Def("ModelItem.BoundingBox").Description);
    }

    // ================================================================= NVM-10, NVM-11, NVM-13: lists beside a list of items

    private const string Item = "System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>";
    private const string Doc = "Autodesk.Navisworks.Api.Document";

    /// <summary>The graph file of an older version names the node by its old id: it must still open as the current node.</summary>
    private static void AssertOldIdOpensAs(string nodeName, string oldId)
    {
        var definition = Def(nodeName);
        Assert.NotEqual(definition.Id, oldId);
        Assert.True(Registry.TryGetDefinition(oldId, out var found), "the old id of " + nodeName + " no longer resolves");
        Assert.Equal(definition.Id, found!.Id);

        var graph = new GraphModel();
        graph.AddNode(new ZeroTouchNodeModel(definition));
        var serializer = new GraphSerializer(Registry);
        var json = serializer.Serialize(graph).Replace(definition.Id, oldId);
        Assert.Contains(oldId, json);

        var loaded = serializer.Deserialize(json);

        Assert.Empty(serializer.LoadWarnings);
        var node = Assert.IsType<ZeroTouchNodeModel>(Assert.Single(loaded.Nodes));
        Assert.Equal(definition.Id, node.Definition.Id);
        Assert.Equal(nodeName, node.Name);
    }

    [Fact]
    public void TranslateRotateAndScaleStillOpenFromTheirOldIds()
    {
        AssertOldIdOpensAs("ModelItem.Translate", "CamelGraph.Navisworks.TransformNodes.Translate@" + Item + ",object," + Doc);
        AssertOldIdOpensAs("ModelItem.RotateAboutAxis", "CamelGraph.Navisworks.TransformNodes.RotateAboutAxis@" + Item + ",object,object,double," + Doc);
        AssertOldIdOpensAs("ModelItem.Scale", "CamelGraph.Navisworks.TransformExtraNodes.Scale@" + Item + ",double,object," + Doc);
    }

    [Theory]
    [InlineData("ModelItem.Translate")]
    [InlineData("ModelItem.RotateAboutAxis")]
    [InlineData("ModelItem.Scale")]
    public void TheAccumulateOptionIsOnByDefaultAndSitsInTheAdvancedPanel(string node)
    {
        var accumulate = Input(node, "accumulate");

        Assert.True(accumulate.HasDefault);
        Assert.Equal(true, accumulate.DefaultValue);
        Assert.Equal("Advanced", accumulate.Panel);
        Assert.Equal(typeof(bool), accumulate.Type);
        Assert.Equal("document", Def(node).Inputs.Last().Name);
    }

    [Theory]
    [InlineData("Translate")]
    [InlineData("RotateAboutAxis")]
    [InlineData("Scale")]
    [InlineData("MoveTo")]
    public void TheTransformNodesLeaveOutItemsThatAListedContainerAlreadyMoves(string method)
    {
        var text = FileText(method == "Scale" || method == "MoveTo" ? "TransformExtraNodes.cs" : "TransformNodes.cs");
        var body = Body(text, "public static List<ModelItem> " + method + "(");

        Assert.Contains("TransformHelpers.WithoutListedDescendants(", body);
        Assert.Contains("TransformHelpers.WarnIfRunOncePerValue(", body);
    }

    [Fact]
    public void WithAccumulateOffTheDeltaReplacesTheOverrideInOneCall()
    {
        var body = Body(FileText("TransformNodes.cs"), "internal static void ApplyDelta(");

        Assert.Contains("if (!accumulate)", body);
        Assert.Contains("OverridePermanentTransform(items, delta, false)", body);
        Assert.Contains("ComposeWithOverride(item, delta)", body);
    }

    [Theory]
    [InlineData("ModelItem.Translate")]
    [InlineData("ModelItem.RotateAboutAxis")]
    [InlineData("ModelItem.SetTransform")]
    [InlineData("ModelItem.ResetTransform")]
    [InlineData("ModelItem.Scale")]
    [InlineData("ModelItem.MoveTo")]
    [InlineData("Model.Remove")]
    public void NodesThatEditTheModelSayWhetherTheyDo(string node)
    {
        var effects = Method(node).Attribute("NodeEffects");

        Assert.NotNull(effects);
        Assert.Contains("ChangesModel", effects!.Positional[0]);
    }

    [Fact]
    public void TheTransformDescriptionsTellHowToGiveEachItemItsOwnValue()
    {
        foreach (var node in new[] { "ModelItem.Translate", "ModelItem.RotateAboutAxis" })
        {
            var description = Def(node).Description;
            Assert.Contains("List Levels L1", description);
            Assert.Contains("add up", description);
        }

        Assert.Contains("[2, 3] is 6 in total", Def("ModelItem.Scale").Description);
        Assert.Contains("the last one stays", Def("ModelItem.MoveTo").Description);
    }

    [Theory]
    [InlineData("Markup.AddText")]
    [InlineData("Markup.AddShape")]
    [InlineData("Markup.AddCloud")]
    [InlineData("Markup.AddNumberTag")]
    [InlineData("Markup.List")]
    [InlineData("Markup.Clear")]
    [InlineData("Markup.AddLine")]
    [InlineData("Markup.AddArrow")]
    [InlineData("Markup.AddEllipse")]
    public void AListOfViewpointsRunsTheMarkupNodeOncePerViewpoint(string node)
    {
        Assert.NotNull(Parameter(node, "viewpoint").Attribute("ScalarInput"));
    }

    [Fact]
    public void ModelRemoveTakesAListAndResolvesItAgainstTheModelsAsTheyAreNow()
    {
        var text = FileText("ModelOpsNodes.cs");
        var body = Body(text, "public static bool Remove(");

        // Every entry is turned into a position first, then the positions go from the highest down.
        Assert.True(body.IndexOf("ResolveModelIndex(doc, entry)", StringComparison.Ordinal) < body.IndexOf("TryRemoveFile", StringComparison.Ordinal));
        Assert.Contains("ModelRemoval.RemovalOrder(wanted)", body);
        Assert.Null(Parameter("Model.Remove", "model").Attribute("ScalarInput"));
        Assert.Contains("[0, 1] remove the first two models", Def("Model.Remove").Description);
    }

    [Theory]
    [InlineData("ToPoint3D", "NavisValues.cs", "point")]
    public void ASocketThatTakesOnePointExplainsAListOfPointsInsteadOfFailingOnAConversion(string method, string file, string thing)
    {
        var text = FileText(Path.Combine("Internal", file));
        var body = Body(text, "internal static Point3D " + method + "(");

        Assert.Contains("SeveralValues.HoldsSeveral(list)", body);
        Assert.Contains("SeveralValues.Describe(\"" + thing + "\", list)", body);
    }

    [Fact]
    public void TheVectorAndMatrixSocketsExplainAListOfValuesToo()
    {
        Assert.Contains("SeveralValues.Describe(\"vector\", list)", FileText("TransformNodes.cs"));
        Assert.Contains("SeveralValues.Describe(\"matrix\", list)", FileText(Path.Combine("Internal", "TransformHelpers.cs")));
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>The text of a method from the line with its signature to its closing brace.</summary>
    private static string Body(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "method not found: " + signature);
        var open = source.IndexOf("\n    {\n", start, StringComparison.Ordinal);
        var close = source.IndexOf("\n    }\n", open, StringComparison.Ordinal);
        Assert.True(open >= 0 && close > open, "could not find the body of " + signature);
        return source.Substring(start, close - start);
    }
}
