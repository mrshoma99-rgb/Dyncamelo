# Dyncamelo

**Dynamo-style visual programming for Autodesk Navisworks.**

<!-- Badges: enabled once CI workflows land in .github/workflows -->
[![Build](https://img.shields.io/badge/build-pending-lightgrey)](https://github.com/mrshoma99-rgb/dyncamelo/actions)
[![Tests](https://img.shields.io/badge/tests-pending-lightgrey)](https://github.com/mrshoma99-rgb/dyncamelo/actions)
[![License: Proprietary](https://img.shields.io/badge/license-proprietary-red)](LICENSE)
[![Navisworks 2024 | 2025 | 2026](https://img.shields.io/badge/Navisworks-2024%20%7C%202025%20%7C%202026-blue)](#requirements)
[![Download](https://img.shields.io/badge/download-DyncameloSetup.exe-1f6feb)](https://github.com/mrshoma99-rgb/dyncamelo/releases/latest)

> ## ⚠️ Proprietary software — public, but **not open source**
> This repository is public so you can browse the code and download official releases. Dyncamelo is **© 2026 BIMCamel — all rights reserved**. No license is granted to use, copy, modify, merge, publish, distribute, sublicense, or sell any part of this software, in source or binary form, except as expressly permitted in writing by BIMCamel. Being able to read the source here does **not** grant any such rights. See [LICENSE](LICENSE).

Dyncamelo brings the visual-programming workflow that Dynamo made famous in Revit to **Autodesk Navisworks 2024, 2025 & 2026**. Wire nodes together on a canvas, watch data flow from outputs into inputs, and let the dataflow engine run your graph against the live Navisworks document — no code, no macros, no SDK boilerplate.

> Search a federated model by property, color-code it by system, bulk-create selection sets, dump quantities to CSV, triage clashes by rule, and batch-generate viewpoints — as reusable, shareable `.dyc` graph files.

<!-- SCREENSHOT PLACEHOLDER: replace with a canvas screenshot once the editor is running in Navisworks.
![Dyncamelo editor docked in Navisworks 2024](docs/images/editor-screenshot.png)
-->
*Screenshot coming soon — the editor is under active development.*

---

## What's new in v0.11 — a library you can read at a glance

- **Create / Modify / Info grouping** — at the last level of every category the node library now splits nodes into three groups — **Create** (＋ green, makes something new), **Modify** (✎ amber, changes existing things), **Info** (ⓘ blue, reads data) — each with its own symbol, so you find the right node faster. The category tree above is unchanged. Every placed node also carries a matching coloured dot on the canvas, so what a node does stays obvious where you work.
- **Saved-viewpoint organizing nodes** — `Viewpoints.SortFolder` (alphabetical, no more drag-and-drop), `SavedViewpoint.Duplicate`, `Viewpoints.DuplicateFolder`, `Viewpoints.RenameFolder`, and `SavedViewpoint.CopyOverrides` (copy one view's colour/ghosting onto another, keeping its camera).
- **Run profiling** — a slow run now names the node that ate the time (e.g. "slowest: Viewpoint.SaveWithOverrides 71,200 ms (17×)"), and a busy indicator shows the graph is working rather than frozen.

## What's new in v0.10 — per-item workflows

- **Universal loops** — a real loop construct: drop **`Loop.Item`** and **`Loop.Collect`** on the canvas and everything wired between them runs **once per item, in order**, built from the ordinary nodes. Generate a viewpoint per room, recolour per system, export per level — no special "action" nodes required.
- **Per-item viewpoints that keep their look** — `Viewpoint.SaveWithOverrides` captures the camera **and** the current isolation/colour into each saved view (Navisworks runtime overrides), so recalling a view restores exactly what it showed. Temporary highlight/ghost nodes (`Appearance.OverrideColorTemporary`, `Action.Highlight`/`Action.Ghost`) make "spotlight each element in its own view" a handful of nodes.
- **Reified actions + `Workflow.ForEach`** — the earlier per-item mechanism (`Action.Isolate` / `ZoomTo` / `SaveViewpoint`) still ships alongside the loop region.
- **Live element preview** — select a node and its output elements highlight in the Navisworks scene (toggle in settings), so you can see exactly what a node collected or produced — the same feedback loop Dynamo gives you.
- **Inline choice dropdowns** — inputs that accept one of a fixed set of values (selection-resolution levels, clash test types, IFC schema, comment status, distance method) now offer a themed dropdown right on the port instead of a free-text box.
- **Colour picker & editable slider step** — the Color Picker node opens a proper HSV dialog, and the number/integer sliders expose an editable step field.
- **Navisworks 2024, 2025 & 2026** — one multi-year bundle and installer.
- **Graphical installer** — `DyncameloSetup.exe` (per-user, no admin rights) with a BIMCamel-themed UI; installs the ribbon add-in and an Add/Remove Programs entry.
- **280+ nodes** and growing — including IFC export, quick geometry/selection-tree utilities (bounding-box scaling, object-ancestor), and camera projection / field-of-view.

Earlier waves: v0.4 instant library search & curated samples, v0.3 "plugin parity" (50 nodes: custom property tabs, Excel, BCF 2.1, clash management, transforms, grids, document lifecycle), v0.2 editor quality-of-life — see the changelogs under [docs/](docs/).

## Features

- **Dynamo-like editor** — a node canvas (built on [Nodify](https://github.com/miroiu/nodify)) docked inside Navisworks: searchable node library, drag-to-wire connectors, pan/zoom, notes, watch nodes.
- **Real dataflow engine** — eager evaluation, topological execution, and dirty propagation: change one slider and only its downstream nodes re-run. Manual and Automatic run modes.
- **Replication ("lacing")** — feed a list into a scalar input and the node maps over it, exactly like Dynamo: Shortest by default, Longest and Cross-Product per node.
- **Robust by design** — a failing node surfaces a per-node Warning/Error state; it never crashes the graph run or Navisworks.
- **Per-item workflows** — a universal loop (`Loop.Item` → body → `Loop.Collect`) runs any nodes once per item, in order, so stateful "isolate → zoom → save viewpoint → next" jobs work with the real nodes, not just pure data mapping.
- **Deep Navisworks node library** — properties/QTO extraction and custom property writing, Find-Items-grade search, selection sets, color/transparency/hide overrides (permanent and viewpoint-scoped), transforms, saved viewpoints, IFC export, clash triage/grouping/deltas, BCF 2.1 exchange, grids, TimeLiner, CSV/Excel/report export. See the full [node catalog](docs/NODE_LIBRARY.md) (280+ nodes).
- **Zero-touch extensibility** — write a `public static` C# method, tag it with `[NodeName]`/`[NodeCategory]`, drop the DLL in the Packages folder, and it appears in the library. No base classes required. See [Extending Dyncamelo](docs/EXTENDING.md).
- **Portable graphs** — graphs are saved as versioned JSON (`.dyc`) that is friendly to diffing and source control.
- **Proprietary** — © 2026 BIMCamel, all rights reserved. Third-party components ship under their own permissive licenses (see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)).

## Architecture at a glance

```mermaid
graph TD
    APP["Dyncamelo.App<br/>net48 - Navisworks add-in<br/>(AddInPlugin + DockPanePlugin)"]
    UI["Dyncamelo.UI<br/>net48 WPF - node editor<br/>(Nodify canvas, library browser)"]
    NAV["Dyncamelo.Navisworks<br/>net48 - Navisworks node library"]
    NODES["Dyncamelo.Nodes<br/>netstandard2.0 - general node library<br/>(math, logic, string, list, color, file)"]
    CORE["Dyncamelo.Core<br/>netstandard2.0 - graph model, engine,<br/>zero-touch loader, .dyc serialization"]

    APP --> UI
    APP --> NAV
    APP --> NODES
    UI --> CORE
    NAV --> CORE
    NAV --> NODES
    NODES --> CORE

    NWAPI["Autodesk Navisworks 2024–2026 API<br/>(bound from the host at runtime)"]
    NAV -.compile-time reference.-> NWAPI
```

`Dyncamelo.Core` and `Dyncamelo.Nodes` have **zero** UI or Navisworks dependencies — they compile and test anywhere (including Linux CI). Everything Navisworks-specific lives in `Dyncamelo.Navisworks`; everything WPF lives in `Dyncamelo.UI`/`Dyncamelo.App`. Details in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Requirements

- **To run:** Autodesk Navisworks Manage or Simulate **2024, 2025, or 2026** on Windows.
- **To build:** Windows 10/11 with **Visual Studio 2022** (with ".NET desktop development" workload) or the **.NET 8 SDK**. No Navisworks installation is needed to build — the Navisworks API is referenced through compile-time-only NuGet packages.

## Install (recommended)

Download **[`DyncameloSetup.exe`](https://github.com/mrshoma99-rgb/dyncamelo/releases/latest)** from the latest release and run it. The graphical installer places the bundle in `%APPDATA%\Autodesk\ApplicationPlugins` (per-user, no admin rights) and registers an Add/Remove Programs entry. If Windows SmartScreen appears (unsigned download), choose **More info → Run anyway**. Start Navisworks 2024/2025/2026 and open **Dyncamelo** from the **BIMCamel** ribbon tab.

Silent install/uninstall: `DyncameloSetup.exe /silent` and `DyncameloSetup.exe /uninstall /silent`.

## Build from source (Windows)

```powershell
git clone https://github.com/mrshoma99-rgb/dyncamelo.git
cd dyncamelo
dotnet build Dyncamelo.sln -c Release
dotnet test Dyncamelo.sln -c Release
```

Or open `Dyncamelo.sln` in Visual Studio 2022 and build the `Release` configuration.

> **Linux/macOS note:** `Dyncamelo.Core`, `Dyncamelo.Nodes`, `Dyncamelo.Navisworks`, and the test projects build off-Windows (netstandard2.0/net8.0; the Navisworks library compiles against reference assemblies). The WPF projects (`Dyncamelo.UI`, `Dyncamelo.App`) require Windows, so `dotnet build Dyncamelo.sln` only succeeds there; CI builds the full solution on `windows-latest` and the non-WPF projects on `ubuntu-latest`:
>
> ```bash
> dotnet build src/Dyncamelo.Core/Dyncamelo.Core.csproj
> dotnet build src/Dyncamelo.Nodes/Dyncamelo.Nodes.csproj
> dotnet build src/Dyncamelo.Navisworks/Dyncamelo.Navisworks.csproj
> dotnet build src/Dyncamelo.Cli/Dyncamelo.Cli.csproj
> dotnet test tests/Dyncamelo.Core.Tests/Dyncamelo.Core.Tests.csproj
> dotnet test tests/Dyncamelo.Nodes.Tests/Dyncamelo.Nodes.Tests.csproj
> dotnet test tests/Dyncamelo.Integration.Tests/Dyncamelo.Integration.Tests.csproj
> ```
>
> You can also run headless graphs (no Navisworks needed) with the cross-platform CLI:
>
> ```bash
> dotnet run --project src/Dyncamelo.Cli -- run samples/hello-math.dyc
> ```
> See [samples/README.md](samples/README.md) for the bundled example graphs.

To run a source build in Navisworks without the installer, use the application-bundle layout under `%APPDATA%\Autodesk\ApplicationPlugins\Dyncamelo.bundle` (see [`dist/README.md`](dist/README.md)); the released `DyncameloSetup.exe` sets this up for you.

## Your first graph

Open a model in Navisworks and launch **Dyncamelo** from the **BIMCamel** ribbon tab — the editor opens as a dockable pane. Then follow the [Getting Started guide](docs/GETTING_STARTED.md):

> *Find every item whose Material contains "Concrete", color it red, and save it as a selection set* — about six nodes, no code.

## Documentation

| Document | What it covers |
|---|---|
| [Getting Started](docs/GETTING_STARTED.md) | Install, editor tour, your first graph, lacing, saving/loading `.dyc` |
| [Node Library](docs/NODE_LIBRARY.md) | The full node catalog: ports, behavior, Navisworks API mapping, tiers |
| [Architecture](docs/ARCHITECTURE.md) | Projects, engine pipeline, zero-touch loading, `.dyc` format, threading |
| [Extending Dyncamelo](docs/EXTENDING.md) | Write your own node pack; custom NodeModel nodes with custom UI |
| [Implementation Plan](docs/IMPLEMENTATION_PLAN.md) | Vision, milestones M0-M5, engineering decisions, testing strategy, risks |
| [Contributing](CONTRIBUTING.md) | Dev setup, code style, PR workflow |

## Roadmap summary

| Milestone | Theme | Highlights |
|---|---|---|
| **M0 Foundation** | Engine + libraries | Graph model, dataflow engine (dirty propagation, lacing, coercion), zero-touch loader, `.dyc` format, general node library, green tests on Linux |
| **M1 MVP editor** | Editor in Navisworks | Dock pane with Nodify canvas, node browser, run modes, save/load, first Navisworks nodes end-to-end |
| **M2 Full MVP node set** | The 88 MVP nodes | Search, properties/QTO, selection sets, appearance, viewpoints, clash read-out; all reference workflows runnable |
| **M3 Beta** | Depth + reporting | Clash triage writes, TimeLiner, image/CSV report export, node packages loaded from folders |
| **M4 v1.0** | Power + reach | IronPython/Roslyn script nodes, Navisworks 2024-2026 multi-targeting, localization |
| **M5 Community** | Ecosystem | Package manager, sample graph gallery |

Full milestone breakdown with exit criteria and risks: [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md).

## Feedback

Dyncamelo is proprietary software developed by BIMCamel; the source is public for transparency and distribution, **not** for outside contribution, and external pull requests are not accepted. Bug reports and feature requests are welcome — please open a [GitHub issue](https://github.com/mrshoma99-rgb/dyncamelo/issues) or reach us at [bimcamel.com](https://www.bimcamel.com/plugins/dyncamelo).

## License

Dyncamelo is **proprietary software** — Copyright (c) 2026 BIMCamel, all rights reserved (see [LICENSE](LICENSE)). This repository being public does not grant any license to use, copy, modify, or redistribute the source or binaries; all rights are reserved except where BIMCamel grants them in writing. Releases up to v0.1.1 were MIT-licensed; that grant remains valid only for copies obtained while it was in effect. Third-party components ship under their own licenses: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Dyncamelo is not affiliated with or endorsed by Autodesk. Autodesk, Navisworks, Revit, and Dynamo are trademarks of Autodesk, Inc. The Autodesk Navisworks API assemblies are referenced at compile time only and are never redistributed with Dyncamelo; at runtime the API is provided by your licensed Navisworks installation.
