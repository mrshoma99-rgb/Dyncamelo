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
        CommandScope scope = CommandScope.Canvas, bool toolbar = false, bool toggle = false, string keywords = "")
    {
        Id = id;
        Title = title;
        Category = category;
        Shortcut = shortcut;
        Scope = scope;
        InToolbar = toolbar;
        IsToggle = toggle;
        Keywords = keywords;
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
        new CommandInfo("file.new", "New", "File", "Ctrl+N", CommandScope.Global, toolbar: true),
        new CommandInfo("file.open", "Open…", "File", "Ctrl+O", CommandScope.Global, toolbar: true),
        new CommandInfo("file.save", "Save", "File", "Ctrl+S", CommandScope.Global, toolbar: true),
        new CommandInfo("file.saveas", "Save As…", "File", "Ctrl+Shift+S", CommandScope.Global, toolbar: true),

        new CommandInfo("edit.undo", "Undo", "Edit", "Ctrl+Z", toolbar: true, keywords: "revert back"),
        new CommandInfo("edit.redo", "Redo", "Edit", "Ctrl+Y", toolbar: true, keywords: "repeat forward"),
        new CommandInfo("edit.copy", "Copy", "Edit", "Ctrl+C", keywords: "clipboard"),
        new CommandInfo("edit.paste", "Paste", "Edit", "Ctrl+V", keywords: "clipboard"),
        new CommandInfo("edit.duplicate", "Duplicate", "Edit", "Ctrl+D", keywords: "clone"),
        new CommandInfo("edit.delete", "Delete", "Edit", "Delete", keywords: "remove"),

        new CommandInfo("view.fit", "Fit to Screen", "View", null, toolbar: true, keywords: "zoom all home"),
        new CommandInfo("view.zoomin", "Zoom In", "View", null, toolbar: true),
        new CommandInfo("view.zoomout", "Zoom Out", "View", null, toolbar: true),
        new CommandInfo("view.hud", "Performance HUD", "View", "Ctrl+Shift+F12", CommandScope.Global, toggle: true, keywords: "fps diagnostics"),

        new CommandInfo("graph.run", "Run", "Graph", "F5", CommandScope.Global, toolbar: true, keywords: "execute evaluate"),
        new CommandInfo("graph.rename", "Rename Graph", "Graph", "F2"),
        new CommandInfo("graph.addnote", "Add Note", "Graph", null, toolbar: true, keywords: "comment annotation"),
        new CommandInfo("graph.group", "Group Selection", "Graph", "Ctrl+G", keywords: "frame"),
        new CommandInfo("graph.arrange", "Arrange Selection", "Graph", "Ctrl+L", keywords: "layout align tidy"),
        new CommandInfo("graph.addnode", "Add Node…", "Graph", "Space", keywords: "search quick library"),
    };

    /// <summary>Finds a command by id, or null.</summary>
    public static CommandInfo? Find(string id) => All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
}
