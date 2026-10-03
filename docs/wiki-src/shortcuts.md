# Keyboard and mouse reference

Every keyboard shortcut and mouse gesture of the Dyncamelo editor, grouped by area.

The shortcuts shown here are the **defaults**. You can change any of them in **Settings ▸ Shortcuts** (see [Settings](settings.md#shortcuts)), and the in-app sheet that opens with `F1` always shows the keys currently in force. Every command can also be reached from a menu and from the command palette (`Ctrl+Shift+P`), so you never have to remember a key.

## How to read the tables

* **Canvas** in the *Works* column means the shortcut acts only while the canvas has the keyboard, so typing in a text box never triggers it.
* **Everywhere** means the shortcut also works while a text box has the focus.
* *(toggle)* marks a command that switches something on and off.
* A dash means the command has no shortcut by default. It is still in its menu and the palette, and you can give it one.
* Keys work when the Dyncamelo pane has the keyboard focus (click the canvas first). While it does, Dyncamelo's keys take priority over Navisworks's own for the same keys.

## File

| Command | Shortcut | Works |
|---|---|---|
| New | `Ctrl+N` | Everywhere |
| Open… | `Ctrl+O` | Everywhere |
| Save | `Ctrl+S` | Everywhere |
| Save As… | `Ctrl+Shift+S` | Everywhere |

The File menu also holds **Recent Files** and **Sample Graphs** (see [Saving and opening scripts](saving-opening.md)).

## Edit

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

If you change the shortcut of **Redo**, the built-in second shortcut (`Ctrl+Shift+Z`) goes away and only your key is used.

## View

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
| Open Script Player | — | Canvas |

`Home` also fits the whole script into view. It is the canvas's own key rather than a command in the list above, so it does not appear in Settings ▸ Shortcuts.

## Graph

| Command | Shortcut | Works |
|---|---|---|
| Run | `F5` | Everywhere |
| Run Up to Selected Node | `Shift+F5` | Everywhere |
| Go to Next Problem | `F8` | Everywhere |
| Go to Previous Problem | `Shift+F8` | Everywhere |
| Find Node on Canvas… | `Ctrl+F` | Canvas |
| Auto-Run *(toggle)* | — | Canvas |
| Rename Graph | `F2` | Canvas |
| Script Description… | — | Canvas |
| Add Note | — | Canvas |
| Group Selection | `Ctrl+G` | Canvas |
| Fit Frame to Contents | `Ctrl+Shift+G` | Canvas |
| Ungroup | `Ctrl+Shift+U` | Canvas |
| Arrange Selection | `Ctrl+L` | Canvas |
| Arrange All | `Ctrl+Shift+L` | Canvas |
| Add Node… | `Space` | Canvas |

**Group Selection**, **Fit Frame to Contents** and **Ungroup** work on *frames*, the coloured rectangles behind nodes. For reusable node groups, see the Node Groups table below.

## Node

| Command | Shortcut | Works |
|---|---|---|
| Collapse / Expand | `H` | Canvas |
| Hide / Show Unused Sockets | `Ctrl+H` | Canvas |
| Mute / Unmute | `M` | Canvas |
| Freeze / Unfreeze | `Shift+M` | Canvas |
| Why Didn't This Run? | `I` | Canvas |
| Show / Hide in Player | `Ctrl+Alt+P` | Canvas |
| Show / Hide Unwired Inputs in Player | — | Canvas |
| Reset Inputs to Default | — | Canvas |
| Insert Into Selected Wire | — | Canvas |
| Connect Selected Nodes | `F` | Canvas |

## Node Groups

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

See [Node groups](node-groups.md).

## Wires

| Command | Shortcut | Works |
|---|---|---|
| Mute / Unmute Selected Wires | — | Canvas |
| Swap Links | — | Canvas |
| Move Wire Earlier | — | Canvas |
| Move Wire Later | — | Canvas |
| Add Reroute to Selected Wires | — | Canvas |
| Disconnect Selected Wires | — | Canvas |

## Help

| Command | Shortcut | Works |
|---|---|---|
| Command Palette… | `Ctrl+Shift+P` | Everywhere |
| UI Guide (online) | — | Canvas |
| BIMCamel Website | — | Canvas |
| Get the Newest Version… | — | Canvas |
| Keyboard & Mouse Shortcuts | `F1` | Everywhere |
| Copy Diagnostics | — | Canvas |
| Run Self-Test… | — | Canvas |
| Privacy Policy | — | Canvas |

## Mouse and gestures

### Moving around

| Gesture | How |
|---|---|
| Pan the canvas | Right or middle mouse drag |
| Zoom | Mouse wheel |
| Search for a node here | `Space` over the canvas |
| Double-click empty canvas | Does what Settings ▸ Editing says (a String node by default) |
| Open a script | Drag a `.dyc` file from Explorer onto the canvas |
| Stop a running script | `Esc` (the run halts before the next node and continues from there next time) |

### Nodes and number fields

| Gesture | How |
|---|---|
| Rename a node | Double-click its title |
| Resize a node | Drag its right edge |
| Change a number | Drag the field; `Shift` for fine steps, `Ctrl` to snap; the pointer wraps at the screen edge |
| Copy or paste a field's value | Hover it, then `Ctrl+C` / `Ctrl+V` |
| Paste a coordinate | Hover a number field, `Ctrl+V` with "1, 2, 3" on the clipboard fills it and the fields after it |
| Reset a field to its default | Hover it, then `Backspace` |
| Pick a colour from the screen | The dropper in a colour popup; click anywhere, `Esc` cancels |
| Push nodes apart | `Ctrl+Shift`+drag |
| Offer an input in the Script Player | Right-click its socket ▸ Show in Player (a ▶ badge marks nodes the Player uses) |

### Wires

| Gesture | How |
|---|---|
| Connect a socket to a new node | Drag a wire onto empty canvas |
| Insert a node into a wire | Drag the node onto the wire |
| Add a reroute | Double-click a wire |
| Connect many wires to one socket | A pill-shaped socket takes any number of wires |
| Take one wire off a pill | Drag from the pill; the wire under the pointer comes off |
| Reorder the wires of a pill | Drop a picked-up wire back on the pill at the slot you want |
| Move a link to another input | Drag from a connected input |
| Swap two links | Drop a picked-up link on an occupied input with `Shift` held |
| Cut wires | `Alt+Shift`+drag across them |
| Mute the wires you cut | Hold `Ctrl` while releasing the cut |
| Add a node group socket | Drop a wire on the Group Input or Group Output node itself |

## Keys inside lists, boxes and panels

These belong to a particular box or pane and are not in the command list.

| Where | Key | What it does |
|---|---|---|
| Quick node search (`Space`) | `Up` / `Down` | Move through the results |
| | `Enter` | Insert the highlighted node |
| | `Esc` | Close without adding anything |
| Command palette (`Ctrl+Shift+P`) | `Up` / `Down` | Move through the entries |
| | `Enter` | Run the highlighted entry |
| | `Esc` | Close the palette |
| | `@` at the start | List only the nodes on your canvas |
| Node library search box | `Esc` | Clear the search; a second press clears the highlight |
| Settings page, keyboard help sheet | `Esc` | Close it |
| Settings ▸ Shortcuts, after pressing **Change** | any shortcut | Records it |
| | `Esc` | Cancel |
| | `Backspace` | Remove the shortcut |
| Script Player list | `Down` | Move from the search box into the list |
| | `Enter` | Choose the script (then `Enter` again runs it) |
| | `Esc` | Fold the list |
| Script Player, while a script runs | `Esc` | Stop the run |

## Tip: finding a key quickly

Open the command palette with `Ctrl+Shift+P`, type a word of the command's name, and the palette lists the matching commands with their shortcuts at the right. To see everything at once, press `F1`.

Related: [The editor: canvas and nodes](canvas-and-nodes.md), [Node library and search](library-and-search.md), [Settings](settings.md).
