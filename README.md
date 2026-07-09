# Dyncamelo

**Dynamo-style visual programming for Autodesk Navisworks.**

<!-- Badges: enabled once CI workflows land in .github/workflows -->
[![Build](https://img.shields.io/badge/build-pending-lightgrey)](https://github.com/mrshoma99-rgb/dyncamelo/actions)
[![Tests](https://img.shields.io/badge/tests-pending-lightgrey)](https://github.com/mrshoma99-rgb/dyncamelo/actions)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Navisworks 2024](https://img.shields.io/badge/Navisworks-2024-blue)](#requirements)

Dyncamelo brings the visual-programming workflow that Dynamo made famous in Revit to **Autodesk Navisworks 2024**. Wire nodes together on a canvas, watch data flow from outputs into inputs, and let the dataflow engine run your graph against the live Navisworks document — no code, no macros, no SDK boilerplate.

> Search a federated model by property, color-code it by system, bulk-create selection sets, dump quantities to CSV, triage clashes by rule, and batch-generate viewpoints — as reusable, shareable `.dyc` graph files.

<!-- SCREENSHOT PLACEHOLDER: replace with a canvas screenshot once the editor is running in Navisworks.
![Dyncamelo editor docked in Navisworks 2024](docs/images/editor-screenshot.png)
-->
*Screenshot coming soon — the editor is under active development.*

---

## Features

- **Dynamo-like editor** — a node canvas (built on [Nodify](https://github.com/miroiu/nodify)) docked inside Navisworks: searchable node library, drag-to-wire connectors, pan/zoom, notes, watch nodes.
- **Real dataflow engine** — eager evaluation, topological execution, and dirty propagation: change one slider and only its downstream nodes re-run. Manual and Automatic run modes.
- **Replication ("lacing")** — feed a list into a scalar input and the node maps over it, exactly like Dynamo: Shortest by default, Longest and Cross-Product per node.
- **Robust by design** — a failing node surfaces a per-node Warning/Error state; it never crashes the graph run or Navisworks.
- **Deep Navisworks node library** — properties/QTO extraction, Find-Items-grade search, selection sets, color/transparency/hide overrides, saved viewpoints, clash test read-out and triage, TimeLiner, CSV/report export. See the full [node catalog](docs/NODE_LIBRARY.md) (~190 nodes planned, ~88 in the MVP).
- **Zero-touch extensibility** — write a `public static` C# method, tag it with `[NodeName]`/`[NodeCategory]`, drop the DLL in the Packages folder, and it appears in the library. No base classes required. See [Extending Dyncamelo](docs/EXTENDING.md).
- **Portable graphs** — graphs are saved as versioned JSON (`.dyc`) that is friendly to diffing and source control.
- **MIT licensed** — permissive dependencies only.

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
    NODES --> CORE

    NWAPI["Autodesk Navisworks 2024 API<br/>(bound from the host at runtime)"]
    NAV -.compile-time reference.-> NWAPI
```

`Dyncamelo.Core` and `Dyncamelo.Nodes` have **zero** UI or Navisworks dependencies — they compile and test anywhere (including Linux CI). Everything Navisworks-specific lives in `Dyncamelo.Navisworks`; everything WPF lives in `Dyncamelo.UI`/`Dyncamelo.App`. Details in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Requirements

- **To run:** Autodesk Navisworks Manage or Simulate **2024** on Windows.
- **To build:** Windows 10/11 with **Visual Studio 2022** (with ".NET desktop development" workload) or the **.NET 8 SDK**. No Navisworks installation is needed to build — the Navisworks API is referenced through compile-time-only NuGet packages.

## Quick start

### 1. Build from source (Windows)

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
> dotnet test tests/Dyncamelo.Core.Tests/Dyncamelo.Core.Tests.csproj
> dotnet test tests/Dyncamelo.Nodes.Tests/Dyncamelo.Nodes.Tests.csproj
> ```

### 2. Install into Navisworks 2024

Copy the build output of `Dyncamelo.App` into a plugin folder named **exactly like the plugin assembly**:

```
C:\Program Files\Autodesk\Navisworks Manage 2024\Plugins\Dyncamelo.App\
    Dyncamelo.App.dll
    Dyncamelo.UI.dll
    Dyncamelo.Navisworks.dll
    Dyncamelo.Nodes.dll
    Dyncamelo.Core.dll
    Nodify.dll
    Newtonsoft.Json.dll
```

(Adjust the path for Navisworks Simulate. A packaged installer/release zip is on the [roadmap](docs/IMPLEMENTATION_PLAN.md).)

### 3. Open the editor

Start Navisworks 2024, open a model, and launch **Dyncamelo** from the *Tool add-ins* ribbon tab. The editor opens as a dockable pane. Now follow the [Getting Started guide](docs/GETTING_STARTED.md) to build your first graph:

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

## Contributing

Contributions are very welcome — nodes, engine work, docs, sample graphs, bug reports. Start with [CONTRIBUTING.md](CONTRIBUTING.md). Core engine and general-node changes can be developed and tested on any OS; only the WPF editor and the in-host smoke tests need Windows/Navisworks.

## License

Dyncamelo is released under the [MIT License](LICENSE) — Copyright (c) 2026 Dyncamelo contributors.

Dyncamelo is not affiliated with or endorsed by Autodesk. Autodesk, Navisworks, Revit, and Dynamo are trademarks of Autodesk, Inc. The Autodesk Navisworks API assemblies are referenced at compile time only and are never redistributed with Dyncamelo; at runtime the API is provided by your licensed Navisworks installation.
