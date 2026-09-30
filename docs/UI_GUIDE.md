# Dyncamelo editor guide

> The tables in this file are generated from the editor's own command and settings catalogues by a test (`UiGuideTests`). Do not edit them by hand — change the catalogue and regenerate with `DYNCAMELO_REGEN_DOCS=1 dotnet test tests/Dyncamelo.Core.Tests --filter UiGuideTests`.

Every function of the editor can be reached four ways: a **menu**, a **shortcut**, the **command palette** (`Ctrl+Shift+P`) and the **Settings** page. If you cannot remember where something is, open the palette and type a word of its name.

## Contents

1. [Finding things](#finding-things)
2. [Anatomy of a node](#anatomy-of-a-node)
3. [Sockets and wires](#sockets-and-wires)
4. [Commands and shortcuts](#commands-and-shortcuts)
5. [Mouse and gestures](#mouse-and-gestures)
6. [Settings](#settings)
7. [Changing shortcuts](#changing-shortcuts)

## Finding things

| To… | Do this |
|---|---|
| Add a node | `Space` over the canvas, then type part of its name |
| Run any command | `Ctrl+Shift+P`, type a word, press `Enter` |
| Find a setting | `Ctrl+Shift+P` and type its name — the entry opens the Settings page on it |
| See every shortcut | `F1` (always shows the shortcuts currently in force) |
| Overview of a big graph | `Home` fits everything; `Ctrl+M` shows the minimap (automatic from 40 nodes) |

## Anatomy of a node

* The **header** carries the node's name (double-click to rename) and its state colour; `H` collapses the node to its header and a slim column of sockets on each edge, so it can still be wired.
* **Rows** run top to bottom: outputs first as labels on the right, then inputs. An input row has a **socket** on the left and, when nothing is wired to it, an **inline editor** — a draggable number field, a checkbox, a text box, a dropdown or segmented switcher for named choices, a colour swatch, a file field with a `…` button, or a **model-element picker** that takes the current Navisworks selection.
* A number field that differs from its default shows a dot at its left edge; **hover a field and press `Backspace`** to put the default back. `Ctrl+C` / `Ctrl+V` while hovering copies or pastes the value.
* Optional inputs can be hidden while they are unconnected (**Hide / Show Unused Sockets**, `Ctrl+H`); nodes with an *Advanced* panel fold rarely-used inputs into it.
* Drag the node's **right edge** to change its width; **Reset Node Width** in the View menu restores the default.

## Sockets and wires

Socket colour names the kind of data. With **Type letters in sockets** turned on (Settings ▸ Appearance) a letter is drawn inside each socket too, so the kind never depends on colour alone. The socket shape shows the structure: a **circle** is a single value, a **rounded square** a list, a **square with a hole** a list of lists, and a **diamond** a value whose structure is not known until it runs.

| Kind | Colour | Letter |
|---|---|---|
| Any | `#B4BBC4` | — |
| Number | `#56B4E9` | `N` |
| Integer | `#2E82D8` | `I` |
| Boolean | `#D55E00` | `B` |
| Text | `#F0E442` | `T` |
| DateTime | `#CC79A7` | `D` |
| Colour | `#E8E8E8` | `C` |
| Geometry | `#009E73` | `G` |
| Item | `#E69F00` | `E` |
| Selection | `#9C7229` | `S` |
| Viewpoint | `#7B68EE` | `V` |
| Clash | `#E0503F` | `X` |
| Document | `#6B7785` | `F` |
| Data | `#8FBC8F` | `{` |
| File | `#A9865B` | `P` |
| Action | `#F47AA5` | `A` |

* A **pill** — an elongated socket — is a **multi-input**: connect as many wires to it as you like. With one wire it behaves exactly like an ordinary input; with several, the node receives everything they carry combined into one list, in the order the wires were made (wires carrying lists contribute their elements). The wires fan out along the pill, one landing point each, and the node's row grows to fit them. Drag from the pill to take off the wire under the pointer; drop a picked-up wire back on the pill at another slot to reorder, or use **Move Wire Earlier / Later** on a selected wire. Any input marked `[MultiInput]` (see the authoring guide) is drawn this way — for example the item lists of the Appearance, Selection and Export nodes, and **List.Merge**.
* A **dashed wire** feeds a list into a single-value input: the node runs once per item (replication).
* **Mute and Freeze** both stop a node from running, but they differ in what happens around it. A **muted** node (`M`, badge *MUTED*) is bypassed: each output passes the first input of a matching type straight through, the node counts as done, and everything downstream still runs on that passed-through data. A **frozen** node (`Shift+M`, badge *FROZEN*) is held: it **and everything downstream of it** are skipped by runs and keep the results from their last run, so a slow branch is not recomputed while you work elsewhere. Use *mute* to switch a step off inside a live chain (a filter, a recolour); use *freeze* to stop a whole branch from recomputing. Both are undoable and saved with the graph. A muted *wire* is different again: only that connection is ignored, and the input falls back to its own value.
* A **muted wire** is ignored by the run — a quick way to switch a branch off without deleting it (`Ctrl` held while cutting mutes instead of deleting).
* Dropping a wire on empty canvas opens the node search filtered to nodes that can accept it; picking one connects it.

## Node groups

A **node group** is a reusable piece of graph — a few nodes that do one job, with their own inputs and outputs — stored in the file. Use it as often as you like; edit it once.

* **Make one:** select nodes and choose *Node Groups ▸ Make Node Group* (`Ctrl+Alt+G`). The wires that crossed the selection become the group's inputs and outputs, and an **instance** takes the selection's place, so the graph computes exactly what it did.
* **Open one:** select an instance and press `Tab` (or click the arrow in its header). The canvas shows the group's nodes between a **Group Input** and a **Group Output** node, and the bar above the canvas shows where you are; `Shift+Tab` (or *Close group*) goes back. Running while a group is open still runs the whole graph, so an edit shows its effect at once. Each level keeps its own Undo history.
* **Edit the interface:** inside a group, *+ Add input* / *+ Add output* on the Group Input / Group Output node adds a socket, and right-clicking a socket renames, retypes, moves or removes it (wires on a removed socket go too, everywhere the group is used, and come back on undo). Sockets pass whole values, so a list travels through as one list. Giving a socket a type colours it and, on instances, gives it an inline editor.
* **Instances share the group:** editing the group changes every instance. *Make Node Group Single User* gives one instance its own copy; *Ungroup* (`Ctrl+Alt+U`) puts a copy of the group's nodes in place of an instance. The group stays in the file until you use *Delete Unused Node Groups*.
* Groups can hold other groups but never themselves. The library lists the file's groups under **Node Groups**, so an instance is added like any node; copying an instance into another file brings its group along. Older versions of Dyncamelo refuse a file with node groups rather than silently dropping them.
* The progress text names the group path (`Outer ▸ Inner ▸ node`), and `Esc` cancels from inside a group too.

## Running and stopping

* **Run** (`F5`) executes the nodes that changed since the last run and reuses the rest; **Auto** runs again after every edit.
* While a graph runs, the window says which node is working (`12 / 40 — name`, with the group path inside a node group). **Press `Esc` to stop it** — switchable in Settings ▸ Editing ▸ *Esc cancels a running graph*.
* The run halts **before the next node**, or between the items of a node that is working through a list, or between the passes of a loop. A single Navisworks call already under way cannot be interrupted, and what finished nodes already changed in Navisworks is kept. A node or loop that was cut short keeps its previous results and waits, together with everything after it, so the next **Run** carries on where this one stopped.

## Keeping your work safe

* A `*` after the graph name in the header means there are **unsaved changes**. Saving removes it; undoing back to the saved state keeps it, to be on the safe side.
* **New**, **Open**, opening a sample, opening a recent file, and dropping a `.dyc` file on the canvas first ask *Save / Don't Save / Cancel* when something is unsaved. A cancelled or failed save cancels the whole action, so nothing is thrown away by accident.
* **Autosave** (Settings ▸ Editing) keeps a copy of a graph with unsaved changes once a minute in `%APPDATA%\Dyncamelo\recovery`, and again when the pane is closed. If Navisworks closed or crashed before you saved, Dyncamelo offers that copy back the next time the editor opens on an empty canvas; saving, or answering *No*, deletes it. A restored graph counts as unsaved until you save it.
* Drag a `.dyc` file from Explorer onto the canvas to open it.

## Finding your way

* **Problems.** The *Warnings* and *Errors* counts in the status bar are buttons: click them (or press `Ctrl+Shift+E`) to list every node that failed or warned in the last run, errors first. Click a row to select that node and bring it into view. `F8` and `Shift+F8` step through them from wherever you are, and the status bar says which one you are on.
* **Why didn't this run?** Select a node and press `I`: the status bar explains in words whether the node is frozen, sits after a frozen node, is muted, failed (and with what), is waiting for an input, or is just waiting for the next Run — naming the node responsible when it is another one.
* **Run up to a node.** `Shift+F5` runs only the selected nodes and what they depend on, then stops. Everything after them keeps its previous results and stays pending, so the next ordinary **Run** finishes the job. Useful while building a graph whose later steps are slow. Inside a node group it runs up to the group instance.
* **Undo history.** `Ctrl+Shift+H` lists every step of the current graph (or the node group you are editing). Click a step to go back or forward to exactly that point; steps you undid are shown dimmed until you make a new edit.
* **Bookmarks.** `Ctrl+K` names the current view (position and zoom) and saves it **in the graph file**; `Ctrl+Shift+K` lists them — click one to go there, the ✕ removes it. Good for touring a large graph (*inputs*, *filters*, *export*).
* **Find a node by name.** `Ctrl+F` opens the command palette on the nodes of this canvas (it starts with `@`; typing plain text in the palette finds nodes too). `Enter` selects the node and scrolls to it.
* **Frame and arrows.** `Shift+F` zooms to the selection. The arrow keys (`Left`, `Right`, `Up`, `Down`) move the selection to the nearest node in that direction, so a graph can be toured without the mouse; they only act when the canvas has the keyboard.
* **Socket tooltips.** Hover a socket to see its type and, after a run, the **value it holds** — for a list the number of items and the first few — or, on a wired input, what arrives on the wire.
* **Hints.** The status bar shows a hint line that follows what you are doing (the keys for the selected nodes, what releasing a dragged wire will do), and an empty canvas lists the ways to add the first node. Both can be switched off in Settings ▸ Appearance, which also has **Window scale** (90–150%) for high-resolution screens or a small pane.

## Commands and shortcuts

Default shortcuts. **Canvas** commands only act when the canvas has the keyboard (so typing in a box never triggers them); **Everywhere** commands also work while a text box has focus.

### File

| Command | Shortcut | Works |
|---|---|---|
| New | `Ctrl+N` | Everywhere |
| Open… | `Ctrl+O` | Everywhere |
| Save | `Ctrl+S` | Everywhere |
| Save As… | `Ctrl+Shift+S` | Everywhere |

### Edit

| Command | Shortcut | Works |
|---|---|---|
| Undo | `Ctrl+Z` | Canvas |
| Redo | `Ctrl+Y` or `Ctrl+Shift+Z` | Canvas |
| Cut | `Ctrl+X` | Canvas |
| Copy | `Ctrl+C` | Canvas |
| Paste | `Ctrl+V` | Canvas |
| Duplicate | `Ctrl+D` | Canvas |
| Delete | `Delete` | Canvas |
| Select All | `Ctrl+A` | Canvas |
| Delete and Reconnect | `Ctrl+Delete` | Canvas |
| Select Downstream | `L` | Canvas |
| Select Upstream | `Shift+L` | Canvas |
| Select Similar | `Shift+G` | Canvas |
| Select Node to the Left | `Left` | Canvas |
| Select Node to the Right | `Right` | Canvas |
| Select Node Above | `Up` | Canvas |
| Select Node Below | `Down` | Canvas |
| Undo History… | `Ctrl+Shift+H` | Canvas |

### View

| Command | Shortcut | Works |
|---|---|---|
| Fit to Screen | — | Canvas |
| Zoom In | — | Canvas |
| Zoom Out | — | Canvas |
| Minimap *(toggle)* | `Ctrl+M` | Canvas |
| Node Library Panel *(toggle)* | `Ctrl+B` | Canvas |
| Frame Selected | `Shift+F` | Canvas |
| Problems List | `Ctrl+Shift+E` | Canvas |
| Canvas Bookmarks… | `Ctrl+Shift+K` | Canvas |
| Bookmark This View… | `Ctrl+K` | Canvas |
| Reset Node Width | — | Canvas |
| Collapse All Nodes | — | Canvas |
| Expand All Nodes | — | Canvas |
| Node Value Previews *(toggle)* | — | Canvas |
| Settings… | — | Canvas |
| Performance HUD *(toggle)* | `Ctrl+Shift+F12` | Everywhere |

### Graph

| Command | Shortcut | Works |
|---|---|---|
| Run | `F5` | Everywhere |
| Run Up to Selected Node | `Shift+F5` | Everywhere |
| Go to Next Problem | `F8` | Everywhere |
| Go to Previous Problem | `Shift+F8` | Everywhere |
| Find Node on Canvas… | `Ctrl+F` | Canvas |
| Auto-Run *(toggle)* | — | Canvas |
| Rename Graph | `F2` | Canvas |
| Add Note | — | Canvas |
| Group Selection | `Ctrl+G` | Canvas |
| Fit Frame to Contents | `Ctrl+Shift+G` | Canvas |
| Ungroup | `Ctrl+Shift+U` | Canvas |
| Arrange Selection | `Ctrl+L` | Canvas |
| Arrange All | `Ctrl+Shift+L` | Canvas |
| Add Node… | `Space` | Canvas |

### Node

| Command | Shortcut | Works |
|---|---|---|
| Collapse / Expand | `H` | Canvas |
| Hide / Show Unused Sockets | `Ctrl+H` | Canvas |
| Mute / Unmute | `M` | Canvas |
| Freeze / Unfreeze | `Shift+M` | Canvas |
| Why Didn't This Run? | `I` | Canvas |
| Reset Inputs to Default | — | Canvas |
| Insert Into Selected Wire | — | Canvas |
| Connect Selected Nodes | `F` | Canvas |

### Node Groups

| Command | Shortcut | Works |
|---|---|---|
| Make Node Group | `Ctrl+Alt+G` | Canvas |
| Ungroup Node Group | `Ctrl+Alt+U` | Canvas |
| Open / Close Node Group | `Tab` | Canvas |
| Close Node Group | `Shift+Tab` | Canvas |
| Rename Node Group… | — | Canvas |
| Make Node Group Single User | — | Canvas |
| Add Group Input Socket | — | Canvas |
| Add Group Output Socket | — | Canvas |
| Delete Unused Node Groups | — | Canvas |

### Wires

| Command | Shortcut | Works |
|---|---|---|
| Mute / Unmute Selected Wires | — | Canvas |
| Swap Links | — | Canvas |
| Move Wire Earlier | — | Canvas |
| Move Wire Later | — | Canvas |
| Add Reroute to Selected Wires | — | Canvas |
| Disconnect Selected Wires | — | Canvas |

### Help

| Command | Shortcut | Works |
|---|---|---|
| Command Palette… | `Ctrl+Shift+P` | Everywhere |
| UI Guide (online) | — | Canvas |
| Keyboard & Mouse Shortcuts | `F1` | Everywhere |

## Mouse and gestures

| Gesture | How |
|---|---|
| Stop a running graph | Esc (the run halts before the next node and continues from there next time) |
| Open a graph | Drag a .dyc file from Explorer onto the canvas |
| Pan the canvas | Right or middle mouse drag |
| Zoom | Mouse wheel |
| Search for a node here | Space over the canvas |
| Double-click empty canvas | Does what Settings ▸ Editing says (a String node by default) |
| Connect a socket to a new node | Drag a wire onto empty canvas |
| Insert a node into a wire | Drag the node onto the wire |
| Add a reroute | Double-click a wire |
| Connect many wires to one socket | A pill-shaped socket takes any number of wires |
| Take one wire off a pill | Drag from the pill; the wire under the pointer comes off |
| Reorder the wires of a pill | Drop a picked-up wire back on the pill at the slot you want |
| Move a link to another input | Drag from a connected input |
| Swap two links | Drop a picked-up link on an occupied input with Shift held |
| Cut wires | Alt+Shift+drag across them |
| Mute the wires you cut | Hold Ctrl while releasing the cut |
| Push nodes apart | Ctrl+Shift+drag |
| Change a number | Drag the field; Shift for fine steps, Ctrl to snap |
| Copy or paste a field's value | Hover it, then Ctrl+C / Ctrl+V |
| Reset a field to its default | Hover it, then Backspace |
| Rename a node | Double-click its title |
| Resize a node | Drag its right edge |

## Settings

Open with the gear in the toolbar, **View ▸ Settings…**, or the palette. Every setting can be found with the search box on the page; the ↺ button puts a changed setting back to its default.

### Appearance

The **colour palette** of the whole editor is chosen here too.

| Setting | Values | Default | What it does |
|---|---|---|---|
| Node density | Compact / Normal / Comfortable | Normal | Row height of nodes. Compact fits more on screen, comfortable is easier to click. |
| Type letters in sockets | On / Off | Off | Draw a short letter naming the type inside every socket, so types do not rely on colour alone. |
| Node library panel | On / Off | On | Show the node library on the left of the canvas. It can also be hidden with the arrow in its header and brought back with the tab at the canvas edge. |
| Descriptions in the library | On / Off | On | Show a description line under each node in the library panel. |
| Value previews under nodes | On / Off | On | Show a preview bubble with the result under each node after a run. |
| Window scale | 90% / 100% / 110% / 125% / 150% | 100% | Make everything in the Dyncamelo window smaller or larger, for high-resolution screens or a small pane. |
| Hints in the status bar | On / Off | On | Show a line of suggestions at the bottom that follows what you are doing: the keys for the selected nodes, what a dragged wire will do. |
| Hints on an empty canvas | On / Off | On | Show the ways to add a first node while the canvas is empty. |

### Canvas

| Setting | Values | Default | What it does |
|---|---|---|---|
| Grid lines | On / Off | On | Draw the grid on the canvas background. |
| Snap nodes to the grid | On / Off | On | Dragged nodes land on grid lines instead of anywhere. |
| Straight wires | On / Off | Off | Draw wires as straight lines instead of curves. Faster on very large graphs. |
| Minimap | Automatic / Always / Never | Automatic | The overview in the corner. Automatic shows it once the graph has 40 or more nodes. |
| Layered Arrange | On / Off | On | Arrange with the layered layout that keeps wire crossings to a minimum. Off uses simple columns. |

### Editing

| Setting | Values | Default | What it does |
|---|---|---|---|
| Number drag speed | Slow / Normal / Fast | Normal | How far the mouse travels for each step of a number field. |
| Run while dragging numbers | On / Off | Off | Re-run the graph continuously while a number field is dragged. Off runs once on release, which is safer for graphs that call Navisworks. |
| Hide unused inputs by default | On / Off | Off | Nodes that have not been set either way hide their unconnected optional inputs. |
| Make room when inserting on a wire | On / Off | On | Nodes downstream move right when a node is dropped onto a wire. |
| Deleting a reroute keeps the wire | On / Off | On | Removing a reroute dot joins the wire back up instead of deleting it. |
| Esc cancels a running graph | On / Off | On | Pressing Esc while a run is in progress stops it before the next node. The node in progress finishes (a Navisworks call cannot be interrupted) and the run continues from there next time. |
| Autosave unsaved work | On / Off | On | Keep a copy of a graph with unsaved changes once a minute, and offer it back if Navisworks closed or crashed before you saved. The copy is deleted when you save. |
| Highlight selected node in Navisworks | On / Off | Off | Select the model items a node outputs in the viewport when the node is clicked. Overwrites the live selection, so turn it off if you use Selection.Current. |
| Double-click empty canvas | Insert a String node / Insert a Number node / Add a note / Do nothing | Insert a String node | What double-clicking the empty canvas does. |

### Shortcuts

The table of every command with its shortcut; see [Changing shortcuts](#changing-shortcuts).

### Diagnostics

Buttons for the performance HUD, the keyboard help and this guide, and **Reset all settings**, which restores every preference and shortcut to its default (favourites and recent files are kept).

## Changing shortcuts

1. Open **Settings ▸ Shortcuts** (or search the palette for *shortcut*).
2. Press **Change** on a command, then press the keys you want. `Esc` cancels; `Backspace` removes the shortcut.
3. If the keys are already used, the row says by which command and keeps listening — nothing is taken away silently.
4. Commands that also work while a text box has focus (`Everywhere` above) must use `Ctrl`, `Alt` or a function key, because plain letters are typed text.
5. ↺ on a row restores its default; **Reset all shortcuts** restores every one.

The menus, the key handling, the `F1` help and the palette all read the same keymap, so a change shows up everywhere at once and is kept between sessions.
