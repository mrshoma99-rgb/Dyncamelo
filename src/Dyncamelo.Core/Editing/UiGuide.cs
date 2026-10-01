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
    // A command's default shortcut in code style, read from the catalogue so the prose cannot name a stale key.
    private static string K(string commandId) => "`" + (CommandCatalog.Find(commandId)?.Shortcut ?? "unassigned") + "`";

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
        sb.Append("* The **header** carries the node's name (double-click to rename) and its state colour; `H` collapses the node to a capsule in its category colour — the title in the middle, the input sockets down the left edge and the output sockets down the right — so it can still be wired.\n");
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
        sb.Append("* **Mute and Freeze** both stop a node from running, but they differ in what happens around it. A **muted** node (`M`, badge *MUTED*) is bypassed: each output passes the first input of a matching type straight through, the node counts as done, and everything downstream still runs on that passed-through data. A **frozen** node (`Shift+M`, badge *FROZEN*) is held: it **and everything downstream of it** are skipped by runs and keep the results from their last run, so a slow branch is not recomputed while you work elsewhere. Use *mute* to switch a step off inside a live chain (a filter, a recolour); use *freeze* to stop a whole branch from recomputing. Both are undoable and saved with the graph. A muted *wire* is different again: only that connection is ignored, and the input falls back to its own value.\n");
        sb.Append("* A **muted wire** is ignored by the run — a quick way to switch a branch off without deleting it (`Ctrl` held while cutting mutes instead of deleting).\n");
        sb.Append("* Dropping a wire on empty canvas opens the node search filtered to nodes that can accept it; picking one connects it.\n\n");

        sb.Append("## Node groups\n\n");
        sb.Append("A **node group** is a reusable piece of graph — a few nodes that do one job, with their own inputs and outputs — stored in the file. Use it as often as you like; edit it once.\n\n");
        sb.Append("* **Make one:** select nodes and choose *Node Groups ▸ Make Node Group* (`Ctrl+Alt+G`). The wires that crossed the selection become the group's inputs and outputs, and an **instance** takes the selection's place, so the graph computes exactly what it did.\n");
        sb.Append("* **Open one:** select an instance and press `Tab` (or click the arrow in its header). The canvas shows the group's nodes between a **Group Input** and a **Group Output** node, and the bar above the canvas shows where you are; `Shift+Tab` (or *Close group*) goes back. Running while a group is open still runs the whole graph, so an edit shows its effect at once. Each level keeps its own Undo history.\n");
        sb.Append("* **Edit the interface:** inside a group, *+ Add input* / *+ Add output* on the Group Input / Group Output node adds a socket, and right-clicking a socket renames, retypes, moves or removes it (wires on a removed socket go too, everywhere the group is used, and come back on undo). Sockets pass whole values, so a list travels through as one list. Giving a socket a type colours it and, on instances, gives it an inline editor.\n");
        sb.Append("* **Instances share the group:** editing the group changes every instance. *Make Node Group Single User* gives one instance its own copy; *Ungroup* (`Ctrl+Alt+U`) puts a copy of the group's nodes in place of an instance. The group stays in the file until you use *Delete Unused Node Groups*.\n");
        sb.Append("* Groups can hold other groups but never themselves. The library lists the file's groups under **Node Groups**, so an instance is added like any node; copying an instance into another file brings its group along. Older versions of Dyncamelo refuse a file with node groups rather than silently dropping them.\n");
        sb.Append("* The progress text names the group path (`Outer ▸ Inner ▸ node`), and `Esc` cancels from inside a group too.\n\n");

        sb.Append("## Running and stopping\n\n");
        sb.Append("* **Run** (`F5`) executes the nodes that changed since the last run and reuses the rest; **Auto** runs again after every edit.\n");
        sb.Append("* While a graph runs, the window says which node is working (`12 / 40 — name`, with the group path inside a node group). **Press `Esc` to stop it** — switchable in Settings ▸ Editing ▸ *Esc cancels a running graph*.\n");
        sb.Append("* The run halts **before the next node**, or between the items of a node that is working through a list, or between the passes of a loop. A single Navisworks call already under way cannot be interrupted, and what finished nodes already changed in Navisworks is kept. A node or loop that was cut short keeps its previous results and waits, together with everything after it, so the next **Run** carries on where this one stopped.\n\n");

        sb.Append("## Keeping your work safe\n\n");
        sb.Append("* A `*` after the graph name in the header means there are **unsaved changes**. Saving removes it; undoing back to the saved state keeps it, to be on the safe side.\n");
        sb.Append("* **New**, **Open**, opening a sample, opening a recent file, and dropping a `.dyc` file on the canvas first ask *Save / Don't Save / Cancel* when something is unsaved. A cancelled or failed save cancels the whole action, so nothing is thrown away by accident.\n");
        sb.Append("* **Autosave** (Settings ▸ Editing) keeps a copy of a graph with unsaved changes once a minute in `%APPDATA%\\Dyncamelo\\recovery`, and again when the pane is closed. If Navisworks closed or crashed before you saved, Dyncamelo offers that copy back the next time the editor opens on an empty canvas; saving, or answering *No*, deletes it. A restored graph counts as unsaved until you save it.\n");
        sb.Append("* Drag a `.dyc` file from Explorer onto the canvas to open it.\n\n");

        sb.Append("## Finding your way\n\n");
        sb.Append("* **Problems.** The *Warnings* and *Errors* counts in the status bar are buttons: click them (or press ").Append(K("view.problems")).Append(") to list every node that failed or warned in the last run, errors first. Click a row to select that node and bring it into view. ");
        sb.Append(K("graph.nextproblem")).Append(" and ").Append(K("graph.prevproblem")).Append(" step through them from wherever you are, and the status bar says which one you are on.\n");
        sb.Append("* **Why didn't this run?** Select a node and press ").Append(K("node.explain")).Append(": the status bar explains in words whether the node is frozen, sits after a frozen node, is muted, failed (and with what), is waiting for an input, or is just waiting for the next Run — naming the node responsible when it is another one.\n");
        sb.Append("* **Run up to a node.** ").Append(K("graph.runtohere")).Append(" runs only the selected nodes and what they depend on, then stops. Everything after them keeps its previous results and stays pending, so the next ordinary **Run** finishes the job. Useful while building a graph whose later steps are slow. Inside a node group it runs up to the group instance.\n");
        sb.Append("* **Undo history.** ").Append(K("edit.history")).Append(" lists every step of the current graph (or the node group you are editing). Click a step to go back or forward to exactly that point; steps you undid are shown dimmed until you make a new edit.\n");
        sb.Append("* **Bookmarks.** ").Append(K("view.addbookmark")).Append(" names the current view (position and zoom) and saves it **in the graph file**; ").Append(K("view.bookmarks")).Append(" lists them — click one to go there, the ✕ removes it. Good for touring a large graph (*inputs*, *filters*, *export*).\n");
        sb.Append("* **Find a node by name.** ").Append(K("graph.findnode")).Append(" opens the command palette on the nodes of this canvas (it starts with `@`; typing plain text in the palette finds nodes too). `Enter` selects the node and scrolls to it.\n");
        sb.Append("* **Frame and arrows.** ").Append(K("view.frameselected")).Append(" zooms to the selection. The arrow keys (").Append(K("edit.navleft")).Append(", ").Append(K("edit.navright")).Append(", ").Append(K("edit.navup")).Append(", ").Append(K("edit.navdown")).Append(") move the selection to the nearest node in that direction, so a graph can be toured without the mouse; they only act when the canvas has the keyboard.\n");
        sb.Append("* **Quick search suggestions.** ").Append(K("graph.addnode")).Append(" before typing lists your starred nodes and the ones you added most recently (so Space, Enter repeats the last node); while typing, starred and recent nodes come first among equally good matches.\n");
        sb.Append("* **Follow a wire.** With a node selected, its wires are drawn heavier and the rest fainter (Settings ▸ Canvas ▸ *Highlight the wires of the selected node*); hovering a wire thickens it.\n");
        sb.Append("* **Pasting a coordinate.** Hover a number field and press `Ctrl+V` with `1, 2, 3` (or cells copied from a spreadsheet) on the clipboard: the values go into that field and the number fields after it, as one undo step. A wired field stops the paste.\n");
        sb.Append("* **Colour eyedropper.** The dropper button in a colour popup turns the next click anywhere on the screen — the Navisworks viewport included — into the colour under the pointer. `Esc` or a right click cancels.\n");
        sb.Append("* **Dragging numbers further.** With *Wrap the pointer while dragging numbers* on (Settings ▸ Editing), dragging a number to the edge of the screen brings the pointer back on the other side and the value carries on.\n");
        sb.Append("* **Socket tooltips.** Hover a socket to see its type and, after a run, the **value it holds** — for a list the number of items and the first few — or, on a wired input, what arrives on the wire.\n");
        sb.Append("* **Hints.** The status bar shows a hint line that follows what you are doing (the keys for the selected nodes, what releasing a dragged wire will do), and an empty canvas lists the ways to add the first node. Both can be switched off in Settings ▸ Appearance, which also has **Window scale** (90–150%) for high-resolution screens or a small pane.\n\n");

        sb.Append("## The Script Player\n\n");
        sb.Append("The **Player** runs a saved graph without opening the node editor: pick a script, fill in the values it asks for, press **Run**, read the results. It is a pane of its own (ribbon ▸ BIMCamel ▸ **Dyncamelo Player**, or ").Append("*Open Script Player*").Append(" in the View menu and the palette) and does not load the editor, so it opens fast and stays small.\n\n");
        sb.Append("* **Where scripts live.** Every `.dyc` file under `Documents\\Dyncamelo\\Scripts` (subfolders included, up to four levels) is listed. **Script folders ▸ Add a folder…** adds more — a shared network folder, say; ✕ takes a folder off the list without touching the files. The list filters as you type and ↻ looks again.\n");
        sb.Append("* **What a script asks for.** The *form* is built from the graph itself: every **input node** (Number, Sliders, Boolean, String, File / Folder path, Colour) becomes a field, and so does any node input you chose to show. The fields are the same editors as on the canvas — scrub a number, pick a colour, browse for a file. ↺ puts a field back to the value saved in the script, and the values you typed are remembered per script.\n");
        sb.Append("* **What it shows.** After a run the Player lists the results: every **Watch** node (text, list and image watches) and any node you marked to show. Failed or warned nodes are listed underneath with the reason; **Copy** puts the results on the clipboard as text. `Esc` stops a running script, exactly as in the editor.\n");
        sb.Append("* **Choosing what appears — in the editor.** ").Append(K("node.player")).Append(" (*Show / Hide in Player*) toggles the selected nodes: an input or Watch node is shown unless you hide it, any other node is hidden unless you show it (its first result is then listed). ");
        sb.Append("*Show / Hide Unwired Inputs in Player* (").Append("palette, Node menu").Append(") offers a node's unconnected inputs as fields; the same choice is in a socket's right-click menu, and a shown node or input carries a small **▶** badge. *Script Description…* (Graph menu) sets the text shown under the script's name in the Player. All of this is saved in the `.dyc` file and is undoable.\n");
        sb.Append("* **Edit and File.** *Edit* opens the script in the node editor (pane and file both); *File* shows it in Explorer.\n");
        sb.Append("* **Safety.** A script that **changes the model** (a node that writes to the model or to disk: Appearance, Selection, Export…) says so above the form, and the first time you run it — and again whenever the file changes — Dyncamelo asks for confirmation. Scripts that only read are never asked about. The answer is remembered for that file as it is now; edit the script and the question comes back.\n");
        sb.Append("* **From other tools.** The add-in plugin `Dyncamelo.Run.DYNC` runs a script by path: `Execute(\"C:\\\\Scripts\\\\audit.dyc\")` — for other add-ins, the Navisworks Automation API (`ExecuteAddInPlugin`) and the Batch Utility. It returns `0` when no node failed and `1` otherwise, and applies the same confirmation.\n");
        sb.Append("* **Limits.** Nodes inside node groups are not offered in the form (put the input at the top level); the list stops at 2000 scripts; a script that needs a node library that is not installed is listed with what is missing and cannot run.\n\n");

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
