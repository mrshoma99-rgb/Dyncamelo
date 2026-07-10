# Contributing to Dyncamelo

Thanks for your interest in Dyncamelo! This document explains how to set up a development environment, the coding conventions the project enforces, and how to get a change merged.

Dyncamelo is proprietary software (see [LICENSE](LICENSE)). By submitting a contribution you assign to the project owner (BIMCamel) all rights in the contribution, and you confirm you are entitled to do so.

## Ways to contribute

- **Nodes** — the [node catalog](docs/NODE_LIBRARY.md) lists every planned node with its tier. Unclaimed MVP/Beta nodes are great first issues.
- **Engine** — the dataflow engine in `Dyncamelo.Core` (see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)) has a well-defined spec and a Linux-runnable test suite.
- **Docs and samples** — tutorials, sample `.dyc` graphs, screenshots.
- **Bug reports** — include Navisworks version, the graph (`.dyc` attaches nicely to issues), and the node error text.

## Development environment

| You want to work on | You need |
|---|---|
| `Dyncamelo.Core`, `Dyncamelo.Nodes`, tests | Any OS with the .NET 8 SDK — Linux and macOS work fully |
| `Dyncamelo.Navisworks` | Any OS (compiles against reference-only NuGet packages); Windows + Navisworks 2024 to actually run it |
| `Dyncamelo.UI`, `Dyncamelo.App` (WPF) | Windows with Visual Studio 2022 or the .NET 8 SDK |

### Build and test

```bash
# Cross-platform: build the portable projects and run the test suite
dotnet build src/Dyncamelo.Core/Dyncamelo.Core.csproj
dotnet build src/Dyncamelo.Nodes/Dyncamelo.Nodes.csproj
dotnet build src/Dyncamelo.Navisworks/Dyncamelo.Navisworks.csproj
dotnet test tests/Dyncamelo.Core.Tests/Dyncamelo.Core.Tests.csproj
dotnet test tests/Dyncamelo.Nodes.Tests/Dyncamelo.Nodes.Tests.csproj
```

```powershell
# Windows: build everything, including the WPF editor and add-in
dotnet build Dyncamelo.sln -c Release
dotnet test Dyncamelo.sln -c Release
```

CI builds the full solution on `windows-latest` and runs the test projects on Linux, so a PR must keep **both** green.

### Running inside Navisworks

Copy the `Dyncamelo.App` output to `C:\Program Files\Autodesk\Navisworks Manage 2024\Plugins\Dyncamelo.App\` (folder name must match the DLL name) and start Navisworks. See the [Getting Started guide](docs/GETTING_STARTED.md). For changes that touch Navisworks nodes or the editor, run the [manual smoke checklist](docs/IMPLEMENTATION_PLAN.md#manual-smoke-checklist-inside-navisworks) before opening the PR and note the result in the PR description.

## Code style

Style is enforced by [.editorconfig](.editorconfig); your IDE will pick it up automatically. The load-bearing rules:

- **C# 10 (`LangVersion` 10), `Nullable` enable** on every project.
- **File-scoped namespaces** (`namespace Dyncamelo.Core;`) — this is the project standard; block namespaces are not used.
- **No records, no init-only setters.** They compile awkwardly against net48/netstandard2.0 (`IsExternalInit` shims). Use classic classes with get/set or get-only properties.
- 4-space indentation, braces on new lines (Allman), `using` directives outside the namespace, `System` usings first.
- Private fields `_camelCase`, everything public `PascalCase`, interfaces `IPascalCase`.
- **XML doc comments on all public API** — for zero-touch nodes the `<summary>` doubles as user-facing help, so write it for end users, not implementers.
- Keep dependencies at zero: **do not add NuGet packages.** The allowed set is fixed and documented in the [engineering decisions table](docs/IMPLEMENTATION_PLAN.md#engineering-decisions). If you believe a new dependency is justified, open an issue first.

## Project boundaries (important)

The layering is strict — the build enforces most of it, reviewers enforce the rest:

- `Dyncamelo.Core` — **no** UI or Navisworks types. Graph model, engine, loader, serialization only.
- `Dyncamelo.Nodes` — references `Core` **only**. Pure .NET nodes.
- `Dyncamelo.Navisworks` — references `Core` plus the compile-time-only Navisworks API packages. Every Navisworks API call must be safe on the host main thread and must go through the documented transaction/undo scoping for writes.
- `Dyncamelo.UI` — WPF/Nodify editor; no Navisworks types.
- `Dyncamelo.App` — the thin add-in shell that composes everything inside Navisworks.

## Adding a node — checklist

1. Check the [node catalog](docs/NODE_LIBRARY.md) for the agreed name, category, ports, and behavior. If your node is not in the catalog, propose it in an issue first.
2. Implement it zero-touch (static method + attributes) unless it genuinely needs interactive UI — see [docs/EXTENDING.md](docs/EXTENDING.md).
3. Follow the error convention: **throw** for real failures (engine surfaces Error state), **warn and return null** for recoverable issues. Never swallow exceptions silently, never crash the run.
4. General-purpose node (`Dyncamelo.Nodes`)? Add xunit tests in `tests/Dyncamelo.Nodes.Tests` — they must pass on Linux.
5. Navisworks node? Unit-test any pure logic you can extract; cover the rest via the manual smoke checklist.
6. Update `docs/NODE_LIBRARY.md` if ports/behavior deviate from the catalog (with reviewer agreement).

## Git workflow

- Branch from `main`; use descriptive branch names (`feature/list-groupbykey`, `fix/lacing-empty-list`).
- Keep commits focused; write imperative-mood commit messages ("Add List.GroupByKey node").
- Open a PR with: what/why, test evidence (CI plus smoke checklist result if applicable), and screenshots/GIFs for UI changes.
- One approving review is required. Maintainers squash-merge by default.

## Reporting security issues

Please do not open public issues for security-sensitive reports (e.g., anything enabling code execution through a `.dyc` file). Email the maintainers instead — addresses are listed on the GitHub organization profile.

## Code of conduct

Be kind, be constructive, assume good faith. Harassment or personal attacks are not tolerated; maintainers may remove content and ban repeat offenders.
