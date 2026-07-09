# Dynamo Execution-Engine Semantics — Design Specification for the Dyncamelo Engine

Audience: the implementer of `Dyncamelo.Core` (graph model, engine, zero-touch loader, .dyc serializer). Everything below is prescriptive; "Dynamo does X" statements are verified against DynamoDS/Dynamo source, wiki, and shipping 2.x `.dyn` files.

---

## 1. Dataflow evaluation: scheduling, dirty propagation, re-execution

### 1.1 How Dynamo does it
Dynamo compiles each node to DesignScript AST and hands it to a VM (LiveRunner) that performs **delta execution**: on every change it re-executes only the modified subgraph and everything downstream of it; unmodified upstream results are served from the VM's value cache. Runs are queued on a `DynamoScheduler` as `UpdateGraphAsyncTask`s; queued tasks of the same kind are **coalesced** (only the latest matters). Three run modes exist: **Manual** (user presses Run), **Automatic** (any graph mutation schedules a run), **Periodic** (timer). Automatic is the default UX and what makes Dynamo feel "live".

Mutations that mark a node modified (dirty): editing a literal/slider value, connecting or disconnecting a port, adding a node, changing lacing or a port's List@Level, un-freezing. Deleting a node dirties every node that consumed its outputs. **Frozen** nodes are excluded from execution and freezing propagates downstream (downstream shows stale/ghosted values and does not run).

### 1.2 Do this in Dyncamelo
We do not need a VM; a direct interpreter over the node graph is correct and far simpler.

1. **Graph invariants.** Directed acyclic graph of `NodeModel`s; each node owns typed `InPort[]`/`OutPort[]`; a `Connector` joins exactly one out-port to one in-port; an in-port accepts **at most one** connector (Dynamo rule). Reject any connector that would create a cycle at connect time (DFS from target back to source) — never at run time.
2. **Value cache.** Each out-port stores its last computed value (`object?`). This cache is the *only* channel between nodes.
3. **Dirty flag + propagation.** Each node has `bool IsDirty` (true at creation). Any mutation listed in 1.1 sets `IsDirty = true` on the affected node and, immediately, on **all transitive downstream nodes** (simple DFS over connectors; idempotent, cheap). Do the propagation eagerly at mutation time, not at run time — it makes "what will re-run" trivially inspectable for the UI.
4. **Run algorithm.** `EngineController.Run()`:
   - Topologically sort the whole graph once per run (Kahn's algorithm; stable order by node creation index for determinism).
   - Walk the sorted list; **skip nodes with `IsDirty == false`** (their cached outputs stand).
   - For each dirty node: gather inputs (connected upstream cached value, else port default, else *missing*); if a required input is missing, do not execute — set state per §4, outputs = null, mark clean, continue.
   - Execute (with replication, §2, and coercion), store outputs in the cache, set node state, mark clean.
   - **Never throw out of the run loop.** Every per-node exception is caught and converted to node state (§4).
5. **Run modes.** Implement `RunType { Manual, Automatic }` on the workspace (skip Periodic for v1, keep the enum extensible). Automatic: every dirty-marking mutation requests a run through a **coalescing debounce** (§6). Manual: mutations only mark dirty; the Run button executes.
6. **Cancellation.** Check a `CancellationToken` between nodes (not inside a node). A cancelled run leaves already-executed nodes clean and the rest dirty — the next run resumes correctly for free.
7. **Freeze (do implement, it's cheap and users love it).** `IsFrozen` on a node ⇒ treat it and everything downstream as non-executable: skip in the run loop, don't clear their dirty flags, UI ghosts them.

Re-execution contract, stated plainly: *changing one slider re-executes exactly that slider node and its transitive downstream; nothing else.* This is the single most important Dynamo behavior to preserve.

---

## 2. Replication / lacing — exact semantics

### 2.1 Concepts
- **Rank** of a value: scalar = 0, `List<object>` = 1, list of lists = 2, …
- **Declared rank** of an input port comes from the zero-touch parameter type: `double`/`string`/`ModelItem` ⇒ 0; `IList<double>`/`IEnumerable<T>`/`List<T>` ⇒ 1; `IList<IList<T>>` ⇒ 2. A `Dictionary<string,object>` param is rank 0 (it's one value).
- **Excess rank** of an argument = actual rank − declared rank (floor at 0). Replication happens **only over excess rank**. This is the exact rule that makes `List.Sum(IList<double>)` consume a whole list unmapped, while `Math.Sin(double)` maps over the same list.

### 2.2 Auto-map rule (single input)
If any argument has excess rank ≥ 1, the node **replicates**: it is invoked once per element along the excess dimensions and results are collected into a list preserving nesting. Replication is **recursive**: a rank-2 list into a rank-0 port produces a rank-2 list of results (map inside map). A `null` element yields a `null` result element plus a node Warning (§4); other elements still compute.

### 2.3 Multiple replicated inputs — pairing per lacing
When ≥ 2 arguments have excess rank ≥ 1, the per-node `LacingStrategy { Auto, Shortest, Longest, CrossProduct }` decides pairing. Arguments with excess rank 0 are **broadcast** — passed unchanged to every invocation.

- **Shortest** (Dynamo default; make `Auto` an alias for it in v1): zip the replicated arguments index-by-index; output length = min of their lengths. `[1,2,3] + [10,20]` ⇒ `[11,22]`.
- **Longest**: zip; output length = max; a shorter list **repeats its last element** to fill (not cyclic, not null-padded). `[1,2,3] + [10,20]` ⇒ `[11,22,23]`. An empty list cannot be extended ⇒ empty output + Warning.
- **CrossProduct**: nested loops, **leftmost replicated port is the outermost loop**. Output is a nested list whose depth grows by (number of replicated inputs − 1). `[1,2] + [10,20,30]` ⇒ `[[11,21,31],[12,22,32]]` — element `[i][j]` pairs first-input `i` with second-input `j`. Exactly Dynamo's behavior.
- **Nested lists / unequal depth**: apply the rule recursively level by level. At each level, arguments whose remaining excess rank is 0 are broadcast to all siblings; arguments still carrying excess rank are paired per the lacing. (Zip `[[1,2],[3,4]]` with `[10,20]` under Shortest at rank-0 ports ⇒ `[[11,12+…]]` → level 1 pairs `[1,2]`↔`10` (10 broadcast), giving `[[11,12],[23,24]]`.) Implement as one recursive function `Replicate(args, declaredRanks, lacing)`; ~80 lines; unit-test it exhaustively — it is the highest-bug-density spot in any Dynamo clone.
- **Serialization**: persist per node as `"Replication": "Auto"|"Shortest"|"Longest"|"CrossProduct"` (Dynamo uses `"Auto"`, `"Shortest"`, `"Longest"`, `"CrossProduct"` in 2.x .dyn files — keep the same strings).

### 2.4 Coercion (applies before rank computation of the *element*)
Per scalar argument, in order: exact type match → numeric widening (`int→long→double`) → `IConvertible` (`Convert.ChangeType`, invariant culture) → failure ⇒ node Warning, null output for that invocation. Never coerce a list to a scalar or vice versa — that's replication's job.

### 2.5 List@Level — future work (design the hook now)
Dynamo's List@Level lets the user pick, per **input port**, which nesting depth counts as "the item" (`@L2` etc.), overriding rank inference, with an optional "keep list structure" flag. Do **not** implement in v1, but: (a) reserve `Level` (int, −1 = off), `UseLevels` (bool), `KeepListStructure` (bool) on the serialized port object exactly as Dynamo does, and (b) route all rank decisions through one function (`GetEffectiveRank(port, value)`) so List@Level later becomes a local change.

---

## 3. Zero-touch import rules

### 3.1 What Dynamo imports
From an assembly: public static methods, public constructors, public instance methods, and public properties of public classes. Namespace + class become the library tree path (`Assembly ▸ Namespace ▸ Class ▸ member`); the node caption is `Class.Method`. Constructors surface as `ClassName.ByXxx` factory nodes. Instance methods get an implicit **first input port for the instance** (named after the class, lowercase); property getters become one-input (instance) → one-output nodes. `out`/`ref` parameters are **not supported**; multi-output is done via `[MultiReturn]` + `Dictionary<string, object>`.

### 3.2 Dyncamelo loader — prescriptive rules
Scope v1 to **public static methods** (matches our fixed decision; instance-method support can be added later behind the same descriptor model).

1. **Discovery.** `NodeLibraryLoader.LoadAssembly(Assembly)` reflects over public static methods of public (non-generic, non-abstract-irrelevant) classes. Skip: generic method definitions, methods with `ref`/`out`/pointer/params `__arglist` params, `[IsVisibleInLibrary(false)]`.
2. **Identity & category.**
   - Node name: `[NodeName]` if present, else `ClassName.MethodName`.
   - Category: `[NodeCategory("Math.Trig")]` if present, else derived from namespace with the root prefix stripped (`Dyncamelo.Nodes.Math` ⇒ `Math`), then class name appended. Dot-separated path drives the library tree.
   - Description: `[NodeDescription]`, else XML doc summary if the `.xml` sits next to the DLL, else empty.
   - **FunctionSignature** (the stable serialized identity — copy Dynamo's mangling): `Namespace.Class.Method@paramType1,paramType2,…` e.g. `Dyncamelo.Nodes.MathNodes.Atan2@double,double`. This string, plus the assembly simple name, is what `.dyc` stores; the loader resolves it back to a `MethodInfo` at load time. Unresolvable signature ⇒ **dummy node** placeholder (Error state, ports reconstructed from the saved file) — never fail the file load.
3. **Inputs.** One in-port per parameter; port name = parameter name; tooltip = XML doc `<param>`. **Optional parameters** (`double step = 1.0`) become defaulted ports: the port carries `DefaultValue` and `UsingDefaultValue = true` when unconnected; connecting overrides; the UI can toggle back to the default. Only compile-time-constant C# defaults supported (skip Dynamo's `[DefaultArgument("expr")]` DesignScript strings).
4. **Outputs.**
   - `void` ⇒ one pass-through out-port (return the first input, Dynamo-style "chains" for side-effecting nodes) — or simpler and acceptable: a single `result` port emitting `null`; choose the pass-through, it enables sequencing writes.
   - Non-void ⇒ single out-port named from XML doc `<returns>` or the `[return:NodeName]`-style attribute, else the method... use `"result"`.
   - `[MultiReturn("first","second")]` + return type `Dictionary<string, object>` ⇒ one out-port per key, in attribute order; missing key at runtime ⇒ that port gets null + Warning.
5. **Supported port types.** Primitives (`bool,int,long,double,string`), `object`, enums (accept enum value, string name, or underlying int via coercion), `Nullable<T>`, any reference type (opaque pass-through — this is how `ModelItem` etc. flow), `IList`/`IList<T>`/`IEnumerable<T>`/`List<T>` (rank 1+ per §2), `Dictionary<string,object>`. Reject at import time anything else (delegates, Span, pointers) by skipping the method with a logged reason.
6. **Overloads.** Each overload is a **separate node definition** sharing the display name; the library UI disambiguates by showing the parameter list in the tooltip/subtitle (Dynamo does exactly this). Serialization is unambiguous because `FunctionSignature` includes the parameter types.
7. **Descriptor model.** The loader emits `ZeroTouchNodeDefinition { FunctionSignature, Assembly, Name, Category, Description, InputDescriptors[], OutputDescriptors[], MethodInfo }`; the graph instantiates `ZeroTouchNodeModel(definition)`. Interactive nodes (Number Slider, Watch, Note, code-input) are hand-written `NodeModel` subclasses registered alongside — same registry, different `NodeType` in the file (§5).

---

## 4. Node state model, failure isolation, null propagation

### 4.1 Dynamo's model
`ElementState`: Dead (missing inputs / inactive), Active, Warning, PersistentWarning, Error, AstBuildBroken, plus Info/PersistentInfo since 2.13. A runtime exception inside a zero-touch method is caught by the VM: node → Warning with the exception message, outputs → null, run continues. Built-in operations on null yield null ("null propagation") with a warning rather than an exception. Errors never abort the graph run; nodes are isolated.

### 4.2 Dyncamelo — do this
- `NodeState { Idle, Executed, Warning, Error }` (matches our fixed decision) plus a `List<NodeMessage>` where `NodeMessage { Severity: Info|Warning|Error, Text }`. State = max severity of messages after execution (`Executed` if none). Clear messages at the start of each execution of the node.
- **Missing required input** (no connector, no default): don't invoke; outputs = null; state stays `Idle` with an Info message "Input 'x' is not connected". This is Dynamo's Dead — gray in UI, and downstream that depends on it will see null.
- **Exception during invocation** (`TargetInvocationException` unwrapped): outputs = null, state = `Warning`, message = exception message (full stack only into the log). Reserve `Error` for structural problems: unresolvable zero-touch signature, corrupt node data, coercion impossible by declaration. Rationale: Dynamo colors runtime failures yellow, and users read red as "the node is broken", not "the data was bad".
- **Null handling**: a null argument bound to a reference-type or `Nullable` parameter is passed through (the method decides); null into a value-type parameter ⇒ skip that invocation, Warning "null value passed to 'x'", null result. Under replication, this is **per element**: `Math.Sqrt([4, null, 9])` ⇒ `[2, null, 3]` + one Warning. Partial results are the point — never discard the whole list for one bad element.
- **Isolation invariant**: the engine's per-node try/catch is the *only* place exceptions are absorbed; `OutOfMemoryException`/`StackOverflowException` excluded. A node failure marks the node, caches nulls, and execution proceeds downstream (downstream will typically also Warn on the null — that's correct and matches Dynamo's "yellow trail" UX).
- Raise `NodeStateChanged` events so the UI can render badges/tooltips without polling.

---

## 5. DYN 2.x file shape, and the proposed .dyc schema

### 5.1 Verified .dyn (JSON, Dynamo 2.x) top level
`Uuid`, `IsCustomNode`, `Description`, `Name`, `ElementResolver`, `Inputs`, `Outputs`, `Nodes`, `Connectors`, `Dependencies`, `NodeLibraryDependencies` (2.3+), `Thumbnail`, then a `View` block. A zero-touch node entry: `ConcreteType` ("Dynamo.Graph.Nodes.ZeroTouch.DSFunction, DynamoCore"), `Id` (GUID, no dashes), `NodeType` ("FunctionNode"), `Inputs`/`Outputs` arrays of port objects (`Id`, `Name`, `Description`, `UsingDefaultValue`, `Level`, `UseLevels`, `KeepListStructure`), `FunctionSignature` (mangled, e.g. `"+@var[]..[],var[]..[]"`), `Replication`, `Description`. A connector: `{ "Start": <outPortId>, "End": <inPortId>, "Id": <guid>, "IsHidden": "False" }` — note connectors reference **port** GUIDs, not node+index. `View` contains `Dynamo` (`ScaleFactor`, `HasRunWithoutCrash`, `RunType`, `RunPeriod`, `X`, `Y`, `Zoom`, `Version`) plus `NodeViews` (per node: `Id`, `Name`, `IsSetAsInput/Output`, `IsPinned`, `ShowGeometry`, `X`, `Y`, `Excluded`) and `Annotations` (groups/notes). `Inputs`/`Outputs` at top level list ports promoted for Dynamo Player-style parameterization.

Two lessons to copy: **model and view are separate blocks** (a headless runner ignores `View` entirely), and **connectors bind port GUIDs** (survives port reordering across versions). One lesson to reject: `ConcreteType` embedding .NET assembly-qualified names makes files brittle — use logical type tags instead.

### 5.2 Prescribed .dyc schema (version 1)
```json
{
  "Dyncamelo": { "FormatVersion": 1, "MinReaderVersion": 1, "AppVersion": "0.1.0" },
  "Uuid": "guid", "Name": "string", "Description": "string",
  "Inputs":  [ { "NodeId": "guid", "Name": "string" } ],
  "Outputs": [ { "NodeId": "guid", "Name": "string" } ],
  "Nodes": [
    {
      "Id": "guid",
      "NodeType": "ZeroTouch",            // or "NumberSlider","Watch","Note","CodeInput",...
      "FunctionSignature": "Dyncamelo.Nodes.MathNodes.Atan2@double,double",  // ZeroTouch only
      "Assembly": "Dyncamelo.Nodes",       // simple name, ZeroTouch only
      "Replication": "Auto",
      "IsFrozen": false,
      "InputPorts":  [ { "Id": "guid", "Name": "y", "UsingDefaultValue": false,
                         "Level": -1, "UseLevels": false, "KeepListStructure": false } ],
      "OutputPorts": [ { "Id": "guid", "Name": "result" } ],
      "Data": { }                          // NodeModel-specific payload (slider min/max/value, note text)
    }
  ],
  "Connectors": [ { "Id": "guid", "Start": "outPortGuid", "End": "inPortGuid" } ],
  "View": {
    "Camera": { "X": 0, "Y": 0, "Zoom": 1.0 },
    "RunType": "Automatic",
    "NodeViews": [ { "Id": "guid", "X": 120.0, "Y": 240.0, "IsCollapsed": false } ],
    "Notes":  [ { "Id": "guid", "Text": "…", "X": 0, "Y": 0 } ],
    "Groups": [ { "Id": "guid", "Title": "…", "Color": "#RRGGBB", "NodeIds": ["…"] } ]
  }
}
```
Rules: serialize with Newtonsoft, `Formatting.Indented`, invariant culture, GUIDs as `"N"` strings. **Reader tolerance**: unknown top-level/nodes fields are preserved-or-ignored, never fatal; `FormatVersion > MinReaderVersion` supported ⇒ open read-only warning; unresolvable `FunctionSignature` ⇒ dummy node (ports rebuilt from `InputPorts`/`OutputPorts` arrays — this is why we persist port names even though they're derivable). Do **not** persist cached values or node states. `Data` is a free `JObject` handed to the `NodeModel` subclass for custom (de)serialization — this is the expandability hook.

---

## 6. Scheduler / host integration (Revit lessons → Navisworks plan)

### 6.1 How Dynamo runs against Revit
Dynamo core runs graph evaluation on a scheduler thread abstraction (`ISchedulerThread`). Standalone Dynamo uses a dedicated background thread; **Dynamo for Revit replaces it with a `RevitSchedulerThread` that pumps the task queue inside Revit's `Idling` event — i.e., all evaluation actually happens on Revit's main/API thread**, because the Revit API is single-threaded. Revit writes additionally require transactions (`TransactionManager`) — a concern Navisworks does not have. Automatic run is debounced/coalesced so slider drags don't queue dozens of runs.

### 6.2 Dyncamelo plan — do this
1. **No engine-owned thread.** The engine is a synchronous library: `Run()` executes on the caller's thread, period. Since our dock pane's WPF `Dispatcher` thread **is** the Navisworks main thread, calling `Run()` from UI event handlers automatically satisfies "all Navisworks API calls on the host main thread". Do not marshal, do not `Task.Run`, ever, for evaluation.
2. **Coalescing debounce for Automatic mode** (UI layer, not Core): on any dirty-marking mutation, restart a ~250 ms `DispatcherTimer`; on tick, if a run is in progress set a `runAgain` flag, else run. After a run, if `runAgain`, run once more. This gives "latest state wins" with at most one queued rerun — equivalent to Dynamo's task coalescing.
3. **Manual vs Automatic tradeoffs, surfaced honestly in UX**: Automatic is the Dynamo-like default and fine for cheap graphs; Navisworks graphs that walk the full model tree or drive Clash/TimeLiner can take seconds and freeze the UI (synchronous main-thread run). Ship: RunType selector defaulting to **Automatic**, persisted in `.dyc` `View.RunType`; a status bar "Run started/completed in Xms, N nodes"; cancellation button honored between nodes (§1.4.6). Nodes flagged `[CanUpdatePeriodically(false)]`-style "expensive" can later auto-suggest Manual — design the metadata slot, don't build the heuristic now.
4. **Document events**: subscribe to `ActiveDocument` change / model load in the App layer and translate them into "dirty all Navisworks input nodes" (e.g., a `Document.Current` source node) — same pipeline as any other mutation, so Automatic mode reacts to model changes like Dynamo reacts to Revit element updates.
5. **Reentrancy guard** in Core: `Run()` throws `InvalidOperationException` if called while running (the debouncer makes this unreachable in practice; the guard catches integration bugs).

---

## Implementation checklist (ordered)
1. Graph model: `NodeModel`, `Port`, `Connector`, cycle-checked `WorkspaceModel` mutations, dirty propagation (§1).
2. Topo-sorted, dirty-skipping, exception-isolated run loop with cancellation (§1, §4).
3. `Replicate()` recursive combinator with the four lacings + coercion (§2) — the heaviest test target: shortest/longest/cross with 2–3 inputs, nesting, broadcast, empty lists, nulls.
4. Zero-touch loader → `ZeroTouchNodeDefinition` registry; signature mangling round-trip (§3).
5. `.dyc` serializer with dummy-node fallback and version gate (§5).
6. RunType + debounce contract for the UI layer (§6).

Sources: [Dynamo repo](https://github.com/DynamoDS/Dynamo), [sample 2.x .dyn (Basics_Basic01.dyn)](https://github.com/DynamoDS/Dynamo/blob/master/doc/distrib/Samples/en-US/Basics/Basics_Basic01.dyn), [Replication wiki pt1](https://github.com/DynamoDS/Dynamo/wiki/Replication-and-Replication-Guide-Part-1), [pt2](https://github.com/DynamoDS/Dynamo/wiki/Replication-and-Replication-Guide-Part-2), [Dynamo 2.0 / JSON serialization notes](https://github.com/DynamoDS/Dynamo/wiki/Dynamo-2.0), [JSON serialization issue #7747](https://github.com/DynamoDS/Dynamo/issues/7747), plus Dynamo Primer/zero-touch developer docs (developer.dynamobim.org) from model knowledge.
