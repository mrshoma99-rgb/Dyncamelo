using System.Collections.Generic;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Execution;

/// <summary>
/// Pairs a node's outputs with its inputs for bypass: used by node muting and
/// by delete-and-reconnect. Pairing is positional-greedy: walking outputs in
/// order, each takes the first not-yet-used input whose advisory compatibility
/// is at worst <see cref="Compat.Convertible"/> (exact and convertible matches
/// win over loose ones; untyped ports only pair when nothing typed matches).
/// </summary>
public static class MutePassThrough
{
    /// <summary>Returns, per output index, the paired input index or -1.</summary>
    public static int[] Pair(NodeModel node)
    {
        var pairs = new int[node.OutPorts.Count];
        var used = new HashSet<int>();
        for (var j = 0; j < pairs.Length; j++)
        {
            pairs[j] = -1;
            var best = -1;
            var bestRank = int.MaxValue;
            for (var i = 0; i < node.InPorts.Count; i++)
            {
                if (used.Contains(i))
                {
                    continue;
                }

                var rank = Rank(node.InPorts[i], node.OutPorts[j]);
                if (rank < bestRank)
                {
                    bestRank = rank;
                    best = i;
                }
            }

            if (best >= 0 && bestRank <= 2)
            {
                pairs[j] = best;
                used.Add(best);
            }
        }

        return pairs;
    }

    /// <summary>Output values for a muted node: each paired output carries its input's value, the rest are null.</summary>
    public static object?[] Resolve(NodeModel node, object?[] inputs)
    {
        var pairs = Pair(node);
        var outputs = new object?[pairs.Length];
        for (var j = 0; j < pairs.Length; j++)
        {
            outputs[j] = pairs[j] >= 0 && pairs[j] < inputs.Length ? inputs[pairs[j]] : null;
        }

        return outputs;
    }

    // 0 exact, 1 convertible, 2 loose-but-typed-on-one-side, 3 anything else (never paired).
    private static int Rank(PortModel input, PortModel output)
    {
        var from = PortKinds.FromPort(input);
        var to = PortKinds.FromPort(output);
        switch (PortKinds.Compare(from, to))
        {
            case Compat.Exact:
                return from.Family == PortFamily.Any ? 2 : 0;
            case Compat.Convertible:
                return 1;
            default:
                return from.Family == PortFamily.Any || to.Family == PortFamily.Any ? 2 : 3;
        }
    }
}
