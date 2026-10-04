# Dyncamelo

**Dynamo-style visual programming for Autodesk Navisworks.**

[![Build](https://github.com/mrshoma99-rgb/dyncamelo/actions/workflows/build.yml/badge.svg)](https://github.com/mrshoma99-rgb/dyncamelo/actions/workflows/build.yml)
[![Release](https://github.com/mrshoma99-rgb/dyncamelo/actions/workflows/release.yml/badge.svg)](https://github.com/mrshoma99-rgb/dyncamelo/actions/workflows/release.yml)
[![License: PolyForm Noncommercial 1.0.0](https://img.shields.io/badge/license-PolyForm%20Noncommercial%201.0.0-blue)](LICENSE)
[![Navisworks 2024 | 2025 | 2026](https://img.shields.io/badge/Navisworks-2024%20%7C%202025%20%7C%202026-blue)](#requirements)
[![Releases: source code](https://img.shields.io/badge/releases-source%20code-1f6feb)](https://github.com/mrshoma99-rgb/dyncamelo/releases/latest)

> ## Part of CamelWorks
> Dyncamelo ships inside **[CamelWorks](https://github.com/mrshoma99-rgb/Camelworks-navisworks-plugin)**,
> which installs it alongside the coordination, data and delivery apps and the
> [BIMCamel IFC exporter](https://github.com/mrshoma99-rgb/bimcamel-ifc-exporter) — one installer,
> one **BIMCamel** ribbon tab. CamelWorks' *Automate ▸ Graphs* tab runs a `.dyc` graph against the
> open document without opening this editor, and opens this editor when one needs changing.
>
> **Install [CamelWorks](https://github.com/mrshoma99-rgb/Camelworks-navisworks-plugin/releases/latest)
> to get all three.** Dyncamelo on its own, from this repository's releases, works exactly as it
> always has — the bundles sit side by side.

> ## Two ways to get it: free for personal use, the Autodesk App Store for professional use
> * **Personal use is free.** Download the installer from the [GitHub releases](https://github.com/mrshoma99-rgb/dyncamelo/releases/latest) or from [bimcamel.com](https://www.bimcamel.com/plugins/dyncamelo). It covers learning, hobby projects and research, and charities, schools, universities, public research bodies and government institutions, under the **[PolyForm Noncommercial License 1.0.0](LICENSE)**. These copies are marked *Personal use* in the installer, the About window and the start screen.
> * **Professional use: a copy for the Autodesk App Store is coming soon.** Using Dyncamelo for your job (at a contractor, consultancy, design office or any company) or inside a paid project or product is commercial use. The copy for the Autodesk App Store will come with a commercial licence from BIMCamel and will be updated by the store; the store listing will show the terms.
>
> It is the same program in both; only the licence and where you get it differ. The source is readable and changeable on the noncommercial terms (so this is "source-available" rather than OSI-certified open source). Releases up to v0.45.1 were published under Apache 2.0 with the Commons Clause, and copies you already have stay under that licence ([details](docs/licenses/README.md)).

Dyncamelo brings the visual-programming workflow that Dynamo made famous in Revit to **Autodesk Navisworks 2024, 2025 & 2026**. Wire nodes together on a canvas, watch data flow from outputs into inputs, and let the dataflow engine run your graph against the live Navisworks document — no code, no macros, no SDK boilerplate.

> Search a federated model by property, color-code it by system, bulk-create selection sets, dump quantities to CSV, triage clashes by rule, and batch-generate viewpoints — as reusable, shareable `.dyc` graph files.

![The Dyncamelo editor with a sample graph](docs/images/editor-screenshot.png)

*The editor with the "Table Summary from Text" sample after a run: nodes, wires, the values under each node and a Watch Table. The same editor has a [light theme](docs/images/editor-screenshot-light.png). It docks inside Navisworks as a pane.*

More pictures: [start screen](docs/images/editor-start.png) · [light theme](docs/images/editor-screenshot-light.png) · [quick node search](docs/images/quick-search.png) · [command palette](docs/images/command-palette.png) · [keyboard and mouse sheet](docs/images/shortcuts.png) · [settings](docs/images/settings.png) · [Script Player](docs/images/player.png)

**Documentation: the [Dyncamelo wiki](https://www.bimcamel.com/plugins/dyncamelo/wiki/)** — installing and removing it, a first script, the editor, how-to guides (clash report, BCF, Excel, quantities), the Script Player, an FAQ and every node with its inputs and outputs.

---

## What's new in v0.12–v0.23 — site safety & spatial analysis

- **Fall-hazard analysis suite** — `FallHazard.FloorOpeningMap` renders a whole-floor heat map of openings from the real model mesh (limit-pivoting gradient, user colours, printed gap-over-limit labels, one saved viewpoint per flagged opening), and `FallHazard.EdgeHandrailCheck` classifies every edge around a void as **dangerous / protected / safe** — with real handrail length-along-edge coverage, a min-passage rule, user colours and printed overages for reports. Both take a **`units`** input so metre inputs stay honest in feet-based documents.
- **Spatial clustering** — `Proximity.Cluster` groups touching geometry into logical elements (a ladder made of loose shapes becomes ladder #1, #2, …), stamps each item's number as a searchable custom property in the same run, and has a precise `mesh` mode that confirms every connection with the Clash engine's exact clearance.
- **Viewpoint intelligence & markups** — `Viewpoint.VisibleItems` answers "does this view actually show these elements?" (camera-frustum test, flexible set/list/name inputs); the experimental `Markup.*` nodes draw text, arrows, ellipses, clouds and numbered tag substitutes onto saved viewpoints via the hidden Navisworks redline API.
- **Color toolkit** — seeded `Color.Random` / `Color.RandomList` (stable across re-runs, golden-angle distinct), `Color.Gradient` between two colors, and `Color.ByValues` + `Appearance.ColorByValues` for one-node color-coding by parameter value with a legend.
- **Ordering, the industry way** — `Flow.Then` pins the execution order of side-effect nodes as a real data dependency (the Dynamo Passthrough pattern); a Captured Selection node snapshots the live selection and replays it every run.
- **Editor quality of life** — Space-bar quick node search at the cursor, port tooltips generated from the API docs on all **314 nodes**, an inline **Watch Image** node for the analysis PNGs, an index gutter on Watch List, click-to-expand preview bubbles, a proper Boolean switch, and Create/Modify/Info grouping with symbols throughout the library.
- **BIMCamel ribbon tab** — Dyncamelo, Player and About buttons, a unified About window, and an update check when the editor opens.

The newest release is 0.48.0 (the personal-use edition with the installer and the bundle zip; one header row, a wide Run button, faster nodes on big models, working clash grouping nodes: see [CHANGELOG.md](CHANGELOG.md))); 0.46.0 was a source-code release (new licence, a question before running graphs from files, Help > Run Self-Test, Copy Diagnostics and Privacy Policy); 0.45.1 was [a fix for picking from the node library](docs/WHATS_NEW_0.45.1.md); 0.45 (tables, conditional flow, about 220 new nodes) is described in [docs/WHATS_NEW_0.45.md](docs/WHATS_NEW_0.45.md), with role-by-role [recipes](docs/RECIPES.md). Full details of the 0.23 wave in [docs/WHATS_NEW_0.23.md](docs/WHATS_NEW_0.23.md). Earlier waves: v0.10–0.11 universal loops, live element preview & viewpoint organizing; v0.4 instant library search & curated samples; v0.3 "plugin parity"; v0.2 editor quality-of-life — see [docs/](docs/).

## Features

- **Dynamo-like editor** — a node canvas (built on [Nodify](https://github.com/miroiu/nodify)) docked inside Navisworks: searchable node library, drag-to-wire connectors, pan/zoom, notes, watch nodes.
- **Real dataflow engine** — eager evaluation, topological execution, and dirty propagation: change one slider and only its downstream nodes re-run. Manual and Automatic run modes.
- **Replication ("lacing")** — feed a list into a scalar input and the node maps over it, exactly like Dynamo: Shortest by default, Longest and Cross-Product per node.
- **Robust by design** — a failing node surfaces a per-node Warning/Error state; it never crashes the graph run or Navisworks.
- **Per-item workflows** — a universal loop (`Loop.Item` → body → `Loop.Collect`) runs any nodes once per item, in order, so stateful "isolate → zoom → save viewpoint → next" jobs work with the real nodes, not just pure data mapping. `Flow.Then` pins side-effect order explicitly when two writes must happen in sequence.
- **Spatial & safety analysis** — fall-hazard heat maps and edge/handrail classification straight from the model mesh, touching-geometry clustering with custom-property stamping, camera-frustum visibility tests, and exact clash-engine distances.
- **Deep Navisworks node library** — properties/QTO extraction and custom property writing, Find-Items-grade search, selection sets, color/transparency/hide overrides (permanent and viewpoint-scoped), transforms, saved viewpoints (incl. experimental redline markups), IFC export, clash triage/grouping/deltas, BCF 2.1 exchange, grids, TimeLiner, CSV/Excel/report export. See the generated [node catalogue](docs/NODE_CATALOG.md) — every node by category with its inputs and outputs, kept current by CI (also as [JSON](docs/dyncamelo-nodes.json)) — and [NODE_LIBRARY.md](docs/NODE_LIBRARY.md) for the design conventions.
- **Zero-touch extensibility** — write a `public static` C# method, tag it with `[NodeName]`/`[NodeCategory]`, drop the DLL in the Packages folder, and it appears in the library. No base classes required. See [Extending Dyncamelo](docs/EXTENDING.md).
- **Portable graphs** — graphs are saved as versioned JSON (`.dyc`) that is friendly to diffing and source control.
- **Source-available** — PolyForm Noncommercial 1.0.0: free for personal and other noncommercial use; commercial use needs a licence from BIMCamel. Third-party components ship under their own permissive licenses (see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)).

## Part of the BIMCamel toolset

- **[CamelWorks](https://github.com/mrshoma99-rgb/Camelworks-navisworks-plugin)** — the coordination,
  data and delivery suite: clash, issues, model audit, revision compare, properties, quantities,
  sets, exports and the site tools. **It ships Dyncamelo and the IFC exporter inside its own
  installer**, so one download puts all three on one ribbon tab, and its Automate ▸ Graphs tab runs
  Dyncamelo graphs without opening the editor.
- **[BIMCamel IFC Exporter](https://github.com/mrshoma99-rgb/bimcamel-ifc-exporter)** — free, fast
  **Navisworks → IFC** export (IFC4 / IFC2x3): streaming engine, geometry instancing, property
  sets, classifications and georeferencing. Website:
  [bimcamel.com/Export-Navisworks-to-Ifc](https://www.bimcamel.com/Export-Navisworks-to-Ifc).
  Both plug-ins install the same way; installed together, each adds its own **BIMCamel** tab.
- **[bimcamel.com](https://www.bimcamel.com)** — browser-based IFC tools (validate, compare,
  upgrade / downgrade schema…).

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

## Install

Download **`DyncameloSetup.exe`** from the [latest release](https://github.com/mrshoma99-rgb/dyncamelo/releases/latest) (or the bundle zip next to it), run it (per user, no administrator rights), then start Navisworks 2024, 2025 or 2026 and open **Dyncamelo** from the **BIMCamel** ribbon tab. The installer is not code-signed, so Windows SmartScreen may warn: choose **More info**, then **Run anyway**, after comparing the SHA-256 checksum next to the download. Some releases carry the source code only (v0.46.0 did); the assets list of a release says which kind it is. To build from source instead, see the next section; a Debug build of the solution puts the whole bundle in `%APPDATA%\Autodesk\ApplicationPlugins\Dyncamelo.bundle` for you (see [`dist/README.md`](dist/README.md)). If Navisworks reports `PLUGIN_LOAD_02`, see [Troubleshooting](docs/TROUBLESHOOTING.md).

**Licence.** In plain words: the copies on GitHub and bimcamel.com are free for personal use and other noncommercial use (hobby, learning, research, charities, schools, public bodies). Using Dyncamelo for work at a company, or in a paid project or product, needs the professional copy, which is coming soon to the Autodesk App Store and will come with a commercial licence from BIMCamel. The text of [LICENSE](LICENSE) is what counts for the free copies; this is a summary, not legal advice. If you are not sure whether your use is commercial, assume it is. The third-party parts keep their own licences ([THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)).

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

To run a source build in Navisworks, use the application-bundle layout under `%APPDATA%\Autodesk\ApplicationPlugins\Dyncamelo.bundle` (see [`dist/README.md`](dist/README.md)); a Debug build sets it up for you.

## Your first graph

Open a model in Navisworks and launch **Dyncamelo** from the **BIMCamel** ribbon tab — the editor opens as a dockable pane. Then follow the [Getting Started guide](docs/GETTING_STARTED.md) (or the [wiki's first-script walkthrough](https://www.bimcamel.com/plugins/dyncamelo/wiki/first-steps.html)):

> *Find every item whose Material contains "Concrete", color it red, and save it as a selection set* — about six nodes, no code.

## Documentation

| Document | What it covers |
|---|---|
| [Getting Started](docs/GETTING_STARTED.md) | Install, editor tour, your first graph, lacing, saving/loading `.dyc` |
| [Editor guide](docs/UI_GUIDE.md) | Every command, shortcut, gesture and setting of the editor (generated from the editor itself) |
| [Node Library](docs/NODE_LIBRARY.md) | The full node catalog: ports, behavior, Navisworks API mapping, tiers |
| [Architecture](docs/ARCHITECTURE.md) | Projects, engine pipeline, zero-touch loading, `.dyc` format, threading |
| [Extending Dyncamelo](docs/EXTENDING.md) | Write your own node pack; custom NodeModel nodes with custom UI |
| [Implementation Plan](docs/IMPLEMENTATION_PLAN.md) | Vision, milestones M0-M5, engineering decisions, testing strategy, risks |
| [Wiki](https://www.bimcamel.com/plugins/dyncamelo/wiki/) | The user guide at bimcamel.com: installation, a first script, the editor, the Script Player, samples, how-to guides, IFC / BCF / Excel exchange, troubleshooting and a page per node category with inputs and outputs. Its source is [docs/wiki-src](docs/wiki-src/index.md), built with MkDocs (Material theme) by `python tools/build_wiki.py` into `build/wiki-site`; see [tools/wiki/README.md](tools/wiki/README.md) |
| [Troubleshooting](docs/TROUBLESHOOTING.md) | Symptom, cause and fix: missing ribbon tab, `PLUGIN_LOAD_02`, red nodes, slow runs, diagnostics, uninstalling, known issues |
| [Changelog](CHANGELOG.md) | What changed in each release, newest first |
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

## Status and support

Dyncamelo supports Autodesk Navisworks Manage and Simulate **2024, 2025 and 2026** on Windows. Navisworks cannot be run in the automated tests, so the nodes that talk to Navisworks are checked by hand, and so far only Navisworks Manage 2024 has been used in the field; the 2025 and 2026 builds are not yet verified in Navisworks (see the [known issues](docs/TROUBLESHOOTING.md#known-issues)). To report a problem, first look in the [troubleshooting guide](docs/TROUBLESHOOTING.md), then open an issue with the [bug report form](https://github.com/mrshoma99-rgb/dyncamelo/issues/new/choose). It asks for your versions, `%APPDATA%\Dyncamelo\errors.log` and the output of **Help > Copy Diagnostics**. Report a security problem privately, as described in [SECURITY.md](SECURITY.md), and read there what a `.dyc` graph file can do before you run one from someone else.

## Feedback

Dyncamelo is developed by BIMCamel and is open to the community: bug reports, feature requests and pull requests are all welcome. Open a [GitHub issue](https://github.com/mrshoma99-rgb/dyncamelo/issues), read [CONTRIBUTING.md](CONTRIBUTING.md) before sending a PR, or reach us at [bimcamel.com](https://www.bimcamel.com/plugins/dyncamelo).

## License

Dyncamelo is **source-available** under the **PolyForm Noncommercial License 1.0.0** (see [LICENSE](LICENSE)): you may use it for personal and other noncommercial purposes — study, hobby projects, research, charities, schools, public research and government bodies — modify it and share it on those terms. Commercial use is covered by the professional copy for the Autodesk App Store, which is coming soon (see the box at the top of this page). Licensing history: releases up to v0.1.1 were MIT-licensed, v0.1.2–v0.26.1 proprietary and v0.26.2–v0.45.1 Apache 2.0 with the Commons Clause; each grant applies to copies obtained while it was in effect ([texts](docs/licenses/README.md)). Third-party components ship under their own licenses: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Dyncamelo is not affiliated with or endorsed by Autodesk. Autodesk, Navisworks, Revit, and Dynamo are trademarks of Autodesk, Inc. The Autodesk Navisworks API assemblies are referenced at compile time only and are never redistributed with Dyncamelo; at runtime the API is provided by your licensed Navisworks installation.
