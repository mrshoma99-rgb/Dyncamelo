using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;

namespace CamelGraph.Core.Loader;

/// <summary>
/// A graph node backed by a zero-touch <see cref="NodeDefinition"/> (a public
/// static method). Ports are built from the definition's descriptors; execution
/// invokes the method. Replication and coercion are handled by the engine.
/// </summary>
public class ZeroTouchNodeModel : NodeModel
{
    /// <summary>Serialized type tag for zero-touch nodes.</summary>
    public const string TypeName = "ZeroTouch";

    /// <summary>Creates a node bound to a definition.</summary>
    /// <param name="definition">The zero-touch definition to bind.</param>
    public ZeroTouchNodeModel(NodeDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Name = definition.Name;
        Category = definition.Category;
        Description = definition.Description;

        foreach (var input in definition.Inputs)
        {
            var port = input.HasDefault
                ? AddInput(input.Name, input.Type, input.DefaultValue, input.Description)
                : AddInput(input.Name, input.Type, input.Description);

            if (input.Choices != null && input.Choices.Count > 0)
            {
                port.Choices = input.Choices;
            }

            port.DataChoice = input.DataChoice;
            port.Range = input.Range;
            port.PathMode = input.PathMode;
            port.PathFilter = input.PathFilter;
            port.Panel = input.Panel;
            port.PanelDefaultOpen = input.PanelDefaultOpen;
            port.KindHint = input.Kind;
            port.IsMultiInput = input.MultiInput;
            port.AcceptsNull = input.AcceptsNull;
            port.IsScalarInput = input.ScalarInput;
            port.Aliases = input.Aliases;
        }

        foreach (var output in definition.Outputs)
        {
            var outPort = AddOutput(output.Name, output.Type, output.Description);
            outPort.KindHint = output.Kind;
            outPort.Aliases = output.Aliases;
        }
    }

    /// <summary>The bound definition.</summary>
    public NodeDefinition Definition { get; }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override NodeFunction Function => Definition.Function;

    /// <inheritdoc />
    public override NodeEffects Effects => Definition.Effects;

    /// <inheritdoc />
    public override IReadOnlyList<string> SearchTags => Definition.SearchTags;

    /// <inheritdoc />
    public override bool CatchesUpstreamErrors => Definition.CatchesUpstreamErrors;

    /// <inheritdoc />
    public override bool IsLiveState => Definition.IsLiveState;

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        // Inside a run the replicator already collects per call. Evaluated on its own (a test, another tool), the node still
        // collects what it reports with NodeWarnings.Add and shows it as warnings of this node.
        var ownCollector = NodeWarnings.IsCollecting ? null : NodeWarnings.Begin();
        try
        {
            return EvaluateCore(inputs, ownCollector);
        }
        finally
        {
            ownCollector?.Dispose();
        }
    }

    private object?[] EvaluateCore(object?[] inputs, NodeWarnings.WarningCollector? ownCollector)
    {
        object? returned;
        try
        {
            returned = Definition.Method.Invoke(null, inputs);
        }
        catch (TargetInvocationException invocationException) when (invocationException.InnerException != null)
        {
            // Surface the node author's exception, not the reflection wrapper.
            ExceptionDispatchInfo.Capture(invocationException.InnerException).Throw();
            throw; // unreachable
        }

        if (ownCollector != null && ownCollector.Total > 0)
        {
            NodeWarnings.Report(this, ownCollector);
        }

        if (Definition.IsVoid)
        {
            // Pass-through of the first input enables sequencing side-effecting nodes.
            return new object?[] { inputs.Length > 0 ? inputs[0] : null };
        }

        if (Definition.MultiReturnKeys != null)
        {
            var outputs = new object?[Definition.MultiReturnKeys.Count];
            var dictionary = returned as IDictionary<string, object>;
            for (int i = 0; i < Definition.MultiReturnKeys.Count; i++)
            {
                var key = Definition.MultiReturnKeys[i];
                if (dictionary != null && dictionary.TryGetValue(key, out var value))
                {
                    outputs[i] = value;
                }
                else
                {
                    outputs[i] = null;
                    AddMessage(MessageSeverity.Warning, "MultiReturn key '" + key + "' was not present in the result.");
                }
            }

            return outputs;
        }

        return new object?[] { returned };
    }
}
