# What's new in Dyncamelo v0.4 — the "user feedback" wave

v0.4 is built directly from field reports of the first production users: everything in this release answers a concrete piece of feedback from running Dyncamelo inside Navisworks 2024 on real federated models. The theme is *day-to-day usability* — a library you can search without stutter, a canvas that looks right, samples you can open from the menu, and pickers that resolve to the selection-tree level you actually meant.

## Editor & library UX

- **Instant library search** — searching the node library used to stall the UI on every keystroke (the full tree was rebuilt and re-scanned each time). Search is now index-backed and debounced (200 ms): each entry carries a prebuilt lowercase index over name, category, tags and description; multi-word queries AND-match and results come back as a **flat, relevance-ranked list** (name-prefix hits first), capped at 200 with a "Showing 200 of N matches — refine your search" footer. Clearing the search restores the tree exactly as you left it — expansion state is never touched while searching. Both the tree and the results list are UI-virtualized for large libraries.
- **No more stray blue rectangles** — Nodify's default node container drew a DodgerBlue border around *every* node on the canvas. Node containers are now borderless at rest; actually **selected** nodes get a deliberately subtle accent outline instead, and warning/error borders are unchanged.
- **Sample graphs in the Open menu** — the Open ▾ dropdown is now split into **Recent Files** and **Sample Graphs**. The Sample Graphs submenu lists every `.dyc` shipped in the plugin's `Samples` folder (10 graphs in this release), refreshed on every open of the menu; picking one prompts before discarding an unsaved canvas. Empty submenus show up disabled instead of empty.
- **Library selection is no longer sticky** — the highlighted library entry used to stay highlighted forever. It now clears when the library loses keyboard focus, when you click anywhere on the canvas, or on Esc (in the library, the first Esc clears the search text, the second clears the selection). Selection colors were also recolored to match the dark theme instead of the Windows default blue.
- **Node descriptions in the library** — every library entry can now show its description as a second, dimmed line under the node name (wrapped, up to two lines), so you can tell `Search.ByPropertyValue` from `Search.ByPropertyContains` without hovering. Toggle it with the **Desc** button in the library header; the preference persists across sessions (`showLibraryDescriptions` in `ui-settings.json`, default on). Tooltips are unchanged and now appear faster.
- **Find in Library** — right-click any node on the canvas → **Find in Library** jumps the library to that node's entry: search is cleared, the category chain expands, and the entry scrolls into view and selects (virtualization-safe). Nodes that no longer resolve to a library entry degrade to a status-bar message.

## New Navisworks nodes & inputs

- **`Selection.Resolve`** *(Navisworks.Selection)* — re-selects items at another level of the selection tree, exactly like changing *Options > Interface > Selection > Resolution* and re-picking. Levels: `Self` (pass-through), `File`, `Layer`, `FirstObject`, `LastObject` (Navisworks' default resolution), `LastUnique`, and `Geometry` (expands *down* to the geometry leaves). Level names are case-insensitive and forgiving about spaces/hyphens/underscores; unknown levels fail fast and list the valid names. Output is deduplicated with input order preserved.
- **`resolveTo` input on the pickers** — the seven `Search.*` picker nodes (`ByPropertyValue`, `ByPropertyContains`, `ByPropertyWildcard`, `ByPropertyCompare`, `HasProperty`, `HasCategory`, `InItems`) and `Selection.Current` gained an optional `resolveTo: string = "Self"` input (just before `document`) that applies the same resolution to their results in one step — e.g. search on a Revit parameter but color/report the *whole objects* (`LastObject`) or the owning *files* (`File`) instead of the raw property-bearing leaves. The default `Self` keeps v0.3 behavior.

> ### Compatibility: saved graphs that use the 8 modified pickers
>
> Adding the `resolveTo` input changes the zero-touch definition id of `Search.ByPropertyValue`, `Search.ByPropertyContains`, `Search.ByPropertyWildcard`, `Search.ByPropertyCompare`, `Search.HasProperty`, `Search.HasCategory`, `Search.InItems` and `Selection.Current`. Graphs saved with **v0.3 or earlier** still open unchanged: the old ids are registered as legacy aliases (a new `[NodeAliases]` mechanism in the loader), the node resolves to its v0.4 definition, and the absent `resolveTo` port simply keeps its `Self` default — i.e. exactly the v0.3 behavior. Re-saving the graph migrates it to the new id. A regression test pins every pre-0.4 id against the source so an alias can't silently disappear.

## Sample graphs

- **Six new curated samples** ship with the plugin and appear in the new Sample Graphs menu (the four lower-case developer graphs stay in the repository for tests/CLI but are not deployed):
  - *Getting Started - Math and Watch* — sliders, math, Watch, notes and groups; runs anywhere, including the CLI.
  - *Color Elements by Property* — search by property text → color the hits.
  - *Bulk Selection Sets from Values* — one saved selection set per property value, via lacing.
  - *Export Properties to Excel* — property extraction into a native .xlsx workbook.
  - *QTO Rollup by Category* — quantity takeoff summed per group, exported.
  - *Clash Triage and BCF Export* — clash results grouped by status → BCF 2.1 issues + CSV report.
  Every sample opens with teaching notes on the canvas (what to edit, what to press). The Navisworks samples are set to Manual run so nothing fires on open.
- **Deployment** — the build now stages the six curated `.dyc` files into a `Samples` folder inside the plugin layout, the dev bundle deploy, and the release bundle zip (with a fail-the-release gate if the folder comes up empty), so the in-app menu is populated in every install layout.
- **Validated, not hand-checked** — a new static-validation test suite pins every shipped sample against the real node registry: definition ids, port names, defaults and connector endpoints (Navisworks node signatures are cross-checked against the C# source). Renaming a node or port now fails CI before it can break a sample.

## Under the hood

- Version bumped to **0.4.0**; all suites green on Linux CI: Core **117** (incl. 4 legacy-alias tests), Nodes **304**, Integration **31** (up from 18: +10 sample static-validation tests, +1 runnable sample, +2 legacy-id alias pins), plus CLI smoke runs of every general-node sample and `validate` passes over the Navisworks samples.
- No new dependencies; the UI work is pure WPF/Nodify 7.3.0, the samples pipeline is pure MSBuild + workflow staging.
