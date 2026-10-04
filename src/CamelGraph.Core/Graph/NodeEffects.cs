using System;

namespace CamelGraph.Core.Graph;

/// <summary>
/// What a node can do to the computer or the Navisworks model, in ways a person opening a graph from somewhere else would want to
/// know about before it runs. Declared with <see cref="CamelGraph.Core.Loader.NodeEffectsAttribute"/>; the editor and the Script Player
/// warn before running a graph from a file that holds such nodes.
/// </summary>
[Flags]
public enum NodeEffects
{
    /// <summary>Nothing beyond reading and computing.</summary>
    None = 0,

    /// <summary>Starts another program, or opens a file or address with the program Windows associates with it.</summary>
    RunsPrograms = 1,

    /// <summary>Sends or fetches data over the network.</summary>
    UsesNetwork = 2,

    /// <summary>Deletes, moves, copies over or extracts over existing files and folders.</summary>
    ChangesFiles = 4,

    /// <summary>
    /// Writes a file: creates it, replaces it, or adds to it. A node that also deletes, moves or copies over existing files carries
    /// <see cref="ChangesFiles"/> as well (or instead).
    /// </summary>
    WritesFiles = 8,

    /// <summary>
    /// Changes the open Navisworks model or its saved content: selection sets, clash tests, viewpoints, TimeLiner tasks, properties,
    /// appearance overrides and the like. The Script Player lists a node of this kind (and any node whose role is Modify) before it
    /// first runs a script.
    /// </summary>
    ChangesModel = 16,
}
