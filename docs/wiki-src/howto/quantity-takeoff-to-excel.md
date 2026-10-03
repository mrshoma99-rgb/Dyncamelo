---
title: Take quantities out to Excel
order: 50
summary: Read properties of model items into a table, total them by category and write an Excel workbook.
---

# Take quantities out to Excel

Goal: count items and total a quantity for each category, and save the result as an `.xlsx` file.

## Before you start

* Open a model in Navisworks and the Dyncamelo editor.
* Know the tab and property to read, for example *Element ▸ Category* and *Element ▸ Volume* in a Revit-sourced model ([find the names](find-property-names.md)). Use the names your model shows.
* Excel does not have to be installed. Dyncamelo writes `.xlsx` itself.

[Download the graph](../graphs/qto-to-excel.dyc)

## Steps

1. Add `Search.ByProperty` (*Navisworks ▸ Search*). Type `Element` into `categoryName`, `Level` into `propertyName` and a level name from your model, such as `Level 1`, into `value`. Leave `mode` on `equals`.
2. Add `Properties.ToTable` (*Navisworks ▸ Properties*). Wire `items` from the search into its `items`.
3. Its `properties` input is a list of column names. Add a `String.Split` node (*String*), type `@Name,Element.Category,Element.Volume` into `text` and `,` into `separator`, and wire `list` into `properties`. `@Name` is the item name; the others are `Category.Property`.
4. Add a `Watch Table` (*Display*) and wire the `table` output into it. Press ++f5++ and check the rows. An empty cell means the item does not carry that property.
5. Add `Table.GroupBy` (*Table*). Wire `table` into `table` and type `Element.Category` into `by`.
6. Add a second `String.Split` with the text `count,sum:Element.Volume` and the separator `,`, and wire its `list` into `aggregations`. The result has one row per category with the columns `Element.Category`, `count` and `sum(Element.Volume)`.
7. Add `Table.Sort` (*Table*). Wire the table in, type `sum(Element.Volume)` into `columns` and tick `descending`. The largest total comes first.
8. Add `Table.ToExcelFile` (*Table*). Wire the table in. Type a **full path** into `path`, for example `C:\Temp\takeoff.xlsx`, and `Takeoff` into `sheet`.
9. Press ++f5++.

![A Watch Table close up: a grouped table with one row for each category, a count and totals.](../../images/wiki-watch-table.png)

![The quantity take-off graph: Search.ByProperty, Properties.ToTable, Table.GroupBy, Table.Sort and Table.ToExcelFile.](../../images/wiki-graph-qto-to-excel.png)

## What you get

A workbook with one worksheet named `Takeoff`. The first row holds the column names and each row below it is one category.

!!! warning "Always use a full path"
    A relative path points into the Navisworks install folder, which ordinary users cannot write to. The write fails with "access denied". Paste the path without the quotes that Explorer's *Copy as path* adds.

!!! tip "Other aggregations"
    `Table.GroupBy` also works out `average`, `min`, `max`, `median`, `first`, `last`, `list` and `distinct`. Add `as Name` to rename a result: `sum:Element.Volume as Volume`.

## If it does not work

* The file node fails: see [A file node fails with "access denied"](../troubleshooting.md#a-file-node-fails-with-access-denied-or-writes-to-the-wrong-place).
* The table is empty: the search found nothing. See [A search returns nothing](../troubleshooting.md#a-search-returns-nothing-or-the-magnifier-next-to-a-tab-or-property-shows-no-names).
* A node is red: see [Read errors and warnings](read-errors-and-warnings.md).

## Next

* The sample *QTO Rollup by Category* does the grouping with one node, `Takeoff.SumPropertyByGroup` ([Sample scripts](../samples.md)).
* [IFC, BCF, Excel and CSV](../exchange-formats.md#excel) lists the Excel and table nodes.
* [Check how complete your data is](data-completeness-kpi.md).
