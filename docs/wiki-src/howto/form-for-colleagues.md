---
title: Give colleagues a form with the Script Player
order: 130
summary: Turn a graph into a small form with input nodes, so a colleague can run it without opening the node editor.
---

# Give colleagues a form with the Script Player

Goal: build a graph whose inputs appear as a form in the **Script Player**, so a colleague fills in a few fields, presses **Run** and reads the result.

## Before you start

* Open a model in Navisworks and the Dyncamelo editor.
* You build and test the graph. Your colleague only needs Dyncamelo installed and a model open.
* The example finds items above a size, totals them by a property and writes an Excel file. Replace the names with your own.

[Download the graph](../graphs/player-form.dyc)

## Steps: build the graph

1. Add a `Number` node (*Input*). Double-click its title and rename it `Minimum volume`. The name becomes the label of the field. Type `1` as the value.
2. Add a `Choice` node (*Input*). Rename it `Group by`. In its **Options, one per line** box type `Element.Category` and `Element.Level` on two lines. The drop-down above the box then offers both, and starts on the first.
3. Add a `File Path` node (*Input*). Rename it `Excel file` and type a full path such as `C:\Temp\takeoff.xlsx` into its box. The `…` button only picks a file that exists, so type the path of a new file.
4. Add `Search.ByProperty` (*Navisworks ▸ Search*) with `categoryName` = `Element`, `propertyName` = `Volume` and `mode` = `>`. Wire the `value` output of `Number` into its `value`.
5. Add `Properties.ToTable` (*Navisworks ▸ Properties*) and wire the search `items` into it. Feed `properties` from a `String.Split` (*String*) with the text `Element.Category,Element.Level,Element.Volume` and the separator `,`.
6. Add `Table.GroupBy` (*Table*). Wire the table in, `value` from `Choice` into `by`, and a `String.Split` with the text `count,sum:Element.Volume` into `aggregations`.
7. Add `Table.ToExcelFile` (*Table*). Wire the grouped table into `table` and `path` from `File Path` into `path`.
8. Add a `Watch Table` (*Display*) on the grouped table. Press ++f5++ and check it works.
9. Choose **Graph ▸ Script Description…** and type what the script does. It is shown above the form.
10. Save with ++ctrl+s++ into `Documents\Dyncamelo\Scripts`.

![The player-form graph: a Number, a Choice and a File Path feeding a search, a table and Table.ToExcelFile.](../../images/wiki-graph-player-form.png)

The Player shows every input node by default, top to bottom in the order the nodes sit on the canvas. Other nodes are hidden. To offer any other unwired input as a field, right-click its socket and choose **Show in Player**. A small ▶ badge marks nodes the Player uses. Press ++ctrl+alt+p++ on selected nodes to show or hide them in the Player. Put inputs at the top level of the graph: inputs inside a [node group](../node-groups.md) are not offered.

## Steps: run it as your colleague

1. Click **Player** on the **BIMCamel** ribbon tab. Choose the script from the list.
2. Fill in the form: a number, a choice, a file path.
3. Press **Run**. Because the script writes a file, the Player lists the nodes responsible and asks "Run it?" the first time, and again whenever the file changes.

![The Script Player with the script chosen and its form: three fields and the buttons Run, Reset, Edit and File.](../../images/wiki-player-form.png)

## What you get

After the run the Player shows a results card: a green or red dot with a summary line, the value of each Watch node, and below it any node that failed or warned. **Copy** puts the report on the clipboard as text for an e-mail. The values your colleague typed are remembered for that script.

![The Script Player after a run: the summary line, the Watch Table result and the Copy button.](../../images/wiki-player-results.png)

## If it does not work

* The script is not in the list: save it in `Documents\Dyncamelo\Scripts` or add its folder under **Script folders**, then press ↻.
* The Player says a node is not installed: the script cannot run until it is. See [A graph opens with a warning, or with missing nodes](../troubleshooting.md#a-graph-opens-with-a-warning-or-with-missing-nodes).
* The confirmation question appears: [that is a safety check, not an error](../troubleshooting.md#the-script-player-asks-me-to-confirm-a-script).

## Next

* [The Script Player](../player.md) describes the form, results and safety in full.
* [Reuse a piece of graph as a node group](reusable-node-group.md).
* [Saving and opening scripts](../saving-opening.md#script-description-and-the-scripts-folder).
