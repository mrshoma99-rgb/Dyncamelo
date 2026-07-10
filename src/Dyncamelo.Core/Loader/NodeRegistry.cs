using System;
using System.Collections.Generic;
using System.Reflection;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;

namespace Dyncamelo.Core.Loader;

/// <summary>
/// Central catalog used by serialization and the library browser: maps
/// zero-touch definition ids to <see cref="NodeDefinition"/>s and logical node
/// type tags (e.g. "NumberSlider") to factories for hand-written
/// <see cref="NodeModel"/> subclasses.
/// </summary>
public class NodeRegistry
{
    private readonly Dictionary<string, NodeDefinition> _definitions =
        new Dictionary<string, NodeDefinition>(StringComparer.Ordinal);

    /// <summary>
    /// Legacy definition ids (see <see cref="NodeAliasesAttribute"/>) mapped to
    /// their current definitions. Consulted only when no definition owns the id
    /// exactly, so an alias can never shadow a live definition.
    /// </summary>
    private readonly Dictionary<string, NodeDefinition> _aliases =
        new Dictionary<string, NodeDefinition>(StringComparer.Ordinal);

    private readonly Dictionary<string, Func<NodeModel>> _factories =
        new Dictionary<string, Func<NodeModel>>(StringComparer.Ordinal);

    /// <summary>All registered zero-touch definitions (library browser source).</summary>
    public IReadOnlyCollection<NodeDefinition> Definitions => _definitions.Values;

    /// <summary>All registered logical node type tags.</summary>
    public IReadOnlyCollection<string> NodeTypes => _factories.Keys;

    /// <summary>
    /// Creates a registry pre-populated with the built-in interactive nodes
    /// (sliders, inputs, watch).
    /// </summary>
    public static NodeRegistry CreateDefault()
    {
        var registry = new NodeRegistry();
        registry.RegisterNodeType(NumberInputNode.TypeName, () => new NumberInputNode());
        registry.RegisterNodeType(IntegerSliderNode.TypeName, () => new IntegerSliderNode());
        registry.RegisterNodeType(NumberSliderNode.TypeName, () => new NumberSliderNode());
        registry.RegisterNodeType(StringInputNode.TypeName, () => new StringInputNode());
        registry.RegisterNodeType(BooleanToggleNode.TypeName, () => new BooleanToggleNode());
        registry.RegisterNodeType(FilePathNode.TypeName, () => new FilePathNode());
        registry.RegisterNodeType(DirectoryPathNode.TypeName, () => new DirectoryPathNode());
        registry.RegisterNodeType(WatchNode.TypeName, () => new WatchNode());
        return registry;
    }

    /// <summary>Registers (or replaces) a zero-touch definition.</summary>
    /// <param name="definition">The definition to register.</param>
    public void RegisterDefinition(NodeDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        _definitions[definition.Id] = definition;
        foreach (var alias in definition.Aliases)
        {
            if (!string.IsNullOrEmpty(alias))
            {
                _aliases[alias] = definition;
            }
        }
    }

    /// <summary>Registers many zero-touch definitions.</summary>
    /// <param name="definitions">The definitions to register.</param>
    public void RegisterDefinitions(IEnumerable<NodeDefinition> definitions)
    {
        if (definitions == null)
        {
            throw new ArgumentNullException(nameof(definitions));
        }

        foreach (var definition in definitions)
        {
            RegisterDefinition(definition);
        }
    }

    /// <summary>
    /// Discovers and registers all zero-touch nodes in an assembly, and runs the
    /// assembly's <see cref="TypeConverterRegistrationAttribute"/> hooks (once per
    /// process) so its custom type converters are available to connection checks
    /// and runtime coercion.
    /// </summary>
    /// <param name="assembly">The node-pack assembly.</param>
    /// <returns>The definitions that were registered.</returns>
    public List<NodeDefinition> RegisterAssembly(Assembly assembly)
    {
        AssemblyNodeLoader.RunConverterRegistrations(assembly);
        var definitions = AssemblyNodeLoader.LoadFrom(assembly);
        RegisterDefinitions(definitions);
        return definitions;
    }

    /// <summary>Registers (or replaces) a factory for a hand-written node type.</summary>
    /// <param name="nodeType">Logical type tag persisted in .dyc files.</param>
    /// <param name="factory">Creates a fresh node instance.</param>
    public void RegisterNodeType(string nodeType, Func<NodeModel> factory)
    {
        if (string.IsNullOrEmpty(nodeType))
        {
            throw new ArgumentNullException(nameof(nodeType));
        }

        _factories[nodeType] = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Looks up a zero-touch definition by id. Ids that miss the live catalog
    /// fall back to registered legacy aliases, so graphs saved before a node's
    /// signature changed keep resolving (they migrate to the current id on save).
    /// </summary>
    /// <param name="definitionId">The mangled function signature (current or legacy).</param>
    /// <param name="definition">The definition when found.</param>
    /// <returns>True when the definition is registered.</returns>
    public bool TryGetDefinition(string definitionId, out NodeDefinition? definition)
    {
        return _definitions.TryGetValue(definitionId, out definition)
            || _aliases.TryGetValue(definitionId, out definition);
    }

    /// <summary>
    /// Instantiates a zero-touch node for a registered definition id (current
    /// or legacy alias), or null when the id is unknown.
    /// </summary>
    /// <param name="definitionId">The mangled function signature (current or legacy).</param>
    public ZeroTouchNodeModel? CreateZeroTouchNode(string definitionId)
    {
        return TryGetDefinition(definitionId, out var definition)
            ? new ZeroTouchNodeModel(definition!)
            : null;
    }

    /// <summary>Instantiates a hand-written node by its logical type tag, or null when unregistered.</summary>
    /// <param name="nodeType">Logical type tag (e.g. "NumberSlider").</param>
    public NodeModel? CreateNode(string nodeType)
    {
        return _factories.TryGetValue(nodeType, out var factory) ? factory() : null;
    }
}
