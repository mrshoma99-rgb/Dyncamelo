using System;
using System.Collections.Generic;
using System.Linq;

namespace Dyncamelo.Core.Editing;

/// <summary>How a setting is edited.</summary>
public enum SettingKind
{
    /// <summary>On or off.</summary>
    Toggle,

    /// <summary>One of a few named values.</summary>
    Choice,
}

/// <summary>One value a choice setting can take.</summary>
public sealed class SettingOption
{
    /// <summary>Creates an option.</summary>
    public SettingOption(string value, string label)
    {
        Value = value;
        Label = label;
    }

    /// <summary>Stored value.</summary>
    public string Value { get; }

    /// <summary>Text shown to the user.</summary>
    public string Label { get; }
}

/// <summary>Describes one preference for the Settings page and the generated guide.</summary>
public sealed class SettingDescriptor
{
    /// <summary>Creates a toggle setting.</summary>
    public SettingDescriptor(string id, string section, string title, string description, bool defaultValue)
    {
        Id = id;
        Section = section;
        Title = title;
        Description = description;
        Kind = SettingKind.Toggle;
        DefaultToggle = defaultValue;
        DefaultChoice = string.Empty;
        Options = new SettingOption[0];
    }

    /// <summary>Creates a choice setting.</summary>
    public SettingDescriptor(string id, string section, string title, string description, string defaultValue, params SettingOption[] options)
    {
        Id = id;
        Section = section;
        Title = title;
        Description = description;
        Kind = SettingKind.Choice;
        DefaultChoice = defaultValue;
        Options = options;
    }

    /// <summary>Stable key (the name the preference is stored under).</summary>
    public string Id { get; }

    /// <summary>Page section it is listed in (one of <see cref="SettingsCatalog.Sections"/>).</summary>
    public string Section { get; }

    /// <summary>Short name.</summary>
    public string Title { get; }

    /// <summary>One or two sentences on what it changes.</summary>
    public string Description { get; }

    /// <summary>Toggle or choice.</summary>
    public SettingKind Kind { get; }

    /// <summary>Default of a toggle.</summary>
    public bool DefaultToggle { get; }

    /// <summary>Default value of a choice.</summary>
    public string DefaultChoice { get; }

    /// <summary>The values of a choice.</summary>
    public IReadOnlyList<SettingOption> Options { get; }

    /// <summary>The option with the given value, or null.</summary>
    public SettingOption? OptionFor(string value) =>
        Options.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.Ordinal));
}

/// <summary>
/// Every preference of the editor, grouped by page section. The Settings page, the search inside it and the generated
/// user guide are all produced from this list, so a preference is documented the moment it exists.
/// </summary>
public static class SettingsCatalog
{
    /// <summary>The Settings page sections, in order. Shortcuts and Diagnostics are built from other sources.</summary>
    public static readonly IReadOnlyList<string> Sections = new[] { "Appearance", "Canvas", "Editing", "Shortcuts", "Diagnostics" };

    /// <summary>All toggle and choice preferences.</summary>
    public static readonly IReadOnlyList<SettingDescriptor> All = new[]
    {
        new SettingDescriptor("density", "Appearance", "Node density",
            "Row height of nodes. Compact fits more on screen, comfortable is easier to click.", "normal",
            new SettingOption("compact", "Compact"), new SettingOption("normal", "Normal"), new SettingOption("comfortable", "Comfortable")),
        new SettingDescriptor("cbGlyphs", "Appearance", "Type letters in sockets",
            "Draw a short letter naming the type inside every socket, so types do not rely on colour alone.", false),
        new SettingDescriptor("libraryPanel", "Appearance", "Node library panel",
            "Show the node library on the left of the canvas. It can also be hidden with the arrow in its header and brought back with the tab at the canvas edge.", true),
        new SettingDescriptor("libraryDescriptions", "Appearance", "Descriptions in the library",
            "Show a description line under each node in the library panel.", true),
        new SettingDescriptor("nodePreviews", "Appearance", "Value previews under nodes",
            "Show a preview bubble with the result under each node after a run.", true),

        new SettingDescriptor("uiScale", "Appearance", "Window scale",
            "Make everything in the Dyncamelo window smaller or larger, for high-resolution screens or a small pane.", "100",
            new SettingOption("90", "90%"), new SettingOption("100", "100%"), new SettingOption("110", "110%"),
            new SettingOption("125", "125%"), new SettingOption("150", "150%")),
        new SettingDescriptor("statusHints", "Appearance", "Hints in the status bar",
            "Show a line of suggestions at the bottom that follows what you are doing: the keys for the selected nodes, what a dragged wire will do.", true),
        new SettingDescriptor("emptyHints", "Appearance", "Hints on an empty canvas",
            "Show the ways to add a first node while the canvas is empty.", true),
        new SettingDescriptor("showGrid", "Canvas", "Grid lines", "Draw the grid on the canvas background.", true),
        new SettingDescriptor("snapToGrid", "Canvas", "Snap nodes to the grid", "Dragged nodes land on grid lines instead of anywhere.", true),
        new SettingDescriptor("straightWires", "Canvas", "Straight wires",
            "Draw wires as straight lines instead of curves. Faster on very large graphs.", false),
        new SettingDescriptor("minimap", "Canvas", "Minimap",
            "The overview in the corner. Automatic shows it once the graph has 40 or more nodes.", "auto",
            new SettingOption("auto", "Automatic"), new SettingOption("on", "Always"), new SettingOption("off", "Never")),
        new SettingDescriptor("wireFocus", "Canvas", "Highlight the wires of the selected node",
            "When a node is selected, its wires are drawn heavier and every other wire fainter, so a connection can be followed through a busy graph.", true),
        new SettingDescriptor("layeredArrange", "Canvas", "Layered Arrange",
            "Arrange with the layered layout that keeps wire crossings to a minimum. Off uses simple columns.", true),

        new SettingDescriptor("scrubSpeed", "Editing", "Number drag speed", "How far the mouse travels for each step of a number field.", "normal",
            new SettingOption("slow", "Slow"), new SettingOption("normal", "Normal"), new SettingOption("fast", "Fast")),
        new SettingDescriptor("scrubWrap", "Editing", "Wrap the pointer while dragging numbers",
            "When a number is dragged to the edge of the screen the pointer reappears on the other side and the value carries on, so any distance can be dragged.", true),
        new SettingDescriptor("liveScrub", "Editing", "Run while dragging numbers",
            "Re-run the graph continuously while a number field is dragged. Off runs once on release, which is safer for graphs that call Navisworks.", false),
        new SettingDescriptor("hideUnusedDefault", "Editing", "Hide unused inputs by default",
            "Nodes that have not been set either way hide their unconnected optional inputs.", false),
        new SettingDescriptor("autoOffset", "Editing", "Make room when inserting on a wire",
            "Nodes downstream move right when a node is dropped onto a wire.", true),
        new SettingDescriptor("deleteReconnectsReroutes", "Editing", "Deleting a reroute keeps the wire",
            "Removing a reroute dot joins the wire back up instead of deleting it.", true),
        new SettingDescriptor("escCancels", "Editing", "Esc cancels a running graph",
            "Pressing Esc while a run is in progress stops it before the next node. The node in progress finishes (a Navisworks call cannot be interrupted) and the run continues from there next time.", true),
        new SettingDescriptor("autosave", "Editing", "Autosave unsaved work",
            "Keep a copy of a graph with unsaved changes once a minute, and offer it back if Navisworks closed or crashed before you saved. The copy is deleted when you save.", true),
        new SettingDescriptor("previewSelection", "Editing", "Highlight selected node in Navisworks",
            "Select the model items a node outputs in the viewport when the node is clicked. Overwrites the live selection, so turn it off if you use Selection.Current.", false),
        new SettingDescriptor("doubleClick", "Editing", "Double-click empty canvas", "What double-clicking the empty canvas does.", "string",
            new SettingOption("string", "Insert a String node"), new SettingOption("number", "Insert a Number node"),
            new SettingOption("note", "Add a note"), new SettingOption("none", "Do nothing")),
    };

    /// <summary>The descriptor with the given id, or null.</summary>
    public static SettingDescriptor? Find(string id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    /// <summary>The descriptors of one section.</summary>
    public static IEnumerable<SettingDescriptor> InSection(string section) =>
        All.Where(s => string.Equals(s.Section, section, StringComparison.Ordinal));

    /// <summary>The descriptors whose title, description or section contain every word typed.</summary>
    public static IReadOnlyList<SettingDescriptor> Search(string? query)
    {
        var words = (query ?? string.Empty).ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return All;
        }

        return All.Where(s =>
        {
            var hay = (s.Title + " " + s.Description + " " + s.Section).ToLowerInvariant();
            return words.All(hay.Contains);
        }).ToList();
    }
}
