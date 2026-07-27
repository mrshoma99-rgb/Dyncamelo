# What's new in Dyncamelo v0.12 – v0.23 — the "site safety & spatial analysis" wave

Eleven minor releases in one arc, driven directly by field use on federated site
models: a full fall-hazard analysis suite, spatial clustering that turns loose
geometry into numbered elements, viewpoint intelligence (what does this view
actually show?), experimental redline markups, an execution-ordering story
aligned with Dynamo/Grasshopper, and a long list of editor quality-of-life
upgrades. The node library now counts **314 nodes across 37 categories**, every
port documented.

## Safety analysis (Navisworks.Analysis)

- **`FallHazard.FloorOpeningMap`** (v0.12) — whole-floor fall-hazard heat map:
  slices the model at a level, rasterises the real floor/equipment mesh (COM
  triangle read), finds the openings enclosed by floor, and grades each by its
  widest clear span. Exports a top-down PNG heat map and one saved viewpoint
  per flagged opening. The gradient pivots on the clearance limit, the two
  gradient colours are user-pickable, and `showOverage` prints each opening's
  gap-over-limit right on the plan (v0.17) — report-ready.
- **`FallHazard.EdgeHandrailCheck`** (v0.13–v0.16) — classifies every floor
  edge around a void as **dangerous / protected / safe**: an edge is safe when
  the gap in front of it is under the limit *or* a handrail (user-selected
  elements, projected onto the plan with real length-along-edge coverage)
  runs along it. A `minPassage` rule (v0.15) marks dangerous runs too short
  for a person to pass as safe, corner cells are classified correctly, edge
  colours are user-pickable and dangerous runs can print their
  gap-over-limit on the plan (v0.16).
- **`units` input** (v0.14) on both analysis nodes — Navisworks documents
  often store feet internally even when the measure tool shows metres; name
  the unit your numbers are in ("Meters", "Feet", …) and inputs, outputs and
  the printed report all stay honest. The same input now ships on
  `Proximity.Cluster`.
- **Diagnostic `report` outputs** — every analysis node returns a
  human-readable report (grid size, world Z range, triangle counts, units,
  plugin version) so odd results explain themselves.

## Spatial clustering

- **`Proximity.Cluster`** (v0.22) — groups items whose geometry touches
  (gap ≤ tolerance, directly or through a chain of neighbours) into logical
  elements: think "a ladder made of loose shapes with no ladder element".
  Returns the groups, 1-based cluster numbers aligned with the input, sizes
  and a report — and can stamp each item's number as a searchable custom
  property (`propertyName`) in the same run. `method = "mesh"` upgrades the
  box test to a candidate prefilter confirmed by the Clash engine's exact
  surface-to-surface clearance, so fat boxes (diagonal members) can no longer
  bridge two separate elements.

## Viewpoint intelligence & markups

- **`Viewpoint.VisibleItems`** (v0.20) — does this viewpoint show these
  elements? Splits candidates into inside/outside the camera frustum
  (perspective and orthographic), with a boolean mask and `containsAny`.
  Inputs are flexible: item lists, a selection/search set, a set name, or a
  single item; the viewpoint accepts a saved viewpoint, its name, or empty
  for the current view.
- **`Markup.*`** (v0.21, EXPERIMENTAL) — seven nodes that draw redlines on
  saved viewpoints through the hidden-but-public Navisworks redline API:
  `AddText`, `AddLine`, `AddArrow`, `AddEllipse`, `AddCloud` (revision
  cloud), `AddNumberTag` (circled number + optional linked comment — a tag
  substitute; real Find-Tags tags have no public API) plus `List` (read
  back / calibrate coordinates) and `Clear`. Undocumented API surface —
  flagged experimental.
- **Viewpoint organizing** (v0.11) — `Viewpoints.SortFolder`,
  `SavedViewpoint.Duplicate`, `Viewpoints.DuplicateFolder`,
  `Viewpoints.RenameFolder`, `SavedViewpoint.CopyOverrides`, XML
  export/import.

## Colors

- **`Color.Random(seed)`** and **`Color.RandomList(count, seed)`** (v0.23) —
  pseudo-random colors that are stable per seed (re-runs never repaint
  everything); the list variant walks golden-angle hues so N groups stay
  visually distinct.
- **`Color.Gradient(count, start, end)`** (v0.23) — N colors evenly blended
  between two colors, endpoints included.
- **`Color.ByValues(values, colors?)`** (v0.23) — one color per value with
  equal values sharing a color, plus a uniqueValues/uniqueColors legend;
  pairs with **`Appearance.ColorByValues`** for one-node color-coding by
  parameter value with a report legend.

## Execution ordering, done the industry way

- **`Flow.Then(value, after, …)`** (v0.19) — Dyncamelo's equivalent of the
  Dynamo Passthrough pattern: passes a value through unchanged *after* the
  wired nodes have run, turning "save the viewpoint AFTER the section box is
  applied" into a real data dependency. The order of unwired side-effect
  nodes is deliberately unspecified (stable, but not a contract) — the same
  policy as Dynamo and Grasshopper; the loop sample teaches the rule.
- **Captured Selection** (v0.13) — a node that *snapshots* the current
  Navisworks selection with Capture/Clear buttons and replays it on every
  run, unlike `Selection.Current` which always reads the live selection.

## Editor quality of life (v0.18)

- **Space-bar quick search** — press Space over the canvas: type to filter
  (same ranked index as the library), ↑/↓ to choose, Enter inserts the node
  where your cursor was.
- **Port tooltips that explain what to connect** — the loader reads the
  compiler XML documentation shipped beside every node pack, so all 314
  nodes' inputs and outputs carry real descriptions.
- **Watch Image** — wire an image path (the heat maps, exports) and the
  picture renders inline on the canvas, resizable, reloading when a run
  overwrites the file.
- **Watch List index gutter** — a real index column beside each entry,
  virtualized for huge lists.
- **Expandable preview bubbles** — when a preview truncates a list
  ("… N more"), click it for the full scrollable list; click again to
  collapse.
- **Boolean switch** — the Boolean input is a proper sliding toggle with a
  True/False label.
- **Library classification** (v0.13) — every node, in every category, is
  grouped **Create / Modify / Info** with coloured symbols, driven by a
  category-aware classifier; helper classes no longer leak into the library.

## Ribbon, About & updates (v0.19, toolset)

- One **BIMCamel ribbon tab** shared with the IFC exporter (duplicate-tab
  merge hardened), a unified About window with working buttons, and an
  update check when the editor opens.

## Distribution

- The website's download buttons now serve the official GitHub release
  directly (`releases/latest/download/…`), so every download is the newest
  version with a stable URL and checksum; the release publishes stable-named
  assets for the zip, the installer and both checksums (v0.17.1).

## Under the hood

- 585 headless tests green on Linux CI (pure cores for the raster analysis,
  frustum, clustering and colors are Navisworks-free by design).
- Saved-graph compatibility: every signature change ships a `[NodeAliases]`
  legacy id, so graphs from earlier versions keep opening unchanged.
