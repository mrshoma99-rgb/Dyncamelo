# Navisworks 2024 API feasibility verification — wave-2 wishlist + clusters A-K

## Method and artifacts

All members below were verified against the real 2024 binaries: `Speckle.Navisworks.API 2024.0.0` (Api, Clash, ComApi, Interop.ComApi, Controls) and `Chuongmep...Timeliner 2023.0.7`, dumped with MetadataLoadContext (net8 + net48 ref pack). Artifacts (scratchpad, reproducible):
- Dumps: `/tmp/claude-0/-home-user-Dyncamelo/72419632-8480-5630-803a-b632034578a4/scratchpad/lab/dumps/` (Autodesk.Navisworks.Api.txt, .Clash.txt, .ComApi.txt, .Interop.ComApi.txt, .Controls.txt, .Timeliner.txt)
- **Compile proof**: `/tmp/claude-0/-home-user-Dyncamelo/72419632-8480-5630-803a-b632034578a4/scratchpad/lab/compiletest/Recipes.cs` — one C# file exercising every recipe below, **compiles green (net48) against the real DLLs**, so every call chain is signature-exact, not just grep-matched.
- xlsx proof: `/tmp/claude-0/-home-user-Dyncamelo/72419632-8480-5630-803a-b632034578a4/scratchpad/lab/xlsxtest/` — dependency-free reader/writer builds on netstandard2.0 AND net48 and round-trips values correctly at runtime.
"Verified" below = present in dumps + compiles. Runtime-behavior notes that cannot be proven without Navisworks are marked RUNTIME-CHECK.

## Wishlist verdicts (summary)

1. Rename tests/results/viewpoints/sets + folders + move — **FEASIBLE** (A)
2. Move/rotate elements, translate points, "location + 1 m up" — **FEASIBLE** (B)
3. Element base/reference points — **PARTIAL** (E)
4. Element type + source file — **FEASIBLE**
5. Delete elements — **PARTIAL, as expected**: whole appended models only, via newly-found `Document.RemoveFile(int)`; hide fallback (H)
6. Refresh linked files — **FEASIBLE**: `Document.UpdateFiles()` (H)
7. Shortest distance (bbox + true mesh) — **FEASIBLE**, true distance is first-class .NET (E)
8. Custom properties via COM — **FEASIBLE**, full chain verified (C)
9. Coordination/Issues integration — **IMPOSSIBLE directly; BCF 2.1 file route confirmed** (J)
10. Comments on clashes/viewpoints/sets — **FEASIBLE**, fully .NET (D)
11. CSV/xlsx read + link rows to elements — **FEASIBLE**, zero new dependencies, proven (K)

## A. SavedItem tree editing — FEASIBLE (all verified)

`DocumentSavedViewpoints` and `DocumentSelectionSets` have identical surfaces (both dumped in full):
- Rename: `EditDisplayName(SavedItem item, String newDisplayName)`
- Folders: `Autodesk.Navisworks.Api.FolderItem` has a **public parameterless ctor**; `SavedItem.DisplayName` setter works on detached items → `part.AddCopy(new FolderItem { DisplayName = "F" })`. Nested: `AddCopy(GroupItem parent, SavedItem item)`, `InsertCopy(GroupItem parent, Int32 index, SavedItem item)`.
- Move: `Move(Int32 oldIndex, Int32 newIndex)` (top level) and `Move(GroupItem oldParent, Int32 oldIndex, GroupItem newParent, Int32 newIndex)` — locate the stored item's parent via `SavedItem.Parent` and index via `GroupItem.Children.IndexOf`. Root group = `part.RootItem` (a `FolderItem`).
- Remove: `Remove(SavedItem)`, `Remove(GroupItem parent, SavedItem)`, `RemoveAt(...)`. Lookup after AddCopy (which stores a copy): `SavedItemCollection.IndexOfDisplayName/IndexOfGuid`, `part.ResolveGuid(Guid)`, `CreateIndexPath/ResolveIndexPath`.

Clash (`DocumentClashTests`, Clash dump):
- Rename test AND result/group: `TestsEditDisplayName(SavedItem item, String name)` — parameter type is `SavedItem`, and `ClashResult`/`ClashResultGroup` are SavedItems, so result renaming goes through the same call (RUNTIME-CHECK that the UI reflects renamed results, but the GUI supports it so this is the mechanism).
- Clash folders: `ClashTestFolder : FolderItem` public ctor; **root group is `TestsData.Value.TestsRoot`** (`ClashTestsData.TestsRoot {get;}` — new find, not in the cookbook) → `TestsAddCopy(td.Value.TestsRoot, folder)`; move with `TestsMove(Int32,Int32)` / `TestsMove(GroupItem,Int32,GroupItem,Int32)`; `TestsRemove/TestsRemoveAt/TestsInsertCopy/TestsReplaceWithCopy` all verified.
- Result status/fields: `TestsEditResultStatus(IClashResult, ClashResultStatus)`, `...AssignedTo/ApprovedBy/ApprovedTime/Description/Distance/Center/BoundingBox/CreatedTime`, `TestsEditTestFromCopy(ClashTest, ClashTest)`.

## B. Transform overrides — FEASIBLE (.NET primary, COM equivalent)

.NET (Api dump, `Document.Models`):
- `OverridePermanentTransform(IEnumerable<ModelItem> items, Transform3D transform, Boolean updateModelTransform)`, `ResetPermanentTransform(items)`, `ResetAllPermanentTransforms()`. **There is NO OverrideTemporaryTransform in 2024** (0 hits) — transform override is permanent-only (undoable, saved in NWF).
- Math helpers (all verified): `Transform3D.CreateTranslation(Vector3D)`, `new Transform3D(Rotation3D)`, `new Transform3D(Rotation3D, Vector3D)`, `Transform3D.Multiply(l, r)`, `Inverse()`, `Factor()` → `Transform3DComponents`, `Translation {get;}`; `Rotation3D(UnitVector3D axis, Double angle)`, `CreateFromEulerAngles(x,y,z)`, `ToEulerAngles()`, `ToAxisAndAngle()`. Rotation about point c: `Multiply(Multiply(T(c), R), T(-c))` — compiles.
- "Get location then offset 1 m up": `items.BoundingBox(true).Center` (+ `Point3D.Add(Vector3D)` verified) → `CreateTranslation(new Vector3D(0,0,1*UnitConversion.ScaleFactor(Units.Meters, doc.Units)))` → override. All lengths are in document units — always scale via `UnitConversion.ScaleFactor`.
- Read-back: `ModelItem.Geometry` (`ModelGeometry`) exposes `ActiveTransform`, `OriginalTransform`, `PermanentTransform`, `PermanentOverrideTransform` — so a "Element.GetTransform" node is pure .NET.
- Whole-model placement: `SetModelUnitsAndTransform(Model, Units, Transform3D, Boolean transformReflected)` (Units-and-Transform dialog equivalent).

COM route (verified, works but redundant in 2024): `ComApiBridge.State` → `InwOpState10.OverrideTransform(InwOpSelection, InwLTransform3f)`, `OverrideTransformReset(sel)`, `OverrideTransformResetAll()`; build the matrix via `ObjectFactory(nwEObjectType.eObjectType_nwLTransform3f)` cast to `InwLTransform3f2` (`MakeTranslation(InwLVec3f)`, `MakeRotation`, `MakeTransRot`, `MakeUniformScale`, `SetMatrix(Object)`); `InwLVec3f.SetValue(x,y,z)`. Recommendation: .NET only.

## C. Custom user properties via COM — FEASIBLE (exact chain verified + compiles)

```
InwOpState10 state = ComApiBridge.State;                          // verified
InwOaPath path = ComApiBridge.ToInwOaPath(item);                  // verified
var node = (InwGUIPropertyNode2)state.GetGUIPropertyNode(path, true);  // returns InwGUIPropertyNode; cast
var vec = (InwOaPropertyVec)state.ObjectFactory(nwEObjectType.eObjectType_nwOaPropertyVec, null, null); // =39
var p   = (InwOaProperty)state.ObjectFactory(nwEObjectType.eObjectType_nwOaProperty, null, null);       // =20
p.name = "STATUS"; p.UserName = "Status"; p.value = "Approved";   // name/UserName/value all get;set
vec.Properties().Add(p);                                          // InwOaPropertyColl.Add(Object)
node.SetUserDefined(0, "Dyncamelo Data", "DyncameloData", vec);   // exact sig: (Int32 ndx, String user_name, String internal_name, InwOaPropertyVec)
```
- Index semantics (established, TwentyTwo/ADN): `0` creates a new user tab; passing an existing user tab's **1-based index** overwrites it — that is also how you **rename a tab** (same vec, new user_name) and **change values** (rebuild vec, same index). `RemoveUserDefined(Int32 ndx)` removes a user tab (1-based).
- Read-back / find tab index: `node.GUIAttributes()` → `InwGUIAttributesColl` of `InwGUIAttribute2` with `Boolean UserDefined {get;}`, `ClassUserName`, `ClassName`, `Properties()` — count only `UserDefined==true` entries to compute the SetUserDefined index. There is **no GetUserDefined** (0 hits). User props also appear to .NET reads via `ModelItem.PropertyCategories` after edit.
- `p.value` is a VARIANT: string/double/int/bool/DateTime supported. Gotchas: COM must run on the main thread; property edits mark the document modified and are stored in NWF (not written back to source); reuse a stable `internal_name` so search sets can target `HasPropertyByName`. No .NET write surface exists (`DataProperty.Value` is get-only; the only `SetProperty` in the Api dump is unrelated `IHasDynamicProperties`).

## D. Comments — FEASIBLE, fully .NET (COM not needed)

- `Comment` ctors `(String body, CommentStatus status)`, `(body, status, author)`; get-only `Body/Author/Status/CreationDate/Id`; `CommentStatus {New, Active, Approved, Resolved}`; `CommentCollection` full IList (Add/AddRange/Remove/Insert/Clear + copy-ctor).
- Prefer `Document.CreateCommentWithUniqueId(String body, CommentStatus status[, String author])` (verified) so `Comment.Id` is unique document-wide.
- Viewpoints/sets: `DocumentSavedViewpoints.AddComment(SavedItem, Comment)` and `EditComments(SavedItem, CommentCollection)` (replace-all → implements edit/delete); identical pair on `DocumentSelectionSets`. Read via `SavedItem.Comments {get;}`.
- Clash results: `TestsEditResultComments(IClashResult, CommentCollection)` — read existing `result.Comments`, copy to a new collection, add/remove, write back. Works for `ClashResultGroup` too (IClashResult).
- `Document.CurrentComments` (`DocumentCurrentComments`) exists for the live comments window (`Value`, `CopyFrom`, `ChangeSource`).

## E. Geometry access, distances, reference points

- **True shortest distance — first-class .NET (better than expected)**: `DocumentClash.TryCalculateMinimumClearance(ModelItemCollection selection1, ModelItemCollection selection2, Boolean useCenterlines, out MinimumClearanceResult)` with `MinimumClearanceResult.ClosestPointOnSelection1/2 (Point3D)` and `Point1/2OnCenterline` — exact mesh-to-mesh minimum distance plus the witness points (distance = `p1.DistanceTo(p2)`). This should be the primary "Element.DistanceTo" node; no COM needed.
- Bbox tier: `BoundingBox3D.ClosestPoint(Point3D)`, `Center`, `Min/Max`, `Intersects`, plus `ModelItem.BoundingBox(bool ignoreHidden)` — cheap approximate distance node.
- COM mesh walk (verified, for point clouds/centroids/custom math): `ComApiBridge.ToInwOaPath(item)` → `InwOaPath.Fragments()` (`InwNodeFragsColl`) → per `InwOaFragment3`: `GenerateSimplePrimitives(nwEVertexProperty bits, InwSimplePrimitivesCB cb)` with callback interface `InwSimplePrimitivesCB { Triangle(v1,v2,v3); Line(v1,v2); Point(v1); SnapPoint(v1) }`, `InwSimpleVertex.coord` = VARIANT float array (1-based); coordinates are in the fragment's local frame → transform by `frag.GetLocalToWorldMatrix()` (`InwLTransform3f2.Matrix` = 16 doubles, 1-based array). A C# class implementing the callback interface compiles against the interop assembly. Gotchas: fragment enumeration must stay on the main thread; large models → cap triangle counts per node run; fragments follow the item's path (compare fragment `path.ArrayData` to the item's path in multi-fragment groups).
- **Reference/base points (PARTIAL)**: there is NO "insertion point" API. Verified candidates to expose as one multi-output node: (a) `BoundingBox().Center` / `Min`; (b) COM `GetLocalToWorldMatrix()` translation column = local-frame origin (approximates the source insertion point for many formats); (c) `Document.GetPointInfo(Point3D worldPoint)` → `IEnumerable<PointInfo>` (snap-style info at a point, verified); (d) source properties when present (Revit "Location" etc. via PropertyCategories). No `ModelItemGeometry` type exists (0 hits) — it is `ModelGeometry` (`FragmentCount`, `PrimitiveCount`, `BoundingBox`, transforms, `IsSolid`).

## F. Grids & levels — FEASIBLE (read-only; exactly what clash-grouping needs)

`Document.Grids` → `DocumentGrids`: `GridSystem ActiveSystem {get;set;}`, `GridSystemCollection Systems`, `SetSystemLockedLevel(GridSystem, GridLevel)`. `GridSystem`: `Levels` (`GridLevelCollection`), `Lines`, `Origin`, `UpDirection`, `ClosestIntersection(Point3D)`. `GridLevel`: `DisplayName`, `Double Elevation`, `ClosestIntersection(Point3D)`. `GridIntersection`: `DisplayName` (e.g. "A-1"), `Position`, `Line1/Line2/Level`, plus `FormatCombinedDisplayString/FormatIntersectionDisplayString/FormatLevelDisplayString(Point3D, Double conversionFactor)` — ready-made "B-3 : Level 2" labels. Clash-by-grid = `ClashResult.Center` → `ActiveSystem.ClosestIntersection(center)` + nearest `Level` by `Elevation` (cookbook's finding stands: no `GridLocation` on ClashResult). Grids are read-only (collections exist but grid data comes from source models).

## G. Image/viewpoint export — FEASIBLE, .NET first-class (COM only for format options)

- **`Document.GenerateImage(ImageGenerationStyle style, Int32 width, Int32 height, Boolean enableSectioning)`** (+ overload with `Double maxTimeHint` for raytrace) → `System.Drawing.Bitmap`; `ImageGenerationStyle {Scene=0, SceneUsingRayTrace=1, ScenePlusOverlay=2}`. Same method also on `View`. Save via `Bitmap.Save(path, ImageFormat.Png)`. Sequence for viewpoint thumbnails: apply viewpoint (`CurrentViewpoint.CopyFrom`) → GenerateImage → restore.
- Clash snapshots without touching the camera: `TestsImageForResult(IClashResult, ImageGenerationStyle, Int32 w, Int32 h)` → Bitmap and `TestsViewpointForResult(IClashResult)` → Viewpoint (both verified) — exactly what a clash-report node needs.
- COM alternative (verified members; established technique per teocomi/ADN): `state.GetIOPluginOptions("lcodpimage")` → set `export.image.format`=`lcodpexpng`, `export.image.width/height` → `state.DriveIOPlugin("lcodpimage", filename, opts)` (`nwEExportStatus`). Also `state.CreatePicture(InwOpAnonView, passes, w, h)`. Use only if GenerateImage quality/AA options prove insufficient. Headless: `ApplicationAutomation.GenerateThumbnail(w, h, fileName)` exists for Automation mode.

## H. Model ops — stronger than expected

- **Remove appended model: `Document.RemoveFile(Int32 index)` / `TryRemoveFile(Int32 index)` — verified** (index aligns with `Document.Models` order; RUNTIME-CHECK removing index 0 / last file, and note a doc must usually retain ≥1 model). COM alternative `InwOpState10.DeleteSelectedFiles()` (deletes currently-selected file nodes). No API deletes arbitrary sub-items (the interop `TryDeleteItem` members are saved-item-tree glue, not scene items) — **hide (`Models.SetHidden`) is the per-element fallback**, as the user expected.
- **Refresh: `Document.UpdateFiles()`** → bool (Home > Refresh equivalent), with `FilesUpdating/FilesUpdated` events. Also `OpenFile/TryOpenFile`, `AppendFile(s)/TryAppendFile(s)`, `SaveFile(String[, DocumentFileVersion])`, `Clear()`, `IsClear`, `IsModified`.
- Inventory: `Model.FileName`, `SourceFileName`, `SourceGuid`, `Guid`, `Creator`, `Units`, `Transform`, `IsTransformReflected`, up/north/front vectors. Wishlist 4 additionally: `ModelItem.ClassDisplayName/ClassName` (type), and verified `DataPropertyNames.ItemType/ItemInternalType/ItemSourceFile/ItemSourceFileName/ItemGuid/RevitElementIdValue` constants for `FindPropertyByName(PropertyCategoryNames.Item, ...)`.
- CRITICAL gotcha: `RemoveFile`, `UpdateFiles`, `OpenFile`, `AppendFile` all invalidate every cached `ModelItem`/collection handle — Dyncamelo must flush node caches on `Models.CollectionChanged`/`SceneLoaded`/`FilesUpdated`.

## I. Model version compare — PARTIAL (honest assessment)

- No multi-document support inside a GUI plugin: one ActiveDocument. `Autodesk.Navisworks.Controls.DocumentControl` (verified: ctor, `Document {get;}`, `SetAsMainDocument()`) hosts an independent document, but it is designed for standalone Controls-API apps; instantiating it inside the Navisworks process is undocumented — treat as a spike, not a plan.
- Recommended feasible route (all members verified): sequential compare — open version A (`TryOpenFile`), snapshot items to plain data keyed by `ModelItem.InstanceGuid` (when non-empty) / `Models.CreatePathId` / property hashes (via existing property nodes), open version B, snapshot, diff in pure .NET (added/removed/changed + transform/bbox deltas). Works in the headless CLI too. `Document.MergeFile/TryMergeFile` (verified) merges another file with duplicate-resolution — useful for merging review artifacts (sets/viewpoints/comments), not a diff tool. Navisworks' own Compare feature has no API hook (no compare members anywhere in the dumps).

## J. BCF 2.1 — CONFIRMED file-format-only (no Navisworks API needed)

- No public Issues/Coordination API exists: across all six 2024 assemblies the only "Issue" types are internal interop glue (`Autodesk.Navisworks.Api.Interop.ClashCurrentIssue`, `LcClCurrentIssue`, ...) — not usable. The Coordination Issues add-in talks to ACC/BIM 360 cloud; the only programmatic route to those issues is the APS REST API (out of scope for local nodes). Third-party precedent confirms BCF-by-file is the standard workaround (BCFier, CASE issue-tracker, BCFplugin all do it).
- bcfzip = zip (System.IO.Compression, proven in-box for net48/netstandard2.0) containing `bcf.version` + per-topic folder with `markup.bcf` (XML), `viewpoint.bcfv` (XML), `snapshot.png`. Camera source: `Viewpoint` exposes everything (all verified): `Position (Point3D)`, `Rotation (Rotation3D quaternion A,B,C,D)`, `Projection`, `HeightField` (vertical FOV, radians for perspective — BCF `FieldOfView` is degrees; RUNTIME-CHECK ortho semantics = view height), `AspectRatio`, `FocalDistance/HasFocalDistance`, `WorldUpVector`. Direction/up = rotate (0,0,-1) and (0,1,0) by the quaternion (pure math on A,B,C,D). Import: `new Viewpoint()` + `Position` + `PointAt(Point3D)` + `AlignUp(Vector3D)` + `Projection` + `HeightField`, applied via `CurrentViewpoint.CopyFrom` — all verified. Snapshot: `Document.GenerateImage` (G). Components: BCF wants IFC GlobalIds; map from item properties (IFC GUID property when present, else `InstanceGuid`/source ids) — lossy for non-IFC sources; state this in the node docs.

## K. xlsx/CSV with zero new dependencies — FEASIBLE (proven end-to-end)

- `System.IO.Compression.ZipArchive` + `XmlReader/XmlWriter`: in-box on netstandard2.0 (Dyncamelo.Core/Nodes) and on net48 (Dyncamelo.Navisworks/App) with only a framework `<Reference Include="System.IO.Compression" />` (no NuGet). Proven: `XlsxLite.cs` builds on both TFMs and round-trips strings/numbers/booleans, shared-strings AND inline-strings read paths, workbook-rels sheet resolution, XML escaping (see xlsxtest paths above; output validated by re-reading the produced zip: 5 parts, `[Content_Types].xml`, `_rels/.rels`, `xl/workbook.xml`, `xl/_rels/workbook.xml.rels`, `xl/worksheets/sheet1.xml`).
- Reader must handle: shared strings (`t="s"`), inline (`t="inlineStr"`), `t="str"`, booleans, dates-as-serial-numbers (document as number; conversion helper optional). Writer: inline strings avoid sharedStrings bookkeeping; Excel opens such files fine.
- Row→element linking needs no new API: key column → `Search` + `SearchCondition.HasPropertyByName(cat, prop).EqualValue(VariantData.FromDisplayString(key))` (or `ItemGuid`/`RevitElementIdValue` from cluster H) → matched items per row; write-back direction uses cluster C (SetUserDefined) to stamp row values onto items. For display-string numeric keys, compare as strings or use `DisplayStringContains` cautiously.

## Cross-cutting gotchas

- Threading: entire .NET API and COM bridge are main-thread-only; run graph execution on the UI thread or drain via `Application.Idle`.
- Units: every double (transform translations, tolerances, clearance distances, grid elevations) is in document units; angles radians. Always convert with `UnitConversion.ScaleFactor`.
- Copy semantics: all `AddCopy/TestsAddCopy` store copies; re-fetch stored items (IndexOfDisplayName/ResolveGuid/indices) before edit/run/move calls. Stored SavedItems are `IsReadOnly` — never set properties in place; use the part edit methods.
- Undo/modified: part edits and COM SetUserDefined create undo steps and set `Document.IsModified`; wrap multi-edit nodes in `Document.BeginTransaction(...)/Commit()` for one undo step (no nesting — check `IsActiveTransaction`).
- Handle invalidation: RemoveFile/UpdateFiles/OpenFile/AppendFile/MergeFile kill all cached ModelItem handles (see H).
- `System.Drawing.Bitmap` usage keeps Dyncamelo.Navisworks net48-only (already true); dispose bitmaps promptly.

## Cookbook deltas worth folding into docs/research/navisworks-api-cookbook.md (/home/user/Dyncamelo/docs/research/navisworks-api-cookbook.md)

New verified members not currently in the cookbook: `Document.RemoveFile/TryRemoveFile`, `UpdateFiles` + Files* events, `GenerateImage`, `MergeFile(s)`, `CreateCommentWithUniqueId`, `GetPointInfo`; `DocumentSavedViewpoints/DocumentSelectionSets.AddComment/EditComments/Move/Remove(GroupItem,...)`; `ClashTestsData.TestsRoot`; `MinimumClearanceResult` members; `ModelGeometry.PermanentOverrideTransform/ActiveTransform`; grid formatting methods; `DataPropertyNames.Item*` constants; full COM chains (`SetUserDefined/RemoveUserDefined`, `OverrideTransform`, `GenerateSimplePrimitives`, `DriveIOPlugin`/`GetIOPluginOptions`, `ObjectFactory` enum values `eObjectType_nwOaProperty=20`, `eObjectType_nwOaPropertyVec=39`, `eObjectType_nwLTransform3f=24`, `eObjectType_nwLVec3f=3`).

Sources: [TwentyTwo — COM interface and adding custom property](https://twentytwo.space/2020/07/18/navisworks-api-com-interface-and-adding-custom-property/), [TwentyTwo — adding property to existing category](https://twentytwo.space/2020/12/19/navisworks-api-adding-property-to-existing-category/), [xiaodongliang ADN property example](https://github.com/xiaodongliang/Navisworks-Net-Plugin-Property-Database-Example/blob/master/NetPluginPropertyDatabaseExample/Class1.cs), [teocomi — image resolution of viewpoints exported via API](https://teocomi.com/image-resolution-of-viewpoints-exported-via-navisworks-api/), [AEC DevBlog — workaround to export image of clash result](https://adndevblog.typepad.com/aec/2012/09/workaround-to-export-image-of-clash-result.html), [Autodesk — Coordination Issues Add-In help](https://help.autodesk.com/view/NAV/2025/ENU/?guid=GUID-92D8E626-BB61-4CB8-AA46-D9E5A9517D65), [BCFier](https://datadrivenaec.com/tools/bcfier), [emaschas/BCFplugin](https://github.com/emaschas/BCFplugin), [CASE BCF exporter writeup](https://wrw.is/exporting-navisworks-saved-viewpoints-to-bcf-for-use-in-revizto/).
