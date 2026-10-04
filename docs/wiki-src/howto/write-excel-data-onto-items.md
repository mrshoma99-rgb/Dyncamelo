---
title: Write spreadsheet data onto model items
order: 80
summary: Join an Excel sheet to model items by GUID and store its columns as a property tab of your own.
---

# Write spreadsheet data onto model items

Goal: take columns such as `Cost` and `Supplier` from an Excel sheet, match each row to a model item by GUID, and store the values as a searchable property tab on the item.

## Before you start

* Open a model in Navisworks and the CamelGraph editor.
* Have an `.xlsx` file with one row for each item. One column, here with the header `GUID`, holds the item's instance GUID as text. The other columns, here `Cost` and `Supplier`, hold the data. The first row holds the headers.
* Keys match as exact text: capitals and lower case differ.
* Save the model first. The values are written into the open document. The source files are never changed.

[Download the graph](../graphs/properties-from-excel.dyc)

## Steps

1. Add a `String` node (*Input*), rename it `Category to enrich` and type `Walls` into it.
2. Add `Search.ByProperty` (*Navisworks ▸ Search*). Type `Element` into `categoryName` and `Category` into `propertyName`. Wire the `String` into `value`, which has no box of its own.
3. Add `Properties.ToTable` (*Navisworks ▸ Properties*). Wire the search `items` into its `items`. Its `properties` input has no box either: add a `String.Split` (*String*) with the text `@Guid,@Name` and the separator `,`, and wire its `list` into it. `@Guid` is the item's instance GUID.
4. Add a `File Path` node (*Input*), rename it `Workbook` and type the full path, for example `C:\Data\walls.xlsx`. Add `Table.FromExcelFile` (*Table*) and wire `path` into its `path`.
5. Add `Table.Join` (*Table*). Wire the `Properties.ToTable` table into `left` and the Excel table into `right`. Type `@Guid` into `leftKey` and `GUID` into `rightKey`, and choose `left` in `kind`. A left join keeps every item, in search order.
6. Add a `Watch Table` (*Display*), rename it `Joined table`, wire the joined table into it and press ++f5++. Check that `Cost` and `Supplier` are filled. An empty cell means the sheet has no row for that item.
7. Add a `String` node with the text `Cost,Supplier`, rename it `Columns to write`, and wire it into `columns` of `Table.SelectColumns` (*Table*). Wire the joined table into its `table`.
8. Add `Table.Rows` (*Table*) and wire the selected table into it. `rows` holds one list of cells for each item.
9. Add `Properties.SetCustom` (*Navisworks ▸ Properties*). Wire the search `items` into `modelItems` and `rows` into `values`. Add another `String.Split` with the text `Cost,Supplier` and the separator `,`, and wire its `list` into `names`. Type `Spreadsheet` into `tabName`.
10. Right-click the `modelItems` socket, choose **List Levels** and then `@L1 — items`. Right-click the `values` socket, choose **List Levels** and then `@L2 — lists of items`. A badge shows on each socket.
11. Press ++f5++ again.

![The graph: the model items and the Excel sheet joined by GUID, then written with Properties.SetCustom using list levels @L1 and @L2.](../../images/wiki-graph-properties-from-excel.png)

## What you get

Every item that the search found has a property tab called `Spreadsheet` with the properties `Cost` and `Supplier`. Open the Navisworks **Properties** window on an item to see it. The values are searchable and travel with the NWF or NWD.

Why step 10: `Properties.SetCustom` writes one list of names and values to **all** the items it is given. The list levels make it run once for each item, with item 1 paired with row 1, item 2 with row 2, and so on. Items and rows come from the same search and the same left join, so they line up. `names` is the same for every item and needs no level.

Running again with `merge` on (the default) keeps other properties in the tab and lets the new values win. `Properties.RemoveCustomTab` removes the tab.

!!! tip "A shorter way: Properties.SetCustomFromTable"
    Steps 7 to 10 can be one node. Add `Properties.SetCustomFromTable` (*Navisworks ▸ Properties*), wire the joined table into `table`, type `Spreadsheet` into `tabName` and `@Guid` into `keyColumn`, and leave `items` and `columns` empty. Every row finds its item by the GUID in the `@Guid` column, in one pass over the model, and every column except `@Guid` and the other `@` columns becomes a property named after its header. No list levels are needed, rows that match no item are skipped and listed in `missing`, and the node checks every value before it writes to the first item. Without `keyColumn`, row 1 goes to item 1, row 2 to item 2 and so on, and the node refuses a table with a different number of rows than items.

!!! warning "One row for each GUID"
    If the sheet holds a GUID twice, the join adds a second row for that item and items and rows no longer line up. Remove duplicates first.

## If it does not work

* The `Cost` and `Supplier` cells in the Watch Table are empty: the keys do not match. Compare one `@Guid` value with the sheet.
* `Properties.SetCustom` is red and says the value for a property "is a list of 2 values": the `values` socket lacks its `@L2` badge, so a whole row reached one property. The node refuses it instead of writing the list into every item.
* Items get the wrong values: a socket lacks its `@L1` or `@L2` badge, or the sheet has duplicate GUIDs ([Concepts](../concepts.md#lists-replication-and-lacing)).
* A node is red: see [Read errors and warnings](read-errors-and-warnings.md).

## Next

* [Find elements from a list of GUIDs](find-elements-from-guids.md) uses a sheet of GUIDs to select the items.
* [Properties nodes](../nodes/navisworks-properties.md#node-properties-setcustom) lists `Properties.SetCustom` and its companions.
