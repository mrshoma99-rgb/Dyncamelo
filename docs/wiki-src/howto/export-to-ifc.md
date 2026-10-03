---
title: Export model items to IFC
order: 110
summary: Write the items a search finds to an IFC file, with your own project names and coordinate options.
---

# Export model items to IFC

Goal: write a chosen set of model items to an `.ifc` file, with names you set for the project, site, building and storey.

## Before you start

* Open a model in Navisworks and the Dyncamelo editor.
* The IFC export engine ships inside Dyncamelo. You do not need another plug-in.
* Decide where the file goes and use a **full path** that ends in `.ifc`, for example `C:\Exports\level-02.ifc`. The folder is created if it is missing.

[Download the graph](../graphs/ifc-export.dyc)

## Steps

1. Add a `String` node (*Input*), rename it `Level to export` and type `Level 02`. Add `Search.ByProperty` (*Navisworks ▸ Search*) to choose what to export: `categoryName` = `Element`, `propertyName` = `Level`, `mode` = `equals`, and wire the `String` into `value`. Use a level name from your model.
2. Add `Export.ToIfc` (*Navisworks ▸ Export*). Wire the search `items` into its `items`. Type the full path into `filePath`. Only `items` and `filePath` are required.
3. Add `Export.IfcSpatialNames` (*Navisworks ▸ Export*). Type names into `project`, `site`, `building` and `storey`, for example `Office Block`, `Main Site`, `Block A` and `Level 02`. Wire its `spatialNames` output into the `spatialNames` input of `Export.ToIfc`.
4. Add `Export.IfcCoordinates` (*Navisworks ▸ Export*). Leave `basePoint` on `GeometryOrigin` for now. Wire its `coordinates` output into the `coordinates` input of `Export.ToIfc`.
5. Add a `Watch` (*Display*), rename it `Elements exported` and wire `elementCount` into it. You can add more for `fileCount` and `fileSizeKb`.
6. Switch **Auto** off in the run bar, then press ++f5++. The export reads the geometry of every item and can take a while on a large model.

![The IFC export graph: a search into Export.ToIfc, with Export.IfcSpatialNames and Export.IfcCoordinates wired into it.](../../images/wiki-graph-ifc-export.png)

## What you get

An IFC file with a spatial tree, geometry (repeated shapes reused as mapped items to keep the file small), property sets, materials, base quantities and, for IFC4, georeferencing. The outputs `filePath`, `fileCount`, `elementCount`, `triangleCount` and `fileSizeKb` tell you what was written.

## Useful options

| Input | Default | Use it to |
|---|---|---|
| `schema` | `IFC4` | Write `IFC2x3` instead. |
| `instancing` | on | Switch off to write every mesh in full. |
| `properties`, `materials`, `quantities` | on | Leave out property sets, materials or base quantities. |
| `units` | `Auto` | Force `Millimeters`, `Centimeters`, `Meters`, `Feet` or `Inches` if the model's units are wrong. |
| `quality` | `Balanced` | Choose `Small file` or `High detail`. |
| `splitMegabytes` | 0 | Split the output into parts near this size. 0 is one file. |
| `validate` | off | Run the built-in structural check on each file written. |

`Export.IfcCoordinates` also takes `Custom` for `basePoint`, with `eastings`, `northings` and `elevation` in metres, a `rotationDegrees` and a `writeGeoref` switch. `Export.IfcSetClassMap` gives items an IFC class by set name, and `Export.IfcRoles` and `Export.IfcParameterRule` map properties to IFC roles and rename them. All are described in [IFC, BCF, Excel and CSV](../exchange-formats.md#writing-ifc-exporttoifc).

!!! tip "One file for each group"
    To write several IFC files from one graph, use one `Export.ToIfc` for each file, or put it between `Loop.Item` and `Loop.Collect`.

## If it does not work

* The file is not written: see [A file node fails with "access denied"](../troubleshooting.md#a-file-node-fails-with-access-denied-or-writes-to-the-wrong-place).
* `Export.ToIfc` says "No geometry elements to export": the search found no items, or none with geometry. See [A search returns nothing](../troubleshooting.md#a-search-returns-nothing-or-the-magnifier-next-to-a-tab-or-property-shows-no-names).
* `Export.ToIfc` says the path "must end in .ifc": fix the file name.
* With `validate` on, the node fails and lists the issues the checker found.
* The export is slow: see [Speed up a slow graph](speed-up-slow-graph.md).

## Next

* [Send clashes to BCF](clash-issues-bcf.md).
* [Export nodes](../nodes/navisworks-export.md#node-export-toifc) lists every input and output.
* [Privacy and safety](../privacy-and-safety.md#what-a-graph-can-do): export nodes write files, so the Player asks before it runs a script that has one.
