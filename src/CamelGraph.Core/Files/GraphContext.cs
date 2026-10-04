using System;
using System.IO;

namespace CamelGraph.Core.Files;

/// <summary>
/// Where the graph that is running lives, for the nodes that open or write files. A relative path typed into a file node
/// (<c>report.csv</c>, <c>..\data\rooms.xlsx</c>) means "next to the graph", not "next to <c>Roamer.exe</c>"; the host says what
/// "the graph's folder" is by setting <see cref="Folder"/> before a run (the editor: the folder of the open file, else
/// <c>Documents\CamelGraph</c>; the command-line runner and the Script Player: the script's folder). The file nodes then call
/// <see cref="PathResolver.Resolve(string)"/> on every path they are given.
/// </summary>
public static class GraphContext
{
    private static readonly object Gate = new object();
    private static string? _folder;

    /// <summary>
    /// The folder relative paths are resolved against, or null when the host did not say (then the process's current directory
    /// is used, which in Navisworks is the program folder). Set by the host around a run (see <see cref="Use"/>).
    /// </summary>
    public static string? Folder
    {
        get
        {
            lock (Gate)
            {
                return _folder;
            }
        }

        set
        {
            lock (Gate)
            {
                _folder = string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
    }

    /// <summary>
    /// Sets <see cref="Folder"/> until the returned scope is disposed, then puts back what it was. Wrap a run in it:
    /// <c>using (GraphContext.Use(GraphContext.FolderFor(path))) { engine.Run(graph); }</c>.
    /// </summary>
    /// <param name="folder">The folder for the run, or null for none.</param>
    public static IDisposable Use(string? folder)
    {
        string? previous;
        lock (Gate)
        {
            previous = _folder;
            _folder = string.IsNullOrWhiteSpace(folder) ? null : folder;
        }

        return new Scope(previous);
    }

    /// <summary>
    /// The folder a graph's relative paths belong to: the folder holding the graph file, or, for a graph that has not been saved
    /// (no file), <see cref="DefaultFolder"/>.
    /// </summary>
    /// <param name="graphFilePath">The path of the graph file, or null or blank for an unsaved graph.</param>
    public static string FolderFor(string? graphFilePath)
    {
        if (!string.IsNullOrWhiteSpace(graphFilePath))
        {
            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(graphFilePath!));
                if (!string.IsNullOrEmpty(directory))
                {
                    return directory!;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException ||
                                       ex is System.Security.SecurityException)
            {
                // A path that cannot be resolved has no folder worth using.
            }
        }

        return DefaultFolder;
    }

    /// <summary>
    /// The folder for a graph with no file: <c>Documents\CamelGraph</c> in the user's own Documents folder. It is not created
    /// here; a node that writes a file creates the folders it needs.
    /// </summary>
    public static string DefaultFolder
    {
        get
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrEmpty(documents) ? Directory.GetCurrentDirectory() : Path.Combine(documents, "CamelGraph");
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        public Scope(string? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (Gate)
            {
                _folder = _previous;
            }
        }
    }
}
