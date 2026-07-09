using System;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Newtonsoft.Json.Linq;

namespace Dyncamelo.Core.Serialization;

/// <summary>
/// Placeholder for a node whose type or zero-touch definition could not be
/// resolved when a .dyc file was loaded. It preserves the complete original
/// JSON (round-tripping it untouched on save, so no data is lost), rebuilds its
/// ports from the persisted port lists so connections still attach, and reports
/// <see cref="NodeState.Error"/> instead of executing.
/// </summary>
public class MissingNodeModel : NodeModel
{
    /// <summary>Serialized type tag reported by this instance (the original tag is preserved in <see cref="RawJson"/>).</summary>
    public const string TypeName = "Missing";

    /// <summary>Creates a placeholder from the node's original JSON.</summary>
    /// <param name="rawJson">The complete persisted node object.</param>
    /// <param name="reason">Why the node could not be resolved.</param>
    public MissingNodeModel(JObject rawJson, string reason)
    {
        RawJson = (JObject)(rawJson ?? throw new ArgumentNullException(nameof(rawJson))).DeepClone();
        Reason = reason ?? string.Empty;
        Name = rawJson.Value<string>("Name") ?? "Missing node";
        Category = string.Empty;
        Description = Reason;

        // Rebuild ports from the persisted lists so existing wires still connect.
        // Inputs get a null default so the node reaches Evaluate (and reports its
        // error) instead of sitting idle on missing inputs.
        RebuildPorts(rawJson["InputPorts"] as JArray, isInput: true);
        RebuildPorts(rawJson["OutputPorts"] as JArray, isInput: false);

        State = NodeState.Error;
        AddMessage(MessageSeverity.Error, Reason);
    }

    /// <summary>The complete original node JSON, re-emitted verbatim (plus updated position) on save.</summary>
    public JObject RawJson { get; }

    /// <summary>Why the node could not be resolved.</summary>
    public string Reason { get; }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        throw new InvalidOperationException(Reason.Length > 0 ? Reason : "Node definition could not be resolved.");
    }

    private void RebuildPorts(JArray? ports, bool isInput)
    {
        if (ports == null)
        {
            return;
        }

        foreach (var token in ports)
        {
            var name = token.Value<string>("Name") ?? (isInput ? "in" : "out");
            if (isInput)
            {
                AddInput(name, typeof(object), defaultValue: null);
            }
            else
            {
                AddOutput(name, typeof(object));
            }
        }
    }
}
