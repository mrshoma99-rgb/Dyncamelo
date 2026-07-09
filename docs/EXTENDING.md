# Extending Dyncamelo — Write Your Own Nodes

Dyncamelo is designed so that adding a node is a five-minute job: **a public static C# method with a couple of attributes is a node.** This guide walks through building a complete node pack, from an empty project to nodes showing up in the editor, and then covers the advanced path — interactive `NodeModel` nodes with custom WPF UI.

> Availability note: zero-touch authoring works from v0.1 for libraries compiled into Dyncamelo. **Loading third-party packs from the Packages folder ships in v0.2 (milestone M3)** — see the [roadmap](IMPLEMENTATION_PLAN.md#3-milestone-roadmap). The authoring model described here is identical in both cases, so packs written today drop in unchanged.

## Contents

1. [How node loading works](#1-how-node-loading-works)
2. [Tutorial: a zero-touch node pack](#2-tutorial-a-zero-touch-node-pack)
3. [Ports, defaults, and multiple outputs](#3-ports-defaults-and-multiple-outputs)
4. [Lists and replication — what your node sees](#4-lists-and-replication--what-your-node-sees)
5. [Errors and warnings](#5-errors-and-warnings)
6. [Navisworks node packs](#6-navisworks-node-packs)
7. [Custom interactive nodes (NodeModel + WPF view)](#7-custom-interactive-nodes-nodemodel--wpf-view)
8. [Conventions checklist](#8-conventions-checklist)

---

## 1. How node loading works

At startup, Dyncamelo's zero-touch loader (in `Dyncamelo.Core`) reflects over node assemblies and registers every `public static` method that carries a `[NodeName]` attribute. Each parameter becomes an input port; the return value becomes the output port (or several, with `[MultiReturn]`). The built-in libraries (`Dyncamelo.Nodes`, `Dyncamelo.Navisworks`) are loaded this way — your pack uses exactly the same mechanism, so anything the built-in nodes can do, yours can too.

From v0.2, the loader also scans:

```
%APPDATA%\Dyncamelo\Packages\<YourPackName>\
    YourPack.dll            (plus any private dependencies)
```

Each pack folder is loaded in isolation: a pack that fails to load is reported in the editor and skipped — it can never take down Dyncamelo or Navisworks.

## 2. Tutorial: a zero-touch node pack

### Step 1 — create the project

A general-purpose pack (no Navisworks API) targets `netstandard2.0` and references `Dyncamelo.Core` only:

```xml
<!-- RebarToolkit.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>10</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>RebarToolkit</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <!-- During development, a project or DLL reference to Dyncamelo.Core.
         (A Dyncamelo.Core NuGet package is planned alongside the M5 package manager.) -->
    <Reference Include="Dyncamelo.Core">
      <HintPath>path\to\Dyncamelo.Core.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

`Private=false` matters: Dyncamelo already provides `Dyncamelo.Core` at runtime — your pack must not ship its own copy.

### Step 2 — write a node

```csharp
using Dyncamelo.Core.Nodes; // attribute namespace — see Dyncamelo.Core XML docs

namespace RebarToolkit;

/// <summary>Rebar quantity helpers.</summary>
public static class Rebar
{
    /// <summary>Weight in kilograms of a straight rebar.</summary>
    [NodeName("Rebar.BarWeight")]
    [NodeCategory("RebarToolkit.Rebar")]
    [NodeDescription("Weight in kg of a straight rebar from its diameter (mm) and length (m).")]
    public static double BarWeight(double diameterMm, double lengthM, double density = 7850)
    {
        double areaM2 = System.Math.PI * System.Math.Pow(diameterMm / 2000.0, 2);
        return areaM2 * lengthM * density;
    }
}
```

That is the entire node. What the attributes do:

| Attribute | Effect |
|---|---|
| `[NodeName("Rebar.BarWeight")]` | The node's display name and search key. Follow the `Category.Verb`/`Category.Noun` convention — see the [catalog](NODE_LIBRARY.md#design-conventions) |
| `[NodeCategory("RebarToolkit.Rebar")]` | Position in the library tree (dots nest) |
| `[NodeDescription("...")]` | Tooltip/help text shown to users — write it for end users |
| *(method signature)* | `diameterMm`, `lengthM` become required input ports; `density` becomes a defaulted port (unconnected = 7850); the return value becomes the output port |

### Step 3 — test it

Your pack is plain .NET — test it with xunit on any OS, no Navisworks needed:

```csharp
[Fact]
public void BarWeight_D16_1m_IsAboutOnePoint58Kg()
    => Assert.Equal(1.58, Rebar.BarWeight(16, 1.0), 2);
```

### Step 4 — install it

Copy the build output to the Packages folder and restart the editor (or use the library's refresh action):

```
%APPDATA%\Dyncamelo\Packages\RebarToolkit\RebarToolkit.dll
```

Your nodes appear under *RebarToolkit → Rebar* in the node browser, with your descriptions as tooltips. Done.

## 3. Ports, defaults, and multiple outputs

**Input ports** come from parameters — name, advisory type, and rank are all inferred:

- `double`, `int`, `string`, `bool`, `DateTime`, your own classes → scalar (rank 0) ports.
- `List<T>` / `IList<T>` / `IEnumerable<T>` → list (rank 1) ports; nested lists → rank 2.
- Optional parameters (`double density = 7850`) → **defaulted ports**: usable unconnected, overridable by wire.
- `object` accepts anything (coercion off — you receive the raw value).

**Multiple outputs** use `[MultiReturn]` with a `Dictionary<string, object>` return; each key becomes an output port:

```csharp
/// <summary>Splits a full bar mark like "16-B-250" into its parts.</summary>
[NodeName("Rebar.ParseBarMark")]
[NodeCategory("RebarToolkit.Rebar")]
[NodeDescription("Splits a bar mark (e.g. \"16-B-250\") into diameter, grade and spacing.")]
[MultiReturn("diameter", "grade", "spacing")]
public static Dictionary<string, object> ParseBarMark(string barMark)
{
    string[] parts = barMark.Split('-');
    return new Dictionary<string, object>
    {
        ["diameter"] = double.Parse(parts[0], CultureInfo.InvariantCulture),
        ["grade"] = parts[1],
        ["spacing"] = double.Parse(parts[2], CultureInfo.InvariantCulture),
    };
}
```

**Value types across nodes:** ports can carry any CLR type. Prefer the shared `Dyncamelo.Core` value types (`Point`, `Vector`, `BoundingBox`, `Color`) where they fit so your nodes compose with the built-in library, and give custom types a meaningful `ToString()` so `Watch` shows something useful.

## 4. Lists and replication — what your node sees

You do **not** write loops. Declare the rank you actually need and the engine's replication does the rest ([ARCHITECTURE.md §4](ARCHITECTURE.md#4-replication-lacing)):

- `BarWeight(double, double, double)` fed a list of 500 diameters is invoked 500 times and yields a list of 500 weights. Lacing (Shortest/Longest/Cross-Product) governs how multiple lists pair up — the user controls that per node instance, your code never sees it.
- Take a `List<object>` parameter only when the node genuinely needs the whole list at once (aggregation, sorting, joining) — a list-typed port *absorbs* a list instead of mapping over it.
- Never mutate an input (lists included) — return new collections. Upstream cached values are shared; mutation corrupts other consumers.

## 5. Errors and warnings

The contract (see [ARCHITECTURE.md §9](ARCHITECTURE.md#9-error-handling-philosophy)):

- **Throw for real failures.** Any exception is caught by the engine and shown as that node's `Error` state with your message. Throw `ArgumentException` and friends with messages an end user can act on ("Bar mark must look like '16-B-250', got 'x'"). The run continues; Navisworks never crashes.
- **Warn and keep going for recoverable issues.** Return `null` (or a documented sentinel like `double.NaN`) for a missing/unparseable value; `Dyncamelo.Core` provides a warning-reporting mechanism for zero-touch nodes so the node shows a yellow `Warning` badge instead of a hard error — see the `Dyncamelo.Core` XML documentation for the exact API. Under replication, warnings aggregate rather than spam.
- **Never** show message boxes, write to the console, or swallow exceptions silently from library nodes.

## 6. Navisworks node packs

A pack that talks to the Navisworks API targets **net48** and adds the same compile-time-only references the built-in library uses:

```xml
<PropertyGroup>
  <TargetFramework>net48</TargetFramework>
  <LangVersion>10</LangVersion>
  <Nullable>enable</Nullable>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Speckle.Navisworks.API" Version="2024.0.0" ExcludeAssets="runtime" />
  <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
  <Reference Include="Dyncamelo.Core" ... Private="false" />
</ItemGroup>
```

`ExcludeAssets="runtime"` is essential: you compile against the API surface, but at runtime your DLL binds the genuine Autodesk assemblies already loaded in the Navisworks process. Never copy Autodesk DLLs into your pack folder.

Rules for Navisworks nodes (the built-in library follows the same ones):

1. **Threading is solved for you** — nodes execute on the Navisworks main thread by construction ([plan §7](IMPLEMENTATION_PLAN.md#7-threading-model)). Do not spawn threads or use `Task.Run`/`async` inside a node.
2. Emit and accept **flat `List<ModelItem>`** so your nodes compose with search, sets, clash, and appearance nodes — it is the lingua franca of the Navisworks library.
3. Take a `Document` parameter (it defaults to the active document when unconnected) rather than reading `Application.ActiveDocument` mid-method — it keeps nodes testable and multi-doc-ready.
4. Mutate the document only through the documented `Document*` edit APIs (`DocumentClashTests`, `DocumentTimeliner`, `Document.Models.Override...`) so the Navisworks UI stays in sync and the host transaction scoping gives users one undo step per run.
5. Convert at the boundary: accept/return `Dyncamelo.Core` geometry (`Point`, `BoundingBox`, `Color`) instead of `Point3D`/`BoundingBox3D`/`Api.Color`, so downstream pure nodes can consume your outputs.

## 7. Custom interactive nodes (NodeModel + WPF view)

Zero-touch covers everything that is "inputs in, outputs out". Subclass `NodeModel` only when a node needs state or UI of its own — inline editors (sliders), variable ports (`List.Create`), pass-through viewers (`Watch`), or OS dialogs (`File Path`). The built-in interactive nodes are implemented through exactly this seam, so it is a supported, stable extension point — not internals.

The shape of it (illustrative — the `Dyncamelo.Core` XML docs are the normative API reference):

```csharp
using Dyncamelo.Core.Graph;

namespace RebarToolkit;

/// <summary>Slider that snaps to standard rebar diameters (8, 10, 12, 16, 20, 25, 32 mm).</summary>
public class RebarDiameterSlider : NodeModel
{
    private static readonly double[] Standard = { 8, 10, 12, 16, 20, 25, 32 };
    private double _diameter = 16;

    public RebarDiameterSlider()
    {
        Name = "Rebar Diameter";
        Category = "RebarToolkit.Rebar";
        AddOutPort("diameter", typeof(double));
    }

    /// <summary>The selected diameter in millimetres. Setting it marks the node dirty.</summary>
    public double Diameter
    {
        get { return _diameter; }
        set { _diameter = SnapToStandard(value); MarkDirty(); }
    }

    // Evaluation: publish the current value to the out-port.
    // Persistence: the node's state (Diameter) round-trips through the .dyc "data" bag.
    // See Dyncamelo.Core docs for the exact override points.
}
```

Two halves, strictly separated:

- **The model** lives in your pack assembly (no WPF references) — ports, state, dirty-marking, evaluation, `.dyc` persistence of its `data` bag. Because it is UI-free it remains unit-testable on Linux like everything else.
- **The view** is a WPF `DataTemplate` keyed by your model type, supplied in a companion UI assembly loaded from the same pack folder. `Dyncamelo.UI` resolves templates for node models it does not know from loaded packs; a model without a template still works — it renders with the default node chrome (ports and name), just without custom controls.

Keep custom UI minimal (a slider, a text box, a swatch). Anything heavier belongs in a dialog opened from the node, not on the canvas.

## 8. Conventions checklist

Before publishing a pack:

- [ ] Node names follow `Category.Verb`/`Category.Noun`; interactive nodes use friendly names. No collisions with [catalog](NODE_LIBRARY.md) names.
- [ ] Every node has `[NodeDescription]` and XML `<summary>` written for end users.
- [ ] Port names are lowercase-camel, short, and self-explanatory (`modelItems`, `path`, `ignoreCase`).
- [ ] Optional parameters used for sensible defaults; no boolean traps (name flags clearly: `includeSelf`, `overwrite`).
- [ ] Culture-invariant parsing/formatting throughout (`CultureInfo.InvariantCulture`).
- [ ] Inputs never mutated; collections returned fresh.
- [ ] Errors thrown with actionable messages; recoverable issues warn + return null; no UI, no console, no threads.
- [ ] Pure logic covered by xunit tests (runnable on Linux).
- [ ] Pack folder contains only your DLLs (+ third-party MIT/Apache/BSD dependencies you are licensed to ship) — never `Dyncamelo.*` or `Autodesk.*` assemblies.
- [ ] LICENSE file included in the pack folder; license shown in your README.
