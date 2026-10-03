# Your first script

In about ten minutes you will build a real graph: **find every item whose Material contains "Concrete", colour it red, and save it as a selection set**. About six nodes, no code.

You need Dyncamelo [installed](installation.md) and a model open in Navisworks (a Revit, IFC or DWG model appended to a new `.nwd` is fine).

## 1. Open the editor and meet the screen

On the **BIMCamel** ribbon tab, click **Dyncamelo**. The editor opens as a pane. Its parts:

* **Library** (left): every node, in a category tree, with a search box. Double-click a node, or drag it onto the canvas.
* **Canvas** (middle): your graph. Pan by dragging with the right or middle mouse button, zoom with the wheel, box-select with the left button.
* **Run bar** (bottom): the **Run** button, the **Manual / Auto** switch and the status line.

While the canvas is empty the **start screen** shows cards: **New script**, your recent scripts and the examples. It disappears when you add a node or open a script.

Every node shows its **inputs on the left** and its **outputs on the right**. Drag from an output socket to an input socket to make a **wire**. The wire is the data flow.

Three habits that save time from day one:

* Press **Space** over the canvas to search for a node right where the cursor is. Type part of the name, `↑`/`↓` to choose, ++enter++ to insert, ++esc++ to close.
* **Hover a socket** to see what it expects: its name, its [kind](ports-and-kinds.md), whether it is required, its default and a description.
* Press **++ctrl+shift+p++** for the command palette: type a word of any command or setting.

## 2. Find the names in Navisworks

Click a concrete element in Navisworks and look at the **Properties** window. Find the tab (also called *category*) and the row (the *property*) that holds the material text. In Revit-sourced models this is usually category **Element**, property **Material**; other formats often use **Item** and **Material**. Dyncamelo searches by exactly the names you see, including their language.

## 3. The search

1. Press ++space++, type `Search.ByProperty`, and insert **Search.ByProperty** (category *Navisworks ▸ Search*).
2. Type into its inputs:
    * `categoryName`: `Element` (or what you found),
    * `propertyName`: `Material`,
    * `value`: `Concrete`,
    * `mode`: choose `contains` from the drop-down. The other modes are `equals`, `wildcard`, `>`, `>=`, `<` and `<=`.
3. Leave `document` alone. A **Document** input uses the active document when nothing is wired to it.
4. Add a **Watch List** node (category *Display*) and wire `items` from the search into its `list`.
5. Press **Run** (++f5++).

The Watch List fills with every matching item. If it is empty, the names do not match what Navisworks shows. The `categoryName` and `propertyName` boxes have a small **magnifier** button: select an element in Navisworks, press the magnifier, and pick the tab and then the property from a list that shows only what that element has. Nothing is read until you press it.

## 4. Colour the result

1. Add **Appearance.OverrideColor** (*Navisworks ▸ Appearance*).
2. Wire `items` from the search into its `items` input.
3. Click the colour swatch on its `color` input and choose red.
4. Press **Run**. Every concrete item in the viewport turns red.

This is a real Navisworks colour override, as if you had used *Item Tools ▸ Override Color*. To take it back from a graph, use **Appearance.Reset** (or **Appearance.ResetAll** for a clean slate before you colour again).

> **Save the model before you experiment.** Whether one Navisworks **Undo** reverses a whole run has not been confirmed. The editor's own undo (++ctrl+z++ while the pane has the focus) changes the graph only.

## 5. Save it as a selection set

1. Add **SelectionSet.Create** (*Navisworks ▸ SelectionSets*).
2. Wire the `items` output of **Appearance.OverrideColor** into its `items` input. Nodes that change the model pass their items through for exactly this reason, so you can chain them.
3. Type `Concrete elements` into its `name` input.
4. Press **Run**. The set appears in the Navisworks **Sets** window. `SelectionSet.Create` replaces a top-level set with the same name, so running again updates it instead of duplicating it.

The finished graph:

```
Search.ByProperty ──items──▶ Appearance.OverrideColor ──items──▶ SelectionSet.Create
(Element / Material /                (colour: red)                (name: Concrete elements)
 Concrete / contains)
        └──items──▶ Watch List
```

## 6. Make it live

Switch the run bar from **Manual** to **Auto**. Change `Concrete` to `Steel`: the graph runs again by itself and recolours the model. Only the nodes **after** your edit run again; the rest serve their stored results, which is what keeps large graphs fast.

## 7. Save the graph

Press ++ctrl+s++ and save a `.dyc` file. It stores the nodes, wires and the values you typed, not the results, so a graph you open later computes afresh on its first run. It is plain text, so it can be emailed or kept in version control. Save it in `Documents\Dyncamelo\Scripts` and the [Script Player](player.md) lists it.

## 8. One search for many values

Lists are where Dyncamelo pays off. Add a **String.Split** node with the text `Concrete,Steel,Masonry` and the separator `,`, and wire its result into the `value` input of the search. The search now receives a **list** of three values on an input that wants **one**, so the node runs three times and gives three lists of items. The wire is drawn dashed to show this. Wire the same three texts into the `name` input of **SelectionSet.Create**, and one run makes three sets.

This is *replication*, and how several lists pair up is *lacing*. Both are explained in [Concepts](concepts.md#lists-replication-and-lacing).

## When something is red

A node that fails shows a red border and a message; hover it, or open the **Errors** count in the status bar. Press ++i++ with a node selected to hear in words why it did or did not run. The full list of what red, amber and idle mean is in [Running a graph](running-graphs.md#node-states) and [Troubleshooting](troubleshooting.md#a-node-is-red-or-amber).

## Where next

* [Concepts](concepts.md) and [Inputs, outputs and kinds](ports-and-kinds.md) explain how graphs behave.
* [Sample scripts](samples.md) are finished graphs to open, run and take apart.
* [Recipes](recipes.md) list node chains for the jobs of a BIM coordinator, a manager and a model maintainer.
* The [node reference](nodes/index.md) lists every node with its inputs and outputs.
