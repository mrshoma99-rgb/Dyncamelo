using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dyncamelo.Core.Loader;
using Dyncamelo.Nodes;

namespace Dyncamelo.Cli;

/// <summary>
/// Builds the node registry the CLI runs against: Core's built-in interactive
/// nodes, the whole Dyncamelo.Nodes library, plus any extra node packs the
/// user pointed at with <c>--pack</c>.
/// </summary>
internal static class RegistryBuilder
{
    /// <summary>Creates a fully-populated registry.</summary>
    /// <param name="packPaths">Paths to node-pack assemblies or directories containing them.</param>
    /// <param name="log">Sink for one line per loaded pack (null to stay quiet).</param>
    /// <exception cref="CliError">A pack path does not exist or is not loadable.</exception>
    public static NodeRegistry Build(IEnumerable<string> packPaths, TextWriter? log = null)
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        foreach (var assemblyPath in ExpandPackPaths(packPaths))
        {
            List<NodeDefinition> definitions;
            try
            {
                definitions = AssemblyNodeLoader.LoadFrom(assemblyPath);
            }
            catch (BadImageFormatException)
            {
                throw new CliError("Pack '" + assemblyPath + "' is not a managed .NET assembly.");
            }
            catch (Exception ex) when (ex is FileLoadException || ex is FileNotFoundException)
            {
                throw new CliError("Pack '" + assemblyPath + "' could not be loaded: " + ex.Message);
            }

            registry.RegisterDefinitions(definitions);
            log?.WriteLine("Loaded pack " + Path.GetFileName(assemblyPath) + " (" + definitions.Count + " nodes).");
        }

        return registry;
    }

    /// <summary>Expands each pack path: a file is taken as-is, a directory yields its *.dll files.</summary>
    private static IEnumerable<string> ExpandPackPaths(IEnumerable<string> packPaths)
    {
        foreach (var packPath in packPaths)
        {
            if (File.Exists(packPath))
            {
                yield return Path.GetFullPath(packPath);
            }
            else if (Directory.Exists(packPath))
            {
                var dlls = Directory.EnumerateFiles(packPath, "*.dll").OrderBy(p => p, StringComparer.Ordinal).ToList();
                if (dlls.Count == 0)
                {
                    throw new CliError("Pack directory '" + packPath + "' contains no .dll files.");
                }

                foreach (var dll in dlls)
                {
                    yield return Path.GetFullPath(dll);
                }
            }
            else
            {
                throw new CliError("Pack path '" + packPath + "' does not exist.");
            }
        }
    }
}
