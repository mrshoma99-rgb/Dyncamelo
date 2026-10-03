---
title: Write spreadsheet data onto model items
order: 80
summary: Read an Excel sheet row by row, find the item for each GUID and store the value in a property tab of your own.
---

# Write spreadsheet data onto model items

Goal: take a value such as a classification from each row of an Excel sheet and store it as a searchable property on the item with the matching GUID.

## Before you start

* Open a model in Navisworks and the Dyncamelo editor.
* Have an `.xlsx` file with one row for each item. Column A holds the item's instance GUID as text, and column B holds the value, here a classification. The first row holds headers.
* Save the model first. The values are written into the open document. The source files of the model are never changed.

[Download the graph](../graphs/properties-from-excel.dyc)

## Steps

1. Add `Table.FromExcelFile` (*Table*). Type the full path of the workbook into `path`, for example `C:\Data\classification.xlsx`. Leave `firstRowIsHeader` ticked, so the header row is not treated as data.
2. Add `Table.Rows` (*Table*) and wire the `table` in. `rows` is a list with one list of cells for each row.
3. Add `Loop.Item` (*Workflow*) and wire `rows` into its `items`. Everything you wire between `Loop.Item` and `Loop.Collect` runs once for each row, in order.
4. Add two `List.GetItemAtIndex` nodes (*List*). Wire the `item` output of `Loop.Item` (the current row) into the `list` of both. Type `0` into `index` of the first and rename it `GUID of the row`. Type `1` into `index` of the second and rename it `Classification of the row`.
5. Add `Search.ByGuid` (*Navisworks ▸ Search*). Wire the `item` output of `GUID of the row` into its `guids`. A single GUID is treated as a list of one.
6. Add a `String` node (*Input*) with the text `Classification`, and rename it `Property name`.
7. Add `Properties.SetCustom` (*Navisworks ▸ Properties*). Wire the `items` output of `Search.ByGuid` into `modelItems`, `Property name` into `names`, and the `item` output of `Classification of the row` into `values`. Leave `tabName` on `Dyncamelo Data`, or type your own tab name.
8. Add `Loop.Collect` (*Workflow*). Wire `loop` from `Loop.Item` into its `loop` and the `modelItems` output of `Properties.SetCustom` into its `value`.
9. Add `List.Count` (*List*) on `results`, then a `Watch` (*Display*) named `Rows written` on its `count`. Try it on a sheet of a few rows first, then press ++f5++.

![The graph: Table.FromExcelFile and Table.Rows into a loop that finds each item by GUID and writes its classification with Properties.SetCustom.](../../images/wiki-graph-properties-from-excel.png)

## What you get

Each matched item has a property tab called `Dyncamelo Data` (or the name you typed) with the property `Classification`. Open the Navisworks **Properties** window on an item to see it. The values are searchable, can be used in schedules, and travel with the NWF or NWD.

Running again with `merge` on (the default) keeps other properties in the tab and lets the new values win. To remove the tab, run `Properties.RemoveCustomTab` with the same items and tab name.

To write more columns, give `names` a list and `values` a list of the same length. For example, a `String.Split` of `Classification,Supplier` for `names`, and a `List.Create` (*List*) of two more `List.GetItemAtIndex` nodes for `values`.

!!! warning "Slow on big sheets"
    `Search.ByGuid` walks every item of the document once each time it runs, and the loop runs it once for each row. A long sheet on a large model takes a long time. Press ++esc++ to stop; the next **Run** carries on. See [Speed up a slow graph](speed-up-slow-graph.md).

!!! note "Which GUID matches"
    `Search.ByGuid` matches the item's **instance GUID** (a 22-character IFC GlobalId is accepted too). Its `missing` output lists the GUIDs that matched no item, so wire it into a `Watch List` while you test.

## If it does not work

* Nothing is written and `missing` lists every GUID: the sheet holds a different kind of identity, or the cells are not plain text. Compare one cell with `ModelItem.InstanceGuid` of an item you picked in Navisworks.
* A node is red: see [Read errors and warnings](read-errors-and-warnings.md).
* `Properties.SetCustom` changes the document, so save before you try it on a real model.

## Next

* [Find elements from a list of GUIDs](find-elements-from-guids.md) uses the same sheet to select the items.
* [Properties nodes](../nodes/navisworks-properties.md#node-properties-setcustom) lists `Properties.SetCustom` and its companions.
* [IFC, BCF, Excel and CSV](../exchange-formats.md#excel).
