---
title: Check how complete your data is
order: 60
summary: Count the items that lack a required property and show the share as a percentage.
---

# Check how complete your data is

Goal: get a single number that says how many items in a group are missing a property they should have, for example how many doors have no Fire Rating.

## Before you start

* Open a model in Navisworks and the CamelGraph editor.
* Decide which items to check (a search) and which property each one must carry. Use the names your model shows ([find the names](find-property-names.md)).

[Download the graph](../graphs/data-completeness.dyc)

## Steps

1. Add a `String` node (*Input*). Double-click its title and rename it `Category to check`. Type `Doors` as the text.
2. Add `Search.ByProperty` (*Navisworks ▸ Search*) to find the items to check. Type `Element` into `categoryName`, `Category` into `propertyName`, and leave `mode` on `equals`. Wire the `String` into `value`.
3. Add `Audit.MissingProperty` (*Navisworks ▸ Analysis*). Wire the search `items` into its `items`. Type `Element` into `categoryName` and the property each item must carry, here `Fire Rating`, into `propertyName`. It finds the items that do **not** carry the property.
4. Add a `List.Count` (*List*), rename it `Missing`, and wire the `items` output of `Audit.MissingProperty` into its `list`. This is the number missing.
5. Add a second `List.Count`, rename it `All`, and wire the search `items` into its `list`. This is the number checked.
6. Add `Math.Percent` (*Math*). Wire `count` from `Missing` into `part` and `count` from `All` into `total`.
7. Add a `Watch` (*Display*), rename it `Percent missing`, and wire `percent` into it.
8. Press ++f5++.

![The data-completeness graph: a search, Audit.MissingProperty, two List.Count nodes, Math.Percent and a Watch.](../../images/wiki-graph-data-completeness.png)

## What you get

The Watch shows the share of checked items that are missing the property. `Math.Percent` gives `part / total * 100`, and 0 when the total is 0, so an empty search reads 0 rather than failing.

To show the share that **has** the property, add `Subtract` (*Math*) with `a` = `100` and `b` wired from `percent`.

To see which items are missing it, wire the `items` output of `Audit.MissingProperty` into a `Watch List`, or into `SelectionSet.Create` ([Save selection sets](save-selection-sets.md)) to keep them for review.

!!! warning "Both counts must cover the same items"
    `Audit.MissingProperty` looks at the items you give it **and everything below them**. While `geometryOnly` is on (the default) it reports only items that have geometry. If your search returns containers, the missing count can be larger than the checked count. If it is, change the search so that it returns the items that carry the geometry. The missing count can never exceed the checked count when both cover the same items.

!!! tip "Check several properties at once"
    `propertyName` wants one text. Wire a list of names into it and the node runs once per name and gives one result for each ([Concepts](../concepts.md#lists-replication-and-lacing)).

## If it does not work

* The checked count is 0: the search found nothing. See [A search returns nothing](../troubleshooting.md#a-search-returns-nothing-or-the-magnifier-next-to-a-tab-or-property-shows-no-names).
* The audit is slow: it searches below every item you pass in. Pass a smaller list. See [Speed up a slow graph](speed-up-slow-graph.md).

## Next

* [Write spreadsheet data onto model items](write-excel-data-onto-items.md) to fill the gaps.
* [Take quantities out to Excel](quantity-takeoff-to-excel.md).
* [Recipes](../recipes.md#bim-manager) has more checks for a BIM manager.
