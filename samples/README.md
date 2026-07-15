# Dyncamelo sample graphs

Two families of `.dyc` graphs live here:

- **Developer samples** (lower-case file names) — four small graphs that
  exercise the headless pipeline end to end. None of them need Navisworks or
  WPF; they run anywhere the `dyncamelo` CLI runs, including Linux and CI.
- **In-app example workflows** (Title Case file names) — nine teaching graphs
  that ship with the Navisworks plugin (the build stages every Title-Case
  graph — the four lower-case developer graphs are excluded — into a
  `Samples` folder next to the plugin DLL, where the UI's Samples menu finds
  them). Except for *Getting Started - Math and Watch*, they use
  `Dyncamelo.Navisworks` nodes and need a running Navisworks with a model
  open — on other machines they load as placeholder nodes.

Run any of the pure-general graphs from the repository root:

```bash
dotnet run --project src/Dyncamelo.Cli -- run samples/hello-math.dyc
dotnet run --project src/Dyncamelo.Cli -- validate samples/hello-math.dyc
dotnet run --project src/Dyncamelo.Cli -- list-nodes
```

The CLI exits with `0` when no node ended in the Error state, `1` when at
least one did, and `2` for unreadable inputs — so the samples double as CI
smoke tests. `tests/Dyncamelo.Integration.Tests` loads and runs every
runnable `.dyc` file in this directory on every test run, and statically
validates the Navisworks-dependent ones (every zero-touch definition id must
exist in the node libraries and every connector must reference real ports),
which pins the on-disk format against accidental drift.

## In-app example workflows

### Getting Started - Math and Watch.dyc

Two sliders multiplied into an area, shown in a Watch node and formatted into
a text report (`String.FromObject` + `String.Concat`). Pure general nodes, so
it also runs headless; expected Watch values: **32** and **`Area = 32`**.

### Color Elements by Property.dyc

`Search.ByPropertyContains → Appearance.OverrideColor` with a Color Picker.
Finds every item whose property text contains a search string (default:
Item ▸ Name contains "Wall") and paints it; `List.Count` reports how many.

### Export Properties to Excel.dyc

`Search.ByPropertyContains → Properties.ValueAsString × 3 → Excel.WriteToFile`.
Item names become the header row; each picked property becomes one row of the
worksheet (the engine's replication turns the single-item property node into
a per-item list).

### Bulk Selection Sets from Values.dyc

`SelectionSets.BulkByPropertyValues` — one live search set per distinct value
of a property (default: Element ▸ Level), filed under a folder in the Sets
window.

### Clash Triage and BCF Export.dyc

`Clash.Tests → List.GetItemAtIndex → Clash.GroupResultsByStatus →
ClashTest.Results → BCF.ExportIssues`, plus `Export.ClashReportCsv` for a
summary CSV of every test.

### QTO Rollup by Category.dyc

`Search.HasProperty → Takeoff.SumPropertyByGroup → Excel.WriteToFile`: sums a
numeric property (default: Element ▸ Volume) grouped by another property
(default: Element ▸ Category) and writes the rollup to a workbook.

### Floor Opening Fall-Hazard Map.dyc

A whole-floor safety sweep. `FallHazard.FloorOpeningMap` slices the model at a
chosen level, reads the **real floor and equipment mesh** (via the COM geometry
bridge, so it sees true holes cut inside a slab), rasterises them onto a plan
grid, isolates the openings that are fully enclosed by floor, and grades each by
how far its centre sits from the nearest edge. It writes a top-down **heat-map
PNG** (hot = the middle of a big opening, the worst fall hazard) and drops one
**saved viewpoint** per opening whose widest gap ≥ your threshold, into a "Floor
Openings" folder. Point the two searches at your floor and equipment elements,
set the level and the trigger gap, and run. Needs a live Navisworks session; the
`Cell size` input trades accuracy for speed. This is the grid/heat-map approach —
like a sunlight study, but for fall hazards.

### Floor Openings Needing Handrails.dyc

A two-part QA graph for slab openings that equipment (a boiler, duct or pipe)
drops through, leaving a gap that may need a handrail. **Part 1** takes the
current selection as the equipment, `Search.ByPropertyContains` finds the
handrails, and `Proximity.NearestDistance` measures each item to its nearest
handrail; `GreaterThan` a trigger distance flags the equipment with none close
by. **Part 2** measures the gap itself with `BoundingBox.PlanGap`, which
compares the opening's footprint (outer) to the equipment's footprint (inner)
and returns the **widest** of the four side gaps — the biggest open strip, which
is the actual fall hazard, not the nearest edge. Part 2 is self-contained (fixed
boxes, reports `0.8`); swap in `ModelItem.BoundingBox` of the opening and the
equipment to run it on a real model — the Navisworks box converts to a geometry
box automatically.

### Isolated Viewpoints per Item.dyc

The per-item loop, built from **reified actions**. `Search.ByPropertyContains`
finds a set of items, and a `List.Create` gathers an ordered action recipe
(`Action.Isolate → Action.ZoomTo → Action.SaveViewpoint`) that
`Workflow.ForEach` replays once for every found item. The result is one saved
viewpoint per element, each framed on its own item (the save captures the live
camera, not the origin) and titled from the item name via the `{name}`
template. Nothing is hard-coded to a specific element — point it at any search
and it fans the same recipe across the whole result set.

### Spotlight Viewpoints per Item.dyc

The same `Workflow.ForEach` loop, dressed for presentation. For each item the
recipe first `Action.ResetTemporaryAppearance`, then `Action.Ghost`s the whole
model to a faint grey, `Action.Highlight`s the current item in a picked colour,
`Action.ZoomTo`s it and `Action.SaveViewpoint`. Every viewpoint is a spotlight —
the item in full colour against a ghosted context — and because these are
*temporary* overrides captured into the viewpoint, each saved view keeps its own
appearance without disturbing the others.

### Isolated Viewpoints (Loop).dyc

The **universal loop region** — the same isolate-zoom-save outcome as *Isolated
Viewpoints per Item*, but built from ordinary nodes instead of packaged actions.
`Loop.Item` and `Loop.Collect` bracket a body of real nodes
(`Appearance.Isolate → Camera.ZoomToItems → Viewpoint.SaveWithOverrides`, with
`ModelItem.DisplayName` naming each view); the engine re-runs that body once per
item and `Loop.Collect` gathers the results. This is the general mechanism: any
subgraph placed between the two boundaries becomes a per-item loop, so you are
never limited to the pre-built action set.

## Developer graphs

### hello-math.dyc

`Number Slider (12.5) + Number (30) → Add → Multiply (× 2) → Math.Round → Watch`

The classic first graph: interactive inputs flowing through zero-touch math
nodes into a Watch node. The Round node's `digits` input is left unconnected,
so it uses its default value (`0`) — a defaulted zero-touch port in action.
Expected Watch value: **85**.

### list-lacing.dyc

`List.Range` produces `[1, 2, 3]` and `[10, 20]`, which feed three `Add`
nodes whose ports are scalar (`double`) — so the engine replicates ("laces")
the node over the lists, once per lacing mode:

| Node                | Lacing        | Result                             |
| ------------------- | ------------- | ---------------------------------- |
| Add (Shortest)      | Shortest      | `[11, 22]`                         |
| Add (Longest)       | Longest       | `[11, 22, 23]` (last item repeats) |
| Add (Cross Product) | Cross-Product | `[[11, 21], [12, 22], [13, 23]]`   |

Each result lands in its own Watch List node.

### string-report.dyc

Splits a sentence into words (`String.Split`), counts them (`List.Count`) and
formats a small report (`String.FromObject` + `String.Concat`), while a second
branch joins the words back together with `String.Join`. Expected Watch
values: **`Word count: 4`** and
**`dyncamelo, makes, navisworks, programmable`**.

### csv-roundtrip.dyc

Builds a 2×3 numeric table (`List.Range` × 2 → `List.Create`), writes it with
`CSV.WriteToFile` and immediately reads it back with `CSV.ReadFromFile` into a
Watch List. Two details worth copying into your own graphs:

- The output path is **relative** (`dyncamelo-sample-output.csv`), so the file
  is created in whatever directory you run the CLI from.
- The write node's `path` **output** is wired into the read node's `path`
  **input** — that both supplies the path and forces the read to execute after
  the write (dataflow sequencing of side effects).

Because it touches the file system, this graph is saved with
`RunType: Manual`, so a UI host will not re-run it automatically on every edit.

## Regenerating the samples

The developer graphs are authored in code
(`src/Dyncamelo.Cli/SampleGraphs.cs`), not by hand-editing JSON. After a
format or node-library change, regenerate them with:

```bash
dotnet run --project src/Dyncamelo.Cli -- write-samples samples
```

`write-samples` only rewrites the four developer graphs; the in-app example
workflows are maintained as files in this directory (their zero-touch
definition ids and port names are pinned by
`tests/Dyncamelo.Integration.Tests/SampleGraphStaticValidationTests.cs`).
