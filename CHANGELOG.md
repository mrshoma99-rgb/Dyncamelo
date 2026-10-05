# Changelog

All notable changes to CamelGraph (called Dyncamelo up to version 0.48) are listed here, newest first. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/) (0.x versions were still changing quickly).

Each release is published on the [Releases](https://github.com/mrshoma99-rgb/dyncamelo/releases) page. The longer release notes live in `docs/`: [0.51.0](dist/RELEASE_NOTES.md), [0.46.0](docs/WHATS_NEW_0.46.0.md), [0.45.1](docs/WHATS_NEW_0.45.1.md), [0.45](docs/WHATS_NEW_0.45.md), [0.12 to 0.23](docs/WHATS_NEW_0.23.md), [0.4](docs/WHATS_NEW_0.4.md), [0.3](docs/WHATS_NEW_0.3.md) and [0.2](docs/WHATS_NEW_0.2.md).

How this file was made: from those notes, from the git tags (they stop at v0.34.0 in this repository) and from the "Release vX" commits for 0.35.0 to 0.45.1. Patch releases are folded into their minor version. Dates are commit dates. Versions before 0.9 are covered only where a `docs/WHATS_NEW_*.md` file exists.

## 0.51.0 - 2026-10-05

A review of the whole node library: 210 findings (bugs, duplicates, gaps, nodes that did not work with lists, nodes that could break a node group) were fixed, with 42 new node names (a few are the merged or renamed form of older nodes) and 32 nodes retired from the library, and node packs now survive an update. The library has 589 nodes in 46 categories. These downloads (GitHub, bimcamel.com) are the free personal-use edition; a copy for professional use is coming soon to the Autodesk App Store.

Saved graphs keep working. Every node that was renamed, merged or retired still loads under its old id and old port names, the shipped sample graphs were not edited, and a retired node is only hidden from the library (it still runs). Nothing in this release was tried inside Navisworks; `docs/QA_CHECKLIST.md` lists what to try.

### Added
* **Node packs survive updates.** Packs are now read from `%APPDATA%\CamelGraph\Packages` first (it lives with the settings, so an update or an uninstall leaves it alone), then from the `Packages` folder beside the plug-in as before; a file name found in the first folder wins. A pack that fails to load no longer takes the others with it. **Help > Node Packs** says where packs go, what loaded and what did not (with the reason), and opens the folder; Copy Diagnostics has a "Node packs" section.
* **Expand all / collapse all in the node library are a small plain + and −**, 20 px rings instead of 28 px arrows.
* **Lists and dictionaries:** `Dictionary.ValueAtPath` (follow `Project/Tasks/Task/0/Name` into JSON or XML data, `*` for every item, a default when a step is missing), `Dictionary.SelectKeys`, `Dictionary.RemoveKeys`, `Dictionary.SetValues`. `XML.Parse` has a `listElements` input that makes named elements always a list.
* **Tables:** `Table.Unmatched` (the left rows with no partner after a join) and `Table.SetColumn` (replace a column in place, or add it).
* **Maths and dates:** `Math.IsClose` (equal within a tolerance), `DateTime.Add` (seconds to years), `DateTime.Difference` (in a chosen unit, calendar months), `DateTime.AddWorkdays`, `DateTime.IsWeekend`, `DateTime.FromExcelSerial` (turns the serial numbers `Excel.ReadFromFile` gives into dates).
* **Files and web:** `Directory.Find` (files or folders; replaces three nodes), `Directory.Copy`, `Directory.Move`, `Web.Download` (a file from the web, byte for byte), `XML.ReadFromFile` (honours the file's declared encoding), `Path.Join` (any number of parts), `Graph.Folder` (the folder relative paths resolve against).
* **Flow and workflow:** `Flow.Require` (stops a branch with your own error message when a condition is false, passes the value through when true), `IFC.IsGlobalId` and `IFC.Normalize` (columns that mix 36-character GUIDs and 22-character GlobalIds).
* **Clash:** `Clash.ResultsTable` (a table of the filtered results), `ClashResult.ByGuid` (find results or groups again from BCF topic GUIDs), `Clash.FilterBySnapshot` (the new or persisting clashes as live results). `Clash.SummaryTable` has a `table` output; `ClashResult.Info` has `guid`, `testName` and `group` outputs.
* **Viewpoints and TimeLiner:** `Viewpoints.DeleteFolder` (delete a folder or empty it), `Camera.Save` and `Camera.Restore` (leave the view as the graph found it), `TimelinerTask.ByName`.
* **Selection sets and properties:** `SelectionSets.InFolder`, `SelectionSets.SortFolder`, `SelectionSets.RenameFolder`, `SelectionSets.DeleteFolder` (refuses a folder that still holds something unless `deleteContents` is on), and `Properties.SetCustomFromTable` (writes a different row of a table onto each item, by position or by a GUID column, and checks everything before it writes).
* **Model and appearance:** `ModelItem.Info` (name, class, GUID, has geometry, is hidden in one node), `ModelItem.Path` (the tree path as text), `SavedItem.SetCommentStatus` (closes out review comments without clearing the thread) and `Appearance.ColorByValuesTemporary`.
* **For node pack authors:** `[AcceptsNull]`, `[ScalarInput]`, `[LiveState]` and `[NodePath(...)]` attributes, `NodeWarnings.Add` (a warning without failing the node), `PathResolver` and `GraphContext.Folder` for relative paths, `NodeEffects.WritesFiles` / `ChangesModel`, `SearchTags` for hand-written nodes and `EvaluationContext.Current` (a long loop can check for Stop). They are described in `docs/EXTENDING.md`.

### Changed
* **One meaning of "equal" and one order for sorting.** `Equals`, `Logic.NotEquals`, `Logic.Compare` (`==` and `!=`) and `Logic.Switch` ignore the case of text, treat text that reads as a number as that number ("5" equals 5), treat text that reads as a date as that date, and call two lists equal when they hold equal items in the same order. Lists and dictionaries are compared by content everywhere (`List.UniqueItems`, `Contains`, `IndexOf`, the set nodes, `CountValues`, `GroupByKey`). Every ordering in the library puts numbers by value and text ignoring case, with text that reads as a number placed as a number and empty items always last; a number against ordinary text no longer fails.
* **A missing value never passes a greater-than or less-than test.** In `Logic.Compare`, `Logic.IsBetween`, `List.FilterByValue` and `Table.Filter`, an empty cell used to pass "less than 3000", and a cell of text such as "TBC" next to a number turned the whole filter red. Now neither passes, the node warns and counts them. `List.FilterByValue` and `Table.Filter` have new `in` / `notIn` tests for a list of values or comma text.
* **Lists with gaps and nothing to average.** `Sum`, `Product`, `Average`, `Median`, `Percentile`, `StandardDeviation`, `Histogram` and `Statistics` skip empty or blank cells like nulls. A list with no numbers gives an empty result and an amber warning instead of a red error. `List.AllTrue`, `AnyTrue` and `CountTrue` count only true/false (a numeric list is no longer "true"). `Logic.IsBetween` and `Logic.Switch` take a whole column; `IsNull` and the comparisons with List Level `@L1` see the empty elements, so the "mask for `List.FilterByBoolMask`" recipe works.
* **List nodes.** `List.Sort` and `List.SortByKey` have a `descending` switch (`List.SortDescending` is retired). `List.RemoveItemAtIndex` takes one index or a list of indices; `List.Slice` can leave `end` empty; `List.Range`, `Cycle` and `OfRepeatedItem` stop with a clear message at more than 1,000,000 items, and `List.Range` no longer drifts. `List.Zip` warns when the lengths differ. `List.CountBy` is shown as `List.CountValues` (still found by "countby").
* **Tables.** `Table.Sort`, `Distinct`, `GroupBy` and `Join` take a list of column names, and a column whose own name has a comma can be named whole. `Table.Join` joins on several columns, never matches a blank key to another blank key (a thousand blank marks used to multiply into millions of rows) and matches a GUID whatever its capitals. `Table.AddFormulaColumn` never counts a blank or text cell as 0 (the row gets an empty cell and the node says how many; a division by zero is an empty cell, not Infinity). `Table.ToCsvFile` and `Table.ToExcelFile` write one table; a list of tables is an error instead of overwriting the same file.
* **Files, paths and encodings.** A relative path means "next to the graph file" (a graph not yet saved: `Documents\CamelGraph`; the Script Player and the command line: the script's folder), and quotes pasted from Explorer are removed. The browse button opens the right dialog: a folder chooser for folders, a Save dialog (a file that does not exist yet can be chosen) for files a node writes, an Open dialog otherwise. Every node that writes a file says so ("writes files: …") when a graph from a file is opened or a script is run in the Player; the clash, export, viewpoint, TimeLiner, selection-set and property nodes that change the model say that too. Reading detects the encoding, so a Windows-1252 "CSV (Comma delimited)" from a European Excel shows `Müller`, not `M?ller`; writing can add a byte-order mark. CSV dates are written as `2026-10-04 14:30:00` in every country and are real date cells in Excel; codes such as `007`, whole numbers of more than 15 digits and the words NaN and Infinity stay text. Excel files are written to a temporary file and put in place, so a failure never leaves a broken file. A file that cannot be read or written is reported in plain words.
* **Writers that used to damage a file.** `Text.WriteToFile`, `Text.AppendToFile` and `JSON.WriteToFile` with nothing wired are errors that leave the file alone (before: the file was emptied, a blank line was added or the word null was written; empty text still empties a file on purpose). `Text.WriteToFile` writes a list of texts one item per line (before: the file was written once per item and only the last survived). `File.Move` with overwrite no longer deletes the destination before it knows the move works. `File.Exists` and `Directory.Exists` answer false for a blank path.
* **Node groups and loops.** A `Flow.When` that is false inside a node group no longer blanks the whole group, a failed input only stops the nodes inside that depend on it (the group shows an amber "Upstream failure" when nothing inside took care of it), `Flow.Try` inside a group catches a failure that comes from outside, and a frozen node keeps the group's previous outputs with an amber warning. A loop pass that a `Flow.When` switched off is left out of the results; a loop whose list comes from a failed node waits instead of running zero times and showing green; failing passes show an amber warning on `Loop.Collect`. **Make Node Group** keeps input nodes, Watch nodes and anything marked "Show in Player" outside the group and says which; "Show in Player" does nothing, and says why, while a group is open.
* **Watch nodes and the Player.** Watch, Watch List, Watch Table and Watch Image clear what they show when they do not run; Watch writes the first 1,000 items and Watch List and Watch Table draw the first 2,000, so a huge list no longer slows the canvas; Watch Image accepts a list of paths. The Script Player shows "(no value: reason)" in red for a result that did not run and a list as a count with one item per line. A node that runs out of memory is an error on that node, and the rest of the graph runs.
* **Clash.** `Clash.GroupResultsBy…` work on a clash test inside a Clash Detective folder and say that they dissolve the groups the test already has. `Clash.FilterBySet` matches clash items against a selection set properly. `ClashResult.SetStatus`, `Assign` and `SetDescription` accept a result group. `ClashTest.Create` no longer silently replaces a test of the same name: the new `ifExists` input defaults to reuse (the test comes back with its results, statuses and comments, with a warning), with `update`, `replace` and `error` as the other choices, and a `folder` input. An empty list wired to `tests` of `Clash.SummaryTable` or `Clash.SnapshotToFile` now means no tests, not every test; leave the input unwired for every test. `Clash.Deduplicate` works across tests (mirrored A-vs-B / B-vs-A clashes are merged). The tolerance, radius and level elevations of the clash nodes can be given in any unit; the nodes that fetch tests and groups read them again on every run.
* **Viewpoints, export and TimeLiner.** `Viewpoint.Save` replaces `Viewpoint.SaveCurrent` and `Viewpoint.SaveWithOverrides` (one node with a `folder`, a name or a path such as "Reviews/Week 12", created when missing, and a `bakeOverrides` switch). `Viewpoints.FromClashResults` no longer lets the results of a second clash test replace the first (viewpoints from more than one test are named "Test - Clash1"). A viewpoint, folder or task name shared by several items is no longer picked silently: the node warns and a path chooses exactly. `Appearance.Isolate` with an empty list does nothing and warns instead of hiding the whole model. `Export.ClashReport` writes CSV or HTML by the file extension; `Export.ViewpointImage` takes the viewpoints itself (`{name}` in the path gives one picture per viewpoint). `TimelinerTask.Create` updates a task of the same name instead of adding it again and can go under a parent.
* **Model and selection.** `Model.Statistics` with an empty list gives an empty table and a warning instead of counting the whole model. `Model.Remove` accepts a list and removes the models you named. `Document.Open` with a list of paths is an error that points to `Document.AppendFiles`. `ModelItem.Translate`, `RotateAboutAxis` and `Scale` have an Advanced option `accumulate` (on, as before; off, running again no longer piles up), skip items below another listed item and warn. `Units.Convert` and `Units.ScaleFactor` can convert areas and volumes. A picked selection of several elements is delivered whole to list inputs (only the first element used to arrive). A model element you pinned on a node input, and the Captured Selection node, remember each element's GUID; elements that cannot be found are left out with a warning. `Selection.Current`, `SelectionSets.All`, `SelectionSet.Items` and the document nodes read Navisworks again on every Run.
* **Search and selection sets.** A list wired into the `value` input of `Search.ByProperty` means "any of these" (it used to match nothing), and there is a new `exists` mode and a `within` input. `SelectionSet.CreateFromSearch` takes the same modes, and `SelectionSet.Create` / `CreateFromSearch` can file the set in a folder path. `SelectionSet.Rename` and `MoveToFolder` take lists. `Properties.SetCustom` refuses a list where a value is expected instead of writing text such as ``System.Collections.Generic.List`1[System.Object]`` into every item; `Properties.ToTable` with no property list reads every property the items carry.
* **Workflow.** `Workflow.ForEach` no longer dies on the first bad item: it names the failing item in one amber line, leaves its result empty and goes on (`onError = stop` ends at the first failure), Stop ends a long batch between two actions, and the Action nodes wire straight into `actions`. `Flow.Then` takes as many `after` wires as you like.
* **Geometry.** `BoundingBox.Union` and `Point.Centroid` accept several wires. Port names are consistent (`boundingBox` on every BoundingBox node; `BoundingBox.ByCorners` says `cornerA` / `cornerB`; `Point.DistanceTo` says `a` / `b`). A number that is not a number (NaN) now turns `Vector.Scale`, `Point.Lerp`, `Point.ByCoordinates` and `Point.DistanceTo` amber with a sentence instead of passing on silently. Sliders for `Point.Lerp` `t`, `BoundingBox.Scale` `factor` and the vector tolerances.
* **Text, dates, maths and colour.** `String.Contains`, `IndexOf` and `LastIndexOf` ignore case unless `ignoreCase` is switched off (a graph that stored `false` keeps it). `String.IsBlank` and `String.Concat` see the gaps of a list; `String.Join` and `String.Format` write a missing value as empty text, not the word "null"; `String.Substring` no longer fails when the length runs past the end. `String.Replace`, `Split`, `Trim` and `ToNumber` gained options (`ignoreCase`, `removeEmpty`, `trim`, characters to trim, `decimalSeparator`, `ignoreUnits`), all defaulting to the old behaviour. `Number.Format` is now `String.FromNumber`. `Divide`, `Modulo`, `Math.Pow`, `Math.Exp`, `Math.Percent`, `Math.MapRange`, `Math.Lerp` and `Math.Formula` still return Infinity or NaN when the maths gives it, but the node shows a warning. `DateTime.Range` steps by days, weeks, months or years; `DateTime.Parse` has a `dayFirst` switch and warns when a date could be read two ways. `Color.ByValues`, `Gradient` and `RandomList` give empty lists for nothing instead of an error. `Report.Html` embeds image files as pictures and `Report.Markdown` links them.
* **Search and the editor.** The library search finds the input, display and loop nodes by the words people use ("dropdown", "checkbox", "preview", "debug", "for each", "picture", "calendar", "folder") and treats colour/color, grey/gray, centre/center and metre/meter as the same word. A node panel that starts open and that you closed stays closed when you save and reopen the graph. A `Choice` whose loaded value is not one of the options becomes the first option, and a slider's Min can no longer end up above Max.
* **Timeouts and limits.** `Flow.Wait` waits at most 600 seconds; `System.Run`, `Web.Get` and `Web.Post` keep their limits but their sliders reach 300 and 120 seconds. The rarely used inputs of `System.Run`, `Web.Get`, `Web.Post`, `Web.Download`, `Directory.Find` and the Excel nodes sit in a collapsed Advanced panel.

### Retired (hidden from the library; graphs that use them keep working)
`List.SortDescending`, `Table.JoinByKey`, `Table.Headers` (into `Table.Info`), `Search.HasProperty`, `Search.HasCategory`, `Search.InItems`, `Viewpoint.SaveCurrent`, `Viewpoint.SaveWithOverrides` (into `Viewpoint.Save`), `Export.ClashReportCsv` and `Export.ClashReportHtml` (into `Export.ClashReport`), `Export.NWD` (into `Document.Save`), `ViewpointPackageFile.Parse` (use `Viewpoints.ImportFile`), `Property.Info`, `Number.Format` (now `String.FromNumber`), `List.CountBy` (now `List.CountValues`), `Directory.FindFiles`, `Directory.GetFiles`, `Directory.GetDirectories`, `BoundingBox.FromPoints` (into `BoundingBox.Union`), `DateTime.AddDays`, `AddHours`, `AddMinutes`, `AddMonths`, `AddYears` and `AgeInDays` (into `DateTime.Add` and `Difference`), and `ModelItem.DisplayName`, `HasGeometry`, `ClassInfo`, `IsHidden`, `InstanceGuid`, `GeometryLeaves` and `ObjectAncestor` (into `ModelItem.Info`, `ModelItem.Path` and `Selection.Resolve`).

### Fixed
* `Clash.GroupResultsBy…` told you the test was no longer in the document when it sat inside a Clash Detective folder.
* `Clash.FilterBySet` could keep nothing, or with invert everything.
* `Model.Remove` with a list of the first two models removed the first and third.
* `Properties.SetCustom` wrote the text of a .NET list into every item.
* `Table.Join` multiplied blank keys into millions of rows.
* A viewpoint, folder or task name used by several items was silently resolved to one of them.
* `Viewpoints.DuplicateFolder` doubled the copy on a re-run.

### Not done in this release
Run-level undo (one undo step per whole run), a literal box for `any` ports, a calendar picker, document pickers for clash tests, sets and viewpoints, and the 2026 `ClashResult.SetStatus` / `Assign` (still "not supported yet" there) need work in the editor or a try in Navisworks and are left for a later release.

## 0.50.0 - 2026-10-04

A new logo, a floating node library, a Script Player that is easier to read, and Run at the right edge of the header. These downloads (GitHub, bimcamel.com) are the free personal-use edition; a copy for professional use is coming soon to the Autodesk App Store.

### Changed
* **New logo.** The mark is the wire that climbs into a hump before it drops into the right-hand socket. The dark icon is a wash from deep blue to near-black instead of flat black; the editor, the Script Player, the installer and the About window show a light mark on a dark surface and a dark one on a light surface.
* **The name in pixel letters.** "CamelGraph" is set in the pixel letters of the BIMCamel wordmark in the editor header and start screen, the About window and the installer.
* **New Script Player icon**: a play triangle made of woven wires, in the ribbon (black and white) and in the Script Player's header.
* **Floating node library.** A rounded card on the canvas with a full-width search box, round expand-all and collapse-all buttons, a **HIDE** tab on its edge and a **NODES** tab on the edge of the canvas to bring it back.
* **Script Player redesign.** Header with its icon and a folder button (the script folders open under it), cards for the script, the inputs and the results, and a sticky footer with a wide **Run** button and reset, edit and show-in-Explorer buttons.
* **Clearer rows in the Script Player.** Bold labels; a tag for the kind of control on each input; the name of a number above its field; results as cards with a green bar and an OUTPUT tag (ERROR in red for a failed node) and a monospace value with no box.
* **Run at the right edge of the editor header**, flat and as tall as the header, with the Auto / Manual switch just left of it, then a separator, then undo, redo, previews, minimap and settings.

### Fixed
* A problem listed under the Script Player's results is marked red for an error and amber for a warning; the dot's colour was set in a way a trigger could not override.

## 0.49.0 - 2026-10-04

Dyncamelo is now called **CamelGraph**, with a new logo, and everything on disk carries the new name. Installing the new version replaces an old Dyncamelo install. These downloads (GitHub, bimcamel.com) are the free personal-use edition; a copy for professional use is coming soon to the Autodesk App Store. Entries below this one use the name the program had at the time.

### Changed
* **New name and logo** on screen: the ribbon button and its tooltips, the editor header and start screen, About, dialogs, the installer, the Windows Apps entry (*CamelGraph for Navisworks*), the diagnostics, self-test and performance reports, the colour theme (*CamelGraph Dark*), the `.dyc` file filter and the privacy policy. The logo is two node ends and the wire between them, drawn light on a dark surface and dark on a light one (the editor header, start screen and Script Player follow the palette); the Autodesk App Store icons and the wiki use it too.
* **New file names.** The installer is `CamelGraphSetup.exe`, the zip `CamelGraph-<version>-navisworks.zip`, the bundle folder `CamelGraph.bundle` with `CamelGraph.App.dll`, `CamelGraph.Core.dll`, `CamelGraph.UI.dll`, `CamelGraph.Nodes.dll` and `CamelGraph.Navisworks.dll`, the script `install-camelgraph.bat`, the node catalogue `docs/camelgraph-nodes.json` and the command-line tool `camelgraph`. Settings are in `%APPDATA%\CamelGraph` and the default scripts folder is `Documents\CamelGraph\Scripts`.
* **Installing replaces an old install.** `CamelGraphSetup.exe` and `install-camelgraph.bat` remove the old `Dyncamelo.bundle` folder and its entry in Windows Settings > Apps before they install; removing CamelGraph also removes a leftover Dyncamelo install. The installer window says "Install (replaces Dyncamelo v…)" when it finds one.
* **Settings and scripts carry over.** The first start moves `%APPDATA%\Dyncamelo` (settings, recent files, favourite nodes, autosaved copies, the error log) to `%APPDATA%\CamelGraph`. The Script Player adds `Documents\Dyncamelo\Scripts` to its folders once, so the scripts there still show up.
* **Old graphs open as before.** Node ids written by earlier versions (they start with `Dyncamelo.`) are upgraded when a graph loads, and so are favourite and recent nodes and the saved *Dyncamelo Dark* theme. The `.dyc` format has not changed, and its envelope key stays `Dyncamelo`, so graphs saved now still open in older versions.
* **Custom property tab.** `Properties.SetCustom` and the cluster nodes write to a tab called *CamelGraph Data* by default; it was *Dyncamelo Data*. A tab already written to a model keeps its old name, so a graph that reads that tab by name needs the old name typed in.
* **Plug-in ids.** The Navisworks plug-ins are `CamelGraph.Command.DYNC`, `CamelGraph.DockPane.DYNC`, `CamelGraph.Launch.DYNC`, `CamelGraph.PlayerPane.DYNC` and `CamelGraph.Run.DYNC` (the last was `Dyncamelo.Run.DYNC`): a script, add-in or Batch Utility job that runs a graph through `ExecuteAddInPlugin` needs the new id.
* **For node pack authors.** The libraries are `CamelGraph.*` and the value types `CamelGraphColor`, `CamelGraphPoint`, `CamelGraphVector`, `CamelGraphBoundingBox` and `CamelGraphTable`. A pack built for 0.48 or earlier has to be built again against `CamelGraph.Core.dll`.

## 0.48.0 - 2026-10-03

The editor header is one row, the Run button is a wide solid key, several nodes are much faster on big models and tables, and three clash grouping nodes that silently did nothing now work. These downloads (GitHub, bimcamel.com) are the **personal-use edition**; professional use is the copy for the Autodesk App Store, coming soon.

### Changed
* **Faster on big models and big tables.** None of this was timed in Navisworks; the logic was tested on its own against the old code (same results, much fewer steps).
  * `Clash.GroupResults` reads the clash tree once instead of searching it for every result it moves; `Clash.GroupResultsByProximity` finds the nearby group through a grid instead of comparing with every group.
  * `Viewpoints.FromClashResults` and `SelectionSets.BulkByPropertyValues` keep the names already in the folder in a dictionary instead of scanning the folder twice per item.
  * `Selection.Remove` filters the selection in one pass and sets it once; it used to search the selection for every item it removed.
  * `BCF.ImportIssues` looks up the component GUIDs that were not matched with one search per batch of 500 instead of one whole-model search per GUID.
  * `Proximity.NearestDistance` reads each bounding box once and skips targets that cannot be nearer (same nearest item and distance as before); in mesh mode the clash engine is set up once, not per item.
  * `Table.Sort` reads each cell once; sorting a column that mixes numbers and text no longer throws two exceptions per comparison. The order is unchanged.
  * `Dictionary.ContainsKey` and `Dictionary.ValueOrDefault` no longer walk a dictionary that can only hold text keys when a key is not there.
* **One header row instead of two.** The blue brand bar and the menu bar are merged: the BIMCamel logo and "Dyncamelo by BIMCamel" sit at the left of the menus, the name of the open script and the buttons at the right, with a thin strip of the brand gradient on top. The pane gains a row for the canvas. In a narrow pane the script name goes first, then the "Dyncamelo by BIMCamel" text, and in a very narrow one the menus fold into one ☰ button (it holds the same menus) instead of wrapping onto several rows; the buttons always stay. Hover the logo for the version.
* **Run is the main button of the toolbar.** It is filled with the palette's main-action colour and bold, with the play symbol, where it was a quiet button like the others. It is a wide, solid-colour key (no gradient), 24 px high, raised by a light edge and a thin shadow so it does not look pressed; pressed, it loses the shadow, darkens and sits a pixel lower. Automatic / manual running is now the same on/off switch the Boolean nodes use, with the name of the mode beside it (*Auto* when on, *Manual* when off), instead of a toggle button. Every palette has two new colours for the main action (`Dyc.PrimaryBrush`, `Dyc.OnPrimaryBrush`), and a test keeps their contrast readable.

### Added
* **A wiki for bimcamel.com**, built with MkDocs and the Material theme from the repository by `python tools/build_wiki.py` (nothing generated is committed; the site goes to `build/wiki-site`): installation, requirements, updating, uninstalling, a first script, concepts, inputs and outputs, the editor, the Script Player, settings, shortcuts, samples, recipes, how-to guides, IFC, BCF, Excel and CSV, troubleshooting, an FAQ, a glossary, privacy and the licence, and a reference page for every one of the library's nodes with its inputs and outputs. It is plain static HTML with a built-in search, a light and a dark theme, and it also works from an unzipped folder. Troubleshooting, recipes and the guide to writing nodes are the repository's own documents, so there is one copy of each. Its pictures (60 of them, each in a dark and a light version) are drawn from the real editor and Script Player by the Windows CI job (`tests/Dyncamelo.UI.Tests/Wiki`; Navisworks nodes are drawn from stand-ins built from the node catalogue) and imported with `tools/wiki_pictures.py`. CI builds the wiki in strict mode (a broken link or a missing picture fails the build) and keeps the site as the `dyncamelo-wiki` artifact.
* **More troubleshooting entries**: a search that returns nothing or a magnifier with no names, and a missing start screen.

### Fixed
* **`Clash.GroupResultsBySameItem`, `Clash.GroupResultsByProximity` and `Clash.GroupResultsByLevel` now change the clash tree.** They saved their result with a call that ignores the results tree, so they reported success while Clash Detective stayed as it was; they now save the way `Clash.GroupResults` and `Clash.GroupResultsByStatus` do. The test they return is the re-fetched one, so a graph that reuses `ClashTest.Results` after one of them must take the results again (the message says so).

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
