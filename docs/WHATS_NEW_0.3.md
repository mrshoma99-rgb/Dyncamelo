# What's new in Dyncamelo v0.3 — the "plugin parity" wave

v0.3 turns Dyncamelo's dataflow engine loose on the workflows that today require a stack of commercial Navisworks plugins: custom property writing (iConstruct SmartProperties territory), element transforms, BCF 2.1 issue exchange (BIMcollab / Newforma Konekt / Revizto / ACC), clash management (batch rename, comments, grid grouping, between-run deltas), the model's own grids and levels, native Excel (.xlsx) read/write with zero new dependencies, and headless document batch processing.

**50 new nodes + 1 extended node** — 7 general (run anywhere, incl. the CLI) and 43 Navisworks. Full port-level specs are in the [node catalog](NODE_LIBRARY.md) (rows tagged *Implemented (v0.3)*).

## Highlights

- **Custom property tabs** — `Properties.SetCustom` writes user-defined, searchable, schedulable property tabs that travel with the NWF/NWD (source files are never touched). With the existing string/math/lacing engine this is bulk data enrichment: spreadsheet → `Table.JoinByKey` → model. `RemoveCustomTab` / `RenameCustomTab` / `CustomTabs` complete the lifecycle.
- **Native Excel** — `Excel.ReadFromFile` / `Excel.WriteToFile` handle .xlsx directly (built-in OPC reader/writer, no new dependencies, works headless on the CLI). `Table.JoinByKey` links spreadsheet rows to element GUIDs/marks in one node.
- **BCF 2.1 exchange** — `BCF.ExportIssues` writes .bcfzip topics (markup + camera + component GUIDs + snapshots) from clash results or saved viewpoints; `BCF.ImportIssues` reads them back and resolves components to model items. The vendor-neutral bridge to every coordination cloud (none of which expose a public in-process API).
- **Clash management suite** — rename tests/results/groups in bulk, comment on results, group by status or by the model's own grid ("B-3 : Level 2"), one-node summary matrix, and `Clash.SnapshotToFile` + `Clash.CompareSnapshots` for the weekly new/resolved/persisting delta report (pure file IO — runs headless).
- **Transforms** — move/rotate/set/reset permanent transform overrides on model items (undoable, saved in the NWF; the 2024 API has no temporary transform). `Distance.BetweenItems` measures true mesh surface-to-surface distance with witness points.
- **Tree operations & comments** — rename saved viewpoints/sets, nested folders for both, move items between folders, read the containing folder, and full Review-tab comment threads on viewpoints, sets and clash items.
- **Document lifecycle** — `Document.Open` / `AppendFiles` / `Refresh` / `Merge` and `Model.Remove`: the Navisworks Batch Utility becomes a three-node graph, schedulable via `dyncamelo run` + Task Scheduler.
- **Grids & levels** — read the model's own grid system: levels with elevations, grid intersections, and closest-intersection location labels.
- **Section boxes** — `Viewpoint.SetSectionBox` applies a sectioned close-up around any bounding box (pure .NET — the planned COM spike turned out unnecessary).
- **Zone tagging** — `Zone.AssignByVolumes`: tag every element with the zone volume that contains it, written as a searchable property tab.

## New nodes (50 + 1 extended)

> `SelectionSets.CreateFolder` (v0.2) is the extended node — it gained a `parentFolder` input for nested folders.

### File

| Node | Description |
|---|---|
| `Excel.ReadFromFile` | Reads an .xlsx worksheet into rows + headers (dates arrive as Excel serial numbers; .xls is not supported). |
| `Excel.WriteToFile` | Writes rows (+ optional headers) to an .xlsx worksheet; append adds a sheet to an existing workbook (foreign styles/formulas are not preserved). |
| `Snapshot.Diff` | Diffs two GUID-keyed dictionaries: added/removed/changed keys (values compared by JSON equality; nested dictionary key order is ignored). |
| `XML.Parse` | Parses XML into dictionaries, lists and strings (attributes as "@name", repeated elements as lists, mixed text as "#text"). |

### List

| Node | Description |
|---|---|
| `Table.JoinByKey` | Joins spreadsheet rows to a key list: one matched row per key (null when unmatched), plus the keys that matched nothing. |

### Geometry

| Node | Description |
|---|---|
| `BoundingBox.Contains` | Tests whether a point lies inside a bounding box (points on the boundary count as inside). |
| `Point.Translate` | Offsets a point by a vector, returning a new point. |

### Navisworks.Properties

| Node | Description |
|---|---|
| `Properties.CustomTabs` | The user-defined property tabs on an item (discovery/QA before SetCustom or RemoveCustomTab). Built-in source-file categories are not listed — use Properties.Categories for those. |
| `Properties.RemoveCustomTab` | Removes a user-defined property tab from items. Items without the tab are skipped (see removedCount) — safe for clean re-runs of SetCustom graphs. |
| `Properties.RenameCustomTab` | Renames a user-defined property tab in place (same properties, same internal name — search sets targeting the tab stay valid). Items without the tab are skipped. |
| `Properties.SetCustom` | Writes a user-defined property tab onto items — values are searchable, schedulable, and travel with the NWF/NWD (source files are never modified). Merge keeps existing same-tab properties; new values win on name collisions. |

### Navisworks.Transform

| Node | Description |
|---|---|
| `ModelItem.GetTransform` | Reads an item's current (active) transform: origin = its translation (a practical base point), matrix = 16 numbers row-major (feed ModelItem.SetTransform to round-trip), hasOverride = whether a permanent transform override is applied. |
| `ModelItem.ResetTransform` | Removes permanent transform overrides, restoring items to their original position. With no items wired (or an empty list) every transform override in the document is reset. |
| `ModelItem.RotateAboutAxis` | Rotates model items by an angle (degrees) about an axis through a point. A permanent override: undoable, saved in the NWF, removed by ModelItem.ResetTransform. Re-runs accumulate. |
| `ModelItem.SetTransform` | Sets the permanent transform override of model items to an absolute 4×4 matrix (16 numbers, row-major, translation at indices 3/7/11) — mirror-place or scale-in-place for power users. Unlike Translate/RotateAboutAxis this REPLACES any earlier override; re-runs are idempotent. |
| `ModelItem.Translate` | Moves model items by a vector, in document units (chain Units.Convert for meters/feet). A permanent override: undoable, saved in the NWF, removed by ModelItem.ResetTransform. Re-runs accumulate — each run moves the items again. |

### Navisworks.Geometry

| Node | Description |
|---|---|
| `Distance.BetweenItems` | Shortest distance between two selections, with the closest (witness) point on each side. method "mesh" = exact surface-to-surface via the Clash engine (can be slow on very large selections); "bbox" = fast bounding-box approximation. Document units — chain Units.Convert. |

### Navisworks.Document

| Node | Description |
|---|---|
| `Document.AppendFiles` | Appends design files to the document — Directory.GetFiles → Document.AppendFiles → Export.NWD is the Navisworks Batch Utility in three nodes. Cached model items from earlier runs are invalidated. |
| `Document.Merge` | Merges another Navisworks file into the document with duplicate resolution — pulls a colleague's review artifacts (sets, viewpoints, comments) into yours. Not a model-diff tool. Cached model items from earlier runs are invalidated. |
| `Document.Open` | Opens a file into the document, REPLACING its current contents (the headless batch driver). Every model item from before the open is invalidated — re-query items downstream of this node. |
| `Document.Refresh` | Refreshes every linked/appended file from disk — Home > Refresh, scriptable. Returns true when anything was updated. Cached model items from earlier runs are invalidated. |

### Navisworks.Model

| Node | Description |
|---|---|
| `Model.Remove` | Removes a WHOLE appended source model from the document (accepts a Model, a 0-based index, or a file name). No API deletes individual elements — hide them and publish an NWD instead. Returns false when Navisworks refuses the removal. Cached model items from earlier runs are invalidated. |

### Navisworks.ModelItem

| Node | Description |
|---|---|
| `ModelItem.ReferencePoints` | Candidate base/reference points of an item, in world document units. Navisworks has NO insertion-point API — bboxCenter/bboxMin come from the bounding box; localOrigin is the item's local-frame origin (COM fragment transform), which approximates the source insertion point for many formats (null when unavailable, e.g. non-geometry items or outside a live session). Source properties (Revit "Location" etc.) remain readable via Properties.Value. |
| `ModelItem.SourceInfo` | One-node answer to "which file did this element come from and what is it": the source file name, the Item-tab Type (falling back to the item's class name), and the owning appended model. |

### Navisworks.SelectionSets

| Node | Description |
|---|---|
| `SelectionSet.MoveToFolder` | Moves a saved selection or search set into a folder (appended at the end). A set already in the folder is left alone, so re-runs are clean. |
| `SelectionSet.Rename` | Renames a saved selection or search set (accepts the set or its current name; searches folders too). Batch-rename via lacing. |
| `SelectionSets.CreateFolder` | Creates a folder in the Sets window, optionally nested under a parent folder. An existing same-named folder in that location is reused, so re-runs are clean. |

### Navisworks.Viewpoints

| Node | Description |
|---|---|
| `SavedViewpoint.Folder` | The folder containing a saved viewpoint: its path as "A/B" ("" for top-level viewpoints) and the folder itself — drives folder-based status workflows. |
| `SavedViewpoint.MoveToFolder` | Moves a saved viewpoint into a folder (appended at the end). A viewpoint already in the folder is left alone, so re-runs are clean. |
| `SavedViewpoint.Rename` | Renames a saved viewpoint (accepts the viewpoint or its current name; searches folders too). Batch-rename via lacing. |
| `Viewpoint.SetSectionBox` | Applies a section box around a region on the current view (Sectioning > Box, scriptable) — chain ModelItem.BoundingBox for the clash-viewpoint close-up look. enabled=false turns sectioning off. |
| `Viewpoints.CreateFolder` | Creates a folder in the Saved Viewpoints window, optionally nested under a parent folder. An existing same-named folder in that location is reused, so re-runs are clean. |

### Navisworks.Comments

| Node | Description |
|---|---|
| `SavedItem.AddComment` | Adds a comment to a saved viewpoint, selection/search set or folder — the Review-tab Comments feature, scriptable. For clash results use ClashResult.AddComment. |
| `SavedItem.ClearComments` | Deletes every comment on a saved viewpoint, selection/search set or folder (replace-all with an empty thread). Rebuild the thread afterwards with SavedItem.AddComment. |
| `SavedItem.Comments` | The comment thread on any saved item (viewpoint, set, folder, clash test): bodies, authors, statuses and creation dates, index-aligned. |

### Navisworks.Clash

| Node | Description |
|---|---|
| `Clash.CompareSnapshots` | Diffs two clash snapshots: clashes NEW since the baseline, clashes RESOLVED (disappeared), and clashes PERSISTING in both (with their previous status) — the weekly delta report no plugin does via live API. Pure file IO: needs no open model. |
| `Clash.GroupResultsByGridIntersection` | Groups a test's results by the model's own grid: each group is named after the nearest grid intersection and level (e.g. "B-3 : Level 2"). Requires a document with grids (Revit/IFC sources). |
| `Clash.GroupResultsByStatus` | Groups a test's results by status (New/Active/Reviewed/Approved/Resolved) — one triage bucket per status in Clash Detective. |
| `Clash.SnapshotToFile` | Saves a clash-run snapshot (per result: test, item identities, status, distance, clash point) as JSON — one half of the between-runs delta report. Items are identified by InstanceGuid when available, else by their tree path. |
| `Clash.SummaryTable` | Per-test clash counts by status (test × Total/New/Active/Reviewed/Approved/Resolved) — the clash summary matrix, ready for CSV.WriteToFile or Excel.WriteToFile. |
| `ClashResult.AddComment` | Appends a comment to a clash result or group — review notes in bulk, and the sync-back half of BCF round trips. |
| `ClashResult.Comments` | The comment thread of a clash result or group: texts, authors, statuses and dates, index-aligned — feeds reports and BCF export. |
| `ClashResult.Rename` | Renames a clash result or result group — with lacing and String nodes this is batch renaming ("Clash1" → "Pipe vs Duct L02-B3"). |
| `ClashTest.Rename` | Renames a clash test — wire a test or its current name. Batch-rename the whole matrix via lacing. |

### Navisworks.Exchange

| Node | Description |
|---|---|
| `BCF.ExportIssues` | Exports clash results (or saved viewpoints) as BCF 2.1 issues (.bcfzip: markup, camera viewpoint, component GUIDs, snapshot) — the vendor-neutral bridge into BIMcollab / Konekt / Revizto / ACC. Components use the IFC GlobalId when present, else the InstanceGuid (lossy for non-IFC sources). Cameras are written in meters per the BCF convention. |
| `BCF.ImportIssues` | Reads a BCF 2.0/2.1 package: per topic title/status/description/comments/component GUIDs/camera, plus the model items each topic's components resolve to (matched by IFC GlobalId, then InstanceGuid). Optionally applies one topic's camera to the current view — feeds ClashResult.SetStatus and Selection.SetCurrent for the issue-sync return leg. |

### Navisworks.Grids

| Node | Description |
|---|---|
| `Grids.ClosestIntersection` | The grid intersection and level nearest to any point — ready-made "B-3 : Level 2" location labels for clash naming, reports and zone tagging. Document units. |
| `Grids.Intersections` | All grid intersections of the active grid system, per level — names like "A-1" with positions in document units. The API only answers closest-intersection queries, so intersections are discovered by sampling the model's bounding box and completing the line lattice; raise samples if a very dense grid comes back incomplete. |
| `Grids.Levels` | The model's own levels from the active grid system — names and elevations (document units) without hand-typed elevation lists. Grid data comes from source models (e.g. Revit/IFC) and is read-only. |

### Navisworks.Takeoff

| Node | Description |
|---|---|
| `Zone.AssignByVolumes` | Tags each target with the name of the zone volume containing its bounding-box center — the iConstruct Zone Tool as one node. The first zone (in list order) that contains an item wins; items inside no zone are left untouched. Written as a searchable user property tab (persists in NWF/NWD only). |

### Navisworks.TimeLiner

| Node | Description |
|---|---|
| `TimeLiner.AutoAttachByProperty` | For every TimeLiner task (subtasks included), finds all items whose property value equals the task name and attaches them — the UI's "Auto-Attach Using Rules", scriptable. Replaces each matched task's existing attachment; tasks with no matching items are left untouched and reported in unmatchedTasks. |

## Reference workflows unlocked

- **Spreadsheet round trip** — `Excel.ReadFromFile → Table.JoinByKey → Properties.SetCustom` (and back out with `Properties.AsDictionary → Excel.WriteToFile`).
- **Weekly clash delta + BCF** — `Clash.RunAllTests → Clash.SnapshotToFile`; next run `Clash.CompareSnapshots → BCF.ExportIssues`; return leg `BCF.ImportIssues → ClashResult.SetStatus`.
- **Batch Utility** — `Directory.GetFiles → Document.AppendFiles → Export.NWD`, headless via the CLI.
- **Model compare** — snapshot `Properties.AsDictionary` keyed by `InstanceGuid` per version, then `Snapshot.Diff` → added/removed/changed → color. (The native Compare command has no API hook; this documented graph is the replacement.)

## Deliberately out of scope (and why)

- **Autodesk Quantification takeoff database** — Autodesk ships no API for it. Use `Takeoff.SumPropertyByGroup` + `Excel.WriteToFile` for workbook-shaped deliverables.
- **Native IFC/DWG export** — no export API exists in Navisworks 2024.
- **Direct BIMcollab / Konekt / Revizto / ACC cloud sync** — proprietary REST only; BCF 2.1 files are the bridge they all accept.
- **Deleting individual elements** — the API removes whole appended files only (`Model.Remove`); hide + publish NWD for anything finer.
- **Temporary transforms** — the 2024 API only offers permanent (undoable) overrides.
- **.xls (legacy BIFF), PDF reports, Appearance Profiler .dat** — use .xlsx and the HTML/CSV reports.

## Notes & known limitations

- `Excel.WriteToFile` with `append` re-emits the workbook: sheets written by other tools keep their data but lose styles/formulas. Dates are written/read as Excel serial numbers.
- `ModelItem.Translate` / `RotateAboutAxis` compose with the current override, so **re-runs accumulate**; `ModelItem.SetTransform` is absolute and idempotent; `ModelItem.ResetTransform` is the undo path.
- `Document.Open` / `AppendFiles` / `Refresh` / `Merge` / `Model.Remove` invalidate every previously cached ModelItem — re-query items downstream of these nodes.
- BCF component references use the IFC GlobalId property when present, else the InstanceGuid (lossy for non-IFC sources).
- `Grids.Intersections` discovers intersections by sampling (the 2024 API has no enumerable intersection collection) — raise the `samples` input if a very dense grid comes back incomplete.
- A handful of behaviors await the in-Navisworks smoke pass on Windows (documented as RUNTIME-CHECK in the source): custom-tab index overwrite semantics, result/group rename reflected in the Clash Detective UI, transform override replace-vs-compose, `Model.Remove` on the first/last model, BCF orthographic camera height semantics, section box surviving viewpoint copy, and TimeLiner explicit-item attachment persistence.

## Under the hood

- `Dyncamelo.Navisworks` gained a `System.IO.Compression` framework reference (net48, no NuGet) for BCF/xlsx zips.
- New internals: `XlsxLite` (OPC xlsx reader/writer), `ComBridge` (COM property-tab writer), `SavedItemTreeHelpers` (stored-copy re-fetch discipline for AddCopy semantics), `BcfDocument` (BCF 2.1 model + camera math), `ClashSnapshotFile` (snapshot JSON + delta matcher).
- The duplicate v0.2 `SelectionSets.CreateFolder` definition was removed in favor of the extended one (same node name, superset signature).
- Version bumped to **0.3.0**; all suites green: Core 113, Nodes 304, Integration 18, plus the CLI smoke run over every sample graph.
