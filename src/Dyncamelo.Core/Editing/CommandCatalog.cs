using System;
using System.Collections.Generic;
using System.Linq;

namespace Dyncamelo.Core.Editing;

/// <summary>Where a command's shortcut is honoured.</summary>
public enum CommandScope
{
    /// <summary>Anywhere in the editor, including while a text box has focus (must not use text-editing chords).</summary>
    Global,
    /// <summary>Only while the canvas (not a text box) has focus.</summary>
    Canvas,
}

/// <summary>Static description of one editor command: the single source of truth for menus, shortcuts, help and the palette.</summary>
public sealed class CommandInfo
{
    /// <summary>Creates a description.</summary>
    public CommandInfo(string id, string title, string category, string? shortcut = null,
        CommandScope scope = CommandScope.Canvas, bool toolbar = false, bool toggle = false, string keywords = "",
        string? alternate = null)
    {
        Id = id;
        Title = title;
        Category = category;
        Shortcut = shortcut;
        Scope = scope;
        InToolbar = toolbar;
        IsToggle = toggle;
        Keywords = keywords;
        Alternate = alternate;
    }

    /// <summary>Stable id ("edit.undo").</summary>
    public string Id { get; }

    /// <summary>Menu/palette title.</summary>
    public string Title { get; }

    /// <summary>Menu group: File, Edit, View, Graph, Node, Wires or Help.</summary>
    public string Category { get; }

    /// <summary>Default shortcut such as "Ctrl+Shift+L", or null.</summary>
    public string? Shortcut { get; }

    /// <summary>Where the shortcut applies.</summary>
    public CommandScope Scope { get; }

    /// <summary>True when the command earns a toolbar button.</summary>
    public bool InToolbar { get; }

    /// <summary>True for checkable commands (bound to a bool).</summary>
    public bool IsToggle { get; }

    /// <summary>Extra palette search terms.</summary>
    public string Keywords { get; }

    /// <summary>A second shortcut that does the same thing (for example Ctrl+Shift+Z for redo), or null.</summary>
    public string? Alternate { get; }
}

/// <summary>
/// The catalogue of every editor command. Menus, key bindings, tooltips, the
/// help overlay and the command palette are all generated from this list, and
/// tests assert its integrity (unique ids and shortcuts, known categories) so a
/// function can never exist in one surface and be missing from another.
/// New features register their commands here in the same change that adds them.
/// </summary>
public static class CommandCatalog
{
    /// <summary>The menu groups, in display order.</summary>
    public static readonly IReadOnlyList<string> Categories = new[] { "File", "Edit", "View", "Graph", "Node", "Wires", "Help" };

    /// <summary>All commands, in menu order.</summary>
    public static readonly IReadOnlyList<CommandInfo> All = new List<CommandInfo>
    {
        new CommandInfo("file.new", "New", "File", "Ctrl+N", CommandScope.Global),
        new CommandInfo("file.open", "Open…", "File", "Ctrl+O", CommandScope.Global),
        new CommandInfo("file.save", "Save", "File", "Ctrl+S", CommandScope.Global),
        new CommandInfo("file.saveas", "Save As…", "File", "Ctrl+Shift+S", CommandScope.Global),

        new CommandInfo("edit.undo", "Undo", "Edit", "Ctrl+Z", toolbar: true, keywords: "revert back"),
        new CommandInfo("edit.redo", "Redo", "Edit", "Ctrl+Y", toolbar: true, keywords: "repeat forward", alternate: "Ctrl+Shift+Z"),
        new CommandInfo("edit.cut", "Cut", "Edit", "Ctrl+X", keywords: "clipboard move"),
        new CommandInfo("edit.copy", "Copy", "Edit", "Ctrl+C", keywords: "clipboard"),
        new CommandInfo("edit.paste", "Paste", "Edit", "Ctrl+V", keywords: "clipboard"),
        new CommandInfo("edit.duplicate", "Duplicate", "Edit", "Ctrl+D", keywords: "clone"),
        new CommandInfo("edit.delete", "Delete", "Edit", "Delete", keywords: "remove"),
        new CommandInfo("edit.selectall", "Select All", "Edit", "Ctrl+A", keywords: "everything"),
        new CommandInfo("edit.deletereconnect", "Delete and Reconnect", "Edit", "Ctrl+Delete", keywords: "dissolve bridge keep wires"),
        new CommandInfo("edit.selectdownstream", "Select Downstream", "Edit", "L", keywords: "linked to outputs followers"),
        new CommandInfo("edit.selectupstream", "Select Upstream", "Edit", "Shift+L", keywords: "linked from inputs feeders"),
        new CommandInfo("edit.selectsimilar", "Select Similar", "Edit", "Shift+G", keywords: "same type grouped"),

        new CommandInfo("view.fit", "Fit to Screen", "View", null, keywords: "zoom all home"),
        new CommandInfo("view.zoomin", "Zoom In", "View", null),
        new CommandInfo("view.zoomout", "Zoom Out", "View", null),
        new CommandInfo("view.minimap", "Minimap", "View", "Ctrl+M", toggle: true, keywords: "overview map navigator"),
        new CommandInfo("view.resetwidth", "Reset Node Width", "View", null, keywords: "resize automatic"),
        new CommandInfo("view.collapseall", "Collapse All Nodes", "View", null, keywords: "fold minimize"),
        new CommandInfo("view.expandall", "Expand All Nodes", "View", null, keywords: "unfold maximize"),
        new CommandInfo("view.previews", "Node Value Previews", "View", null, toolbar: true, toggle: true, keywords: "bubble preview values"),
        new CommandInfo("view.settings", "Settings…", "View", null, toolbar: true, keywords: "options preferences palette layout"),
        new CommandInfo("view.hud", "Performance HUD", "View", "Ctrl+Shift+F12", CommandScope.Global, toggle: true, keywords: "fps diagnostics"),

        new CommandInfo("graph.run", "Run", "Graph", "F5", CommandScope.Global, toolbar: true, keywords: "execute evaluate"),
        new CommandInfo("graph.autorun", "Auto-Run", "Graph", null, toolbar: true, toggle: true, keywords: "automatic rerun live"),
        new CommandInfo("graph.rename", "Rename Graph", "Graph", "F2"),
        new CommandInfo("graph.addnote", "Add Note", "Graph", null, keywords: "comment annotation"),
        new CommandInfo("graph.group", "Group Selection", "Graph", "Ctrl+G", keywords: "frame"),
        new CommandInfo("graph.fitframe", "Fit Frame to Contents", "Graph", "Ctrl+Shift+G", keywords: "group shrink wrap resize"),
        new CommandInfo("graph.ungroup", "Ungroup", "Graph", "Ctrl+Shift+U", keywords: "frame remove group"),
        new CommandInfo("graph.arrange", "Arrange Selection", "Graph", "Ctrl+L", keywords: "layout align tidy"),
        new CommandInfo("graph.arrangeall", "Arrange All", "Graph", "Ctrl+Shift+L", keywords: "layout align tidy whole graph"),
        new CommandInfo("node.collapse", "Collapse / Expand", "Node", "H", keywords: "fold header minimize"),
        new CommandInfo("node.hideunused", "Hide / Show Unused Sockets", "Node", "Ctrl+H", keywords: "sockets ports optional"),
        new CommandInfo("node.mute", "Mute / Unmute", "Node", "M", keywords: "bypass disable pass through wire"),
        new CommandInfo("node.resetinputs", "Reset Inputs to Default", "Node", null, keywords: "clear values defaults"),
        new CommandInfo("node.insertonwire", "Insert Into Selected Wire", "Node", null, keywords: "splice between"),
        new CommandInfo("node.autoconnect", "Connect Selected Nodes", "Node", "F", keywords: "auto link chain make links"),
        new CommandInfo("wire.mute", "Mute / Unmute Selected Wires", "Wires", null, keywords: "bypass disable ignore"),
        new CommandInfo("wire.swap", "Swap Links", "Wires", null, keywords: "exchange two wires"),
        new CommandInfo("wire.earlier", "Move Wire Earlier", "Wires", null, keywords: "order first up multi-input sequence"),
        new CommandInfo("wire.later", "Move Wire Later", "Wires", null, keywords: "order last down multi-input sequence"),
        new CommandInfo("wire.reroute", "Add Reroute to Selected Wires", "Wires", null, keywords: "knot dot bend"),
        new CommandInfo("wire.disconnect", "Disconnect Selected Wires", "Wires", null, keywords: "cut remove unlink"),
        new CommandInfo("help.palette", "Command Palette…", "Help", "Ctrl+Shift+P", CommandScope.Global, keywords: "search run any command"),
        new CommandInfo("help.guide", "UI Guide (online)", "Help", null, keywords: "documentation manual"),
        new CommandInfo("help.keys", "Keyboard & Mouse Shortcuts", "Help", "F1", CommandScope.Global, keywords: "help keys shortcuts gestures cheat sheet"),
        new CommandInfo("graph.addnode", "Add Node…", "Graph", "Space", keywords: "search quick library"),
    };

    /// <summary>Finds a command by id, or null.</summary>
    public static CommandInfo? Find(string id) => All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
}

/// <summary>A parsed keyboard shortcut: a key name plus the modifiers that must be held.</summary>
public readonly struct KeyChord : IEquatable<KeyChord>
{
    /// <summary>Creates a chord.</summary>
    public KeyChord(string key, bool ctrl, bool shift, bool alt)
    {
        Key = key;
        Ctrl = ctrl;
        Shift = shift;
        Alt = alt;
    }

    /// <summary>Key name as WPF's Key enum spells it ("H", "Delete", "F5", "Space").</summary>
    public string Key { get; }

    /// <summary>Ctrl must be held.</summary>
    public bool Ctrl { get; }

    /// <summary>Shift must be held.</summary>
    public bool Shift { get; }

    /// <summary>Alt must be held.</summary>
    public bool Alt { get; }

    /// <inheritdoc />
    public bool Equals(KeyChord other) =>
        string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase) && Ctrl == other.Ctrl && Shift == other.Shift && Alt == other.Alt;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is KeyChord other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (Key.ToUpperInvariant().GetHashCode() * 31) ^ (Ctrl ? 1 : 0) ^ (Shift ? 2 : 0) ^ (Alt ? 4 : 0);

    /// <inheritdoc />
    public override string ToString() => (Ctrl ? "Ctrl+" : string.Empty) + (Alt ? "Alt+" : string.Empty) + (Shift ? "Shift+" : string.Empty) + Key;
}

/// <summary>Parses the shortcut strings of <see cref="CommandCatalog"/>.</summary>
public static class Shortcuts
{
    /// <summary>Parses "Ctrl+Shift+L", "Delete", "F5"… Returns false for anything malformed.</summary>
    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        bool ctrl = false, shift = false, alt = false;
        var parts = text!.Split('+');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].Trim().ToUpperInvariant())
            {
                case "CTRL": ctrl = true; break;
                case "SHIFT": shift = true; break;
                case "ALT": alt = true; break;
                default: return false;
            }
        }

        var key = parts[parts.Length - 1].Trim();
        if (key.Length == 0)
        {
            return false;
        }

        chord = new KeyChord(key, ctrl, shift, alt);
        return true;
    }

    /// <summary>Every (command, chord) pair of the catalogue: shortcuts and alternates.</summary>
    public static IEnumerable<KeyValuePair<KeyChord, CommandInfo>> All()
    {
        foreach (var info in CommandCatalog.All)
        {
            if (Shortcuts.TryParse(info.Shortcut, out var main))
            {
                yield return new KeyValuePair<KeyChord, CommandInfo>(main, info);
            }

            if (Shortcuts.TryParse(info.Alternate, out var alt))
            {
                yield return new KeyValuePair<KeyChord, CommandInfo>(alt, info);
            }
        }
    }
}
