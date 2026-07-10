# Navisworks Plugin-Ecosystem Survey → Dyncamelo v0.3 Node Gaps

Method: surveyed the feature sets of the leading commercial/free Navisworks add-ons (vendor docs, App Store listings, help centers, GitHub), mapped each plugin workflow onto the current Dyncamelo library (docs/NODE_LIBRARY.md, docs/WHATS_NEW_0.2.md, 203 shipped nodes), and extracted the missing nodes. API-route claims are anchored to docs/research/navisworks-api-cookbook.md where verified (noted "cookbook-verified"); the rest are flagged VERIFY for the API-verification teammate (MetadataLoadContext against Speckle.Navisworks.API 2024).

Headline finding: **Dyncamelo v0.2 already replicates a surprising share of the paid ecosystem** — clash auto-grouping (Group Clashes/Clash Sloth's core feature), clash HTML/CSV reports with images (Clash Sloth/Clashtrix), bulk search-set generation (iConstruct-style), color-coding with legends (Appearance Profiler/iConstruct Color Code), QTO rollups, 4D set-attachment (Synchro-lite), batch screenshots. The recurring gaps cluster into **five capability families** that unlock most remaining plugin workflows: (1) **Excel round-trip**, (2) **write custom properties via COM**, (3) **BCF/issue exchange**, (4) **rename/organize/annotate saved items (sets, viewpoints, clash tests) + comments**, (5) **document/file lifecycle (open/append/refresh) + transforms**. These same families cover 10 of the 11 user-wishlist items.

---

## 1. Plugin-by-plugin feature analysis

### 1.1 iConstruct (Hexagon) — the benchmark commercial suite (~35 tools)

**a) SmartProperties / Integrator** — user builds a *custom property tab* by combining/reformatting existing properties (concatenation, formulas, parent-property lookup, Excel-formula parsing since 2022.1), written onto items so the data travels with the NWD and is searchable/schedulable.
- *Graph version*: `Search.* / ModelItem.GeometryLeaves → Properties.Value (several) → String.Concat/If/Math → [MISSING write-back]`. Everything up to the write exists — Dyncamelo is literally a formula engine.
- *Missing*: **Properties.SetCustom** (write a user tab via `ComApiBridge.State` → `InwGUIPropertyNode2.SetUserDefined` — the ComApiBridge entry points are cookbook-verified; the exact `SetUserDefined(index, tabName, internalName, attributes)` call is VERIFY item #8 on the wishlist), plus **Properties.RemoveCustomTab**. This one node converts ~6 iConstruct tools into graphs.

**b) Data Links** — join external XLS/MDB/ODBC rows to model items by a key property; values appear as a property tab in Navisworks.
- *Graph version*: `Excel.ReadFromFile [MISSING] → keys column → match against Properties.Value per item [today: List.IndexOf + List.GetItemAtIndex, clumsy] → Properties.SetCustom [MISSING]`.
- *Missing*: **Excel.ReadFromFile**, **Table.JoinByKey** (convenience), **Properties.SetCustom**. (Wishlist #11: xlsx read without new deps is feasible — .xlsx is a zip of SpreadsheetML XML; `System.IO.Compression.ZipArchive` + `XmlReader` on `xl/worksheets/sheet1.xml` + `xl/sharedStrings.xml` handles the read; writing needs only `[Content_Types].xml`, workbook, one sheet, sharedStrings — well-trodden, no NPOI/EPPlus needed. .xls (BIFF) should be declared out of scope.)

**c) Zone Tool** — assigns zone/room/area names onto objects by testing geometry against nominated zone volumes; used for room data sheets, work-area scheduling.
- *Graph version*: `zone items → ModelItem.BoundingBox`; target items → `BoundingBox` center; `[MISSING BoundingBox.Contains] → List.FilterByBoolMask → Properties.SetCustom("Zones","Zone", name)`.
- *Missing*: **BoundingBox.Contains(point)** (pure geometry, trivial), **Properties.SetCustom**; optional composite **Zone.AssignByVolumes**.

**d) Export Data / Quick Reports** — templated Excel/Access/PDF reports of model data.
- Covered by `Export.PropertyReportCsv` (catalog Beta — should ship) + missing **Excel.WriteToFile** (multi-sheet, headers). PDF is P3/skip.

**e) Appearance profiles & color legend** — covered by `Appearance.ColorByValues` + `SelectionSets.BulkByPropertyValues`. Reading/writing native Appearance Profiler `.dat` files: P3 curiosity only.

**f) IFC/DWG export from Navisworks** — iConstruct ships its own geometry writers; the Navisworks 2024 .NET/COM API has **no IFC/DWG export** → *impossible* without a from-scratch mesh serializer via COM fragments; recommend explicit out-of-scope note (COM `InwOaFragment3` mesh access does enable a rough **glTF/OBJ export** later — App Store shows demand — P3).

### 1.2 BIMcollab BCF Manager / Newforma Konekt / Revizto — issue & BCF sync

The common workflow (per BIMcollab help center, Konekt "Clashes to Issues", Revizto "clash sync"): clash groups/results or saved viewpoints → issues with title/status/assignee/comments/snapshot/camera/component references → pushed to a cloud tracker or exchanged as **BCF files**; statuses sync back to Clash Detective on the next round.
- All three vendors gate the cloud legs behind proprietary REST APIs; the **vendor-neutral interchange is BCF 2.1** (zip of per-issue folders: `markup.bcf` XML, `viewpoint.bcfv` camera+components, `snapshot.png`) — confirming the wishlist #9 expectation: no public Coordination/Issues API; **BCF export/import is the right substitute**.
- *Graph version*: `ClashTest.Results → Clash.GroupResultsBySameItem → ClashResult.Info/.Items/.Viewpoint/.SaveImage → [MISSING BCF.ExportIssues]`. Ingredients (camera via `TestsViewpointForResult`, image via `TestsImageForResult`, item GUIDs via `ModelItem.InstanceGuid` for IfcGuid component references) all exist.
- *Missing*: **BCF.ExportIssues** (results/viewpoints + statuses/comments → .bcfzip via System.IO.Compression — no new deps), **BCF.ImportIssues** (read markup + return topic dicts, match components back by GUID → drive `ClashResult.SetStatus`/`Selection.SetCurrent`), **ClashResult.AddComment** (round #2 sync-back; `DocumentClashTests.TestsEditResultComments(IClashResult, CommentCollection)` is cookbook-verified — wishlist #10 clash half is *feasible*).
- Comments on viewpoints/sets (wishlist #10 second half): `SavedItem.Comments` reads are in the API; a generic .NET **write** route for non-clash SavedItems is uncertain (likely copy-edit + ReplaceWithCopy, or `Document.Comments` part) — VERIFY; mark *partial* until proven.

### 1.3 Group Clashes (bim42, free) / Clash Sloth / Clashtrix — clash productivity

- **Grouping rules** (by item, by level, by grid intersection, by proximity, by status/assignment; chained two-level rules): Dyncamelo v0.2 ships `Clash.GroupResultsBySameItem/ByProximity/ByLevel`. Gaps: **grouping by grid intersection** and level grouping *without hand-typed elevations* → needs **Grids.Levels / Grids.Intersections** reading `Document.Grids.ActiveSystem` (`Autodesk.Navisworks.Api.DocumentParts.DocumentGrids` exists since 2016 — VERIFY exact members in 2024 dump), plus **Clash.GroupResultsByStatus** and a **chained/segmented re-group** option (group within existing groups / keep-existing-groups flag à la Group Clashes v2).
- **Batch rename results** (Clashtrix "Smart Results": `Clash1` → meaningful names from items/levels): missing **ClashResult.Rename** and **ClashTest.Rename** (wishlist #1; route: detached `CreateCopy()` + set `DisplayName` + `TestsEditTestFromCopy` — same cookbook-verified commit pattern v0.2 grouping already uses; a direct `TestsEditDisplayName` may exist — VERIFY).
- **Excel clash matrix + summary tables** (Clashtrix Summary Matrix: tests × disciplines with active counts, colored headers): achievable today into CSV via `Clash.Tests → ClashTest.Info → List.GroupByKey → CSV.WriteToFile`; add **Excel.WriteToFile** for the expected .xlsx artifact and a convenience **Clash.SummaryTable** (per test: counts by status) so it's one node instead of fifteen.
- **Delta between runs** (Clash Sloth's killer report: new / persisting / resolved since last run): nothing in the ecosystem does this via live API — they diff saved reports. Node pair: **Clash.SnapshotToFile** (per result: test, GUID-pair identity, status, distance → JSON) + **Clash.CompareSnapshots** (old path, new path → new/resolved/persisting lists). Pure file I/O + existing identity fields; high user value, low risk.
- **Auto-update status by property criteria** (Clashtrix Auto Update): fully covered today (`ClashTest.Results → ClashResult.Info/Items → Properties.Value → mask → ClashResult.SetStatus`). Documentation win, not code.
- **Viewpoint generation with section boxes/context transparency** (App Store "Clash Viewpoints" style): `Viewpoints.FromClashResults` covers the base; **SavedViewpoint sectioning** (apply a section box around the clash) is missing — `Viewpoint` clipping planes API (`Viewpoint.SetClippingPlanes`/COM `InwOpClipPlaneSet`) — VERIFY; P2 **Viewpoint.SetSectionBox**.
- **Sync statuses from viewpoint subfolders** (Clashtrix): needs **SavedViewpoint.Folder** (read parent folder path) — trivial walk of `SavedItem.Parent`.

### 1.4 BIMOne Import/Export Excel + DiRoots-style round-tripping

Pattern (Revit-side, but the #1 App Store theme for Navisworks too — Properties+, BIMCAVE "Export Properties to Excel", Navis-SystemPropertyExporter): dump selected items × selected properties to Excel with a stable key column, edit values offline, re-import; read-only values grayed.
- *Navisworks twist*: native properties are **read-only**; re-import must land in a **user-defined tab** (exactly BulkProps-Navisworks-Plugin on GitHub — proof the COM route works).
- *Graph*: export = `items → Properties.Value (laced) → Excel.WriteToFile [MISSING]` with `ModelItem.InstanceGuid` key column; import = `Excel.ReadFromFile [MISSING] → Search.ByPropertyValue(GUID) or Table.JoinByKey [MISSING] → Properties.SetCustom [MISSING]`.
- Missing set identical to iConstruct Data Links → **the Excel pair + Properties.SetCustom is the single highest-leverage v0.3 investment** (unlocks iConstruct Integrator/Data Links/Export Data, BIMOne round-trip, COBie, Clashtrix xlsx artifacts, wishlist #8 + #11).

### 1.5 Autodesk Quantification workbook

Takeoff DB (catalogs, WBS, virtual takeoff) has **no public API** (long-standing forum answer) → *impossible* to drive directly. The ecosystem substitute is property-based takeoff, which Dyncamelo already does better (`Takeoff.SumPropertyByGroup`, `BoundingBox.Size`, `Units.Convert`). Gap: **Excel.WriteToFile** for the workbook-shaped deliverable; optional **Takeoff.PivotTable** convenience (rows=group values, columns=metric list). Mark Quantification integration itself: impossible; workflow replication: covered + Excel.

### 1.6 Navisworks Batch Utility (+ "Batch Utility Enhanced")

Workflow: list of design files → append into one NWF/NWD, or convert each to NWD; schedulable via Windows Scheduler/CLI.
- Dyncamelo has a **headless CLI** — the natural Batch Utility successor — plus `Directory.GetFiles`, `Document.Save`, `Export.NWD`. Missing the document lifecycle: **Document.Open(path)**, **Document.AppendFiles(paths)** (`Document.OpenFile`, `Document.AppendFile` — standard API, VERIFY exact overloads), **Document.Clear/New**. With lacing: `Directory.GetFiles("*.rvt|*.dwg|*.nwc") → Document.AppendFiles → Export.NWD` = Batch Utility in four nodes, schedulable via `dyncamelo run` + Task Scheduler.
- **Refresh linked files** (wishlist #6): no direct .NET `Refresh()`; the honest route is re-open the NWF (`Document.OpenFile` re-reads changed NWCs) or COM/plugin-command invocation of the UI Refresh (`Application.ExecuteAddInPlugin` / built-in command id) — VERIFY; ship **Document.RefreshOrReopen** as *partial*.

### 1.7 Synchro-style 4D linking

Synchro/Fuzor-class links: import schedule (CSV/P6/MSP XML), auto-attach tasks to geometry by rules (set name, property match), simulate, export media.
- v0.2 already covers the core: `TimeLiner.AddTask` (Beta→ship), `TimelinerTask.AttachSet`, `TimelinerTask.SetDates`, CSV import chain.
- Missing: **TimeLiner.AutoAttachByProperty** (rule: task name == property value → attach search; replicates the UI "Auto-Attach Using Rules"; TimelinerSelection from Search — VERIFY route), **P6/MSP XML import** = just `Text.ReadFromFile` + XML parse → missing generic **XML.Parse** node (System.Xml, no deps), **Export.SimulationImages** (step the simulation clock and render frames: `DocumentTimeliner.SimulationSetCurrentTime`-style — VERIFY; P3).

### 1.8 Zone/grid/level search-set generators

Free tools that generate one search/selection set per level, per grid band, per zone. `SelectionSets.BulkByPropertyValues` covers property-driven generation. Missing: **Grids.Levels/Grids.Intersections** (drive per-level sets from the model's own grid system instead of a typed list; also upgrades `Clash.GroupResultsByLevel`), **SelectionSets.MoveToFolder** + nested folder support (wishlist #1: `CreateFolder` exists but top-level only; need parent-folder input + **move** via `DocumentSelectionSets.Move(oldParent, oldIndex, newParent, newIndex)` — VERIFY signature).

### 1.9 Model comparison tools

Native Compare (Home > Compare) diffs two selected items/files; no public API for the Compare command. Ecosystem substitute: GUID-keyed snapshot diffing.
- *Graph*: `run A: items → InstanceGuid + Properties.AsDictionary → JSON.WriteToFile`; later `run B → [MISSING Model.CompareSnapshots]` → added/removed/changed GUID lists → `Search`/color. A generic **Snapshot.Diff** (two JSON dicts keyed by GUID) covers both model compare and clash delta plumbing. P2.

### 1.10 Viewpoint/image batch exporters

App Store recurring theme (batch viewpoint images, viewpoint cleanup, folder ops). Mostly covered (`Viewpoints.All → SavedViewpoint.Apply → Export.ViewpointImage` laced; `SavedViewpoint.Delete`). Missing: **Viewpoints.CreateFolder** + **SavedViewpoint.MoveToFolder** (wishlist #1; `FolderItem` + `DocumentSavedViewpoints.AddCopy(group,…)`/`Move` — VERIFY), **SavedViewpoint.Rename**, **SavedViewpoint.Folder** (read location), **Viewpoints.ExportAllImages** convenience (folder-named files).

### 1.11 COBie for Navisworks (Autodesk interoperability tool)

Template-driven export of COBie-formatted **multi-sheet xlsx** from federated model data; re-import of supply-chain-filled sheets.
- *Graph*: property extraction exists; needs **Excel.WriteToFile with multiple sheets** (Contact/Facility/Floor/Space/Type/Component…) and re-import = Excel.Read + Properties.SetCustom. A **COBie.Export** composite is P3 sugar; the primitives are the same Excel/custom-property family.

### 1.12 App Store sweep — remaining recurring themes

Property viewers (Properties+) → covered; selection-tree rebuilders/model splitters → *partial* (needs open/append + hide+publish trick); DXF/glTF exporters → P3 COM-mesh project; **Transform/move-rotate tools** ("Transformer"-style, and wishlist #2/#3): `Models.OverridePermanentTransform(items, Transform3D, bool)` / `ResetPermanentTransform` are **cookbook-verified** → feasible: **ModelItem.Translate / RotateAboutAxis / SetTransform / ResetTransform**, **ModelItem.Transform (read)** for base points (plus `PropertyCategoryNames.Transform` category read); "move up 1 m" = `ModelItem.BoundingBox → BoundingBox.Center → Vector.ByCoordinates(0,0,1) → Units.Convert → ModelItem.Translate`. Deleting tree elements (wishlist #5): no public .NET delete of arbitrary items; whole-model removal — `DocumentModels` remove — VERIFY (expectation: 2024 API lacks even that; COM `InwOpState.DeleteSelection` existed in old Roamer — likely dead); ship **Model.Remove** as *partial/likely-impossible*, document `Appearance.Hide` + republish-NWD as the workflow. **True mesh distance** (wishlist #7): bbox distance is pure math (**Distance.BetweenBoxes**); exact distance via COM fragment primitives (`InwOaFragment3.GenerateSimplePrimitives` vertex harvesting + closest-pair) → **Distance.BetweenItems** with `method: "bbox"|"mesh"` — feasible but perf-bounded; sample/voxel cap needed.

---

## 2. Workflow → missing-nodes matrix

| # | Plugin workflow | Already covered by | Missing nodes |
|---|---|---|---|
| W1 | iConstruct SmartProperties/Integrator (computed property tabs) | Properties.*, String.*, If, lacing | Properties.SetCustom, Properties.RemoveCustomTab |
| W2 | iConstruct Data Links / BIMOne-DiRoots Excel round-trip | Properties.Value, InstanceGuid, Search.ByPropertyValue | Excel.ReadFromFile, Excel.WriteToFile, Table.JoinByKey, Properties.SetCustom |
| W3 | iConstruct Zone Tool | BoundingBox.*, FilterByBoolMask | BoundingBox.Contains, Properties.SetCustom, (Zone.AssignByVolumes) |
| W4 | iConstruct Export Data / property reports | Export.PropertyReportCsv (ship Beta) | Excel.WriteToFile |
| W5 | BIMcollab/Konekt/Revizto issue sync | clash read/edit/status/image/viewpoint nodes | BCF.ExportIssues, BCF.ImportIssues, ClashResult.AddComment |
| W6 | Clash Sloth/Clashtrix reports + matrix | Export.ClashReportCsv/Html, ClashResult.SaveImage | Excel.WriteToFile, Clash.SummaryTable |
| W7 | Clash delta between runs (Clash Sloth) | InstanceGuid, ClashResult.Info | Clash.SnapshotToFile, Clash.CompareSnapshots (or generic Snapshot.Diff) |
| W8 | Group Clashes chained/grid grouping | 3 grouping nodes (v0.2) | Grids.Levels, Grids.Intersections, Clash.GroupResultsByStatus, regroup-within-groups option |
| W9 | Clashtrix Smart Results rename; wishlist renames | — | ClashTest.Rename, ClashResult.Rename, SavedViewpoint.Rename, SelectionSet.Rename |
| W10 | Viewpoint/set organization (folders) | SelectionSets.CreateFolder (top-level) | Viewpoints.CreateFolder, SavedViewpoint.MoveToFolder, SelectionSets.MoveToFolder, SavedViewpoint.Folder, nested-folder parent input |
| W11 | Batch Utility (append/convert/schedule) | CLI, Directory.GetFiles, Export.NWD, Document.Save | Document.Open, Document.AppendFiles, Document.RefreshOrReopen (partial) |
| W12 | Synchro-style 4D auto-link | TimeLiner v0.2 nodes | TimeLiner.AutoAttachByProperty, XML.Parse, (Export.SimulationImages P3) |
| W13 | Model comparison | InstanceGuid, Properties.AsDictionary, JSON.* | Snapshot.Diff |
| W14 | Viewpoint batch export | Apply+ViewpointImage lacing | Viewpoints.CreateFolder, SavedViewpoint.Rename/Folder |
| W15 | COBie export/import | property chain | Excel.WriteToFile (multi-sheet), Excel.ReadFromFile, Properties.SetCustom, (COBie.Export P3) |
| W16 | Transform tools; wishlist #2/#3 | BoundingBox.Center, Vector, Units.Convert | ModelItem.Translate, ModelItem.RotateAboutAxis, ModelItem.SetTransform/ResetTransform, ModelItem.Transform (read) |
| W17 | Clearance/distance checks; wishlist #7 | Point.DistanceTo, BoundingBox.Intersects | Distance.BetweenItems (bbox + COM mesh) |
| W18 | Quantification / IFC export / Issues-cloud APIs | — | **Impossible** (no public API) — replicate via W4/W2, W5(BCF); document explicitly |
| W19 | Element source file + type; wishlist #4 | ModelItem.Ancestors + Model.FileName, ClassInfo, Properties.Value("Item","Source File"/"Type") | ModelItem.SourceInfo (convenience) |
| W20 | Delete from tree; wishlist #5 | Appearance.Hide fallback | Model.Remove (partial — VERIFY; expect whole-model only or impossible) |

---

## 3. Deduplicated missing-node list

Priorities: P1 = unlocks multiple flagship workflows or a wishlist item with a verified route; P2 = strong single-workflow value or needs API verification; P3 = convenience/long-tail.

**File / data plumbing (Dyncamelo.Nodes, no Navisworks dependency)**
1. **Excel.ReadFromFile** — File — in: path, sheet:string="" (first), hasHeaders=true; out: rows:List<List<object>>, headers, sheetNames. xlsx via ZipArchive+XmlReader (no new deps). Unlocks W2/W6/W12/W15, wishlist #11. **P1**
2. **Excel.WriteToFile** — File — in: path, rows, headers=null, sheet="Sheet1", append:bool=false (adds sheet); out: path. Minimal SpreadsheetML writer. Unlocks W2/W4/W6/W15, Quantification-shaped deliverables. **P1**
3. **Table.JoinByKey** — List — in: rows, headers, keys:List<object>, keyColumn:string; out: matchedRows (parallel to keys), unmatchedKeys. Convenience for W2/W15, wishlist #11. **P2**
4. **XML.Parse** — File — in: xml:string; out: value (nested dicts/lists). System.Xml. Unlocks W12 (MSP XML), search-set XML, BCF import internals. **P2**
5. **Snapshot.Diff** — File — in: oldDict, newDict (GUID-keyed); out: addedKeys, removedKeys, changedKeys. Unlocks W13 and underpins W7. **P2**
6. **BoundingBox.Contains** — Geometry — in: boundingBox, point; out: contains:bool. Unlocks W3. **P2**

**Properties (Navisworks, COM bridge)**
7. **Properties.SetCustom** — Navisworks.Properties — in: modelItems, tabName:string, names:List<string>, values:List<object>, merge:bool=true; out: items (pass-through). Route: `ComApiBridge.State`+`ToInwOaPath` → `InwGUIPropertyNode2.SetUserDefined` (wishlist #8; VERIFY exact call/order). *The single most valuable new node in v0.3.* Unlocks W1/W2/W3/W15. **P1**
8. **Properties.RemoveCustomTab** — Navisworks.Properties — in: modelItems, tabName; out: items. Same COM route (SetUserDefined with empty vector / index removal — VERIFY). Clean re-runs. **P1**

**Rename / organize / annotate (wishlist #1, #10; W9/W10/W14)**
9. **SelectionSet.Rename** — Navisworks.SelectionSets — in: set-or-name, newName; out: set. Copy-edit + `DocumentSelectionSets.ReplaceWithCopy` or `.Rename` — VERIFY. **P1**
10. **SavedViewpoint.Rename** — Navisworks.Viewpoints — same pattern via DocumentSavedViewpoints. **P1**
11. **ClashTest.Rename** — Navisworks.Clash — detached copy + `TestsEditTestFromCopy` (cookbook-verified commit path) or `TestsEditDisplayName` — VERIFY. **P1**
12. **ClashResult.Rename** — Navisworks.Clash — same commit path (Clashtrix "Smart Results" batch rename via lacing). **P1**
13. **Viewpoints.CreateFolder** — Navisworks.Viewpoints — in: name, parentFolder=null; out: folder. Mirror of SelectionSets.CreateFolder. **P1**
14. **SavedViewpoint.MoveToFolder** / 15. **SelectionSets.MoveToFolder** — in: item, folder; out: item. `Document*.Move(oldParent, oldIdx, newParent, newIdx)` — VERIFY signature. Also extend `SelectionSets.CreateFolder` with a parent input (nested). **P1**
16. **SavedViewpoint.Folder** — read parent-folder path (walk `SavedItem.Parent`). Enables Clashtrix folder-status sync. **P2**
17. **ClashResult.AddComment** — Navisworks.Clash — in: result, body, author=""; out: result. `TestsEditResultComments(IClashResult, CommentCollection)` — cookbook-verified. Also promote catalog-Future **ClashResult.Comments** (read) to ship. **P1**
18. **SavedItem.AddComment / SavedItem.Comments** — generic comments on viewpoints/sets — write route uncertain (VERIFY: copy-edit vs Document.Comments part); mark *partial*. **P2**

**BCF / issues (wishlist #9; W5)**
19. **BCF.ExportIssues** — Navisworks.Exchange — in: results:List<ClashResult> (or viewpoints), filePath, statusMap=default, includeSnapshots=true; out: filePath, topicCount. BCF 2.1 zip via System.IO.Compression; components from InstanceGuid; camera from `TestsViewpointForResult`. **P1**
20. **BCF.ImportIssues** — in: filePath; out: topics:List<Dictionary> (title/status/comments/GUIDs), guids. Feeds `ClashResult.SetStatus`, `Selection.SetCurrent`. **P1**

**Transforms & location (wishlist #2, #3; W16)**
21. **ModelItem.Translate** — Navisworks.Transform — in: modelItems, vector (model units); out: items. `Models.OverridePermanentTransform` (cookbook-verified). **P1**
22. **ModelItem.RotateAboutAxis** — in: modelItems, origin:Point, axis:Vector, degrees; out: items. Compose Transform3D rotation. **P1**
23. **ModelItem.SetTransform** / 24. **ModelItem.ResetTransform** — raw Transform3D set + `ResetPermanentTransform` (cookbook-verified). **P2**
25. **ModelItem.Transform (read)** — out: origin:Point, matrix:List<double>. Item current transform / base point (wishlist #3; fallback: "Transform" property category + BoundingBox.Center). VERIFY read surface. **P2**

**Distance (wishlist #7; W17)**
26. **Distance.BetweenItems** — Navisworks.Geometry — in: itemA, itemB, method:"bbox"|"mesh"="bbox", maxVertices:int=5000; out: distance, pointA, pointB. bbox = interval math (pure); mesh = COM `InwOaFragment3` primitive harvest + closest-pair (perf-capped). **P2** (bbox part P1-easy, mesh P2)

**Document lifecycle (wishlist #5, #6; W11)**
27. **Document.Open** — Navisworks.Document — in: path; out: document. `Document.OpenFile` — VERIFY headless/SetActive constraints. **P1**
28. **Document.AppendFiles** — in: paths:List<string>, document; out: document, models. `Document.AppendFile`. Batch Utility replication with CLI. **P1**
29. **Document.RefreshOrReopen** — wishlist #6 — *partial*: re-open NWF or UI-command invocation — VERIFY. **P2**
30. **Model.Remove** — wishlist #5 — *partial/likely impossible* in 2024 public API — VERIFY `DocumentModels` for remove; else document Hide+`Export.NWD` fallback and mark impossible. **P3**

**Clash & grids (W6/W7/W8)**
31. **Clash.SnapshotToFile** — in: tests=null, path; out: path, resultCount (GUID-pair+status JSON). **P1**
32. **Clash.CompareSnapshots** — in: oldPath, newPath; out: newResults, resolved, persisting (+counts). The Clash Sloth delta report. **P1**
33. **Grids.Levels** — Navisworks.Grids — out: names, elevations. `Document.Grids.ActiveSystem` — VERIFY. Feeds `Clash.GroupResultsByLevel` automatically. **P2**
34. **Grids.Intersections** — out: names, points. Enables grid-intersection grouping/naming. **P2**
35. **Clash.GroupResultsByStatus** — trivial variant of existing grouping commit path. **P3**
36. **Clash.SummaryTable** — in: tests=null; out: rows (test × status counts), headers — feeds CSV/Excel matrix. **P2**
37. **Viewpoint.SetSectionBox** — in: viewpoint/current, boundingBox; out: done. Clipping-plane API — VERIFY. Clash-viewpoint plugins' signature feature. **P2**

**TimeLiner & convenience (W12/W19)**
38. **TimeLiner.AutoAttachByProperty** — in: category, property, taskNameMatch:bool; out: attachedCount, unmatchedTasks. **P2**
39. **ModelItem.SourceInfo** — out: sourceFileName, model (walk Ancestors → Model). Wishlist #4 one-node answer (type covered by ClassInfo/Properties). **P2**
40. **COBie.Export** — composite over Excel.WriteToFile — **P3**. 41. **Export.SimulationImages** — **P3**. 42. **Search-set XML export/import** — VERIFY API — **P3**. 43. **glTF/OBJ mesh export via COM** — **P3**.

**Explicit impossibles to record in the catalog**: Quantification DB automation; native IFC/DWG export; Compare-command API; vendor cloud issue APIs (BIMcollab/Konekt/Revizto — REST, out of process scope; BCF is the bridge); arbitrary tree-item deletion (expected).

**Coverage check vs wishlist**: #1 nodes 9-15 (feasible); #2 nodes 21-24 (feasible, cookbook-verified route); #3 node 25 + existing BoundingBox.Center (partial-to-feasible); #4 node 39 + existing (feasible); #5 node 30 (partial/likely impossible — as user expected); #6 node 29 (partial); #7 node 26 (feasible; mesh = COM); #8 nodes 7-8 (feasible pending COM verification); #9 nodes 19-20 (confirmed: BCF substitute); #10 nodes 17-18 (clash: feasible/verified; viewpoints/sets: verify); #11 nodes 1-3 (feasible, no new deps).

Sources: iConstruct features/user guides (iconstruct.com/features, support.iconstruct.com Data Links, hexagon.com iConstruct MAX), BIMcollab Help Center (helpcenter.bimcollab.com BCF Manager Navisworks articles), Newforma Konekt help (konekt.help.newforma.com Clashes-to-Issues, Clash Status Synchronization), Revizto help (help.revizto.com Syncing Navisworks clashes), Group Clashes (github.com/simonmoreau/GroupClashes, bim42.com), Clashtrix (clashtrix.com/Help, App Store listing), BulkProps (github.com/shaun-wilson/BulkProps-Navisworks-Plugin), TwentyTwo COM custom-property article (twentytwo.space), Autodesk Help (Batch Utility, Appearance Profiler, Compare, Quantification workflow, COBie Explorer via Cadline/ARKANCE), Autodesk App Store Navisworks section (apps.autodesk.com/NAVIS), NVClashAnalytics (github.com/r-y-t-o).
