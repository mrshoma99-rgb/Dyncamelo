---
title: Take quantities out to Excel
order: 50
summary: Read properties of model items into a table, total them by category and write an Excel workbook.
---

# Take quantities out to Excel

Goal: count items and total a quantity for each category, and save the result as an `.xlsx` file.

## Before you start

* Open a model in Navisworks and the CamelGraph editor.
* Know the tab and property to read, for example *Element ▸ Category* and *Element ▸ Volume* in a Revit-sourced model ([find the names](find-property-names.md)). Use the names your model shows.
* Excel does not have to be installed. CamelGraph writes `.xlsx` itself.

[Download the graph](../graphs/qto-to-excel.dyc)

## Steps

1. Add a `Number` node (*Input*). Double-click its title and rename it `Minimum volume`. Leave the value at `0`.
2. Add `Search.ByProperty` (*Navisworks ▸ Search*). Type `Element` into `categoryName`, `Volume` into `propertyName` and choose `>` in the `mode` drop-down. Wire the `Number` into `value`. It finds every item whose volume is above the minimum.
3. Add `Properties.ToTable` (*Navisworks ▸ Properties*). Wire `items` from the search into its `items`.
4. Its `properties` input is a list of column names and has no box. Add a `String` node (*Input*), rename it `Columns to read` and type `@Name,Element.Category,Element.Volume`. Add a `String.Split` (*String*) with the separator `,`, wire the `String` into its `text` and its `list` into `properties`. `@Name` is the item name; the others are `Category.Property`.
5. Add a `Watch Table` (*Display*), rename it `Rows read`, and wire the `table` output into it. Press ++f5++ and check the rows. An empty cell means the item does not carry that property.
6. Add `Table.GroupBy` (*Table*). Wire the table into `table` and type `Element.Category` into `by`.
7. The `aggregations` input has no box either. Add a second `String`, rename it `Totals to work out` and type `count,sum:Element.Volume as Volume`. Add a second `String.Split` with the separator `,` and wire its `list` into `aggregations`. The result has one row for each category with the columns `Element.Category`, `count` and `Volume`.
8. Add `Table.Sort` (*Table*). Wire the table in, type `Volume` into `columns` and tick `descending`. The largest total comes first.
9. Add `Table.ToExcelFile` (*Table*). Wire the sorted table in. Type a **full path** into `path`, for example `C:\Reports\qto.xlsx`, and `Quantities` into `sheet`.
10. Press ++f5++.

![A Watch Table close up: a grouped table with one row for each category, a count and totals.](../../images/wiki-watch-table.png)

![The quantity take-off graph: Search.ByProperty, Properties.ToTable, Table.GroupBy, Table.Sort and Table.ToExcelFile.](../../images/wiki-graph-qto-to-excel.png)

## What you get

A workbook with one worksheet named `Quantities`. The first row holds the column names and each row below it is one category.

!!! warning "Always use a full path"
    A relative path points into the Navisworks install folder, which ordinary users cannot write to. The write fails with "access denied". Paste the path without the quotes that Explorer's *Copy as path* adds.

!!! tip "Other aggregations"
    `Table.GroupBy` also works out `average`, `min`, `max`, `median`, `first`, `last`, `list` and `distinct`. Add `as Name` to name a result, as step 7 does.

## If it does not work

* The file node fails: see [A file node fails with "access denied"](../troubleshooting.md#a-file-node-fails-with-access-denied-or-writes-to-the-wrong-place).
* The table is empty: the search found nothing. See [A search returns nothing](../troubleshooting.md#a-search-returns-nothing-or-the-magnifier-next-to-a-tab-or-property-shows-no-names).
* A node is red: see [Read errors and warnings](read-errors-and-warnings.md).

## Next

* The sample *QTO Rollup by Category* does the grouping with one node, `Takeoff.SumPropertyByGroup` ([Sample scripts](../samples.md)).
* [IFC, BCF, Excel and CSV](../exchange-formats.md#excel) lists the Excel and table nodes.
* [Check how complete your data is](data-completeness-kpi.md).
