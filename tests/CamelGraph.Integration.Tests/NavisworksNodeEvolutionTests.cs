using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using CamelGraph.Core.Serialization;
using CamelGraph.Nodes.Portable;
using CamelGraph.TestSupport.StandIns;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>
/// Navisworks nodes of the viewpoint, camera, appearance, analysis, export and TimeLiner families that changed their signature,
/// were merged into another node or were retired. A graph saved before the change must still open: its node id is resolved (the
/// real loader path, with the stand-ins built from the source: <c>[NodeAliases]</c>, <c>[NodeDeprecated]</c>), nothing it
/// wired or typed is dropped (<c>[PortAlias]</c>), and a parameter that was added later is optional. The nodes themselves need
/// Navisworks to run, so what is pinned here is the loading, not the call.
/// </summary>
public class NavisworksNodeEvolutionTests
{
    private static readonly Lazy<NodeRegistry> Rig = new Lazy<NodeRegistry>(() =>
    {
        var registry = Pipeline.CreateRegistry();
        var catalogue = NodeCatalogue.Load();
        StandInCatalogue.Create(catalogue, SourceScan.ReadNavisworks(), registry).RegisterInto(registry);
        return registry;
    });

    private static NodeRegistry Registry => Rig.Value;

    /// <summary>Earlier definition id, the node's name now, and the inputs the node has now that the earlier one did not (all optional).</summary>
    public static IEnumerable<object[]> EarlierIds()
    {
        const string doc = "Autodesk.Navisworks.Api.Document";
        const string items = "System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>";
        yield return new object[] { "CamelGraph.Navisworks.CameraNodes.Current@" + doc, "Camera.Current", "after" };
        yield return new object[] { "CamelGraph.Navisworks.CameraNodes.LookAt@object,object," + doc, "Camera.LookAt", "after" };
        yield return new object[] { "CamelGraph.Navisworks.CameraNodes.ZoomToItems@" + items + ",double," + doc, "Camera.ZoomToItems", "after" };
        yield return new object[] { "CamelGraph.Navisworks.CameraNodes.SetProjection@bool," + doc, "Camera.SetProjection", "after" };
        yield return new object[] { "CamelGraph.Navisworks.CameraNodes.SetFieldOfView@double," + doc, "Camera.SetFieldOfView", "after" };
        yield return new object[] { "CamelGraph.Navisworks.ViewpointExtraNodes.SetStandardView@string," + items + ",double," + doc, "Camera.SetStandardView", "after" };
        yield return new object[] { "CamelGraph.Navisworks.ViewpointNodes.Apply@Autodesk.Navisworks.Api.SavedViewpoint," + doc, "SavedViewpoint.Apply", "after" };
        yield return new object[] { "CamelGraph.Navisworks.ViewpointExtraNodes.Update@Autodesk.Navisworks.Api.SavedViewpoint," + doc, "SavedViewpoint.Update", "after" };
        yield return new object[] { "CamelGraph.Navisworks.ViewpointVisibilityNodes.VisibleItems@object,object,bool," + doc, "Viewpoint.VisibleItems", "after" };
        yield return new object[] { "CamelGraph.Navisworks.ViewpointTreeNodes.SortFolder@object,bool," + doc, "Viewpoints.SortFolder", "numeric" };
        yield return new object[]
        {
            "CamelGraph.Navisworks.ViewpointNodes.FromClashResults@System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.Clash.ClashResult>,string," + doc,
            "Viewpoints.FromClashResults",
            "nameFormat",
        };
        yield return new object[] { "CamelGraph.Navisworks.ViewpointNodes.Delete@string," + doc, "SavedViewpoint.Delete", "viewpoint" };
        yield return new object[] { "CamelGraph.Navisworks.ExportNodes.ViewpointImage@string,int,int," + doc, "Export.ViewpointImage", "viewpoint" };
        yield return new object[] { "CamelGraph.Navisworks.AuditNodes.DuplicateItems@" + items + ",double," + doc, "Audit.DuplicateItems", "units" };
        yield return new object[] { "CamelGraph.Navisworks.TimeLiner.TimelinerNodes.Tasks@" + doc, "TimeLiner.Tasks", "order" };
        yield return new object[]
        {
            "CamelGraph.Navisworks.TimeLiner.TimelinerNodes.Create@string,System.DateTime,System.DateTime," + items + ",string," + doc,
            "TimelinerTask.Create",
            "parent",
        };
        yield return new object[]
        {
            "CamelGraph.Navisworks.TimeLiner.TimelinerProgressNodes.SetActual@Autodesk.Navisworks.Api.Timeliner.TimelinerTask,System.DateTime,System.DateTime," + doc,
            "TimelinerTask.SetActual",
            "start",
        };
        yield return new object[]
        {
            "CamelGraph.Navisworks.TimeLiner.TimelinerNodes.AttachSet@Autodesk.Navisworks.Api.Timeliner.TimelinerTask,string," + doc,
            "TimelinerTask.AttachSet",
            "set",
        };
        yield return new object[] { "CamelGraph.Navisworks.TimeLiner.TimelinerAutoNodes.AutoAttachByProperty@string,string," + doc, "TimeLiner.AutoAttachByProperty", "matchOn" };
    }

    [Theory]
    [MemberData(nameof(EarlierIds))]
    public void AGraphSavedWithTheEarlierIdStillLoadsTheSameNode(string earlierId, string name, string addedInput)
    {
        Assert.True(Registry.TryGetDefinition(earlierId, out var definition), "The earlier id '" + earlierId + "' no longer resolves.");
        Assert.Equal(name, definition!.Name);
        Assert.NotEqual(earlierId, definition.Id);

        // The input the node gained is optional, so the old file (which cannot feed it) is complete.
        var added = definition.Inputs.Single(i => i.Name == addedInput);
        Assert.True(added.HasDefault || addedInput == "viewpoint" || addedInput == "set", addedInput + " must be optional (or be a renamed input).");

        LoadAsSavedUnder(definition, earlierId);
    }

    /// <summary>Saves a graph holding one node, writes the earlier id into the file in place of the current one and opens it again.</summary>
    private static NodeModel LoadAsSavedUnder(NodeDefinition definition, string earlierId)
    {
        var graph = new GraphModel();
        graph.AddNode(new ZeroTouchNodeModel(definition));
        var serializer = new GraphSerializer(Registry);
        var json = serializer.Serialize(graph);
        Assert.Contains("\"" + definition.Id + "\"", json);

        var loaded = serializer.Deserialize(json.Replace("\"" + definition.Id + "\"", "\"" + earlierId + "\""));
        Assert.Empty(serializer.LoadWarnings);
        var node = Assert.Single(loaded.Nodes);
        Assert.IsNotType<MissingNodeModel>(node);
        Assert.Equal(definition.Id, ((ZeroTouchNodeModel)node).Definition.Id);
        return node;
    }

    [Fact]
    public void TheNameTypedIntoAnOldDeleteNodeFindsTheRenamedInput()
    {
        // SavedViewpoint.Delete took 'name' (text); it now takes 'viewpoint' (the viewpoint, its name or its path).
        var source = NavisworksSourceIndex.Nodes.Single(n => n.Path == "CamelGraph.Navisworks.ViewpointNodes.Delete");
        Assert.Equal("viewpoint", source.CurrentInputName("name"));
        Assert.Equal("deleted", source.CurrentOutputName("deleted"));
        Assert.Contains(source.Parameters, p => p.Name == "viewpoint" && p.Type.Contains("object"));
    }

    [Theory]
    [InlineData("CamelGraph.Navisworks.ViewpointNodes.SaveCurrent@string,Autodesk.Navisworks.Api.Document", "Viewpoint.SaveCurrent", "Viewpoint.Save")]
    [InlineData("CamelGraph.Navisworks.ViewpointNodes.SaveWithOverrides@string,string,Autodesk.Navisworks.Api.Document", "Viewpoint.SaveWithOverrides", "Viewpoint.Save")]
    [InlineData("CamelGraph.Navisworks.ExportNodes.Nwd@string,Autodesk.Navisworks.Api.Document", "Export.NWD", "Document.Save")]
    [InlineData("CamelGraph.Navisworks.ExportNodes.ClashReportCsv@string,System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.Clash.ClashTest>,Autodesk.Navisworks.Api.Document", "Export.ClashReportCsv", "Export.ClashReport")]
    [InlineData("CamelGraph.Navisworks.ExportNodes.ClashReportHtml@string,System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.Clash.ClashTest>,bool,int,int,Autodesk.Navisworks.Api.Document", "Export.ClashReportHtml", "Export.ClashReport")]
    public void ARetiredNodeStillLoadsAndPointsAtItsReplacement(string id, string name, string replacement)
    {
        Assert.True(Registry.TryGetDefinition(id, out var definition), "The retired node '" + name + "' is not registered any more.");
        Assert.Equal(name, definition!.Name);
        Assert.True(definition.IsDeprecated);
        Assert.Equal(replacement, definition.Replacement);
        Assert.Contains(Registry.Definitions, d => d.Name == replacement && !d.IsDeprecated);

        LoadAsSavedUnder(definition, id);
        var current = NavisworksSourceIndex.Nodes.Single(n => n.Path == id.Substring(0, id.IndexOf('@')));
        Assert.True(current.Deprecated);
    }

    [Fact]
    public void ViewpointSaveTakesTheFolderAndTheOverridesChoiceOfBothRetiredNodes()
    {
        var save = Registry.Definitions.Single(d => d.Name == "Viewpoint.Save");
        Assert.Equal(new[] { "name", "folder", "bakeOverrides", "after", "document" }, save.Inputs.Select(i => i.Name));
        Assert.All(save.Inputs.Skip(1), i => Assert.True(i.HasDefault, i.Name + " must be optional."));
        Assert.False((bool)save.Inputs.Single(i => i.Name == "bakeOverrides").DefaultValue!);

        // Every tag the two old nodes had still finds the new one.
        foreach (var old in Registry.Definitions.Where(d => d.Name == "Viewpoint.SaveCurrent" || d.Name == "Viewpoint.SaveWithOverrides"))
        {
            foreach (var tag in old.SearchTags)
            {
                Assert.True(
                    save.SearchTags.Contains(tag, StringComparer.OrdinalIgnoreCase) || save.Name.Contains(tag, StringComparison.OrdinalIgnoreCase),
                    "Viewpoint.Save lacks the search tag '" + tag + "' of " + old.Name + ".");
            }
        }
    }

    [Fact]
    public void ExportClashReportCarriesTheInputsOutputsAndSearchWordsOfBothReports()
    {
        var report = Registry.Definitions.Single(d => d.Name == "Export.ClashReport");
        Assert.Equal(new[] { "filePath", "tests", "includeImages", "imageWidth", "imageHeight", "document" }, report.Inputs.Select(i => i.Name));
        Assert.Equal(new[] { "filePath", "rowCount" }, report.Outputs.Select(o => o.Name));
        Assert.All(report.Inputs.Skip(1), i => Assert.True(i.HasDefault, i.Name + " must be optional."));

        foreach (var old in Registry.Definitions.Where(d => d.Name == "Export.ClashReportCsv" || d.Name == "Export.ClashReportHtml"))
        {
            foreach (var tag in old.SearchTags)
            {
                Assert.True(report.SearchTags.Contains(tag, StringComparer.OrdinalIgnoreCase), "Export.ClashReport lacks the search tag '" + tag + "' of " + old.Name + ".");
            }
        }

        var save = Registry.Definitions.Single(d => d.Name == "Document.Save");
        Assert.Contains("export nwd", save.SearchTags);
    }

    [Fact]
    public void ViewpointPackageFileParseIsRetiredButStillParsesAPackage()
    {
        var registry = Pipeline.CreateRegistry();
        var parse = registry.Definitions.Single(d => d.Name == "ViewpointPackageFile.Parse");
        Assert.True(parse.IsDeprecated);
        Assert.Equal("Viewpoints.ImportFile", parse.Replacement);
        Assert.True(registry.TryGetDefinition("CamelGraph.Nodes.Portable.ViewpointPackageFile.Parse@string", out _));

        var package = new ViewpointPackageFile
        {
            SourceUnits = "Meters",
            Views = new List<PortableViewpoint> { new PortableViewpoint { Name = "Plan", Position = new[] { 1.0, 2.0, 3.0 } } },
        };
        var graph = new GraphModel();
        var text = new StringInputNode { Name = "Package", Value = package.ToJson() };
        var node = new ZeroTouchNodeModel(parse);
        graph.AddNode(text);
        graph.AddNode(node);
        Pipeline.Connect(graph, text, "value", node, "json");

        var loaded = Pipeline.SaveLoad(graph, registry);
        new GraphEngine().Run(loaded);

        var result = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single().OutPorts[0].Value;
        var parsed = Assert.IsType<ViewpointPackageFile>(result);
        Assert.Equal("Plan", Assert.Single(parsed.Views).Name);
    }

    [Fact]
    public void ANodeThatWasRetiredIsNotOfferedInTheLibraryButTheReplacementIs()
    {
        var offered = Registry.Definitions.Where(d => !d.IsDeprecated).Select(d => d.Name).ToList();
        Assert.DoesNotContain("Viewpoint.SaveCurrent", offered);
        Assert.DoesNotContain("Viewpoint.SaveWithOverrides", offered);
        Assert.DoesNotContain("ViewpointPackageFile.Parse", offered);
        Assert.DoesNotContain("Export.NWD", offered);
        Assert.DoesNotContain("Export.ClashReportCsv", offered);
        Assert.DoesNotContain("Export.ClashReportHtml", offered);
        Assert.Contains("Export.ClashReport", offered);
        Assert.Contains("Viewpoint.Save", offered);
        Assert.Contains("Camera.Save", offered);
        Assert.Contains("Camera.Restore", offered);
        Assert.Contains("Viewpoints.DeleteFolder", offered);
        Assert.Contains("Appearance.ColorByValuesTemporary", offered);
    }
}
