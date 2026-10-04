# Contributing to CamelGraph

Thanks for your interest in CamelGraph! This document explains how to set up a development environment, the coding conventions the project enforces, and how to get a change merged.

CamelGraph is source-available under the PolyForm Noncommercial License 1.0.0 (see [LICENSE](LICENSE)) — free for personal and other noncommercial use; professional use will be licensed through the copy for the Autodesk App Store (coming soon). By submitting a contribution you assign to the project owner (BIMCamel) all rights in the contribution and you confirm you are entitled to do so — this is what lets BIMCamel keep the whole work under one license (including granting commercial licenses) without chasing per-file permissions later.

## Ways to contribute

- **Nodes** — the generated [node catalogue](docs/NODE_CATALOG.md) lists every node that exists; [docs/NODE_LIBRARY.md](docs/NODE_LIBRARY.md) holds the design and the tiers. Unclaimed MVP/Beta nodes are great first issues.
- **Engine** — the dataflow engine in `CamelGraph.Core` (see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)) has a well-defined spec and a Linux-runnable test suite.
- **Docs and samples** — tutorials, sample `.dyc` graphs, screenshots.
- **Bug reports** — use the [bug report form](https://github.com/mrshoma99-rgb/dyncamelo/issues/new/choose). It asks for the CamelGraph and Navisworks versions, the end of `%APPDATA%\CamelGraph\errors.log` and the output of **Help > Copy Diagnostics**; the graph (`.dyc` attaches nicely to issues) and the node error text help too. [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) covers the common problems.

## Development environment

| You want to work on | You need |
|---|---|
| `CamelGraph.Core`, `CamelGraph.Nodes`, `CamelGraph.Cli`, `Core.Tests`, `Nodes.Tests`, `Integration.Tests` | Any OS with the .NET 8 SDK — Linux and macOS work fully |
| `CamelGraph.Navisworks` | Any OS (compiles against reference-only NuGet packages); Windows + Navisworks to actually run it |
| `CamelGraph.UI`, `CamelGraph.App`, `CamelGraph.Installer` (WPF, net48), `UI.Tests`, `UI.HostProbe` | Windows with Visual Studio 2022 or the .NET 8 SDK |

The projects in `tests/`:

| Project | What it checks |
|---|---|
| `CamelGraph.Core.Tests` | Engine, graph model, loader, serialization, editing logic, the command and settings catalogues |
| `CamelGraph.Nodes.Tests` | The general nodes and the pure logic extracted from the Navisworks nodes |
| `CamelGraph.Integration.Tests` | Whole graphs end to end, including every sample in `samples/` |
| `CamelGraph.UI.Tests` | The editor and the Player in real WPF windows (Windows only) |
| `CamelGraph.UI.HostProbe` | Not a test project: a small net48 program that hosts the editor the way Navisworks does (a WinForms `ElementHost`, no WPF `Application`). `UI.Tests` starts it as a child process; you do not run it yourself. |

### Build and test

```bash
# Cross-platform: build the portable projects and run the test suite
dotnet build src/CamelGraph.Core/CamelGraph.Core.csproj
dotnet build src/CamelGraph.Nodes/CamelGraph.Nodes.csproj
dotnet build src/CamelGraph.Navisworks/CamelGraph.Navisworks.csproj
dotnet build src/CamelGraph.Cli/CamelGraph.Cli.csproj
dotnet test tests/CamelGraph.Core.Tests/CamelGraph.Core.Tests.csproj
dotnet test tests/CamelGraph.Nodes.Tests/CamelGraph.Nodes.Tests.csproj
dotnet test tests/CamelGraph.Integration.Tests/CamelGraph.Integration.Tests.csproj
python3 tools/generate_node_catalog.py --check   # the node catalogue is current
```

```powershell
# Windows: build everything, including the WPF editor and add-in
dotnet build CamelGraph.sln -c Release
dotnet test CamelGraph.sln -c Release
```

CI builds the full solution and runs all four test projects (including `UI.Tests`) on `windows-latest`, and builds and tests the non-WPF projects on Linux, where it also checks the node catalogue and runs the samples that do not need Navisworks. A PR must keep **both** jobs green.

### Running inside Navisworks

On Windows, a **Debug** build deploys the whole application bundle (DLLs, ribbon layout, icons, samples and `PackageContents.xml`) to `%APPDATA%\Autodesk\ApplicationPlugins\CamelGraph.bundle\<year>` (the `DeployToBundle` target in `src/CamelGraph.App/CamelGraph.App.csproj`). Restart Navisworks to load it. The build targets Navisworks 2024 unless you pass `-p:NavisworksYear=2025` or `2026` (run `dotnet restore` with the same property first, because the API package differs by year). Or install a release with `CamelGraphSetup.exe`. The older `Plugins\CamelGraph.App\` folder layout still works; see the [Getting Started guide](docs/GETTING_STARTED.md).

For changes that touch Navisworks nodes, the editor or the installer, run the rows of the [release QA checklist](docs/QA_CHECKLIST.md) that apply (and the [manual smoke checklist](docs/IMPLEMENTATION_PLAN.md#manual-smoke-checklist-inside-navisworks) for the basics) before opening the PR, and note the result in the PR description. Say plainly when you could not try a change in Navisworks.

## Code style

Style is enforced by [.editorconfig](.editorconfig); your IDE will pick it up automatically. The load-bearing rules:

- **C# 10 (`LangVersion` 10), `Nullable` enable** on every project.
- **File-scoped namespaces** (`namespace CamelGraph.Core;`) — this is the project standard; block namespaces are not used.
- **No records, no init-only setters.** They compile awkwardly against net48/netstandard2.0 (`IsExternalInit` shims). Use classic classes with get/set or get-only properties.
- 4-space indentation, braces on new lines (Allman), `using` directives outside the namespace, `System` usings first.
- Private fields `_camelCase`, everything public `PascalCase`, interfaces `IPascalCase`.
- **XML doc comments on all public API** — for zero-touch nodes the `<summary>` doubles as user-facing help, so write it for end users, not implementers.
- Keep dependencies at zero: **do not add NuGet packages.** The allowed set is fixed (at the moment Newtonsoft.Json in `Core` and `Nodes`, AutomaticGraphLayout in `Core`, Nodify in `UI`, the compile-time-only Navisworks API packages, and the test packages; the [engineering decisions table](docs/IMPLEMENTATION_PLAN.md#engineering-decisions) predates AutomaticGraphLayout). If you believe a new dependency is justified, open an issue first.

## Project boundaries (important)

The layering is strict — the build enforces most of it, reviewers enforce the rest:

- `CamelGraph.Core` — **no** UI or Navisworks types. Graph model, engine, loader, serialization only.
- `CamelGraph.Nodes` — references `Core` **only**. Pure .NET nodes.
- `CamelGraph.Navisworks` — references `Core` and `Nodes` plus the compile-time-only Navisworks API packages. Every Navisworks API call must be safe on the host main thread. Writes should be undoable in Navisworks where its API allows it; whether one Navisworks Undo reverses a whole run is not known yet (see section 8 of the [QA checklist](docs/QA_CHECKLIST.md)).
- `CamelGraph.UI` — WPF/Nodify editor and Script Player; no Navisworks types.
- `CamelGraph.App` — the thin add-in shell that composes everything inside Navisworks (ribbon, panes, update check).
- `CamelGraph.Cli` — the headless runner (`run`, `validate`, `list-nodes`, `write-samples`); references `Core` and `Nodes` only, so it cannot run Navisworks nodes.
- `CamelGraph.Installer` — `CamelGraphSetup.exe`; no references to the other projects.

### Commands and settings

Every editor command (a menu item, a shortcut, a palette entry) is declared **once**, in `CommandCatalog` (`src/CamelGraph.Core/Editing/CommandCatalog.cs`). The menus, the shortcut table, the `F1` help, the command palette and [docs/UI_GUIDE.md](docs/UI_GUIDE.md) are all generated from it, so **a new command goes through `CommandCatalog`**, not straight into a menu or a key handler. Settings work the same way through `SettingsCatalog`. A test fails when `docs/UI_GUIDE.md` is out of date; regenerate it with:

```bash
CAMELGRAPH_REGEN_DOCS=1 dotnet test tests/CamelGraph.Core.Tests --filter UiGuideTests
```

## Adding a node — checklist

1. Check [docs/NODE_LIBRARY.md](docs/NODE_LIBRARY.md) for the agreed name, category, ports, and behavior, and the [node catalogue](docs/NODE_CATALOG.md) for what already exists. If your node is not in the design catalog, propose it in an issue first.
2. Implement it zero-touch (static method + attributes) unless it genuinely needs interactive UI — see [docs/EXTENDING.md](docs/EXTENDING.md).
3. Follow the error convention: **throw** for real failures (engine surfaces Error state), **warn and return null** for recoverable issues. Never swallow exceptions silently, never crash the run.
4. **A node that changes the model, writes or deletes a file, starts a program or sends data must be marked `[NodeFunction(NodeFunction.Modify)]`.** The Script Player decides whether to ask for confirmation from that mark, and the name-based guess is not reliable for this. Reading nodes are `Info`.
5. General-purpose node (`CamelGraph.Nodes`)? Add xunit tests in `tests/CamelGraph.Nodes.Tests` — they must pass on Linux.
6. Navisworks node? Unit-test any pure logic you can extract; cover the rest via the [QA checklist](docs/QA_CHECKLIST.md) (add a row for it) and say in the PR whether you tried it in Navisworks.
7. **Regenerate the node catalogue** and commit the result: `python3 tools/generate_node_catalog.py` (it rewrites `docs/NODE_CATALOG.md` and `docs/camelgraph-nodes.json`; CI runs it with `--check` and fails when they are out of date). Do this whenever you add, rename or retire a node.
8. **Build the wiki to check it**: `pip install -r tools/wiki/requirements.txt` once, then `python3 tools/build_wiki.py` (it builds the site in `build/wiki-site` from `docs/wiki-src`, `docs/TROUBLESHOOTING.md`, `docs/RECIPES.md`, `docs/EXTENDING.md`, `CHANGELOG.md` and the node catalogue; add `--serve` to see it while you edit). Nothing it makes is committed, and CI runs the same build in strict mode, so fix every warning. Do this whenever one of those changes. See [tools/wiki/README.md](tools/wiki/README.md).
9. Changing a node that has already shipped? Saved graphs must keep loading: keep an old definition id with `[NodeAliases]`, a renamed port with `[PortAlias]`, and retire a node with `[NodeDeprecated]` instead of deleting it. See [docs/EXTENDING.md](docs/EXTENDING.md#10-changing-a-node-that-is-already-shipped).
10. Update `docs/NODE_LIBRARY.md` if ports/behavior deviate from the design catalog (with reviewer agreement), and add a line to [CHANGELOG.md](CHANGELOG.md) for a user-visible change.

## Git workflow

- Branch from `main`; use descriptive branch names (`feature/list-groupbykey`, `fix/lacing-empty-list`).
- Keep commits focused; write imperative-mood commit messages ("Add List.GroupByKey node").
- Open a PR with: what/why, test evidence (CI plus QA checklist rows if applicable), and screenshots/GIFs for UI changes. The [pull request template](.github/pull_request_template.md) lists what to tick.
- One approving review is required. Maintainers squash-merge by default.

## Reporting security issues

Please do not open public issues for security-sensitive reports (e.g., anything enabling code execution through a `.dyc` file). Report them privately through a GitHub security advisory, as described in [SECURITY.md](SECURITY.md).

## Code of conduct

Be kind, be constructive, assume good faith. Harassment or personal attacks are not tolerated; maintainers may remove content and ban repeat offenders.
