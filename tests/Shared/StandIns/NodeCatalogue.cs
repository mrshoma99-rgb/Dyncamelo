using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CamelGraph.TestSupport.StandIns;

/// <summary>One port of a node as <c>docs/camelgraph-nodes.json</c> lists it.</summary>
internal sealed class CataloguePort
{
    public CataloguePort(string name, string type, string description, string? defaultText, bool multiInput)
    {
        Name = name;
        Type = type;
        Description = description;
        DefaultText = defaultText;
        MultiInput = multiInput;
    }

    public string Name { get; }

    /// <summary>The catalogue's own type vocabulary: <c>number</c>, <c>string</c>, <c>ModelItem[]</c>, <c>any</c>...</summary>
    public string Type { get; }

    public string Description { get; }

    /// <summary>The default as the catalogue writes it (<c>"Self"</c> with the quotes, <c>true</c>, <c>0.5</c>, <c>null</c>), or null for a required input.</summary>
    public string? DefaultText { get; }

    public bool HasDefault => DefaultText != null;

    public bool MultiInput { get; }
}

/// <summary>One node of <c>docs/camelgraph-nodes.json</c>.</summary>
internal sealed class CatalogueNode
{
    public CatalogueNode(
        string name,
        string id,
        string assembly,
        string category,
        string description,
        IReadOnlyList<string> tags,
        IReadOnlyList<CataloguePort> inputs,
        IReadOnlyList<CataloguePort> outputs,
        bool interactive)
    {
        Name = name;
        Id = id;
        Assembly = assembly;
        Category = category;
        Description = description;
        Tags = tags;
        Inputs = inputs;
        Outputs = outputs;
        Interactive = interactive;
    }

    /// <summary>The display name (<c>Search.ByProperty</c>, <c>Watch List</c>).</summary>
    public string Name { get; }

    /// <summary>
    /// What a saved graph calls the node: the definition id of a zero-touch node (<c>"DefinitionId"</c> in a .dyc file), the serialised
    /// type tag of an interactive one (<c>"NodeType"</c>). Empty for the Note annotation.
    /// </summary>
    public string Id { get; }

    /// <summary>The assembly a zero-touch node comes from (<c>CamelGraph.Navisworks</c>, <c>CamelGraph.Nodes</c>), or empty.</summary>
    public string Assembly { get; }

    public string Category { get; }

    public string Description { get; }

    public IReadOnlyList<string> Tags { get; }

    public IReadOnlyList<CataloguePort> Inputs { get; }

    public IReadOnlyList<CataloguePort> Outputs { get; }

    /// <summary>True for a hand-written <c>NodeModel</c> (inputs, Watch, Reroute...), false for a zero-touch static method.</summary>
    public bool Interactive { get; }

    /// <summary>True for a zero-touch node: it has a definition id with the parameter types after the '@'.</summary>
    public bool IsZeroTouch => !Interactive && Id.Length > 0;

    /// <summary><c>Namespace.Class.Method</c>, the part of the id before the '@'.</summary>
    public string IdPath
    {
        get
        {
            var at = Id.IndexOf('@');
            return at < 0 ? Id : Id.Substring(0, at);
        }
    }

    /// <summary>The parameter types of the id, in order, as the loader wrote them.</summary>
    public IReadOnlyList<string> IdParameterTypes
    {
        get
        {
            var at = Id.IndexOf('@');
            return at < 0 ? new List<string>() : TypeNames.SplitTopLevel(Id.Substring(at + 1), ',');
        }
    }

    public override string ToString() => Name;
}

/// <summary>The generated node catalogue, <c>docs/camelgraph-nodes.json</c>.</summary>
internal sealed class NodeCatalogue
{
    private NodeCatalogue(string version, IReadOnlyList<CatalogueNode> nodes)
    {
        Version = version;
        Nodes = nodes;
    }

    public string Version { get; }

    public IReadOnlyList<CatalogueNode> Nodes { get; }

    /// <summary>Reads the catalogue of this repository.</summary>
    public static NodeCatalogue Load() => Load(RepoLocator.CataloguePath());

    /// <summary>Reads a catalogue file.</summary>
    public static NodeCatalogue Load(string path)
    {
        var json = JObject.Parse(File.ReadAllText(path));
        var nodes = new List<CatalogueNode>();
        foreach (var token in (JArray)json["nodes"]!)
        {
            var n = (JObject)token;
            nodes.Add(new CatalogueNode(
                n.Value<string>("name") ?? string.Empty,
                n.Value<string>("id") ?? string.Empty,
                n.Value<string>("assembly") ?? string.Empty,
                n.Value<string>("category") ?? string.Empty,
                n.Value<string>("description") ?? string.Empty,
                ((n["tags"] as JArray) ?? new JArray()).Select(t => t.ToString()).ToList(),
                ReadPorts(n["inputs"] as JArray),
                ReadPorts(n["outputs"] as JArray),
                n.Value<bool?>("interactive") == true));
        }

        return new NodeCatalogue(json.Value<string>("version") ?? string.Empty, nodes);
    }

    private static List<CataloguePort> ReadPorts(JArray? ports)
    {
        var list = new List<CataloguePort>();
        if (ports == null)
        {
            return list;
        }

        foreach (var token in ports)
        {
            var p = (JObject)token;
            list.Add(new CataloguePort(
                p.Value<string>("name") ?? string.Empty,
                p.Value<string>("type") ?? "any",
                p.Value<string>("description") ?? string.Empty,
                p.Value<string>("default"),
                p.Value<bool?>("multiInput") == true));
        }

        return list;
    }
}
