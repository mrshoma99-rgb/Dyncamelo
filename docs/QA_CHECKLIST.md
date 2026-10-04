# Release QA checklist

The manual test script for a release. The owner runs it in **real Navisworks, once for each of 2024, 2025 and 2026**, before a release goes out.

Nothing can run the Navisworks API outside Navisworks, so the automated tests (see [CONTRIBUTING.md](../CONTRIBUTING.md)) cannot tell whether a Navisworks node, a pane or the installer works inside Navisworks. This script does. **A crash of Navisworks at any step is a release blocker.**

## How to use it

1. Copy the [results template](#results-template) into a text file. Fill it in as you go and paste it into an issue (or the release checklist) when you finish.
2. Do the sections in order. Use a fresh Navisworks session for each year.
3. In the **Result** column write `Pass`, `Fail`, `N/A` or `?`. Where **Expected** says **Unknown**, nobody knows the answer yet. Do not judge: write down what you see. That is the point of the row.
4. For every `Fail`: note the row number, save the end of `%APPDATA%\CamelGraph\errors.log`, and run **Help > Copy Diagnostics**. See [Troubleshooting](TROUBLESHOOTING.md#how-to-collect-diagnostics).
5. Expected results marked *(intended)* come from a node's description in the [node catalogue](NODE_CATALOG.md), not from having seen it work.

New in this release, and not yet seen working: **Help > Run Self-Test**, **Help > Copy Diagnostics**, and the warning before running a graph opened from a file (sections 5 and 6). Their rows are written from the release plan. If the real behaviour differs, write down what you see.

## What you need

* A Windows machine with Navisworks Manage or Simulate for the year under test. Write down which one.
* The release's `CamelGraphSetup.exe` and its `.sha256` file, and (for the upgrade test) the previous release's `CamelGraphSetup.exe`.
* **The small model.** A model of a few files with named items (for example items whose **Item > Name** contains "Wall"), a **Level** and a **Category** property, at least one **clash test that has been run** and has results (grouped results are better), at least two **saved viewpoints**, a **TimeLiner** task if possible, and grids if possible.
* **The large model.** The biggest federated model you have.
* An empty scratch folder, `C:\Temp\camelgraph-qa`. Graphs that write files must use full paths in it.
* Microsoft Excel, to open the `.xlsx` files.
* The sample graphs: they install with CamelGraph (**File > Sample Graphs**). The list of what each needs is in [samples/README.md](../samples/README.md).

---

## 1. Install, upgrade and uninstall

Close Navisworks before each step unless a row says otherwise.

| # | Step | Expected | Result |
|---|---|---|---|
| 1.1 | Check the download against its `.sha256` file: `Get-FileHash .\CamelGraphSetup.exe -Algorithm SHA256` | The hashes match. | |
| 1.2 | On a machine with no CamelGraph, run `CamelGraphSetup.exe`. | SmartScreen may warn (the file is unsigned): **More info > Run anyway**. No administrator prompt. The setup window names the Navisworks years it found. | |
| 1.3 | Click **Install**. | The window ends with "Installed". The folder `%APPDATA%\Autodesk\ApplicationPlugins\CamelGraph.bundle` exists with `PackageContents.xml` and the folders `2024`, `2025` and `2026`. Each year folder has `CamelGraph.App.dll`, `en-US`, `Resources` and `Samples`. | |
| 1.4 | Open **Windows Settings > Apps > Installed apps**. | "CamelGraph for Navisworks" is listed with the release version. | |
| 1.5 | Start Navisworks and open a model. | A **BIMCamel** ribbon tab with a **Visual Programming** panel and three buttons: **CamelGraph**, **Player**, **About**. No "Run Last" button. No `PLUGIN_LOAD_02` message. | |
| 1.6 | Click **About**. | The window shows "Version" with the release number. | |
| 1.7 | Install the **previous** release, then run the new `CamelGraphSetup.exe`. | The button reads "Update install (v... found)". After it, the bundle is the new version and no files of the old one are left. Settings in `%APPDATA%\CamelGraph` are kept. A saved graph still opens. | |
| 1.8 | Run `CamelGraphSetup.exe` while Navisworks is open. | The window warns that Navisworks is running. **Unknown:** does the install finish with files in use? Does the new version load after a restart? | |
| 1.9 | In a command prompt: `start /wait "" CamelGraphSetup.exe /uninstall /silent`, then `start /wait "" CamelGraphSetup.exe /silent`. | No window. Exit code 0 each time (`echo %errorlevel%`). The bundle folder is gone, then back. | |
| 1.10 | Uninstall from **Windows Settings > Apps**. | The bundle folder and the Apps entry are gone. `%APPDATA%\CamelGraph`, `Documents\CamelGraph\Scripts` and your `.dyc` files are still there. | |
| 1.11 | Zip install (once, any year): extract the release zip, run `CamelGraphSetup.exe` from the extracted folder. Also try `install-camelgraph.bat`. | Both install the bundle. No `PLUGIN_LOAD_02`. | |
| 1.12 | Download the zip in a browser, extract it with Windows Explorer, and copy its bundle folder by hand into `ApplicationPlugins`, without unblocking. | **May fail** with `PLUGIN_LOAD_02` / `0x80131515` (it does when Windows kept the "downloaded file" mark). If it fails, run the `Unblock-File` command from [Troubleshooting](TROUBLESHOOTING.md#navisworks-reports-plugin_load_02-or-0x80131515-at-start) and restart: it must load. | |

## 2. The editor and Player panes

Open a model first.

| # | Step | Expected | Result |
|---|---|---|---|
| 2.1 | Click **CamelGraph** on the ribbon. | The editor pane opens with the node library on the left and an empty canvas. | |
| 2.2 | Click **CamelGraph** again, then again. | The pane hides, then shows (the button toggles it). | |
| 2.3 | Click **Player**. | A separate **CamelGraph Player** pane opens, with the editor still open. | |
| 2.4 | Dock the editor on the left, right and bottom. Float it. Move it to a second monitor if you have one. | After each move the pane still works: add a node, run a graph. | |
| 2.5 | Dock the Player as a tab next to the editor, then float it. | It still works: pick a script, run it. | |
| 2.6 | Hide the editor pane with its close button, then re-open it from the ribbon. | It opens. **Unknown:** is the graph still there, or an empty canvas with an offer to restore it? | |
| 2.7 | With unsaved changes in the editor (wait a minute after your last edit) and the Player open, **close Navisworks**. | Navisworks closes with no error dialog, and no `Roamer.exe` is left in Task Manager. | |
| 2.8 | Start Navisworks and open the editor. | An empty canvas, and an offer to restore the autosaved graph from step 2.7. Saving or answering *No* removes the copy. | |
| 2.9 | Restart Navisworks with the panes where you left them. | **Unknown:** are the panes where you left them? ([Getting Started](GETTING_STARTED.md) says Navisworks remembers placement.) | |
| 2.10 | With the editor pane focused, press `Ctrl+Z`, `Ctrl+Y`, `Delete` and `F1`. | They act on the CamelGraph canvas (undo a node move, delete a node, show CamelGraph's shortcut list), not on the Navisworks model or help. | |
| 2.11 | Type in a text box in the Player, using `Ctrl+V`, `Ctrl+Z` and `Delete`. | Nothing in the Navisworks model changes. | |
| 2.12 | With the editor open, open another model (or append one). | No error. A run on the new model works. | |
| 2.13 | Press **F1**, and open the command palette with `Ctrl+Shift+P`. | The shortcut list and the palette open and close with `Esc`. | |

## 3. Library, canvas and failure drills

| # | Step | Expected | Result |
|---|---|---|---|
| 3.1 | Click once on a node in the node library (select, do not add). Do it on 5 different nodes. | **No crash** (this was the 0.45 crash fixed in 0.45.1). The entry is highlighted. | |
| 3.2 | Double-click a node in the library. | The node appears on the canvas. | |
| 3.3 | Drag a node from the library onto the canvas, and onto an existing wire. | It lands where dropped; on a wire it is spliced in. | |
| 3.4 | Press `Space` over the canvas, type `Add`, press `Enter`. | An Add node is added at the cursor. | |
| 3.5 | Type in the library search box, then press `Esc`. | Matching nodes are listed; `Esc` clears the search. | |
| 3.6 | In **Settings > Appearance**, choose each palette (CamelGraph Dark, Midnight, Slate, Light). After each, select an entry in the library. | No crash. The selection is visible and follows the palette. | |
| 3.7 | Add one node from each library category (open each category, add its first node). | No crash. Each node appears with its sockets. | |
| 3.8 | Wire two nodes, delete the wire, rewire, copy and paste nodes, undo and redo (`Ctrl+Z`, `Ctrl+Y`). | The canvas follows each step. | |
| 3.9 | Make a node group (`Ctrl+Alt+G`), open it with `Tab`, close it with `Shift+Tab`. | It works. Run the graph while inside the group. | |
| 3.10 | Paste a graph of 100 or more nodes (copy a sample several times). Pan and zoom. | The canvas stays usable. **Unknown:** record how it feels. | |
| 3.11 | **Failure drill.** Close the model so no document is open, then press **Run** on a graph with a Navisworks node. | **Unknown:** record the message. Navisworks must stay open. | |
| 3.12 | **Failure drill.** Close the document in the middle of a session, then open another one and run again. | No stale-handle error ("Object has been Disposed"). | |
| 3.13 | **Failure drill.** Leave a required input unconnected. | The node is idle with "Input 'x' is not connected." | |
| 3.14 | **Failure drill.** Feed a node nonsense (text into a number input, a list where an item is expected). | The node is red or amber, the message can be read, and Navisworks stays open. | |

## 4. Nodes by category

Use the small model. Set the graph to **Manual** run mode (Graph > Auto-Run off) unless a row says otherwise. Press **Run** (`F5`) after wiring. Clean up as you go (reset overrides, delete test sets and viewpoints) so one row does not hide a problem in the next.

### 4.1 Inputs and displays

| # | Step | Expected | Result |
|---|---|---|---|
| 4.1.1 | Number, Number Slider, Integer, Integer Slider, String, Boolean, Choice, Date: add each and wire it to a **Watch**. | Each shows an inline editor. Run: the Watch shows the value. Drag a slider: the number field scrubs. | |
| 4.1.2 | File Path and Directory Path: use the browse button. | The chosen path appears; a Watch shows it. | |
| 4.1.3 | Color Picker: open its popup (it is in the node, not a window). Pick a colour. Use the dropper on the Navisworks 3D view, then press `Esc` in another try. **Repeat 5 times.** | No crash. (A Color Picker crash report is not yet confirmed fixed, see [Known issues](TROUBLESHOOTING.md#known-issues).) The dropper takes the colour under the pointer; `Esc` cancels. | |
| 4.1.4 | `List.Range` into **Watch List**; `Table.FromRows` into **Watch Table**; a PNG path into **Watch Image** (type it out in full, for example `C:\Users\<you>\AppData\Roaming\Autodesk\ApplicationPlugins\CamelGraph.bundle\2024\Resources\camelgraph_32.png`). | A numbered list, a grid, and the picture. | |

### 4.2 Pure data nodes

| # | Step | Expected | Result |
|---|---|---|---|
| 4.2.1 | `Math.Formula` with `expression` = `a * b + 2`, a = 3, b = 4. | 14 | |
| 4.2.2 | Lacing: `Add` with `[1, 2, 3]` and `[10, 20]`; set the lacing to Shortest, Longest and Cross Product (right-click the node, **Lacing**). | `[11, 22]`, `[11, 22, 23]`, `[[11, 21], [12, 22], [13, 23]]` ([samples/README.md](../samples/README.md#list-lacingdyc)). | |
| 4.2.3 | `Table.FromRows`, `Table.GroupBy`, `Table.Sort`, `Table.ToText`. (Or run the sample *Table Summary from Text*, section 7.) | The table is grouped and sorted; the text is Markdown. | |
| 4.2.4 | `Flow.Wait` with `seconds` = 1. | The run takes about a second; the value passes through unchanged. | |

### 4.3 Files, system and web (scratch folder `C:\Temp\camelgraph-qa`)

| # | Step | Expected | Result |
|---|---|---|---|
| 4.3.1 | `Directory.Create` with a new sub-folder. | The folder exists. Running again is harmless. | |
| 4.3.2 | `Text.WriteToFile` (full path), wire its `path` output into `Text.ReadFromFile`, into a Watch. | The text read back equals the text written. `File.Exists` is true. | |
| 4.3.3 | `CSV.WriteToFile` then `CSV.ReadFromFile` (a 2 by 3 table), and `CSV.AppendToFile`. | Rows read back equal; the append adds rows. | |
| 4.3.4 | `Excel.WriteToFile` with headers and rows; open the file in Excel. | Excel opens it with no repair message; the cells are right. | |
| 4.3.5 | `Zip.Create` on a folder, `Zip.List`, `Zip.Extract` into another folder. | The listing and the extracted files match. Extracting again without `overwrite` fails with a message. | |
| 4.3.6 | `File.Copy`, `File.Move`, `File.Delete` on a test file. | Each does what it says; `File.Delete` on a missing file returns false. | |
| 4.3.7 | `Text.WriteToFile` with a **relative** path such as `qa-relative.txt`. | The file lands next to the graph file (in `Documents\CamelGraph` for a graph that was never saved); `Graph.Folder` shows that folder. It does not fail with "access denied". ([Troubleshooting](TROUBLESHOOTING.md#a-file-node-fails-with-access-denied-or-writes-to-the-wrong-place) says it cannot be written under the Navisworks install folder.) | |
| 4.3.8 | `Log.Write` twice to the same file. | Two lines, each with a time stamp and level. | |
| 4.3.9 | `System.Environment` into a Watch. | Your user name, the computer name, and the temp and Documents folders. | |
| 4.3.10 | `System.Run` with `executable` = `cmd.exe`, `arguments` = `/c echo hello`. | `exitCode` 0 and `output` contains `hello`. | |
| 4.3.11 | `System.OpenPath` on the scratch folder, once with `reveal` on. | Explorer opens the folder, then shows it selected. | |
| 4.3.12 | `Web.Get` of `https://api.github.com/zen` (any web address you trust will do). | `status` 200, `ok` true, `body` holds text. With the network off: the node is red with a readable message and Navisworks stays open. | |
| 4.3.13 | `Web.Post` to a test endpoint you control (skip if you have none). | `status` and `ok` match the endpoint's answer. | |

### 4.4 Document, model, search, selection and properties

| # | Step | Expected | Result |
|---|---|---|---|
| 4.4.1 | `Application.Version`, `Document.Info`, `Document.Models`, `Model.Info`, `Units.Current`. | The Navisworks version, the file name, units and model count match what Navisworks shows. | |
| 4.4.2 | `Search.ByProperty` (category `Item`, property `Name`, value `Wall`, mode contains) into `List.Count`. | The count equals the count of **Find Items** with the same condition. | |
| 4.4.3 | `Selection.Current` with 3 items selected, into a Watch List. | 3 items. | |
| 4.4.4 | Change the Navisworks selection and press **Run** again. | The status bar says "Run finished: 0 node(s) executed" and the Watch is unchanged, because nothing in the graph changed ([Troubleshooting](TROUBLESHOOTING.md#nothing-happens-when-i-press-run)). **Confirm.** | |
| 4.4.5 | `Selection.SetCurrent` with the search result. | Those items are selected in the 3D view. | |
| 4.4.6 | **Captured Selection**: press Capture with 3 items selected, change the selection, press **Run**. Then Clear. | The node keeps outputting the 3 captured items until cleared. | |
| 4.4.7 | `Properties.ValueAsString` for one item; `Properties.ToTable` for the search result into a **Watch Table**; `Properties.Discover`. | The value equals the Properties window. One table row per item. Discover lists the properties with counts. | |
| 4.4.8 | `Properties.SetCustom` (tab `CamelGraphQA`), then `Properties.RemoveCustomTab`. | The tab appears in the Navisworks Properties window with the values, then disappears. | |

### 4.5 Appearance

| # | Step | Expected | Result |
|---|---|---|---|
| 4.5.1 | Search result into `Appearance.OverrideColor` (red). | The items turn red. | |
| 4.5.2 | `Appearance.Reset` on the same items. | They return to their own colours. | |
| 4.5.3 | `Appearance.Hide`, then `Appearance.ShowAll`. | The items hide; then everything is visible. | |
| 4.5.4 | `Appearance.Isolate` then `Appearance.ShowAll`. | Only those items show; then everything. | |
| 4.5.5 | `Appearance.OverrideColorTemporary`, then `Appearance.ResetTemporary`. Then `Appearance.OverrideTransparency` (0.5), then `Appearance.Reset`. | Each effect shows, then clears. | |
| 4.5.6 | `Appearance.Focus` on a few items (others fade by `otherTransparency`), then `Appearance.ResetTemporary`. | Everything else fades, then returns *(intended)*. | |

### 4.6 Selection sets, viewpoints and camera

| # | Step | Expected | Result |
|---|---|---|---|
| 4.6.1 | `SelectionSet.Create` with the name `DYC QA set`, then `SelectionSet.Delete`. | The set appears in the **Sets** window with the right item count, then is removed. | |
| 4.6.2 | `SelectionSets.BulkByPropertyValues` (category `Element`, property `Level`, a folder name). Run it again. | One search set per distinct value, in the folder. A second run replaces the same-named sets rather than adding more. | |
| 4.6.3 | `Viewpoint.SaveCurrent` named `DYC QA view`. Move the camera. `SavedViewpoint.Apply`. | The viewpoint appears in **Saved Viewpoints**; Apply returns to it. | |
| 4.6.4 | `Camera.ZoomToItems`, `Camera.SetProjection` (orthographic and perspective), `Camera.SetFieldOfView`. | The view changes as named. | |
| 4.6.5 | `Viewpoint.SetSectionBox` from `ModelItem.BoundingBox`; then `enabled` off. | A section box is applied, then removed. | |
| 4.6.6 | `Viewpoints.CreateFolder`, `Viewpoint.SaveWithOverrides` into it, `Viewpoints.InFolder`. | The folder holds the viewpoint; InFolder lists it. | |
| 4.6.7 | `Viewpoints.ExportFile` and `Viewpoints.ImportFile` with a full path. | A `.json` package is written; import rebuilds the viewpoints in the same or another model. | |

### 4.7 Clash

Use the model with the clash test that has results.

| # | Step | Expected | Result |
|---|---|---|---|
| 4.7.1 | `Clash.Tests`, then `ClashTest.Results` for one test, into `List.Count`. | The count equals the result count in Clash Detective. | |
| 4.7.2 | `Clash.GroupResultsByStatus` for one test. | Groups appear in Clash Detective, one per status. | |
| 4.7.3 | `Export.ClashReportCsv` with a full path. | The CSV exists and opens in Excel. | |
| 4.7.4 | `BCF.ExportIssues` with the results, a full path ending `.bcfzip`. | The file exists and opens as a zip. **Unknown:** does a BCF viewer read it? | |
| 4.7.5 | `ClashResult.Focus` on one result. | The view isolates and frames the result's items. | |

### 4.8 TimeLiner, transforms, grids, markups

| # | Step | Expected | Result |
|---|---|---|---|
| 4.8.1 | `TimeLiner.Tasks` into a Watch List. | The tasks match the TimeLiner window (skip if the model has none). | |
| 4.8.2 | `ModelItem.Translate` by a vector, then `ModelItem.ResetTransform`. | The items move, then return. Running Translate twice moves them twice (re-runs accumulate). | |
| 4.8.3 | `Grids.Levels` (skip if the model has no grids). | Level names and elevations match the model. | |
| 4.8.4 | `Markup.AddText` on a saved viewpoint (**experimental**). | **Unknown:** a text redline appears on that viewpoint, or an error. Record which. | |

### 4.9 Exports

| # | Step | Expected | Result |
|---|---|---|---|
| 4.9.1 | `Export.ToCsv` with the search result and a full path. | A CSV with one row per item. | |
| 4.9.2 | `Export.ViewpointImage` with a full path ending `.png`. | An image of the current view is written. | |
| 4.9.3 | `Export.ToIfc` for a few items, full path. | A non-empty `.ifc` file. `fileCount` and `elementCount` are shown. | |
| 4.9.4 | `Export.NWD` with a full path. | A `.nwd` file that opens in Navisworks. | |

### 4.10 Workflow, loops and order

| # | Step | Expected | Result |
|---|---|---|---|
| 4.10.1 | `Flow.When` with condition false, nodes after it. | Those nodes are idle, **not red**, with "Skipped: ... (Flow.When was false)". With true: they run. | |
| 4.10.2 | `Flow.Try` after a node that fails (for example `Text.ReadFromFile` on a path that does not exist). | `failed` true, `error` holds the text, the fallback is the result, and nothing after it turns red. | |
| 4.10.3 | `Flow.Then`: set a section box, then save a viewpoint. | The saved viewpoint has the section box. | |
| 4.10.4 | `Loop.Item` / `Loop.Collect` around `Appearance.Isolate`, `Camera.ZoomToItems`, `Viewpoint.SaveWithOverrides` for 5 items. | 5 viewpoints, each framed on its item. | |
| 4.10.5 | `Workflow.ForEach` with `Action.*` nodes (or the sample *Isolated Viewpoints per Item*). | One viewpoint per item. | |
| 4.10.6 | Press `Esc` during a long loop. | The run stops before the next item. The status bar says "Run cancelled after ... node(s)". The next **Run** continues. | |

## 5. Self-test and diagnostics

New in this release. Open the small model first.

| # | Step | Expected | Result |
|---|---|---|---|
| 5.1 | **Help > Run Self-Test**. | A battery of read-only Navisworks nodes runs against the open model. Each node shows pass or fail. | |
| 5.2 | Copy the report and paste it into your results file. **Attach it to the results.** | The report can be copied. List every failed node; ask whether the model explains it (no clash tests, no TimeLiner tasks, no grids). | |
| 5.3 | After the self-test, look at the model. | Nothing changed: no overrides, hidden items, new sets, viewpoints or properties. (The nodes are read-only.) | |
| 5.4 | Run the self-test twice. | The same result both times. | |
| 5.5 | Run the self-test with no model open. | The checks that need a model show "skipped: open a model first"; the others still run. Navisworks stays open. | |
| 5.6 | **Help > Copy Diagnostics**, paste into a text editor. | The CamelGraph version, the Navisworks version, the installed Navisworks plug-ins and the end of `errors.log`. The versions match **About** and the Navisworks year. | |
| 5.7 | Rename `%APPDATA%\CamelGraph\errors.log` and run **Copy Diagnostics** again. | **Unknown:** it should still copy the versions. Record what it does about the missing log. (Rename it back.) | |
| 5.8 | Open the Performance HUD with `Ctrl+Shift+F12` and click **Copy report**. | **Unknown:** the HUD appears over the canvas; the report holds frame rate, visuals, node count and render tier. | |

## 6. Questions before a graph runs

| # | Step | Expected | Result |
|---|---|---|---|
| 6.1 | Make a graph with `System.Run` (`cmd.exe`, `/c echo hello`), saved in **Manual** run mode. Close and open it as a file. | Opening does not run it. | |
| 6.2 | Press **Run**. Answer **no**. | A warning names the program-running node; nothing runs. | |
| 6.3 | Press **Run** again, trust the file. | It runs. Close and open the same file, press **Run**: no warning ("trust this file until it changes"). | |
| 6.4 | Edit the file (change a value and save). Open it again and press **Run**. | The warning is back. | |
| 6.5 | Repeat 6.2 with `Web.Get`, `File.Delete` and `File.Move`. | The warning names each of them. | |
| 6.6 | Save the same graph in **Automatic** run mode and open it from the file (new trust: rename the file first). | Nothing runs on opening and no dialog appears; the status bar says "Not run automatically: this graph came from a file …". Press **Run**: the warning appears. | |
| 6.7 | Open *Getting Started - Math and Watch*. | No warning (it has no such nodes). | |
| 6.8 | Put a graph with `Appearance.OverrideColor` in `Documents\CamelGraph\Scripts`. Open the Player, pick it. | The Player says "Changes the model, writes files, runs programs or uses the network: Appearance.OverrideColor" above the form. | |
| 6.9 | Run it. | A question lists the node; after **yes** it runs. Run it again: no question. Edit and save the file: the question is back. | |
| 6.10 | A script whose only risky node is `Web.Get`. | The Player lists `Web.Get` above the form and asks the first time (it uses the network). | |
| 6.11 | A script with a node that is not installed (rename a node's id in the file). | The Player says nodes are missing and does not run it. | |

## 7. Sample graphs

Open each from **File > Sample Graphs**. For every sample: it opens with no "could not be restored" message and no red "Unresolved" node. Clean up afterwards (reset overrides; delete the sets and viewpoints it made). Details of each sample are in [samples/README.md](../samples/README.md).

| # | Sample | Needs | Expected | Result |
|---|---|---|---|---|
| 7.1 | Getting Started - Math and Watch | nothing | Runs by itself (Automatic). Watch shows **32** and **Area = 32**. | |
| 7.2 | Table Summary from Text | nothing | Runs by itself (Automatic). First row of the table **Wall, 3, 10000, 4500**; a Markdown version below. | |
| 7.3 | Color Elements by Property | a model | Default search is Item > Name contains "Wall". Matching items are painted; **Items colored** equals the count. Reset the overrides after. | |
| 7.4 | Export Properties to Excel | a model; set the output to a full path | An `.xlsx` is written: item names across the first row, one row per property. Excel opens it. | |
| 7.5 | Bulk Selection Sets from Values | a model with Element > Level | One search set per level in a folder. **Sets created** matches. | |
| 7.6 | Clash Triage and BCF Export | a model with clash tests run | Results grouped by status; a `.bcfzip` and a CSV are written (use full paths). | |
| 7.7 | QTO Rollup by Category | a model with Element > Volume | One row per category with sum and count; the `.xlsx` opens. | |
| 7.8 | Floor Opening Fall-Hazard Map | a model with floor and equipment | A heat-map PNG is written and viewpoints appear in a "Floor Openings" folder. The diagnostic report text explains a zero result. **Unknown:** counts and run time. | |
| 7.9 | Floor Openings Needing Handrails | a model for part 1; part 2 runs alone | Part 2 reports **0.8**. Part 1 flags equipment with no handrail within range. | |
| 7.10 | Isolated Viewpoints per Item | a model | One saved viewpoint per found item, named from the item, framed on it. | |
| 7.11 | Spotlight Viewpoints per Item | a model | One viewpoint per item: the item in colour, the rest ghosted. Each view keeps its own colours. | |
| 7.12 | Isolated Viewpoints (Loop) | a model | The same outcome as 7.10, built from ordinary nodes. | |
| 7.13 | Section Box Viewpoints per Group | a selection in a model | A cluster report, then one viewpoint per group in a "Group Views" folder, each with a section box. | |
| 7.14 | Clash Group Viewpoints per Test | clash tests with groups | A folder per test, a viewpoint per group; first elements red, second green. | |

## 8. Modify nodes and Navisworks Undo

**The answer is unknown.** The node descriptions call permanent overrides "undoable", and [Getting Started](GETTING_STARTED.md) says one **Undo** reverts a whole run. The source has no code that groups a run into one undo step, and the editor's own Undo only changes the graph. This section finds out.

Use Navisworks's own Undo: `Ctrl+Z` with the **3D view** focused (not the CamelGraph pane, which takes `Ctrl+Z` for itself), or an Undo button on the Quick Access Toolbar if you have one. For each row: run the node once on 5 items, press Undo, and write down **how many presses** it took and **what remained**.

| # | Node run once | Expected | Result |
|---|---|---|---|
| 8.1 | `Appearance.OverrideColor` | **Unknown:** presses to revert; is the colour gone? | |
| 8.2 | `Appearance.OverrideColorTemporary` | **Unknown** | |
| 8.3 | `Appearance.Hide` | **Unknown** | |
| 8.4 | `Appearance.Isolate` | **Unknown** | |
| 8.5 | `SelectionSet.Create` | **Unknown:** is the set removed? | |
| 8.6 | `Viewpoint.SaveCurrent` | **Unknown:** is the viewpoint removed? | |
| 8.7 | `Properties.SetCustom` | **Unknown:** is the property tab removed? | |
| 8.8 | `ModelItem.Translate` | **Unknown** (described as undoable) | |
| 8.9 | `ClashTest.Delete` | **Unknown** (described as "Undoable in Navisworks"): is the test back with its results? | |
| 8.10 | Run a graph of three of the nodes above, then Undo once. | **Unknown:** does one press undo the whole run, or only the last step? | |
| 8.11 | After a run, press `Ctrl+Z` in the CamelGraph pane. | The graph changes, the model does not, and the status bar says "(graph only — Navisworks changes from earlier runs are not reverted)". | |

## 9. Save, reopen and recover

| # | Step | Expected | Result |
|---|---|---|---|
| 9.1 | Build a graph with a few nodes and run it. **File > Save As** a `.dyc`. | The title loses the `*`. | |
| 9.2 | Close Navisworks, start it, open the editor, **File > Open** the file, press **Run**. | The same nodes, values and results. | |
| 9.3 | Open a graph saved in 2024 in 2026 (and one saved in 2026 in 2024). | It opens with no warning and runs. | |
| 9.4 | **File > Recent Files**. Drag a `.dyc` from Explorer onto the canvas. | Recent lists the file. **Unknown:** does drag and drop from Explorer work inside Navisworks? | |
| 9.5 | Edit a graph, do not save, choose **New**. | A Save / Don't Save / Cancel question. | |
| 9.6 | Edit a graph, wait more than a minute, then end `Roamer.exe` in Task Manager. Start Navisworks, open the editor. | The autosaved graph is offered. | |
| 9.7 | Save a graph that contains a node group. Close and open it. | The group is intact. | |
| 9.8 | In the Player, run a script after changing a value. Restart Navisworks, pick the script. | The value you typed is remembered. | |

## 10. Large model

Use the large model. There is no time limit to meet yet. The numbers are for comparing releases. After every run the **status bar** shows the time ("Run finished: N node(s) executed in X ms") and, when a run takes a second or more, the slowest node.

| # | Step | Expected | Result |
|---|---|---|---|
| 10.1 | Open the model. Write down its size on disk, the number of files, and the Navisworks memory use. | Navisworks opens it as without CamelGraph. | |
| 10.2 | `Model.Statistics`, nothing wired, `by` = `model`, then `class`, then `layer`. | A table of counts. **Record the three times.** (It walks every item once: O(items).) | |
| 10.3 | A broad `Search.ByProperty` into `List.Count`. | **Record** count and time. | |
| 10.4 | `Properties.ToTable` for 1,000 items (use `List.TakeItems`). | **Record** the time. | |
| 10.5 | `Appearance.OverrideColor` on the search result, then `Appearance.Reset`. | **Record** both times. No freeze. | |
| 10.6 | `Search.ByGuid` with 100 GUIDs. | **Record** the time. It should be one pass, not 100 searches *(intended)*. | |
| 10.7 | `SelectionSets.BulkByPropertyValues` on a property with many values. | **Record** the time and number of sets. | |
| 10.8 | Press `Esc` during a long run. | **Record** how long it takes to stop. The next **Run** continues. | |
| 10.9 | Run the same graph three times (change a value to force a re-run). Watch Navisworks memory. | **Record** the memory before and after. It should not grow without limit. | |
| 10.10 | On a graph with 100 or more nodes, open the Performance HUD (`Ctrl+Shift+F12`). | **Unknown:** record the frame rate and **Copy report** text. | |
| 10.11 | Throughout: does Navisworks stay open? | It does. Any crash is a blocker. | |

## 11. The Navisworks side of 0.45

These nodes were only compiled and unit-tested before. The list is from "For the Navisworks side" in [WHATS_NEW_0.45.md](WHATS_NEW_0.45.md#for-the-navisworks-side). Rows marked **new behaviour** rely on Navisworks behaviour that was not seen in the code before, so try them first. *(intended)* means taken from the node's description.

| # | Node | Try this | Expected | Result |
|---|---|---|---|---|
| 11.1 | `IFC.GuidEncode`, `IFC.GuidDecode` | Encode `3f81e10a-25b0-49ff-9520-63f2a763150a`; decode `0$WU4A9R19$vKWO$AdOnKA`; round-trip both. | A 22-character id; a lower-case hyphenated GUID; the round trip returns the input *(intended)*. | |
| 11.2 | `ModelItem.IfcGuid` — **new behaviour** | On an item from an **IFC import**, and on an item from another format. | IFC item: the value of its `GlobalId` / `IfcGUID` / `IFC GUID` / `Guid` property. Other item: the instance GUID, encoded. **Record which property names an IFC import really creates.** | |
| 11.3 | `Search.ByGuid` | Three GUIDs of known items (text), one made-up GUID, one 22-character IFC id. | `items` holds the known ones; `missing` lists the made-up one *(intended)*. | |
| 11.4 | `Model.Snapshot` with `Snapshot.Diff` | Snapshot some items; change a property (for example with `Properties.SetCustom`); snapshot again; diff. | The diff shows the changed item. Nothing in the model changes *(intended)*. | |
| 11.5 | `Model.Statistics` by `layer` — **new behaviour** | By `model`, `class`, `layer`. | Counts agree with the model tree. **Record** what `layer` returns for formats without layers. | |
| 11.6 | `Selection.Invert` | Select a few items, run it into `List.Count`. | The items that are not selected; the selection itself is unchanged. Wire it into `Selection.SetCurrent` to select them. | |
| 11.7 | `Selection.Remove` — **new behaviour** | Select a parent and a child; remove the child; then remove a selected item. | A child of a selected parent is ignored; a selected item leaves the selection; the output lists what is still selected *(intended)*. | |
| 11.8 | `SelectionSet.Info` | A fixed set and a search set. | Name, kind ("selection" or "search"), item count, folder path *(intended)*. | |
| 11.9 | `SelectionSet.Duplicate` — **new behaviour** | A search set, then a fixed set. Run twice. | A copy named "<name> copy" in the same folder; the search set copy stays a live search; the second run adds another copy *(intended)*. | |
| 11.10 | `ClashTest.Edit` — **new behaviour** | Change only the name; then only the tolerance. | Only the named field changes. The old results are kept but are stale *(intended)*. | |
| 11.11 | `ClashTest.Duplicate` | Duplicate a test, then run it with `ClashTest.Run`. | "<name> copy", un-run until you run it *(intended)*. | |
| 11.12 | `ClashTest.ClearResults` — **new behaviour** | On the duplicate. | Results and groups are gone; the test and its settings stay *(intended)*. | |
| 11.13 | `ClashTest.Delete` — **new behaviour** | On the duplicate; then run it again. | The test is removed and the node returns true; the second run returns false *(intended)*. Also try Undo (row 8.9). | |
| 11.14 | `SavedViewpoint.Info` (section on 2025+) | A viewpoint with a section box; a viewpoint with isolation. | Name, folder path, comment count, camera position and look-at, `hasOverrides`. `hasSection` is **empty on 2025 and 2026** *(intended)*; on 2024 it reports. **Record both.** | |
| 11.15 | `SavedViewpoint.Update` — **new behaviour** | Move the camera, then update a viewpoint. | The viewpoint now has the new view and keeps its name, folder and position in the list *(intended)*. | |
| 11.16 | `Camera.SetStandardView` — **new behaviour** | `top`, `front`, `left`, `iso`, with and without items. | Z is up and +Y is north: top is a plan with north at the top of the screen. **Record** the result on a model whose north is not +Y. | |
| 11.17 | `Appearance.Focus` — **new behaviour** | Focus on a few items with `otherTransparency` 80; then `Appearance.ResetTemporary`. | Others fade; reset brings them back; no permanent change *(intended)*. On the large model: allow a moment. | |
| 11.18 | `TimelinerTask.SetProgress` | 50 on a task; then 150. | The TimeLiner window shows 50 percent; 150 is clamped to 100 *(intended)*. | |
| 11.19 | `TimelinerTask.SetActual` — **new behaviour** | Set actual start and end dates. | Actual dates change; planned dates do not *(intended)*. | |
| 11.20 | `TimelinerTask.Delete` | A task with a sub-task; run twice. | The task and its sub-tasks are removed; the second run returns false *(intended)*. | |
| 11.21 | `ModelItem.Scale` — **new behaviour** | Factor 2 about the default point; run again. | The group grows in place. A second run scales again *(intended)*. `ModelItem.ResetTransform` restores. | |
| 11.22 | `ModelItem.MoveTo` — **new behaviour** | Move to a target point; run again. | The group's centre lands on the target; a second run does nothing *(intended)*. | |

---

## Results template

Paste into an issue or the release checklist. One copy per Navisworks year.

````text
CamelGraph QA result
-------------------
CamelGraph version:
Date:
Tester:
Navisworks: [Manage | Simulate] [2024 | 2025 | 2026]  (update/build: )
Windows version:
Small model:
Large model: (size on disk, files, items from Model.Statistics)

Section results (P = all pass, F = failures, - = not done). List the row numbers that failed.
 1 Install/upgrade/uninstall:
 2 Panes:
 3 Library, canvas, failure drills:
 4 Nodes by category:
 5 Self-test and diagnostics:
 6 Questions before a graph runs:
 7 Sample graphs:
 8 Modify nodes and Navisworks Undo:
 9 Save, reopen, recover:
10 Large model:
11 Navisworks side of 0.45:

Crashes of Navisworks (row, what you did): none | ...

Answers to the "Unknown" rows:
 1.8  install while Navisworks runs:
 2.6  graph after hiding and re-opening the pane:
 2.9  pane placement after restart:
 3.10 100-node canvas:
 3.11 Run with no document:
 4.3.7 relative path:
 4.7.4 BCF viewer:
 4.8.4 markup:
 5.5  self-test with no model:
 5.8 and 10.10 Performance HUD:
 6.10 script with only Web.Get:
 7.8  fall-hazard sample:
 8.1 to 8.10 Navisworks Undo (presses needed, what remained):
 9.4  drag and drop of a .dyc:
 11.2 IFC property names:
 11.14 SavedViewpoint.Info hasSection on this year:
 11.16 Camera.SetStandardView on a north-not-+Y model:

Large-model timings (ms): Model.Statistics model/class/layer = / / ; Search.ByProperty = ; Properties.ToTable(1000) = ;
  OverrideColor = ; Reset = ; Search.ByGuid(100) = ; BulkByPropertyValues = ; Esc to stop = ; memory before/after = / .

Attached: self-test report (5.2) | Help > Copy Diagnostics (5.6) | end of errors.log | graphs that failed
````
