using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Groups;
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
    /// <summary>
    /// Highest .dyc format version this serializer writes and fully understands. Version 2 added node groups; a file is only
    /// written as version 2 (and only refused by older readers) when it actually contains some.
    /// </summary>
    public const int CurrentFormatVersion = 2;

    private static readonly string AppVersion = ResolveAppVersion();

    private readonly NodeRegistry _registry;

    /// <summary>Creates a serializer bound to a node registry.</summary>
    /// <param name="registry">Registry used to resolve node types and zero-touch definitions on load.</param>
    public GraphSerializer(NodeRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Version stamped into the envelope's <c>AppVersion</c> field: the
    /// assembly's informational version (build metadata stripped), falling
    /// back to the plain assembly version.
    /// </summary>
    private static string ResolveAppVersion()
    {
        var assembly = typeof(GraphSerializer).Assembly;
        var informational = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault();
        var version = informational?.InformationalVersion;
        if (!string.IsNullOrEmpty(version))
        {
            int metadata = version!.IndexOf('+');
            return metadata >= 0 ? version.Substring(0, metadata) : version;
        }

        var name = assembly.GetName().Version;
        return name != null ? name.ToString(3) : "0.0.0";
    }

    /// <summary>Serializes a graph to indented .dyc JSON.</summary>
    /// <param name="graph">The graph to save.</param>
    public string Serialize(GraphModel graph)
    {
        if (graph == null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        var hasGroups = graph.NodeGroups.Groups.Count > 0;
        var version = hasGroups ? 2 : 1;
        var root = new JObject
        {
            ["Dyncamelo"] = new JObject
            {
                ["FormatVersion"] = version,
                ["MinReaderVersion"] = version,
                ["AppVersion"] = AppVersion,
            },
            ["Uuid"] = graph.Uuid.ToString("N"),
            ["Name"] = graph.Name,
            ["Description"] = graph.Description,
        };

        foreach (var property in SerializeBody(graph).Properties())
        {
            root.Add(property.Name, property.Value);
        }

        if (hasGroups)
        {
            root["NodeGroups"] = new JArray(graph.NodeGroups.Groups.Select(SerializeNodeGroup));
        }

        root["View"] = new JObject
        {
            ["RunType"] = graph.RunType.ToString(),
            ["Camera"] = new JObject { ["X"] = 0d, ["Y"] = 0d, ["Zoom"] = 1d },
        };

        return root.ToString(Formatting.Indented);
    }

    private readonly List<string> _loadWarnings = new List<string>();

    /// <summary>
    /// What the last load or paste could not restore, one sentence each: a wire or a typed-in value that belonged to an input or
    /// output the node no longer has (a node was changed since the file was saved). Empty when everything came back.
    /// </summary>
    public IReadOnlyList<string> LoadWarnings => _loadWarnings;

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

        _loadWarnings.Clear();

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

        if (root["NodeGroups"] is JArray nodeGroups)
        {
            LoadNodeGroups(graph.NodeGroups, nodeGroups, skipExisting: false);
        }

        LoadBody(graph, root, null);

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

        // Instances are useless without their definitions, so a fragment carries the groups it uses (and theirs).
        var used = new List<NodeGroup>();
        foreach (var instance in nodes.OfType<GroupInstanceNode>())
        {
            if (instance.Definition != null)
            {
                CollectGroups(instance.Definition, used);
            }
        }

        if (used.Count > 0)
        {
            root["NodeGroups"] = new JArray(used.Select(SerializeNodeGroup));
        }

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

        _loadWarnings.Clear();

        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new GraphFormatException("The fragment is not valid JSON.", ex);
        }

        // Definitions the target does not have yet come along (a paste into another document); ones it has are reused.
        if (root["NodeGroups"] is JArray fragmentGroups)
        {
            LoadNodeGroups(target.NodeGroups, fragmentGroups, skipExisting: true);
        }

        var pasted = new List<NodeModel>();
        var nodesByOriginalId = new Dictionary<Guid, NodeModel>();
        if (root["Nodes"] is JArray nodes)
        {
            foreach (var token in nodes.OfType<JObject>())
            {
                var node = DeserializeNode(token, target.NodeGroups, target.OwnerGroup);
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

    private void LoadBody(GraphModel graph, JObject root, NodeGroup? owner)
    {
        var nodesById = new Dictionary<Guid, NodeModel>();
        if (root["Nodes"] is JArray nodes)
        {
            foreach (var token in nodes.OfType<JObject>())
            {
                var node = DeserializeNode(token, graph.NodeGroups, owner);
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

        // "Bookmarks" is additive too: older applications ignore it.
        if (root["Bookmarks"] is JArray bookmarks)
        {
            foreach (var token in bookmarks.OfType<JObject>())
            {
                var bookmark = new BookmarkModel
                {
                    Name = token.Value<string>("Name") ?? "Bookmark",
                    X = token.Value<double?>("X") ?? 0d,
                    Y = token.Value<double?>("Y") ?? 0d,
                    Zoom = token.Value<double?>("Zoom") ?? 1d,
                };
                if (TryParseGuid(token.Value<string>("Id"), out var bookmarkId))
                {
                    bookmark.Id = bookmarkId;
                }

                graph.Bookmarks.Add(bookmark);
            }
        }
    }

    // The content every graph has, for a document and for the body of a node group alike.
    private static JObject SerializeBody(GraphModel graph)
    {
        var body = new JObject
        {
            ["Nodes"] = new JArray(graph.Nodes.Select(SerializeNode)),
            ["Connectors"] = new JArray(graph.Connections.Select(SerializeConnection)),
            ["Notes"] = new JArray(graph.Notes.Select(SerializeNote)),
            ["Groups"] = new JArray(graph.Groups.Select(SerializeGroup)),
        };
        if (graph.Bookmarks.Count > 0)
        {
            body["Bookmarks"] = new JArray(graph.Bookmarks.Select(SerializeBookmark));
        }

        return body;
    }

    private static void CollectGroups(NodeGroup group, List<NodeGroup> into)
    {
        if (into.Contains(group))
        {
            return;
        }

        // Dependencies first, so a reader that loads in order finds them.
        foreach (var inner in group.Graph.Nodes.OfType<GroupInstanceNode>())
        {
            if (inner.Definition != null)
            {
                CollectGroups(inner.Definition, into);
            }
        }

        into.Add(group);
    }

    private static JObject SerializeNodeGroup(NodeGroup group)
    {
        return new JObject
        {
            ["Id"] = group.Id.ToString("N"),
            ["Name"] = group.Name,
            ["Description"] = group.Description,
            ["Inputs"] = new JArray(group.Inputs.Select(SerializeSocket)),
            ["Outputs"] = new JArray(group.Outputs.Select(SerializeSocket)),
            ["Graph"] = SerializeBody(group.Graph),
        };
    }

    private static JObject SerializeSocket(GroupSocket socket)
    {
        return new JObject
        {
            ["Id"] = socket.Id.ToString("N"),
            ["Name"] = socket.Name,
            ["Kind"] = socket.Kind,
        };
    }

    /// <summary>
    /// Reads the node groups of a document (or of a pasted fragment, where groups the target already has are skipped). Two
    /// passes, so a group can contain instances of another that comes later in the file: every interface is read first, then
    /// every body.
    /// </summary>
    private List<NodeGroup> LoadNodeGroups(NodeGroupLibrary library, JArray groups, bool skipExisting)
    {
        var loaded = new List<(NodeGroup Group, JObject Body)>();
        foreach (var token in groups.OfType<JObject>())
        {
            if (!TryParseGuid(token.Value<string>("Id"), out var id) || (skipExisting && library.Find(id) != null))
            {
                continue;
            }

            var group = new NodeGroup(library, id, library.UniqueName(token.Value<string>("Name") ?? "Node Group"))
            {
                Description = token.Value<string>("Description") ?? string.Empty,
            };
            ReadSockets(group, SocketSide.Input, token["Inputs"] as JArray);
            ReadSockets(group, SocketSide.Output, token["Outputs"] as JArray);
            library.Add(group);
            loaded.Add((group, token["Graph"] as JObject ?? new JObject()));
        }

        foreach (var entry in loaded)
        {
            LoadBody(entry.Group.Graph, entry.Body, entry.Group);
            entry.Group.AdoptInterfaceNodes();
        }

        foreach (var entry in loaded)
        {
            if (entry.Group.Uses(entry.Group))
            {
                throw new GraphFormatException("The node group '" + entry.Group.Name + "' contains itself, so the file cannot be opened.");
            }
        }

        return loaded.Select(e => e.Group).ToList();
    }

    private static void ReadSockets(NodeGroup group, SocketSide side, JArray? sockets)
    {
        if (sockets == null)
        {
            return;
        }

        foreach (var token in sockets.OfType<JObject>())
        {
            var id = TryParseGuid(token.Value<string>("Id"), out var parsed) ? parsed : Guid.NewGuid();
            group.LoadSocket(side, id, token.Value<string>("Name") ?? string.Empty, token.Value<string>("Kind") ?? string.Empty);
        }
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
        if (node.IsMuted)
        {
            json["Muted"] = true;
        }

        if (!node.Ui.IsDefault)
        {
            json["Ui"] = SerializeUi(node.Ui);
        }

        if (node.PlayerExposed.HasValue)
        {
            json["Player"] = node.PlayerExposed.Value;
        }

        json["InputPorts"] = new JArray(node.InPorts.Select(SerializeInputPort));
        json["OutputPorts"] = new JArray(node.OutPorts.Select(p => new JObject
        {
            ["Name"] = p.Name,
        }));
        json["Data"] = data;
        return json;
    }

    private static JObject SerializeInputPort(PortModel port)
    {
        var json = new JObject
        {
            ["Name"] = port.Name,
            ["UsingDefaultValue"] = port.UsingDefaultValue,
            ["Level"] = port.Level,
            ["UseLevels"] = port.UseLevels,
            ["KeepListStructure"] = port.KeepListStructure,
        };

        if (port.IsHidden)
        {
            json["Hidden"] = true;
        }

        if (port.PlayerExposed)
        {
            json["Player"] = true;
        }

        // Persist an inline value pinned by the editor (choice dropdowns). Only
        // JSON-primitive values are expected here; anything else is skipped so a
        // stray boxed reference can never make a graph unserializable.
        if (port.HasUserValue)
        {
            if (port.UserValue == null)
            {
                json["UserValue"] = JValue.CreateNull();
            }
            else if (IsSerializablePrimitive(port.UserValue))
            {
                json["UserValue"] = JToken.FromObject(port.UserValue);
            }
        }

        return json;
    }

    private static bool IsSerializablePrimitive(object value)
    {
        return value is string
            || value is bool
            || value is long
            || value is int
            || value is double
            || value is float
            || value is decimal;
    }

    private static JObject SerializeConnection(ConnectionModel connection)
    {
        var json = new JObject
        {
            ["Id"] = connection.Id.ToString("N"),
            ["FromNode"] = connection.SourceNode.Id.ToString("N"),
            ["FromPort"] = connection.Source.Name,
            ["ToNode"] = connection.TargetNode.Id.ToString("N"),
            ["ToPort"] = connection.Target.Name,
        };

        if (connection.IsMuted)
        {
            json["Muted"] = true;
        }

        return json;
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

    private static JObject SerializeBookmark(BookmarkModel bookmark)
    {
        return new JObject
        {
            ["Id"] = bookmark.Id.ToString("N"),
            ["Name"] = bookmark.Name,
            ["X"] = bookmark.X,
            ["Y"] = bookmark.Y,
            ["Zoom"] = bookmark.Zoom,
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

    private NodeModel DeserializeNode(JObject json, NodeGroupLibrary library, NodeGroup? owner)
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

        // A node group's ports come from its definition, so bind before the saved per-port flags are restored by name.
        if (node is GroupInstanceNode instance)
        {
            var definition = library.Find(instance.GroupId);
            if (definition == null)
            {
                node = new MissingNodeModel(json, "Unknown node group '" + instance.GroupId.ToString("N") + "'.");
            }
            else
            {
                instance.Bind(definition);
            }
        }
        else if (node is GroupInputNode groupInput && owner != null)
        {
            groupInput.Bind(owner);
        }
        else if (node is GroupOutputNode groupOutput && owner != null)
        {
            groupOutput.Bind(owner);
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
        node.IsMuted = json.Value<bool?>("Muted") ?? false;
        RestoreUi(node.Ui, json["Ui"] as JObject);

        if (Enum.TryParse<LacingMode>(json.Value<string>("Lacing") ?? string.Empty, ignoreCase: true, out var lacing))
        {
            node.Lacing = lacing;
        }

        if (json["Player"] is JValue playerFlag && playerFlag.Type == JTokenType.Boolean)
        {
            node.PlayerExposed = (bool)playerFlag.Value!;
        }

        if (!(node is MissingNodeModel))
        {
            // Restore per-port persisted flags by port name.
            if (json["InputPorts"] is JArray inputPorts)
            {
                foreach (var portJson in inputPorts.OfType<JObject>())
                {
                    var portName = portJson.Value<string>("Name");
                    var port = node.FindInPort(portName);
                    if (port == null)
                    {
                        if (portJson["UserValue"] != null)
                        {
                            _loadWarnings.Add("'" + node.Name + "' no longer has an input '" + portName + "'; the value typed into it was dropped.");
                        }

                        continue;
                    }

                    if (port.HasDefault)
                    {
                        port.UsingDefaultValue = portJson.Value<bool?>("UsingDefaultValue") ?? port.UsingDefaultValue;
                    }

                    // An inline value pinned by the editor (choice dropdowns). Present
                    // only when set; a null token means "pinned to null", which is
                    // distinct from an absent key ("not pinned").
                    if (portJson["UserValue"] is JValue userValue)
                    {
                        port.SetUserValue(userValue.Value);
                    }

                    port.Level = portJson.Value<int?>("Level") ?? -1;
                    port.UseLevels = portJson.Value<bool?>("UseLevels") ?? false;
                    port.KeepListStructure = portJson.Value<bool?>("KeepListStructure") ?? false;
                    port.IsHidden = portJson.Value<bool?>("Hidden") ?? false;
                    port.PlayerExposed = portJson.Value<bool?>("Player") ?? false;
                }
            }
        }

        return node;
    }

    private void RestoreConnection(GraphModel graph, Dictionary<Guid, NodeModel> nodesById, JObject json, bool restoreId = true)
    {
        if (!TryParseGuid(json.Value<string>("FromNode"), out var fromNodeId) ||
            !TryParseGuid(json.Value<string>("ToNode"), out var toNodeId) ||
            !nodesById.TryGetValue(fromNodeId, out var fromNode) ||
            !nodesById.TryGetValue(toNodeId, out var toNode))
        {
            return; // tolerate dangling connectors
        }

        var fromName = json.Value<string>("FromPort");
        var toName = json.Value<string>("ToPort");
        var fromPort = fromNode.FindOutPort(fromName);
        var toPort = toNode.FindInPort(toName);
        if (fromPort == null || toPort == null)
        {
            // A node that is not installed is already shown as a placeholder; say nothing more about its wires.
            if (!(fromNode is MissingNodeModel) && !(toNode is MissingNodeModel))
            {
                _loadWarnings.Add(fromPort == null
                    ? "A connection from '" + fromNode.Name + "' was dropped: it no longer has an output '" + fromName + "'."
                    : "A connection into '" + toNode.Name + "' was dropped: it no longer has an input '" + toName + "'.");
            }

            return;
        }

        // A placeholder for an unresolved node cannot know which inputs were multi-input; a second wire into the same
        // input in the file says this one was, so keep both instead of letting the later wire replace the earlier.
        if (toNode is MissingNodeModel && !toPort.IsMultiInput && graph.FindConnectionInto(toPort) != null)
        {
            toPort.IsMultiInput = true;
        }

        var result = graph.Connect(fromPort, toPort);
        if (result.Success && restoreId && TryParseGuid(json.Value<string>("Id"), out var connectionId))
        {
            result.Connection!.Id = connectionId;
        }

        if (result.Success && (json.Value<bool?>("Muted") ?? false))
        {
            graph.SetConnectionMuted(result.Connection!, true);
        }
    }

    private static JObject SerializeUi(NodeUiState ui)
    {
        var json = new JObject();
        if (ui.Collapsed)
        {
            json["Collapsed"] = true;
        }

        if (ui.Width.HasValue)
        {
            json["Width"] = ui.Width.Value;
        }

        if (ui.HideUnused.HasValue)
        {
            json["HideUnused"] = ui.HideUnused.Value;
        }

        if (ui.OpenPanels.Count > 0)
        {
            json["OpenPanels"] = new JArray(ui.OpenPanels.OrderBy(n => n, StringComparer.Ordinal));
        }

        if (ui.ClosedPanels.Count > 0)
        {
            json["ClosedPanels"] = new JArray(ui.ClosedPanels.OrderBy(n => n, StringComparer.Ordinal));
        }

        return json;
    }

    private static void RestoreUi(NodeUiState ui, JObject? json)
    {
        if (json == null)
        {
            return;
        }

        ui.Collapsed = json.Value<bool?>("Collapsed") ?? false;
        var width = json.Value<double?>("Width");
        ui.Width = width.HasValue && width.Value > 0d && !double.IsNaN(width.Value) && !double.IsInfinity(width.Value) ? width : null;
        ui.HideUnused = json.Value<bool?>("HideUnused");
        if (json["OpenPanels"] is JArray panels)
        {
            foreach (var name in panels.Values<string>())
            {
                if (!string.IsNullOrEmpty(name))
                {
                    ui.OpenPanels.Add(name!);
                }
            }
        }

        if (json["ClosedPanels"] is JArray closed)
        {
            foreach (var name in closed.Values<string>())
            {
                if (!string.IsNullOrEmpty(name))
                {
                    ui.ClosedPanels.Add(name!);
                }
            }
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
