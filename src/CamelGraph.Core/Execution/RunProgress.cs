using System;
using System.Collections.Generic;

namespace CamelGraph.Core.Execution;

/// <summary>Where a run is: the node about to execute and how many of the current graph's nodes are done.</summary>
public sealed class RunProgress
{
    /// <summary>Creates a progress report.</summary>
    public RunProgress(int completed, int total, string node, IReadOnlyList<string> scope)
    {
        Completed = completed;
        Total = total;
        Node = node;
        Scope = scope;
    }

    /// <summary>Nodes of the current graph already done.</summary>
    public int Completed { get; }

    /// <summary>Nodes the current graph will run in this pass.</summary>
    public int Total { get; }

    /// <summary>Name of the node about to run.</summary>
    public string Node { get; }

    /// <summary>Node groups being run, outermost first; empty at the top level.</summary>
    public IReadOnlyList<string> Scope { get; }

    /// <summary>One line for a status bar: "12 / 40 — Viewpoint.SaveWithOverrides", prefixed with the group path inside a group.</summary>
    public string Describe()
    {
        var where = Scope.Count == 0 ? string.Empty : string.Join(" \u25B8 ", Scope) + " \u25B8 ";
        return (Completed + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " / " +
               Total.ToString(System.Globalization.CultureInfo.InvariantCulture) + " \u2014 " + where + Node;
    }
}
