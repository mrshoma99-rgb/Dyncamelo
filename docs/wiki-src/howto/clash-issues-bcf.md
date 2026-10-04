---
title: Send clashes to BCF and read an issue list back
order: 100
summary: Export the results of a clash test as a BCF file for other tools, and import a BCF file to select its elements and list its titles.
---

# Send clashes to BCF and read an issue list back

Goal: hand clash results to a BCF tool such as BIMcollab, Konekt, Revizto or Autodesk Construction Cloud, and read a BCF file from someone else back into Navisworks.

## Before you start

* To export clash results, use **Navisworks Manage**: Simulate does not include Clash Detective. Run the clash tests first. Importing a BCF file does not need Clash Detective.
* Open a model and the CamelGraph editor.
* BCF is the vendor-neutral issue format: a `.bcfzip` file of topics, each with a title, status, comments, a camera and the elements involved.

## Part A: export

[Download the graph](../graphs/bcf-export.dyc)

1. Add `Clash.Tests` (*Navisworks ▸ Clash ▸ Tests*) for the list of tests.
2. Add `List.GetItemAtIndex` (*List*). Wire `tests` into `list` and type `0` into `index` for the first test. A negative index counts from the end.
3. Add `ClashTest.Results` (*Navisworks ▸ Clash ▸ Tests*). Wire `item` into its `test`.
4. Add `BCF.ExportIssues` (*Navisworks ▸ Export*). Wire `results` into `results`. Type a **full path** into `filePath`, for example `C:\Reports\clash-issues.bcfzip`.
5. Add a `Watch` (*Display*) on `topicCount`, rename it `Topics exported`, and press ++f5++.

![The BCF export graph: Clash.Tests, List.GetItemAtIndex, ClashTest.Results and BCF.ExportIssues.](../../images/wiki-graph-bcf-export.png)

Each result becomes a topic with a markup, a camera viewpoint, the GUIDs of its elements and a snapshot picture. Switch `includeSnapshots` off to go faster on long result lists. `statusMap` is optional: without it New and Active become Open, Reviewed becomes In Progress, and Approved and Resolved become Closed.

## Part B: import

[Download the graph](../graphs/bcf-import.dyc)

1. Add `BCF.ImportIssues` (*Navisworks ▸ Export*). Type the full path of a `.bcfzip` (BCF 2.0 or 2.1) into `filePath`, for example the file from Part A.
2. Add an `Integer` node (*Input*), rename it `Topic number` and leave it at `0`.
3. Add `List.GetItemAtIndex` and rename it `Topic`. Wire `topics` into `list` and `Topic number` into `index`.
4. Add `Dictionary.ValueOrDefault` (*Dictionary*). Wire `item` into `dictionary` and type `title` into `key`. Wire `value` into a `Watch` and rename it `Topic title`.
5. Add a second `List.GetItemAtIndex` and rename it `Items of the topic`. Wire `modelItems` into `list` and `Topic number` into `index`. `modelItems` holds one list of items for each topic, in the same order as `topics`.
6. Add `Selection.SetCurrent` (*Navisworks ▸ Selection*). Wire the `item` output of the second `List.GetItemAtIndex` into its `items`. The elements named in that topic are selected.
7. Press ++f5++, then change `Topic number` to look at the next topic.

![The BCF import graph: BCF.ImportIssues, two List.GetItemAtIndex nodes picking one topic, Dictionary.ValueOrDefault for its title and Selection.SetCurrent.](../../images/wiki-graph-bcf-import.png)

To list **all** the titles, skip the first `List.GetItemAtIndex` and wire `topics` straight into `Dictionary.ValueOrDefault`. The wire turns dashed and you get one title for each topic. Each topic is a dictionary with the keys `guid`, `title`, `status`, `type`, `description`, `creationAuthor`, `creationDate`, `comments`, `commentAuthors`, `commentDates`, `componentGuids`, `camera` and `hasSnapshot`.

To jump the view to one issue, set `applyCameraTopicIndex` to its position, counting from 0. The default, `-1`, leaves the view alone. A topic without a camera, or an index past the last topic, makes the node report an error.

## What you get

* A BCF file that other tools open, with one topic for each clash result.
* A selection of the elements from a BCF file, and a list of what it says.

!!! warning "Elements are matched by GUID"
    Elements are identified by their IFC GlobalId when the item has one, and by its InstanceGuid otherwise. For models that did not come from IFC this can be lossy: another tool may not find the same element. On import, elements are matched by IFC GlobalId first, then InstanceGuid. Cameras are written in metres, as the BCF convention says, whatever units the model uses.

## If it does not work

* Fewer elements are selected than topics name: some GUIDs matched no item in this model.
* A clash node is red: you may be in a product without Clash Detective. See [Make a clash report](clash-report.md#if-it-does-not-work).
* A file node fails: use a full path ([why](../troubleshooting.md#a-file-node-fails-with-access-denied-or-writes-to-the-wrong-place)).

## Next

* `ClashResult.SetStatus` (*Navisworks ▸ Clash ▸ Results*) can update clash statuses from the topic statuses. `ClashResult.ByGuid` finds the live results again from the topic GUIDs (the `guid` output of `BCF.ImportIssues`), and a result group can be set in one step too.
* [IFC, BCF, Excel and CSV](../exchange-formats.md#bcf-issues) describes every BCF input.
* [Export items to IFC](export-to-ifc.md).
