# Node library audit (v0.43.0)

Scope: every node the catalogue lists (366: 351 zero-touch methods and 15 interactive nodes), plus the
node-group and Watch Image nodes the catalogue generator misses. Everything below was checked in the source, not inferred from names.
Navisworks-side nodes cannot run in CI (no Navisworks), so the Navisworks half is only compile-checked and statically tested;
that is why the plan below prefers *forwarding and hiding* over rewriting.

## Outcome

Done (steps 0–4 below, in the release after v0.43):

* **Mechanisms.** `[NodeDeprecated]` (a retired node keeps loading and running, is hidden from the library, quick search, CLI list and catalogue, and says what to use), `[PortAlias]` (a renamed input or output keeps the wires and values of old graphs), `[NodeChoicesFromEnum]` (a string input with an enum's names as its dropdown), and a report when a file is opened with wires or values that no longer have a port. Documented in EXTENDING §10.
* **Retired (14):** `List.Join`, `ClashTest.ResultsByStatus`, `ClashResult.Comments`, `ClashResult.AddComment` (`SavedItem.AddComment` now also handles clash results), `Search.ByPropertyValue/Contains/Wildcard/Compare` → new `Search.ByProperty` (mode dropdown), `Markup.AddLine/AddArrow/AddEllipse` → new `Markup.AddShape`, `Model.FileName/Units/RootItem` → new `Model.Info`. The catalogue lists them under "Retired nodes".
* **Tidying.** Nine groups of copied helpers now live once; categories merged (Application→Document, Audit/Takeoff→Analysis, Exchange→Export, `Viewpoint.SetSectionBox`→Camera, a new Data category for JSON/XML parsing, `Snapshot.Diff`, `Table.JoinByKey`); `docs/NODE_CATALOG.md` is generated beside `dyncamelo-nodes.json` (now including Watch Image) and CI fails when either is stale.
* **Consistency.** All 74 multi-output nodes declare `[PortKinds]` (a test keeps it so); the String nodes' `str` input is `text` (old files keep their wires through the alias); the case-sensitivity default of each String comparison is stated in its description; the IFC export's `units` is a dropdown.

Not done, and why:

* `SavedItem.Rename / MoveToFolder / Delete` were **not** merged: the typed nodes also accept the item's *name*, and each lives under the tree where people look for it. `SavedItem.AddComment/Comments` already covered every kind, so those did merge.
* `Clash.Status` was kept: it is a reusable, validated constant that can feed several status inputs from one place.
* The "13 nodes end in a done flag" inconsistency is a convention, not a defect, and changing it would change port names and types; it is now a checklist item for new nodes.
* The String comparison *defaults* (`Contains` case-sensitive, `StartsWith/EndsWith` not) are unchanged, because changing a default changes what existing graphs compute; they are documented instead.
* Correction to section 4: four of the five `units` inputs already had a dropdown; only the IFC export's did not.

## 1. What decides how much we can change

| Fact (verified) | Consequence |
|---|---|
| A saved wire and a typed-in value are stored by **port name** (`FromPort`/`ToPort`, input `Name`). A port that no longer exists makes `RestoreConnection` return and the value loop `continue` — **silently**. | Renaming any input or output drops wires in every saved graph, with no message. |
| A zero-touch node's id is `Namespace.Class.Method@paramTypes`; only `[NodeAliases]` can carry an old id forward. | Adding, removing or retyping a parameter needs an alias (v0.4 did this for the Search nodes). |
| `[NodeName]` (display name) and `[NodeCategory]` are not part of the id. | Renaming a node's display name and moving it to another category are free. |
| `[IsVisibleInLibrary(false)]` makes the loader **skip** the method. | There is no "retired but still loadable" state: hiding a node today turns it into a Missing node in old graphs. |
| No port alias exists (`NodeAliasesAttribute` is the only alias attribute). | Merging two nodes whose output names differ (e.g. `comments` vs `bodies`) breaks wires from the retired one. |

So the first step is to add the missing mechanisms (section 5, step 0). Without them only code-level tidying and category moves are safe.

## 2. Duplicates

| Node(s) | Finding | Proposal |
|---|---|---|
| `ClashTest.ResultsByStatus` | Body is `FlattenResults(test)` filtered by status — exactly `ClashTest.Results` → `Clash.FilterByStatus` (same `ParseResultStatuses`). | Retire. |
| `ClashResult.Comments` vs `SavedItem.Comments` | Same loop over `item.Comments`; only the output key differs (`comments` vs `bodies`). `SavedItem.Comments` already documents clash tests. | Retire `ClashResult.Comments` (needs port alias). |
| `ClashResult.AddComment` vs `SavedItem.AddComment` | Same signature (`body`, `status`, `author`, `document`); they differ only in where the comment is written (`TestsData.TestsEditResultComments` vs `SavedViewpoints`/`SelectionSets`). | One `SavedItem.AddComment` that dispatches on `IClashResult`. |
| `ClashTest.Rename`, `ClashResult.Rename`, `SelectionSet.Rename`, `SavedViewpoint.Rename`, `Viewpoints.RenameFolder` | Five renames of the same kind of thing (a `SavedItem`). | One `SavedItem.Rename`. |
| `SavedViewpoint.MoveToFolder` / `SelectionSet.MoveToFolder`; `SavedViewpoint.Delete` / `SelectionSet.Delete` | Mirrored pairs (`ViewpointTreeNodes` / `SelectionSetTreeNodes` are parallel classes). | `SavedItem.MoveToFolder`, `SavedItem.Delete`. |
| `List.Join` | Two-list concatenation; `List.Merge` (multi-input since 0.40) does any number, in wire order. | Retire `List.Join`. |
| `Clash.Status` | A one-output dropdown. Every status input (`SetStatus`, `FilterByStatus`, `ResultsByStatus`) already has the same dropdown. `Clash.Statuses` (the multi-select) is the useful one. | Retire `Clash.Status`, keep `Clash.Statuses`. |
| `ClashResult.Angle` | `ClashResult.Orientation` calls `ClashNodes.Angle` and returns it as `degrees`. | Keep (lighter, used by `FilterByAngle`); say so in the description. |

Not duplicates (checked, keep): `Appearance.*` vs `Appearance.*Temporary` (permanent vs viewpoint-scoped is a safety distinction worth seeing in
the name); the nine `Action.*` nodes (deferred twins of `Appearance.*`/`Camera.*` for `Workflow.ForEach`, by design); `Watch` / `Watch List` /
`Watch Image` (text, indexed rows, picture); `Number` / `Number Slider` / `Integer Slider`; the five `Clash.GroupResultsBy*` (different inputs).

## 3. Overlaps that could be joined (a design choice)

* `Search.ByPropertyValue / ByPropertyContains / ByPropertyWildcard / ByPropertyCompare` → one `Search.ByProperty` with a `mode`
  dropdown. `Clash.FilterByItemProperty` already works that way. `SelectionSet.CreateFromSearch` (equals only) could take the same `mode`.
* `Markup.AddArrow / AddLine / AddEllipse` have identical inputs (`x1,y1,x2,y2,color,thickness`) → `Markup.AddShape(kind)`. Experimental feature, low risk.
* `Model.FileName`, `Model.Units`, `Model.RootItem`, `Models.RootItems`, `Document.Models` → a `Model.Info` getter (one node, four outputs) beside the list nodes.
* List family: `AddItemToFront/AddItemToEnd/Insert`, `FirstItem/LastItem/GetItemAtIndex(-1)`, `Take/Drop/RestOfItems/Slice` overlap, but they mirror Dynamo
  names users already know. Recommendation: keep, except `List.Join`.

## 4. Inconsistencies

* **String defaults and names.** `String.Contains` has `ignoreCase=false`; `StartsWith`/`EndsWith` have `true`. The first input is `str` in
  Contains/Length/Replace/Split/ToNumber and `text` in StartsWith/EndsWith/Substring/ToLower/ToUpper/Trim. Changing a default changes results of
  existing graphs that left it alone, so this needs your decision (section 6).
* **`units`.** Seven nodes take units; five (`FallHazard.EdgeHandrailCheck`, `FallHazard.FloorOpeningMap`, `Proximity.Cluster`, `Clash.FilterByDepth`,
  `Export.ToIfc`) take free text (`"document"`, `"Meters"`, `"Auto"`), while `Units.Convert/ScaleFactor` take the `Units` enum. Three copies of the same
  `ResolveUnitsScale` live in ClashFilterNodes, ClusterNodes and FallHazardNodes. Proposal: one shared resolver and a dropdown on the free-text inputs.
* **"Did it work" outputs.** 13 nodes end in a flag (`done`, `deleted`, `removed`, `cleared`, `updated`); `Appearance.ResetTemporary` returns `done:any` *and*
  `after:any`; `Appearance.ShowAll`/`ResetAll` return a boolean while `Hide/Show/Isolate` pass the items through. Pass-through values chain; flags do not.
* **Untyped outputs.** 280 of 526 outputs are `any`; 74 of the 75 multi-output nodes have *every* output `any`, because `[MultiReturn]` names keys but not
  types, so their sockets are grey and do not suggest compatible nodes. Typing the keys needs no id change.
* **Untyped inputs.** `viewpoint:any` (13 nodes) and `folder:any` (6) accept "the object or its name"; that is deliberate, but the socket loses its colour.
* **Categories.** Eight categories hold one or two nodes: Annotation (Note), Utility (Reroute), Display (Watch, Watch List; Watch Image is the third),
  Navisworks.Application, .Audit, .Exchange, .Takeoff, Portable.ViewpointPackageFile. Misplaced: `Table.JoinByKey` (in List), `Snapshot.Diff` (in File),
  `JSON.Parse/Stringify` and `XML.Parse` (in File although they touch no file), `Viewpoint.SetSectionBox` (in Viewpoints, it acts on the current view),
  `Zone.AssignByVolumes` (in Takeoff). Category moves are free (no id change).

## 5. Code-level duplication (no behaviour change)

Identical private helpers found by body comparison across `Dyncamelo.Nodes` and `Dyncamelo.Navisworks`:

| Helper | Where |
|---|---|
| `ItemIdentity` | ClashDeltaNodes, ClashFilterNodes |
| `ParseStatus` = `ParseCommentStatus` | SavedItemCommentNodes, ClashEditNodes |
| `RequireItem` | ModelItemNodes, PropertyNodes, ModelItemInfoNodes |
| `RequireTest`, `RequireResult` | ClashNodes **and** ClashHelpers |
| `EnsureDirectory` | ExportNodes, IfcExportNodes |
| `ResolveUnitsScale` | ClashFilterNodes, ClusterNodes, FallHazardNodes |
| `ToDouble` | LogicNodes, RedlineNodes, TransformHelpers |
| `FormatCell` = `HeaderText`; `FormatKey` = `FormatValue` | FileNodes/ExcelNodes; AppearanceNodes/SelectionSetNodes |

## 6. Documentation and catalogue

* README, `docs/NODE_LIBRARY.md` and `docs/GETTING_STARTED.md` still say **314 nodes across 37 categories** (v0.23); the generated catalogue has 366 across 39.
* `docs/NODE_LIBRARY.md` does not mention 45 shipped nodes (the whole IFC export set, the temporary-appearance nodes, `Viewpoints.DuplicateFolder/SortFolder/RenameFolder`,
  `List.SetDifference/SetIntersection`, `BoundingBox.Union/Scale/PlanGap`, `Flow.Then`, `Workflow.ForEach`, `Loop.Collect`, all `Action.*`, `Reroute`, …).
* `tools/generate_node_catalog.py` hard-codes its interactive-node list, so `Watch Image` and the node-group nodes are missing from the website catalogue.
  A test comparing it with `NodeRegistry.CreateDefault()` would stop that recurring.
* Checked and fine: every node name mentioned in a description or port doc resolves to an existing node.

## 7. Plan, in order

0. **Enablers** (Core, testable on Linux): `[NodeDeprecated("Use X")]` (loads and runs, hidden from library and quick search, hint on the node);
   `[PortAliases]` for renamed or merged ports; a visible warning when a graph loads with a wire or value that no longer has a port.
1. **No-risk tidying:** share the duplicated helpers (section 5); move categories; fix the docs and counts; make the catalogue generator complete, with a guard test.
2. **Retire the true duplicates** by deprecating and forwarding: `List.Join`, `ClashTest.ResultsByStatus`, `ClashResult.Comments`, `Clash.Status`.
3. **Merges:** `SavedItem.Rename / MoveToFolder / Delete / AddComment`, `Search.ByProperty`, `Markup.AddShape`, `Model.Info`. Each keeps an alias for the old id.
4. **Consistency:** `units` dropdown, typed `[MultiReturn]` outputs, the String defaults and input names (needs port aliases), flag outputs.

Decisions needed from you before step 4 (steps 0–2 change nothing a saved graph depends on):

* String comparison default: make `Contains` case-insensitive like its siblings (changes existing graphs that use the default), or the reverse, or leave and document?
* Keep the Dynamo-parity List twins, or trim to the minimum?
