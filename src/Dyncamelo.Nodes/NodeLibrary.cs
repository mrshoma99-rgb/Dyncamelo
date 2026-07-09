using System;
using System.Collections.Generic;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes;

/// <summary>
/// Entry point for hosts: registers everything in this assembly with a
/// <see cref="NodeRegistry"/> — the interactive <c>NodeModel</c> nodes
/// (List.Create, Watch List, Color Picker) and all zero-touch static nodes.
/// </summary>
[IsVisibleInLibrary(false)]
public static class NodeLibrary
{
    /// <summary>
    /// Registers all Dyncamelo.Nodes node types and zero-touch definitions.
    /// </summary>
    /// <param name="registry">The registry to populate (e.g. <c>NodeRegistry.CreateDefault()</c>).</param>
    /// <returns>The zero-touch definitions that were registered.</returns>
    public static List<NodeDefinition> RegisterAll(NodeRegistry registry)
    {
        if (registry == null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        registry.RegisterNodeType(ListCreateNode.TypeName, () => new ListCreateNode());
        registry.RegisterNodeType(WatchListNode.TypeName, () => new WatchListNode());
        registry.RegisterNodeType(ColorPickerNode.TypeName, () => new ColorPickerNode());
        return registry.RegisterAssembly(typeof(NodeLibrary).Assembly);
    }
}
