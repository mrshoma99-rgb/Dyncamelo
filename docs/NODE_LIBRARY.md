# Dyncamelo Node Library

This catalog defines the complete planned node library for Dyncamelo, the visual programming environment for Autodesk Navisworks 2024. It is the **product-design source of truth** for node names, ports, behavior, and the exact Navisworks API surface each node wraps. Implementations follow this document; deviations require updating it (see [CONTRIBUTING.md](../CONTRIBUTING.md)).

Related reading: [ARCHITECTURE.md](ARCHITECTURE.md) for engine semantics (replication, coercion, states) and [EXTENDING.md](EXTENDING.md) for authoring your own nodes.

## Contents

- [Design conventions](#design-conventions) and [tiers](#tiers)
- General library: [Input](#input) · [Output](#output) · [Math](#math) · [Logic](#logic) · [String](#string) · [List](#list) · [Dictionary](#dictionary) · [Color](#color) · [DateTime](#datetime) · [File](#file) · [Geometry](#geometry)
- Navisworks library: [Application](#navisworksapplication) · [Document](#navisworksdocument) · [Model](#navisworksmodel) · [ModelItem](#navisworksmodelitem) · [Properties](#navisworksproperties) · [Search](#navisworkssearch) · [Selection](#navisworksselection) · [SelectionSets](#navisworksselectionsets) · [Appearance](#navisworksappearance) · [Viewpoints](#navisworksviewpoints) · [Camera](#navisworkscamera) · [Clash](#navisworksclash) · [TimeLiner](#navisworkstimeliner) · [Export](#navisworksexport) · [Units](#navisworksunits) · [Coordination (v0.2)](#coordination-v02-workflow-nodes)
- [Future platform nodes](#future-platform-nodes-v10-backlog-not-counted-per-category) · [Reference workflows](#reference-workflows-what-the-mvp-set-must-support-end-to-end) · [Tier totals](#tier-totals)

## Design conventions

- **Naming** follows Dynamo conventions: `Category.Verb` or `Category.Noun` (e.g. `List.GetItemAtIndex`, `Search.ByPropertyValue`, `Appearance.OverrideColor`). Interactive input nodes use plain friendly names (`Number Slider`, `Boolean`, `Watch`).
- **Port types** are advisory: the engine applies gentle coercion (numeric widening, `IConvertible`, `object` accepts anything). A scalar-typed input receiving a list triggers **replication (lacing)**: the node maps over the list (Shortest by default; Longest and Cross-Product opt-in per node instance).
- **`List<ModelItem>`** is the lingua franca of the Navisworks half of the library: every producer of model items emits a flat list, and every consumer accepts one, so any selection source (search, selection set, clash result, current selection) can feed any action (color, hide, attach, report).
- **Authoring**: unless marked *(NodeModel)*, every node is a zero-touch `public static` method in `Dyncamelo.Nodes` (pure .NET) or `Dyncamelo.Navisworks` (net48, Navisworks API), discovered via `[NodeName]`/`[NodeCategory]`/`[NodeDescription]`/`[MultiReturn]`. Multi-output rows return `Dictionary<string, object>` via `[MultiReturn]`.
- **Geometry types** (`Point`, `Vector`, `BoundingBox`, `Color`) are lightweight immutable classes in `Dyncamelo.Core` so the general library stays Navisworks-free; `Dyncamelo.Navisworks` converts to/from `Autodesk.Navisworks.Api.Point3D`, `Vector3D`, `BoundingBox3D`, `Color` at the boundary.
- **Errors** never crash a run: a node that throws surfaces `Error` state with the exception message; `Warning` covers recoverable issues (property not found returns `null` + warning).

### Tiers

| Tier | Meaning |
|---|---|
| **MVP** | Shipped in v0.1. Covers the core dataflow toolkit plus the highest-value Navisworks workflows (property extraction/QTO, search, selection sets, color/hide, viewpoints, clash triage read-out). |
| **Implemented (v0.1)** / **Implemented (v0.2)** | Rows implemented after the original tier plan: v0.1 rows shipped with the first release under this exact name; v0.2 rows are new in the current release (43 general + 59 Navisworks nodes). |
| **Beta** | Planned but not yet implemented (a handful of NodeModel and composite rows remain). |
| **Skipped** | Deliberately not built — the row notes what covers it instead. |
| **Future** | v1.0+. Script nodes, condition-builder search DSL, package manager, geometry preview, multi-document. |

---

## Input

Interactive constant nodes (all *(NodeModel)* subclasses with inline editors; no upstream ports).

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Number | Input | — (inline numeric editor) | number: double | Editable numeric constant. | NodeModel (Core) | MVP |
| Number Slider | Input | — (inline slider; min/max/step settings) | number: double | Draggable double slider, Dynamo-style. | NodeModel (Core) | MVP |
| Integer Slider | Input | — (inline slider; min/max settings) | integer: int | Draggable integer slider. | NodeModel (Core) | MVP |
| String | Input | — (inline text editor) | text: string | Editable text constant (multi-line). | NodeModel (Core) | MVP |
| Boolean | Input | — (toggle) | value: bool | True/False toggle. | NodeModel (Core) | MVP |
| File Path | Input | — (browse button) | path: string | Pick a file via OS dialog; stores relative-to-graph path when possible. | NodeModel (UI dialog) | MVP |
| Directory Path | Input | — (browse button) | path: string | Pick a folder via OS dialog. | NodeModel (UI dialog) | MVP |
| Date | Input | — (date picker) | date: DateTime | Calendar date constant (for TimeLiner/clash filters). | NodeModel (Core) | Beta |

## Output

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Watch | Output | value: object | value: object (pass-through) | Shows the incoming value inline on the node; passes it through. | NodeModel (Core) | MVP |
| Watch List | Output | list: List&lt;object&gt; | list: List&lt;object&gt; (pass-through) | Scrollable, index-annotated view of a list (nested lists expandable). | NodeModel (Core) | MVP |
| Note | Output | — (inline text) | — | Free-floating canvas annotation; not executed. | NodeModel (Core) | MVP |
| Panel | Output | text: string | — | Large resizable text panel pinned to canvas; live-updates each run (report preview). | NodeModel (Core) | Beta |

## Math

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Add | Math | a: double, b: double | result: double | a + b. Replicates over lists. | C# `+` | MVP |
| Subtract | Math | a: double, b: double | result: double | a − b. | C# `-` | MVP |
| Multiply | Math | a: double, b: double | result: double | a × b. | C# `*` | MVP |
| Divide | Math | a: double, b: double | result: double | a ÷ b; warns on divide-by-zero, returns NaN. | C# `/` | MVP |
| Modulo | Math | a: double, b: double | result: double | Remainder of a ÷ b. | C# `%` | MVP |
| Math.Round | Math | number: double, digits: int = 0 | result: double | Round to given decimal places (MidpointRounding.AwayFromZero). | System.Math.Round | MVP |
| Math.Min | Math | a: double, b: double | min: double | Smaller of two values. | System.Math.Min | MVP |
| Math.Max | Math | a: double, b: double | max: double | Larger of two values. | System.Math.Max | MVP |
| Math.Abs | Math | number: double | result: double | Absolute value. | System.Math.Abs | Implemented (v0.2) |
| Math.Pow | Math | base: double, exponent: double | result: double | base ^ exponent. | System.Math.Pow | Implemented (v0.2) |
| Math.Sqrt | Math | number: double | result: double | Square root; warns on negative input. | System.Math.Sqrt | Implemented (v0.2) |
| Math.Floor | Math | number: double | result: double | Round down to integer. | System.Math.Floor | Implemented (v0.2) |
| Math.Ceiling | Math | number: double | result: double | Round up to integer. | System.Math.Ceiling | Implemented (v0.2) |
| Math.MapRange | Math | value: double, fromLow: double, fromHigh: double, toLow: double, toHigh: double | result: double | Linearly remap a value between ranges (drives color gradients). | pure C# | Implemented (v0.2) |
| Math.Random | Math | min: double = 0, max: double = 1, seed: int = -1 | result: double | Random double in range; seed ≥ 0 gives deterministic output. | System.Random | Implemented (v0.2) |

## Logic

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| If | Logic | test: bool, true: object, false: object | result: object | Returns `true` or `false` input depending on test. Replicates over test lists. | pure C# | MVP |
| And | Logic | a: bool, b: bool | result: bool | Logical AND. | C# `&&` | MVP |
| Or | Logic | a: bool, b: bool | result: bool | Logical OR. | C# `\|\|` | MVP |
| Not | Logic | value: bool | result: bool | Logical negation. | C# `!` | MVP |
| Equals | Logic | a: object, b: object | equal: bool | Value equality with numeric/string coercion. | Object.Equals + coercion | MVP |
| GreaterThan | Logic | a: double, b: double | result: bool | a &gt; b. | C# `>` | MVP |
| LessThan | Logic | a: double, b: double | result: bool | a &lt; b. | C# `<` | MVP |
| GreaterThanOrEqual | Logic | a: double, b: double | result: bool | a ≥ b. | C# `>=` | Implemented (v0.2) |
| LessThanOrEqual | Logic | a: double, b: double | result: bool | a ≤ b. | C# `<=` | Implemented (v0.2) |

## String

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| String.Concat | String | a: string, b: string, c: string = "" | text: string | Concatenate up to three strings (defaulted ports). | String.Concat | MVP |
| String.Contains | String | text: string, searchFor: string, ignoreCase: bool = true | contains: bool | Substring test (property-value filtering workhorse). | String.IndexOf(OrdinalIgnoreCase) | MVP |
| String.Split | String | text: string, separator: string = "," | parts: List&lt;string&gt; | Split into a list. | String.Split | MVP |
| String.Replace | String | text: string, searchFor: string, replaceWith: string | text: string | Replace all occurrences. | String.Replace | MVP |
| String.Length | String | text: string | length: int | Character count. | String.Length | MVP |
| String.ToNumber | String | text: string | number: double | Parse a number (invariant + current culture fallback); warns and returns null on failure. | Double.TryParse | MVP |
| String.FromObject | String | object: object | text: string | Convert any value to its display string (invariant). | Convert.ToString | MVP |
| String.Join | String | list: List&lt;object&gt;, separator: string = ", " | text: string | Join list items into one string. | String.Join | Implemented (v0.1) |
| String.StartsWith | String | text: string, searchFor: string, ignoreCase: bool = true | result: bool | Prefix test. | String.StartsWith | Implemented (v0.2) |
| String.EndsWith | String | text: string, searchFor: string, ignoreCase: bool = true | result: bool | Suffix test. | String.EndsWith | Implemented (v0.2) |
| String.Substring | String | text: string, startIndex: int, length: int = -1 | text: string | Extract a substring (-1 length = to end). | String.Substring | Implemented (v0.2) |
| String.ToUpper | String | text: string | text: string | Uppercase. | String.ToUpperInvariant | Implemented (v0.2) |
| String.ToLower | String | text: string | text: string | Lowercase. | String.ToLowerInvariant | Implemented (v0.2) |
| String.Trim | String | text: string | text: string | Strip leading/trailing whitespace. | String.Trim | Implemented (v0.2) |

## List

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| List.Create | List | item0: object, item1: object, … (variable-port) | list: List&lt;object&gt; | Build a list from N inputs (UI grows ports). *(NodeModel)* | NodeModel (Core) | MVP |
| List.GetItemAtIndex | List | list: List&lt;object&gt;, index: int | item: object | Item at index (negative = from end). Replicates over index lists. | IList indexer | MVP |
| List.Count | List | list: List&lt;object&gt; | count: int | Number of items. | ICollection.Count | MVP |
| List.FirstItem | List | list: List&lt;object&gt; | item: object | First item; warns on empty. | LINQ FirstOrDefault | MVP |
| List.Flatten | List | list: List&lt;object&gt;, depth: int = -1 | list: List&lt;object&gt; | Flatten nested lists (-1 = completely). | recursive C# | MVP |
| List.FilterByBoolMask | List | list: List&lt;object&gt;, mask: List&lt;bool&gt; | in: List&lt;object&gt;, out: List&lt;object&gt; | Split list by a parallel true/false mask — the core filter idiom. | LINQ / [MultiReturn] | MVP |
| List.Range | List | start: double, end: double, step: double = 1 | list: List&lt;double&gt; | Numeric range (inclusive start, ≤ end). | iterator C# | MVP |
| List.Sort | List | list: List&lt;object&gt; | sorted: List&lt;object&gt; | Sort ascending (numeric or ordinal-string comparison). | List.Sort + Comparer | MVP |
| List.UniqueItems | List | list: List&lt;object&gt; | unique: List&lt;object&gt; | Remove duplicates, keep first occurrence order. | LINQ Distinct | MVP |
| List.LastItem | List | list: List&lt;object&gt; | item: object | Last item. | LINQ LastOrDefault | Implemented (v0.2) |
| List.Contains | List | list: List&lt;object&gt;, item: object | contains: bool | Membership test with coercing equality. | LINQ Any | Implemented (v0.2) |
| List.IndexOf | List | list: List&lt;object&gt;, item: object | index: int | First index of item, −1 if absent. | IList.IndexOf | Implemented (v0.2) |
| List.Reverse | List | list: List&lt;object&gt; | reversed: List&lt;object&gt; | Reverse order. | LINQ Reverse | Implemented (v0.2) |
| List.AddItemToEnd | List | list: List&lt;object&gt;, item: object | list: List&lt;object&gt; | Append (returns new list; inputs immutable). | copy + Add | Implemented (v0.2) |
| List.Join | List | listA: List&lt;object&gt;, listB: List&lt;object&gt; | list: List&lt;object&gt; | Concatenate two lists. | LINQ Concat | Implemented (v0.2) |
| List.RemoveItemAtIndex | List | list: List&lt;object&gt;, index: int | list: List&lt;object&gt; | Remove item(s) at index(es). | copy + RemoveAt | Implemented (v0.2) |
| List.GroupByKey | List | list: List&lt;object&gt;, keys: List&lt;object&gt; | groups: List&lt;List&lt;object&gt;&gt;, uniqueKeys: List&lt;object&gt; | Group items by parallel key list (QTO by-system grouping). | LINQ GroupBy / [MultiReturn] | Implemented (v0.2) |
| List.SortByKey | List | list: List&lt;object&gt;, keys: List&lt;object&gt; | sorted: List&lt;object&gt;, sortedKeys: List&lt;object&gt; | Sort items by parallel key list. | LINQ OrderBy / [MultiReturn] | Implemented (v0.2) |

## Dictionary

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Dictionary.ByKeysValues | Dictionary | keys: List&lt;string&gt;, values: List&lt;object&gt; | dictionary: Dictionary&lt;string,object&gt; | Build a dictionary from parallel lists. | Dictionary ctor | MVP |
| Dictionary.ValueAtKey | Dictionary | dictionary: Dictionary&lt;string,object&gt;, key: string | value: object | Value for key; warns + null if missing (unpacks [MultiReturn] outputs too). | TryGetValue | MVP |
| Dictionary.Keys | Dictionary | dictionary: Dictionary&lt;string,object&gt; | keys: List&lt;string&gt; | All keys. | Dictionary.Keys | Implemented (v0.2) |
| Dictionary.Values | Dictionary | dictionary: Dictionary&lt;string,object&gt; | values: List&lt;object&gt; | All values. | Dictionary.Values | Implemented (v0.2) |
| Dictionary.SetValueAtKey | Dictionary | dictionary: Dictionary&lt;string,object&gt;, key: string, value: object | dictionary: Dictionary&lt;string,object&gt; | Returns a copy with key set/updated. | copy + indexer | Implemented (v0.2) |

## Color

`Color` is a Dyncamelo.Core value type (r,g,b,a bytes); Navisworks appearance nodes convert to `Autodesk.Navisworks.Api.Color` (0–1 doubles) at the boundary.

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Color Picker | Color | — (inline swatch + dialog) | color: Color | Visual color picker constant. | NodeModel (UI) | MVP |
| Color.ByARGB | Color | red: int, green: int, blue: int, alpha: int = 255 | color: Color | Color from 0–255 channels. | Core Color ctor | MVP |
| Color.FromHex | Color | hex: string | color: Color | Parse "#RRGGBB" / "#AARRGGBB". | pure C# parse | Implemented (v0.2) |
| Color.Components | Color | color: Color | red: int, green: int, blue: int, alpha: int | Deconstruct channels. | [MultiReturn] | Implemented (v0.2) |
| Color.Lerp | Color | start: Color, end: Color, t: double | color: Color | Interpolate between two colors (t 0–1); with Math.MapRange builds value-driven gradients. | pure C# | Implemented (v0.2) |

## DateTime

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| DateTime.Now | DateTime | — | now: DateTime | Current local date-time (re-evaluates each run; timestamping reports). | System.DateTime.Now | MVP |
| DateTime.Format | DateTime | dateTime: DateTime, format: string = "yyyy-MM-dd HH:mm" | text: string | Format as string (invariant culture). | DateTime.ToString(fmt) | MVP |
| DateTime.Parse | DateTime | text: string, format: string = "" | dateTime: DateTime | Parse from string (exact format optional). | DateTime.TryParse(Exact) | Implemented (v0.2) |
| DateTime.ByDate | DateTime | year: int, month: int, day: int | dateTime: DateTime | Construct a date. | DateTime ctor | Implemented (v0.2) |
| DateTime.AddDays | DateTime | dateTime: DateTime, days: double | dateTime: DateTime | Offset a date (4D schedule shifting). | DateTime.AddDays | Implemented (v0.2) |
| DateTime.DaysBetween | DateTime | start: DateTime, end: DateTime | days: double | Signed day difference. | TimeSpan.TotalDays | Implemented (v0.2) |

## File

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| CSV.ReadFromFile | File | path: string, hasHeaders: bool = true, delimiter: string = "," | rows: List&lt;List&lt;string&gt;&gt;, headers: List&lt;string&gt; | Read a CSV (RFC-4180 quoting) into rows + header list. | StreamReader + parser / [MultiReturn] | MVP |
| CSV.WriteToFile | File | path: string, rows: List&lt;List&lt;object&gt;&gt;, headers: List&lt;string&gt; = null | path: string | Write rows (and optional header) to CSV with proper quoting — the QTO/clash-report sink. | StreamWriter | MVP |
| Text.ReadFromFile | File | path: string | text: string, lines: List&lt;string&gt; | Read a whole text file. | File.ReadAllText/Lines / [MultiReturn] | MVP |
| Text.WriteToFile | File | path: string, text: string, append: bool = false | path: string | Write or append text. | File.WriteAllText/AppendAllText | MVP |
| JSON.Parse | File | json: string | value: object | Parse JSON into nested Dictionary/List/primitives. | Newtonsoft JToken → CLR | Implemented (v0.2) |
| JSON.Stringify | File | value: object, indented: bool = true | json: string | Serialize dictionaries/lists/primitives to JSON. | JsonConvert.SerializeObject | Implemented (v0.2) |
| File.Exists | File | path: string | exists: bool | Does the file exist. | System.IO.File.Exists | Implemented (v0.2) |
| Directory.GetFiles | File | path: string, pattern: string = "*.*" | files: List&lt;string&gt; | List files in a folder (batch processing driver). | Directory.GetFiles | Implemented (v0.2) |
| Path.Combine | File | directory: string, fileName: string | path: string | Join path segments safely. | System.IO.Path.Combine | Implemented (v0.2) |

## Geometry

Lightweight geometry for measurements and camera math — no display; geometry preview is a Future feature.

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Point.ByCoordinates | Geometry | x: double, y: double, z: double = 0 | point: Point | Construct a 3D point. | Core Point ctor (↔ Api.Point3D) | MVP |
| Point.Components | Geometry | point: Point | x: double, y: double, z: double | Deconstruct a point. | [MultiReturn] | MVP |
| BoundingBox.Center | Geometry | boundingBox: BoundingBox | center: Point | Box center point (clash/viewpoint targeting). | (↔ BoundingBox3D.Center) | MVP |
| Point.DistanceTo | Geometry | point: Point, other: Point | distance: double | Euclidean distance (in model units). | vector math | Implemented (v0.2) |
| Vector.ByCoordinates | Geometry | x: double, y: double, z: double | vector: Vector | Construct a direction vector (camera up/forward). | Core Vector ctor (↔ Api.Vector3D) | Implemented (v0.2) |
| BoundingBox.Size | Geometry | boundingBox: BoundingBox | sizeX: double, sizeY: double, sizeZ: double, min: Point, max: Point | Extents and corner points (rough QTO dimensions). | (↔ BoundingBox3D.Min/Max) / [MultiReturn] | Implemented (v0.2) |
| BoundingBox.Intersects | Geometry | boundingBox: BoundingBox, other: BoundingBox | intersects: bool | Axis-aligned overlap test (cheap proximity checks). | interval math | Implemented (v0.2) |

---

# Navisworks

All nodes below live in `Dyncamelo.Navisworks` (net48) and execute on the Navisworks main thread. `Document` ports default to the active document when unconnected, so most graphs never need an explicit `Document.Current` wire (it exists for clarity and future multi-doc support). Write operations (color, sets, viewpoints, clash edits) participate in Navisworks undo via transaction scoping in the node host.

## Navisworks.Application

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Application.Version | Navisworks.Application | — | product: string, apiVersion: string | Running Navisworks product name and API version (for report headers, compatibility checks). | `Autodesk.Navisworks.Api.Application.Version.RuntimeProductName`, `Application.Version.ApiMajor/ApiMinor` | Implemented (v0.2) |

## Navisworks.Document

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Document.Current | Navisworks.Document | — | document: Document | The active Navisworks document (re-resolves each run). | `Autodesk.Navisworks.Api.Application.ActiveDocument` | MVP |
| Document.Info | Navisworks.Document | document: Document | title: string, fileName: string, isClear: bool | Title, full file path, and whether no file is open. | `Document.Title`, `Document.FileName`, `Document.IsClear` | MVP |
| Document.Models | Navisworks.Document | document: Document | models: List&lt;Model&gt; | All appended source models. | `Document.Models` (DocumentModels) | MVP |
| Document.Units | Navisworks.Document | document: Document | units: string | Display units of the document (e.g. "Meters"). Not implemented — `Units.Current` already covers it. | `Document.Units` (Units enum) | Skipped (dup of Units.Current) |
| Document.Save | Navisworks.Document | filePath: string, document: Document | filePath: string | Save the document as .nwf/.nwd to the given path (directory created when missing). | `Document.SaveFile(string)` | Implemented (v0.2) |

## Navisworks.Model

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Model.RootItem | Navisworks.Model | model: Model | rootItem: ModelItem | Root node of a source model's item tree. | `Model.RootItem` | MVP |
| Models.RootItems | Navisworks.Model | document: Document | rootItems: List&lt;ModelItem&gt; | Roots of all appended models in one step (start of most graphs). | `Document.Models.RootItems` | MVP |
| Model.FileName | Navisworks.Model | model: Model | fileName: string, sourceFileName: string | Cached and original source file paths. | `Model.FileName`, `Model.SourceFileName` | Implemented (v0.2) |
| Model.Units | Navisworks.Model | model: Model | units: string | Native units of the source file. | `Model.Units` | Implemented (v0.2) |

## Navisworks.ModelItem

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| ModelItem.Children | Navisworks.ModelItem | modelItem: ModelItem | children: List&lt;ModelItem&gt; | Direct children in the selection tree. | `ModelItem.Children` | MVP |
| ModelItem.Descendants | Navisworks.ModelItem | modelItem: ModelItem, includeSelf: bool = false | descendants: List&lt;ModelItem&gt; | All items below (optionally including the item itself). | `ModelItem.Descendants` / `ModelItem.DescendantsAndSelf` | MVP |
| ModelItem.DisplayName | Navisworks.ModelItem | modelItem: ModelItem | name: string | Selection-tree display name. | `ModelItem.DisplayName` | MVP |
| ModelItem.HasGeometry | Navisworks.ModelItem | modelItem: ModelItem | hasGeometry: bool | True for geometry-bearing leaf items. | `ModelItem.HasGeometry` | MVP |
| ModelItem.BoundingBox | Navisworks.ModelItem | modelItem: ModelItem | boundingBox: BoundingBox | Axis-aligned bounding box in world coordinates. | `ModelItem.BoundingBox()` (→ BoundingBox3D) | MVP |
| ModelItem.Parent | Navisworks.ModelItem | modelItem: ModelItem | parent: ModelItem | Parent item (null + warning at root). | `ModelItem.Parent` | Implemented (v0.2) |
| ModelItem.Ancestors | Navisworks.ModelItem | modelItem: ModelItem, includeSelf: bool = false | ancestors: List&lt;ModelItem&gt; | Chain of parents up to the model root. | `ModelItem.Ancestors` / `AncestorsAndSelf` | Implemented (v0.2) |
| ModelItem.ClassInfo | Navisworks.ModelItem | modelItem: ModelItem | className: string, classDisplayName: string | Internal and localized class names (layer/group/geometry detection). | `ModelItem.ClassName`, `ModelItem.ClassDisplayName` | Implemented (v0.2) |
| ModelItem.IsHidden | Navisworks.ModelItem | modelItem: ModelItem | isHidden: bool | Current hidden state. | `ModelItem.IsHidden` | Implemented (v0.2) |
| ModelItem.InstanceGuid | Navisworks.ModelItem | modelItem: ModelItem | guid: string | Stable instance GUID (empty when absent) — cross-run item identity for reports. | `ModelItem.InstanceGuid` | Implemented (v0.2) |
| ModelItem.GeometryLeaves | Navisworks.ModelItem | modelItems: List&lt;ModelItem&gt; | leaves: List&lt;ModelItem&gt; | Convenience: flatten to unique geometry-bearing descendants (the items QTO and coloring actually want). | composite: `DescendantsAndSelf` + `HasGeometry` filter | Implemented (v0.2) |

## Navisworks.Properties

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Properties.Value | Navisworks.Properties | modelItem: ModelItem, category: string, property: string | value: object | Read one property by display names ("Item"/"Name", "Element"/"Volume"…). Returns typed value (double/string/bool/DateTime) via VariantData coercion; null + warning when missing. Replicates over item lists — the QTO workhorse. | `ModelItem.PropertyCategories.FindPropertyByDisplayName(cat, prop)` → `DataProperty.Value` (VariantData: ToDouble/ToDisplayString/ToDateTime/ToBoolean per `VariantData.DataType`) | MVP |
| Properties.Categories | Navisworks.Properties | modelItem: ModelItem | names: List&lt;string&gt;, categories: List&lt;PropertyCategory&gt; | List all property tabs on an item (discovery/QA). | `ModelItem.PropertyCategories` (PropertyCategoryCollection) / [MultiReturn] | MVP |
| Properties.InCategory | Navisworks.Properties | modelItem: ModelItem, category: string | names: List&lt;string&gt;, values: List&lt;object&gt; | All property names + values inside one category. | `PropertyCategoryCollection.FindCategoryByDisplayName` → `PropertyCategory.Properties` (DataPropertyCollection) / [MultiReturn] | Implemented (v0.2) |
| Properties.ValueAsString | Navisworks.Properties | modelItem: ModelItem, category: string, property: string | text: string | Read a property as text ("" when absent; invariant formatting — VariantData converters throw on wrong types, so a units-suffixed display string is not obtainable). | `DataProperty.Value` via VariantData `DataType` switch → invariant text | Implemented (v0.2) |
| Properties.HasProperty | Navisworks.Properties | modelItem: ModelItem, category: string, property: string | hasProperty: bool | Existence test — drives model-QA "missing data" masks. | `FindPropertyByDisplayName(...) != null` | Implemented (v0.2) |
| Properties.AsDictionary | Navisworks.Properties | modelItem: ModelItem | properties: Dictionary&lt;string,object&gt; | Every category/property flattened to "Category.Property" → value (full data dump/JSON export). | iterate `PropertyCategories` → `PropertyCategory.Properties` → `DataProperty` | Implemented (v0.2) |
| Property.Info | Navisworks.Properties | property: DataProperty | name: string, displayName: string, value: object | Deconstruct a raw DataProperty (pairs with Properties.InCategory categories output). | `DataProperty.Name`, `DataProperty.DisplayName`, `DataProperty.Value` | Implemented (v0.2) |

## Navisworks.Search

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Search.ByPropertyValue | Navisworks.Search | document: Document, category: string, property: string, value: string | modelItems: List&lt;ModelItem&gt; | Find every item whose property equals the value — the bulk-selection workhorse (equivalent to Find Items → Equals). | `new Search()`; `Search.Selection.SelectAll()`; `SearchConditions.Add(SearchCondition.HasPropertyByDisplayName(cat, prop).EqualValue(VariantData.FromDisplayString(value)))`; `Search.FindAll(doc, false)` | MVP |
| Search.ByPropertyContains | Navisworks.Search | document: Document, category: string, property: string, value: string | modelItems: List&lt;ModelItem&gt; | Find items whose property display-string contains the text. | `SearchCondition...DisplayStringContains(value)` + `Search.FindAll` | MVP |
| Search.ByPropertyWildcard | Navisworks.Search | category: string, property: string, pattern: string, document: Document | items: List&lt;ModelItem&gt; | Wildcard match (`*`, `?`) on property display string. | `SearchCondition...DisplayStringWildcard(pattern)` | Implemented (v0.2) |
| Search.ByPropertyCompare | Navisworks.Search | category: string, property: string, comparison: string, value: double, document: Document | items: List&lt;ModelItem&gt; | Numeric comparison search: &gt;, &gt;=, &lt;, &lt;= (e.g. pipes with Diameter &gt; 100). Runs once per numeric variant type and unions the hits. | `SearchCondition...CompareWith(SearchConditionComparison.Numeric*, VariantData.FromDouble/Length/Area/Volume/Angle/Int32)` | Implemented (v0.2) |
| Search.HasProperty | Navisworks.Search | category: string, property: string, document: Document | items: List&lt;ModelItem&gt; | All items that carry the property at all (data-completeness audits; see Audit.MissingProperty for the inverse). | `SearchCondition.HasPropertyByDisplayName(cat, prop)` (no value test) | Implemented (v0.2) |
| Search.HasCategory | Navisworks.Search | category: string, document: Document | items: List&lt;ModelItem&gt; | All items carrying a property tab (e.g. every item with "TimeLiner" data). | `SearchCondition.HasCategoryByDisplayName(cat)` | Implemented (v0.2) |
| Search.InItems | Navisworks.Search | modelItems: List&lt;ModelItem&gt;, category: string, property: string, value: object, document: Document | items: List&lt;ModelItem&gt; | Scoped search: run the property-equals test only inside the given items (chained refinement). | `Search.Selection.CopyFrom(ModelItemCollection)` + conditions + `FindAll` | Implemented (v0.2) |
| Search.ByConditions | Navisworks.Search | document: Document, conditions: List&lt;object&gt;, matchAll: bool = true | modelItems: List&lt;ModelItem&gt; | Compose arbitrary condition objects (AND/OR groups, numeric compare, negation) built by companion condition nodes. | `SearchConditionGroup`, `SearchCondition` full surface | Future |

## Navisworks.Selection

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Selection.Current | Navisworks.Selection | document: Document | modelItems: List&lt;ModelItem&gt; | Items currently selected in the Navisworks UI (bridge from manual picking into the graph). | `Document.CurrentSelection.SelectedItems` (ModelItemCollection) | MVP |
| Selection.SetCurrent | Navisworks.Selection | document: Document, modelItems: List&lt;ModelItem&gt; | modelItems: List&lt;ModelItem&gt; | Make these items the live UI selection (visual feedback of graph results). | `Document.CurrentSelection.CopyFrom(ModelItemCollection)` | MVP |
| Selection.Clear | Navisworks.Selection | document: Document, run: bool = true | done: bool | Clear the UI selection. | `Document.CurrentSelection.Clear()` | MVP |
| Selection.SelectAll | Navisworks.Selection | document: Document | items: List&lt;ModelItem&gt; | Select everything and return the snapshot. | `Document.CurrentSelection.SelectAll()` | Implemented (v0.2) |
| Selection.AddToCurrent | Navisworks.Selection | modelItems: List&lt;ModelItem&gt;, document: Document | items: List&lt;ModelItem&gt; | Union new items into the existing selection; returns the unioned snapshot. | `Document.CurrentSelection.AddRange(IEnumerable<ModelItem>)` | Implemented (v0.2) |

## Navisworks.SelectionSets

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| SelectionSets.All | Navisworks.SelectionSets | document: Document | names: List&lt;string&gt;, sets: List&lt;SelectionSet&gt; | Flat list of all saved selection/search sets (recursing folders). | `Document.SelectionSets.RootItem` (FolderItem) walked via `GroupItem.Children` / [MultiReturn] | MVP |
| SelectionSet.ByName | Navisworks.SelectionSets | document: Document, name: string | set: SelectionSet | Find a saved set by display name (folder-recursive; warns if absent). | walk `DocumentSelectionSets.RootItem`, match `SavedItem.DisplayName` | MVP |
| SelectionSet.Items | Navisworks.SelectionSets | set: SelectionSet, document: Document | modelItems: List&lt;ModelItem&gt; | Resolve a set to its model items (explicit items, or executes the stored search for search sets). | `SelectionSet.ExplicitModelItems`; `SelectionSet.HasSearch` → `SelectionSet.Search.FindAll(doc, false)` | MVP |
| SelectionSet.Create | Navisworks.SelectionSets | document: Document, name: string, modelItems: List&lt;ModelItem&gt;, overwrite: bool = true | set: SelectionSet | Save items as a named selection set — the "bulk set creation from property rules" payoff node. Replicates over name/items lists to create many sets in one run. | `new SelectionSet(ModelItemCollection) { DisplayName = name }`; `Document.SelectionSets.AddCopy(set)` (or `ReplaceWithCopy` when overwriting) | MVP |
| SelectionSet.CreateFromSearch | Navisworks.SelectionSets | name: string, category: string, property: string, value: object, document: Document | selectionSet: SelectionSet | Save a live *search set* (re-evaluates as the model changes) from a property rule; equality variants are OR-grouped so numbers match measured types too. | `new SelectionSet(Search)` + `DocumentSelectionSets.AddCopy`/`ReplaceWithCopy` | Implemented (v0.2) |
| SelectionSets.CreateFolder | Navisworks.SelectionSets | name: string, document: Document | folder: FolderItem | Create (or reuse) a top-level folder to organize generated sets. | `new FolderItem { DisplayName = name }` + `DocumentSelectionSets.AddCopy` | Implemented (v0.2) |
| SelectionSet.Delete | Navisworks.SelectionSets | name: string, document: Document | deleted: bool | Remove a saved set by name; false when absent (clean re-runs). | locate SavedItem, `Document.SelectionSets.Remove(parent, item)` | Implemented (v0.2) |
| SelectionSet.Name | Navisworks.SelectionSets | set: SelectionSet | name: string | Display name of a set. | `SavedItem.DisplayName` | Implemented (v0.2) |

## Navisworks.Appearance

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Appearance.OverrideColor | Navisworks.Appearance | document: Document, modelItems: List&lt;ModelItem&gt;, color: Color | modelItems: List&lt;ModelItem&gt; | Permanently override item color (status/system color-coding). Pass-through output chains further actions. | `Document.Models.OverridePermanentColor(items, Autodesk.Navisworks.Api.Color.FromByteRGB(r,g,b))` | MVP |
| Appearance.OverrideTransparency | Navisworks.Appearance | document: Document, modelItems: List&lt;ModelItem&gt;, transparency: double | modelItems: List&lt;ModelItem&gt; | Override transparency (0 = opaque, 1 = invisible) — ghost context around a work area. | `Document.Models.OverridePermanentTransparency(items, fraction)` | MVP |
| Appearance.Reset | Navisworks.Appearance | document: Document, modelItems: List&lt;ModelItem&gt; | modelItems: List&lt;ModelItem&gt; | Remove color/transparency overrides from these items. | `Document.Models.ResetPermanentMaterials(items)` | MVP |
| Appearance.Hide | Navisworks.Appearance | document: Document, modelItems: List&lt;ModelItem&gt; | modelItems: List&lt;ModelItem&gt; | Hide items. | `Document.Models.SetHidden(items, true)` | MVP |
| Appearance.Show | Navisworks.Appearance | document: Document, modelItems: List&lt;ModelItem&gt; | modelItems: List&lt;ModelItem&gt; | Unhide items. | `Document.Models.SetHidden(items, false)` | MVP |
| Appearance.ResetAll | Navisworks.Appearance | document: Document | done: bool | Clear every appearance override in the model (clean slate before re-coloring). | `Document.Models.ResetAllPermanentMaterials()` | Implemented (v0.2) |
| Appearance.Isolate | Navisworks.Appearance | modelItems: List&lt;ModelItem&gt;, document: Document | items: List&lt;ModelItem&gt; | Show only these items; hide everything else. | `ModelItemCollection.Invert(doc)` → `SetHidden(true)`; items → `SetHidden(false)` | Implemented (v0.2) |
| Appearance.ColorByValues | Navisworks.Appearance | modelItems: List&lt;ModelItem&gt;, values: List&lt;object&gt;, palette: List&lt;Color&gt; = null, document: Document | items: List&lt;ModelItem&gt;, legend: Dictionary&lt;string,string&gt; | One-node color-coding: all-numeric values → blue→red gradient, discrete values → built-in or supplied categorical palette; outputs a value→"#RRGGBB" legend for reporting. | grouping + `Models.OverridePermanentColor` per bucket | Implemented (v0.2) |

## Navisworks.Viewpoints

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Viewpoints.All | Navisworks.Viewpoints | document: Document | names: List&lt;string&gt;, viewpoints: List&lt;SavedViewpoint&gt; | All saved viewpoints (recursing folders/animations). | `Document.SavedViewpoints.RootItem` walked via `GroupItem.Children` / [MultiReturn] | MVP |
| SavedViewpoint.ByName | Navisworks.Viewpoints | document: Document, name: string | viewpoint: SavedViewpoint | Find a saved viewpoint by name. | walk `DocumentSavedViewpoints.RootItem`, match `SavedItem.DisplayName` | MVP |
| SavedViewpoint.Apply | Navisworks.Viewpoints | document: Document, viewpoint: SavedViewpoint | viewpoint: SavedViewpoint | Make it the current view (restores camera + hidden/override state saved with it). | `Document.SavedViewpoints.CurrentSavedViewpoint = viewpoint` | MVP |
| Viewpoint.SaveCurrent | Navisworks.Viewpoints | document: Document, name: string | viewpoint: SavedViewpoint | Snapshot the current camera (+ hide/override state) as a named saved viewpoint. Replicates over name lists for batch generation. | `new SavedViewpoint(Document.CurrentViewpoint.ToViewpoint()) { DisplayName = name }`; `Document.SavedViewpoints.AddCopy(...)` | MVP |
| SavedViewpoint.Name | Navisworks.Viewpoints | viewpoint: SavedViewpoint | name: string | Display name. | `SavedItem.DisplayName` | Implemented (v0.2) |
| SavedViewpoint.Delete | Navisworks.Viewpoints | name: string, document: Document | deleted: bool | Remove a saved viewpoint by name; false when absent (clean re-runs of batch generation). | locate SavedItem + `DocumentSavedViewpoints.Remove(...)` | Implemented (v0.2) |
| Viewpoints.FromClashResults | Navisworks.Viewpoints | results: List&lt;ClashResult&gt;, folderName: string = "Clash Views", document: Document | viewpoints: List&lt;SavedViewpoint&gt; | Batch-generate one saved viewpoint per clash result, camera aimed at the clash, named after the result — clash-triage staple. Same-named viewpoints in the folder are replaced. | `DocumentClashTests.TestsViewpointForResult` + `SavedViewpoints.AddCopy/ReplaceWithCopy` into a `FolderItem` | Implemented (v0.2) |

## Navisworks.Camera

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Camera.Current | Navisworks.Camera | document: Document | position: Point, focalDistance: double, heightField: double | Current camera position and lens parameters (focalDistance null when unset). | `Document.CurrentViewpoint.Value` → `Viewpoint.Position`, `Viewpoint.FocalDistance`, `Viewpoint.HeightField` | Implemented (v0.2) |
| Camera.LookAt | Navisworks.Camera | eye: Point, target: Point, document: Document | done: bool | Move the camera to `eye` looking at `target` (up = +Z). Accepts Navisworks or Dyncamelo points, or [x,y,z] lists. | `CurrentViewpoint.CreateCopy()`; set `Viewpoint.Position`; `Viewpoint.PointAt(target)`; `Viewpoint.AlignUp(Vector3D 0,0,1)`; `Document.CurrentViewpoint.CopyFrom(vp)` | Implemented (v0.2) |
| Camera.ZoomToItems | Navisworks.Camera | modelItems: List&lt;ModelItem&gt;, paddingFactor: double = 1.5, document: Document | done: bool | Frame the given items in the view (per-item screenshots, clash close-ups). | `ModelItemCollection.BoundingBox(true)` padded around its center → `Viewpoint.ZoomBox(box)` + `CurrentViewpoint.CopyFrom` | Implemented (v0.2) |

## Navisworks.Clash

Namespace `Autodesk.Navisworks.Api.Clash`; the clash test document part is `DocumentClash clash = document.GetClash()`, and all edits go through its `TestsData` (`DocumentClashTests`) so the UI stays in sync.

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Clash.Tests | Navisworks.Clash | document: Document | names: List&lt;string&gt;, tests: List&lt;ClashTest&gt; | All clash tests in the document. | `document.GetClash().TestsData.Tests` (SavedItemCollection of ClashTest) / [MultiReturn] | MVP |
| ClashTest.ByName | Navisworks.Clash | name: string, document: Document | test: ClashTest | Find one clash test by display name (searches folders too). | iterate `DocumentClashTests.Tests`, match `SavedItem.DisplayName` | Implemented (v0.2) |
| ClashTest.Info | Navisworks.Clash | test: ClashTest | name: string, status: string, lastRun: DateTime | Test name, status (New/Done/OK…), and last run time — report headers. | `ClashTest.DisplayName`, `ClashTest.Status` (ClashTestStatus), `ClashTest.LastRun` / [MultiReturn] | MVP |
| ClashTest.Results | Navisworks.Clash | test: ClashTest, includeGroups: bool = true | results: List&lt;ClashResult&gt;, groupNames: List&lt;string&gt; | All individual results of a test, flattening clash groups (group name reported per result, "" when ungrouped). | `ClashTest.Children` recursed through `ClashResultGroup.Children` → `ClashResult` / [MultiReturn] | MVP |
| ClashResult.Info | Navisworks.Clash | result: ClashResult | name: string, status: string, distance: double, created: DateTime, assignedTo: string | Core result metadata in one node — feeds the CSV triage report. | `ClashResult.DisplayName`, `.Status` (ClashResultStatus), `.Distance`, `.CreatedTime`, `.AssignedTo` / [MultiReturn] | MVP |
| ClashResult.Items | Navisworks.Clash | result: ClashResult | item1: ModelItem, item2: ModelItem | The two clashing model items (composite ancestors preferred so names are human-meaningful). | `ClashResult.CompositeItem1` / `CompositeItem2` (fallback `Item1`/`Item2`) / [MultiReturn] | MVP |
| ClashResult.Center | Navisworks.Clash | result: ClashResult | point: Point | Clash location in world coordinates (viewpoint targets, zone bucketing). | `ClashResult.Center` (Point3D) | MVP |
| ClashTest.Run | Navisworks.Clash | test: ClashTest, document: Document | test: ClashTest, resultCount: int | Execute one clash test now (requires the stored test, not a detached copy). | `DocumentClashTests.TestsRunTest(storedTest)` / [MultiReturn] | Implemented (v0.2) |
| Clash.RunAllTests | Navisworks.Clash | document: Document | tests: List&lt;ClashTest&gt; | Execute every clash test — the weekly re-run in one node. | `DocumentClashTests.TestsRunAllTests()` | Implemented (v0.2) |
| ClashResult.SetStatus | Navisworks.Clash | result: ClashResult, status: string, document: Document | result: ClashResult | Set result status (New/Active/Reviewed/Approved/Resolved) — bulk triage by rule (e.g. "distance < 10 mm → Reviewed") via lacing. | `DocumentClashTests.TestsEditResultStatus(result, ClashResultStatus)` | Implemented (v0.2) |
| ClashResult.Assign | Navisworks.Clash | result: ClashResult, assignedTo: string, document: Document | result: ClashResult | Assign a result to a person/trade in bulk via lacing. | `DocumentClashTests.TestsEditResultAssignedTo(result, assignedTo)` | Implemented (v0.2) |
| ClashResult.Comments | Navisworks.Clash | result: ClashResult | comments: List&lt;string&gt;, authors: List&lt;string&gt; | Read the comment thread on a result. | `SavedItem.Comments` (Comment.Body, Comment.Author) | Future |

## Navisworks.TimeLiner

Namespace `Autodesk.Navisworks.Api.Timeliner`; the document part is `DocumentTimeliner timeliner = document.GetTimeliner()`. The compile-time reference comes from the pinned `Chuongmep.Navis.Api...Timeliner` package; the host's 2024 `Autodesk.Navisworks.Timeliner.dll` binds at runtime. All TimeLiner nodes are Beta pending that binding being proven in-host.

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| TimeLiner.Tasks | Navisworks.TimeLiner | document: Document, flatten: bool = true | tasks: List&lt;TimelinerTask&gt;, names: List&lt;string&gt; | All TimeLiner tasks (hierarchy flattened depth-first by default). | `document.GetTimeliner().Tasks` walked via `GroupItem.Children` → `TimelinerTask` / [MultiReturn] | Implemented (v0.1) |
| TimelinerTask.Name | Navisworks.TimeLiner | task: TimelinerTask | name: string | Task display name. | `TimelinerTask.DisplayName` | Beta |
| TimelinerTask.Dates | Navisworks.TimeLiner | task: TimelinerTask | plannedStart: DateTime, plannedEnd: DateTime, actualStart: DateTime, actualEnd: DateTime | All four schedule dates (null when unset). | `TimelinerTask.PlannedStartDate`, `.PlannedEndDate`, `.ActualStartDate`, `.ActualEndDate` / [MultiReturn] | Beta |
| TimelinerTask.Type | Navisworks.TimeLiner | task: TimelinerTask | taskType: string | Simulation task type (Construct/Demolish/Temporary…). | `TimelinerTask.SimulationTaskTypeName` | Beta |
| TimelinerTask.AttachedItems | Navisworks.TimeLiner | document: Document, task: TimelinerTask | modelItems: List&lt;ModelItem&gt; | Model items attached to a task (audit unlinked geometry). | `TimelinerTask.Selection` (TimelinerSelection) resolved against the document | Beta |
| TimelinerTask.AttachSet | Navisworks.TimeLiner | task: TimelinerTask, setName: string, document: Document | task: TimelinerTask | Attach a saved selection/search set as a LIVE link (like the UI's Attach Set) — the core 4D-linking automation. | `DocumentSelectionSets.CreateSelectionSource(set)` → `Selection.SelectionSources` → copy-edit + `DocumentTimeliner.TaskEdit(index, copy)` (located via `TaskCreateIndexPath`) | Implemented (v0.2) |
| TimeLiner.AddTask | Navisworks.TimeLiner | document: Document, name: string, plannedStart: DateTime, plannedEnd: DateTime, taskType: string = "Construct" | task: TimelinerTask | Create a new root-level task. Replicates over lists for CSV-driven schedules. | `new TimelinerTask { DisplayName, PlannedStartDate, PlannedEndDate, SimulationTaskTypeName }` + `DocumentTimeliner.TaskAddCopy(root, task)` | Beta |
| TimelinerTask.SetDates | Navisworks.TimeLiner | task: TimelinerTask, plannedStart: DateTime, plannedEnd: DateTime, document: Document | task: TimelinerTask | Update a task's planned dates in place — bulk schedule edits without a re-import. | copy-edit pattern: `TimelinerTask.CreateCopy()` set dates + `DocumentTimeliner.TaskEdit(index, copy)` (located via `TaskCreateIndexPath`) | Implemented (v0.2) |
| TimeLiner.ImportTasksFromCsv | Navisworks.TimeLiner | document: Document, path: string, attachBySetName: bool = true | tasks: List&lt;TimelinerTask&gt;, report: string | One-node schedule import: CSV columns (name, start, end, type, set) → tasks with attachments; row-level error report. | composite: CSV.ReadFromFile + TimeLiner.AddTask + TimelinerTask.AttachSet | Future |

## Navisworks.Export

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Export.ViewpointImage | Navisworks.Export | filePath: string, width: int = 1920, height: int = 1080, document: Document | filePath: string | Render the current view to a .png/.jpg/.bmp file. Replicated after SavedViewpoint.Apply it becomes a batch screenshot factory. | COM bridge: `ComApiBridge.State.GetIOPluginOptions("lcodpimage")` (format/width/height options) + `DriveIOPlugin("lcodpimage", path, options)` | Implemented (v0.2) |
| Export.ClashReportCsv | Navisworks.Export | filePath: string, tests: List&lt;ClashTest&gt; = null, document: Document | filePath: string, rowCount: int | One-node clash CSV: test, group, result, status, distance, assigned-to, description, created date, item1/2 paths + GUIDs, clash point (null tests = every test). | flatten `ClashTest.Children` (groups included) + plain file IO | Implemented (v0.2) |
| Export.PropertyReportCsv | Navisworks.Export | modelItems: List&lt;ModelItem&gt;, categories: List&lt;string&gt;, properties: List&lt;string&gt;, path: string | path: string, rowCount: int | One-node property/QTO dump: one row per item, one column per requested property. | composite over Properties.Value + CSV.WriteToFile | Beta |
| Export.NWD | Navisworks.Export | filePath: string, document: Document | filePath: string | Publish the current document as .nwd (with any appearance overrides baked in). | `Document.SaveFile(path)` with .nwd extension | Implemented (v0.2) |

## Navisworks.Units

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Units.Convert | Navisworks.Units | value: double, fromUnits: string, toUnits: string | value: double | Convert a length value between unit systems (QTO normalization: model units → project units). | `Autodesk.Navisworks.Api.UnitConversion.ScaleFactor(Units from, Units to)` × value | Implemented (v0.1) |
| Units.ScaleFactor | Navisworks.Units | fromUnits: string, toUnits: string | factor: double | Raw multiplier between two unit systems (apply to areas/volumes by squaring/cubing). | `UnitConversion.ScaleFactor(Units, Units)` | Implemented (v0.1) |
| Units.All | Navisworks.Units | — | names: List&lt;string&gt; | Valid unit names accepted by the convert nodes. | `Enum.GetNames(typeof(Autodesk.Navisworks.Api.Units))` | Implemented (v0.2) |

## Coordination (v0.2 workflow nodes)

Nodes built directly from BIM-coordination pain-point research: clash-test scripting, auto-grouping (the gap three third-party plugins exist to fill), report/snapshot export, model QA audits, bulk set generation and QTO rollups. They live in the categories shown below; this section gathers them so coordinators can find the whole workflow in one place.

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| ClashTest.Create | Navisworks.Clash | name: string, itemsA: List&lt;ModelItem&gt;, itemsB: List&lt;ModelItem&gt;, testType: string = "Hard", tolerance: double = 0.01, document: Document | test: ClashTest | Script the weekly clash-test matrix: creates (or replaces) a test between two selections, ready for ClashTest.Run. | `new ClashTest`; `SelectionA/B.Selection.CopyFrom(items)`; `TestsAddCopy`/`TestsReplaceWithCopy`; stored copy re-fetched | Implemented (v0.2) |
| ClashTest.ResultsByStatus | Navisworks.Clash | test: ClashTest, status: string | results: List&lt;ClashResult&gt; | The results of a test with one status (New/Active/Reviewed/Approved/Resolved) — triage filtering. | flatten `ClashTest.Children`, filter `ClashResult.Status` | Implemented (v0.2) |
| ClashResult.SetDescription | Navisworks.Clash | result: ClashResult, description: string, document: Document | result: ClashResult | Set a result's description text (context for reports and reviews). | `DocumentClashTests.TestsEditResultDescription` | Implemented (v0.2) |
| ClashResult.Viewpoint | Navisworks.Clash | result: ClashResult, apply: bool = false, document: Document | viewpoint: Viewpoint | The camera viewpoint Navisworks generates for a clash; optionally applies it to the current view. | `DocumentClashTests.TestsViewpointForResult`; `CurrentViewpoint.CopyFrom` | Implemented (v0.2) |
| ClashResult.SaveImage | Navisworks.Clash | result: ClashResult, filePath: string, width: int = 1280, height: int = 720, document: Document | filePath: string | Renders a clash snapshot (scene + clash highlight) to .png/.jpg/.bmp — the picture half of every clash report; a snapshot folder via lacing. | `DocumentClashTests.TestsImageForResult(result, ImageGenerationStyle.ScenePlusOverlay, w, h)` → `Bitmap.Save` | Implemented (v0.2) |
| Clash.GroupResultsBySameItem | Navisworks.Clash | test: ClashTest, useItem1: bool = true, document: Document | test: ClashTest, groupCount: int | Groups all clashes involving the same element into one named group — the auto-grouping third-party plugins exist for. Singleton buckets stay ungrouped. | detached `CreateCopy()` repartitioned into `ClashResultGroup`s (identity via `InstanceHashCode` + `IsSameInstance`), committed with `TestsEditTestFromCopy` | Implemented (v0.2) |
| Clash.GroupResultsByProximity | Navisworks.Clash | test: ClashTest, radius: double, document: Document | test: ClashTest, groupCount: int | Clusters results whose clash points lie within a radius of the cluster seed — one issue per hotspot. | greedy clustering on `ClashResult.Center` (`Point3D.DistanceTo`) + `TestsEditTestFromCopy` commit | Implemented (v0.2) |
| Clash.GroupResultsByLevel | Navisworks.Clash | test: ClashTest, levelNames: List&lt;string&gt;, levelElevations: List&lt;double&gt;, document: Document | test: ClashTest, groupCount: int | Groups results by nearest level at/below each clash point (explicit level list — ClashResult carries no grid data in the 2024 API) — per-floor triage. | classify `Center.Z` against elevations + `TestsEditTestFromCopy` commit | Implemented (v0.2) |
| Export.ClashReportHtml | Navisworks.Export | filePath: string, tests: List&lt;ClashTest&gt; = null, includeImages: bool = false, imageWidth: int = 320, imageHeight: int = 240, document: Document | filePath: string, rowCount: int | Self-contained single-file HTML clash report (one section per test), optionally with embedded base64 snapshots — shareable without any add-in. | flatten results + `TestsImageForResult` → base64 PNG + string templating | Implemented (v0.2) |
| Audit.MissingProperty | Navisworks.Audit | category: string, property: string, items: List&lt;ModelItem&gt; = null, geometryOnly: bool = true, document: Document | items: List&lt;ModelItem&gt;, count: int | Every item NOT carrying a property (whole model or a scope) — the "find items missing property X" forum ask; batch over a property list via lacing. | `SearchCondition.HasPropertyByDisplayName(cat, prop).Negate()` + `FindAll` | Implemented (v0.2) |
| Audit.DuplicateItems | Navisworks.Audit | items: List&lt;ModelItem&gt;, tolerance: double = 0.001, document: Document | items1: List&lt;ModelItem&gt;, items2: List&lt;ModelItem&gt;, count: int | Finds double-exported geometry by running (and then removing) a temporary Duplicate clash test over the items. | throwaway `ClashTest { TestType = Duplicate, SelfIntersect }` + `TestsRunTest` + `TestsRemove` | Implemented (v0.2) |
| SelectionSets.BulkByPropertyValues | Navisworks.SelectionSets | category: string, property: string, folderName: string = null, document: Document | selectionSets: List&lt;SelectionSet&gt;, values: List&lt;string&gt; | One live search set per distinct value of a property (one set per Level / System / Category), optionally filed in a folder — kills hours of Find Items clicking. Re-runs replace, not duplicate. | distinct `DataProperty.Value` variants → `new SelectionSet(Search)` per value + `AddCopy`/`ReplaceWithCopy` (folder-aware) | Implemented (v0.2) |
| Takeoff.SumPropertyByGroup | Navisworks.Takeoff | items: List&lt;ModelItem&gt;, groupCategory: string, groupProperty: string, valueCategory: string, valueProperty: string | keys: List&lt;string&gt;, sums: List&lt;double&gt;, counts: List&lt;int&gt; | One-node QTO rollup: group items by a property and sum a numeric property per group (Volume per Level) — replaces Selection Inspector + Excel pivots. | property reads via VariantData coercion + in-memory grouping | Implemented (v0.2) |

---

## Future platform nodes (v1.0+ backlog, not counted per-category)

| Node | Category | Inputs (name: type) | Outputs (name: type) | Description | Maps to API | Tier |
|---|---|---|---|---|---|---|
| Python Script | Script | IN[0..n]: object | OUT: object | In-canvas IronPython/CPython script node with document access. | embedded interpreter | Future |
| C# Script | Script | IN[0..n]: object | OUT: object | In-canvas C# snippet compiled with Roslyn. | Roslyn scripting | Future |
| Geometry Preview | Output | modelItems / geometry | — | 3D thumbnail preview of items/bounding boxes on canvas. | UI viewport | Future |
| Package Manager | — | — | — | Discover/install community node packages (zero-touch DLL drop-in is the v0.1 story). | Core loader + registry | Future |
| Application.Documents | Navisworks.Application | — | documents: List&lt;Document&gt; | Multi-document automation. | MultiDocument API | Future |

## Reference workflows (what the MVP set must support end-to-end)

1. **Property extraction / QTO to CSV** — `Models.RootItems → ModelItem.Descendants → ModelItem.HasGeometry → List.FilterByBoolMask → Properties.Value("Element","Volume") → CSV.WriteToFile` (+ `List.GroupByKey` per system, implemented in v0.2).
2. **Bulk selection sets from property rules** — `String` (list of system names) → `Search.ByPropertyValue → SelectionSet.Create` with lacing, one set per value.
3. **Color-coding by system/status** — `Search.ByPropertyValue → Appearance.OverrideColor` fed by `Color.ByARGB`/`Color Picker`; `Appearance.ResetAll` first (v0.2) for idempotent re-runs.
4. **Clash triage + reporting** — `Clash.Tests → ClashTest.Results → ClashResult.Info / .Items → ModelItem.DisplayName → CSV.WriteToFile`; v0.2 adds `ClashResult.SetStatus`, `ClashResult.Assign`, `Viewpoints.FromClashResults`, `Export.ViewpointImage`.
5. **Viewpoint batch generation** — names list → `Viewpoint.SaveCurrent` / `SavedViewpoint.Apply` loops; the v0.2 camera nodes aim views automatically.
6. **Model QA audit** — `Properties.HasProperty` mask → `List.FilterByBoolMask` → count + `SelectionSet.Create("Missing <prop>")` + `Appearance.OverrideColor` red.
7. **TimeLiner 4D linking (v0.2)** — `CSV.ReadFromFile → TimeLiner.AddTask → TimelinerTask.AttachSet` by selection-set name.

## Tier totals

| Tier | Count |
|---|---|
| MVP (shipped v0.1) | 89 |
| Implemented (v0.1) | 4 |
| Implemented (v0.2) | 102 |
| Beta (not yet implemented) | 8 |
| Skipped | 1 |
| Future | 8 (in-catalog) + 5 platform backlog |
| **Total catalog** | **212** |

Registered node count as built: **100** general (`dyncamelo list-nodes`, includes the interactive Input/Output nodes) + **103** Navisworks = **203 nodes**. A few v0.1 nodes shipped under slightly different names than their catalog rows (`TimelinerTask.Info`/`Items` cover the `TimelinerTask.Name`/`Dates`/`Type`/`AttachedItems` rows; `Export.ToCsv` covers `Export.PropertyReportCsv`; plus `JSON.ReadFromFile`, `JSON.WriteToFile`, `BoundingBox.ByCorners`, `Units.Current`, `TimelinerTask.Create` beyond the catalog).
