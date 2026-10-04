using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Groups;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Serialization;
using CamelGraph.Core.Types;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Core.Player;

/// <summary>One field of a script's form: a value the user can set before running.</summary>
public sealed class PlayerField
{
    internal PlayerField(string key, string label, string detail, NodeModel node, PortModel port)
    {
        Key = key;
        Label = label;
        Detail = detail;
        Node = node;
        Port = port;
    }

    /// <summary>Stable identity within the script (used to remember the value between sessions).</summary>
    public string Key { get; }

    /// <summary>What the field is called: the input node's name, or the exposed input's name.</summary>
    public string Label { get; }

    /// <summary>The node an exposed input belongs to, or empty for an input node.</summary>
    public string Detail { get; }

    /// <summary>The node the value goes to.</summary>
    public NodeModel Node { get; }

    /// <summary>The port the editors work on: the node's own input, or the detached port standing for an input node's value.</summary>
    public PortModel Port { get; }

    /// <summary>True when the value differs from the one saved in the script.</summary>
    public bool IsChanged => Port.HasUserValue;
}

/// <summary>One result shown after a run.</summary>
public sealed class ScriptOutput
{
    internal ScriptOutput(string label, string text, NodeState state)
    {
        Label = label;
        Text = text;
        State = state;
    }

    /// <summary>The node's name.</summary>
    public string Label { get; }

    /// <summary>What it holds, possibly over several lines.</summary>
    public string Text { get; }

    /// <summary>Whether the node ran cleanly.</summary>
    public NodeState State { get; }
}

/// <summary>What a run of a script produced.</summary>
public sealed class ScriptResult
{
    internal ScriptResult(RunResult run, IReadOnlyList<ScriptOutput> outputs, IReadOnlyList<NodeProblem> problems)
    {
        Cancelled = run.Cancelled;
        Elapsed = run.Elapsed;
        Executed = run.ExecutedNodes.Count;
        Planned = run.PlannedCount;
        Outputs = outputs;
        Problems = problems;
    }

    /// <summary>True when the run was stopped before it finished.</summary>
    public bool Cancelled { get; }

    /// <summary>How long the run took.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>How many nodes ran.</summary>
    public int Executed { get; }

    /// <summary>How many nodes were due to run.</summary>
    public int Planned { get; }

    /// <summary>The results to show.</summary>
    public IReadOnlyList<ScriptOutput> Outputs { get; }

    /// <summary>The nodes that failed or warned, errors first.</summary>
    public IReadOnlyList<NodeProblem> Problems { get; }

    /// <summary>Number of failed nodes.</summary>
    public int ErrorCount => Problems.Count(p => p.Severity == ProblemSeverity.Error);

    /// <summary>Number of nodes that warned.</summary>
    public int WarningCount => Problems.Count(p => p.Severity == ProblemSeverity.Warning);

    /// <summary>True when the run finished without a failed node.</summary>
    public bool Succeeded => !Cancelled && ErrorCount == 0;

    /// <summary>"Finished in 1.2 s — 14 nodes, 1 warning", or why it did not.</summary>
    public string Summary
    {
        get
        {
            var seconds = Elapsed.TotalSeconds.ToString(Elapsed.TotalSeconds < 10 ? "0.0" : "0", CultureInfo.InvariantCulture);
            if (Cancelled)
            {
                return "Stopped after " + Executed.ToString(CultureInfo.InvariantCulture) + " of " + Planned.ToString(CultureInfo.InvariantCulture) +
                       " nodes (" + seconds + " s). Changes already made in Navisworks are kept.";
            }

            var text = (ErrorCount > 0 ? "Finished with errors" : "Finished") + " in " + seconds + " s — " +
                       Executed.ToString(CultureInfo.InvariantCulture) + (Executed == 1 ? " node" : " nodes");
            if (ErrorCount > 0)
            {
                text += ", " + ErrorCount.ToString(CultureInfo.InvariantCulture) + (ErrorCount == 1 ? " error" : " errors");
            }

            if (WarningCount > 0)
            {
                text += ", " + WarningCount.ToString(CultureInfo.InvariantCulture) + (WarningCount == 1 ? " warning" : " warnings");
            }

            return text;
        }
    }

    /// <summary>The summary, the results and the problems as plain text, for the clipboard.</summary>
    /// <param name="scriptName">Shown in the heading.</param>
    public string ToReport(string scriptName)
    {
        var lines = new List<string> { scriptName, Summary, string.Empty };
        foreach (var output in Outputs)
        {
            lines.Add(output.Label + ":");
            lines.AddRange(output.Text.Replace("\r\n", "\n").Split('\n').Select(l => "  " + l));
        }

        foreach (var problem in Problems)
        {
            lines.Add((problem.Severity == ProblemSeverity.Error ? "Error" : "Warning") + " in " + problem.Node.Name + ": " + problem.Text);
        }

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// A script loaded for the Player: the graph, the form made from it, and what running it gives. The form is the script's input
/// nodes (numbers, sliders, toggles, text, paths, colours) plus any unwired input the script's author marked "Show in Player";
/// the results are the Watch nodes plus any node marked the same way.
/// </summary>
public sealed class ScriptSession
{
    /// <summary>Most lines of one result kept (a list of thousands of items would only bury the page).</summary>
    public const int MaxOutputLines = 300;

    private const int MaxLineLength = 400;

    private readonly List<PlayerField> _fields = new List<PlayerField>();
    private readonly List<NodeModel> _outputNodes = new List<NodeModel>();

    private ScriptSession(string path, GraphModel graph, string hash)
    {
        Path = path;
        Graph = graph;
        Hash = hash;
        Name = graph.Name.Length > 0 ? graph.Name : System.IO.Path.GetFileNameWithoutExtension(path);
        Description = graph.Description;
        Build();
    }

    /// <summary>Full path of the .dyc file.</summary>
    public string Path { get; }

    /// <summary>The script's name: the graph's name, else the file's.</summary>
    public string Name { get; }

    /// <summary>The description saved in the graph.</summary>
    public string Description { get; }

    /// <summary>SHA-256 of the file as it was read, in hex.</summary>
    public string Hash { get; }

    /// <summary>The graph that runs.</summary>
    public GraphModel Graph { get; }

    /// <summary>The form, top to bottom as the nodes sit on the canvas.</summary>
    public IReadOnlyList<PlayerField> Fields => _fields;

    /// <summary>The nodes whose result is shown.</summary>
    public IReadOnlyList<NodeModel> OutputNodes => _outputNodes;

    /// <summary>
    /// Names of the nodes that change the model, write files, run programs or use the network (distinct, muted and frozen ones left out):
    /// the nodes the Player asks about before it first runs a script.
    /// </summary>
    public IReadOnlyList<string> ModifyingNodes { get; private set; } = new List<string>();

    /// <summary>Number of nodes whose type is not installed (the script cannot run fully).</summary>
    public int MissingNodes { get; private set; }

    /// <summary>Number of nodes in the script (inside node groups too).</summary>
    public int NodeCount { get; private set; }

    /// <summary>Reads a script and builds its form.</summary>
    /// <param name="path">The .dyc file.</param>
    /// <param name="registry">The node registry that resolves the nodes.</param>
    /// <exception cref="GraphFormatException">The file is not a readable .dyc document.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static ScriptSession Load(string path, NodeRegistry registry)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (registry == null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        var bytes = File.ReadAllBytes(path);
        var hash = CamelGraph.Core.Editing.FileFingerprint.Of(bytes);

        var graph = new GraphSerializer(registry).Deserialize(System.Text.Encoding.UTF8.GetString(bytes));
        return new ScriptSession(path, graph, hash);
    }

    /// <summary>True when a node is an input of the form: an input node not hidden, or one marked to show.</summary>
    public static bool IsFormNode(NodeModel node) =>
        node is IPlayerInputNode && node.PlayerExposed != false;

    /// <summary>True when a node's result is shown: a Watch node not hidden, or any node marked to show.</summary>
    public static bool IsResultNode(NodeModel node) =>
        node.PlayerExposed == true || (node is IPlayerOutputNode && node.PlayerExposed != false);

    private void Build()
    {
        var ordered = Graph.Nodes.OrderBy(n => n.Y).ThenBy(n => n.X).ToList();
        foreach (var node in ordered)
        {
            if (IsFormNode(node))
            {
                var port = ((IPlayerInputNode)node).CreatePlayerPort();
                _fields.Add(new PlayerField(node.Id.ToString("N"), node.Name, string.Empty, node, port));
            }

            foreach (var port in node.InPorts.Where(p => p.PlayerExposed))
            {
                // A wired input is fed by the graph; only an unwired one can be filled in.
                if (Graph.FindConnectionInto(port) == null && PortEditors.Resolve(port) != PortEditorKind.None)
                {
                    _fields.Add(new PlayerField(node.Id.ToString("N") + ":" + port.Name, port.Name, node.Name, node, port));
                }
            }

            if (IsResultNode(node))
            {
                _outputNodes.Add(node);
            }
        }

        var modifying = new List<string>();
        var missing = 0;
        var count = 0;
        Survey(Graph, new HashSet<NodeGroup>(), modifying, ref missing, ref count);
        ModifyingNodes = modifying.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        MissingNodes = missing;
        NodeCount = count;
    }

    // Walks the graph and, through every node group instance, the groups' bodies.
    private static void Survey(GraphModel graph, HashSet<NodeGroup> visited, List<string> modifying, ref int missing, ref int count)
    {
        foreach (var node in graph.Nodes)
        {
            count++;
            if (node is MissingNodeModel)
            {
                missing++;
            }
            else if (!node.IsMuted && !node.IsFrozen && !(node is GroupInstanceNode) &&
                     (node.Function == NodeFunction.Modify || node.Effects != NodeEffects.None))
            {
                modifying.Add(node.Name);
            }

            if (node is GroupInstanceNode instance && instance.Definition != null && visited.Add(instance.Definition))
            {
                Survey(instance.Definition.Graph, visited, modifying, ref missing, ref count);
            }
        }
    }

    // ----- values ------------------------------------------------------------------------------

    /// <summary>The values the user changed (by field key), ready to be saved and restored next time.</summary>
    public JObject CaptureValues()
    {
        var values = new JObject();
        foreach (var field in _fields)
        {
            var port = field.Port;
            if (!port.HasUserValue || port.UserValue == null)
            {
                continue;
            }

            var value = port.UserValue;
            if (value is string || value is bool || value is double || value is float || value is int || value is long || value is decimal)
            {
                values[field.Key] = JToken.FromObject(value);
            }
        }

        return values;
    }

    /// <summary>Puts saved values back into the form; a value that no longer fits its field is ignored.</summary>
    /// <param name="values">What <see cref="CaptureValues"/> returned.</param>
    public void RestoreValues(JObject? values)
    {
        if (values == null)
        {
            return;
        }

        foreach (var field in _fields)
        {
            if (!(values[field.Key] is JValue token) || token.Value == null)
            {
                continue;
            }

            try
            {
                switch (PortEditors.Resolve(field.Port))
                {
                    case PortEditorKind.Number:
                        PortEditors.SetNumber(field.Port, Convert.ToDouble(token.Value, CultureInfo.InvariantCulture));
                        break;
                    case PortEditorKind.Toggle:
                        PortEditors.SetBool(field.Port, Convert.ToBoolean(token.Value, CultureInfo.InvariantCulture));
                        break;
                    case PortEditorKind.Text:
                    case PortEditorKind.Path:
                        PortEditors.SetText(field.Port, token.Value.ToString());
                        break;
                    case PortEditorKind.Colour:
                    case PortEditorKind.Choice:
                        if (token.Value is string text)
                        {
                            field.Port.SetUserValue(text);
                        }

                        break;
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                // A value saved for a different kind of field: leave the field as the script has it.
            }
        }
    }

    /// <summary>Brings every field back to the value saved in the script.</summary>
    public void ResetValues()
    {
        foreach (var field in _fields)
        {
            field.Port.ClearUserValue();
        }
    }

    // ----- running -----------------------------------------------------------------------------

    /// <summary>
    /// Runs the whole script: every node runs again, because a script talks to a live model that may have changed since it
    /// last ran. The caller sets up cancellation and progress on the context.
    /// </summary>
    /// <param name="context">The evaluation context (with the host's services) for this run.</param>
    public ScriptResult Run(EvaluationContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        Graph.ResetForRun();
        var run = new GraphEngine().Run(Graph, context);
        var outputs = _outputNodes.Select(Describe).ToList();
        return new ScriptResult(run, outputs, Problems.Collect(Graph));
    }

    private static ScriptOutput Describe(NodeModel node)
    {
        string text;
        if (node is IPlayerOutputNode watch)
        {
            text = watch.PlayerText;
        }
        else
        {
            text = string.Join(
                "\n",
                node.OutPorts.Select(p => (node.OutPorts.Count > 1 ? p.Name + ": " : string.Empty) + TypeCoercion.FormatValue(p.Value)));
        }

        return new ScriptOutput(node.Name, Limit(text), node.State);
    }

    private static string Limit(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var kept = lines
            .Take(MaxOutputLines)
            .Select(l => l.Length <= MaxLineLength ? l : l.Substring(0, MaxLineLength - 1) + "…")
            .ToList();
        if (lines.Length > MaxOutputLines)
        {
            kept.Add("… " + (lines.Length - MaxOutputLines).ToString(CultureInfo.InvariantCulture) + " more lines");
        }

        return string.Join("\n", kept);
    }
}
