# Navisworks 2024 .NET API Cookbook (verified against real 2024 assemblies)

**Verification method**: All member signatures below were dumped from the actual `Speckle.Navisworks.API 2024.0.0` assemblies (`Autodesk.Navisworks.Api.dll`, `.Clash.dll`, `.ComApi.dll`) and `Chuongmep.Navis.Api.Autodesk.Navisworks.Timeliner 2023.0.7` (`Autodesk.Navisworks.Timeliner.dll`) using a `MetadataLoadContext` (net8 + `System.Reflection.MetadataLoadContext` 8.0.0, resolver = lib/net48 dirs + `Microsoft.NETFramework.ReferenceAssemblies.net48` ref pack). Dump files are at `/tmp/claude-0/-home-user-Dyncamelo/72419632-8480-5630-803a-b632034578a4/scratchpad/dumps/*.txt` (dumper project at `.../scratchpad/apidump/`). **Every signature in this report is verified against those dumps unless explicitly marked UNVERIFIED (docs-only).** Only 3 items are docs-only: plugin folder discovery rules, threading rules, and the 2023-vs-2024 Timeliner note.

---

## 1. Plugin system — `Autodesk.Navisworks.Api.Plugins`

**`PluginAttribute`** (`: System.Attribute`):
- ctor: `PluginAttribute(string name, string developerId)`
- props: `string Name {get;}`, `string DeveloperId {get;}`, `string DisplayName {get;set;}`, `string ToolTip {get;set;}`, `string ExtendedToolTip {get;set;}`, `PluginOptions Options {get;set;}` (enum: `None=0, SupportsControls=1`), `bool SupportsIsSelfEnabled {get;set;}`
- Plugin identity: `PluginRecord.Id`/`Plugin.Id` is the string `"<Name>.<DeveloperId>"`; Name must be unique per session. Use this Id with `Application.Plugins.FindPlugin(string pluginId)` and `Application.Gui.SetDockPanePluginVisibility(string pluginId, bool visible)`.

**`Plugin`** (abstract base): props `string Id {get;}`, `string Name {get;}`, `string DeveloperId {get;}`, `PluginRecord PluginRecord {get;}`; methods `string GetString(string name)`, `GetStringSafe`, `TryGetString` (resource lookup).

**`AddInPlugin : Plugin`** (toolbar/ribbon button):
- override `int Execute(string[] parameters)` (abstract in practice — return 0), virtual `CommandState CanExecute()`, `bool TryShowHelp()`
- **`AddInPluginAttribute`**: ctor `AddInPluginAttribute(AddInLocation location)`; props `CallCanExecute CallCanExecute {get;set;}`, `bool CanToggle {get;set;}`, `string Icon {get;set;}` (16x16), `string LargeIcon {get;set;}` (32x32), `bool LoadForCanExecute {get;set;}`, `AddInLocation Location {get;}`, `string Shortcut {get;set;}`, `string ShortcutWindowTypes {get;set;}`
- **`AddInLocation`** enum: `None=0, AddIn=1, Import=2, Export=3, Help=4, CurrentSelectionContextMenu=5, CurrentSelection2DContextMenu=6`.

**`DockPanePlugin : Plugin`** (dockable pane — Dyncamelo's host):
- override `System.Windows.Forms.Control CreateControlPane()` and `void DestroyControlPane(System.Windows.Forms.Control pane)` — it **returns WinForms `Control`**, so host WPF inside `System.Windows.Forms.Integration.ElementHost` (WindowsFormsIntegration.dll): `var host = new ElementHost { Child = new DyncameloView(), Dock = DockStyle.Fill }; host.CreateControl(); return host;`
- also: `bool Visible {get;set;}`, `void ActivatePane()`, virtual `OnVisibleChanged()`, `OnActivePaneChanged(bool isActive)`, `CreateHWndPane(IWin32Window parent)`/`DestroyHWndPane` (alternate HWND mode), `TryShowHelp()`, `TryShowHelpAtScreenPoint(int x, int y)`, `TryShowHelpForHighlight()`
- **`DockPanePluginAttribute`**: ctor `DockPanePluginAttribute(int preferredWidth, int preferredHeight)`; props `bool AutoScroll {get;set;}`, `bool FixedSize {get;set;}`, `int MinimumHeight/MinimumWidth {get;set;}`, `int PreferredHeight/PreferredWidth {get;}`.

Typical declaration:
```csharp
[Plugin("DyncameloDockPane", "DYNC", DisplayName = "Dyncamelo")]
[DockPanePlugin(1000, 700, AutoScroll = false, FixedSize = false)]
public class DyncameloDockPanePlugin : DockPanePlugin { ... }
```
To toggle the pane from an AddInPlugin button: `Application.Gui.SetDockPanePluginVisibility("DyncameloDockPane.DYNC", true)` then optionally `SetDockPanePluginActive(...)` (both on `IApplicationGui`, verified).

**Discovery (UNVERIFIED — docs/ADN, not derivable from metadata)**: Navisworks scans (a) `<install>\Plugins\` — a DLL directly there, or in a subfolder whose name equals the assembly base name: `%ProgramFiles%\Autodesk\Navisworks Manage 2024\Plugins\Dyncamelo.App\Dyncamelo.App.dll` (subfolder name MUST match DLL name; dependent DLLs may sit beside it); (b) since 2014 also `%APPDATA%\Autodesk Navisworks Manage 2024\Plugins\` with the same rule; (c) since 2015, PackageContents.xml `.bundle` format under `%ProgramData%\Autodesk\ApplicationPlugins`.

**Plugin registry** (`Autodesk.Navisworks.Api.ApplicationParts.ApplicationPlugins`, via static `Application.Plugins`): `ReadOnlyCollection<PluginRecord> PluginRecords {get;}`, `PluginRecord FindPlugin(string pluginId)`, `int ExecuteAddInPlugin(string pluginId, string[] parameters)`, `void AddPluginAssembly(string fileName)`. `PluginRecord`: `Id`, `Name`, `DeveloperId`, `DisplayName`, `IsLoaded`, `IsEnabled`, `Plugin LoadedPlugin {get;}` (loads on demand via record access patterns; check `IsLoaded` then `LoadedPlugin`).

## 2. Application entry — `Autodesk.Navisworks.Api.Application` (static class)

- `static Document ActiveDocument {get;}` — the document to operate on in GUI mode.
- `static Document MainDocument {get;}` — the primary document (in GUI, same as ActiveDocument except during transient states); `static ReadOnlyCollection<Document> Documents {get;}`.
- `static IApplicationGui Gui {get;}` (null in Automation mode), `static ApplicationAutomation Automation {get;}`, `static bool IsAutomated {get;}` — branch on `IsAutomated`/`Gui != null` for GUI vs Automation.
- Events (all `EventHandler<...>`, static): `Idle` (per UI idle tick — use for deferred/queued work), `ActiveDocumentChanged/Changing`, `MainDocumentChanged/Changing`, `DocumentAdded/DocumentRemoved (DocumentEventArgs)`, `GuiCreated/GuiDestroying`, plus Progress* and File* events.
- `IApplicationGui`: `IWin32Window MainWindow {get;}` (parent for dialogs), `SetDockPanePluginVisibility/GetDockPanePluginVisibility/SetDockPanePluginActive`, events `Closing (CancelEventHandler)`, `Closed`.
- Progress: `Application.BeginProgress(string title, string message)` returns `Progress`; `Application.EndProgress()`.

## 3. Document tree

- `Document.Models` → `DocumentParts.DocumentModels` (implements `IList<Model>`): `int Count`, `Model First {get;}`, indexer `Model this[int]`, and convenience enumerables `ModelItemEnumerableCollection RootItems {get;}`, `RootItemDescendants`, `RootItemDescendantsAndSelf`; `ModelItemCollection CreateCollection()`, `CreateCollectionFromRootItems()`.
- `Model`: `ModelItem RootItem {get;}`, `string FileName`, `SourceFileName`, `Guid Guid`, `Units Units {get;}`, `Transform3D Transform`, `UnitVector3D UpVector/NorthVector/FrontVector/RightVector` + `Has*Vector` flags.
- `ModelItem` (`: NativeHandle, IDisposable`):
  - Traversal (all `ModelItemEnumerableCollection`, lazily enumerated): `Children`, `Descendants`, `DescendantsAndSelf`, `Ancestors`, `AncestorsAndSelf`, `Self`, `Instances`; plus `ModelItem Parent {get;}`.
  - Identity/metadata: `string DisplayName {get;}`, `string ClassDisplayName {get;}`, `string ClassName {get;}`, `Guid InstanceGuid {get;}` (Guid.Empty when none), `int InstanceHashCode {get;}`, `bool IsSameInstance(ModelItem item)` (use this, not reference equality), `Model Model {get;}`, `bool HasModel`.
  - State: `bool IsHidden {get;}`, `IsLayer`, `IsCollection`, `IsComposite`, `IsInsert`, `IsFrozen`, `IsRequired`, `bool HasGeometry {get;}`, `ModelGeometry Geometry {get;}`, `FindFirstGeometry()`, `FindFirstObjectAncestor()`.
  - Bounds: `BoundingBox3D BoundingBox()` and `BoundingBox(bool ignoreHidden)` — **methods, not properties**.
- `ModelItemEnumerableCollection` (`IEnumerable<ModelItem>`): `ModelItem First {get;}`, `Where(SearchCondition)`, `Where(SearchConditionCollection)`, `WhereInstanceGuid(Guid)`, `CopyTo(ICollection<ModelItem>)`, `static Empty`. DisplayName is frequently `""` for anonymous nodes — fall back to ClassDisplayName in UI.

## 4. Properties

- `ModelItem.PropertyCategories` → `PropertyCategoryCollection` (`IEnumerable<PropertyCategory>`), with finders:
  - `PropertyCategory FindCategoryByDisplayName(string)` / `FindCategoryByName(string)` / `FindCategoryByCombinedName(NamedConstant)`
  - `DataProperty FindPropertyByDisplayName(string categoryDisplayName, string propertyDisplayName)` / `FindPropertyByName(string categoryName, string propertyName)` / `FindPropertyByCombinedName(NamedConstant, NamedConstant)` — return **null** if not found.
- `PropertyCategory`: `string DisplayName {get;}` (localized), `string Name {get;}` (internal), `NamedConstant CombinedName {get;}`, `DataPropertyCollection Properties {get;}`.
- `DataPropertyCollection` (`IList<DataProperty>`): `FindPropertyByDisplayName(string)`, `FindPropertyByName(string)`.
- `DataProperty`: `string DisplayName {get;}`, `string Name {get;}`, `NamedConstant CombinedName {get;}`, `VariantData Value {get;}`; ctors `DataProperty(string name, string displayName, VariantData value)` and `DataProperty(NamedConstant combinedName, VariantData data)`.
- `NamedConstant`: ctors `(string name)`, `(string name, string displayName)`; props `Name`, `DisplayName`, `BaseName`, `int Value`.
- `PropertyCategoryNames` static class provides canonical internal names: `Item`, `Geometry`, `Material`, `Hyperlinks`, `RevitElementId`, `AutoCad`, `AutoCadEntityHandle`, `Microstation`, `MicrostationElementId`, `Transform`, `XRef` (a `DataPropertyNames` class also exists).

**`VariantData`** — type checks (all `bool` get-props, all verified): `IsNone`, `IsDisplayString`, `IsIdentifierString`, `IsDouble`, `IsInt32`, `IsBoolean`, `IsDateTime`, `IsNamedConstant`, `IsDoubleLength`, `IsDoubleArea`, `IsDoubleVolume`, `IsDoubleAngle`, `IsAnyDouble` (any of the double kinds), `IsPoint2D`, `IsPoint3D`; plus `VariantDataType DataType {get;}` (enum: `None=0, Double=1, Int32=2, Boolean=3, DisplayString=4, DateTime=5, DoubleLength=6, DoubleAngle=7, NamedConstant=8, IdentifierString=9, DoubleArea=10, DoubleVolume=11, Point3D=12, Point2D=13`).
Converters: `ToDisplayString()`, `ToIdentifierString()`, `ToDouble()`, `ToInt32()`, `ToBoolean()`, `ToDateTime()`, `ToNamedConstant()`, `ToDoubleLength()/ToDoubleArea()/ToDoubleVolume()/ToDoubleAngle()`, `ToAnyDouble()`, `ToPoint2D()/ToPoint3D()`. Converters throw on wrong type — always switch on `DataType` first.

**Robust VariantData→object recipe** (for Dyncamelo's node values):
```csharp
public static object? ToClrObject(VariantData v)
{
    switch (v.DataType)
    {
        case VariantDataType.None: return null;
        case VariantDataType.Boolean: return v.ToBoolean();
        case VariantDataType.Int32: return v.ToInt32();
        case VariantDataType.Double: return v.ToDouble();
        case VariantDataType.DoubleLength: return v.ToDoubleLength();   // document units
        case VariantDataType.DoubleArea: return v.ToDoubleArea();
        case VariantDataType.DoubleVolume: return v.ToDoubleVolume();
        case VariantDataType.DoubleAngle: return v.ToDoubleAngle();     // radians
        case VariantDataType.DateTime: return v.ToDateTime();
        case VariantDataType.DisplayString: return v.ToDisplayString();
        case VariantDataType.IdentifierString: return v.ToIdentifierString();
        case VariantDataType.NamedConstant: return v.ToNamedConstant()?.DisplayName;
        case VariantDataType.Point2D: return v.ToPoint2D();
        case VariantDataType.Point3D: return v.ToPoint3D();
        default: return v.ToString();  // ToString() is safe on any variant
    }
}
```
Factory statics (for search values): `VariantData.FromDisplayString(string)`, `FromIdentifierString(string)`, `FromDouble(double)`, `FromInt32(int)`, `FromBoolean(bool)`, `FromDateTime(DateTime)`, `FromDoubleLength/Area/Volume/Angle(double)`, `FromNamedConstant(NamedConstant)`, `FromNone()`, `FromPoint2D/FromPoint3D`.

## 5. Search API

**Note**: a type named `SearchSelectionSources` does **not exist** (grep over all dumps: zero hits). The search scope is set via `Search.Selection` (a `Selection`) + `Search.Locations`.

- `Search` (ctor `Search()`): `SearchConditionCollection SearchConditions {get;}`, `Selection Selection {get;}`, `SearchLocations Locations {get;set;}` (`Self=1, Descendants=2, DescendantsAndSelf=3`), `bool PruneBelowMatch {get;set;}`, `Clear()`.
- Execution: `ModelItemCollection FindAll(Document document, bool reportProgress)`, `ModelItem FindFirst(Document document, bool reportProgress)`, `IEnumerable<ModelItem> FindIncremental(Document document, bool reportProgress)` (each also has a 1-arg overload without document).
- `SearchCondition` — build via statics then fluent comparison:
  - statics: `HasPropertyByDisplayName(string categoryDisplayName, string propertyDisplayName)`, `HasPropertyByName(string, string)`, `HasPropertyByCombinedName(NamedConstant, NamedConstant)`, `HasCategoryByDisplayName(string)`, `HasCategoryByName(string)`, `HasCategoryByCombinedName(NamedConstant)`
  - fluent (each returns a new `SearchCondition`): `EqualValue(VariantData value)`, `DisplayStringContains(string value)`, `DisplayStringWildcard(string value)` (supports `*`/`?`), `CompareWith(SearchConditionComparison comparison, VariantData value)`, `SameType(VariantDataType)`, `Negate()`, `StartGroup()` (OR-group start), `IgnoreStringValueCase()`, `IgnoreStringValueAccents()`, `IgnoreStringValueCharWidths()`
  - `SearchConditionComparison` enum: `None, HasCategory, NotHasCategory, HasProperty, NotHasProperty, SameType, Equal, NotEqual, NumericLessThan, NumericLessThanOrEqual, NumericGreaterThanOrEqual, NumericGreaterThan, DisplayStringContains, DisplayStringWildcard, DateTimeWithinDay, DateTimeWithinWeek` (values 0–15).
  - Conditions in `SearchConditions` are ANDed; `StartGroup` begins an OR-group (Navisworks Find-Items semantics).

Recipe:
```csharp
var search = new Search();
search.Selection.SelectAll();                       // whole model; or CopyFrom(items)
search.Locations = SearchLocations.DescendantsAndSelf;
search.SearchConditions.Add(
    SearchCondition.HasPropertyByDisplayName("Element", "Category")
                   .EqualValue(VariantData.FromDisplayString("Walls")));
ModelItemCollection hits = search.FindAll(doc, false);
```
`SearchConditionCollection` implements `IList<SearchCondition>` (`Add`, etc.).

## 6. Current selection

- `Document.CurrentSelection` → `DocumentParts.DocumentCurrentSelection`:
  - `ModelItemCollection SelectedItems {get;}` (live view), `Selection Value {get;}`, `bool IsEmpty {get;}`
  - mutation: `Add(ModelItem)`, **`AddRange(IEnumerable<ModelItem> range)` — verified**, `Remove(ModelItem)`, `Clear()`, `SelectAll()`, `CopyFrom(IEnumerable<ModelItem>)`, `CopyFrom(ModelItemCollection)`, `CopyFrom(Selection)`, `CopyFrom(SelectionSourceCollection)`
  - snapshot: `Selection CreateCopy()`, `Selection ToSelection()`; events `Changed`, `Changing`.
- `Selection`: ctors `()`, `(ModelItemCollection)`, `(SelectionSourceCollection)`, `(Selection)`; `ModelItemCollection ExplicitSelection {get;}`, `SelectionSourceCollection SelectionSources {get;}`, `HasExplicitSelection`, `HasSelectionSources`, `GetSelectedItems(Document)`, `SelectAll()`, `Clear()`, `CopyFrom(...)`.
- `ModelItemCollection` (`ICollection<ModelItem>`): ctors `()`, `(ModelItemCollection from)`; `Count`, `First`, indexer, `IsEmpty`; `Add`, `AddRange(IEnumerable<ModelItem>)`, `CopyFrom(...)`, `Remove`, `Clear`, `Contains`; set-ish ops `IsContained(ModelItem)`, `IsSelected(ModelItem)` (true if item or an ancestor is in the set — "selection semantics"), `Invert(Document doc)`, `MakeDisjoint()`, `IsDisjoint()`, `Minimize()` (collapse to highest common ancestors), `AddAllInstances()`; `Descendants`/`DescendantsAndSelf` enumerables; `BoundingBox()`/`BoundingBox(bool ignoreHidden)`; `Where(SearchCondition)`. Semantics: it stores an item tree-path set; `Contains` is exact membership, `IsSelected` is hierarchical membership. **Do not mutate `CurrentSelection.SelectedItems` while enumerating it — copy first** (`new ModelItemCollection(doc.CurrentSelection.SelectedItems)` — copy-ctor verified).

## 7. Selection sets

- `Document.SelectionSets` → `DocumentParts.DocumentSelectionSets`:
  - tree: `FolderItem RootItem {get;}` (root; `FolderItem : GroupItem : SavedItem`, `GroupItem.Children` is `SavedItemCollection`), `SavedItemCollection Value {get;}` (top level)
  - mutation (all "document-part edit" style — you never set properties on stored items directly): `AddCopy(SavedItem item)` (to root), `AddCopy(GroupItem parent, SavedItem item)`, `InsertCopy(int index, SavedItem)`, `ReplaceWithCopy(...)`, `Move(...)`, `Remove(SavedItem)`, `RemoveAt(int)`, `EditDisplayName(SavedItem item, string newDisplayName)`, `Clear()`, `CopyFrom(...)`
  - resolve: `ResolveGuid(Guid)`, `CreateReference(SavedItem)` / `ResolveReference(SavedItemReference)`, `CreateSelectionSource(SavedItem)` / `ResolveSelectionSource(SelectionSource)`.
- `SavedItem` (base): `string DisplayName {get;set;}` (setter only works on detached copies — stored ones are read-only; use `EditDisplayName`), `Guid Guid {get;set;}`, `bool IsGroup {get;}`, `GroupItem Parent {get;}`, `SavedItem CreateCopy()`, `CreateUniqueCopy()`.
- `SelectionSet : SavedItem`: ctors `()`, `(ModelItemCollection items)` (explicit set), `(Search search)` (search set!), `(SelectionSet)`; `ModelItemCollection ExplicitModelItems {get;}`, `Search Search {get;}`, `bool HasExplicitModelItems`, `bool HasSearch`, `ModelItemCollection GetSelectedItems(Document)` (evaluates either kind).
- Create recipe: `var set = new SelectionSet(items) { DisplayName = "My Set" }; doc.SelectionSets.AddCopy(set);` — note **AddCopy stores a copy**; re-find it via `doc.SelectionSets.Value` / `RootItem.Children` if you need the stored instance. `SavedItemCollection` has `IndexOfDisplayName(string)` and `IndexOfGuid(Guid)` for lookup.

## 8. Overrides — `DocumentParts.DocumentModels` (on `Document.Models`)

All verified, exact names:
- `void OverridePermanentColor(IEnumerable<ModelItem> items, Color color)`
- `void OverridePermanentTransparency(IEnumerable<ModelItem> items, double transparency)` (0.0 opaque … 1.0 invisible)
- `void OverrideTemporaryColor(IEnumerable<ModelItem> items, Color color)` / `OverrideTemporaryTransparency(...)`
- `void OverridePermanentTransform(IEnumerable<ModelItem> items, Transform3D transform, bool updateModelTransform)` / `ResetPermanentTransform(items)`
- `void ResetPermanentMaterials(IEnumerable<ModelItem> items)`, `ResetTemporaryMaterials(items)`, `ResetAllPermanentMaterials()`, `ResetAllTemporaryMaterials()`
- `void SetHidden(IEnumerable<ModelItem> items, bool value)`, `ResetAllHidden()`, `bool IsHidden(IEnumerable<ModelItem> items)`; likewise `SetRequired`/`SetFrozen` + resets.
- **`Autodesk.Navisworks.Api.Color`**: ctor `Color(double red, double green, double blue)` (0–1 doubles); **`static Color FromByteRGB(byte red, byte green, byte blue)` — verified**; props `R`, `G`, `B` (double), statics `Black, White, Red, Green, Blue`; `GetClampedByteValue(int index)` for conversion back to bytes. Permanent overrides participate in undo and are saved; temporary ones are cleared on reset/reload.

## 9. Viewpoints

- `Document.SavedViewpoints` → `DocumentParts.DocumentSavedViewpoints`: same SavedItem-tree part pattern as SelectionSets — `FolderItem RootItem`, `SavedItemCollection Value`, `AddCopy(SavedItem)`, `AddCopy(GroupItem parent, SavedItem)`, `InsertCopy`, `ReplaceWithCopy`, `EditDisplayName(SavedItem, string)`, `Remove/RemoveAt/Move/Clear/CopyFrom`, `ResolveGuid`; plus viewpoint-specific: `SavedItem CurrentSavedViewpoint {get;set;}` (set to apply a saved viewpoint), `ReplaceFromCurrentView(SavedViewpoint)`, `SavedViewpoint CaptureRuntimeOverrides()`.
- `Document.CurrentViewpoint` → `DocumentParts.DocumentCurrentViewpoint`: `Viewpoint Value {get;}` (read-only live), `void CopyFrom(Viewpoint viewpoint)` (apply camera), `Viewpoint CreateCopy()`, `Viewpoint ToViewpoint()` (snapshot incl. runtime state).
- `SavedViewpoint : SavedItem`: ctors `()`, **`SavedViewpoint(Viewpoint viewpoint)` — verified**; `Viewpoint Viewpoint {get;}`.
- `Viewpoint` (ctor `Viewpoint()`): camera basics — `Point3D Position {get;set;}`, `Rotation3D Rotation {get;set;}` (quaternion A,B,C,D; `Rotation3D.CreateFromEulerAngles(x,y,z)`), `void PointAt(Point3D to)` (look-at), `void AlignUp(Vector3D up)`, `AlignDirection(Vector3D)`, `ViewpointProjection Projection {get;set;}`, `double HeightField {get;set;}` (vertical FOV for perspective), `FocalDistance`, `WorldUpVector (UnitVector3D)`, `void ZoomBox(BoundingBox3D box)` (frame a bbox), `Viewpoint CreateCopy()`. There is **no LookAt property**; use `PointAt` + `Position`.
- Recipes — save: `doc.SavedViewpoints.AddCopy(new SavedViewpoint(doc.CurrentViewpoint.ToViewpoint()) { DisplayName = "View 1" });` apply: `doc.SavedViewpoints.CurrentSavedViewpoint = savedItem;` or `doc.CurrentViewpoint.CopyFrom(vp);` zoom-to-items: `var vp = doc.CurrentViewpoint.CreateCopy(); vp.ZoomBox(items.BoundingBox(true)); doc.CurrentViewpoint.CopyFrom(vp);`

## 10. Clash — `Autodesk.Navisworks.Api.Clash` (Autodesk.Navisworks.Clash.dll)

**Obtain**: extension method `Autodesk.Navisworks.Api.Clash.DocumentExtensions.GetClash(this Document doc)` → `DocumentClash` (verified static method `GetClash(Document doc)`; usable as `doc.GetClash()` with `using Autodesk.Navisworks.Api.Clash;`). Alternative: `DocumentClash.ClashInstance(Document doc)`. (`Document.Clash` property exists but is typed as empty marker interface `IDocumentClash` — cast to `DocumentClash` also works.)

- `DocumentClash`: `DocumentClashTests TestsData {get;}`; `bool TryCalculateMinimumClearance(ModelItemCollection selection1, ModelItemCollection selection2, bool useCenterlines, out MinimumClearanceResult clearanceResult)`.
- `DocumentClashTests` (the document part; all edits go through it):
  - `SavedItemCollection Tests {get;}` — contains `ClashTest` items (and `ClashTestFolder : FolderItem`).
  - run: **`void TestsRunTest(ClashTest test)`**, **`void TestsRunAllTests()`** — verified.
  - manage: `TestsAddCopy(ClashTest test)`, `TestsAddCopy(GroupItem parent, SavedItem item)`, `TestsInsertCopy`, `TestsReplaceWithCopy`, `TestsRemove(ClashTest)`, `TestsRemoveAt(int)`, `TestsClear()`, `TestsClearResults(ClashTest)`, `TestsCompactTest/TestsCompactAllTests`, `TestsCopyFrom(...)`, **`TestsEditDisplayName(SavedItem item, string name)`** — verified.
  - result edits: `TestsEditResultStatus(IClashResult result, ClashResultStatus status)`, `TestsEditResultAssignedTo(IClashResult, string)`, `TestsEditResultApprovedBy`, `TestsEditResultApprovedTime`, `TestsEditResultDescription`, `TestsEditResultComments(IClashResult, CommentCollection)`, `TestsEditResultDistance`, `TestsEditResultCenter(IClashResult, Point3D)`, `TestsEditResultBoundingBox`, `TestsEditResultCreatedTime`, `TestsEditTestFromCopy(ClashTest test, ClashTest copyFrom)`.
  - extras: `Viewpoint TestsViewpointForResult(IClashResult result)`, `Bitmap TestsImageForResult(IClashResult, ImageGenerationStyle, int w, int h)`, `TestsSortTests(...)`, `TestsSortResults(...)`; events `Changed/Changing`.
- `ClashTest : GroupItem` (ctor `ClashTest()`): `ClashSelection SelectionA {get;}`, `ClashSelection SelectionB {get;}` (each has `Selection Selection {get;}` — populate via `test.SelectionA.Selection.CopyFrom(items)`; plus `PrimitiveTypes {get;set;}`, `bool SelfIntersect {get;set;}`), `ClashTestType TestType {get;set;}` (`Hard=0, HardConservative=1, Clearance=2, Duplicate=3, Custom=4`), `double Tolerance {get;set;}`, `ClashTestStatus Status {get;set;}` (`New=0, Old=1, Partial=2, Complete=3`), `DateTime? LastRun {get;}`, `Children` (inherited) = results/groups; `DisplayName` inherited from `SavedItem`.
- `ClashResult : SavedItem, IClashResult`: `ClashResultStatus Status {get;set;}` (`New=0, Active=1, Reviewed=2, Approved=3, Resolved=4`), `ModelItem Item1 {get;}`, `ModelItem Item2 {get;}`, `Point3D Center {get;set;}`, `BoundingBox3D BoundingBox {get;set;}`, `double Distance {get;set;}`, `string Description/AssignedTo/ApprovedBy {get;set;}`, `DateTime? CreatedTime/ApprovedTime {get;set;}`, `Selection1/Selection2 (ModelItemCollection)`, `CompositeItem1/CompositeItem2`. **No `GridLocation` property exists in the 2024 assembly** (grep: absent) — grid intersection must be derived from `Center` + `Document.Grids`.
- `ClashResultGroup : GroupItem, IClashResult`: same summary props (Status, Center, Distance, ...) + children are `ClashResult`s + `ClashResult RepresentativeResult {get;}`.

Obtain-and-run recipe:
```csharp
using Autodesk.Navisworks.Api.Clash;
var clash = doc.GetClash();                 // DocumentExtensions.GetClash(doc)
var test = new ClashTest { DisplayName = "Walls vs Pipes",
    TestType = ClashTestType.Hard, Tolerance = 0.01 };
test.SelectionA.Selection.CopyFrom(itemsA);
test.SelectionB.Selection.CopyFrom(itemsB);
clash.TestsData.TestsAddCopy(test);
var stored = (ClashTest)clash.TestsData.Tests[clash.TestsData.Tests.Count - 1]; // AddCopy copies!
clash.TestsData.TestsRunTest(stored);
foreach (SavedItem child in stored.Children)  // ClashResult or ClashResultGroup
    if (child is ClashResult r) { /* r.Status, r.Item1, r.Item2, r.Center, r.Distance */ }
```

## 11. TimeLiner — `Autodesk.Navisworks.Api.Timeliner` (Autodesk.Navisworks.Timeliner.dll)

**Obtain**: extension `Autodesk.Navisworks.Api.Timeliner.TimelinerDocumentExtensions.GetTimeliner(this Document doc)` → `DocumentTimeliner` (verified; `doc.GetTimeliner()` with namespace using). Alternative static: `DocumentTimeliner.TimelinerInstance(Document doc)`. (`Document.Timeliner` prop is marker interface `IDocumentTimeliner` — castable.)

- `DocumentTimeliner`: `SavedItemCollection Tasks {get;}` (top-level `TimelinerTask` tree), `GroupItem TasksRoot {get;}`, `SavedItemCollection DataSources/SimulationTaskTypes/SimulationAppearances {get;}`, `TimelinerSettings Settings {get;}`.
  - Task edits (document-part pattern — **the edit API is `TaskEdit`, there is no `TasksEditTaskProperties`**): `TaskAddCopy(TimelinerTask task)`, `TaskAddCopy(GroupItem parent, TimelinerTask task)`, `TaskInsertCopy(...)`, `TaskReplaceWithCopy(...)`, **`TaskEdit(int index, TimelinerTask newValues)`** / **`TaskEdit(GroupItem parent, int index, TimelinerTask newValues)`** (replace stored task's values with a modified copy), `TaskEditDisplayName(TimelinerTask task, string newName)`, `TaskRemoveAt(...)`, `TaskMove(...)`, `TasksClear()`, `TasksCopyFrom(...)`, `TasksSort(TaskField byField, bool ascending)`, `TaskCreateIndexPath(TimelinerTask)` / `TaskResolveIndexPath(IEnumerable<int>)`, `TaskTotalTasks()`, `TaskMergeRebuild/TaskMergeSynchronize(TimelinerTask tasks, bool importTaskType)`, summary recalc helpers; events `Changed/Changing`.
- `TimelinerTask : GroupItem` (ctor `TimelinerTask()`; children = subtasks):
  - `string DisplayName {get;set;}` (inherited), `string DisplayId {get;set;}`, `string SynchronizationId {get;set;}`
  - dates (all nullable): `DateTime? PlannedStartDate {get;set;}`, `PlannedEndDate`, `ActualStartDate`, `ActualEndDate`; `TimeSpan? PlannedDuration/ActualDuration {get;}`; `TaskStatus TaskStatus {get;}` (computed enum: `None=-1, Same=0, Before=1, After=2, ...`)
  - **task type is by name**: `string SimulationTaskTypeName {get;set;}` (e.g. "Construct", "Demolish", "Temporary" — matches `SimulationTaskTypes` entries; there is no `TaskType` object property)
  - attached items: `TimelinerSelection Selection {get;}` — `TimelinerSelection` has ctors `(ModelItemCollection)`, `(Search)`, `(Selection)`, `(SelectionSourceCollection)` and `CopyFrom(...)` overloads for all of those + `IEnumerable<ModelItem>`; `GetSelectedItems(Document)`; `HasExplicitSelection/HasSearch/HasSelectionSources`.
  - costs: `double? MaterialCost/LaborCost/EquipmentCost/SubcontractorCost {get;set;}`, `TotalCost {get;}`; `double? ProgressPercent {get;set;}`; `User1`…`User10 {get;set;}`; `bool IsEnabled/IsPlannedEnabled/IsActualEnabled {get;set;}`; `TimelinerTask CreateCopy()`, `CreateCopyWithoutChildren()`.

Read recipe: iterate `timeliner.Tasks` recursively via `GroupItem.Children`, cast to `TimelinerTask`. Create + attach recipe:
```csharp
using Autodesk.Navisworks.Api.Timeliner;
var tl = doc.GetTimeliner();
var task = new TimelinerTask { DisplayName = "Pour L1 slab",
    PlannedStartDate = start, PlannedEndDate = end,
    SimulationTaskTypeName = "Construct" };
task.Selection.CopyFrom(items);            // attach ModelItemCollection
tl.TaskAddCopy(task);                      // stored as a copy
// to modify later: locate index in tl.Tasks, build edited copy, tl.TaskEdit(index, copy)
```
**2023 DLL vs 2024 note (docs-only)**: signatures above come from the 2023.0.7 Timeliner DLL. Autodesk's 2024 docs list the same `DocumentTimeliner`/`TimelinerTask` surface; no 2024 API changes to TimeLiner are documented in the 2024 What's New, and the DLL is not strong-named so the host's 2024 copy binds at runtime. Treat as stable but this equivalence itself is UNVERIFIED against a 2024 Timeliner binary (none is published on NuGet).

## 12. Units & geometry

- `Document.Units` → enum `Autodesk.Navisworks.Api.Units`: `Meters=0, Centimeters=1, Millimeters=2, Feet=3, Inches=4, Yards=5, Kilometers=6, Miles=7, Micrometers=8, Mils=9, Microinches=10`. Each `Model.Units` too; all API doubles (lengths, bboxes, tolerances) are in document units.
- **`UnitConversion` — verified**: `static double ScaleFactor(Units from, Units to)` (multiply a `from`-value by the factor to get `to`-units).
- `BoundingBox3D`: ctors `()`, `(Point3D minPoint, Point3D maxPoint)`; `Point3D Min {get;}`, `Max {get;}`, **`Center {get;}`**, **`Vector3D Size {get;}`** (both verified), `double Volume/SurfaceArea {get;}`, `bool IsEmpty`, `static Empty`, `Extend(Point3D|BoundingBox3D)`, `Intersect/Intersects`, `Contains`, `Translate(Vector3D)`, `ClosestPoint(Point3D)`.
- `Point3D`: ctors `()`, `(double x, double y, double z)`; `double X/Y/Z {get;}` (immutable — no setters), `static Origin`, `DistanceTo(Point3D)`, `Add(Vector3D)`, `Subtract(Point3D)→Vector3D`, `ToVector3D()`, `ToData()→VariantData`. `Vector3D`: `(double x,double y,double z)`, `X/Y/Z`, `Length`, `Cross`, `Add`, statics `Zero/UnitX/UnitY/UnitZ`.

## 13. Transactions / undo

Navisworks transactions are **thin undo-grouping wrappers, not Revit-style required gates** — most document-part mutations work without one and create their own undo entries.
- `Transaction Document.BeginTransaction(string displayName)` — verified; `Transaction` (also ctor `Transaction(Document document, string displayName)`, `IDisposable`): `void Commit()`, `Dispose()` (rolls back if not committed), `bool IsCommitted`, `string DisplayName`, `Document Document`.
- `Document`: `bool IsActiveTransaction {get;}`, `void Undo()/Redo()`, `bool TryUndo()/TryRedo()`, `string NextUndo/NextRedo {get;}`, `void Rollback()/TryRollback()`, `StartDisableUndo()/EndDisableUndo()`, `bool IsUndoDisabled`; events `TransactionBeginning/TransactionEnded`.
- Editing rules for document parts: stored `SavedItem`s (selection sets, saved viewpoints, clash tests, timeliner tasks) are **read-only in place** (`IsReadOnly == true`); mutate by (a) building/modifying a detached copy (`CreateCopy()` or `new`), then (b) calling the part's `AddCopy`/`ReplaceWithCopy`/`TaskEdit`/`Tests*`/`EditDisplayName` method. Wrap multi-step edits in `BeginTransaction(...)` + `Commit()` so users get a single undo step. Nesting is not supported (check `IsActiveTransaction` first) — recommended pattern for Dyncamelo: one transaction per graph run.

## 14. Threading, re-entrancy, gotchas

- **Threading (docs-only)**: the entire .NET API is single-threaded and must be called on the Navisworks main (UI) thread. There is no built-in marshaling; a docked pane's WPF Dispatcher thread IS the main thread, so Dyncamelo's engine can call the API directly from UI event handlers. For work queued from elsewhere, hook `Application.Idle` and drain a queue there. Never touch API objects from `Task.Run`/background threads — native handles are not thread-safe.
- **Re-entrancy**: do not mutate the document from inside `Changing`/`Changed` event handlers of the same document part (e.g. don't call `CurrentSelection.CopyFrom` inside `CurrentSelection.Changed`); defer via `Application.Idle`. `FindAll(reportProgress: true)` pumps progress UI and can re-enter — pass `false` from Dyncamelo.
- **Handle lifetime**: `ModelItem`, `ModelItemCollection`, `VariantData`, `Color`, etc. all derive from `NativeHandle : IDisposable` and wrap native memory. They are invalidated when the model is unloaded/refreshed: any `Clear()`, `OpenFile`, `AppendFile`, `UpdateFiles()`, or `DocumentAdded/Removed` transition kills cached `ModelItem` references — using them afterwards throws/returns disposed handles. Dyncamelo must invalidate cached node outputs on `Application.ActiveDocumentChanged`, `Document.FileNameChanged`, `Document.Models.CollectionChanged`, and `Models.SceneLoaded`. For persistence across sessions use `Document.Models.CreatePathId(item)`/`ResolvePathId`, `CreateIndexPath`/`ResolveIndexPath`, or `InstanceGuid` (when non-empty) — never the object reference.
- **Identity**: use `ModelItem.IsSameInstance(other)` or `InstanceHashCode`, not `==`; two `ModelItem` wrappers for the same node are distinct CLR objects (`Equals` is overridden, reference compare is not enough for dictionaries — key on `InstanceHashCode` + `IsSameInstance`).
- **Collections are lazy or live**: `Children/Descendants/...` (`ModelItemEnumerableCollection`) enumerate the live tree on demand — cheap to hold, but materialize with `new ModelItemCollection` + `AddRange`/`CopyFrom` before mutating the document based on them. `CurrentSelection.SelectedItems` is a live view: copy before enumerating if the loop changes selection/visibility.
- **AddCopy copies**: every part-level `AddCopy/InsertCopy/TaskAddCopy/TestsAddCopy` stores a *copy*; the local object remains detached. Re-fetch the stored item (by index, `IndexOfDisplayName`, or `ResolveGuid`) before passing it to run/edit methods like `TestsRunTest`.
- **ComApi bridge** (`Autodesk.Navisworks.Api.ComApi.ComApiBridge`, verified): `static InwOpState10 State {get;}`, `ToInwOaPath(ModelItem)`, `ToModelItem(InwOaPath)`, `ToInwOpSelection(ModelItemCollection)`, `ToModelItemCollection(InwOpSelection)`, `ToInwOpAnonView(Viewpoint)`/`ToViewpoint(...)` — needed only for features absent from .NET API (e.g. *writing* custom user properties via `InwOpState10`), COM must also stay on the main thread.
- **`Document.Grids`** (`DocumentGrids`) exists for grid data; clash results do not carry grid locations (see §10).

Sources consulted for the three docs-only items: [spiderinnet plugin deployment](https://spiderinnet.typepad.com/blog/2012/10/navisworks-net-deploy-addins-plugins.html), [AEC DevBlog — 2014 APPDATA plugin path](https://adndevblog.typepad.com/aec/2013/05/navisworks-2014-api-new-feature-one-more-path-to-load-plugin.html), [Autodesk Navisworks API forum — plugin locations](https://forums.autodesk.com/t5/navisworks-api-forum/navisworks-api-plugin-locations/td-p/5358803).
