---
title: Find the name of a tab or property
order: 30
summary: Use the magnifier on a node, and Properties.Discover, to find the exact tab and property names a model uses.
---

# Find the name of a tab or property

Goal: get the exact tab (category) and property names for a search, without guessing.

## Before you start

* Open a model in Navisworks and the Dyncamelo editor.
* Names must be the ones Navisworks **displays**, in the language it displays them in. Internal names do not match.

## Steps

### With the magnifier

1. Click one element in Navisworks that has the data you want.
2. In Dyncamelo, add `Search.ByProperty` (*Navisworks ▸ Search*).
3. Press the small **magnifier** next to `categoryName`. Dyncamelo lists the tabs of the elements selected in Navisworks right now. Type a few letters to narrow the list, then click a tab. The box fills in.
4. Press the magnifier next to `propertyName`. It lists the properties of the tab you chose. Click one.
5. Wire a `String` (or `Number`) node holding the value you want into `value`. That input accepts any kind of value, so it has no box of its own.

![The magnifier popup on Properties.Value, listing the tabs of the picked element.](../../images/wiki-magnifier.png)

Some nodes read one element, such as `Properties.Value` (*Navisworks ▸ Properties*). Their magnifier lists the names of the element on that node's own `item` input. Press the element picker on the node to take the current selection, or wire an element in and press **Run** first.

!!! note "What the magnifier reads"
    Nothing is read until you press it. It reads only the element in question, at most the first 100 if the input carries a longer list. The model is never searched to fill the list. Typing a name by hand always works.

### With Properties.Discover

To see everything a group of items carries:

1. Select a few items in Navisworks.
2. Add `Selection.Current` (*Navisworks ▸ Selection*), `Properties.Discover` (*Navisworks ▸ Properties*) and a `Watch Table` (*Display*).
3. Wire `items` from `Selection.Current` into `items`, and `table` into the Watch Table.
4. Press ++f5++.

You get one row for each category and property, with the columns `Category`, `Property`, `Items` (how many items carry it), `Distinct` (how many different values) and `Samples` (the first few values). `maxSamples` sets how many samples to show; the default is 3.

!!! warning "Give it a small selection"
    `Properties.Discover` reads every property of every item once. The cost grows with items times properties. Use a selection, not the whole model.

## What you get

The exact `categoryName` and `propertyName` text for any search node, and a table of what a model actually holds.

## If it does not work

The magnifier explains itself when it has nothing to show, for example "Nothing is selected in Navisworks. Select an element, then search." Each message and its fix is in [A search returns nothing, or the magnifier shows no names](../troubleshooting.md#a-search-returns-nothing-or-the-magnifier-next-to-a-tab-or-property-shows-no-names).

## Next

* [Colour elements by a property](colour-elements-by-property.md).
* [Inputs, outputs and kinds](../ports-and-kinds.md#choosing-a-tab-or-a-property-name) explains the magnifier in full.
* [Take quantities out to Excel](quantity-takeoff-to-excel.md).
