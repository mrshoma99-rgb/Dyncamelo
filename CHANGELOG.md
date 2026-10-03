# Changelog

All notable changes to Dyncamelo are listed here, newest first. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/) (0.x versions were still changing quickly).

Each release is published on the [Releases](https://github.com/mrshoma99-rgb/dyncamelo/releases) page. The longer release notes live in `docs/`: [0.47.0](dist/RELEASE_NOTES.md), [0.46.0](docs/WHATS_NEW_0.46.0.md), [0.45.1](docs/WHATS_NEW_0.45.1.md), [0.45](docs/WHATS_NEW_0.45.md), [0.12 to 0.23](docs/WHATS_NEW_0.23.md), [0.4](docs/WHATS_NEW_0.4.md), [0.3](docs/WHATS_NEW_0.3.md) and [0.2](docs/WHATS_NEW_0.2.md).

How this file was made: from those notes, from the git tags (they stop at v0.34.0 in this repository) and from the "Release vX" commits for 0.35.0 to 0.45.1. Patch releases are folded into their minor version. Dates are commit dates. Versions before 0.9 are covered only where a `docs/WHATS_NEW_*.md` file exists.

## Unreleased

### Changed
* **One header row instead of two.** The blue brand bar and the menu bar are merged: the BIMCamel logo and "Dyncamelo by BIMCamel" sit at the left of the menus, the name of the open script and the buttons at the right, with a thin strip of the brand gradient on top. The pane gains a row for the canvas. In a narrow pane the "Dyncamelo by BIMCamel" text and then the script name give way to the menus and buttons; hover the logo for the version.
* **Run is the main button of the toolbar.** It is filled with the palette's main-action colour, bold and larger, with the play symbol, where it was a quiet button like the others. Automatic / manual running is now the same on/off switch the Boolean nodes use, with the name of the mode beside it (*Auto* when on, *Manual* when off), instead of a toggle button. Every palette has two new colours for the main action (`Dyc.PrimaryBrush`, `Dyc.OnPrimaryBrush`), and a test keeps their contrast readable.

### Added
* **A wiki for bimcamel.com**, built with MkDocs and the Material theme from the repository by `python tools/build_wiki.py` (nothing generated is committed; the site goes to `build/wiki-site`): installation, requirements, updating, uninstalling, a first script, concepts, inputs and outputs, the editor, the Script Player, settings, shortcuts, samples, recipes, how-to guides, IFC, BCF, Excel and CSV, troubleshooting, an FAQ, a glossary, privacy and the licence, and a reference page for every one of the library's nodes with its inputs and outputs. It is plain static HTML with a built-in search, a light and a dark theme, and it also works from an unzipped folder. Troubleshooting, recipes and the guide to writing nodes are the repository's own documents, so there is one copy of each. CI builds it in strict mode (a broken link fails the build) and keeps the site as the `dyncamelo-wiki` artifact.
* **More troubleshooting entries**: a search that returns nothing or a magnifier with no names, and a missing start screen.

## 0.47.0 - 2026-10-03

The first release since 0.45.1 with an installer and the bundle zip. These downloads (GitHub, bimcamel.com) are the **personal-use edition**; professional use is the copy sold in the Autodesk App Store.

### Added
* **Two editions, marked as such.** A copy from GitHub or bimcamel.com is the free *Personal use* edition (PolyForm Noncommercial 1.0.0): the installer, the About window and the start screen say so. For professional use (work at a company, a paid project or product) a copy for the Autodesk App Store is coming soon and will come with a commercial licence from BIMCamel; until it exists every store link in the app is greyed out and reads *coming soon*. A copy installed from the store (it carries the `distribution.txt` marker) will say *Professional* and does not check GitHub for updates. New command **Help > Autodesk App Store…**. The README, the store listing and help page say the same.
* **A small search button next to tab and property names.** On the nodes that read a property of an element (Properties.Value, ValueAsString, HasProperty and InCategory, ModelItem.AncestorPropertyMatches, Audit.MissingProperty, Takeoff.SumPropertyByGroup) the category and property inputs stay plain text boxes you can type in; a magnifier beside each lists the tabs, or the properties of the chosen tab, **of that node's own element** — the one picked on it or wired in — and a click fills the box. Nothing is read until the button is pressed, only that one element is read (the first 100 of a longer list), and what you typed narrows the list. The nodes that search the whole model (Search.ByProperty, HasProperty, HasCategory, SelectionSet.CreateFromSearch, SelectionSets.BulkByPropertyValues) have no element input, so their magnifier lists the tabs and properties of **the elements selected in Navisworks right now** (the first 100) and never searches the model to fill the list; Search.InItems offers those of its own items. Node authors get `[NodeTabChoice]` and `[NodePropertyChoice]` for it (see docs/EXTENDING.md).
* **Help > Run Self-Test** has one more check: a search for the name of the first model's root item must find it.
* **A start screen on an empty canvas.** Opening Dyncamelo (or deleting every node) now shows the installed version, a link to bimcamel.com, a "newer version available" notice with a **Get it** button when the daily update check found one, and cards: **New script**, your four newest recent scripts and the examples. The cards go away as soon as you open a script or add a node. *Help > BIMCamel Website* and *Help > Get the Newest Version…* are new commands too; the old plain hint is what remains after you press **New script**. Settings > Appearance > *Start screen on an empty canvas* switches it off.

### Fixed
* **Every top-level category of the node library now has an icon.** Data, IFC, Report, System and Utility showed only their name. A test now fails when a category is added without one.

### Faster
* **The search nodes walk the model once instead of once per data type.** Navisworks matches a value only when its data type equals the stored one, so Search.ByProperty (equals and the number comparisons), Search.ByPropertyValue, Search.ByPropertyCompare and Search.InItems tried a text as 2 types and a number as 5 or 6, each in a search of its own over the whole model. They now run one search with the types as alternatives (the way live search sets already did), and a search result is no longer copied a second time when no selection resolution is asked for. Same items as before, in model order. This is untested against a real Navisworks model on the build machines; the new self-test check and your own run will show it.

### Changed
* **The Script Player has a new layout.** A script bar names the open script and unfolds the list (search, ↻), the form is a card of labelled fields with an on/off switch beside its label, the results sit in a card of their own, and **Run**, **Reset**, *Edit* and *File* stay at the bottom in view however long the form is. The list folds away once a script is chosen, `Enter` in it chooses and puts the focus on Run, and typing in the search box no longer closes the open script or loses its values.

### Fixed
* **Text boxes no longer cut their text off.** The themed text box counted its padding twice, so every one was taller than intended with its text further in than asked, and a box with a fixed height clipped what it held. The padding now counts once everywhere (the library and quick search boxes, property fields, the text inputs on nodes, the Script Player). In the Script Player a long or multi-line value (a pasted table, a sentence) was shown in a box one line high with the second line half hidden; the field now wraps and grows with its text up to a limit, then scrolls.

## 0.46.0 - 2026-10-02

A source-code release: GitHub carries no installer or bundle for this version.

### Added
* **Help > Run Self-Test** runs 29 read-only checks on the Navisworks nodes against the open model (application and document, saved items, the model, model items, properties) and shows pass, fail or skip for each, with the node's own error. The report goes to the clipboard. It cannot change anything: it only uses nodes on a reviewed read-only list.
* **Help > Copy Diagnostics** copies the Dyncamelo and Navisworks versions, the installed Autodesk plug-in bundles, which copies of Nodify and Newtonsoft.Json are loaded and from where, and the end of `errors.log`. The user name, computer name and profile folder are replaced, so it is safe to post in an issue.
* **A question before a graph from a file runs** nodes that run programs, use the network, or delete, move or overwrite files. It is asked once per file and again only if the file changes. A graph saved with run mode Automatic is no longer run when it is opened from a file; the status bar says so. Graphs made in the editor and the built-in samples never ask. A new setting, **Ask before running graphs from files**, turns it off. Nodes declare what they do with the new `[NodeEffects]` attribute; nine library nodes do.
* **Help > Privacy Policy** shows the privacy policy (`PRIVACY.md`, embedded in the app), and **Settings > Privacy > Check for a newer version once a day** switches the only network request Dyncamelo makes off.
* Files for an Autodesk App Store listing in `appstore/` and a package builder, `tools/build_store_package.py`, which the release workflow runs only when asked (`[store]` in the commit message) and keeps as a build artifact (it is never published). A copy installed from the store never checks GitHub for updates.
* Documents: [SECURITY.md](SECURITY.md), this changelog, [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md), [docs/QA_CHECKLIST.md](docs/QA_CHECKLIST.md), issue forms and a pull request template.

### Fixed
* The node library no longer lists a stray "WatchTableNode" category (a helper method of the Watch Table node was offered as a node).

### Changed
* **New licence: the PolyForm Noncommercial License 1.0.0.** Dyncamelo is free for personal and other noncommercial use (hobby, learning, research, charities, schools, public bodies); using it for work at a company or in a paid project needs a commercial licence from BIMCamel. Releases up to 0.45.1 were published under Apache 2.0 with the Commons Clause and copies you already have stay under that licence; its text is kept in [docs/licenses](docs/licenses/README.md).
* Dyncamelo no longer merges its ribbon tab with the one of other BIMCamel tools. The merge used code that reached into Autodesk's ribbon assembly, which is not a public API. With the IFC exporter installed as well, Navisworks may now show two BIMCamel tabs.
* The "Table Summary from Text" sample is spaced out so the wires between its columns can be seen.
* The Script Player now also asks about a script whose only risky nodes run programs or use the network (for example `Web.Get`), not only about nodes that change the model or write files.
* The release pipeline installs the installer on a clean runner before publishing and checks the files, the version, the upgrade over an existing install and the uninstall. It signs Dyncamelo's own DLLs and the installer when a code-signing certificate is configured; until then downloads stay unsigned.

## 0.45.1 - 2026-10-01

### Fixed
* Picking an item in the node library no longer closes Navisworks. The library's four selection colours were built in a way WPF refuses; they are now the theme's own brushes.
* A WPF failure that follows a click or key press in the editor or the Script Player is written to `%APPDATA%\Dyncamelo\errors.log` and shown in the status bar instead of ending Navisworks.
* More tests: every resource of the editor and the Player is built up front, the library is selected in a real window, and the "hosted the way Navisworks hosts it" check runs in its own process.

Notes: [docs/WHATS_NEW_0.45.1.md](docs/WHATS_NEW_0.45.1.md).

## 0.45.0 - 2026-10-01

### Added
* About 220 new nodes. **Tables** (`Table.*`, **Watch Table**, `Report.Html`, `Report.Markdown`, `Properties.ToTable`, `Properties.Discover`), **conditional flow** (`Flow.When`, `Flow.Try`, `Flow.Wait`, `Logic.Choose`, `Logic.Switch`), `Math.Formula`, list statistics, strings with regular expressions, dates, dictionaries, colour, vector and bounding-box maths.
* Path, file, zip, system and web nodes (`File.*`, `Directory.*`, `Zip.*`, `System.Run`, `System.OpenPath`, `Web.Get`, `Web.Post`). Everything that writes, launches or posts is marked as changing things, so the Script Player asks before running it.
* New inputs: Integer, Date and Choice.
* Navisworks nodes, not yet tried in Navisworks (see "For the Navisworks side" in the notes): the IFC GlobalId bridge, `Model.Snapshot`, `Model.Statistics`, `Search.ByGuid`, `Selection.Invert` and `Selection.Remove`, clash test edit, delete, duplicate and clear, `SavedViewpoint.Info` and `Update`, `Camera.SetStandardView`, `Appearance.Focus`, TimeLiner progress, `ModelItem.Scale` and `MoveTo`.
* A sample graph, *Table Summary from Text*, and [recipes by role](docs/RECIPES.md).

### Changed
* `Navisworks.Clash` is split into Tests, Results, Filter, Group and Report; Viewpoints into Folders and Files; list statistics sit under `List.Statistics`. Saved graphs are not affected.
* The Number, Number Slider and Integer Slider nodes use the same scrub field as every other number.
* A node whose visual cannot be built is logged with its XAML file and position, taken off the canvas, and reported in the status bar instead of closing Navisworks.

### Removed
* The **Run Last Script** ribbon button. The Player button is now black and white.

Notes: [docs/WHATS_NEW_0.45.md](docs/WHATS_NEW_0.45.md).

## 0.44.0 - 2026-10-01

### Fixed
* The Script Player pane opens even when it is the first thing used in a session.
* Collapsed nodes are a capsule in their category colour with inputs on the left and outputs on the right.
* A failure of ours that would have closed Navisworks is logged to `%APPDATA%\Dyncamelo\errors.log` with its inner exceptions, and a XAML template that fails to load is reported in the status bar.

### Changed
* Node library clean-up. Retired nodes keep loading and running in saved graphs but are no longer offered; renamed ports keep their wires; every multi-output node declares the kind of its outputs.
* Opening a file says when wires or typed-in values could not be restored.
* `docs/NODE_CATALOG.md` is generated from the source and kept current by CI.

## 0.43.0 - 2026-09-30

### Added
* **Script Player**: a separate pane that runs saved graphs without opening the node editor. It lists the scripts under `Documents\Dyncamelo\Scripts` and any folders you add, builds a form from the script's input nodes, and lists the results of its Watch nodes. Esc stops a run. A script that changes the model asks once per file version.
* The `Dyncamelo.Run.DYNC` plug-in runs a script by path, for other add-ins, the Automation API and the Batch Utility.

## 0.42.0 - 2026-09-30

### Added
* **Node groups**: reusable pieces of graph with their own inputs and outputs (`.dyc` format 2, written only when a graph has groups).
* Unsaved-work safety (autosave and recovery, save prompts), a clickable Problems list, undo history, run up to a selected node (`Shift+F5`), canvas bookmarks, keyboard navigation, hints and a colour eyedropper.

## 0.41.0 - 2026-09-30

### Added
* Scrub fields and editors for colours, **Freeze** (`Shift+M`), a minimap button, a node library panel that can be hidden (`Ctrl+B`).
* **Esc cancels a running graph**; the run resumes from where it stopped next time.

### Changed
* Collapsed nodes keep their sockets. The light theme and menus were fixed.

## 0.40.0 - 2026-09-30

### Added
* **Multi-input sockets**: a list input can take any number of wires.

## 0.39.0 - 2026-09-30

### Added
* Layered **Arrange** (`Ctrl+Shift+L`), which ships `AutomaticGraphLayout.dll`; minimap (`Ctrl+M`); **command palette** (`Ctrl+Shift+P`); a sectioned Settings page with shortcut rebinding; the generated [editor guide](docs/UI_GUIDE.md).

### Fixed
* `Ctrl+Z`, `Ctrl+Y` and the other shortcuts Navisworks binds now reach the editor while its pane has focus.

## 0.38.0 - 2026-09-30

### Added
* Rope-style wire gestures (drop a node on a wire, release a wire on empty canvas to search for a node that fits, cut wires), a colour popup inside the node, model-element picker inputs, one command bar.
* The **crash guard**: a failing command or handler is written to `%APPDATA%\Dyncamelo\errors.log` and shown in the status bar instead of ending Navisworks.

## 0.37.0 - 2026-09-30

### Changed
* Blender-style row layout: each input is a row with its socket and an inline editor (scrub number field, toggle, choice, text, colour, path). Sockets and wires are coloured by data type.

## 0.36.0 - 2026-09-30

### Added
* Undo and redo for the whole editor, and the header menu bar.

## 0.35.0 - 2026-09-30

### Added
* Foundations for the new node UI: port kinds, colour palettes, mute, editor metadata saved in the graph, and the performance HUD (`Ctrl+Shift+F12`).

## 0.34.0 - 2026-09-30

### Added
* `Viewpoints.InFolder`: every saved viewpoint in a folder, in window order.

### Fixed
* The canvas no longer drifts when a node is deleted.

## 0.33.x - 2026-08-31

0.33.0 to 0.33.3.

### Added
* **Arrange Selection** (`Ctrl+L`).

### Fixed
* Canvas drift on delete (finished in 0.34.0), `ClashResult.Focus` on clash items that are containers, empty viewpoints after the first in a loop, a `Ctrl+L` crash, and the clash-viewpoint loop slowing down into a freeze.

## 0.32.0 - 2026-08-31

### Added
* `Clash.AllGroups` and `ClashGroup.Info`, and the sample *Clash Group Viewpoints per Test*.

## 0.31.x - 2026-08-21

0.31.0 to 0.31.3 (until 2026-08-24).

### Added
* A Dynamo-parity wave of 24 new `List` nodes (slicing, sets, transposing, true/false tests and more).
* `ModelItem.AncestorPropertyMatches`, and comma-separated statuses ("New,Active") in the clash status filters, with a `Clash.Statuses` node to build them.

### Fixed
* `Clash.GroupResults` falsely reported results from more than one test, and its grouping never reached the document. It now edits the tree in place.

## 0.30.0 - 2026-08-21

### Added
* Clash noise-reduction filters: property pair, set, depth and de-duplication.

## 0.29.0 - 2026-08-21

### Added
* `ModelItem.AncestorNameMatches`: tests the names of an item's ancestors.

## 0.28.0 - 2026-08-21

### Changed
* Lacing tolerates nulls: a null element gives a null result at its place instead of failing the whole node.

### Added
* `List.Clean`, `IsNull` and `IsNullOrEmpty`.

## 0.27.0 - 2026-08-17

### Added
* The clash triage set (eight nodes): explicit grouping with `Clash.GroupResults`, `Clash.FilterByOrientation`, `ClashResult.Focus`, `ClashGroup.ByName` and `ClashTest.Groups`.

## 0.26.x - 2026-07-29

0.26.0 to 0.26.2 (until 2026-08-14).

### Added
* `Viewpoints.ExportFile` and `Viewpoints.ImportFile` copy saved viewpoints between models.

### Changed
* **Licence**: Dyncamelo is now source-available under Apache 2.0 with the Commons Clause (0.26.2). Free to use, including at work; selling it is not allowed. See [LICENSE](LICENSE).

### Fixed
* The viewpoint package nodes no longer fail with a bare "access denied" for a folder, a relative path or a quoted path.

## 0.25.x - 2026-07-27

### Added
* **List@Level**: choose the nesting level an input port consumes, as in Dynamo (0.25.0).
* The sample *Section Box Viewpoints per Group* (0.25.1).

## 0.24.0 - 2026-07-27

### Added
* `ModelItem.CombinedBoundingBox`: one box around a whole group of items.

## 0.23.x - 2026-07-27

### Added
* `Color.Random`, `Color.RandomList`, `Color.Gradient` and `Color.ByValues`, with `Appearance.ColorByValues`. 0.23.1 refreshed the docs.

## 0.22.x - 2026-07-27

### Added
* `Proximity.Cluster` groups touching geometry into elements. A precise `mesh` mode confirms each connection with the Clash engine.

## 0.21.0 - 2026-07-23

### Added
* Experimental `Markup.*` nodes that draw redlines on saved viewpoints through a hidden Navisworks API.

## 0.20.0 - 2026-07-23

### Added
* `Viewpoint.VisibleItems`: which items does this viewpoint show?

## 0.19.x - 2026-07-19

0.19.0 to 0.19.3 (until 2026-07-21).

### Added
* A single **BIMCamel** ribbon tab shared with the IFC exporter, a shared About window, and an update check when the editor opens.
* `Flow.Then` pins the order of side-effect nodes as a data dependency.

### Changed
* The order of nodes that have no wire between them is no longer taken from their position on the canvas (0.19.3).

## 0.18.0 - 2026-07-17

### Added
* **Watch Image** node, an index gutter on Watch List, quick node search with `Space`, and port tooltips generated from the API documentation.

## 0.17.x - 2026-07-15

0.17.0 to 0.17.1 (until 2026-07-17).

### Added
* `FallHazard.FloorOpeningMap` takes a two-colour gradient and prints gap-over-limit labels.
* Each release also publishes a checksum file for the stable download name (0.17.1).

## 0.16.0 - 2026-07-15

### Added
* `FallHazard.EdgeHandrailCheck` takes custom edge colours and prints gap-over-limit labels.

## 0.15.0 - 2026-07-15

### Added
* `minPassage` input on `FallHazard.EdgeHandrailCheck`.

### Fixed
* Corner cells were classified as safe.

## 0.14.0 - 2026-07-15

### Added
* A `units` input on the fall-hazard nodes, so numbers can be entered in metres whatever units the document stores.

## 0.13.x - 2026-07-15

0.13.0 to 0.13.8.

### Added
* `FallHazard.EdgeHandrailCheck` (0.13.4) and the Captured Selection node (0.13.3).
* A better Create, Modify or Info sorting of nodes in the library (0.13.0).

* The fall-hazard reports start with the plugin version (0.13.7) and state the rasterised plug and void areas (0.13.8).

### Fixed
* The fall-hazard clearance collapsed to about one cell (0.13.1); the edge gap measured the length of a slot instead of its width (0.13.5); a half-cell bias in the clearance (0.13.6).

## 0.12.x - 2026-07-15

### Added
* `FallHazard.FloorOpeningMap` gets a diagnostic `report` output (0.12.3), and its heat map pivots on the clearance limit (0.12.4).

The whole 0.12 to 0.23 wave is described in [docs/WHATS_NEW_0.23.md](docs/WHATS_NEW_0.23.md).

## 0.10.x - 2026-07-13

### Added
* The **universal loop**: `Loop.Item` and `Loop.Collect` run any nodes once per item (0.10.0).
* `BoundingBox.Scale`, `Camera.SetProjection`, `Camera.SetFieldOfView`, `ModelItem.ObjectAncestor` (0.10.1); an HSV colour picker dialog, an editable slider step and `ModelItem.CommonAncestor` (0.10.2).

## 0.9.x - 2026-07-13

### Added
* **Per-item workflows**: `Workflow.ForEach` and the `Action.*` builders (0.9.0), with temporary-override `Action.Highlight` and `Action.Ghost` so each saved view keeps its own colours (0.9.1).
* The bundle ships folders for **Navisworks 2024, 2025 and 2026** (0.9.0).

### Fixed
* Saved viewpoints now keep the zoomed camera (0.9.2).

## 0.2 to 0.4 (no dates recorded in this repository)

* **0.4**: instant library search, a Sample Graphs menu with six curated samples, `Selection.Resolve` and a `resolveTo` input on the pickers. Saved graphs from 0.3 and earlier still open. Notes: [docs/WHATS_NEW_0.4.md](docs/WHATS_NEW_0.4.md).
* **0.3**: 50 new nodes: custom property tabs, native Excel, BCF 2.1 exchange, clash management, transforms, document lifecycle, grids and section boxes. Notes: [docs/WHATS_NEW_0.3.md](docs/WHATS_NEW_0.3.md).
* **0.2**: 103 new nodes, copy and paste, node frames, value previews, library favourites. Notes: [docs/WHATS_NEW_0.2.md](docs/WHATS_NEW_0.2.md).
