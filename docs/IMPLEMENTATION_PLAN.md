# Dyncamelo Implementation Plan

This is the master plan for Dyncamelo: what we are building, in what order, with which technical decisions, and how we will know each stage is done. It is written to stay useful — every milestone has explicit scope, exit criteria, and risks, and the engineering decisions are recorded with their rationale so they are not relitigated by accident.

Companion documents:

- [ARCHITECTURE.md](ARCHITECTURE.md) — how the pieces fit together (projects, engine pipeline, `.dyc` format, threading, extension points).
- [NODE_LIBRARY.md](NODE_LIBRARY.md) — the full node catalog with tiers (MVP / Beta / Future); the product-design source of truth for nodes.
- [GETTING_STARTED.md](GETTING_STARTED.md) / [EXTENDING.md](EXTENDING.md) — end-user and node-author guides.

---

## 1. Vision

**Dyncamelo is Dynamo for Navisworks.** BIM coordinators live in Navisworks Manage: federated models, clash tests, TimeLiner schedules, selection sets, viewpoints. Today, automating any of it means .NET SDK development or fragile journal-style workarounds — a cliff that most coordinators never climb. Meanwhile the same people happily automate Revit with Dynamo every day.

Dyncamelo removes that cliff. It embeds a node-graph editor in a Navisworks dockable pane; users wire nodes together and a dataflow engine executes the graph against the live document. The archetypal workflows it must make trivial:

1. Property extraction / QTO to CSV.
2. Bulk selection-set creation from property rules.
3. Color-coding the model by system/status/value.
4. Clash triage and reporting (read, filter, set status/assign, export).
5. Batch viewpoint generation.
6. Model QA audits ("which items are missing property X?").
7. TimeLiner 4D linking driven by CSV schedules.

**Design north star:** a Dynamo user should feel at home in five minutes, and a Navisworks user who has never scripted should build workflow #3 on their first afternoon.

## 2. Guiding principles

1. **Dynamo parity where it matters.** Evaluation semantics (eager dataflow, dirty propagation, replication/lacing with Shortest/Longest/Cross-Product, per-node Warning/Error states that never crash a run), naming conventions (`List.GetItemAtIndex`, `Search.ByPropertyValue`), and UX idioms (node search, watch nodes, notes, lacing badge) follow Dynamo deliberately. Familiarity is a feature; we innovate in the Navisworks node library, not in the basics.
2. **Expandable by architecture, not by patching.** The engine knows nothing about Navisworks; node libraries are discovered via reflection ("zero-touch"); new node packs are DLL drop-ins; interactive nodes are `NodeModel` subclasses with pluggable WPF views; the `.dyc` format is versioned from day one. Every layer has a documented extension point ([ARCHITECTURE.md §8](ARCHITECTURE.md)).
3. **Open-source-friendly dependencies only.** MIT/Apache-2.0/BSD, pinned versions, and as few as possible (see [§5 Engineering decisions](#engineering-decisions)). The Autodesk API is referenced compile-time-only and never redistributed.
4. **Correctness over cleverness.** No true parallelism; the engine evaluates synchronously on the calling thread, and all Navisworks API calls stay on the host main thread. Simplicity here buys reliability inside someone else's process.
5. **Testable off-Windows.** `Dyncamelo.Core` and `Dyncamelo.Nodes` (the engine and most node logic) target netstandard2.0 with zero UI/Navisworks dependencies, so the heart of the product is unit-tested on Linux CI on every commit. Windows CI covers WPF compilation; a manual smoke checklist covers in-host behavior.
6. **Errors are information.** A node that fails shows a per-node Error with the real message; a recoverable issue is a Warning plus a null result. The graph always finishes its run.

## 3. Milestone roadmap

Six milestones, each independently shippable. Tiers referenced below are the catalog tiers in [NODE_LIBRARY.md](NODE_LIBRARY.md).

### M0 — Foundation (engine and libraries, no Navisworks yet)

**Theme:** everything that can be built and proven on any OS.

**Scope**

- Solution/repo skeleton: `Dyncamelo.sln`, `src/` + `tests/` layout, `.editorconfig`, CI (build on `windows-latest`, tests on Linux).
- `Dyncamelo.Core`:
  - Graph model: `NodeModel`, typed in/out ports (single-connector inputs), connectors, workspace; cycle rejection at connect time.
  - Execution engine: topological run (Kahn's algorithm, stable order), eager dirty propagation at mutation time, value cache per out-port, per-node states Idle/Executed/Warning/Error, freeze with downstream ghosting, cancellation between nodes, Manual/Automatic run types with a coalescing debounce for Automatic.
  - Replication/lacing: excess-rank rule, recursive auto-map, Shortest (default, `Auto` alias) / Longest (repeat-last-element) / Cross-Product (leftmost outermost); rank-0 arguments broadcast.
  - Gentle type coercion: numeric widening, `IConvertible`, `object` accepts anything.
  - Zero-touch loader: reflection discovery of `public static` methods via `[NodeName]`, `[NodeCategory]`, `[NodeDescription]`, `[MultiReturn]`; optional parameters become defaulted ports.
  - `.dyc` serialization: versioned JSON envelope (Newtonsoft.Json 13.0.3), round-trip stable.
- `Dyncamelo.Nodes`: the MVP-tier general-purpose nodes (Math, Logic, String, List, Dictionary, Color, DateTime, File/CSV, Geometry value types) plus the interactive `NodeModel` cores (Number/sliders/String/Boolean, Watch, Watch List, Note, List.Create) as UI-agnostic models.
- Test suites `Dyncamelo.Core.Tests` and `Dyncamelo.Nodes.Tests` (net8.0, xunit) green on this repo's Linux CI.

**Exit criteria**

- Engine behavior matrix fully unit-tested: *changing one input re-executes exactly that node and its transitive downstream, nothing else*; lacing examples from the spec (`[1,2,3]+[10,20]` → Shortest `[11,22]`, Longest `[11,22,23]`, Cross-Product nested) pass; a throwing node yields Error state and the run completes; frozen nodes and their downstream do not execute.
- A graph built in code, saved to `.dyc`, reloaded, and re-run produces identical results (golden-file round-trip tests).
- Zero-touch loader discovers `Dyncamelo.Nodes` and exposes correct port names, defaults, and `[MultiReturn]` outputs.
- `dotnet test` green on Linux; full solution compiles on `windows-latest`.

**Risks**

| Risk | Impact | Mitigation |
|---|---|---|
| Lacing/replication corner cases (empty lists, nulls, jagged nesting) ossify wrongly | High — semantics are the product | Spec-first tests written from the Dynamo semantics digest before implementation; corner cases enumerated in the test matrix |
| Over-designed engine abstractions slow M1 | Medium | Direct interpreter, no VM; extension points documented but minimal |
| `.dyc` schema churn later breaks early graphs | Medium | `formatVersion` in the envelope from v0.1 + tolerant reader policy (§7) |

### M1 — MVP editor inside Navisworks

**Theme:** the first end-to-end experience: open pane, wire nodes, run against the live model.

**Scope**

- `Dyncamelo.App`: Navisworks 2024 add-in (`AddInPlugin` ribbon entry + `DockPanePlugin` hosting the editor), plugin packaging layout (`Plugins\Dyncamelo.App\`).
- `Dyncamelo.UI`: Nodify-based canvas (nodes, connectors, pan/zoom, selection, delete/duplicate), searchable node library browser fed by the zero-touch registry, port tooltips, per-node state badges (Warning/Error with message), lacing indicator + right-click switcher, Run button + Manual/Automatic toggle, inline editors for the interactive nodes (sliders, text, boolean, color picker, watch, note), save/open `.dyc` dialogs.
- `Dyncamelo.Navisworks`: the *walking-skeleton* subset of MVP Navisworks nodes needed for a real workflow: `Document.Current`, `Document.Info`, `Models.RootItems`, `ModelItem.Children/Descendants/DisplayName/HasGeometry`, `Properties.Value`, `Search.ByPropertyValue`, `Search.ByPropertyContains`, `Selection.Current/SetCurrent/Clear`, `Appearance.OverrideColor/OverrideTransparency/Reset/Hide/Show`, `SelectionSet.Create`.
- Transaction/undo scoping for write nodes (single undo entry per run), executed on the host main thread (§6).
- Basic diagnostics: an output/log strip showing run duration and node errors.

**Exit criteria**

- Reference workflow 3 (color-coding): *String → Search.ByPropertyContains → Color → Appearance.OverrideColor → SelectionSet.Create* runs end-to-end in Navisworks Manage 2024 on a real federated model, twice in a row (idempotent), with one undo step per run.
- Getting Started first-graph tutorial is executable exactly as written.
- Editor survives: no document open, document closed mid-session, run with disconnected required inputs (nodes gray/warn; no crash).
- Graph save → close Navisworks → reopen → load → run reproduces results.

**Risks**

| Risk | Impact | Mitigation |
|---|---|---|
| Dock pane + WPF + Nodify integration friction (input focus, DPI, theming inside Navisworks) | High | Walking skeleton first — one node on a canvas in a pane before building the library UI; keep UI virtualization on for large graphs |
| Speckle/Autodesk compile-ref vs. runtime binding mismatches | High | Smoke-test the exact API calls listed in the catalog early in M1; pin to API members verified against 2024 |
| Threading mistakes (API touched off main thread) crash Navisworks | High | Engine runs synchronously on the dispatcher thread by design; debug-build main-thread assertion in the Navisworks node host (§6) |
| Undo scoping API friction | Medium | Encapsulate in one `TransactionScope`-style helper in `Dyncamelo.Navisworks`; worst case: document "no undo" for v0.1 and revisit |

### M2 — Full MVP node set (v0.1 "MVP" release)

**Theme:** complete the 88-node MVP tier; all seven reference workflows possible (TimeLiner workflow via CSV read only).

**Scope**

- All remaining MVP-tier nodes in the catalog: full Math/Logic/String/List/Dictionary/Color/DateTime/File(CSV)/Geometry set; Navisworks Properties (`Properties.Categories`), SelectionSets (`All`, `ByName`, `Items`), Viewpoints (`All`, `ByName`, `Apply`, `SaveCurrent`), Clash read-out (`Clash.Tests`, `ClashTest.Info`, `ClashTest.Results`, `ClashResult.Info/Items/Center`).
- Replication polish: per-node lacing UI complete; list-handling nodes verified against nested lists.
- Watch/Watch List rendering for `ModelItem`, sets, clash results (meaningful `ToString` projections).
- Error UX: clicking a node's error badge shows the full message; warnings aggregate when replicating (e.g. "312 of 5,000 items missing property").
- First public release artifacts: GitHub release zip with install instructions, README badges live.

**Exit criteria**

- Reference workflows 1-6 each verified in-host per the smoke checklist and recorded as sample `.dyc` graphs in the repo (`samples/` — added to repo layout in this milestone).
- Node count: MVP tier complete (~88 nodes) and discoverable in the library browser with descriptions.
- QTO workflow on a 100k-item model completes without UI freeze longer than a few seconds (perf smoke, not a hard SLA).
- Zero known crash bugs; all catalog MVP nodes have XML-doc summaries surfaced as node tooltips.

**Risks**

| Risk | Impact | Mitigation |
|---|---|---|
| `VariantData` coercion surprises (units, localized display strings) in `Properties.Value` | High — QTO correctness | Follow the catalog's DataType-driven conversion table; expose `Properties.ValueAsString` early for escape hatch; test against Revit- and IFC-sourced NWDs |
| Large-model performance (search + per-item property reads) | Medium | Bulk `Search.FindAll` over per-item scans where possible; measure with the perf smoke |
| Saved-item tree walking (sets/viewpoints folders) edge cases | Low | Shared folder-recursion helper, unit-tested against a mocked tree |

### M3 — Beta (depth, reporting, packages) (v0.2)

**Theme:** the Beta tier — write-heavy Navisworks operations, reporting sinks, and the extensibility promise made real.

**Scope**

- Beta-tier nodes across all categories (see catalog), notably:
  - Clash triage writes: `ClashTest.Run`, `Clash.RunAllTests`, `ClashResult.SetStatus`, `ClashResult.Assign`, `Viewpoints.FromClashResults`.
  - TimeLiner: task read-out, `TimelinerTask.AttachSet`, `TimeLiner.AddTask`, `TimelinerTask.SetDates` — validating the 2023-compile-ref/2024-runtime binding (§8, caveat 1).
  - Camera nodes and `Export.ViewpointImage` (ComApi bridge), `Export.ClashReportCsv`, `Export.PropertyReportCsv`, `Export.NWD`.
  - CSV/reporting depth: `List.GroupByKey`, `List.SortByKey`, JSON nodes, `Appearance.ColorByValues` heat-maps.
  - Excel interop remains **CSV-first** (CSV opens natively in Excel). A dedicated `.xlsx` writer would require a new dependency and is deliberately deferred (decision reviewed in M3; candidate must be MIT/Apache).
- **Package loading from folders**: scan `%APPDATA%\Dyncamelo\Packages\<PackName>\` at startup, zero-touch-load node DLLs, per-package enable/disable and error isolation (a broken pack must not take down the editor). This turns [EXTENDING.md](EXTENDING.md) from a tutorial into a shipped feature.
- Graph-level niceties: Panel node, Date picker node, groups/annotation colors, recent-files list.

**Exit criteria**

- Reference workflow 4 full loop (run tests → filter by rule → set status/assign → viewpoints per clash → CSV + images) works in-host.
- Reference workflow 7 (CSV → TimeLiner tasks → attach sets) works in-host, proving the TimeLiner runtime binding; result recorded in the caveats table.
- A third-party demo node pack (built from the EXTENDING tutorial verbatim) drops into the Packages folder and its nodes appear and run.
- Beta-tier catalog complete (~99 additional nodes).

**Risks**

| Risk | Impact | Mitigation |
|---|---|---|
| TimeLiner 2023 compile-ref binds incorrectly against 2024 host | High for 4D scope | Proven by a dedicated M3 spike *first*; fallback: late-bound (reflection) TimeLiner adapter, or regenerate reference assemblies from a licensed 2024 install (not redistributed) |
| ComApi image export fragility (`lcodpimage`) | Medium | Isolate in one adapter class; feature-flag the node; document known-good option sets |
| Clash/TimeLiner write APIs desync the Navisworks UI | Medium | Only mutate via `DocumentClashTests`/`DocumentTimeliner` documented edit methods, never via object setters on live items |
| Package loading becomes a support burden (version conflicts) | Medium | Load packs in isolation-friendly way (independent `Assembly.LoadFrom` per folder), require `Dyncamelo.Core` version stamp in pack manifest, clear per-pack error reporting |

### M4 — v1.0 (power features, reach)

**Theme:** power users and broader deployment.

**Scope**

- **Script nodes**: Python Script (IronPython) and C# Script (Roslyn scripting) NodeModels with `IN[0..n]`/`OUT` convention and document access; script text stored in `.dyc`. (Both engines are MIT/Apache; exact packages chosen and pinned at M4 start — the only planned dependency additions, each gated by the license rule.)
- **Multi-version Navisworks support (2024/2025/2026)**: multi-targeted `Dyncamelo.Navisworks`/`UI`/`App` builds (`NAVIS2024`… constants, per-version compile-ref package pins), single codebase, per-version plugin output folders; compatibility strategy per §7.
- **Localization**: resource-based strings for the editor UI and node descriptions (en baseline; community translations); culture-safe numeric parsing/formatting audit across nodes (invariant-by-default already the rule).
- Hardening from Beta feedback: performance profiling on very large graphs/models, `.dyc` format v2 if accumulated needs justify it (with v1 reader kept).

**Exit criteria**

- Same graph runs unmodified on Navisworks 2024, 2025, and 2026 (nodes present in all three); per-version release zips produced by CI.
- Script nodes: a Python and a C# snippet each reading the active document, replicating correctly when fed lists, and failing safely (script exception → node Error).
- UI fully localizable; at least one non-English translation shipped as proof.
- Zero P1 bugs open for two consecutive beta cycles before tagging v1.0.

**Risks**

| Risk | Impact | Mitigation |
|---|---|---|
| API breaks across Navisworks versions | High | Compile-time `#if NAVIS*` shims isolated to one `Compat` folder; CI builds all targets on every PR |
| IronPython/Roslyn size & startup cost inside the add-in | Medium | Lazy-load script engines on first use; script nodes optional at packaging level |
| Arbitrary code in `.dyc` (script nodes) = security surface | Medium | Load-time consent prompt for graphs containing script nodes ("this graph contains code"), documented threat model |

### M5 — Community ecosystem

**Theme:** growth loops: sharing nodes and graphs.

**Scope**

- **Package manager UI**: browse/install/update community node packs from a static, Git-backed registry (JSON index + release zips; no server to run), with license display and version pinning; builds directly on M3's folder loader.
- **Sample graph gallery**: curated `.dyc` gallery (in-repo + website page), one-click open from the editor's start view; contribution pipeline documented.
- Community infrastructure: node-pack template repository (`dotnet new` template), pack validation CLI (checks attributes, docs, version stamp), showcase docs.

**Exit criteria**

- A pack can be published by a third party via PR to the registry and installed from inside Navisworks without touching the filesystem manually.
- Ten or more sample graphs covering the seven reference workflows plus community submissions.
- At least two external node packs published by non-maintainers (adoption signal, not a hard gate).

**Risks**

| Risk | Impact | Mitigation |
|---|---|---|
| Malicious/broken packs damage trust | High | Registry PR review + validation CLI + install-time license/author display; packs are opt-in, isolated, disable-able |
| Maintainer bandwidth for registry curation | Medium | Validation automated in registry CI; curation checklist; multiple maintainers |
| Low community uptake | Medium | The M3 tutorial pack + M5 template lower the entry bar; showcase gallery gives visibility |

## 4. Repository layout

```
Dyncamelo.sln
src/
  Dyncamelo.Core/            netstandard2.0  — graph model, engine, zero-touch loader, .dyc
  Dyncamelo.Nodes/           netstandard2.0  — general-purpose node library
  Dyncamelo.Navisworks/      net48           — Navisworks node library
  Dyncamelo.UI/              net48 (WPF)     — Nodify-based editor
  Dyncamelo.App/             net48 (WPF)     — Navisworks add-in shell
tests/
  Dyncamelo.Core.Tests/      net8.0 (xunit)  — engine/serialization/loader tests (run on Linux CI)
  Dyncamelo.Nodes.Tests/     net8.0 (xunit)  — node behavior tests (run on Linux CI)
docs/                        this documentation set
samples/                     sample .dyc graphs (added in M2)
```

Root namespace prefix `Dyncamelo`; C# `LangVersion` 10 with `Nullable` enable everywhere; no records or init-only setters (net48/netstandard2.0 friction — classic classes + properties).

## 5. Engineering decisions

<a id="engineering-decisions"></a>

### Dependency table (complete and closed — additions require a maintainer decision)

| Package | Version | Used by | License | Why this one |
|---|---|---|---|---|
| Newtonsoft.Json | 13.0.3 | Core | MIT | `.dyc` serialization. Ubiquitous, netstandard2.0-safe, tolerant-reader friendly (`JToken` model suits versioned envelopes). System.Text.Json rejected: weaker net48 story and stricter model hurts graph forward-compat |
| Nodify | 7.3.0 | UI | MIT | The best open-source WPF node-editor control: virtualized canvas (large graphs), MVVM-first, actively maintained. Writing a canvas from scratch is a project by itself |
| Speckle.Navisworks.API | 2024.0.0 (`ExcludeAssets="runtime"`) | Navisworks | Apache-2.0 (package) | Compile-time-only reference to the Navisworks 2024 .NET API so `Dyncamelo.Navisworks` builds on any machine — including Linux CI — without a Navisworks install. Runtime binds the host's genuine Autodesk assemblies |
| Chuongmep.Navis.Api.Autodesk.Navisworks.Timeliner | 2023.0.7 (`ExcludeAssets="runtime"`) | Navisworks | MIT (package) | Compile-time-only TimeLiner API reference. No 2024 edition is published; the TimeLiner API surface is stable between 2023 and 2024, and the assemblies are **not strong-named**, so the host's 2024 `Autodesk.Navisworks.Timeliner.dll` binds at runtime. Known caveat — see §8 |
| Microsoft.NETFramework.ReferenceAssemblies | latest 1.x (`PrivateAssets="all"`) | Navisworks, UI, App | MIT | Lets net48 projects compile with the .NET 8 SDK on machines without .NET Framework targeting packs (i.e., Linux) |
| xunit | 2.9.x | test projects | Apache-2.0 | De-facto standard .NET test framework |
| xunit.runner.visualstudio | 2.8.x/3.x (matching xunit) | test projects | Apache-2.0 | VSTest adapter so `dotnet test` runs the suites |
| Microsoft.NET.Test.Sdk | 17.x | test projects | MIT | Test host for `dotnet test` |

Autodesk's Navisworks assemblies themselves are **never redistributed**: the two API packages above contribute compile-time surface only (`ExcludeAssets="runtime"`), and end users always run against the assemblies installed with their licensed Navisworks.

### Other recorded decisions

| Decision | Choice | Rationale |
|---|---|---|
| Engine style | Direct graph interpreter (no compiler/VM) | Dynamo's VM exists for DesignScript; we have no language. An interpreter over the node graph implements identical user-visible semantics at a fraction of the complexity |
| Evaluation | Synchronous, on the calling thread; no parallelism | All Navisworks API calls must be on the host main thread anyway; determinism and debuggability win |
| Dirty tracking | Eager propagation at mutation time | Makes "what will re-run" trivially inspectable for the UI; run loop just skips clean nodes |
| Lacing default | Shortest (`Auto` = alias for Shortest in v1) | Matches Dynamo's default and user expectations |
| Node authoring | Zero-touch static methods first; NodeModel only for interactive nodes | Lowest possible authoring bar; reflection loader is the extensibility mechanism |
| Graph format | `.dyc` = versioned JSON envelope | Human-diffable, source-controllable, tolerant-reader evolvable (§7) |
| Core geometry | Lightweight immutable `Point`/`Vector`/`BoundingBox`/`Color` classes in Core | Keeps Core/Nodes Navisworks-free; `Dyncamelo.Navisworks` converts at the boundary |
| C# level | LangVersion 10, Nullable enable, no records/init-only | net48/netstandard2.0-friendly while keeping modern niceties (file-scoped namespaces, pattern matching) |
| UI pattern | MVVM over Nodify's ItemsControl model | Nodify is designed for it; keeps editor testable without WPF where possible |

## 6. Testing strategy

Three concentric rings, cheapest ring catches most bugs:

### Ring 1 — Linux-testable core (every commit, this repo's primary CI signal)

`tests/Dyncamelo.Core.Tests` and `tests/Dyncamelo.Nodes.Tests` (net8.0, xunit) run on Linux via `dotnet test`. They own:

- **Engine semantics matrix** — the spec as tests: dirty propagation exactness (slider change re-runs only downstream), topological order stability, skip-clean behavior, freeze semantics, cancellation resume, Automatic-mode debounce coalescing.
- **Replication/lacing** — excess-rank computation from method signatures; auto-map recursion on nested lists; Shortest/Longest/Cross-Product pairing including empty-list, null-element, and jagged cases; broadcast of rank-0 args.
- **Coercion** — numeric widening, `IConvertible` paths, failure → Warning behavior.
- **Error isolation** — throwing node → Error state, downstream gets nulls/does-not-execute per spec, run completes.
- **Zero-touch loader** — attribute discovery, port naming, optional-parameter defaults, `[MultiReturn]` unpacking, malformed-library error isolation.
- **`.dyc` round-trip** — golden files: serialize → deserialize → re-serialize byte-stable; older-version envelopes still load (tolerant reader).
- **Node behavior** — every `Dyncamelo.Nodes` node: happy path, documented warning cases (divide-by-zero, parse failure, empty list), replication behavior where relevant; CSV writer/parser RFC-4180 cases.

### Ring 2 — Windows CI (every PR)

`windows-latest` GitHub Actions job builds the **full** solution (`Dyncamelo.UI`, `Dyncamelo.App` included — WPF cannot compile on Linux) in Release, runs the same test suites, and produces the plugin-folder zip as a build artifact. Compilation of `Dyncamelo.Navisworks` against the pinned API packages is the compile-compat gate for Navisworks API usage.

### Ring 3 — Manual smoke checklist inside Navisworks

<a id="manual-smoke-checklist-inside-navisworks"></a>

No Navisworks in CI (licensing + no GUI), so a human runs this scripted checklist in Navisworks Manage 2024 before every release, and for any PR touching `Dyncamelo.Navisworks`, `UI`, or `App`:

1. **Install/launch** — copy zip to `Plugins\Dyncamelo.App\`; ribbon button appears; dock pane opens, docks, resizes, persists across restart.
2. **Editor basics** — add nodes from search; wire/rewire/delete; pan/zoom a 100+ node graph without lag; undo of canvas edits.
3. **Run semantics** — Manual vs Automatic; change one slider → only downstream re-executes (state badges confirm); freeze node ghosts downstream.
4. **Workflow 1 (QTO)** — property extraction to CSV on a real multi-model NWF; spot-check values incl. unit suffixes against the Properties pane.
5. **Workflow 2/3 (sets + color)** — bulk selection sets via lacing; color-code by system; single undo entry per run restores prior state; re-run idempotent.
6. **Workflow 4 (clash)** — read tests/results on a model with clash data; CSV report opens in Excel; (Beta) status/assign writes visible in the Clash Detective UI.
7. **Workflow 5 (viewpoints)** — batch save/apply; names correct; visible in the Saved Viewpoints pane.
8. **Failure drills** — run with no document; close document mid-session; disconnect a required input; a node fed garbage (Error badge, message readable, Navisworks alive).
9. **Persistence** — save graph, restart Navisworks, reload, re-run: identical results.
10. **(Beta+) TimeLiner** — task read-out matches TimeLiner window; AttachSet reflected in the UI. Records the 2023-ref binding verdict.

Results are pasted into the PR/release checklist. Any crash of Navisworks is a release blocker by definition.

## 7. Threading model

Stated once, enforced everywhere:

1. **All Navisworks API calls happen on the host main thread.** The Navisworks .NET API is not thread-safe and must be used from the thread that runs the Navisworks UI.
2. **The engine evaluates synchronously on the calling thread.** `Run()` walks the topologically sorted node list and executes nodes inline — no worker threads, no `Task.Run`, no parallel node execution.
3. **The UI triggers runs from its dispatcher thread, which *is* the Navisworks main thread** for a docked pane (the dock pane's WPF dispatcher is the host UI thread). Therefore every Navisworks node executes on the correct thread *by construction*, with zero marshalling code.
4. **Long runs and responsiveness.** Because runs occupy the UI thread, the engine checks a `CancellationToken` between nodes; the UI can pump a cancel request. Progress reporting is per-node (cheap). If profiling ever demands background evaluation of *pure* subgraphs, that is an explicit future engine feature — Navisworks nodes stay main-thread forever.
5. **Automatic mode** debounces: graph mutations request a run via a coalescing scheduler on the dispatcher, so a slider drag produces one trailing run, not fifty.
6. **Write scoping.** Navisworks write nodes (appearance, sets, viewpoints, clash/TimeLiner edits) execute inside a transaction/undo scope managed by the node host in `Dyncamelo.Navisworks`, yielding one undo entry per run and keeping the host UI in sync (all edits go through the documented `Document*` edit APIs, never direct setters on live objects).
7. **Debug guardrail.** Debug builds assert the ambient thread is the expected dispatcher thread at the Navisworks node-host boundary, so a future refactor that breaks rule 3 fails loudly in development, not in a user's session.

## 8. Versioning and compatibility strategy

### Product versioning

- **SemVer** for Dyncamelo itself: v0.1 (M2), v0.2 (M3), v1.0 (M4). Assembly + informational versions stamped by CI.
- **`.dyc` format versioning**: the envelope carries `formatVersion` (integer-ish "1.0" string) and the writing app version. Readers are **tolerant**: unknown JSON properties are preserved-or-ignored, never fatal; missing optional properties get defaults. Breaking format changes bump the major format version and keep the previous reader in place (one-way upgrade on save, warning to the user). A graph containing node types that are not installed loads as *unresolved nodes* (kept, shown as placeholders, saved back intact) rather than failing — protecting graphs that use packs or newer node sets.
- **Node identity** in `.dyc` is the stable zero-touch identifier (assembly-qualified type + method) plus the `[NodeName]`; renames ship with a mapping table so old graphs keep loading.

### Navisworks compatibility

- **v0.x targets Navisworks 2024 only** (single pinned compile-ref package set).
- **M4 introduces 2024/2025/2026 multi-targeting**: one codebase, MSBuild target multiplexing (`NAVIS2024`/`NAVIS2025`/`NAVIS2026` define constants + per-target compile-ref package pins), per-version output folders and release zips. API drift is quarantined in a small `Compat` layer; nodes code against our own wrappers where Autodesk churn is known.
- Navisworks add-ins load per product version (plugin folder per install), so side-by-side versions are naturally supported.
- Because the Autodesk assemblies are resolved from the host at runtime (compile refs are `ExcludeAssets="runtime"`, and the relevant assemblies are not strong-named), a binary built against the 2024 surface generally loads on later hosts — but we never rely on that accidentally: each supported version gets its own build and its own smoke pass.
- The `Application.Version` node exposes host product/API version so graphs themselves can branch on it.

## 9. Known caveats (tracked honestly)

| # | Caveat | Consequence | Status / plan |
|---|---|---|---|
| 1 | **TimeLiner compile reference is the 2023 package** (`Chuongmep...Timeliner 2023.0.7`) because no 2024 edition is published. The TimeLiner API is stable across 2023→2024 and the DLLs are not strong-named, so the host's 2024 assembly binds at runtime | If any referenced member changed in 2024, TimeLiner nodes fail at runtime (`MissingMethodException`) even though the build is green | All TimeLiner nodes are Beta-tier pending an in-host M3 spike; fallback is a reflection-based adapter or locally-generated reference assemblies (never redistributed) |
| 2 | **WPF projects cannot build on Linux** — `Dyncamelo.UI` and `Dyncamelo.App` compile only on Windows | This container/dev-box only builds Core/Nodes/Navisworks + tests; editor regressions surface on Windows CI, not locally on Linux | Accepted; enforced project layering keeps the un-buildable surface thin, `windows-latest` CI builds every PR |
| 3 | **No automated in-host testing** — Navisworks cannot run headless in CI | In-host behavior (threading, undo, UI sync, ComApi export) is covered only by the manual smoke checklist | Accepted; checklist is scripted, mandatory for releases and Navisworks-touching PRs |
| 4 | **`Export.ViewpointImage` rides the COM interop bridge** (`ComApiBridge` + `lcodpimage` plugin), not the managed API | Most fragile API surface we use; option plumbing is under-documented | Isolated in a single adapter; node is Beta and feature-flagged; known-good option sets documented |
| 5 | **Compile-ref packages are third-party republications** of the Autodesk API surface (Speckle, Chuongmep) | Coverage gaps are possible (a member missing from the package though present in the host) | Any gap gets a reflection shim + an upstream issue; runtime always uses genuine host assemblies |
| 6 | **No `.xlsx` writer in v0.x** — reporting is CSV (Excel-compatible) | Users wanting styled multi-sheet workbooks must post-process | Deliberate dependency-budget choice; revisit at M3 with an MIT/Apache candidate |
| 7 | **Script nodes (M4) execute arbitrary code from `.dyc` files** | Social-engineering surface for shared graphs | Load-time consent prompt + documented threat model before the feature ships |

## 10. Definition of "expandable" (acceptance of principle 2)

The architecture must keep all of these true at every milestone; they are re-checked at each release:

1. A new general node = one attributed static method + tests. No engine, UI, or serializer changes.
2. A new Navisworks node = the same, in `Dyncamelo.Navisworks`.
3. A new node **pack** = a DLL in a folder (M3+: no Dyncamelo rebuild at all).
4. A new interactive node = a `NodeModel` subclass + a WPF `DataTemplate`; the engine needs no changes.
5. A new Navisworks *version* = a new build target + compat shims; no node rewrites (M4).
6. A new graph feature that needs persistence = additive `.dyc` change under the tolerant-reader rules; old graphs keep loading.
