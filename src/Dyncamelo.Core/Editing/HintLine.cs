using System.Collections.Generic;

namespace Dyncamelo.Core.Editing;

/// <summary>What the editor is doing right now, as far as the hint line cares.</summary>
public sealed class HintContext
{
    /// <summary>A graph run is in progress.</summary>
    public bool IsRunning { get; set; }

    /// <summary>A wire is being dragged from a socket.</summary>
    public bool DraggingWire { get; set; }

    /// <summary>Number of selected nodes.</summary>
    public int SelectedNodes { get; set; }

    /// <summary>One of the selected nodes is an instance of a node group.</summary>
    public bool SelectedGroupInstance { get; set; }

    /// <summary>The canvas shows the body of a node group.</summary>
    public bool InsideGroup { get; set; }

    /// <summary>The graph has at least one node.</summary>
    public bool HasNodes { get; set; }

    /// <summary>The last run left nodes in error.</summary>
    public bool HasErrors { get; set; }
}

/// <summary>
/// The one-line hint at the bottom of the window and the hint shown on an empty canvas. Shortcuts are read from the keymap in
/// force, so a rebound key is named correctly, and a command with no shortcut is left out rather than shown as "(none)".
/// </summary>
public static class HintLine
{
    /// <summary>The status-bar hint for the current situation.</summary>
    /// <param name="context">What is happening.</param>
    /// <param name="keymap">The shortcuts in force.</param>
    public static string ForStatusBar(HintContext context, Keymap keymap)
    {
        var parts = new List<string?>();
        if (context.IsRunning)
        {
            parts.Add("Running — press Esc to stop after the node in progress");
        }
        else if (context.DraggingWire)
        {
            parts.Add("Release on a socket to connect");
            parts.Add("release on empty canvas to search for a node to connect to");
        }
        else if (context.SelectedNodes > 0)
        {
            parts.Add(With("to collapse", "node.collapse", keymap));
            parts.Add(With("to mute", "node.mute", keymap));
            parts.Add(With("to freeze", "node.freeze", keymap));
            parts.Add(With("to frame", "graph.group", keymap));
            parts.Add(With("to see why it did not run", "node.explain", keymap));
            if (context.SelectedGroupInstance)
            {
                parts.Insert(0, With("to open the node group", "group.edit", keymap));
            }
        }
        else if (context.InsideGroup)
        {
            parts.Add(With("to go back", "group.exit", keymap));
            parts.Add(With("to add a node", "graph.addnode", keymap));
            parts.Add("the + on Group Input / Output adds sockets");
        }
        else if (!context.HasNodes)
        {
            parts.Add(With("to add your first node", "graph.addnode", keymap) ?? "Double-click the canvas to add a node");
            parts.Add("or drag one from the library");
        }
        else
        {
            parts.Add(With("to add a node", "graph.addnode", keymap));
            parts.Add("drag from a socket to connect");
            if (context.HasErrors)
            {
                parts.Add(With("for the next problem", "graph.nextproblem", keymap));
            }

            parts.Add(With("for all shortcuts", "help.keys", keymap));
        }

        return Join(parts);
    }

    /// <summary>The hint shown in the middle of an empty canvas: the ways to put the first node there.</summary>
    /// <param name="keymap">The shortcuts in force.</param>
    /// <param name="doubleClickAction">The Settings choice for double-clicking the canvas ("string", "number", "note" or "none").</param>
    public static IReadOnlyList<string> ForEmptyCanvas(Keymap keymap, string doubleClickAction)
    {
        var lines = new List<string>();
        var search = With("to search for a node", "graph.addnode", keymap);
        if (search != null)
        {
            lines.Add(search);
        }

        switch (doubleClickAction)
        {
            case "string": lines.Add("Double-click: add a String node"); break;
            case "number": lines.Add("Double-click: add a Number node"); break;
            case "note": lines.Add("Double-click: add a note"); break;
        }

        lines.Add("Drag a node in from the library");
        lines.Add("Drop a .dyc file here to open it");
        var palette = With("for every command", "help.palette", keymap);
        if (palette != null)
        {
            lines.Add(palette);
        }

        return lines;
    }

    // "M to mute": the key first, as it is on the keyboard; null when the command has no shortcut.
    private static string? With(string what, string commandId, Keymap keymap)
    {
        var key = keymap.ShortcutOf(commandId);
        return string.IsNullOrEmpty(key) ? null : key + " " + what;
    }

    private static string Join(IEnumerable<string?> parts)
    {
        var kept = new List<string>();
        foreach (var part in parts)
        {
            if (!string.IsNullOrEmpty(part))
            {
                kept.Add(part!);
            }
        }

        return string.Join(" · ", kept);
    }
}
