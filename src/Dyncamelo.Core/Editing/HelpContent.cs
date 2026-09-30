using System.Collections.Generic;
using System.Linq;

namespace Dyncamelo.Core.Editing;

/// <summary>One line of the help overlay: what it does and how to trigger it.</summary>
public sealed class HelpLine
{
    /// <summary>Creates a line.</summary>
    public HelpLine(string action, string keys)
    {
        Action = action;
        Keys = keys;
    }

    /// <summary>What it does.</summary>
    public string Action { get; }

    /// <summary>The shortcut or gesture; empty when the command is menu-only.</summary>
    public string Keys { get; }
}

/// <summary>A titled group of help lines.</summary>
public sealed class HelpSection
{
    /// <summary>Creates a section.</summary>
    public HelpSection(string title, IReadOnlyList<HelpLine> lines)
    {
        Title = title;
        Lines = lines;
    }

    /// <summary>Group heading.</summary>
    public string Title { get; }

    /// <summary>Its lines.</summary>
    public IReadOnlyList<HelpLine> Lines { get; }
}

/// <summary>
/// The text of the keyboard/mouse help overlay. Keyboard sections are generated from <see cref="CommandCatalog"/>,
/// so a shortcut is documented the moment it exists; the mouse gestures are listed here once.
/// </summary>
public static class HelpContent
{
    /// <summary>Mouse and pointer gestures (they have no menu item, so they are listed by hand).</summary>
    public static readonly IReadOnlyList<HelpLine> Gestures = new[]
    {
        new HelpLine("Stop a running graph", "Esc (the run halts before the next node and continues from there next time)"),
        new HelpLine("Open a graph", "Drag a .dyc file from Explorer onto the canvas"),
        new HelpLine("Pan the canvas", "Right or middle mouse drag"),
        new HelpLine("Zoom", "Mouse wheel"),
        new HelpLine("Search for a node here", "Space over the canvas"),
        new HelpLine("Double-click empty canvas", "Does what Settings ▸ Editing says (a String node by default)"),
        new HelpLine("Connect a socket to a new node", "Drag a wire onto empty canvas"),
        new HelpLine("Insert a node into a wire", "Drag the node onto the wire"),
        new HelpLine("Add a reroute", "Double-click a wire"),
        new HelpLine("Connect many wires to one socket", "A pill-shaped socket takes any number of wires"),
        new HelpLine("Take one wire off a pill", "Drag from the pill; the wire under the pointer comes off"),
        new HelpLine("Reorder the wires of a pill", "Drop a picked-up wire back on the pill at the slot you want"),
        new HelpLine("Move a link to another input", "Drag from a connected input"),
        new HelpLine("Swap two links", "Drop a picked-up link on an occupied input with Shift held"),
        new HelpLine("Cut wires", "Alt+Shift+drag across them"),
        new HelpLine("Mute the wires you cut", "Hold Ctrl while releasing the cut"),
        new HelpLine("Push nodes apart", "Ctrl+Shift+drag"),
        new HelpLine("Change a number", "Drag the field; Shift for fine steps, Ctrl to snap; the pointer wraps at the screen edge"),
        new HelpLine("Paste a coordinate", "Hover a number field, Ctrl+V with \"1, 2, 3\" on the clipboard fills it and the fields after it"),
        new HelpLine("Pick a colour from the screen", "The dropper in a colour popup; click anywhere, Esc cancels"),
        new HelpLine("Offer an input in the Script Player", "Right-click its socket ▸ Show in Player (a ▶ badge marks nodes the Player uses)"),
        new HelpLine("Add a node group socket", "Drop a wire on the Group Input or Group Output node itself"),
        new HelpLine("Copy or paste a field's value", "Hover it, then Ctrl+C / Ctrl+V"),
        new HelpLine("Reset a field to its default", "Hover it, then Backspace"),
        new HelpLine("Rename a node", "Double-click its title"),
        new HelpLine("Resize a node", "Drag its right edge"),
    };

    /// <summary>All sections: one per command category that has shortcuts, then the gestures.</summary>
    /// <param name="keymap">The shortcuts in force (with the user's rebindings); the defaults when null.</param>
    public static IReadOnlyList<HelpSection> Build(Keymap? keymap = null)
    {
        keymap ??= new Keymap();
        var sections = new List<HelpSection>();
        foreach (var category in CommandCatalog.Categories)
        {
            var lines = new List<HelpLine>();
            foreach (var c in CommandCatalog.All)
            {
                var main = keymap.ShortcutOf(c.Id);
                if (c.Category != category || main == null)
                {
                    continue;
                }

                var alternate = keymap.AlternateOf(c.Id);
                lines.Add(new HelpLine(c.Title, alternate == null ? main : main + " / " + alternate));
            }

            if (lines.Count > 0)
            {
                sections.Add(new HelpSection(category, lines));
            }
        }

        sections.Add(new HelpSection("Mouse", Gestures));
        return sections;
    }
}
