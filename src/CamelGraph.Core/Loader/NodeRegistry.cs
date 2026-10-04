using System;
using System.Collections.Generic;
using System.Reflection;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Nodes;

namespace CamelGraph.Core.Loader;

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
        registry.RegisterNodeType(IntegerInputNode.TypeName, () => new IntegerInputNode());
        registry.RegisterNodeType(DateInputNode.TypeName, () => new DateInputNode());
        registry.RegisterNodeType(ChoiceInputNode.TypeName, () => new ChoiceInputNode());
        registry.RegisterNodeType(BooleanToggleNode.TypeName, () => new BooleanToggleNode());
        registry.RegisterNodeType(FilePathNode.TypeName, () => new FilePathNode());
        registry.RegisterNodeType(DirectoryPathNode.TypeName, () => new DirectoryPathNode());
        registry.RegisterNodeType(WatchNode.TypeName, () => new WatchNode());
        registry.RegisterNodeType(LoopItemNode.TypeName, () => new LoopItemNode());
        registry.RegisterNodeType(LoopCollectNode.TypeName, () => new LoopCollectNode());
        registry.RegisterNodeType(RerouteNode.TypeName, () => new RerouteNode());
        registry.RegisterNodeType(CamelGraph.Core.Groups.GroupInputNode.TypeName, () => new CamelGraph.Core.Groups.GroupInputNode());
        registry.RegisterNodeType(CamelGraph.Core.Groups.GroupOutputNode.TypeName, () => new CamelGraph.Core.Groups.GroupOutputNode());
        registry.RegisterNodeType(CamelGraph.Core.Groups.GroupInstanceNode.TypeName, () => new CamelGraph.Core.Groups.GroupInstanceNode());
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
    /// Ids written before the product was renamed (they start with "Dyncamelo.") are
    /// upgraded to the current namespace and tried again.
    /// </summary>
    /// <param name="definitionId">The mangled function signature (current or legacy).</param>
    /// <param name="definition">The definition when found.</param>
    /// <returns>True when the definition is registered.</returns>
    public bool TryGetDefinition(string definitionId, out NodeDefinition? definition)
    {
        if (_definitions.TryGetValue(definitionId, out definition) || _aliases.TryGetValue(definitionId, out definition))
        {
            return true;
        }

        var upgraded = UpgradeLegacyId(definitionId);
        return !ReferenceEquals(upgraded, definitionId)
            && (_definitions.TryGetValue(upgraded, out definition) || _aliases.TryGetValue(upgraded, out definition));
    }

    /// <summary>
    /// Maps a definition id saved under the product's old name (Dyncamelo, up to version 0.48) to the current one: the namespace
    /// prefix and the names of the value types (DyncameloPoint, DyncameloTable, …) in the signature change from "Dyncamelo" to
    /// "CamelGraph". An id that does not start with the old namespace is returned as it is.
    /// </summary>
    /// <param name="definitionId">A definition id, current or from before the rename.</param>
    public static string UpgradeLegacyId(string definitionId)
    {
        return definitionId != null && definitionId.StartsWith(LegacyNamespacePrefix, StringComparison.Ordinal)
            ? definitionId.Replace("Dyncamelo", "CamelGraph")
            : definitionId!;
    }

    private const string LegacyNamespacePrefix = "Dyncamelo.";

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
