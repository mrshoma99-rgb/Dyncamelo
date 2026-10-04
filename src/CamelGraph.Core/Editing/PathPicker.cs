using System;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;

namespace CamelGraph.Core.Editing;

/// <summary>What a browse button should open for one path input: which dialog, which file types, and what to call it.</summary>
public sealed class PathPick
{
    /// <summary>Creates a pick.</summary>
    /// <param name="mode">Open, Save or Folder.</param>
    /// <param name="filter">A Windows file dialog filter; blank means all files.</param>
    /// <param name="title">The dialog's caption.</param>
    public PathPick(NodePathMode mode, string? filter, string title)
    {
        Mode = mode;
        Filter = string.IsNullOrWhiteSpace(filter) ? PathPicker.AllFiles : filter!;
        Title = title ?? string.Empty;
    }

    /// <summary>Which dialog to open.</summary>
    public NodePathMode Mode { get; }

    /// <summary>The file types offered, as a Windows file dialog filter (never blank).</summary>
    public string Filter { get; }

    /// <summary>The dialog's caption.</summary>
    public string Title { get; }

    /// <summary>True for a folder chooser.</summary>
    public bool IsFolder => Mode == NodePathMode.Folder;

    /// <summary>True for a save dialog (the file need not exist).</summary>
    public bool IsSave => Mode == NodePathMode.Save;
}

/// <summary>
/// Decides which dialog the browse button of a path input opens. An input marked <see cref="NodePathAttribute"/> says so itself;
/// for every other path-looking input the choice falls back to its name and the node's name: a folder name opens a folder
/// chooser, a name that says output/save/export/destination, or a node that writes (Write, Save, Export, Append, Snapshot,
/// Create, To…File), opens a SAVE dialog, and anything else an open dialog.
/// </summary>
public static class PathPicker
{
    /// <summary>The filter that offers every file.</summary>
    public const string AllFiles = "All files (*.*)|*.*";

    private const string Programs = "Programs (*.exe;*.bat;*.cmd;*.com)|*.exe;*.bat;*.cmd;*.com|All files (*.*)|*.*";
    private const string ZipFiles = "Zip archives (*.zip)|*.zip|All files (*.*)|*.*";

    /// <summary>What the browse button of an input opens.</summary>
    /// <param name="port">An input port.</param>
    public static PathPick Resolve(PortModel port)
    {
        if (port == null)
        {
            throw new ArgumentNullException(nameof(port));
        }

        if (port.PathMode.HasValue)
        {
            return Make(port.PathMode.Value, port.PathFilter, port.Name);
        }

        // The value of a File Path input node (in the Player's form): what it feeds decides.
        if (port.Owner is FilePathNode filePath)
        {
            return ForFilePathNode(filePath);
        }

        var lower = port.Name.ToLowerInvariant();
        var owner = DefinitionName(port);
        if (IsFolderName(lower) || (lower == "path" && owner.StartsWith("Directory.", StringComparison.Ordinal)))
        {
            return Make(NodePathMode.Folder, string.Empty, port.Name);
        }

        var filter = lower == "executable" ? Programs : lower.EndsWith("zippath", StringComparison.Ordinal) ? ZipFiles : string.Empty;
        return Make(IsSaveName(lower) || IsWriter(owner) ? NodePathMode.Save : NodePathMode.Open, filter, port.Name);
    }

    /// <summary>
    /// What the browse button of a File Path input node opens. The node cannot tell by itself, so it looks at what its wires feed:
    /// a save dialog when everything it feeds writes a file (so a file that does not exist yet can be chosen), else an open dialog.
    /// The file types come from the first input it feeds that names some.
    /// </summary>
    /// <param name="node">The File Path node.</param>
    public static PathPick ForFilePathNode(FilePathNode node)
    {
        if (node == null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        var graph = node.Graph;
        var targets = graph == null
            ? Array.Empty<PathPick>()
            : node.OutPorts
                .SelectMany(o => graph.FindConnectionsFrom(o))
                .Where(c => !c.IsMuted && c.Target.Owner is ZeroTouchNodeModel && PortKinds.FromPort(c.Target).Family == PortFamily.File)
                .Select(c => Resolve(c.Target))
                .Where(p => !p.IsFolder)
                .ToArray();

        var save = targets.Length > 0 && targets.All(p => p.IsSave);
        var mode = save ? NodePathMode.Save : NodePathMode.Open;
        var filter = targets.Select(p => p.Filter).FirstOrDefault(f => !string.Equals(f, AllFiles, StringComparison.Ordinal)) ?? string.Empty;
        return new PathPick(mode, filter, save ? "Choose file to create" : "Select File");
    }

    /// <summary>
    /// True when a string input is a path: it carries <see cref="NodePathAttribute"/>, or its name says so ("path", "file",
    /// "filename", "folder", "directory", "dir", "executable", or "source" / "destination" on a file, folder or zip node). Such an
    /// input gets a browse button.
    /// </summary>
    /// <param name="port">An input port of string type.</param>
    public static bool SuggestsPath(PortModel port)
    {
        if (port == null)
        {
            throw new ArgumentNullException(nameof(port));
        }

        if (port.PathMode.HasValue)
        {
            return true;
        }

        var n = port.Name.ToLowerInvariant();
        if (n.EndsWith("path", StringComparison.Ordinal) || n.EndsWith("file", StringComparison.Ordinal) ||
            n.EndsWith("filename", StringComparison.Ordinal) || IsFolderName(n) || n == "executable")
        {
            return true;
        }

        if (n == "source" || n == "destination")
        {
            var owner = DefinitionName(port);
            return owner.StartsWith("File.", StringComparison.Ordinal) || owner.StartsWith("Directory.", StringComparison.Ordinal) ||
                   owner.StartsWith("Zip.", StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>True when a (lower-case) port name is that of a folder: it ends in "folder", "directory" or "dir".</summary>
    /// <param name="lowerName">The port name in lower case.</param>
    public static bool IsFolderName(string lowerName) =>
        lowerName.EndsWith("folder", StringComparison.Ordinal) || lowerName.EndsWith("directory", StringComparison.Ordinal) ||
        lowerName.EndsWith("dir", StringComparison.Ordinal);

    private static bool IsSaveName(string lowerName) =>
        lowerName.Contains("output") || lowerName.Contains("save") || lowerName.Contains("export") ||
        lowerName.Contains("target") || lowerName.Contains("destination");

    // The node's library name ("Text.WriteToFile"); empty for a node that is not from a zero-touch definition. The user may
    // rename a node on the canvas, the definition's name stays.
    private static string DefinitionName(PortModel port) =>
        (port.Owner as ZeroTouchNodeModel)?.Definition.Name ?? string.Empty;

    // A node that creates or writes a file: its operation (the part after the last dot) starts with Write, Save, Export, Append,
    // Snapshot or Create, or reads "To…File" (Table.ToCsvFile), or it belongs to Export.*. The role Modify is no help here:
    // Document.Open and Document.Merge are Modify and only read a file.
    private static bool IsWriter(string definitionName)
    {
        if (definitionName.Length == 0)
        {
            return false;
        }

        if (definitionName.StartsWith("Export.", StringComparison.Ordinal))
        {
            return true;
        }

        var dot = definitionName.LastIndexOf('.');
        var op = dot >= 0 ? definitionName.Substring(dot + 1) : definitionName;
        return op.StartsWith("Write", StringComparison.Ordinal) || op.StartsWith("Save", StringComparison.Ordinal) ||
               op.StartsWith("Export", StringComparison.Ordinal) || op.StartsWith("Append", StringComparison.Ordinal) ||
               op.StartsWith("Snapshot", StringComparison.Ordinal) || op.StartsWith("Create", StringComparison.Ordinal) ||
               (op.StartsWith("To", StringComparison.Ordinal) && op.EndsWith("File", StringComparison.Ordinal));
    }

    private static PathPick Make(NodePathMode mode, string? filter, string portName)
    {
        switch (mode)
        {
            case NodePathMode.Folder:
                return new PathPick(mode, string.Empty, "Choose folder for '" + portName + "'");
            case NodePathMode.Save:
                return new PathPick(mode, filter, "Save file for '" + portName + "'");
            default:
                return new PathPick(mode, filter, "Choose file for '" + portName + "'");
        }
    }
}
