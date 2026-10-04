using System;

namespace CamelGraph.Core.Graph;

/// <summary>
/// What a node can do outside the Navisworks model and the graph, in ways a person opening a graph from somewhere else would want to
/// know about before it runs. Declared with <see cref="CamelGraph.Core.Loader.NodeEffectsAttribute"/>; the editor and the Script Player
/// warn before running a graph from a file that holds such nodes.
/// </summary>
[Flags]
public enum NodeEffects
{
    /// <summary>Nothing beyond reading and computing (and writing new files of its own, which is not warned about).</summary>
    None = 0,

    /// <summary>Starts another program, or opens a file or address with the program Windows associates with it.</summary>
    RunsPrograms = 1,

    /// <summary>Sends or fetches data over the network.</summary>
    UsesNetwork = 2,

    /// <summary>Deletes, moves, copies over or extracts over existing files and folders.</summary>
    ChangesFiles = 4,
}
