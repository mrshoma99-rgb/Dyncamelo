# Wiki pictures and graphs: the agreed list

Everything the wiki shows as a picture is made by a test on the Windows build machine (`tests/Dyncamelo.UI.Tests`, folder `Wiki`) from the real editor, then committed to `docs/images/`. The names below are the contract between the people writing pages, the test that draws them and the graphs it draws.

* Image file: `docs/images/<id>.png` (dark theme) and `docs/images/<id>-light.png` (light theme). Pages write `![caption](../images/<id>.png)` once; the wiki build shows the light file in the light theme and the dark file in the dark theme when both exist.
* Graph file: `docs/wiki-src/graphs/<name>.dyc` (a real Dyncamelo graph, also offered for download on the how-to page: `[Download the graph](../graphs/<name>.dyc)`).
* Every graph picture is the graph as it opens, without a model: nodes idle, no results (unless the scene says it was run). Navisworks nodes are drawn from stand-ins made from `docs/dyncamelo-nodes.json`, so names, ports, types and defaults are exactly the real ones.

## Scenes the test draws

| Image id | What it shows | Source |
|---|---|---|
| `wiki-start-screen` | The start screen of an empty canvas, with New, recent scripts, examples, version chip, update notice | existing test `RenderTheStartScreenOfAnEmptyEditor` (named `editor-start*.png`; copied) |
| `wiki-editor-overview` | The whole editor with the library open and the *Table Summary from Text* sample after a run | sample, run |
| `wiki-node-anatomy` | One node close up: header, sockets, inline editors (number, text, drop-down, colour), value preview bubble | code |
| `wiki-socket-kinds` | One small node per socket kind (Number, Integer, Boolean, Text, DateTime, Colour, Geometry, Item, Selection, Viewpoint, Clash, Document, Data, File, Action) with type letters on | code |
| `wiki-socket-shapes` | A single value, a list, a list of lists, and a multi-input pill with three wires | code |
| `wiki-replication` | A list wired into a one-value input: dashed wire and a list of results | sample `list-lacing`, run |
| `wiki-lacing-modes` | The three lacing modes with results (Shortest, Longest, Cross-Product) | sample `list-lacing`, run |
| `wiki-first-script` | The finished first script: `Search.ByProperty` (Element, Material, Concrete, contains) → `Appearance.OverrideColor` (red) → `SelectionSet.Create` ("Concrete elements"), and a `Watch List` | graph `first-script` |
| `wiki-first-script-search` | The search node alone with its inputs filled | graph `first-script`, cropped |
| `wiki-magnifier` | `Properties.Value` with the magnifier popup open listing sample tab names | code, stand-in catalogue |
| `wiki-library-search` | The node library panel with "clash" typed in the search box and the category icons | code |
| `wiki-errors-and-warnings` | A red node with its message, an amber node, idle nodes behind a false `Flow.When`; status bar counts | code, run |
| `wiki-problems-list` | The Problems list open (errors first) | code, run |
| `wiki-mute-freeze` | A muted node (MUTED badge) in a live chain and a frozen branch (ghosted, FROZEN) | code |
| `wiki-node-group-instance` | A node group instance with its inputs and outputs next to the rest of the graph | code |
| `wiki-node-group-open` | The same group opened: Group Input, nodes, Group Output and the breadcrumb | code |
| `wiki-run-progress` | The progress overlay while running ("12 / 40 - name", Esc cancels) | code |
| `wiki-undo-history` | The Undo History panel with a few steps, some dimmed | code |
| `wiki-minimap` | The minimap on a graph of 40+ nodes | code |
| `wiki-watch-table` | A Watch Table result close up | sample `Table Summary from Text`, run, cropped |
| `wiki-colour-popup` | The colour popup with swatches and the eyedropper button | code |
| `wiki-player-form` | The Script Player with a script chosen and its form | existing player test |
| `wiki-player-results` | The Script Player after a run: results card | code, run |
| `wiki-settings-appearance`, `wiki-settings-canvas`, `wiki-settings-editing`, `wiki-settings-privacy` | One Settings page each | code |
| `wiki-shortcuts` | The F1 sheet | existing test (copy of `shortcuts.png`) |
| `wiki-command-palette`, `wiki-quick-search` | Command palette and Space quick search | existing tests (copies) |
| `wiki-sample-<kebab-name>` | One picture of every sample graph in `samples/` as it opens (e.g. `wiki-sample-color-elements-by-property`); the ones made of general nodes only (*Getting Started - Math and Watch*, *Table Summary from Text*, the four developer graphs) are run | `samples/*.dyc` |
| `wiki-graph-<name>` | One picture per how-to graph (below), fitted to the canvas | `docs/wiki-src/graphs/<name>.dyc` |
| `wiki-installer-ready`, `wiki-installer-done` | The installer window (stretch goal; skip if it needs more than a few lines) | `src/Dyncamelo.Installer` |

What cannot be drawn without a running Navisworks (the ribbon, the Navisworks window, the Properties and Sets windows): not pictured; pages describe them in words.

## How-to graphs (`docs/wiki-src/graphs/`)

Each is a small, correct graph built only from nodes in the catalogue, with the typed values the how-to uses. Names are file names without `.dyc`.

| Graph | Chain |
|---|---|
| `first-script` | `Search.ByProperty` → `Appearance.OverrideColor` → `SelectionSet.Create`; `Watch List` on the search result |
| `colour-by-value` | `Search.HasProperty` (or `Search.ByProperty`) → `Appearance.ColorByValues` (colour-code by a property's values) |
| `bulk-selection-sets` | `SelectionSets.BulkByPropertyValues` (Element ▸ Level) |
| `qto-to-excel` | `Search.ByProperty` → `Properties.ToTable` → `Table.GroupBy` → `Table.Sort` → `Table.ToExcelFile` |
| `clash-report` | `Clash.Tests` → `ClashTest.Results` → `Clash.SummaryTable` → `Table.FromRows` (or the table the node gives) → `Report.Html` → `Text.WriteToFile` |
| `bcf-export` | `Clash.Tests` → `List.GetItemAtIndex` → `ClashTest.Results` → `BCF.ExportIssues` |
| `bcf-import` | `BCF.ImportIssues` → `Selection.SetCurrent`; topics → `List.GetItemAtIndex` → `Dictionary.ValueOrDefault` (title) → `Watch` |
| `ifc-export` | `Search.ByProperty` → `Export.ToIfc`, with `Export.IfcSpatialNames` and `Export.IfcCoordinates` wired into it |
| `player-form` | `Number`, `Choice` and `File Path` input nodes feeding a search and `Table.ToExcelFile`, set to show in the Player |
| `properties-from-excel` | `Table.FromExcelFile` + `Properties.ToTable` → `Table.Join` → `Properties.SetCustom` |
| `find-by-guid` | `Table.FromExcelFile` → a column → `Search.ByGuid` → `Selection.SetCurrent`; `missing` → `Watch List` |
| `data-completeness` | `Search.ByProperty` → `Audit.MissingProperty` → `List.Count` ×2 → `Math.Percent` → `Watch` |
| `keep-going-after-failure` | A risky node → `Flow.Try` → `Flow.When` on `failed` → `Log.Write` |

If a node, port or chain above does not exist as written, use the nearest real equivalent and say so in your report; never invent a node.
