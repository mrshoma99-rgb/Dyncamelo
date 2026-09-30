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

* The **header** carries the node's name (double-click to rename) and its state colour; `H` collapses the node to its header.
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

* A **dashed wire** feeds a list into a single-value input: the node runs once per item (replication).
* A **muted wire** is ignored by the run — a quick way to switch a branch off without deleting it (`Ctrl` held while cutting mutes instead of deleting).
* Dropping a wire on empty canvas opens the node search filtered to nodes that can accept it; picking one connects it.

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

### View

| Command | Shortcut | Works |
|---|---|---|
| Fit to Screen | — | Canvas |
| Zoom In | — | Canvas |
| Zoom Out | — | Canvas |
| Minimap *(toggle)* | `Ctrl+M` | Canvas |
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
| Reset Inputs to Default | — | Canvas |
| Insert Into Selected Wire | — | Canvas |
| Connect Selected Nodes | `F` | Canvas |

### Wires

| Command | Shortcut | Works |
|---|---|---|
| Mute / Unmute Selected Wires | — | Canvas |
| Swap Links | — | Canvas |
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
| Pan the canvas | Right or middle mouse drag |
| Zoom | Mouse wheel |
| Search for a node here | Space over the canvas |
| Double-click empty canvas | Does what Settings ▸ Editing says (a String node by default) |
| Connect a socket to a new node | Drag a wire onto empty canvas |
| Insert a node into a wire | Drag the node onto the wire |
| Add a reroute | Double-click a wire |
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
| Descriptions in the library | On / Off | On | Show a description line under each node in the library panel. |
| Value previews under nodes | On / Off | On | Show a preview bubble with the result under each node after a run. |

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
