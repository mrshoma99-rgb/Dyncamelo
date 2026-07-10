# Getting Started with Dyncamelo

This guide takes you from a fresh Navisworks installation to your first working graph: **find every item whose Material contains "Concrete", color it red, and save it as a selection set** — without writing a line of code.

> **What's new in 0.2** — copy/paste and duplicate nodes (`Ctrl+C`/`Ctrl+V`/`Ctrl+D`), group nodes into colored frames (`Ctrl+G`), value previews under every node, library favourites (star any node) and tooltips, a recent-files menu on **Open**, resizable Watch nodes — and the node library has grown past **200 nodes** (clash triage and reports, camera control, bulk search sets, model audits, and 43 new general-purpose nodes).

## 1. What you need

- Autodesk **Navisworks Manage or Simulate 2024** (Windows).
- The Dyncamelo plugin files — either a [release zip](https://github.com/mrshoma99-rgb/dyncamelo/releases) or your own build (see the [README](../README.md#quick-start)).
- Any model to play with (`.nwd`, `.nwf`, or an appended `.rvt`/`.ifc`/`.dwg`).

## 2. Install

### Option A — application bundle (recommended: own ribbon tab)

1. Close Navisworks.
2. Take the `dist\Dyncamelo.bundle\` folder from this repository, drop the built
   DLLs into its `2024\` subfolder (see `2024\PLACE_DYNCAMELO_DLLS_HERE.txt`;
   a Debug build of the solution deploys the whole bundle for you), and copy
   the folder to:

   ```
   %APPDATA%\Autodesk\ApplicationPlugins\Dyncamelo.bundle\
   ```

   (Per user, no admin rights needed. Use `C:\ProgramData\Autodesk\ApplicationPlugins\` for all users.)
3. Start Navisworks 2024 — Dyncamelo appears on the **BIMCamel** ribbon tab,
   in the *Visual Programming* panel.

   > If Navisworks reports `PLUGIN_LOAD_02` / `0x80131515`, the DLLs still
   > carry Windows' "downloaded file" mark. The release installer
   > (`install-dyncamelo.bat`) unblocks automatically; for a manual copy run
   > `Get-ChildItem "$env:APPDATA\Autodesk\ApplicationPlugins\Dyncamelo.bundle" -Recurse -File | Unblock-File`
   > in PowerShell and restart Navisworks.

### Option B — classic Plugins folder (no ribbon tab)

1. Close Navisworks.
2. Copy the Dyncamelo files into a plugin folder **named exactly like the plugin DLL**:

   ```
   C:\Program Files\Autodesk\Navisworks Manage 2024\Plugins\Dyncamelo.App\
       Dyncamelo.App.dll
       Dyncamelo.UI.dll
       Dyncamelo.Navisworks.dll
       Dyncamelo.Nodes.dll
       Dyncamelo.Core.dll
       Nodify.dll
       Newtonsoft.Json.dll
       en-US\Dyncamelo.xaml
       Resources\*.png
   ```

   (For Navisworks Simulate, replace `Navisworks Manage 2024` accordingly. Creating the folder requires administrator rights. The `LayoutNavisworksPlugin` build target assembles this exact folder under `bin\<Configuration>\net48\NavisworksPlugin\`.)
3. If Windows blocked the downloaded files, unblock them: right-click each DLL → Properties → check *Unblock* (or run `Unblock-File *` in PowerShell in that folder).
4. Start Navisworks 2024.

## 3. Open the Dyncamelo pane

Open a model first (Dyncamelo works against the active document), then go to the **BIMCamel** ribbon tab and click **Dyncamelo** (with the classic Plugins-folder install the button also appears under **Tool add-ins**). The editor opens as a dockable pane — dock it, float it, or drop it on a second monitor; Navisworks remembers the placement.

The pane has three areas:

- **Library** (left): all loaded nodes in a category tree, with a search box. Double-click or drag a node onto the canvas.
- **Canvas** (center): your graph. Pan with the middle mouse button, zoom with the wheel, box-select with the left button.
- **Run bar** (bottom): the **Run** button, the Manual/Automatic mode toggle, and the status/log strip.

Every node shows its inputs on the left edge and outputs on the right. Drag from an output port to an input port to make a wire — that wire is the data flow.

## 4. Your first graph: color all concrete red

We will build this chain:

```
String ("Concrete") ──▶ Search.ByPropertyContains ──▶ Appearance.OverrideColor ──▶ SelectionSet.Create
                                                        ▲
                                     Color Picker (red) ┘
```

### Step 1 — find the right property names

In Navisworks, click a concrete element and look at the **Properties** window. Find the tab (category) and row (property) that holds the material text — for Revit-sourced models this is typically category **Element**, property **Material**; other formats often use category **Item**, property **Material**. Note the two display names you see; Dyncamelo searches by exactly these names.

### Step 2 — the search

1. In the library, search for **String** (under *Input*) and add **three** String nodes. Type into them:
   - first: `Element` (or whatever category you found),
   - second: `Material`,
   - third: `Concrete`.
2. Add **Search.ByPropertyContains** (under *Navisworks → Search*).
3. Wire the three strings to its `category`, `property`, and `value` inputs.
4. Leave the `document` input unconnected — Document ports default to the active document automatically.
5. Add a **Watch List** node (under *Output*), wire `modelItems` into it, and press **Run**.

The Watch List fills with every matching item. If it is empty, re-check the category/property names against the Properties window (they must match what Navisworks displays, including language).

### Step 3 — color the results

1. Add **Color Picker** (under *Color*) and pick red.
2. Add **Appearance.OverrideColor** (under *Navisworks → Appearance*).
3. Wire `Search.ByPropertyContains → modelItems` into `modelItems`, and the Color Picker into `color`.
4. Press **Run** — every concrete item in the viewport turns red.

This is a real Navisworks color override, exactly like *Item Tools → Override Color*, and one **Undo** in Navisworks reverts the whole run. To clear overrides from the graph instead, use **Appearance.Reset** (or **Appearance.ResetAll** for a clean slate before re-coloring).

### Step 4 — save the selection set

1. Add a **String** node with the text `Concrete elements`, and **SelectionSet.Create** (under *Navisworks → SelectionSets*).
2. Wire `Appearance.OverrideColor → modelItems` (write nodes pass their items through precisely so you can chain like this) into `modelItems`, and the name string into `name`.
3. Run. Check Navisworks' **Sets** window — your set is there, ready for use in clash tests, TimeLiner, or manual work. `overwrite` defaults to true, so re-running updates the same set instead of duplicating it.

### Step 5 — make it live

Flip the run bar from **Manual** to **Automatic**. Now edit the search String from `Concrete` to `Steel` — the graph re-runs by itself and recolors accordingly. Only the nodes *downstream of your edit* re-execute; everything else serves cached results, which is what keeps big graphs fast.

## 5. Understanding lacing (working with lists)

Most Dyncamelo power comes from feeding **lists** into inputs that expect a **single value** — the node then runs once per item automatically. This is called *replication*, and the pairing rule when several list inputs meet is called **lacing** (Dynamo users: it is the same concept).

Example — turn the single-set graph above into a set-per-system factory:

1. Replace the value String with **String.Split** fed by a String containing `Concrete,Steel,Masonry` (separator `,`), so `Search.ByPropertyContains` receives a **list** of three values → it runs three times → outputs a list of three item-lists.
2. Feed `SelectionSet.Create` the same three texts as `name` and the search output as `modelItems` → three selection sets are created in one run.

When a node receives lists on more than one input, its **lacing** setting (right-click the node) pairs them:

| Lacing | Behavior | `[1,2,3]` + `[10,20]` |
|---|---|---|
| **Shortest** (default) | Pair index-by-index, stop at the shorter list | `[11, 22]` |
| **Longest** | Pair index-by-index, reuse the last item of the shorter list | `[11, 22, 23]` |
| **Cross-Product** | Every combination (nested result) | `[[11,21,31],[12,22,32]]` |

Rules of thumb: parallel lists that belong together (names + item-lists) → **Shortest**; one list against one constant "list of one" → **Longest**; "try everything against everything" (e.g. all colors × all searches) → **Cross-Product**. A small badge on the node shows non-default lacing.

Inputs that already *expect* a list (like `CSV.WriteToFile → rows`) absorb the list whole instead of replicating — port tooltips tell you which kind you are looking at.

## 6. Saving and loading graphs

- **Save** (toolbar or Ctrl+S) writes a `.dyc` file — a small, versioned JSON document. It stores nodes, wires, your input values, notes, and lacing settings; it does **not** store results, so a loaded graph recomputes fresh on its first run.
- **Open** loads any `.dyc`; the graph reconnects to whatever document is currently active, so one graph serves every project. File paths picked with *File Path* nodes are stored relative to the graph when possible, which keeps graphs portable across machines.
- `.dyc` files are plain text: they diff cleanly, belong in source control, and attach nicely to issues. If you open a graph using nodes you do not have (from a newer version or a node pack), those nodes appear as placeholders and are preserved on save — nothing is lost.

## 7. When something goes wrong

Nodes never crash a run — they report on themselves, per node:

| Badge | State | Meaning |
|---|---|---|
| none / gray | Idle | Not executed yet (or missing a required input) |
| green | Executed | Ran fine; hover outputs to peek at values |
| yellow | Warning | Ran with a recoverable issue — e.g. a property missing on 12 of 500 items (those results are null); click the badge for details |
| red | Error | The node failed; click the badge for the full message. Downstream nodes wait until it is fixed |

Quick fixes for common cases:

- **Search returns nothing** — category/property names must match the Properties window exactly (localized names included). Try `Search.ByPropertyContains` before `Search.ByPropertyValue`, and verify with a Watch List.
- **`Properties.Value` warns "property not found"** — not all items carry all properties; filter first (e.g. `ModelItem.HasGeometry → List.FilterByBoolMask`) or accept the nulls.
- **Everything is Idle after loading** — that is normal; press Run once.
- **A run takes long** — press the cancel button in the run bar; already-computed nodes keep their results and the next run resumes where it stopped. Right-click any node and **Freeze** it to exclude an expensive branch (it and its downstream ghost out) while you work on the rest.

## 8. Where to go next

- Browse the [Node Library catalog](NODE_LIBRARY.md) — including seven reference workflows (QTO to CSV, bulk sets, clash triage, viewpoint batches, model QA) with their node chains spelled out.
- Learn the engine's exact behavior in [ARCHITECTURE.md](ARCHITECTURE.md).
- Write your own nodes — it is one C# method: [EXTENDING.md](EXTENDING.md).
- Sample graphs live in the repository's `samples/` folder (growing every release).
