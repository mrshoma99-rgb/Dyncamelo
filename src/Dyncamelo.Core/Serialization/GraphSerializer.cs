using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Dyncamelo.Core.Serialization;

/// <summary>
/// Reads and writes the .dyc graph format: a versioned JSON envelope with
/// separate model and view concerns. The reader is tolerant — unknown fields
/// are ignored, unknown node types degrade to <see cref="MissingNodeModel"/>
/// placeholders that round-trip their original JSON — and only refuses files
/// whose <c>MinReaderVersion</c> exceeds what this reader supports.
/// Cached values and node states are never persisted.
/// </summary>
public class GraphSerializer
{
    /// <summary>Highest .dyc format version this serializer writes and fully understands.</summary>
    public const int CurrentFormatVersion = 1;

    private readonly NodeRegistry _registry;

    /// <summary>Creates a serializer bound to a node registry.</summary>
    /// <param name="registry">Registry used to resolve node types and zero-touch definitions on load.</param>
    public GraphSerializer(NodeRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>Serializes a graph to indented .dyc JSON.</summary>
    /// <param name="graph">The graph to save.</param>
    public string Serialize(GraphModel graph)
    {
        if (graph == null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        var root = new JObject
        {
            ["Dyncamelo"] = new JObject
            {
                ["FormatVersion"] = CurrentFormatVersion,
                ["MinReaderVersion"] = 1,
                ["AppVersion"] = "0.1.0",
            },
            ["Uuid"] = graph.Uuid.ToString("N"),
            ["Name"] = graph.Name,
            ["Description"] = graph.Description,
            ["Nodes"] = new JArray(graph.Nodes.Select(SerializeNode)),
            ["Connectors"] = new JArray(graph.Connections.Select(SerializeConnection)),
            ["Notes"] = new JArray(graph.Notes.Select(SerializeNote)),
            ["Groups"] = new JArray(graph.Groups.Select(SerializeGroup)),
            ["View"] = new JObject
            {
                ["RunType"] = graph.RunType.ToString(),
                ["Camera"] = new JObject { ["X"] = 0d, ["Y"] = 0d, ["Zoom"] = 1d },
            },
        };

        return root.ToString(Formatting.Indented);
    }

    /// <summary>Serializes a graph and writes it to a file (UTF-8).</summary>
    /// <param name="graph">The graph to save.</param>
    /// <param name="path">Destination file path (conventionally *.dyc).</param>
    public void SaveToFile(GraphModel graph, string path)
    {
        File.WriteAllText(path, Serialize(graph));
    }

    /// <summary>Parses .dyc JSON into a graph. All nodes load dirty (states and cached values are not persisted).</summary>
    /// <param name="json">.dyc file content.</param>
    /// <exception cref="GraphFormatException">The content is not a readable .dyc document.</exception>
    public GraphModel Deserialize(string json)
    {
        if (json == null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new GraphFormatException("The file is not valid JSON.", ex);
        }

        var envelope = root["Dyncamelo"] as JObject;
        if (envelope == null)
        {
            throw new GraphFormatException("The file is not a Dyncamelo .dyc document (missing 'Dyncamelo' envelope).");
        }

        var minReaderVersion = envelope.Value<int?>("MinReaderVersion") ?? 1;
        if (minReaderVersion > CurrentFormatVersion)
        {
            throw new GraphFormatException(
                "The file requires .dyc reader version " + minReaderVersion.ToString(CultureInfo.InvariantCulture) +
                " but this application supports version " + CurrentFormatVersion.ToString(CultureInfo.InvariantCulture) + ".");
        }

        var graph = new GraphModel
        {
            Name = root.Value<string>("Name") ?? string.Empty,
            Description = root.Value<string>("Description") ?? string.Empty,
        };

        if (TryParseGuid(root.Value<string>("Uuid"), out var uuid))
        {
            graph.Uuid = uuid;
        }

        var view = root["View"] as JObject;
        if (view != null &&
            Enum.TryParse<RunType>(view.Value<string>("RunType") ?? string.Empty, ignoreCase: true, out var runType))
        {
            graph.RunType = runType;
        }

        var nodesById = new Dictionary<Guid, NodeModel>();
        if (root["Nodes"] is JArray nodes)
        {
            foreach (var token in nodes.OfType<JObject>())
            {
                var node = DeserializeNode(token);
                graph.AddNode(node);
                nodesById[node.Id] = node;
            }
        }

        if (root["Connectors"] is JArray connectors)
        {
            foreach (var token in connectors.OfType<JObject>())
            {
                RestoreConnection(graph, nodesById, token);
            }
        }

        if (root["Notes"] is JArray notes)
        {
            foreach (var token in notes.OfType<JObject>())
            {
                var note = new NoteModel
                {
                    Text = token.Value<string>("Text") ?? string.Empty,
                    X = token.Value<double?>("X") ?? 0d,
                    Y = token.Value<double?>("Y") ?? 0d,
                };
                if (TryParseGuid(token.Value<string>("Id"), out var noteId))
                {
                    note.Id = noteId;
                }

                graph.Notes.Add(note);
            }
        }

        // "Groups" is an additive field (format version 1 stays readable by
        // older applications, which simply ignore it).
        if (root["Groups"] is JArray groups)
        {
            foreach (var token in groups.OfType<JObject>())
            {
                var group = new GroupModel
                {
                    Title = token.Value<string>("Title") ?? "Group",
                    X = token.Value<double?>("X") ?? 0d,
                    Y = token.Value<double?>("Y") ?? 0d,
                    Width = token.Value<double?>("Width") ?? 200d,
                    Height = token.Value<double?>("Height") ?? 200d,
                    Color = token.Value<string>("Color") ?? GroupModel.DefaultColor,
                };
                if (TryParseGuid(token.Value<string>("Id"), out var groupId))
                {
                    group.Id = groupId;
                }

                graph.Groups.Add(group);
            }
        }

        return graph;
    }

    /// <summary>
    /// Serializes a set of nodes plus the connections that run among them into a
    /// standalone JSON fragment (used by copy/paste and duplicate). Connections
    /// touching nodes outside the set are omitted.
    /// </summary>
    /// <param name="nodes">The nodes to serialize. They may belong to any graph (or none).</param>
    public string SerializeFragment(IReadOnlyCollection<NodeModel> nodes)
    {
        if (nodes == null)
        {
            throw new ArgumentNullException(nameof(nodes));
        }

        var nodeSet = new HashSet<NodeModel>(nodes);
        var connections = nodes
            .Select(n => n.Graph)
            .FirstOrDefault(g => g != null)?
            .Connections
            .Where(c => nodeSet.Contains(c.SourceNode) && nodeSet.Contains(c.TargetNode))
            ?? Enumerable.Empty<ConnectionModel>();

        var root = new JObject
        {
            ["Dyncamelo"] = new JObject
            {
                ["FormatVersion"] = CurrentFormatVersion,
                ["MinReaderVersion"] = 1,
                ["Fragment"] = true,
            },
            ["Nodes"] = new JArray(nodes.Select(SerializeNode)),
            ["Connectors"] = new JArray(connections.Select(SerializeConnection)),
        };

        return root.ToString(Formatting.Indented);
    }

    /// <summary>
    /// Materializes a fragment produced by <see cref="SerializeFragment"/> into a
    /// graph: every node gets a fresh identifier and is offset by the given
    /// amount; connections among the pasted nodes are re-created. The same
    /// fragment can be pasted any number of times.
    /// </summary>
    /// <param name="target">The graph receiving the pasted nodes.</param>
    /// <param name="json">Fragment JSON.</param>
    /// <param name="offsetX">Horizontal offset applied to every pasted node.</param>
    /// <param name="offsetY">Vertical offset applied to every pasted node.</param>
    /// <returns>The pasted nodes, in fragment order.</returns>
    /// <exception cref="GraphFormatException">The content is not a readable fragment.</exception>
    public IReadOnlyList<NodeModel> PasteFragment(GraphModel target, string json, double offsetX, double offsetY)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (json == null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new GraphFormatException("The fragment is not valid JSON.", ex);
        }

        var pasted = new List<NodeModel>();
        var nodesByOriginalId = new Dictionary<Guid, NodeModel>();
        if (root["Nodes"] is JArray nodes)
        {
            foreach (var token in nodes.OfType<JObject>())
            {
                var node = DeserializeNode(token);
                if (node.Id != Guid.Empty && !nodesByOriginalId.ContainsKey(node.Id))
                {
                    nodesByOriginalId[node.Id] = node;
                }

                node.Id = Guid.NewGuid();
                node.X += offsetX;
                node.Y += offsetY;
                target.AddNode(node);
                pasted.Add(node);
            }
        }

        if (root["Connectors"] is JArray connectors)
        {
            foreach (var token in connectors.OfType<JObject>())
            {
                RestoreConnection(target, nodesByOriginalId, token, restoreId: false);
            }
        }

        // Inputs that were fed by nodes outside the fragment lost their wire:
        // let them fall back to their default value instead of pasting a node
        // with a missing required input.
        foreach (var node in pasted)
        {
            foreach (var port in node.InPorts)
            {
                if (port.HasDefault && !port.UsingDefaultValue && target.FindConnectionInto(port) == null)
                {
                    port.UsingDefaultValue = true;
                }
            }
        }

        return pasted;
    }

    /// <summary>Loads a graph from a .dyc file.</summary>
    /// <param name="path">Path to the file.</param>
    /// <exception cref="GraphFormatException">The content is not a readable .dyc document.</exception>
    public GraphModel LoadFromFile(string path)
    {
        return Deserialize(File.ReadAllText(path));
    }

    private static JObject SerializeNode(NodeModel node)
    {
        // Placeholders round-trip their original JSON so no data is ever lost;
        // only mutable cosmetics (position, name, lacing, frozen flag) are refreshed.
        if (node is MissingNodeModel missing)
        {
            var preserved = (JObject)missing.RawJson.DeepClone();
            preserved["Id"] = node.Id.ToString("N");
            preserved["Name"] = node.Name;
            preserved["X"] = node.X;
            preserved["Y"] = node.Y;
            preserved["Lacing"] = node.Lacing.ToString();
            preserved["IsFrozen"] = node.IsFrozen;
            return preserved;
        }

        var data = new JObject();
        node.SerializeData(data);

        var json = new JObject
        {
            ["Id"] = node.Id.ToString("N"),
            ["NodeType"] = node.NodeType,
        };

        if (node is ZeroTouchNodeModel zeroTouch)
        {
            json["DefinitionId"] = zeroTouch.Definition.Id;
            json["Assembly"] = zeroTouch.Definition.AssemblyName;
        }

        json["Name"] = node.Name;
        json["X"] = node.X;
        json["Y"] = node.Y;
        json["Lacing"] = node.Lacing.ToString();
        json["IsFrozen"] = node.IsFrozen;
        json["InputPorts"] = new JArray(node.InPorts.Select(p => new JObject
        {
            ["Name"] = p.Name,
            ["UsingDefaultValue"] = p.UsingDefaultValue,
            ["Level"] = p.Level,
            ["UseLevels"] = p.UseLevels,
            ["KeepListStructure"] = p.KeepListStructure,
        }));
        json["OutputPorts"] = new JArray(node.OutPorts.Select(p => new JObject
        {
            ["Name"] = p.Name,
        }));
        json["Data"] = data;
        return json;
    }

    private static JObject SerializeConnection(ConnectionModel connection)
    {
        return new JObject
        {
            ["Id"] = connection.Id.ToString("N"),
            ["FromNode"] = connection.SourceNode.Id.ToString("N"),
            ["FromPort"] = connection.Source.Name,
            ["ToNode"] = connection.TargetNode.Id.ToString("N"),
            ["ToPort"] = connection.Target.Name,
        };
    }

    private static JObject SerializeNote(NoteModel note)
    {
        return new JObject
        {
            ["Id"] = note.Id.ToString("N"),
            ["Text"] = note.Text,
            ["X"] = note.X,
            ["Y"] = note.Y,
        };
    }

    private static JObject SerializeGroup(GroupModel group)
    {
        return new JObject
        {
            ["Id"] = group.Id.ToString("N"),
            ["Title"] = group.Title,
            ["X"] = group.X,
            ["Y"] = group.Y,
            ["Width"] = group.Width,
            ["Height"] = group.Height,
            ["Color"] = group.Color,
        };
    }

    private NodeModel DeserializeNode(JObject json)
    {
        var nodeType = json.Value<string>("NodeType") ?? string.Empty;
        NodeModel? node = null;

        if (nodeType == ZeroTouchNodeModel.TypeName)
        {
            var definitionId = json.Value<string>("DefinitionId") ?? string.Empty;
            node = _registry.CreateZeroTouchNode(definitionId);
            if (node == null)
            {
                node = new MissingNodeModel(json, "Unresolved zero-touch definition '" + definitionId + "'.");
            }
        }
        else
        {
            node = _registry.CreateNode(nodeType);
            if (node == null)
            {
                node = new MissingNodeModel(json, "Unknown node type '" + nodeType + "'.");
            }
        }

        // Restore the node's private payload before anything else: DeserializeData
        // may rebuild ports. A corrupt payload degrades the node to a placeholder
        // (preserving its JSON) instead of aborting the whole graph open.
        if (!(node is MissingNodeModel) && json["Data"] is JObject data)
        {
            try
            {
                node.DeserializeData(data);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException) && !(ex is StackOverflowException))
            {
                node = new MissingNodeModel(json, "The node's saved data could not be read: " + ex.Message);
            }
        }

        if (TryParseGuid(json.Value<string>("Id"), out var id))
        {
            node.Id = id;
        }

        var name = json.Value<string>("Name");
        if (!string.IsNullOrEmpty(name))
        {
            node.Name = name!;
        }

        node.X = json.Value<double?>("X") ?? 0d;
        node.Y = json.Value<double?>("Y") ?? 0d;
        node.IsFrozen = json.Value<bool?>("IsFrozen") ?? false;

        if (Enum.TryParse<LacingMode>(json.Value<string>("Lacing") ?? string.Empty, ignoreCase: true, out var lacing))
        {
            node.Lacing = lacing;
        }

        if (!(node is MissingNodeModel))
        {
            // Restore per-port persisted flags by port name.
            if (json["InputPorts"] is JArray inputPorts)
            {
                foreach (var portJson in inputPorts.OfType<JObject>())
                {
                    var portName = portJson.Value<string>("Name");
                    var port = node.InPorts.FirstOrDefault(p => p.Name == portName);
                    if (port == null)
                    {
                        continue;
                    }

                    if (port.HasDefault)
                    {
                        port.UsingDefaultValue = portJson.Value<bool?>("UsingDefaultValue") ?? port.UsingDefaultValue;
                    }

                    port.Level = portJson.Value<int?>("Level") ?? -1;
                    port.UseLevels = portJson.Value<bool?>("UseLevels") ?? false;
                    port.KeepListStructure = portJson.Value<bool?>("KeepListStructure") ?? false;
                }
            }
        }

        return node;
    }

    private static void RestoreConnection(GraphModel graph, Dictionary<Guid, NodeModel> nodesById, JObject json, bool restoreId = true)
    {
        if (!TryParseGuid(json.Value<string>("FromNode"), out var fromNodeId) ||
            !TryParseGuid(json.Value<string>("ToNode"), out var toNodeId) ||
            !nodesById.TryGetValue(fromNodeId, out var fromNode) ||
            !nodesById.TryGetValue(toNodeId, out var toNode))
        {
            return; // tolerate dangling connectors
        }

        var fromPort = fromNode.OutPorts.FirstOrDefault(p => p.Name == json.Value<string>("FromPort"));
        var toPort = toNode.InPorts.FirstOrDefault(p => p.Name == json.Value<string>("ToPort"));
        if (fromPort == null || toPort == null)
        {
            return;
        }

        var result = graph.Connect(fromPort, toPort);
        if (result.Success && restoreId && TryParseGuid(json.Value<string>("Id"), out var connectionId))
        {
            result.Connection!.Id = connectionId;
        }
    }

    private static bool TryParseGuid(string? text, out Guid guid)
    {
        if (!string.IsNullOrEmpty(text) && Guid.TryParse(text, out guid))
        {
            return true;
        }

        guid = Guid.Empty;
        return false;
    }
}
