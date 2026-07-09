using System;
using System.Collections;
using System.Collections.Generic;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Core.Execution;

/// <summary>
/// Implements Dynamo-style replication ("lacing"): when an input port's declared
/// type is scalar but the incoming value is a list, the node is mapped over the
/// list. Replication happens only over <em>excess</em> rank (actual rank minus
/// declared rank), recursively for nested lists. Multiple replicated inputs are
/// paired per the node's <see cref="LacingMode"/>; non-replicated inputs are
/// broadcast unchanged to every invocation.
/// </summary>
internal static class Replicator
{
    /// <summary>
    /// Executes a node over its (raw, possibly list-shaped) arguments, applying
    /// replication and per-invocation coercion. Exceptions thrown by the node's
    /// <see cref="NodeModel.Evaluate"/> propagate to the engine's per-node catch.
    /// </summary>
    /// <param name="node">The node to execute.</param>
    /// <param name="args">One raw value per input port.</param>
    /// <param name="context">Ambient services and cancellation.</param>
    /// <returns>One value per output port (at least one entry).</returns>
    public static object?[] Execute(NodeModel node, object?[] args, EvaluationContext context)
    {
        int outCount = Math.Max(node.OutPorts.Count, 1);
        var declaredRanks = new int[node.InPorts.Count];
        var declaredTypes = new Type[node.InPorts.Count];
        for (int i = 0; i < node.InPorts.Count; i++)
        {
            declaredTypes[i] = node.InPorts[i].DeclaredType;
            declaredRanks[i] = GetEffectiveRank(node.InPorts[i]);
        }

        var lacing = node.Lacing == LacingMode.Auto ? LacingMode.Shortest : node.Lacing;
        return Invoke(node, args, declaredTypes, declaredRanks, lacing, context, outCount);
    }

    /// <summary>
    /// Declared rank of an input port. All rank decisions route through here so
    /// List@Level can later override rank inference as a purely local change.
    /// </summary>
    /// <param name="port">The input port.</param>
    internal static int GetEffectiveRank(PortModel port)
    {
        // Future: if (port.UseLevels) { ... interpret port.Level ... }
        return GetDeclaredRank(port.DeclaredType);
    }

    /// <summary>
    /// Rank implied by a declared CLR type: scalar = 0, IList&lt;T&gt; = 1,
    /// IList&lt;IList&lt;T&gt;&gt; = 2, ... <see cref="int.MaxValue"/> means "accepts
    /// anything" (object, untyped or object-element lists) and never replicates.
    /// </summary>
    /// <param name="type">The declared type.</param>
    internal static int GetDeclaredRank(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(object))
        {
            return int.MaxValue;
        }

        if (!TypeCoercion.IsListType(type))
        {
            return 0;
        }

        var elementType = TypeCoercion.GetListElementType(type) ?? typeof(object);
        var elementRank = GetDeclaredRank(elementType);
        return elementRank == int.MaxValue ? int.MaxValue : elementRank + 1;
    }

    /// <summary>
    /// Rank of a runtime value: 0 for scalars (including strings and dictionaries),
    /// 1 + deepest element rank for lists. An empty list has rank 1.
    /// </summary>
    /// <param name="value">The value to classify.</param>
    internal static int GetValueRank(object? value)
    {
        if (value is IList list && !(value is string))
        {
            int max = 0;
            foreach (var element in list)
            {
                var rank = GetValueRank(element);
                if (rank > max)
                {
                    max = rank;
                }
            }

            return max + 1;
        }

        return 0;
    }

    private static object?[] Invoke(
        NodeModel node,
        object?[] args,
        Type[] declaredTypes,
        int[] declaredRanks,
        LacingMode lacing,
        EvaluationContext context,
        int outCount)
    {
        // Which arguments still carry excess rank at this nesting level?
        var replicated = new List<int>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is IList && !(args[i] is string) &&
                declaredRanks[i] != int.MaxValue &&
                GetValueRank(args[i]) > declaredRanks[i])
            {
                replicated.Add(i);
            }
        }

        if (replicated.Count == 0)
        {
            return InvokeScalar(node, args, declaredTypes, context, outCount);
        }

        var accumulators = NewAccumulators(outCount);

        if (lacing == LacingMode.CrossProduct)
        {
            // Leftmost replicated input is the outermost loop; the remaining
            // replicated inputs are handled by the recursive call.
            var outer = (IList)args[replicated[0]]!;
            foreach (var element in outer)
            {
                var child = (object?[])args.Clone();
                child[replicated[0]] = element;
                Collect(accumulators, Invoke(node, child, declaredTypes, declaredRanks, lacing, context, outCount));
            }
        }
        else
        {
            int length = lacing == LacingMode.Longest ? 0 : int.MaxValue;
            bool hasEmpty = false;
            foreach (var index in replicated)
            {
                int count = ((IList)args[index]!).Count;
                hasEmpty |= count == 0;
                length = lacing == LacingMode.Longest ? Math.Max(length, count) : Math.Min(length, count);
            }

            if (lacing == LacingMode.Longest && hasEmpty)
            {
                node.AddMessage(MessageSeverity.Warning, "An empty list cannot be extended under Longest lacing; the result is empty.");
                length = 0;
            }

            for (int k = 0; k < length; k++)
            {
                var child = (object?[])args.Clone();
                foreach (var index in replicated)
                {
                    var list = (IList)args[index]!;
                    int elementIndex = lacing == LacingMode.Longest ? Math.Min(k, list.Count - 1) : k;
                    child[index] = list[elementIndex];
                }

                Collect(accumulators, Invoke(node, child, declaredTypes, declaredRanks, lacing, context, outCount));
            }
        }

        var results = new object?[outCount];
        for (int j = 0; j < outCount; j++)
        {
            results[j] = accumulators[j];
        }

        return results;
    }

    private static object?[] InvokeScalar(
        NodeModel node,
        object?[] args,
        Type[] declaredTypes,
        EvaluationContext context,
        int outCount)
    {
        var call = new object?[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            var declared = declaredTypes[i];
            var value = args[i];
            if (value == null)
            {
                if (declared.IsValueType && Nullable.GetUnderlyingType(declared) == null)
                {
                    node.AddMessage(MessageSeverity.Warning, "Null value passed to input '" + node.InPorts[i].Name + "'.");
                    return new object?[outCount];
                }

                call[i] = null;
                continue;
            }

            if (!TypeCoercion.TryCoerce(value, declared, out var coerced))
            {
                node.AddMessage(
                    MessageSeverity.Warning,
                    "Cannot convert value of type '" + value.GetType().Name + "' to '" + declared.Name +
                    "' for input '" + node.InPorts[i].Name + "'.");
                return new object?[outCount];
            }

            call[i] = coerced;
        }

        var outputs = node.Evaluate(call, context) ?? new object?[outCount];
        var normalized = new object?[outCount];
        for (int j = 0; j < outCount; j++)
        {
            normalized[j] = j < outputs.Length ? TypeCoercion.MaterializeLists(outputs[j]) : null;
        }

        return normalized;
    }

    private static List<object?>[] NewAccumulators(int outCount)
    {
        var accumulators = new List<object?>[outCount];
        for (int j = 0; j < outCount; j++)
        {
            accumulators[j] = new List<object?>();
        }

        return accumulators;
    }

    private static void Collect(List<object?>[] accumulators, object?[] childOutputs)
    {
        for (int j = 0; j < accumulators.Length; j++)
        {
            accumulators[j].Add(j < childOutputs.Length ? childOutputs[j] : null);
        }
    }
}
