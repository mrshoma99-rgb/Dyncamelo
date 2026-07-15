using System;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Newtonsoft.Json.Linq;

namespace Dyncamelo.Nodes;

/// <summary>
/// Builds a list from a variable number of item inputs ("item0", "item1", ...),
/// like Dynamo's List.Create. The UI grows the node with <see cref="AddItemPort"/>
/// and shrinks it with <see cref="RemoveItemPort"/> (always the last port, so
/// port names stay contiguous). Item inputs are untyped, so wired lists become
/// nested elements rather than being replicated over.
/// </summary>
public class ListCreateNode : NodeModel
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "ListCreate";

    /// <summary>Creates the node with a single item input and a list output.</summary>
    public ListCreateNode()
    {
        Name = "List.Create";
        Category = "List";
        Description = "Builds a list from the wired item inputs.";
        AddOutput("list", typeof(IList<object>), "The created list.");
        AddItemPort();
    }

    /// <summary>Number of item input ports.</summary>
    public int ItemCount => InPorts.Count;

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override NodeFunction Function => NodeFunction.Create;

    /// <summary>Appends one item input port ("itemN") and dirties the node.</summary>
    /// <returns>The new port.</returns>
    public PortModel AddItemPort()
    {
        var port = AddInput(
            "item" + InPorts.Count.ToString(CultureInfo.InvariantCulture),
            typeof(object),
            "An element of the list.");
        MarkDirty();
        return port;
    }

    /// <summary>
    /// Removes the last item input port (disconnecting any wire into it) and
    /// dirties the node. At least one item port always remains.
    /// </summary>
    /// <returns>True when a port was removed; false when only one port is left.</returns>
    public bool RemoveItemPort()
    {
        if (InPorts.Count <= 1)
        {
            return false;
        }

        var last = InPorts[InPorts.Count - 1];
        if (Graph != null)
        {
            var connection = Graph.FindConnectionInto(last);
            if (connection != null)
            {
                Graph.Disconnect(connection);
            }
        }

        // NodeModel exposes no port-removal API yet; its InPorts property is
        // backed by a mutable list, which we rely on here. If Core ever wraps
        // the list, this fails loudly instead of corrupting the node.
        if (!(InPorts is IList<PortModel> ports) || ports.IsReadOnly)
        {
            throw new NotSupportedException(
                "ListCreateNode cannot remove ports: NodeModel.InPorts is not a mutable list in this Core version.");
        }

        ports.RemoveAt(ports.Count - 1);
        OnPropertyChanged(nameof(ItemCount));
        MarkDirty();
        return true;
    }

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        return new object?[] { new List<object?>(inputs) };
    }

    /// <inheritdoc />
    public override void SerializeData(JObject data)
    {
        data["ItemCount"] = InPorts.Count;
    }

    /// <inheritdoc />
    public override void DeserializeData(JObject data)
    {
        var count = Math.Max(1, data.Value<int?>("ItemCount") ?? 1);
        while (InPorts.Count < count)
        {
            AddItemPort();
        }

        while (InPorts.Count > count)
        {
            RemoveItemPort();
        }
    }
}
