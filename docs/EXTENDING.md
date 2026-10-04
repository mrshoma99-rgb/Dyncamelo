# Extending CamelGraph — Write Your Own Nodes

CamelGraph is designed so that adding a node is a five-minute job: **a public static C# method with a couple of attributes is a node.** This guide walks through building a complete node pack, from an empty project to nodes showing up in the editor, and then covers the advanced path — interactive `NodeModel` nodes with custom WPF UI.

> Packs are loaded from a `Packages` folder next to `CamelGraph.App.dll`, the first time the editor or the Script Player opens in a session. A pack is code that runs inside Navisworks with your rights, so install packs only from authors you trust.

## Contents

1. [How node loading works](#1-how-node-loading-works)
2. [Tutorial: a zero-touch node pack](#2-tutorial-a-zero-touch-node-pack)
3. [Ports, defaults, and multiple outputs](#3-ports-defaults-and-multiple-outputs)
4. [Lists and replication — what your node sees](#4-lists-and-replication--what-your-node-sees)
5. [Errors and warnings](#5-errors-and-warnings)
6. [Navisworks node packs](#6-navisworks-node-packs)
7. [Custom interactive nodes (NodeModel + WPF view)](#7-custom-interactive-nodes-nodemodel--wpf-view)
8. [Conventions checklist](#8-conventions-checklist)
9. [Making a node look right in the editor](#9-making-a-node-look-right-in-the-editor)
10. [Changing a node that is already shipped](#10-changing-a-node-that-is-already-shipped)

---

## 1. How node loading works

At startup, CamelGraph's zero-touch loader (in `CamelGraph.Core`) reflects over node assemblies and registers every `public static` method of every public class (generic methods, property accessors and methods marked `[IsVisibleInLibrary(false)]` are skipped). `[NodeName]` sets the display name; without it the node is called `Class.Method`. Each parameter becomes an input port; the return value becomes the output port (or several, with `[MultiReturn]`). The built-in libraries (`CamelGraph.Nodes`, `CamelGraph.Navisworks`) are loaded this way — your pack uses exactly the same mechanism, so anything the built-in nodes can do, yours can too.

The loader also scans the `Packages` folder next to `CamelGraph.App.dll` (subfolders included):

```
%APPDATA%\Autodesk\ApplicationPlugins\CamelGraph.bundle\<year>\Packages\<YourPackName>\
    YourPack.dll            (plus any private dependencies)
```

Each pack folder is loaded in isolation: a pack that fails to load is reported in the editor and skipped — it can never take down CamelGraph or Navisworks.

## 2. Tutorial: a zero-touch node pack

### Step 1 — create the project

A general-purpose pack (no Navisworks API) targets `netstandard2.0` and references `CamelGraph.Core` only:

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
    <!-- During development, a project or DLL reference to CamelGraph.Core.
         (A CamelGraph.Core NuGet package is planned alongside the M5 package manager.) -->
    <Reference Include="CamelGraph.Core">
      <HintPath>path\to\CamelGraph.Core.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

`Private=false` matters: CamelGraph already provides `CamelGraph.Core` at runtime — your pack must not ship its own copy.

*A pack built for version 0.48 or earlier, when the product was called Dyncamelo, references `Dyncamelo.Core.dll` and the value types `DyncameloColor`, `DyncameloPoint`, …; it has to be built again against `CamelGraph.Core.dll` to load in newer versions.*

### Step 2 — write a node

```csharp
using CamelGraph.Core.Loader; // the attributes: [NodeName], [NodeCategory], [NodeDescription], [MultiReturn]…

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
%APPDATA%\Autodesk\ApplicationPlugins\CamelGraph.bundle\2024\Packages\RebarToolkit\RebarToolkit.dll
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

**Value types across nodes:** ports can carry any CLR type. Prefer the shared `CamelGraph.Core` value types (`Point`, `Vector`, `BoundingBox`, `Color`) where they fit so your nodes compose with the built-in library, and give custom types a meaningful `ToString()` so `Watch` shows something useful.

## 4. Lists and replication — what your node sees

You do **not** write loops. Declare the rank you actually need and the engine's replication does the rest ([ARCHITECTURE.md §4](ARCHITECTURE.md#4-replication-lacing)):

- `BarWeight(double, double, double)` fed a list of 500 diameters is invoked 500 times and yields a list of 500 weights. Lacing (Shortest/Longest/Cross-Product) governs how multiple lists pair up — the user controls that per node instance, your code never sees it.
- Take a `List<object>` parameter only when the node genuinely needs the whole list at once (aggregation, sorting, joining) — a list-typed port *absorbs* a list instead of mapping over it.
- Never mutate an input (lists included) — return new collections. Upstream cached values are shared; mutation corrupts other consumers.

## 5. Errors and warnings

The contract (see [ARCHITECTURE.md §9](ARCHITECTURE.md#9-error-handling-philosophy)):

- **Throw for real failures.** Any exception is caught by the engine and shown as that node's `Error` state with your message. Throw `ArgumentException` and friends with messages an end user can act on ("Bar mark must look like '16-B-250', got 'x'"). The run continues; Navisworks never crashes.
- **Warn and keep going for recoverable issues.** Return `null` (or a documented sentinel like `double.NaN`) for a missing/unparseable value and call `NodeWarnings.Add("…")` (namespace `CamelGraph.Core.Execution`) so the node shows the amber `Warning` badge with your sentence instead of a hard error. The node still delivers its result to the nodes after it.
  - Call it from inside the node method (or any helper it calls); the engine collects the messages **per call** of your method. The same text reported several times in one call is shown once with a count, and at most five different texts are listed.
  - Under replication the messages of all calls are summarised in one line, `3 of 40 calls: <first message>`, so a thousand bad elements cannot flood the badge.
  - Outside a run (a unit test that calls your method directly) `NodeWarnings.Add` does nothing and never throws, so a node stays testable on its own.
  - Write the message for the person at the keyboard: what was wrong and what the node did about it ("2 of 10 values were not numbers and were skipped"), not an exception dump.

```csharp
using CamelGraph.Core.Execution;

public static double SafeRatio(double part, double total)
{
    if (total == 0)
    {
        NodeWarnings.Add("The total is 0, so the ratio is 0.");
        return 0;
    }

    return part / total;
}
```
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
  <Reference Include="CamelGraph.Core" ... Private="false" />
</ItemGroup>
```

`ExcludeAssets="runtime"` is essential: you compile against the API surface, but at runtime your DLL binds the genuine Autodesk assemblies already loaded in the Navisworks process. Never copy Autodesk DLLs into your pack folder.

Rules for Navisworks nodes (the built-in library follows the same ones):

1. **Threading is solved for you** — nodes execute on the Navisworks main thread by construction ([plan §7](IMPLEMENTATION_PLAN.md#7-threading-model)). Do not spawn threads or use `Task.Run`/`async` inside a node.
2. Emit and accept **flat `List<ModelItem>`** so your nodes compose with search, sets, clash, and appearance nodes — it is the lingua franca of the Navisworks library.
3. Take a `Document` parameter (it defaults to the active document when unconnected) rather than reading `Application.ActiveDocument` mid-method — it keeps nodes testable and multi-doc-ready.
4. Mutate the document only through the documented `Document*` edit APIs (`DocumentClashTests`, `DocumentTimeliner`, `Document.Models.Override...`) so the Navisworks UI stays in sync and the host transaction scoping gives users one undo step per run.
5. Convert at the boundary: accept/return `CamelGraph.Core` geometry (`Point`, `BoundingBox`, `Color`) instead of `Point3D`/`BoundingBox3D`/`Api.Color`, so downstream pure nodes can consume your outputs.

## 7. Custom interactive nodes (NodeModel + WPF view)

Zero-touch covers everything that is "inputs in, outputs out". Subclass `NodeModel` only when a node needs state or UI of its own — inline editors (sliders), variable ports (`List.Create`), pass-through viewers (`Watch`), or OS dialogs (`File Path`). The built-in interactive nodes are implemented through exactly this seam, so it is a supported, stable extension point — not internals.

The shape of it (illustrative — the `CamelGraph.Core` XML docs are the normative API reference):

```csharp
using CamelGraph.Core.Graph;

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
    // See CamelGraph.Core docs for the exact override points.
}
```

Two halves, strictly separated:

- **The model** lives in your pack assembly (no WPF references) — ports, state, dirty-marking, evaluation, `.dyc` persistence of its `data` bag. Because it is UI-free it remains unit-testable on Linux like everything else.
- **The view** is a WPF `DataTemplate` keyed by your model type, supplied in a companion UI assembly loaded from the same pack folder. `CamelGraph.UI` resolves templates for node models it does not know from loaded packs; a model without a template still works — it renders with the default node chrome (ports and name), just without custom controls.

Keep custom UI minimal (a slider, a text box, a swatch). Anything heavier belongs in a dialog opened from the node, not on the canvas.

## 8. Conventions checklist

Before publishing a pack:

- [ ] Node names follow `Category.Verb`/`Category.Noun`; interactive nodes use friendly names. No collisions with [catalog](NODE_LIBRARY.md) names.
- [ ] Every node has `[NodeDescription]` and XML `<summary>` written for end users.
- [ ] Port names are lowercase-camel, short, and self-explanatory (`modelItems`, `path`, `ignoreCase`).
- [ ] Optional parameters used for sensible defaults; no boolean traps (name flags clearly: `includeSelf`, `overwrite`).
- [ ] Culture-invariant parsing/formatting throughout (`CultureInfo.InvariantCulture`).
- [ ] Inputs never mutated; collections returned fresh.
- [ ] A node that acts on something returns that thing (so the next node can be chained to it); a bare `done` flag is only for acts on the whole document.
- [ ] A `[MultiReturn]` node also declares `[PortKinds(...)]`, one kind per output, so its sockets are coloured before the graph has run (a test fails when it is missing).
- [ ] Errors thrown with actionable messages; recoverable issues warn + return null; no UI, no console, no threads.
- [ ] Pure logic covered by xunit tests (runnable on Linux).
- [ ] Pack folder contains only your DLLs (+ third-party MIT/Apache/BSD dependencies you are licensed to ship) — never `CamelGraph.*` or `Autodesk.*` assemblies.
- [ ] LICENSE file included in the pack folder; license shown in your README.

## 9. Making a node look right in the editor

The editor builds a node's rows from your method signature, so most nodes need nothing extra. A handful of optional attributes (all in `CamelGraph.Core.Loader`) tune how the rows look. They are **advisory**: none of them changes the node's definition id, so adding one to an existing parameter never breaks saved `.dyc` files.

| You write | The editor shows |
|---|---|
| `double width = 200` | A draggable number field with the default remembered; a dot marks it when changed. |
| `[NodeRange(0, 100, SoftMin = 0, SoftMax = 10, Step = 0.5, Unit = "mm")] double gap` | The field clamps to 0–100, its drag range is 0–10, it steps by 0.5 and prints `mm` after the value. |
| `[NodeChoices("Model", "Object", "Face")] string level` | A dropdown instead of a free text box — or a segmented switcher when there are two or three short values (24 characters in all). |
| `[NodeTabChoice("item")] string categoryName` and `[NodePropertyChoice("item", "categoryName")] string propertyName` | The text box stays (typing always works) and gets a small magnifier button. Pressed, it lists the property tabs — or the properties of the tab named by `categoryName` — of the element carried by the node's `item` input, in a drop-down. It reads only that element (a picked one, or what the wire delivered in the last run), only when pressed, and never searches the model; add `IncludeAncestors = true` when the node also looks at the element's parents. A node that searches the whole model has no element input; pass `NodeDataSource.Selection` as the source (`[NodeTabChoice(NodeDataSource.Selection)]`) and the button lists what the elements selected in the host right now carry. Only on `string` parameters; the host supplies the reader through `ModelPropertyHost.Current` (CamelGraph does for Navisworks). |
| `[NodePanel("Advanced")] double tolerance = 0.01` | The input sits in a foldable *Advanced* panel (`DefaultOpen = true` starts it expanded). |
| `[MultiInput] IEnumerable<ModelItem> items` (any list-typed parameter) | A **multi-input** pill: any number of wires connect to it. One wire arrives untouched — so adding the attribute to an existing parameter never changes a saved graph — and two or more arrive combined into one list, in the order the wires were made (list-valued wires contribute their elements, other values themselves, nulls nothing). Ignored on parameters that are not list-typed. |
| `[PortKinds("viewpoint*")]` on an `object` parameter, or `[PortKinds("text*", "integer")]` on a `[MultiReturn]` method | The socket takes the colour and shape of that kind: a family name (`number`, `integer`, `boolean`, `text`, `datetime`, `colour`, `geometry`, `item`, `selection`, `viewpoint`, `clash`, `document`, `data`, `file`, `action`), then `*` for a list or `**` for a list of lists. |
| `ModelItem`, `List<ModelItem>` or `ModelItemCollection` parameter | A **model-element picker**: *Use selection* takes the current Navisworks selection, clicking the value re-selects it, ✕ clears it. |
| `bool`, `Color`, `DateTime`, enums | A checkbox, a colour swatch, a text field holding an ISO date, and a dropdown (or segmented switcher) of the enum's names. |
| a `string` parameter whose name ends in `path`, `file`, `filename`, `folder` or `directory` | A file field with a `…` button; names ending in `folder`, `directory` or `dir` open a folder chooser instead. |

Guidelines:

- Give every number a `[NodeRange]` when a sensible range exists — it turns a blind text box into a slider-like field and stops absurd values.
- Prefer `[NodeChoices]` to documenting "one of A, B, C" in the description.
- A node that takes an element plus the *name* of one of its tabs or properties should mark those parameters with `[NodeTabChoice]` / `[NodePropertyChoice]`, pointing at the element input; a node that searches the whole model should not (there is no element to read).
- Keep the *main* inputs unpaneled and move rare options into one `[NodePanel("Advanced")]`; the node stays short and the panel is one click away.
- Use `[PortKinds]` whenever you return `object` from a `[MultiReturn]` method, so the wires downstream are coloured correctly and the editor can filter the node search when a wire is dropped on the canvas.
- Mark a list parameter `[MultiInput]` when a caller would reasonably want to feed it from several places — "these items, and those, and the current selection". Do not use it on a list whose *nesting* matters (a list of lists that should replicate the node once per sublist): with several wires the outer level is concatenated, so each wire's sublists merge into one list of sublists.
- In a hand-written `NodeModel`, declare the port with `AddMultiInput(name, typeof(IList<object>))` in the constructor.
- Every attribute is listed with its editor result in the [editor guide](UI_GUIDE.md#anatomy-of-a-node).

### Attributes that change how a node runs

Some attributes say nothing about how the node looks; they tell the engine how to treat a port or the whole node. They are advisory in the same sense as the ones above: none of them changes the definition id, so adding one to a shipped node never breaks a saved graph.

- **`[AcceptsNull]` on a parameter** — by default a `null` element of a list the node is mapped over never reaches the node: that position gets a `null` result and the node shows one warning ("1 of 3 laced calls received a null element"). Mark the parameter when the node's job is to answer the empty case itself — a test for "is this blank?", a join that treats a missing cell as empty text. The null is then passed to the method and its answer is used. Only the marked parameter changes; a single call with a null, and every other parameter, behave as before. The parameter must be able to hold null (a reference or nullable type): on a plain `double` the engine still says "Null value passed to input".

```csharp
// ["a", null, ""] gives [false, true, true]; without [AcceptsNull] it gave [false, null, true] plus a warning.
public static bool IsBlank([AcceptsNull] string text) => string.IsNullOrWhiteSpace(text);
```

## 10. Changing a node that is already shipped

Saved graphs are the contract. A `.dyc` file refers to a zero-touch node by its **definition id** — `Namespace.Class.Method@parameterTypes` — and stores each wire and each typed-in value by the **port name** (the parameter name, the `[MultiReturn]` key, or the return name). What you may change:

| Change | Safe? | What to do |
|---|---|---|
| Display name (`[NodeName]`), category, description, search tags | Yes | Nothing: none of them is part of the id. |
| Add, remove or retype a parameter (this changes the id) | With an alias | Put the old id on the method: `[NodeAliases("Ns.Class.Method@double,double")]`. Old files resolve to the new method and write the new id when saved. |
| Rename an input or an output | With an alias | `[PortAlias("oldName", "newName")]` on the method, once per renamed port. A wire or value saved under the old name finds the port. |
| Retire a node | With a replacement | Mark it `[NodeDeprecated("Use Category.Replacement")]` and have it call the replacement. It stays registered — old graphs load and run — but is left out of the library, the quick search, the CLI list and the catalogue, and its description says what to use instead. Never delete a shipped node. |

Without an alias, a wire or value whose port no longer exists is dropped when the graph opens; the editor then says so in the status bar ("*N connections or values could not be restored*") instead of losing it silently.

After adding, renaming or retiring a node, run `python3 tools/generate_node_catalog.py` and commit `docs/camelgraph-nodes.json` and `docs/NODE_CATALOG.md`; CI fails when they are out of date.
