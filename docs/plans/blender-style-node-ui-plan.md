# Blender-grade node UI for Dyncamelo — full implementation plan

**Status:** plan for review — no implementation code has been written.
**Baseline:** Dyncamelo v0.34.0 (`main` @ 2028676), Nodify 7.3.0, WPF/net48, Core on netstandard2.0.
**Inputs:** [`docs/research/blender-node-ui-study.md`](../research/blender-node-ui-study.md), the Dyncamelo source, the Nodify 7.3.0 source, and the Navisworks-facing node code.
**Scope:** the *full* system from the study. Nothing is deferred; where a piece is risky it has a spike, a fallback, and a place in the phase order — not an exit.

Evidence tags used throughout:

| Tag | Meaning |
|---|---|
| **[V]** | Verified by reading the cited file in this repo or in the Nodify 7.3.0 source. |
| **[S#]** | Depends on a fact I could not verify on this Linux container (no WPF rendering). Each has a numbered spike in §13 with a pass/fail criterion and a fallback. |
| **[P]** | Proposal — a design decision made in this document. |

---

## 0. What you will get, in one screen

1. **Rows, not columns.** Every node becomes a vertical stack: header → outputs → panels/settings → inputs. Inputs keep their sockets on the left edge and outputs on the right edge (as in Blender); each row carries its own socket and, for unwired inputs, its own editor. (Blender's anatomy, study §2.)
2. **Every unwired input is its own editor.** Number fields you drag / Shift-drag / double-click-type, toggles, dropdowns, text boxes, colour swatches, path pickers. Values persist in the `UserValue` slot that already serialises and already feeds the engine.
3. **Type language.** Socket colour = data family, socket shape = list depth, wire colour = source family, **dashed wire = automatic replication**, unwired-but-defaulted = hollow.
4. **Rope-style editing.** Drop a wire on empty canvas → filtered search; drag a node onto a wire → insert; Ctrl+X-delete reconnects; Alt-cut wires; mute a node / a wire; reroute dots; frames; auto-offset; grey-out incompatible sockets while dragging; hide-unused-sockets; collapse; panels; resize.
5. **Undo/redo for the whole editor.** Does not exist today (**[V]** — `GraphEditorViewModel` has no undo; §2.1). It is a prerequisite for every gesture above, so it is built first.
6. **Performance is designed in, not tuned after:** row virtualisation is unnecessary, but level-of-detail (LOD), frozen brushes, no bitmap effects, cached geometry, wire hit-testing on demand, and an in-app diagnostics HUD that reports frame time and visual counts on the user's machine — because I cannot measure WPF here.

---

## 1. Decisions (the four open questions from the study, resolved)

You did not answer the study's questions, and you asked for no deferrals, so these are the assumptions the plan is built on. Each is cheap to reverse because it lives in one place.

| # | Question | Decision **[P]** | Where it lives |
|---|---|---|---|
| Q1 | Typed defaults? | **Yes, all of them.** Every unwired input whose type is number/int/bool/string/enum/choice/colour/path gets an inline editor. | `InputEditorKind` (§6.1) |
| Q2 | Scrub or type? | **Both, Blender semantics:** click-drag scrubs, Shift = fine, Ctrl = snap, double-click / Enter = type, Tab moves to the next field, Esc reverts. | `ScrubNumberBox` (§6.2) |
| Q3 | Colour-blind? | **Yes.** Palette is Okabe–Ito-derived; colour is *never* the only signal (shape = list depth, tooltip = type name, optional label chip). Verified in unit tests with CVD simulation (§4.3). | `PortKindPalette` (§4.3) |
| Q4 | Daily gesture? | **Wire-drop-to-search** and **insert-on-wire** are treated as the two headline gestures and get the most polish; everything else in the study ships too. | §7 |
| Q5 | Which edges hold the sockets? | **Same as Blender and as Nodify today: inputs on the left edge, outputs on the right edge**, each socket centred on the edge line (half outside the card). The *only* layout change is vertical: instead of Nodify's side-by-side input/output columns, rows are stacked — output rows first, then inputs, each input row carrying its own editor. Wires still flow left→right. (An earlier draft of this table framed it as "left/right vs Blender", which was wrong: Blender is left/right too.) | §5.2 |
| Q6 | Palette theming vs. fixed socket colours? | **Two systems.** Chrome (canvas, body, text) keeps the live-swappable `PaletteCatalog`. Socket-family colours are *semantic* and fixed, so they can be frozen (perf) and stay recognisable across palettes. | §4.3, §8.2 |

---

## 2. Ground truth: what the code does today

Everything the plan builds on, with the line-level evidence.

### 2.1 There is no undo layer **[V]**

`GraphEditorViewModel` mutates the model directly and immediately:

```csharp
// GraphEditorViewModel.cs:1106  DeleteSelection
foreach (var connection in SelectedConnections.ToList()) _graph.Disconnect(connection.Model);
foreach (var item in SelectedItems.ToList())
    if (item is NodeViewModel node) _graph.RemoveNode(node.Model); ...
```

`grep -n "Undo" src/**/*.cs` returns nothing. Every gesture in the study (insert-on-wire, cut, delete-reconnect, swap) is a multi-step graph edit that users will get wrong once in a while; shipping them without undo is not acceptable. **Undo is Phase 1.**

The good news: the model already has the primitives an undo journal needs — `GraphModel` raises `NodeAdded/NodeRemoved/ConnectionAdded/ConnectionRemoved/Modified` **[V]** (`GraphModel.cs:68-83`), and `GraphSerializer.SerializeFragment` / `PasteFragment` exist **[V]** (`GraphSerializer.cs:225,267`) so a removed node can be restored from JSON.

### 2.2 Ports: what a port knows **[V]** (`PortModel.cs`)

`Name`, `DeclaredType` (CLR type), `Direction`, `HasDefault`, `DefaultValue`, `UsingDefaultValue`, `Choices`, `HasUserValue`/`UserValue`, `Level`/`UseLevels`/`KeepListStructure`, cached `Value` for outputs. No range, no kind, no panel, no editor hint.

**`UserValue` already persists and already drives evaluation:**

```csharp
// GraphSerializer.cs:401
if (port.HasUserValue) { ... json["UserValue"] = JToken.FromObject(port.UserValue); }
// GraphEngine.cs:174
else if (port.HasUserValue) { inputs[i] = port.UserValue; }   // wins over DefaultValue
```

So inline editors need **no new persistence and no engine change** — they call `Port.SetUserValue(...)`. This is the single most useful existing fact for the whole plan. Only `IsSerializablePrimitive` values are written, which is exactly the set we edit inline.

### 2.3 Connection rules are deliberately loose **[V]** (`TypeCoercion.CanConvert`, `GraphModel.Connect`)

`CanConvert` returns true for anything involving `object`, any list, or two scalar-convertibles (`TypeCoercion.cs:209-229`). `Connect` then rejects only self-links, cycles, direction errors. Consequences for the UI:

* "Grey out incompatible sockets while dragging" cannot use `CanConvert` alone — it says yes to almost everything. It needs a *stricter, advisory* compatibility level on top (§4.4): **exact / convertible / loose / no**. Only "no" greys out; "loose" is dimmed, not blocked.
* An input accepts one wire; connecting replaces the old one (`GraphModel.cs:171`). The swap gesture and insert-on-wire must go through `Connect` so this holds.

### 2.4 Untyped (`object`) ports are the majority of outputs **[V]** (tally of `docs/dyncamelo-nodes.json`, 364 nodes)

`any` is 334 of 1 404 ports (24 %); counting `any[]` too it is 431 (31 %). The split matters: **inputs** are 153 of 880 (17 %), but **outputs** are **278 of 524 (53 %)** — most are `[MultiReturn]` outputs (`Viewpoints.InFolder` → viewpoints/names/subfolders/count). A colour system keyed only on `DeclaredType` would paint more than half of all output sockets "unknown grey". §4.2 solves this with an explicit attribute plus a *runtime-observed* kind.

### 2.5 The node visual is Nodify's default `Node` with a `HeaderTemplate` **[V]** (`DyncameloDark.xaml:1329-1375`)

```xml
<nodify:Node Header="{Binding}" HeaderBrush="{Binding HeaderBrush}"
             Background="{StaticResource Dyc.NodeBodyBrush}" ContentBrush="{StaticResource Dyc.NodeBodyBrush}"
             Input="{Binding Inputs}" Output="{Binding Outputs}"> ... <ContentControl Content="{Binding Model}"
             ContentTemplateSelector="{StaticResource Dyc.NodeBodySelector}"/>
```

Nodify's `Node` has `PART_Input` / `PART_Output` `ItemsControl`s side by side with the content **[V]** (`Nodify/Nodes/Node.cs:13-21`). Row layout therefore means **re-templating `Node`**, not styling it. That is supported: `TemplatePart`s are optional and `Connector` finds its editor by visual-tree walk, not by living in `PART_Input`:

```csharp
// Nodify/Connectors/Connector.cs:190-191
Container = this.GetParentOfType<ItemContainer>();
Editor    = Container?.Editor ?? this.GetParentOfType<NodifyEditor>();
```

A `NodeInput`/`NodeOutput` placed anywhere inside the item container therefore still registers its `Anchor`. (Spike **S1** confirms with a 5-row test node; fallback in §13.)

### 2.6 The body-template system already exists and is the right seam **[V]**

`NodeBodyTemplateSelector` maps `NumberInputNode`, `NumberSliderNode`, `StringInputNode`, `WatchNode`… to `NodeBody.*` `DataTemplate`s. These special nodes keep working unchanged; they simply render inside the new "body" row (§5.2). The new per-port editors are a *second*, port-level seam.

### 2.7 Wires are Nodify `Connection`s with fixed curvature **[V]**

`Connection` draws a cubic Bézier with private `_baseOffset = 100`, `_offsetGrowthRate = 25` (`Connections/Connection.cs:19-20`). Curvature is not a public property. But `BaseConnection.DrawLineGeometry` is `protected abstract` **[V]** (`BaseConnection.cs:644`), so a subclass `DycWire : BaseConnection` can own the geometry — required for a Blender-like softer curve and for cheap LOD (straight line when zoomed far out).

### 2.8 Live palette swapping mutates shared *unfrozen* brushes **[V]** (`PaletteCatalog.cs` header comment)

> "Applied live by mutating the shared (unfrozen) `SolidColorBrush` instances' `.Color` in place."

So **chrome brushes must stay unfrozen and shared** (few instances — fine) while **per-family socket brushes are frozen** (semantic, many uses). Mixing these up would either break palette swapping or forfeit the freeze optimisation. Decision Q6.

### 2.9 Nodify facts that constrain the design **[V]**

| Fact | Source | Consequence |
|---|---|---|
| No virtualisation of items | `NodifyEditor` uses a plain canvas panel | Cost is O(visible visuals); our lever is *fewer, simpler visuals per node* (LOD + row templates without nested effects). |
| Cutting exists: gesture `Alt+Shift+LeftClick`; crossed wires are removed through `RemoveConnectionCommand` | `EditorGestures.cs:314`, `NodifyEditor.Cutting.cs:216` | Cut = free, and it already goes through our command → we only wrap it in an undo step. |
| `AutoPanOnNodeFocus` static, default true | `NodifyEditor.cs` | Already set false in v0.34.0. New focus-moving gestures (Tab between fields!) must not re-enable it. |
| `KnotNode` (reroute dot) and `GroupingNode` (frame) ship in the library | `Nodes/KnotNode.cs`, `GroupingNode.cs` | Reroutes and frames are re-skins + a Core node type, not new controls. |
| Push-items strategy is public: `BeginPushingItems(Point, Orientation)` | `NodifyEditor.PushingItems.cs:77` | Auto-offset ("make room when inserting") reuses Nodify's own algorithm. |
| `Minimap` control ships | `Minimap/Minimap.cs` | Minimap = template + binding. |
| Connector needs an `ItemContainer` ancestor | `Connector.cs:190` | Sockets must live inside the node's container (they will). |

---

## 3. Libraries: what we adopt, what we refuse

| Need | Choice | Licence | Why / evidence | Rejected |
|---|---|---|---|---|
| Node canvas | **Nodify 7.3.0** (already used) | MIT | Everything in §2.9 is already there; we re-template rather than replace. | Replacing the canvas would re-implement pan/zoom/selection/pending-connection/cutting/minimap — thousands of lines with no user-visible gain. |
| Auto-arrange | **MSAGL** — NuGet `AutomaticGraphLayout` **1.1.12** (one 1.5 MB assembly) | MIT upstream (microsoft/automatic-graph-layout); the nuspec itself carries no licence element, so spike **S7** records the licence text from the repo before we ship | I downloaded the package: single `lib/netstandard2.0/AutomaticGraphLayout.dll`, no dependency groups, and the binary contains `Microsoft.Msagl.Layout.Layered` (the Sugiyama engine). netstandard2.0 loads on net48 *and* is usable from Core tests. Layered layout gives crossing-minimised placement with per-node sizes. | The existing `GraphLayout` (Core) is a longest-path column layout with no crossing reduction (**[V]** `GraphLayout.cs`); kept as the fallback and for tiny selections. |
| Colour maths for tests (CVD simulation, ΔE) | **Colourful** | MIT | Test-only dependency (`Dyncamelo.Core.Tests`); never shipped. | Hand-rolled Lab conversion — easy to get subtly wrong. |
| Number-field control | **Custom `ScrubNumberBox`** (~300 lines) | ours | The widget *is* the Blender identity; no library implements Blender's semantics (drag-scrub + Shift/Ctrl modifiers + in-place typing + Tab chain + arrow-step regions). Xceed/MahApps/HandyControl numeric boxes are spinners with heavy templates; 200+ of them on a canvas would cost more than the feature is worth. | Xceed IntegerUpDown, MahApps NumericUpDown. |
| Colour picker | **Existing `ColorPickerDialog`** **[V]** (`Views/ColorPickerDialog.xaml`) | ours | Already in-repo; the inline widget is a swatch that opens it. | New library. |
| Icons | **Existing `StreamGeometry` set** **[V]** (`Dyc.Icon.*`) | Material-derived | Add ~12 more geometries (mute, collapse chevron, pin, reroute, frame). | Icon font / package. |
| Undo | **Own `UndoManager`** in Core (~250 lines) | ours | The domain is small (graph edits) and must be Navisworks-agnostic and unit-testable on Linux. | `Undo.Net`, `Stateless`-style packages: solve UI-property undo, which is not our problem. |

**Dependency hygiene.** MSAGL is the only new *runtime* dependency: one DLL, netstandard2.0, no transitive packages **[V]**. Shipping it needs edits in **four explicit file lists** — the plugin layout is an allow-list, not a glob, so a missed entry means "works on my build, `FileNotFoundException` in Navisworks":

| File | Current list **[V]** | Change |
|---|---|---|
| `src/Dyncamelo.App/Dyncamelo.App.csproj:80` | `Nodify.dll;Newtonsoft.Json.dll` | add `AutomaticGraphLayout.dll` |
| `.github/workflows/release.yml:85` | `'Nodify.dll', 'Newtonsoft.Json.dll'` | add `'AutomaticGraphLayout.dll'` |
| `dist/Dyncamelo.bundle/2024/PLACE_DYNCAMELO_DLLS_HERE.txt:8` | lists `Nodify.dll` | add it (doc) |
| `docs/GETTING_STARTED.md:49` | lists `Nodify.dll` | add it (doc) |

`Dyncamelo.UI.csproj` adds one `PackageReference`. Spike **S7** proves the load inside the Navisworks add-in context (shared process; `DyncameloHost` already installs an `AssemblyResolve` handler for a similar reason **[V]** `DyncameloHost.cs:41`) *before* any layout code depends on it; if MSAGL cannot load, the existing Core `GraphLayout` remains the arrange engine and only the MSAGL arrange upgrade (§9.1) is lost.

---

## 4. Foundations (Core, testable on Linux)

Everything in this phase is pure C#, has unit tests that run in the existing `dotnet test` job, and changes **no** visuals.

### 4.1 Port descriptors: `PortKind`, range, panel, editor hint

**New attributes** (`Dyncamelo.Core/Loader/Attributes.cs`, next to `NodeChoicesAttribute` **[V]** line 102):

```csharp
/// <summary>Soft/hard bounds and step for a numeric parameter (drives the scrub field).</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class NodeRangeAttribute : Attribute
{
    public NodeRangeAttribute(double min, double max) { Min = min; Max = max; SoftMin = min; SoftMax = max; }
    public double Min { get; }  public double Max { get; }
    public double SoftMin { get; set; }  public double SoftMax { get; set; } // slider extent; typing may exceed
    public double Step { get; set; } = double.NaN;                          // NaN → derive from type
    public string Unit { get; set; } = "";                                  // "mm", "°", "%"
}

/// <summary>Groups optional parameters into a collapsible panel ("Advanced").</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class NodePanelAttribute : Attribute
{
    public NodePanelAttribute(string name) { Name = name; }
    public string Name { get; }
    public bool DefaultOpen { get; set; }
}

/// <summary>Semantic kind for ports whose CLR type is <c>object</c> (see §4.2).</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.Method)]
public sealed class PortKindsAttribute : Attribute
{
    public PortKindsAttribute(params string[] kinds) { Kinds = kinds; }   // "viewpoint*", "string*", "int", "any"
    public string[] Kinds { get; }
}
```

`PortDescriptor` (**[V]** `Loader/PortDescriptor.cs`) gains `Range`, `Panel`, `PanelDefaultOpen`, `Kind`; `AssemblyNodeLoader` reads them next to where it already reads `NodeChoices` (same reflection site, so no new pass). `PortModel` gets matching `internal set` properties, exactly as `Choices` is done today **[V]** (`PortModel.cs:66`). None of this is serialised (it is static metadata), so `.dyc` is untouched.

**Backwards compatibility:** all new attributes are optional. A parameter without `[NodeRange]` gets a *derived* range: `int` → unbounded step 1; `double` → unbounded, step derived from the default's magnitude (§6.2). Nothing breaks if a node pack ignores the attributes.

**Backfill** (part of the plan, not a deferral): a one-off audit pass adds `[NodeRange]` to the parameters where a range is obviously meaningful (opacity 0-1, transparency 0-100, angles, distances with units, clash tolerance, `count/limit` ≥ 0), and `[NodePanel("Advanced")]` to the trailing optional parameters of the widest nodes. A generated report (`docs/dyncamelo-nodes.json` already lists every port) proves coverage; a ratchet test (§11) prevents regressions.

### 4.2 `PortKind`: colour/shape without knowing Navisworks types

The UI project has **no Navisworks reference** **[V]** (`Dyncamelo.UI.csproj`: description "Host-agnostic: no Navisworks dependencies"), so kinds must be derived from *type names*, and from data for the `object` ports.

```csharp
namespace Dyncamelo.Core.Editing;

public enum PortFamily { Any, Number, Integer, Boolean, Text, DateTime, Colour, Geometry, Item, Selection,
                         Viewpoint, Clash, Document, Data, File, Action }
public enum PortDepth  { Item, List, Nested, Unknown }      // shape axis

public readonly struct PortKind { public PortFamily Family; public PortDepth Depth; public bool IsInferred; }

public static class PortKinds
{
    public static PortKind FromPort(PortModel p);            // static metadata → declared kind
    public static PortKind Observe(PortModel p);             // runtime value → observed kind (outputs)
    public static Compat  Compare(PortKind from, PortKind to);
}
```

**Resolution order for a port** (first hit wins):

1. `[PortKinds]` explicit annotation (used for `object`/`MultiReturn` outputs).
2. `DeclaredType` mapping by *simple type name* (table below) — `IList<T>`/`T[]`/`IEnumerable<T>` → depth `List` with family of `T`, nested generics → `Nested`.
3. **Observed** kind: after a run, `PortModel.Value` is inspected once (type of first non-null element, list nesting depth up to 3) and cached on `ConnectorViewModel`. Inference is O(1) per port per run (first element only), never per frame.
4. `Any` (neutral diamond).

Type-name table (from the 20+ distinct port types in the shipped catalog — I counted them from `docs/dyncamelo-nodes.json`; *inputs* 880 ports: string 206, Document 135, number 99, any 86, any[] 67, boolean 56, integer 47, ModelItem[] 46 …; *outputs* 524 ports of which **`any`+`any[]` = 278 (53 %)**, so the explicit/observed path is not an edge case, it is the majority of outputs):

| Family | Matches (simple name) |
|---|---|
| Number | `Double`, `Single`, `Decimal` |
| Integer | `Int32`, `Int64`, `Int16`, `Byte`, any `enum` |
| Boolean | `Boolean` |
| Text | `String` |
| DateTime | `DateTime`, `TimeSpan`, `DateTimeOffset` |
| Colour | `Color` (Navisworks, `System.Windows.Media`, `System.Drawing`) |
| Geometry | `Point`, `Point3D`, `Vector`, `Vector3D`, `BoundingBox*`, `Transform*`, `Rotation*` |
| Item | `ModelItem`, `Model`, `Units` |
| Selection | `ModelItemCollection`, `SelectionSet`, `SelectionSource` |
| Viewpoint | `Viewpoint`, `SavedViewpoint`, `Camera`, `SavedViewpointAnimation*` |
| Clash | `ClashResult*`, `ClashTest`, `ClashResultGroup`, `ClashResultGroupBase` |
| Document | `Document`, `DocumentModels` |
| Data | `IDictionary*`, `DataProperty*`, `PropertyCategory`, `ParamMapRule` |
| File | port name ends with `path`/`file`/`directory` **and** type `String` (checked *after* Text so only named paths flip) |
| Action | `IWorkflowAction` |
| Any | `Object` or unrecognised |

The rule is by simple name + generic argument, not full name, so the UI project needs no reference to `Autodesk.Navisworks.Api`. A unit test feeds every distinct `DeclaredType.FullName` in `dyncamelo-nodes.json` through `PortKinds.FromPort` and asserts none of the ≥ 10-use types falls through to `Any`.

**Depth (shape) source:** declared type first; for `object` ports, the observed value (list → `List`, list-of-lists → `Nested`). This is also what powers the dashed replicating wire (§4.5).

### 4.3 The palette (colour-blind-safe by construction)

Base hues from the **Okabe–Ito** set (designed for the three common CVD types), extended with lightness steps for families that need more than eight distinct entries. Colour is redundant with shape (depth) and text (tooltip), so the palette does not have to carry the whole burden.

```csharp
public static class PortKindPalette
{
    // sRGB hex, Okabe–Ito-derived. Semantic and fixed → frozen brushes (§8.2).
    public static string Hex(PortFamily f) => f switch {
        PortFamily.Number    => "#56B4E9", // sky blue
        PortFamily.Integer   => "#0072B2", // blue
        PortFamily.Boolean   => "#D55E00", // vermillion
        PortFamily.Text      => "#F0E442", // yellow
        PortFamily.DateTime  => "#CC79A7", // reddish purple
        PortFamily.Colour    => "#E8E8E8", // near-white (chip shows the actual colour when known)
        PortFamily.Geometry  => "#009E73", // bluish green
        PortFamily.Item      => "#E69F00", // orange
        PortFamily.Selection => "#B36B00", // dark orange (same hue family as Item)
        PortFamily.Viewpoint => "#7B68EE", // violet
        PortFamily.Clash     => "#C0392B", // brick red
        PortFamily.Document  => "#6B7785", // steel grey (context plumbing, deliberately quiet)
        PortFamily.Data      => "#8FBC8F", // sage
        PortFamily.File      => "#A9865B", // tan
        PortFamily.Action    => "#FF8FAB", // pink
        _                    => "#9AA3AD", // Any
    };
}
```

**Verification (Linux, in CI):** a test converts each pair to CIELAB with Colourful, applies Machado-2009 protan/deutan/tritan simulation matrices, and asserts **ΔE₀₀ ≥ 12** between every pair of *adjacent-in-menu* families and ≥ 8 between all pairs, and that every colour has ≥ 3:1 contrast against `Dyc.NodeBodyBrush` in all built-in palettes (`PaletteCatalog`). Any failing pair is a test failure with the pair named — so the palette is tuned by evidence, not by eye. The exact hexes above are a starting point that the test may force me to adjust.

### 4.4 Compatibility levels (the "grey out while dragging" oracle)

```csharp
public enum Compat { Exact, Convertible, Loose, No }
```

* **Exact** — same family and depth, or `Any` on both sides.
* **Convertible** — a registered converter exists (`TypeCoercion.HasConverterFor`, currently private at `TypeCoercion.cs:532` **[V]** — exposed as `internal` for this), or numeric family widening (Integer→Number), or item→list wrap.
* **Loose** — `CanConvert` says true only because one side is `object`/list (`TypeCoercion.cs:209-224`). Wire is allowed; socket is **dimmed 40 %**, not hidden.
* **No** — `!CanConvert`, same-node, same-direction, or would create a cycle (`GraphModel.IsReachable` **[V]**). Socket is **greyed 15 %** and shows a not-allowed cursor.

During a pending connection the editor computes a `Compat` for every socket **once at drag start** (O(sockets), ≤ few thousand, one pass, results cached in a `Dictionary<ConnectorViewModel, Compat>`), never on mouse-move. Cycle detection per candidate uses one reachability pass from the source node (a `HashSet<NodeModel>` of downstream nodes, `GraphModel.CollectDownstream` **[V]** line 249), so it is O(V+E) once, not per socket.

### 4.5 Replication awareness (dashed wires)

A wire "replicates" when the source's rank exceeds the target's *and* the target does not consume levels. Rank comes from `PortKind.Depth` (observed after run, declared before). `ConnectionViewModel` gets `bool IsReplicating`, recomputed on (a) run completion and (b) connection add/remove — never on render. The wire style binds `StrokeDashArray` to a frozen `DoubleCollection` when true.

### 4.6 Undo/redo: a command journal in Core

**Why not snapshots?** Serialising the whole graph per step rebuilds every `NodeModel`/`NodeViewModel`, loses selection, invalidates every cached output (forcing a full re-run of Navisworks queries), and costs O(graph) per edit. A command journal keeps identity (same `Guid`s, same view-models), costs O(changed), and lets us coalesce drags.

```csharp
namespace Dyncamelo.Core.Editing;

public interface IUndoStep { string Label { get; } void Undo(); void Redo(); }

public sealed class UndoManager
{
    public bool CanUndo { get; } public bool CanRedo { get; }
    public string? UndoLabel { get; } public string? RedoLabel { get; }
    public event EventHandler? Changed;

    public IDisposable Begin(string label);       // transaction: all steps recorded until Dispose become ONE undo item
    public void Record(IUndoStep step);            // no-op while replaying (re-entrancy guard)
    public void Undo();  public void Redo();
    public void Clear();                           // on New/Open
    public int Capacity { get; set; } = 200;      // ring; oldest dropped
}
```

Design rules **[P]**:

* **Replay guard.** `Undo()`/`Redo()` set `IsReplaying`; `Record` ignores calls while it is true, so replaying `Connect` (which raises `ConnectionAdded` → the VM → maybe records) cannot re-enter.
* **Steps are inverse operations on the model**, not on the view. View-models react to `GraphModel` events already (**[V]** `GraphEditorViewModel.AddConnectionViewModel` …), so undo needs no view code.
* **Coalescing:** `MoveNodesStep` merges consecutive moves of the same node set within 400 ms with no other step in between; `SetValueStep` merges consecutive edits of the same port (a scrub drag = one step, not 200).
* **Auto-run interaction:** undo triggers `GraphModel.Modified`, which already schedules auto-run **[V]** (`GraphEditorViewModel.OnGraphModified`, line 950) — so Undo re-evaluates exactly like any edit. Nothing special.
* **Navisworks side effects are not undoable and are never claimed to be.** A node that hid items or created a viewpoint has changed the *document*, not the graph. Undo reverts the graph; the status bar says "Undid *Delete 3 nodes* (graph only)" whenever a run has happened since that step. This is an honest limit, stated once in the docs.

**Step catalogue** (each ≤ 40 lines, each with a Core unit test that runs it forward/back and compares `GraphSerializer.Serialize` output byte-for-byte):

| Step | Undo does | Data captured |
|---|---|---|
| `AddNodesStep` | remove nodes | node ids |
| `RemoveNodesStep` | `PasteFragment` with **original ids** + re-`Connect` external wires | `SerializeFragment` JSON + list of `(srcNodeId, srcPort, dstNodeId, dstPort)` for wires leaving the fragment |
| `ConnectStep` | `Disconnect`, and **re-`Connect` the wire that was replaced** | new wire, replaced wire (if any) |
| `DisconnectStep` | `Connect` | wire endpoints |
| `MoveNodesStep` | set `X/Y` | ids, old/new positions |
| `SetPortValueStep` | `SetUserValue`/`ClearUserValue` | port ref, old/new |
| `SetPortStateStep` | levels, `UsingDefaultValue`, hidden flag | port ref, old/new |
| `RenameStep`, `FreezeStep`, `MuteStep`, `LacingStep`, `CollapseStep`, `ResizeStep` | restore property | node id, old/new |
| `NoteStep`, `GroupStep` (create/move/resize/recolour/delete) | inverse | model refs |
| `CompositeStep` | undo children in reverse | produced by `Begin(label)` |

Required Core changes: `GraphSerializer.PasteFragment` currently mints new ids (paste semantics) — add an overload `PasteFragment(GraphModel, string json, double dx, double dy, bool preserveIds)` (**[V]** signature at `GraphSerializer.cs:267`; the `Id` setter is `internal`, and the serializer lives in the same assembly, so this is a 10-line change). `PortRef` = `(Guid nodeId, PortDirection dir, string name)` (port names are the serialised identity **[V]** `PortModel.cs:39`).

**Hook sites in `GraphEditorViewModel`** (each is a single wrapped call): `CompletePendingConnection` (984), `DisconnectConnector` (1006), `RemoveConnection` (1032), `AddNodeFromParameter` (1042), `DeleteSelection` (1106), `ArrangeSelection` (1137), `DuplicateSelection` (1270), paste, group/ungroup, note edit, node drag-end (from the `ItemContainer.Location` two-way binding, recorded on drag *completion*, not on every location change), port value edits, lacing/freeze menu items. Keyboard: **Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z**, plus toolbar buttons with the step label as tooltip ("Undo Delete 3 nodes").

### 4.7 Mute / bypass at the engine level

Blender's *mute* (M) makes a node pass its first compatible input through to its first compatible output. Required engine support, ~30 lines in `GraphEngine.ExecuteNode` **[V]** (line 154):

```csharp
// NodeModel:  public bool IsMuted { get; set; }   // persisted, MarkDirty on change
// ConnectionModel: public bool IsMuted { get; set; } // muted wire = treated as unconnected

// In ExecuteNode, after inputs[] are gathered:
if (node.IsMuted)
{
    var pass = MutePassThrough.Resolve(node, inputs);     // pure function, unit-tested
    SetOutputs(node, pass);                               // outputs[j] = inputs[i] for the first type-compatible pairs, else null
    node.State = NodeState.Executed;                      // shown as "muted" by the view, not as an error
    return;
}
// And in the input loop:  if (connection != null && !connection.IsMuted) { ... } else if (HasUserValue) ...
```

`MutePassThrough.Resolve` pairs outputs to inputs in order by `PortKinds.Compare != No`, so muting `Isolate` forwards its item input to its item output — the behaviour Blender users expect from **M**. Muted nodes are **not executed**, so a muted `Viewpoints.Save` truly does nothing — this is also the answer to "I want to disable this branch without deleting it".

**Difference from the existing `IsFrozen`** **[V]** (`NodeModel.cs:108`): freeze keeps the *last outputs* and skips re-execution; mute *replaces* outputs by pass-through. Both stay; the context menu labels make the difference explicit.

### 4.8 Additive `.dyc` fields

`GraphSerializer` is version-1 additive **[V]** — unknown fields are ignored by older builds and absent fields default on load. New optional fields, all with safe defaults:

```jsonc
// node
"Ui": { "Collapsed": true, "Width": 240, "HideUnused": true, "OpenPanels": ["Advanced"] },
"Muted": true,
// connection
"Muted": true,
// port (existing object)
"Hidden": true                     // user-hidden unused socket
```

Round-trip tests: (a) load every sample in `samples/` and re-save — output must be structurally equal except for normalised fields (exists already as `SampleGraphFileTests` **[V]**, extend it); (b) a v0.34 file with none of the new fields loads with all defaults; (c) a file with the new fields loaded by the *current* serializer (simulated by stripping unknown keys) still runs — proving forward compatibility on the reader side.

### 4.9 Reroute node

`RerouteNode : NodeModel` in Core: one input `object`, one output `object`, `Evaluate` returns the input unchanged (so lists, nulls, everything pass through untouched — no coercion, no replication, no messages). Its `PortKind` is *inherited*: `PortKinds.Observe` for its ports returns the upstream port's kind, so the dot takes the family colour of whatever flows through. `NodeType = "Reroute"`, registered in `NodeRegistry.CreateDefault()` **[V]** so `.dyc` round-trips. Nodify's `KnotNode` **[V]** is the visual.

---

## 5. The visual system (Phase 2)

### 5.1 Design tokens

One new dictionary, `Themes/Dyc.Tokens.xaml`, merged before `DyncameloDark.xaml`. Every size in the node UI comes from here, so "uniform appearance" is enforced by construction (the study's principle: one row height, one radius, one gutter).

```xml
<sys:Double x:Key="Dyc.RowHeight">22</sys:Double>
<sys:Double x:Key="Dyc.HeaderHeight">26</sys:Double>
<sys:Double x:Key="Dyc.SocketSize">10</sys:Double>
<sys:Double x:Key="Dyc.NodeMinWidth">168</sys:Double>
<sys:Double x:Key="Dyc.NodeMaxAutoWidth">300</sys:Double>
<CornerRadius x:Key="Dyc.NodeRadius">6</CornerRadius>
<CornerRadius x:Key="Dyc.FieldRadius">4</CornerRadius>
<Thickness   x:Key="Dyc.RowPadding">14,0,14,0</Thickness>   <!-- leaves room for the half-outside socket -->
<sys:Double x:Key="Dyc.FontSize">11</sys:Double>
```

Chrome colours keep using the existing `Dyc.*Brush` keys (live-swappable palettes, §2.8). New chrome keys added to `PaletteCatalog.BrushKeys` **[V]** (currently 18): `Dyc.FieldBrush`, `Dyc.FieldHoverBrush`, `Dyc.FieldFillBrush` (the slider "value bar"), `Dyc.HeaderTextBrush`. Each built-in palette gets values for them (a palette missing a key is a compile-time-visible omission because `BrushKeys` is the canonical list and a unit test asserts every palette defines every key).

### 5.2 The row model (view-model side)

The single structural change that makes everything else simple: a node's visual is an ordered list of **rows**, computed in the view-model, not assembled in XAML triggers.

```csharp
public abstract class NodeRowViewModel : ObservableObject { public NodeViewModel Node { get; } }
public sealed class OutputRowViewModel : NodeRowViewModel { public ConnectorViewModel Connector { get; } }
public sealed class InputRowViewModel  : NodeRowViewModel { public ConnectorViewModel Connector { get; } }   // socket + label + editor
public sealed class BodyRowViewModel   : NodeRowViewModel { public NodeModel Model { get; } }               // existing NodeBody.* templates
public sealed class PanelRowViewModel  : NodeRowViewModel { public string Title; public bool IsOpen; public int ModifiedCount; }
public sealed class HiddenSummaryRow   : NodeRowViewModel { public int Count; }                              // "+3 hidden" chip
```

`NodeViewModel.Rows` (an `ObservableCollection<NodeRowViewModel>`) is **rebuilt only on structural events**: port added/removed (`SyncPorts` **[V]** already exists), collapse toggled, hide-unused toggled, panel opened/closed, a port connected/disconnected (because "connected ports are never hidden"). It is never touched by value changes, evaluation, or scrolling.

**Order** (Blender §2): outputs → body (special nodes such as Watch/Slider) → ungrouped inputs → panels (header row, then their inputs when open).

**Hide-unused rules** (`HideUnused` is per node, default **on for nodes with ≥ 6 ports**, toggle Ctrl+H or header menu):

* An **input** is hidden iff it is unconnected **and** `HasDefault` (optional) **and** (`Kind.Family == Document` **or** the node's `HideUnused` is on **and** the port has never had a user value).
* An **output** is hidden iff it is unconnected and `HideUnused` is on.
* A connected port, and any port with a pinned `UserValue`, is **never** hidden — so nothing the graph depends on can disappear.
* Individual ports can be pinned visible/hidden from the socket's context menu (persisted as `"Hidden"` in §4.8).
* A trailing "+N hidden" row (click to reveal, chevron) makes hidden state discoverable.

Why `Document` is auto-hidden: **135 of the 880 catalog inputs (15 %) are the trailing optional `Document? document = null`** **[V]** (catalog tally; the convention is visible in `SavedItemTreeNodes.cs`, e.g. `Viewpoints.InFolder(..., Document? document = null)`). It is pure context plumbing, almost never wired; showing it on 135 nodes is the largest single source of visual noise, and hiding it changes no behaviour (the port keeps its default).

### 5.3 Re-templating the node (XAML, abridged)

Nodify's `Node` is dropped for the *visual* (its `PART_Input`/`PART_Output` split is the thing we are replacing, §2.5). The `ItemContainer` (drag, select, focus, location, size) stays. The `DataTemplate` for `NodeViewModel` becomes:

```xml
<DataTemplate DataType="{x:Type vm:NodeViewModel}">
  <Grid x:Name="Root" MinWidth="{StaticResource Dyc.NodeMinWidth}" Width="{Binding Width}">
    <Border x:Name="Card" CornerRadius="{StaticResource Dyc.NodeRadius}"
            Background="{StaticResource Dyc.NodeBodyBrush}" BorderBrush="{StaticResource Dyc.NodeBorderBrush}"
            BorderThickness="1" SnapsToDevicePixels="True">
      <StackPanel>
        <local:NodeHeader/>                                   <!-- category bar, fn dot, title, collapse chevron, mute badge -->
        <ItemsControl ItemsSource="{Binding Rows}"
                      ItemTemplateSelector="{StaticResource Dyc.RowTemplateSelector}"
                      KeyboardNavigation.TabNavigation="Continue"
                      Focusable="False"/>
        <local:NodeResizeGrip HorizontalAlignment="Right"/>   <!-- width only; reuses SizeGripThumb -->
      </StackPanel>
    </Border>
    <!-- state border, balloon, preview bubble: unchanged from today, lifted verbatim -->
  </Grid>
</DataTemplate>
```

The state border/balloon/preview bubble/context-menu blocks (`DyncameloDark.xaml:1283-1450` **[V]**) are moved unchanged, so warning/error/frozen visuals keep working. `ItemsControl` uses a plain `StackPanel` (no virtualisation needed: rows per node are 2-20 and must all be measured to size the node anyway).

**Input row template** (the core of the Blender look):

```xml
<DataTemplate DataType="{x:Type vm:InputRowViewModel}">
  <Grid Height="{StaticResource Dyc.RowHeight}">
    <nodify:NodeInput DataContext="{Binding Connector}" Style="{StaticResource Dyc.Socket.Input}"
                      HorizontalAlignment="Left" Margin="-5,0,0,0"/>       <!-- socket straddles the node edge -->
    <Grid Margin="{StaticResource Dyc.RowPadding}">
      <TextBlock Text="{Binding Connector.Title}" Visibility="{Binding Connector.ShowPlainLabel}"/>
      <local:PortEditorHost Connector="{Binding Connector}" Visibility="{Binding Connector.ShowEditor}"/>
    </Grid>
  </Grid>
</DataTemplate>
```

`Dyc.Socket.Input` re-templates `NodeInput` to *only* the socket glyph (the label lives in the row, next to the editor). Nodify's contract is a `PART_Connector` element in the template, from which `Anchor` is computed; if missing it falls back to the control centre **[V]** (`Connector.cs:12-17`, `Thumb => ... ?? this`). We name the socket element `PART_Connector` so the anchor is the socket centre, which sits on the node edge because of the −5 px margin.

`PortEditorHost` is a `ContentControl` whose content is chosen by `ConnectorViewModel.EditorKind` (§6). One extra visual tree level per input, created lazily: when `ShowEditor` is false (wired input, or `EditorKind == None`) it is `Collapsed` and never instantiates its template.


### 5.3b Details taken from Blender reference screenshots (Geometry Nodes)

Two reference screenshots supplied during review confirm the anatomy and add three details the plan now includes:

1. **Compound fields.** A vector input (Translation/Rotation/Scale) is *one socket* with a label row, then a tight group of three stacked fields (X/Y/Z) sharing one rounded container with hairline dividers. Plan: ports whose kind is `Geometry` with a 3-component `Point`/`Vector` type get a `VectorEditor` (3 `ScrubNumberBox`es in one container, one `SetUserValue` of the composite on any change). Colour-channel and bounding-box ports can reuse the same container later.
2. **Field-vs-item socket glyph beside an inline field** (diamond sockets next to "Rate", "Damping"): consistent with the shape-by-structure rule in §5.4; the diamond stays reserved for "kind unknown until run".
3. **Multi-input socket** ("Behaviors": one elongated pill accepting many wires). Dyncamelo inputs accept one wire today (`GraphModel.Connect` replaces, §2.3), but nodes with add/remove-port support (`AddPortCommand` **[V]**) are the natural candidates; the pill glyph is added to the socket set, and multi-wire acceptance is a per-port flag implemented only for those nodes so no existing graph changes meaning.

Also visible and already planned: header with collapse arrow at left, outputs listed first with right-aligned labels, headers coloured by category, dark flat card with a 1 px border, selected node = white outline.

### 5.4 Sockets

Four frozen `StreamGeometry`s in the token dictionary, chosen by `PortDepth`:

| Depth | Glyph | Meaning |
|---|---|---|
| Item | circle | one value |
| List | rounded square | a list |
| Nested | rounded square with inner ring | list of lists |
| Unknown | diamond | not known until run |

Fill = `PortKindPalette` brush for the family (`PortFamilyToBrush` converter returns **shared frozen** instances from a static dictionary — zero allocations per binding evaluation). State encoding: filled = connected or required; hollow ring = optional & unwired (existing convention **[V]** `Dyc.OptionalConnectorTemplate`); 1.5× ring on hover; ring in accent while it is a valid drop target; opacity from `CompatState` during a drag (§4.4). The socket is one `Path` — no `Ellipse` + `Border` stacks — so a 40-node graph with ~300 sockets is ~300 visuals, not ~1500.

The **level badge** (`@L2` **[V]** `ConnectorViewModel.LevelLabel`) stays as a small chip after the label.

### 5.5 Wires

A new `DycWire : BaseConnection` (Nodify allows this: `DrawLineGeometry` is `protected abstract`, `BaseConnection.cs:644` **[V]**).

```csharp
protected override ((Point, Point), (Point, Point)) DrawLineGeometry(StreamGeometryContext ctx, Point s, Point t)
{
    double dx  = Math.Abs(t.X - s.X);
    double off = Math.Clamp(dx * 0.5, 32, 160);          // Blender-like curvature: proportional, bounded
    var c1 = new Point(s.X + off, s.Y);  var c2 = new Point(t.X - off, t.Y);
    ctx.BeginFigure(s, false, false);
    ctx.BezierTo(c1, c2, t, true, true);
    if (IsMuted) AddMuteTick(ctx, s, c1, c2, t);          // short perpendicular tick at t=0.5, same geometry → no extra visual
    return ((t, s), (s, t));
}
```

(`Math.Clamp` is not in net48 — a one-line local helper is used; noted so the compile doesn't surprise.)

* **Colour** = the source port's family (`Stroke` bound to `Source.FamilyBrush`, frozen).
* **Dashed** (`StrokeDashArray = 4 3`, frozen `DoubleCollection`) when `IsReplicating` (§4.5); **dotted** when muted with 45 % opacity; **thicker + accent** when selected (existing behaviour **[V]**); **brighter + 3 px** while it is the insert-on-wire target (§7.2).
* **Gradient wires** (source colour → target colour) as a setting, off by default; on only while the graph has ≤ 150 wires (a per-wire `LinearGradientBrush` is the cost). Above the cap the setting silently falls back to solid, with a status-bar note.
* **LOD**: at zoom < 0.35 wires use `SnapsToDevicePixels` 1 px straight lines (`IsLowDetail` switch inside `DrawLineGeometry`), which also avoids Bézier flattening cost for hundreds of wires.
* **Cost note.** Every `Connection` is a `Shape` whose geometry is rebuilt when its endpoints change (i.e. while a node it touches is dragged). That cost is per *moved* wire, not per wire — same as today — and our geometry is cheaper than Nodify's (no separate arrowhead figures, no 100-px base offset lines).

### 5.6 Level of detail

`GraphEditorViewModel.LodLevel` (`Full`, `Compact`, `Overview`) is derived from the editor's `ViewportZoom` (a `DependencyProperty` **[V]** `NodifyEditor.cs:44`, already accessible from the control) with hysteresis so it doesn't flicker at a threshold:

| Level | Zoom (enter / leave) | What changes |
|---|---|---|
| Full | ≥ 0.60 / 0.55 | everything |
| Compact | 0.30–0.60 | editors render as read-only value text (no `ScrubNumberBox` templates instantiated), socket labels kept, wires solid 1.5 px |
| Overview | < 0.30 | node = header bar only (rows get `Height=0` but sockets stay in the tree so anchors remain valid, §5.7); wires 1 px straight |

The level is stored once and consumed by `DataTrigger`s / `Visibility` bindings, so a zoom gesture triggers *one* property change at a threshold crossing, not one per wheel tick. **Nodify already applies its own optimisation** — when `Items.Count ≥ OptimizeRenderingMinimumContainers` and zoom ≤ `OptimizeRenderingZoomOutPercent` (0.3) it puts a `BitmapCache` on the items host **[V]** (`NodifyEditor.cs:236-250`) — and our Overview threshold deliberately coincides with it, so zoomed-out rendering is a cached bitmap of very cheap visuals.

### 5.7 Collapse, and why anchors survive it

Blender collapses a node to its header; wires stay attached at the edge. In WPF, a `Collapsed` element has no layout, so a `Connector` inside it would report a stale `Anchor` and its wires would jump to (0,0). Rule: **collapse never sets `Visibility.Collapsed` on socket elements.** Instead each row container gets `Height=0` (via the LOD/collapse trigger), its label/editor children are set `Collapsed`, and the 10-px socket keeps its arrange rect, overflowing the zero-height row (`ClipToBounds=False`). All sockets of a collapsed node therefore share the header's bottom edge y, and wires converge on one point per side — exactly Blender's collapsed look. Spike **S1** proves the anchor stays correct (and updates when the node moves) with real Nodify before this is built on.

### 5.8 Header

Left→right: collapse chevron (▸/▾, click or **H**), function dot (Create/Modify/Info, **[V]** exists), editable title (**[V]** `EditableTextBlock`), lacing chip (**[V]**), mute badge (when muted), state glyph (warning/error). Background = category brush (**[V]** `HeaderBrush`), top corners rounded via a `Border.CornerRadius` clip on the header only (a `Clip` geometry on the whole node would force per-frame re-clip on every drag — avoided).

### 5.9 Width and resize

`NodeViewModel.Width` (nullable = auto). A right-edge `Thumb` (reuse `SizeGripThumb` **[V]** `Views/SizeGripThumb.cs`, width-only mode) drags it; double-click resets to auto. Auto width = content-driven but capped at `Dyc.NodeMaxAutoWidth`; long titles ellipsise with a tooltip. Persisted as `Ui.Width`. Resize is an undo step (`ResizeStep`, coalesced per drag).

### 5.10 What we deliberately don't do

* **No `DropShadowEffect`/`BlurEffect`.** WPF effects render through an extra offscreen pass per element; hundreds of nodes each with a shadow is the classic way to make a WPF canvas stutter. Depth comes from a 1 px border and two flat tones (header/body), which is also what Blender's flat style does.
* **No per-node `BitmapCache`.** It would rasterise once at one scale and blur or re-raster on zoom; Nodify's editor-level cache (above) is the right granularity.
* **No animations on the steady state.** Animations only for: auto-offset slide (150 ms), panel expand (120 ms), and the field hover fade — each on a handful of elements, each disposable.

---

## 6. Inline input editors (Phase 3)

### 6.1 Which editor a port gets

`ConnectorViewModel.EditorKind` is computed once when the row is built:

| Condition (first match) | Editor |
|---|---|
| `Port.Choices != null` **or** declared type is an `enum` | `Choice` (≤ 3 short options → segmented buttons; else drop-down) |
| Kind = Boolean | `Toggle` |
| Kind = Integer or Number (scalar, not list) | `Number` (integer vs real by type) |
| Kind = Colour | `Colour` |
| Kind = File, or name ends `path`/`file`/`folder`/`directory` and type is string | `Path` |
| Kind = DateTime | `Text` with ISO validation |
| Kind = Text | `Text` |
| anything else (lists, `object`, model items, viewpoints…) | `None` — plain label; the input is supplied by a wire |

Enum-typed parameters (e.g. `SeasonName(Season)` in the test fixtures **[V]** `TestFixtures.cs:67`) currently need a wire; `AssemblyNodeLoader` will fill `PortDescriptor.Choices` from `Enum.GetNames` when the parameter is an enum and has no `[NodeChoices]`, so they become drop-downs for free. The `SelectedChoice` plumbing **[V]** (`ConnectorViewModel.cs:169-198`) is reused unchanged.

### 6.2 Value semantics (all editors)

The displayed value is `HasUserValue ? UserValue : DefaultValue`.

* **Edit → `Port.SetUserValue(v)`**, which marks the node dirty **[V]** (`PortModel.cs:88-99`).
* **If the edit equals the default → `ClearUserValue()`**, so files stay lean and the "modified" indicator is truthful.
* **Modified indicator:** label in normal weight + accent dot when `HasUserValue`; a click on the dot (or Backspace while hovering, or the context menu) resets to default. Blender colours modified fields; a dot is the dark-theme-friendly equivalent.
* **Required input with no default:** shows a dashed placeholder ("value"); typing pins `UserValue`, which the engine already honours before it reports a missing input **[V]** (`GraphEngine.cs:174` precedes the `missingInputs` branch at 181). This is a small *behaviour* improvement worth stating: unwired required scalar inputs become fillable instead of producing "missing input" warnings.
* **Stored type:** the value is stored as the port's own CLR primitive (`int` stays `int`, `double` stays `double`), so the serializer's `IsSerializablePrimitive` gate **[V]** (`GraphSerializer.cs:407`) accepts it and coercion at execution is a no-op.
* **Wired ⇒ editor hidden, value retained** (as today for choice ports **[V]** `ShowChoiceEditor`). Disconnecting brings the editor back with the previous pinned value.

### 6.3 `ScrubNumberBox` (the Blender number field)

A custom control (`Views/ScrubNumberBox.cs`, one `Control` + a generic.xaml template, ~300 lines, no dependencies). Behaviour table:

| Input | Result |
|---|---|
| Hover | field brightens; arrows ◂ ▸ appear at the ends |
| Click ◂ / ▸ | value ∓ step |
| Press + drag > 3 px | **scrub**: `value += dx × k`; cursor hidden; wraps at screen edges (Win32 `SetCursorPos`, guarded by try/catch and skipped under remote-desktop where it can fail) |
| … with **Shift** | fine: `k × 0.1` |
| … with **Ctrl** | snap to multiples of step |
| Click without drag, or double-click, or Enter when focused | **type**: in-place `TextBox`, all selected |
| Enter | commit (expression evaluated) |
| Esc | revert, exit |
| Tab / Shift+Tab | commit, move to the next/previous editor **within the node** (`KeyboardNavigation.TabNavigation="Continue"` scoped to the rows control); never re-enables Nodify's pan-on-focus (already off, v0.34.0 **[V]**) |
| Ctrl+C / Ctrl+V while hovered | copy / paste the value |
| Backspace while hovered | reset to default |
| `-` while hovered (not typing) | negate |
| Ctrl + wheel while hovered | step (plain wheel is left to canvas zoom — `e.Handled` stays false) |

* **Sensitivity `k`:** if a finite soft range exists, `k = (softMax − softMin) / fieldWidthPx` (dragging across the field sweeps the range); otherwise `k = step / 8` (8 px per step). `step` = `[NodeRange].Step`, else `NiceStep(default)`: integers → 1, otherwise `10^floor(log10(max(|default|,1))−1)` → 0.01 for defaults under 1, 0.1 for under 10, 1 for under 100, and so on.
* **Slider look:** with a finite soft range, a `Dyc.FieldFillBrush` bar fills the proportion of the range (Blender's "slider" property look). Without one, no bar.
* **Display:** `decimals = clamp(−floor(log10(step)), 0, 6)`, trailing zeros trimmed for reals, unit suffix from `[NodeRange].Unit`.
* **Expressions:** typing `2*3+1`, `12.5/2`, `(4+2)^2`, `pi/4` evaluates through `Dyncamelo.Core.Editing.NumberExpression.TryEvaluate` (~90 lines, recursive descent, invariant culture, no reflection, no `DataTable.Compute`). Pure Core → unit-tested on Linux, including malformed input, division by zero, overflow.
* **Hard vs soft range:** `Min/Max` clamp everything (typed or scrubbed). `SoftMin/SoftMax` only set the slider extent; typing may exceed them.
* **Live vs. commit:** while dragging, the field shows the live value but `SetUserValue` (and therefore auto-run) happens **on release**. Reason: graph nodes call the Navisworks API on the UI thread, and re-evaluating 60×/s while scrubbing is exactly the freeze class fixed in v0.33.x. A setting **"Live scrub evaluation"** (default off) commits every 150 ms for pure-math graphs where that is desirable. Either way the drag is **one** undo step.
* **Numeric edge cases:** `NaN`/`±∞` rejected (the Ctrl+L crash in v0.33.x was a NaN reaching layout — no NaN may enter a `UserValue`); `int` ports round half-away-from-zero; `decimal`-typed ports use `decimal` math for the step.

### 6.4 Other editors

* **Toggle** — the switch already exists (`NodeBody.BooleanToggle`, **[V]**); extracted into a shared `Dyc.Switch` style and sized to the row.
* **Choice** — existing `Dyc.ComboBox` style **[V]**; segmented variant is a `ListBox` restyled as buttons (RadioButton semantics), so there is no popup HWND for the common 2-3-option case.
* **Text** — `Dyc.TextBox` **[V]**, single line, commit on Enter/LostFocus, Esc reverts, right-click clears to default. `MaxWidth` follows node width.
* **Colour** — a swatch button; click opens the existing `ColorPickerDialog` **[V]**. Stored as `"#AARRGGBB"` string (a primitive, serialisable). Because the target port type is a Navisworks/Dyncamelo colour struct, a `string → Color` converter is registered next to the existing colour converters (`[TypeConverterRegistration]` **[V]** `Attributes.cs:164`). *Before coding I will grep which concrete `Color` type each of the 5 catalog colour inputs uses (they may not be the same type) and register a converter per type.*
* **Path** — text + "…" button using `IDialogService` **[V]** (`Services/IDialogService.cs`); file vs folder chosen by name suffix.
* **Validation** — invalid typed input flashes the field border red (`Dyc.ErrorBrush`), reverts, and posts one line to the status bar; nothing is ever committed half-parsed.

### 6.5 Panels

`[NodePanel("Advanced")]` groups a port into a foldable panel (§4.1). Panel header row: chevron, title, "(2 changed)" badge counting `HasUserValue` members, animated 120 ms expand. Open state persists (`Ui.OpenPanels`). A **connected** port inside a *closed* panel is still rendered as a zero-height row with its socket (the §5.7 rule), so wires attach at the panel header and are never orphaned.

Default assignment when a node author provides no panels: none (flat). Audit backfill (§4.1) applies `Advanced` to the trailing optionals of the 20 widest nodes.

---

## 7. Graph-editing gestures (Phase 4)

Every gesture below runs inside `UndoManager.Begin(label)` (§4.6), so each is one Ctrl+Z. Each lists its mechanism, the Nodify facts it depends on, and its fallback.

### 7.1 Drop a wire on empty canvas → filtered node search

* **Today:** `CompletePendingConnection(target)` returns silently when there is no target **[V]** (`GraphEditorViewModel.cs:987-991`). Quick search already exists — `OpenQuickSearch(Point insertLocation)`, `QuickSearchResults`, `CommitQuickSearchCommand` **[V]** (`GraphEditorViewModel.cs:483,466,476`).
* **Change:** when `target == null` and a source exists, call `OpenQuickSearch(PendingConnection.TargetLocation, context: source)`. The context filters and ranks results:
  * From an **output**: keep entries with at least one input whose `Compare(sourceKind, inputKind)` is not `No`. From an **input**: entries with a compatible output.
  * Rank: `Exact` < `Convertible` < `Loose`, then the existing text-match score. The label of each result shows *which* port it will connect to ("→ `items`").
  * Entry port kinds are precomputed once when the library loads (`LibraryEntryViewModel.InputKinds/OutputKinds`; ~364 entries, one pass) — filtering is then a scan over cached structs, no reflection at keystroke time.
* **Commit** (`CommitQuickSearch`): add node at the drop location → connect to the best port (first `Exact`, then `Convertible`, then `Loose`; required inputs before optional) → done, one transaction. `Esc` cancels with no change.
* **Nodify dependency:** none new — the drop location is already available (`PendingConnectionViewModel.TargetLocation` **[V]**).

### 7.2 Drag a node onto a wire → insert

* **Trigger:** exactly one node is being dragged and it currently has **no connections** (Blender's rule; prevents accidental rewiring of wired nodes) **[P]**.
* **Events:** `NodifyEditor.ItemsDragStartedCommand` / `ItemsDragCompletedCommand` exist **[V]** (`NodifyEditor.Dragging.cs:14-61`). While dragging, the node's `Location` is written through the two-way binding **[V]** (`Dyc.ItemContainerStyle`), so the view-model already sees every step; a `DispatcherTimer` (40 ms, started at drag start, stopped at completion) samples it — no per-mouse-move work.
* **Which wire?** Ask WPF, don't re-derive Nodify's Bézier maths (its offsets are private, §2.7):

  ```csharp
  var rect = new Rect(container.TranslatePoint(...)).Inflate(4);          // node bounds in ConnectionsHost coordinates
  var hits = new List<DycWire>();
  VisualTreeHelper.HitTest(editor.ConnectionsHost,
      filter: d => d is DycWire ? HitTestFilterBehavior.Continue : HitTestFilterBehavior.ContinueSkipSelf,
      resultCallback: r => { if (r.VisualHit is DycWire w) hits.Add(w); return HitTestResultBehavior.Continue; },
      new GeometryHitTestParameters(new RectangleGeometry(rect)));
  ```

  `PART_ConnectionsHost` is a real template part **[V]** (`NodifyEditor.cs:32,40`). `GeometryHitTestParameters` tests against the wire's *rendered stroke geometry*, so the answer is exactly what the user sees. If several wires intersect, pick the one whose midpoint anchor is nearest the node centre.
* **Affordance is honest:** the wire highlights only if an insert would succeed — i.e. the dragged node has an input compatible with the wire's source **and** an output compatible with the wire's target (`Compare != No`, §4.4). No false promises.
* **On release:** `Disconnect(old)`, `Connect(src → node.input)`, `Connect(node.output → dst)`; then auto-offset (§7.9). If either `Connect` fails (cycle), the transaction is aborted and rolled back.
* **Cost:** one `HitTest` per 40 ms tick over the wires in a rect — WPF prunes by bounds first; with 500 wires this is microseconds-to-low-milliseconds. It runs only while a single unwired node is being dragged.
* **Fallback if `HitTest` proves too slow (S2):** pre-compute each wire's bounding rect and midpoint at drag start and do the rect-intersect prefilter in managed code, HitTest only the survivors.

### 7.3 Delete and reconnect (dissolve)

* **Keys:** `Ctrl+Delete`, context menu "Delete && Reconnect". Plain `Delete` keeps its meaning. **Reroutes** dissolve on plain `Delete` (their whole purpose).
* **Algorithm** (pure Core, `NodeDissolve.Plan(graph, node)`): use the same pairing as mute pass-through (`MutePassThrough.Resolve`, §4.7) to find `(inputPort → outputPort)` pairs; for each pair where the input is wired, connect its source to every target of the output. Unpaired wires are simply removed. Unit-testable and shared with mute → one behaviour, one test set.

### 7.4 Swap / move a link

* Nodify blocks starting a pending connection from a *connected input* (`StartConnectionCommand.CanExecute` is false for it **[V]** `GraphEditorViewModel.cs:110`), and its own Disconnect gesture just removes the wire. Blender's Shift-drag needs a custom path.
* **Design:** `Shift+drag` on a connected input starts a "link move": the pending wire is anchored at the wire's **source output**; the wire is visually lifted from its old target (dimmed). Drop on:
  * another input that is **free** → move the link;
  * another input that is **wired** → swap the two sources;
  * empty canvas → §7.1 search (with the original wire kept until a node is chosen; `Esc` restores);
  * its origin → no change.
* **Mechanism:** the view-model gets `PendingConnection.MovedFrom` (the origin input). Nodify's `PendingConnection` control follows the pointer once started from a connector; the start is the only special part (S3 verifies that starting it with `Source = wire.Source` from the input's mouse handler works in 7.3.0).
* **Fallback (if S3 fails):** with two wires selected, `Shift+S` swaps their targets; context menu "Swap with…" on an input offers the sibling inputs of that node. Same undo step, same Core logic.

### 7.5 Cut and mute-cut

* **Cut:** Nodify's cutting line (`Alt+Shift+LeftClick` **[V]** `EditorGestures.cs:314`) removes each crossed wire through `RemoveConnectionCommand` **[V]** (`NodifyEditor.Cutting.cs:216`). The editor exposes `CuttingStartedCommand`/`CuttingCompletedCommand` **[V]** (`NodifyEditor.Cutting.cs:27-48`), which we bind to `UndoManager.Begin`/`Dispose` — so a cut across 12 wires is **one** undo step.
* **Mute-cut:** if `Ctrl` is held at completion, `RemoveConnection` toggles `IsMuted` instead of disconnecting (checked via `Keyboard.Modifiers` in the command; one `if`).
* The gesture is documented in the in-app help (§10) since `Alt+Shift+drag` is not discoverable by itself.

### 7.6 Mute (`M`)

* `M` toggles `IsMuted` on selected nodes (or selected wires). Muted node: header striped/50 % opacity + badge; outputs show the pass-through kinds. Engine semantics in §4.7. `MuteStep` for undo.

### 7.7 Reroute

* **Add:** double-click on a wire (custom handler on `DycWire`; Nodify's `Connection` uses single click for selection) or context menu "Add reroute". Transaction: create `RerouteNode` at the click point snapped to the 15-px grid **[V]** (`GridCellSize="15"`), disconnect the wire, connect `src → reroute → dst`.
* **Visual:** a 14-px `KnotNode`-style dot (Nodify ships `KnotNode` **[V]**) in the family colour of the value flowing through it; input and output sockets share the centre. Selectable, draggable, deletable like any node. `Delete` dissolves it (§7.3).
* Reroutes are real nodes in `.dyc` (`NodeType="Reroute"`), so old builds that don't know the type show them as `MissingNodeModel` (**[V]** exists) rather than failing to load.

### 7.8 Frames

Dyncamelo's `GroupModel`/`GroupViewModel` **[V]** already are frames (coloured rectangle, moves its members, resizable, editable title; created with `Ctrl+G` **[V]**). Added: **Shrink to fit** (`Ctrl+Shift+G`, computes the members' bounding box + padding), **colour presets** in the context menu (six chrome-palette tints), **label size** small/medium/large, and **undo steps** for create/move/resize/recolour/delete. Members are unchanged (spatial containment, as today).

### 7.9 Auto-offset (make room)

After insert-on-wire (§7.2) and add-from-wire-drop (§7.1), downstream nodes of the target are shifted right so the new node doesn't overlap: `delta = nodeWidth + 40 − gap(source.right, target.left)`; only if `delta > 0`; applied to `graph.CollectDownstream(targetNode)` **[V]** (`GraphModel.cs:249`) and to nothing else. The model is updated at once (so undo/serialise are exact); the **view** tweens `Location` over 150 ms using `CompositionTarget.Rendering` (a handful of nodes, removed when done). The moves are part of the same transaction.

### 7.10 Auto-connect (`F`)

Selected nodes are ordered by `X`; for each adjacent pair, connect the best output→input by `Compare` rank, skipping inputs that are already wired and pairs with no compatible ports. One transaction; status bar reports "Connected 4 links".

### 7.11 Dim incompatible sockets while dragging

`PendingConnection` starts → `StartConnectionCommand` **[V]** → view-model computes the `Compat` map once (§4.4) and sets `CompatState` on each `ConnectorViewModel`; on completion/cancel it resets them. The row/socket triggers read a single `double SocketOpacity`. Cost: O(sockets) at drag start and end, zero during the drag.

### 7.12 Explicitly not planned (and why)

* *Dropping a wire onto a node body to auto-connect:* Blender doesn't do it either; the socket-precise drop plus §7.1 covers the need.
* *Cursor-wrapping when scrubbing under remote desktop:* falls back to non-wrapping capture; 1900 px at 8 px/unit is 240 units, enough in practice.

---

## 8. Performance plan

I cannot measure WPF rendering in this environment (no display, Linux container). So the plan is: **design the cheap thing, add instruments, and gate on numbers from your machine.**

### 8.1 Budgets **[P]**

| Scenario | Budget |
|---|---|
| Pan/zoom, 150 nodes / 250 wires, Full LOD | ≥ 50 fps sustained |
| Drag one node touching 6 wires | ≥ 50 fps |
| Scrub a number field (no live eval) | field redraw < 4 ms per mouse move |
| Insert-on-wire hit-test tick | < 3 ms |
| Open a graph of 300 nodes | UI thread busy < 1.5 s (rows built once; no per-node eval on load) |
| Zoomed out (< 0.3) 1000 nodes | ≥ 30 fps (Overview LOD + Nodify's BitmapCache) |

### 8.2 Design rules that make the budgets plausible

1. **Fewer visuals per node.** One `Path` per socket (§5.4); one `Border` card; editors instantiate only for unwired input rows in Full LOD; Compact/Overview swap editors for text or nothing. Estimated visuals per typical node (5 inputs, 2 outputs): ~45 today (Nodify template + ellipses + header pieces) → ~30 target.
2. **Frozen everything static.** Family brushes, dash arrays, socket geometries are `Freeze()`d singletons shared by all nodes (frozen freezables skip change-tracking and can be shared across threads). Chrome brushes stay *unfrozen and shared* because palettes mutate them in place (§2.8) — a handful of instances, so no cost.
3. **Compute on structure change, not on render.** Rows, kinds, compatibility, replication flags: recomputed on connect/disconnect/run-complete/palette change only.
4. **No effects, no per-node caches** (§5.10).
5. **Bindings:** `OneWay` for everything except editors; `Mode=OneTime` for values that can't change during the node's life (title of a body row, socket geometry).
6. **Selection/drag cost:** the two-way `Location` binding fires per move; nothing new subscribes to it except the insert-on-wire timer (sampled, not per event) and the minimap.
7. **Threading:** all UI stays on the dispatcher; heavy Core work (layout, compatibility scans) is synchronous but bounded (O(V+E)) — MSAGL layout is the only potentially long operation (§9.1) and runs behind an "Arranging…" status with cancellation via a `CancellationToken` polled by a checked-out worker (MSAGL is pure managed, no Navisworks calls).

### 8.3 Diagnostics HUD (so I don't guess)

`Ctrl+Shift+F12` toggles a small overlay, implemented with `CompositionTarget.Rendering` timestamps:

```
fps 58  frame 17.2 ms (p95 21)  visuals 6 840  nodes 150  wires 231  LOD Full  zoom 0.82
```

* **Visual count** is gathered once per second by a bounded tree walk (skips subtrees of collapsed rows).
* A **"Copy report"** button puts the numbers plus machine info (WPF render tier via `RenderCapability.Tier >> 16`, DPI scale, monitor count) on the clipboard so you can paste them back to me — this is the loop that replaces my missing profiler.
* Off by default; zero cost when off.

### 8.4 Regression guards that *do* run in CI

* A Core test builds a synthetic 1 000-node/2 000-wire graph and asserts `PortKinds`, `Compat` scan, row planning and `UndoManager` record/replay complete within generous CPU budgets (coarse, e.g. 250 ms each) — catches an accidental O(n²), not micro-regressions.
* A test asserts no row planner call allocates per-node closures in the steady state (allocation count via `GC.GetAllocatedBytesForCurrentThread` before/after 10 000 plans), which would signal a leak into the render path.

---

## 9. Arrange, minimap, and the rest of the "scale" toolbox

### 9.1 Arrange with MSAGL

* **Trigger:** existing `Ctrl+L` **[V]** (`DyncameloEditorControl.xaml:27`, `ArrangeSelectionCommand`) plus `Ctrl+Shift+L` for the whole graph.
* **Input:** nodes (width/height from `CanvasItemViewModel.Size` **[V]** — the ItemContainer writes `ActualSize`), edges from `GraphModel.Connections`, layer direction left→right, ports as edge anchors.
* **Engine:** `Microsoft.Msagl.Layout.Layered.SugiyamaLayoutSettings` with `Transformation = PlaneTransformation.Rotation(π/2)`-style orientation for LR flow, node separation 40, layer separation 90, `EdgeRoutingSettings.EdgeRoutingMode = None` (we draw our own Béziers).
* **Fallbacks, in order:** (1) MSAGL throws/times out (> 3 s) → existing Core `GraphLayout` (NaN-hardened in v0.33.x **[V]**); (2) any NaN/∞ in results → discard MSAGL output, use Core `GraphLayout`.
* **Anchoring:** as today, the arranged block keeps its centroid so the graph doesn't jump. One undo step (`MoveNodesStep` composite).
* **Tests (Linux):** feed a fixed DAG to the adapter, assert no overlaps, LR monotonic layering, determinism (same input ⇒ same output), and NaN safety with degenerate sizes (0×0, huge).

### 9.2 Minimap

Nodify `Minimap` **[V]** bound to the editor; a toggle in the toolbar and `Ctrl+M`. Styled with the chrome brushes (the viewport rectangle uses the accent). Hidden by default under 40 nodes.

### 9.3 Everything else from study §6

* **Collapse** (§5.7), **hide unused** (§5.2), **panels** (§6.5), **frames** (§7.8), **reroutes** (§7.7), **resize** (§5.9), **Home** (`Home` = fit all — exists as `Dyc.Icon.Fit` toolbar **[V]**; bind key).
* **Select linked:** `L` selects nodes linked to the selection downstream, `Shift+L` upstream (`CollectDownstream` **[V]** and a mirrored upstream in Core).
* **Select similar:** `Shift+G` selects nodes of the same definition (for bulk operations).

---

## 10. One command surface: every function reachable everywhere

**Requirement (added in review):** no capability may exist only as a shortcut or only as a hidden gesture. Every new (and existing) function must be reachable from **the header menu, the relevant context menu, the toolbar where it earns a button, Settings where it has a preference, the help overlay, and a searchable command palette** — and show its shortcut wherever it appears.

### 10.1 What exists today **[V]**

* Toolbar icon buttons with tooltips-with-shortcut (`DyncameloEditorControl.xaml:80-235`): New, Open (+ Recent, Samples submenus), Save, Save As, Run, Fit, Zoom in/out, Add note, Settings.
* A Settings popup (`SettingsButton`, line 234) for descriptions / double-click action / palette, backed by `UiSettingsService` (persisted).
* Canvas, node, port and wire context menus, and 11 `KeyBinding`s (lines 18-28, 730).
* **No menu bar**, and shortcuts are defined separately from menus and tooltips, so they can drift (and today's Ctrl+L, Ctrl+G exist in some menus and not others).

### 10.2 Single source of truth: the command registry

```csharp
public sealed class EditorCommand
{
    public string Id;                 // "edit.undo", "graph.arrange.all", "node.mute"
    public string Title;              // "Undo"
    public string Category;           // File | Edit | View | Graph | Node | Wires | Help
    public string? Shortcut;          // "Ctrl+Z" — single definition, parsed into KeyGesture
    public ICommand Command;          // the same RelayCommand the VM already exposes
    public Geometry? Icon;            // from Dyc.Icon.*
    public bool InToolbar;            // earns a toolbar button
    public bool IsToggle;             // checkable; bound to a bool (mute, minimap, hide-unused)
    public string? SettingKey;        // links to a Settings entry, if any
    public string Keywords;           // palette search: "delete reconnect dissolve"
}
```

`Services/CommandRegistry.cs` builds the list once from the view-model. Everything below is **generated from it**, so a command cannot appear in one place and be missing from another:

| Surface | Generated from the registry |
|---|---|
| `KeyBinding`s on the editor | every entry with a `Shortcut` |
| **Header menu bar** (new) | grouped by `Category`, with shortcut text and check marks for toggles |
| Toolbar | entries with `InToolbar` (existing buttons migrate to the registry so their tooltips gain the live shortcut) |
| Context menus | each menu declares the *category filters* it shows (node menu: Node + Wires; canvas menu: Edit + Graph) |
| Help overlay (`F1`) | the whole table, searchable |
| **Command palette** (`Ctrl+Shift+P`) | fuzzy search over `Title` + `Keywords`; runs the command; shows the shortcut |
| Documentation | `docs/UI_GUIDE.md` keymap table is *generated* by a test (below), not hand-written |

### 10.3 The header menu bar

A compact 22-px `Menu` row directly under the toolbar (the dock pane is narrow, so the toolbar keeps icons only and the menu carries the words). Menus are styled with the existing `Dyc.MenuItem` **[V]**.

| Menu | Entries (shortcut) |
|---|---|
| **File** | New (Ctrl+N), Open (Ctrl+O), Recent ▸, Samples ▸, Save (Ctrl+S), Save As (Ctrl+Shift+S) |
| **Edit** | Undo (Ctrl+Z), Redo (Ctrl+Y), Cut/Copy/Paste (Ctrl+X/C/V), Duplicate (Ctrl+D), Delete (Del), **Delete && Reconnect (Ctrl+Del)**, Select All, **Select Linked ▸ Downstream (L) / Upstream (Shift+L) / Similar (Shift+G)** |
| **View** | Fit (Home), Zoom in/out, **Minimap (Ctrl+M)** ☑, **Collapse/Expand Selected (H)**, **Hide Unused Sockets (Ctrl+H)** ☑, **Collapse All / Expand All**, **Reset Node Width**, Show Previews ☑, **Performance HUD (Ctrl+Shift+F12)** ☑, Node density ▸ |
| **Graph** | Run (F5), Auto-run ☑, **Arrange Selection (Ctrl+L)**, **Arrange All (Ctrl+Shift+L)**, Add Note, **Frame Selection (Ctrl+G)**, **Shrink Frame (Ctrl+Shift+G)**, Rename Graph (F2) |
| **Node** | Freeze ☑, **Mute (M)** ☑, Lacing ▸, Show Preview ☑, Rename, **Auto-Connect Selected (F)**, Find in Library, **Reset Inputs to Default**, **Add Reroute** |
| **Wires** | **Mute Wire (M on wire)**, **Insert Reroute**, **Swap Links**, **Cut Wires…** (arms the cutting tool so it is usable without Alt+Shift), Disconnect Selected |
| **Help** | Keyboard Shortcuts (F1), Command Palette (Ctrl+Shift+P), UI Guide, About |

Gestures that are mouse-only by nature also get a **menu/palette equivalent** so nothing is gesture-exclusive: insert-on-wire → *Node ▸ Insert Into Selected Wire* (uses the selected node + selected wire); link move/swap → *Wires ▸ Swap Links* (two wires selected); wire-drop search → *Graph ▸ Add Node…* (Space) with the selected output as context; cut → *Wires ▸ Cut Wires…*; scrub-only widgets always accept typing, and *Node ▸ Reset Inputs to Default*, *Edit ▸ Copy/Paste Value* (on the focused editor) cover the hover shortcuts.

### 10.4 Settings: everything that is a preference

The settings popup becomes a sectioned panel (scrollable, same styling **[V]**), still backed by `UiSettingsService`; each new key is nullable-with-default like the existing ones **[V]** (`UiSettingsService.cs:341-355`) so old settings files load unchanged.

| Section | Settings |
|---|---|
| **Appearance** | Palette (existing), **Node density** (Compact / Normal / Comfortable → `Dyc.RowHeight`), **Classic node layout**, **Wire style** (curved / straight), **Gradient wires**, **Colour-blind aid** (glyphs in sockets), Show library descriptions (existing) |
| **Editing** | Double-click action (existing), **Hide unused sockets by default**, **Live scrub evaluation**, **Scrub sensitivity** (slow / normal / fast), **Snap to grid** on/off, **Auto-offset on insert**, **Delete key reconnects reroutes** |
| **Canvas** | **Minimap** default, **Show grid**, preview-on-selection (existing) |
| **Diagnostics** | **Performance HUD**, **Reset all UI settings** |
| **Shortcuts** | read-only table (from the registry) with a **Reset** button; *rebinding* is included: click a shortcut, press the new chord, conflicts are flagged; overrides persist in settings as `{ "edit.undo": "Ctrl+Z" }` and the registry re-parses them |

Every toggle in the View/Node menus that is a *preference* (density, minimap, HUD, hide-unused default) is the **same property** as its Settings entry, so changing it in either place updates the other (one `UiSettingsService` property, two bindings).

### 10.5 Guardrails so this stays true

Core-side unit tests (no WPF needed; the registry is plain C# in `Dyncamelo.UI` compiled for tests via the Windows UI test project, plus a pure `CommandCatalog` list in Core for the Linux suite):

1. **Completeness:** every `EditorCommand.Id` appears in (a) the palette, (b) the help table, and (c) at least one of {menu bar, toolbar}. A command with neither fails the build.
2. **Shortcut hygiene:** no two commands share a `Shortcut`; every shortcut in `KeyBinding`s equals the registry's; no shortcut collides with text-editing keys when a text box has focus (the registry marks each as `Global` or `CanvasOnly`, and only `Global` may use Ctrl+letter chords that TextBox also uses — Ctrl+Z inside a text field undoes text, not the graph).
3. **Tooltip/menu text is generated**, never typed: a test greps the XAML for hard-coded "(Ctrl+" strings and fails if any remain in `DyncameloEditorControl.xaml` after the migration.
4. **Gesture parity:** each gesture in §7 has a registry entry with a non-gesture route (list in 10.3); a test asserts the mapping is total.
5. **Docs:** `docs/UI_GUIDE.md`'s keymap table is generated from the registry by a test that fails if the committed file is stale.

### 10.6 Where this lands in the phases

* **Phase 0:** `EditorCommand`, `CommandRegistry`, the Core `CommandCatalog` list and the guardrail tests; existing toolbar/shortcuts/context items migrate onto it (no visible change except tooltips now show the live shortcut).
* **Phase 1:** *Edit ▸ Undo/Redo* + toolbar buttons, and the **header menu bar** itself (File/Edit/View/Graph/Node/Help) so undo is visible the moment it exists.
* **Phases 2-4:** each feature lands **with** its registry entry, menu item, context-menu item and Settings entry in the same change — a phase's acceptance checklist now includes "reachable from menu, palette, and (if a preference) Settings".
* **Phase 5:** command palette, shortcut rebinding UI, generated `UI_GUIDE.md`, Help menu.

---

## 11. Testing strategy (honest about what can run where)

| Layer | Where | What |
|---|---|---|
| **Core logic** (`PortKinds`, `Compat`, palette CVD, `UndoManager` + every step, `MutePassThrough`, `NodeDissolve`, `NumberExpression`, scrub math `NiceStep/Format`, hide-unused planner, row planner, MSAGL adapter, serializer round-trips) | `dotnet test` on Linux CI (existing `Dyncamelo.Core.Tests`; Colourful added test-only) | Fully automated; the bulk of the risk lives here by design. |
| **Engine semantics** (mute pass-through, muted wires, user-value precedence, required-input fill) | `Dyncamelo.Core.Tests` + `Dyncamelo.Integration.Tests` | Extends `SampleGraphFileTests` **[V]**: new pinned sample `Blender-style basics.dyc` runs with inline user values and a muted branch. |
| **Catalog audits** | Core tests reading `docs/dyncamelo-nodes.json` | No ≥10-use type falls through to `Any`; every `[NodeRange]` has min ≤ default ≤ max; every panel name non-empty. |
| **WPF visuals & gestures** | *Cannot run in this container.* Windows CI job (`windows-latest`, which already builds the UI **[V]** per csproj comment) builds the UI + runs headless STA tests for what doesn't need a GPU: template loading (`XamlReader.Load` of every dictionary), `ScrubNumberBox` state machine via synthetic `MouseEventArgs`, `Freeze()` invariants, `PortFamilyToBrush` identity. | New `Dyncamelo.UI.Tests` project (net48, xunit + `[StaFact]`). |
| **Manual visual QA on your machine** | You | A one-page checklist per phase (§12) with the exact things to try, and the HUD "Copy report". |

Quality gates per phase (each new function must pass the §10.5 parity tests: menu, palette, help, and Settings where it is a preference): `dotnet build` for `-p:NavisworksYear=2024/2025/2026` (as I do today), `dotnet test`, Windows CI green, then a tagged release and a ~6-minute CI recheck.

---

## 12. Phase plan, releases and acceptance

Six releases. Each is independently shippable, builds green for Navisworks 2024/2025/2026, ships with docs, and leaves the editor usable. The order is by **dependency**, not by visibility: undo before gestures, types before colours, rows before editors.

To keep you unblocked while the visual layer changes, Phases 2-4 ship the new node layout behind **Settings → "Classic node layout"** (default: new). The old `DataTemplate` stays in the file until v0.40, then is deleted.

### Phase 0 — Foundations and proof (v0.35.0) · no visible change

> **Implementation status (recorded as built):** Core foundations, engine mute, serializer fields, `RerouteNode`, `CommandCatalog`, HUD and the MSAGL spike are done and tested (Core 304 tests). Deviations from the text below: (1) **Colourful was not adopted** — the CVD/contrast checks are ~40 lines of test code (Machado 2009 matrices, CIE Lab ΔE76), fewer dependencies for the same guarantee; (2) the palette was **tuned by those tests** (Integer, Clash, Selection, Action, Any changed; the worst colour-vision-deficiency pair is guarded at ΔE ≥ 3, normal vision ≥ 15 — sixteen families cannot all be CVD-separable, so shape and tooltips carry the rest, as designed); (3) the **command registry migration and header menu bar move to Phase 1** where the menu bar is built (the catalog data and its integrity tests exist now); (4) the **Windows UI-test project (S0/S1)** is scheduled with Phase 2 where anchors first matter; (5) **MSAGL S7:** loads and lays out on .NET 8 (`MsaglSpikeTests`), upstream licence text confirmed MIT; the in-Navisworks load check and the four packaging file lists happen in Phase 5 when the runtime dependency is actually taken. (6) Enum-typed parameters now show as drop-downs (loader fills `Choices`), a small visible change.

| Work | Files (new **N** / changed **C**) | ~LOC |
|---|---|---|
| Port kinds, depth, compat, palette | **N** `Core/Editing/PortKind.cs`, `PortKinds.cs`, `Compat.cs`, `PortKindPalette.cs` | 450 |
| Attributes + loader: `[NodeRange]`, `[NodePanel]`, `[PortKinds]`, enum → choices | **C** `Loader/Attributes.cs`, `PortDescriptor.cs`, `AssemblyNodeLoader.cs`, `Graph/PortModel.cs` | 220 |
| `NumberExpression`, `NiceStep`, number formatting | **N** `Core/Editing/NumberExpression.cs`, `NumberFormat.cs` | 200 |
| Additive `.dyc` fields (`Ui`, `Muted`, `Hidden`), model props | **C** `NodeModel.cs`, `ConnectionModel.cs`, `PortModel.cs`, `GraphSerializer.cs` | 150 |
| `RerouteNode` | **N** `Core/Nodes/RerouteNode.cs`; **C** `NodeRegistry.cs` | 60 |
| Engine: `IsMuted` nodes/wires, `MutePassThrough` | **C** `Execution/GraphEngine.cs`; **N** `Execution/MutePassThrough.cs` | 120 |
| **Command registry + Core `CommandCatalog` + guardrail tests (§10)**; migrate existing shortcuts/tooltips | **N** `UI/Services/CommandRegistry.cs`, `Core/Editing/CommandCatalog.cs`; **C** `DyncameloEditorControl.xaml` | 450 |
| Diagnostics HUD (baseline measurement of the **current** UI) | **N** `UI/Views/PerfHud.cs` | 180 |
| Windows UI test project + CI job | **N** `tests/Dyncamelo.UI.Tests/`, **C** `.github/workflows/ci.yml` | 150 |
| MSAGL packaging (4 file lists, §3) | **C** `Dyncamelo.UI.csproj`, `Dyncamelo.App.csproj:80`, `release.yml:85`, 2 docs | 10 |
| Tests | Core.Tests: kinds, catalog audit, palette CVD (Colourful), expression, serializer round-trip, mute engine | 900 |

**Acceptance:** all Core tests green on Linux; UI test project runs on `windows-latest`; spikes S0/S1/S7 pass (or their fallback is chosen and recorded in this document); HUD numbers for the *current* UI captured from you as the baseline (one paste of "Copy report" on a graph of your choice).

**Your QA (2 minutes):** open a big graph, press `Ctrl+Shift+F12`, pan/zoom for 10 s, click "Copy report", paste it to me.

### Phase 1 — Undo/redo everywhere (v0.36.0)

| Work | Files | ~LOC |
|---|---|---|
| `UndoManager`, coalescing, transactions | **N** `Core/Editing/UndoManager.cs`, `UndoSteps.cs` (≈14 steps) | 550 |
| `PasteFragment(preserveIds)` | **C** `GraphSerializer.cs` | 25 |
| Hook every edit site (§4.6 list) | **C** `GraphEditorViewModel.cs` | 300 |
| Move/resize recording via `ItemsDragStarted/Completed` | **C** `DyncameloEditorControl.xaml` (bind commands), `GraphEditorViewModel.cs` | 60 |
| Toolbar buttons, Ctrl+Z/Y, status text, **header menu bar** (§10.3) | **C** `DyncameloEditorControl.xaml`, `DyncameloDark.xaml` (2 icons) | 200 |
| Mute/wire-mute commands (no gesture UI yet: context menu only) | **C** VM + XAML | 80 |
| Tests | forward/back byte-equality for each step; replay guard; coalescing; capacity; delete-and-restore preserves ids; undo of `Connect` restores the replaced wire | 700 |

**Acceptance:** every edit gesture that exists today is undoable and redoable; `Clear()` on New/Open; undo after a run reverts the graph only and says so; no leak (capacity ring). **Your QA:** delete nodes → undo → the same nodes reappear with their wires and values; drag a node, undo, it returns; edit a value, undo.

### Phase 2 — The new look (v0.37.0)

| Work | Files | ~LOC |
|---|---|---|
| Tokens, new chrome brush keys, palette values | **N** `Themes/Dyc.Tokens.xaml`; **C** `PaletteCatalog.cs`, `DyncameloDark.xaml` | 180 |
| Row view-models + planner (hide-unused, panels, collapse) | **N** `ViewModels/NodeRows.cs`, `Core/Editing/RowPlanner.cs` (pure, testable) | 420 |
| New node `DataTemplate`, header, rows, resize grip | **C** `DyncameloDark.xaml`; **N** `Views/NodeHeader.xaml`, `RowTemplateSelector.cs` | 500 |
| Sockets + family brushes + depth glyphs | **N** `Views/PortFamilyToBrush.cs`, geometry resources | 200 |
| `DycWire` (curve, mute tick, dash, LOD) | **N** `Views/DycWire.cs`; **C** connection template | 220 |
| LOD service with hysteresis | **N** `ViewModels/LodService.cs` | 90 |
| Collapse/H, hide-unused/Ctrl+H, width persistence | **C** `NodeViewModel.cs`, serializer glue | 160 |
| Reroute visual | **N** `Views/RerouteTemplate.xaml` | 80 |
| Classic-layout switch | **C** `UiSettingsService.cs`, template selector | 60 |
| Tests | row planner (hide rules, panels, connected-never-hidden), LOD hysteresis, palette keys complete, anchor/collapse UI tests (S1) | 500 |

**Acceptance:** all existing samples load and render; wires attach to correct sockets after collapse, move, zoom and palette swap; HUD shows ≥ the Phase-0 baseline fps with the new template on the same graph; error/warning/frozen visuals identical in meaning. **Your QA:** open each sample; collapse a node with `H` and drag it; Ctrl+H on a Navisworks node — the Document port disappears; palette swap; zoom out to Overview and back; paste HUD report.

### Phase 3 — Every input is an editor (v0.38.0)

| Work | Files | ~LOC |
|---|---|---|
| `ScrubNumberBox` control + template + generic.xaml | **N** `Views/ScrubNumberBox.cs`, `Themes/Generic.xaml` | 420 |
| `PortEditorHost` + editor templates (toggle, choice, segmented, text, colour, path, datetime) | **N** `Views/PortEditorHost.cs`, `Themes/Editors.xaml` | 450 |
| `ConnectorViewModel`: `EditorKind`, `Range`, `DisplayValue`, `IsModified`, reset | **C** `ConnectorViewModel.cs` | 220 |
| Colour string ↔ struct converters | **C** Navisworks/Nodes converter registration | 60 |
| Range/panel backfill audit across the catalog | **C** node source files (attribute-only edits), regenerate `docs/dyncamelo-nodes.json` | ~120 attrs |
| Panels UI + persistence | **C** rows/templates | 140 |
| Live-scrub setting | **C** settings | 40 |
| Tests | scrub math, expression edge cases, value semantics (`SetUserValue` vs `ClearUserValue`), required-input fill in the engine, UI state-machine tests | 650 |

**Acceptance:** for a sample of 30 catalog nodes covering every editor kind, the unwired input shows the right editor, the value persists across save/load, the graph runs with it, undo works, wired inputs hide the editor. **Your QA:** scrub `Clash.Run` tolerance-like fields, Shift for fine, type `2*3`, Tab through a node, Ctrl+C/V a value, Backspace to reset.

### Phase 4 — Rope-style editing (v0.39.0)

| Work | Files | ~LOC |
|---|---|---|
| Wire-drop search (context-aware quick search) | **C** `GraphEditorViewModel.cs`, `LibraryViewModel.cs` (entry kinds), XAML popup | 260 |
| Insert-on-wire (timer, hit test, highlight, commit) | **N** `ViewModels/InsertOnWire.cs`, `Views/WireHitTester.cs` | 260 |
| Delete & reconnect + `NodeDissolve` | **N** `Core/Editing/NodeDissolve.cs`; **C** VM | 140 |
| Link move / swap | **C** `ConnectorViewModel`, VM, code-behind | 200 |
| Cut → undo transaction; mute-cut | **C** editor bindings | 50 |
| Mute gestures (M) + visuals | **C** VM/XAML | 90 |
| Reroute add (double-click) / dissolve | **C** VM, `DycWire` | 120 |
| Frames: shrink, presets, undo | **C** `GroupViewModel.cs`, XAML | 160 |
| Auto-offset + tween | **N** `ViewModels/AutoOffset.cs` | 150 |
| Auto-connect `F`; select linked / similar | **C** VM + Core helpers | 200 |
| Compat dimming during drag | **C** VM + socket style | 100 |
| Help overlay + `KeymapEntry[]` | **N** `Views/HelpOverlay.xaml`, `Services/Keymap.cs` | 220 |
| Tests | dissolve, auto-connect, offset math, link swap logic, compat map, search filtering/ranking, transaction rollback on failed insert | 700 |

**Acceptance:** each gesture is one undo step; insert-on-wire highlights only when insertion will succeed; dropping a wire on empty canvas offers only compatible nodes and connects to the right port; nothing leaves a half-edited graph when a `Connect` fails (transaction abort restores state). **Your QA:** the eight gestures in the keymap table.

### Phase 5 — Arrange, minimap, polish, docs (v0.40.0)

| Work | Files | ~LOC |
|---|---|---|
| MSAGL arrange adapter + fallbacks + cancel | **N** `Core/Editing/MsaglLayout.cs` (Core, netstandard2.0) | 220 |
| Minimap, `Ctrl+M`, `Home` | **C** XAML | 80 |
| Command palette, shortcut rebinding, sectioned Settings (§10.4) | **C** `UiSettingsService.cs`, settings XAML; **N** `Views/CommandPalette.xaml` | 420 |
| CB aid glyphs option | **C** socket template | 60 |
| Delete classic layout | **C** `DyncameloDark.xaml` | −400 |
| Docs: `docs/UI_GUIDE.md`, keymap, `NODE_LIBRARY.md` notes, `EXTENDING.md` (how node authors use `[NodeRange]`/`[NodePanel]`/`[PortKinds]`) | docs | — |
| Perf pass driven by your HUD reports; adjust LOD thresholds/budgets in this document | various | — |
| Tests | layout determinism, NaN safety, overlap-freedom | 300 |

**Acceptance:** all Phase 0-4 checks still pass; a 300-node graph arranges in < 2 s (or falls back with a status note); the budgets in §8.1 are met on your machine or the deviations are documented with a fix list.

### Effort summary (estimates, not promises)

~7 000 lines of production code, ~4 500 lines of tests, spread over 6 releases; in this environment each phase is roughly one long working session plus your visual QA. The largest single risk to the calendar is **not code, it is my inability to see the result** — which is why every phase ends in a short, specific QA on your machine and why the HUD exists.

---

## 13. Spikes (each is a pass/fail experiment with a pre-agreed fallback)

| # | Question | How | Pass | Fallback |
|---|---|---|---|---|
| **S0** | Can WPF UI tests run on `windows-latest` (STA, real `Window`, layout)? | Minimal `Dyncamelo.UI.Tests` with one `[StaFact]` that shows a `NodifyEditor` with 2 nodes and reads `Anchor`s | Test passes in CI | Spikes become a "UI Lab" ribbon item you run once and paste the result of |
| **S1** | Do `NodeInput`/`NodeOutput` outside `PART_Input/Output` report correct `Anchor`s — at rest, after node move, after zoom, and in the zero-height (collapsed) arrangement? | 5-row node in the test editor; assert anchors vs. expected socket centres | Anchors within 1 px in all four cases | Keep sockets inside `PART_Input/PART_Output` `ItemsControl`s of a re-templated `Node` and lay out rows with a two-column `Grid` sharing row heights via `SharedSizeGroup` |
| **S2** | `GeometryHitTestParameters` against `DycWire`s: correct and fast? | 500 synthetic wires, 1 000 rect queries, measure | < 3 ms per query, correct hit | Managed bounding-rect prefilter (§7.2) |
| **S3** | Can a pending connection be started from a connected input's mouse handler with `Source = wire.Source`? | Test editor + simulated mouse events | Pending wire follows pointer and completes | `Shift+S` swap + "Swap with…" menu (§7.4) |
| **S4** | Does `SetCursorPos`-based scrub wrapping work in the Navisworks-hosted `ElementHost` (DPI, remote desktop)? | Manual: your machine | Wraps without jitter | Non-wrapping capture (works, just finite drag) |
| **S5** | Is `ItemsDragStartedCommand` raised for a single-node drag and does `Location` update continuously? | UI test with simulated drag or manual | yes | Read `ItemsMoved` routed event on completion + sample container positions in a timer |
| **S6** | Do hidden `Document` ports change behaviour anywhere (a node whose logic checks "was document supplied")? | Static audit: all `Document? document = null` parameters resolve `document ?? Application.ActiveDocument` | Pattern uniform | Exclude any exception from auto-hide via `[PortKinds]` override |
| **S7** | Does `AutomaticGraphLayout.dll` load in the Navisworks add-in context, and what is its licence file? | Windows CI: load + lay out a graph via reflection; manual: Navisworks | Loads and lays out; licence text recorded in `THIRD_PARTY.md` | Ship Core `GraphLayout` only; drop the polish tier |

Spikes S0, S1, S7 gate Phase 2 and 5; S2, S3, S5 gate the corresponding Phase 4 gestures; S4 and S6 are checked during Phases 3 and 2 respectively. A failed spike changes one row of this document, not the overall plan.

---

## 14. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| I can't render/measure WPF here; visuals need your eyes | certain | medium | Windows CI layout tests, HUD reports, per-phase QA checklists, Classic-layout switch |
| Row template breaks Nodify anchor tracking | low-medium | high | S1 first; fallback layout defined |
| Undo drifts from model state (missed hook site) | medium | medium | Byte-equality step tests; a debug assertion mode that snapshots `Serialize` before/after every recorded transaction in tests; grep-based test that every `_graph.` mutation in the VM sits inside `Begin` |
| Existing `.dyc` files or samples change behaviour | low | high | Only additive fields; `SampleGraphFileTests` pinned values; hidden-`Document` audit (S6) |
| Perf regression from more visuals per row | medium | high | Fewer visuals per node by design, LOD, HUD baseline before/after, budgets in §8.1 |
| MSAGL packaging omission (allow-list DLL copy) | medium | medium | The four file lists are explicit in §3; a CI step lists the release zip and asserts `AutomaticGraphLayout.dll` is present |
| Scope size (~11k lines) | certain | schedule | Six releases, each shippable; each phase's Classic switch guarantees you are never stranded mid-way |
| Colour palette fails CVD tests | medium | low | Test names the failing pair; palette tuned by evidence |

---

## 15. Traceability: every study feature → where it is built

| Study item | Section | Phase |
|---|---|---|
| Fixed anatomy (header, outputs, body, inputs) | 5.2, 5.3 | 2 |
| Category-coloured header, collapse arrow | 5.8, 5.7 | 2 |
| Unwired input is its own editor | 6.1-6.4 | 3 |
| Number-field widget (drag, Shift, Ctrl, type, Tab, copy/paste, expressions) | 6.3 | 3 |
| Boolean switch, enum drop-down / expanded enum | 6.4 | 3 |
| Colour = type | 4.2, 4.3, 5.4 | 0, 2 |
| Shape = structure | 4.2, 5.4 | 0, 2 |
| Wire colour follows type; gradient option | 5.5 | 2 |
| Dashed wires for automatic replication | 4.5, 5.5 | 0, 2 |
| Rounded softer surface, uniform sizes | 5.1, 5.3 | 2 |
| Panels (foldable sections) | 4.1, 6.5 | 0, 3 |
| Hide unused sockets | 5.2 | 2 |
| Node resize | 5.9 | 2 |
| Collapse/hide | 5.7 | 2 |
| Reroutes | 4.9, 7.7 | 0, 4 |
| Frames | 7.8 | 4 |
| Insert node on link | 7.2 | 4 |
| Auto-offset | 7.9 | 4 |
| Link swap / move | 7.4 | 4 |
| Cut links; mute links | 7.5 | 4 |
| Mute node | 4.7, 7.6 | 0-1, 4 |
| Delete and reconnect (dissolve) | 7.3 | 4 |
| Drop-wire-on-canvas filtered search | 7.1 | 4 |
| Grey out invalid sockets while dragging | 4.4, 7.11 | 0, 4 |
| Auto-connect selected (F) | 7.10 | 4 |
| Minimap, fit, select linked | 9.2, 9.3 | 5 |
| Auto-arrange | 9.1 | 5 |
| Explicit over automatic (visible state, no hidden magic) | 5.2 (never hide connected), 6.2 (modified dot), 4.5 (dashed replication) | 2-3 |
| Colour-blind safety | 4.3, 10 | 0, 5 |
| Undo (Blender's safety net; not in Dyncamelo) | 4.6 | 1 |
| Performance at scale (LOD, minimap) | 5.6, 8 | 2, 5 |

Nothing from the study is left unassigned. The two items I *chose not to build* are listed with reasons in §7.12.

---

## 16. What I need from you (none of it blocks starting)

1. **Approve the phase order** (foundations → undo → look → editors → gestures → polish), or tell me which visible phase you want first. Constraint: undo must precede gestures, and types/attributes must precede colours/editors.
2. **Confirm the layout change (Q5):** sockets stay on the left (inputs) and right (outputs) edges exactly as in Blender; only the vertical stacking of rows changes. It is the biggest visual change and the one most worth a second look at the Phase 2 screenshot.
3. **Confirm you accept one new runtime dependency** (MSAGL, MIT, one DLL) for Phase 5. If you'd rather have none, Phase 5 uses the Core layout only, and the plan otherwise stands.
4. **Send me one HUD report** after Phase 0 (a graph you consider "big") so the budgets in §8.1 are calibrated to your machine rather than to my guesses.
