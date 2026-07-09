using System.IO;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Serialization;

namespace Dyncamelo.Cli;

/// <summary>Loads .dyc files with CLI-friendly error reporting.</summary>
internal static class GraphIo
{
    /// <summary>Reads a .dyc file's text.</summary>
    /// <exception cref="CliError">The file does not exist.</exception>
    public static string ReadGraphText(string path)
    {
        if (!File.Exists(path))
        {
            throw new CliError("Graph file '" + path + "' does not exist.");
        }

        return File.ReadAllText(path);
    }

    /// <summary>Deserializes .dyc text into a graph.</summary>
    /// <exception cref="CliError">The content is not a readable .dyc document.</exception>
    public static GraphModel Deserialize(string json, NodeRegistry registry, string path)
    {
        try
        {
            return new GraphSerializer(registry).Deserialize(json);
        }
        catch (GraphFormatException ex)
        {
            throw new CliError("Cannot read '" + path + "': " + ex.Message);
        }
    }

    /// <summary>Loads a graph from a .dyc file.</summary>
    /// <exception cref="CliError">The file is missing or unreadable.</exception>
    public static GraphModel Load(string path, NodeRegistry registry)
    {
        return Deserialize(ReadGraphText(path), registry, path);
    }
}
