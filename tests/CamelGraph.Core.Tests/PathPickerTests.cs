using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using CamelGraph.Core.Tests.Fixtures;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>Nodes with path inputs, written the way node authors write them, to see which dialog each browse button gets.</summary>
public static class PathFixtures
{
    // ----- explicit [NodePath] ------------------------------------------------------------------

    [NodeName("Pick.SaveReport")]
    public static string SaveReport([NodePath(NodePathMode.Save, Filter = "Excel workbooks (*.xlsx)|*.xlsx")] string report) => report;

    [NodeName("Pick.ReadTable")]
    public static string ReadTable([NodePath(NodePathMode.Open, Filter = "CSV files (*.csv)|*.csv")] string data) => data;

    [NodeName("Pick.ScanTree")]
    public static string ScanTree([NodePath(NodePathMode.Folder)] string root) => root;

    // The name says "output" and "path", the attribute says the file is only read: the attribute wins.
    [NodeName("Pick.ReadOutput")]
    public static string ReadOutput([NodePath(NodePathMode.Open)] string outputPath) => outputPath;

    [NodeName("Pick.Title")]
    public static string Title(string title) => title;

    // The attribute means nothing on a parameter that is not a string.
    [NodeName("Pick.NotAString")]
    public static double NotAString([NodePath(NodePathMode.Save)] double amount) => amount;

    // ----- no attribute: the names decide -------------------------------------------------------

    [NodeName("File.Copy")]
    public static string CopyFile(string source, string destination) => destination;

    [NodeName("Directory.Create")]
    public static string CreateDirectory(string path) => path;

    [NodeName("Directory.GetFiles")]
    public static string GetFiles(string path, string pattern) => path;

    [NodeName("Text.WriteToFile")]
    public static string WriteText(string path, string text) => path;

    [NodeName("Text.ReadFromFile")]
    public static string ReadText(string path) => path;

    [NodeName("Log.Write")]
    public static string WriteLog(string path, string line) => path;

    [NodeName("Table.ToCsvFile")]
    public static string TableToCsv(string path, string table) => path;

    [NodeName("Zip.Create")]
    public static string CreateZip(string zipPath) => zipPath;

    [NodeName("Zip.Extract")]
    public static string ExtractZip(string zipPath, string directory) => zipPath;

    [NodeName("Document.Open")]
    public static string OpenDocument(string filePath) => filePath;

    [NodeName("Export.ToIfc")]
    public static string ExportIfc(string filePath) => filePath;

    [NodeName("System.Run")]
    public static string RunProgram(string executable, string workingDirectory) => executable;

    // "source" is a path on file nodes only.
    [NodeName("Export.IfcRule")]
    public static string IfcRule(string source) => source;

    // A viewpoint folder is a name inside the model, not a folder on disk: the kind hint takes the browse button away.
    [NodeName("Action.SaveView")]
    public static string SaveView(string name, [PortKinds("text")] string folder) => folder;

    [NodeName("Action.SaveViewUnmarked")]
    public static string SaveViewUnmarked(string name, string folder) => folder;

    // Not an Export: nothing here says the file is written.
    [NodeName("Tidy.Source")]
    public static string TidySource(string source) => source;
}

/// <summary>Which dialog the browse button of a path input opens (SYS-05).</summary>
public class PathPickerTests
{
    private static readonly List<NodeDefinition> Definitions = AssemblyNodeLoader.LoadType(typeof(PathFixtures));

    private static ZeroTouchNodeModel Node(string method) => new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == method));

    private static PortModel Port(string method, string port) => Node(method).InPorts.Single(p => p.Name == port);

    // ----- explicit [NodePath] ------------------------------------------------------------------

    [Fact]
    public void TheLoaderReadsTheAttribute()
    {
        var report = Definitions.Single(d => d.Method.Name == "SaveReport").Inputs.Single();
        Assert.Equal(NodePathMode.Save, report.PathMode);
        Assert.Equal("Excel workbooks (*.xlsx)|*.xlsx", report.PathFilter);
        Assert.Equal(NodePathMode.Folder, Definitions.Single(d => d.Method.Name == "ScanTree").Inputs.Single().PathMode);
        Assert.Null(Definitions.Single(d => d.Method.Name == "Title").Inputs.Single().PathMode);
        Assert.Null(Definitions.Single(d => d.Method.Name == "NotAString").Inputs.Single().PathMode);
        Assert.Equal(NodePathMode.Save, Port("SaveReport", "report").PathMode);
    }

    [Fact]
    public void ASaveParameterGetsASaveDialogWithItsFilterWhateverItIsCalled()
    {
        var port = Port("SaveReport", "report");

        Assert.Equal(PortEditorKind.Path, PortEditors.Resolve(port));
        var pick = PathPicker.Resolve(port);
        Assert.Equal(NodePathMode.Save, pick.Mode);
        Assert.True(pick.IsSave);
        Assert.Equal("Excel workbooks (*.xlsx)|*.xlsx", pick.Filter);
        Assert.False(PortEditors.IsFolder(port));
    }

    [Fact]
    public void AnOpenParameterGetsAnOpenDialogWithItsFilter()
    {
        var port = Port("ReadTable", "data");

        Assert.Equal(PortEditorKind.Path, PortEditors.Resolve(port));
        var pick = PathPicker.Resolve(port);
        Assert.Equal(NodePathMode.Open, pick.Mode);
        Assert.Equal("CSV files (*.csv)|*.csv", pick.Filter);
    }

    [Fact]
    public void AFolderParameterGetsAFolderChooser()
    {
        var port = Port("ScanTree", "root");

        Assert.Equal(PortEditorKind.Path, PortEditors.Resolve(port));
        Assert.Equal(NodePathMode.Folder, PathPicker.Resolve(port).Mode);
        Assert.True(PortEditors.IsFolder(port));
    }

    [Fact]
    public void TheAttributeBeatsTheNameHeuristic()
    {
        // "outputPath" is a save name, but the attribute says the file is read.
        Assert.Equal(NodePathMode.Open, PathPicker.Resolve(Port("ReadOutput", "outputPath")).Mode);
    }

    [Fact]
    public void ABlankFilterOffersAllFiles()
    {
        Assert.Equal(PathPicker.AllFiles, PathPicker.Resolve(Port("ScanTree", "root")).Filter);
        Assert.Equal(PathPicker.AllFiles, new PathPick(NodePathMode.Open, "  ", "t").Filter);
    }

    [Fact]
    public void AnOrdinaryStringHasNoBrowseButtonAndTheAttributeMeansNothingOnANumber()
    {
        Assert.Equal(PortEditorKind.Text, PortEditors.Resolve(Port("Title", "title")));
        Assert.Equal(PortEditorKind.Number, PortEditors.Resolve(Port("NotAString", "amount")));
    }

    // ----- the names decide when there is no attribute ------------------------------------------

    [Theory]
    [InlineData("CopyFile", "source", NodePathMode.Open)]
    [InlineData("CopyFile", "destination", NodePathMode.Save)]
    [InlineData("CreateDirectory", "path", NodePathMode.Folder)]
    [InlineData("GetFiles", "path", NodePathMode.Folder)]
    [InlineData("WriteText", "path", NodePathMode.Save)]
    [InlineData("ReadText", "path", NodePathMode.Open)]
    [InlineData("WriteLog", "path", NodePathMode.Save)]
    [InlineData("TableToCsv", "path", NodePathMode.Save)]
    [InlineData("CreateZip", "zipPath", NodePathMode.Save)]
    [InlineData("ExtractZip", "zipPath", NodePathMode.Open)]
    [InlineData("ExtractZip", "directory", NodePathMode.Folder)]
    [InlineData("OpenDocument", "filePath", NodePathMode.Open)]
    [InlineData("ExportIfc", "filePath", NodePathMode.Save)]
    [InlineData("RunProgram", "executable", NodePathMode.Open)]
    [InlineData("RunProgram", "workingDirectory", NodePathMode.Folder)]
    public void WithoutTheAttributeTheNodeAndPortNamesPickTheDialog(string method, string port, NodePathMode expected)
    {
        var input = Port(method, port);

        Assert.Equal(PortEditorKind.Path, PortEditors.Resolve(input));
        Assert.Equal(expected, PathPicker.Resolve(input).Mode);
        Assert.Equal(expected == NodePathMode.Folder, PortEditors.IsFolder(input));
    }

    [Fact]
    public void ARoleOfModifyDoesNotMakeAFileASaveTarget()
    {
        // Document.Open is a Modify node (it changes the open document) but only reads its file.
        var node = Node("OpenDocument");
        Assert.Equal(NodeFunction.Modify, node.Function);
        Assert.Equal(NodePathMode.Open, PathPicker.Resolve(node.InPorts[0]).Mode);
    }

    [Fact]
    public void ProgramsAndZipFilesGetTheirOwnFileTypes()
    {
        Assert.Contains("*.exe", PathPicker.Resolve(Port("RunProgram", "executable")).Filter);
        Assert.Contains("*.zip", PathPicker.Resolve(Port("CreateZip", "zipPath")).Filter);
        Assert.Contains("*.zip", PathPicker.Resolve(Port("ExtractZip", "zipPath")).Filter);
    }

    [Fact]
    public void SourceAndDestinationAreOnlyPathsOnFileNodes()
    {
        Assert.Equal(PortEditorKind.Text, PortEditors.Resolve(Port("IfcRule", "source")));
        Assert.Equal(PortEditorKind.Text, PortEditors.Resolve(Port("TidySource", "source")));
        Assert.Equal(PortEditorKind.Path, PortEditors.Resolve(Port("CopyFile", "source")));
    }

    [Fact]
    public void ATextKindHintTakesTheBrowseButtonAwayFromANameThatLooksLikeAFolder()
    {
        Assert.Equal(PortEditorKind.Text, PortEditors.Resolve(Port("SaveView", "folder")));
        Assert.Equal(PortEditorKind.Path, PortEditors.Resolve(Port("SaveViewUnmarked", "folder")));
    }

    [Fact]
    public void ARenamedNodeKeepsItsDialog()
    {
        var node = Node("WriteText");
        node.Name = "My log";

        Assert.Equal(NodePathMode.Save, PathPicker.Resolve(node.InPorts[0]).Mode);
    }

    [Fact]
    public void TheOldNameRulesStillHold()
    {
        // These are what the heuristic did before: a name that says output/save/export is a save target, "folder"/"directory" a folder.
        var node = new ValueNode();
        PortModel Input(string name) => new PortModel(node, name, typeof(string), PortDirection.Input);

        Assert.Equal(NodePathMode.Save, PathPicker.Resolve(Input("outputPath")).Mode);
        Assert.Equal(NodePathMode.Save, PathPicker.Resolve(Input("exportFile")).Mode);
        Assert.Equal(NodePathMode.Open, PathPicker.Resolve(Input("inputFile")).Mode);
        Assert.Equal(NodePathMode.Folder, PathPicker.Resolve(Input("exportFolder")).Mode);
        Assert.Equal(NodePathMode.Folder, PathPicker.Resolve(Input("outputDirectory")).Mode);
        Assert.Equal(PortEditorKind.Path, PortEditors.Resolve(Input("outputDir")));
        Assert.Equal(PortEditorKind.Text, PortEditors.Resolve(Input("title")));
    }

    // ----- the File Path input node --------------------------------------------------------------

    private static FilePathNode Wired(GraphModel graph, params ZeroTouchNodeModel[] targets)
    {
        var file = new FilePathNode();
        graph.AddNode(file);
        foreach (var target in targets)
        {
            graph.AddNode(target);
            var result = graph.Connect(file.OutPorts[0], target.InPorts[0]);
            Assert.True(result.Success, result.Message);
        }

        return file;
    }

    [Fact]
    public void AFilePathNodeThatFeedsNothingOpensAnOpenDialog()
    {
        var graph = new GraphModel();
        var file = new FilePathNode();
        graph.AddNode(file);

        Assert.Equal(NodePathMode.Open, PathPicker.ForFilePathNode(file).Mode);
        Assert.Equal(NodePathMode.Open, PathPicker.ForFilePathNode(new FilePathNode()).Mode);
    }

    [Fact]
    public void AFilePathNodeThatFeedsAWriterOpensASaveDialogWithTheWritersFilter()
    {
        var graph = new GraphModel();
        var file = Wired(graph, Node("SaveReport"));

        var pick = PathPicker.ForFilePathNode(file);

        Assert.Equal(NodePathMode.Save, pick.Mode);
        Assert.Equal("Excel workbooks (*.xlsx)|*.xlsx", pick.Filter);
    }

    [Fact]
    public void AFilePathNodeThatFeedsAReaderOrBothOpensAnOpenDialog()
    {
        var reader = new GraphModel();
        Assert.Equal(NodePathMode.Open, PathPicker.ForFilePathNode(Wired(reader, Node("ReadTable"))).Mode);
        Assert.Equal("CSV files (*.csv)|*.csv", PathPicker.ForFilePathNode(Wired(new GraphModel(), Node("ReadTable"))).Filter);

        // One wire reads the file and another writes it: the file must exist.
        var both = new GraphModel();
        Assert.Equal(NodePathMode.Open, PathPicker.ForFilePathNode(Wired(both, Node("SaveReport"), Node("ReadTable"))).Mode);
    }

    [Fact]
    public void ThePlayersFieldForAFilePathNodeFollowsWhatTheNodeFeeds()
    {
        var graph = new GraphModel();
        var file = Wired(graph, Node("SaveReport"));
        var field = file.CreatePlayerPort();

        Assert.Equal(PortEditorKind.Path, PortEditors.Resolve(field));
        Assert.Equal(NodePathMode.Save, PathPicker.Resolve(field).Mode);
    }

    [Fact]
    public void TheDirectoryPathNodesPlayerFieldIsStillAFolder()
    {
        var field = new DirectoryPathNode().CreatePlayerPort();

        Assert.Equal(PortEditorKind.Path, PortEditors.Resolve(field));
        Assert.Equal(NodePathMode.Folder, PathPicker.Resolve(field).Mode);
    }
}
