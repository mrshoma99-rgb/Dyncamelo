using System.Linq;
using System.Text;

namespace Dyncamelo.Core.Editing;

/// <summary>
/// Produces <c>docs/UI_GUIDE.md</c>. The command, gesture and setting tables come straight from
/// <see cref="CommandCatalog"/>, <see cref="HelpContent"/> and <see cref="SettingsCatalog"/>, so the guide cannot drift from
/// the editor; a test fails when the committed file differs from this output.
/// </summary>
public static class UiGuide
{
    /// <summary>The guide as Markdown (LF line endings).</summary>
    public static string Build()
    {
        var sb = new StringBuilder();
        sb.Append("# Dyncamelo editor guide\n\n");
        sb.Append("> The tables in this file are generated from the editor's own command and settings catalogues by a test (`UiGuideTests`). ");
        sb.Append("Do not edit them by hand — change the catalogue and regenerate with `DYNCAMELO_REGEN_DOCS=1 dotnet test tests/Dyncamelo.Core.Tests --filter UiGuideTests`.\n\n");

        sb.Append("Every function of the editor can be reached four ways: a **menu**, a **shortcut**, the **command palette** (`Ctrl+Shift+P`) and the **Settings** page. ");
        sb.Append("If you cannot remember where something is, open the palette and type a word of its name.\n\n");

        sb.Append("## Contents\n\n");
        sb.Append("1. [Finding things](#finding-things)\n");
        sb.Append("2. [Anatomy of a node](#anatomy-of-a-node)\n");
        sb.Append("3. [Sockets and wires](#sockets-and-wires)\n");
        sb.Append("4. [Commands and shortcuts](#commands-and-shortcuts)\n");
        sb.Append("5. [Mouse and gestures](#mouse-and-gestures)\n");
        sb.Append("6. [Settings](#settings)\n");
        sb.Append("7. [Changing shortcuts](#changing-shortcuts)\n\n");

        sb.Append("## Finding things\n\n");
        sb.Append("| To… | Do this |\n|---|---|\n");
        sb.Append("| Add a node | `Space` over the canvas, then type part of its name |\n");
        sb.Append("| Run any command | `Ctrl+Shift+P`, type a word, press `Enter` |\n");
        sb.Append("| Find a setting | `Ctrl+Shift+P` and type its name — the entry opens the Settings page on it |\n");
        sb.Append("| See every shortcut | `F1` (always shows the shortcuts currently in force) |\n");
        sb.Append("| Overview of a big graph | `Home` fits everything; `Ctrl+M` shows the minimap (automatic from 40 nodes) |\n\n");

        sb.Append("## Anatomy of a node\n\n");
        sb.Append("* The **header** carries the node's name (double-click to rename) and its state colour; `H` collapses the node to its header.\n");
        sb.Append("* **Rows** run top to bottom: outputs first as labels on the right, then inputs. An input row has a **socket** on the left and, when nothing is wired to it, an **inline editor** — a draggable number field, a checkbox, a text box, a dropdown or segmented switcher for named choices, a colour swatch, a file field with a `…` button, or a **model-element picker** that takes the current Navisworks selection.\n");
        sb.Append("* A number field that differs from its default shows a dot at its left edge; **hover a field and press `Backspace`** to put the default back. `Ctrl+C` / `Ctrl+V` while hovering copies or pastes the value.\n");
        sb.Append("* Optional inputs can be hidden while they are unconnected (**Hide / Show Unused Sockets**, `Ctrl+H`); nodes with an *Advanced* panel fold rarely-used inputs into it.\n");
        sb.Append("* Drag the node's **right edge** to change its width; **Reset Node Width** in the View menu restores the default.\n\n");

        sb.Append("## Sockets and wires\n\n");
        sb.Append("Socket colour names the kind of data. With **Type letters in sockets** turned on (Settings ▸ Appearance) a letter is drawn inside each socket too, so the kind never depends on colour alone. The socket shape shows the structure: a **circle** is a single value, a **rounded square** a list, a **square with a hole** a list of lists, and a **diamond** a value whose structure is not known until it runs.\n\n");
        sb.Append("| Kind | Colour | Letter |\n|---|---|---|\n");
        foreach (var family in PortKindPalette.Families)
        {
            var glyph = PortKindPalette.Glyph(family);
            sb.Append("| ").Append(family).Append(" | `").Append(PortKindPalette.Hex(family)).Append("` | ").Append(glyph.Length == 0 ? "—" : "`" + glyph + "`").Append(" |\n");
        }

        sb.Append("\n* A **pill** — an elongated socket — is a **multi-input**: connect as many wires to it as you like. With one wire it behaves exactly like an ordinary input; with several, the node receives everything they carry combined into one list, in the order the wires were made (wires carrying lists contribute their elements). The wires fan out along the pill, one landing point each, and the node's row grows to fit them. Drag from the pill to take off the wire under the pointer; drop a picked-up wire back on the pill at another slot to reorder, or use **Move Wire Earlier / Later** on a selected wire. Any input marked `[MultiInput]` (see the authoring guide) is drawn this way — for example the item lists of the Appearance, Selection and Export nodes, and **List.Merge**.\n");
        sb.Append("* A **dashed wire** feeds a list into a single-value input: the node runs once per item (replication).\n");
        sb.Append("* A **muted wire** is ignored by the run — a quick way to switch a branch off without deleting it (`Ctrl` held while cutting mutes instead of deleting).\n");
        sb.Append("* Dropping a wire on empty canvas opens the node search filtered to nodes that can accept it; picking one connects it.\n\n");

        sb.Append("## Commands and shortcuts\n\n");
        sb.Append("Default shortcuts. **Canvas** commands only act when the canvas has the keyboard (so typing in a box never triggers them); **Everywhere** commands also work while a text box has focus.\n\n");
        foreach (var category in CommandCatalog.Categories)
        {
            var commands = CommandCatalog.All.Where(c => c.Category == category).ToList();
            if (commands.Count == 0)
            {
                continue;
            }

            sb.Append("### ").Append(category).Append("\n\n");
            sb.Append("| Command | Shortcut | Works |\n|---|---|---|\n");
            foreach (var c in commands)
            {
                var keys = c.Shortcut == null ? "—" : "`" + c.Shortcut + "`" + (c.Alternate == null ? string.Empty : " or `" + c.Alternate + "`");
                var title = c.Title + (c.IsToggle ? " *(toggle)*" : string.Empty);
                sb.Append("| ").Append(title).Append(" | ").Append(keys).Append(" | ")
                    .Append(c.Scope == CommandScope.Global ? "Everywhere" : "Canvas").Append(" |\n");
            }

            sb.Append('\n');
        }

        sb.Append("## Mouse and gestures\n\n");
        sb.Append("| Gesture | How |\n|---|---|\n");
        foreach (var line in HelpContent.Gestures)
        {
            sb.Append("| ").Append(line.Action).Append(" | ").Append(line.Keys).Append(" |\n");
        }

        sb.Append("\n## Settings\n\n");
        sb.Append("Open with the gear in the toolbar, **View ▸ Settings…**, or the palette. Every setting can be found with the search box on the page; the ↺ button puts a changed setting back to its default.\n\n");
        foreach (var section in SettingsCatalog.Sections)
        {
            sb.Append("### ").Append(section).Append("\n\n");
            var settings = SettingsCatalog.InSection(section).ToList();
            if (section == "Shortcuts")
            {
                sb.Append("The table of every command with its shortcut; see [Changing shortcuts](#changing-shortcuts).\n\n");
            }
            else if (section == "Diagnostics")
            {
                sb.Append("Buttons for the performance HUD, the keyboard help and this guide, and **Reset all settings**, which restores every preference and shortcut to its default (favourites and recent files are kept).\n\n");
            }
            else
            {
                if (section == "Appearance")
                {
                    sb.Append("The **colour palette** of the whole editor is chosen here too.\n\n");
                }

                sb.Append("| Setting | Values | Default | What it does |\n|---|---|---|---|\n");
                foreach (var s in settings)
                {
                    var values = s.Kind == SettingKind.Toggle ? "On / Off" : string.Join(" / ", s.Options.Select(o => o.Label));
                    var def = s.Kind == SettingKind.Toggle ? (s.DefaultToggle ? "On" : "Off") : s.OptionFor(s.DefaultChoice)!.Label;
                    sb.Append("| ").Append(s.Title).Append(" | ").Append(values).Append(" | ").Append(def).Append(" | ").Append(s.Description).Append(" |\n");
                }

                sb.Append('\n');
            }
        }

        sb.Append("## Changing shortcuts\n\n");
        sb.Append("1. Open **Settings ▸ Shortcuts** (or search the palette for *shortcut*).\n");
        sb.Append("2. Press **Change** on a command, then press the keys you want. `Esc` cancels; `Backspace` removes the shortcut.\n");
        sb.Append("3. If the keys are already used, the row says by which command and keeps listening — nothing is taken away silently.\n");
        sb.Append("4. Commands that also work while a text box has focus (`Everywhere` above) must use `Ctrl`, `Alt` or a function key, because plain letters are typed text.\n");
        sb.Append("5. ↺ on a row restores its default; **Reset all shortcuts** restores every one.\n\n");
        sb.Append("The menus, the key handling, the `F1` help and the palette all read the same keymap, so a change shows up everywhere at once and is kept between sessions.\n");
        return sb.ToString();
    }
}
