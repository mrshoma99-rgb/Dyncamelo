# Node library review by role, and gaps (v0.44)

The library as it ships: **357 nodes** ([catalogue](../NODE_CATALOG.md)). This review walks it as seven kinds of user, then looks at the general-purpose half (lists, math, logic, inputs, strings …) for what is simply missing.

How it was checked: every node named as *existing* below is looked up in the generated catalogue by a script (a name that is not there fails the check), and every *proposed* node is marked with a leading `+` and checked **not** to exist yet, so nothing is proposed twice. "Works today" means a chain of existing nodes does the job, not that the job is one node.

**Priority** — H: this role hits it in its normal week; M: regularly; L: occasionally.
**Feasibility** — *pure*: plain .NET, fully testable on a build agent; *API*: documented Navisworks .NET API (compiles in CI, only runs in Navisworks); *COM*: needs the Navisworks COM interop (the mesh-based `Distance.BetweenItems` already does); *verify*: version-dependent or I am not sure the API allows it — needs a probe in Navisworks before any promise.

---

## 1. BIM coordinator — weekly clash cycle

| Workflow | Works today with | Missing |
|---|---|---|
| Run the matrix, re-run weekly | `Clash.Tests`, `Clash.RunAllTests`, `ClashTest.Run` | — |
| Build the matrix from sets | `SelectionSets.All`, `ClashTest.Create` with cross-product lacing (works, not obvious — worth a sample) | — |
| Triage by rule | `Clash.FilterByStatus`, `Clash.FilterByAngle`, `Clash.FilterByDepth`, `Clash.FilterByOrientation`, `Clash.FilterByItemProperty`, `Clash.FilterBySet`, `Clash.Deduplicate`, `ClashResult.SetStatus`, `ClashResult.Assign`, `ClashResult.SetDescription` | **H** edit an existing test (tolerance, type, the two selections), delete, duplicate, clear its results: `+ClashTest.Edit`, `+ClashTest.Delete`, `+ClashTest.Duplicate`, `+ClashTest.ClearResults` — *API* (`TestsData.TestsEdit*` family is already used for rename and comments) |
| Group for the meeting | `Clash.GroupResults`, `Clash.GroupResultsByLevel`, `Clash.GroupResultsByGridIntersection`, `Clash.GroupResultsByProximity`, `Clash.GroupResultsBySameItem`, `Clash.GroupResultsByStatus` | — |
| Report | `Clash.SummaryTable`, `Export.ClashReportCsv`, `Export.ClashReportHtml`, `ClashResult.SaveImage`, `Viewpoints.FromClashResults`, `BCF.ExportIssues` | **M** a clash age filter (`ClashResult.Info` has the created time, but dates cannot be compared: see §9 DateTime) |
| Compare with last week | `Clash.SnapshotToFile`, `Clash.CompareSnapshots` | **H** append a row to a history file instead of overwriting: `+CSV.AppendToFile`, `+Text.AppendToFile` — *pure* |
| Tell people | — | **M** `+Web.Post` (Teams/Slack webhook), mail — *pure* |

Library-shape finding: **`Navisworks.Clash` is one flat list of 45 nodes**, the longest in the library. Category is free to change (it is not part of a node's identity), so split it: `Navisworks.Clash.Tests`, `.Results`, `.Filter`, `.Group`, `.Report`. Same for `Navisworks.Viewpoints` (22) and `List` (43).

## 2. BIM manager — standards, health and KPIs

| Workflow | Works today with | Missing |
|---|---|---|
| Data completeness | `Audit.MissingProperty`, `Search.HasProperty`, `Properties.HasProperty`, `Audit.DuplicateItems` | — |
| Naming / value rules | `Search.ByProperty` (equals, contains, wildcard, compare) | **H** regular expressions: `+String.RegexIsMatch`, `+String.RegexMatch`, `+String.RegexReplace` — *pure* |
| Rules kept in Excel | `Excel.ReadFromFile`, `Table.JoinByKey` | **H** a table toolkit (§6) and a rule runner: `+Audit.CheckRules` (rows of category / property / expected pattern → failing items with the reason) — *API* |
| KPIs and percentages | `List.Count`, `List.CountTrue`, `Takeoff.SumPropertyByGroup`, `List.GroupByKey` | **H** `+List.Sum`, `+List.Average`, `+Math.Percent` (§9) |
| Visual QA | `Appearance.ColorByValues`, `Color.ByValues` | **L** colour-blind-safe named palettes: `+Color.Palette` — *pure* |
| Model health | `Model.Info`, `Document.Info`, `Models.RootItems` | **M** `+Model.Statistics` (item / geometry counts by class, layer or model), `+File.Info` (size, modified) — *API* / *pure* |
| Track change over time | `Snapshot.Diff`, `JSON.WriteToFile`, `JSON.ReadFromFile` | **H** nothing *produces* the keyed dictionaries `Snapshot.Diff` compares: `+Model.Snapshot(items, properties)` (GUID → property values) — *API* |
| Publish a report | `Excel.WriteToFile`, `CSV.WriteToFile`, `Export.ClashReportHtml` (clash only) | **M** a generic table → HTML / Markdown report: `+Report.Html`; Excel number formats and header style — *pure* |

## 3. Model maintainer / compiler

| Workflow | Works today with | Missing |
|---|---|---|
| Find the files to append | `Directory.GetFiles`, `File.Exists`, `Path.Combine` | **H** `+Path.GetFileName`, `+Path.GetExtension`, `+Path.GetDirectory`, `+Path.ChangeExtension`; `+File.Info`; `+Directory.GetDirectories`; recursive listing; "newest file per pattern" — *pure* |
| Housekeeping on disk | — | **M** `+File.Copy`, `+File.Move`, `+File.Delete`, `+Directory.Create`, `+Directory.Exists` — *pure* (file-changing, so they should ask in the Player like model-changing nodes) |
| Assemble the federation | `Document.Open`, `Document.AppendFiles`, `Document.Refresh`, `Document.Merge`, `Document.Save`, `Export.NWD` | **M** one model at a time: `+Model.Refresh`, `+Model.Replace`; append options (units, origin) — *verify* |
| Check and align | `Model.Info`, `Models.RootItems`, `ModelItem.SetTransform`, `ModelItem.Translate`, `ModelItem.RotateAboutAxis`, `ModelItem.GetTransform`, `ModelItem.ResetTransform` | **M** `+ModelItem.Scale`, `+ModelItem.MoveTo`, a transform builder from translate / rotate / scale — *API* |
| Keep sets and views tidy | `SelectionSets.*`, `SelectionSet.CreateFromSearch` (equals only), `Viewpoints.CreateFolder`, `Viewpoints.SortFolder`, `SavedViewpoint.Duplicate`, `Viewpoint.SaveCurrent` (replaces by name) | **H** live search sets with the other match modes and several conditions: extend `SelectionSet.CreateFromSearch` (it exists; add the modes); **M** `+SelectionSet.Info` (kind, item count), `+SelectionSet.Duplicate`, `+SavedViewpoint.Info` (camera, section, overrides, comment count), `+SavedViewpoint.Update` — *API* |
| Log what happened | `Text.WriteToFile`, `DateTime.Now`, `DateTime.Format` | **H** append-to-log (see §1) and a timestamped `+Log.Write` — *pure* |
| Publish | `Export.NWD`, `Document.Save` | **L** publish options (password, expiry, embedded data), save as an older version — *verify* |

## 4. Automation and bulk actions

| Workflow | Works today with | Missing |
|---|---|---|
| Loop over items / views | `Workflow.ForEach`, `Loop.Item`, `Loop.Collect`, `Flow.Then`, nine `Action.*` nodes, lacing | **M** actions are limited to appearance / view steps; no generic "do this node for each file" |
| Re-run by name or button | Script Player, `Dyncamelo.Run.DYNC` plugin | **H** the batch runner over many NWD / NWF files (host feature, already planned) |
| Skip, branch, recover | `If`, `IsNull`, `IsNullOrEmpty` | **H** `+Flow.When` (run downstream only if true), `+Flow.Try` (result or error text instead of a failed node) — engine semantics, *pure* |
| Pause, time, identify | `DateTime.Now`, `Application.Version` | **M** `+Flow.Wait`, `+Flow.Stopwatch`, `+System.Environment` (user, machine) — *pure* |
| Talk to other tools | — | **M** `+Web.Get`, `+Web.Post` (JSON in / out, header for a token); `+System.Run` (exe + arguments, captured output — must be a "changes things" node that the Player confirms) — *pure* |
| Ask the person | — | **M** `+Dialog.Message`, `+Dialog.Confirm`, `+Dialog.Prompt` for Player scripts — *pure* |
| Pick many files | `File Path` (one), `Directory.GetFiles` | **M** a file-list input (several files or folder + pattern) |

## 5. Open BIM integration

| Workflow | Works today with | Missing |
|---|---|---|
| IFC out | `Export.ToIfc`, `Export.IfcCoordinates`, `Export.IfcRoles`, `Export.IfcSpatialNames`, `Export.IfcParameterRule`, `Export.IfcSetClassMap`, `Export.IfcClasses` | — |
| IFC in | `Document.AppendFiles` (Navisworks reads IFC), `Properties.InCategory` (property sets are categories) | — |
| IFC GlobalId ⇄ Navisworks GUID | `ModelItem.InstanceGuid` | **H, cheapest win in this document:** the codec already exists inside the BCF nodes but is not a node: `+IFC.GuidEncode`, `+IFC.GuidDecode`, `+ModelItem.IfcGuid` — *pure* / *API* |
| BCF | `BCF.ExportIssues`, `BCF.ImportIssues` | **M** BCF 3.0; topics not tied to a clash result (priority, labels, due date, assignee): `+BCF.CreateTopic`; apply imported topics back onto clash results: `+BCF.ApplyToClashResults` — *API* |
| Find items by GUID | `Search.ByProperty` on the Item / GUID property, laced | **M** `+Search.ByGuid` (a list in, items out) — *API* |
| Requirements (IDS-style) | `Audit.MissingProperty`, `Search.ByProperty` | **H** `+Audit.CheckRules` (§2); a real IDS validator later — *API* / large |
| COBie, classification tables | `Excel.WriteToFile` (several sheets), `Table.JoinByKey`, `Properties.SetCustom` | **H** the table toolkit (§6) |
| Other systems | `JSON.*`, `XML.Parse`, `CSV.*` | **M** `+Web.Get` / `+Web.Post` (§4); **L** SQL |

## 6. Model data and quantity analysis

| Workflow | Works today with | Missing |
|---|---|---|
| Read properties | `Properties.Value`, `Properties.ValueAsString`, `Properties.InCategory`, `Properties.AsDictionary`, `Properties.Categories`, `Property.Info` | **H** what properties does this model *have*? `Properties.Categories` is per item only. `+Properties.Discover(items)` → every category / property with counts and sample values. This is the biggest usability hole for data work: category and property names are typed by hand today — *API* |
| Many items × many properties → table | `Export.ToCsv` (CSV only) | **H** `+Properties.ToTable(items, properties)` → rows + headers, ready for the next node — *API* |
| Roll up | `Takeoff.SumPropertyByGroup`, `List.GroupByKey`, `List.UniqueItems`, `SelectionSets.BulkByPropertyValues` | **H** a general group-by with several aggregations: `+Takeoff.GroupBy` (count / sum / average / min / max per group) — *API* |
| Work on the table | `Table.JoinByKey`, `List.Transpose`, `List.SortByKey`, `Dictionary.*` | **H** `+Table.FromRows`, `+Table.Column`, `+Table.AddColumn`, `+Table.SelectColumns`, `+Table.Filter`, `+Table.Sort`, `+Table.GroupBy`, `+Table.Pivot`, `+Table.Concat`, `+Table.Distinct`, `+Table.ToDictionaries` — *pure*, and testable here |
| See a table | `Watch`, `Watch List` | **H** `+Watch Table` (rows × headers grid) — a list of lists is unreadable in `Watch List` — UI |
| Geometry quantities | `ModelItem.BoundingBox`, `BoundingBox.Size`, `ModelItem.CombinedBoundingBox`, `Zone.AssignByVolumes` | **M** `+BoundingBox.Volume`, `+ModelItem.MeshVolume`, `+ModelItem.SurfaceArea` (closed-mesh assumption, said in the description) — *COM* |
| Units | `Units.Convert`, `Units.ScaleFactor`, `Units.Current`, `Model.Info` | — |
| Look at distributions | `List.GroupByKey` + `List.Count`, `Appearance.ColorByValues` | **M** `+List.CountBy` (value → count), `+List.Histogram`; **L** a chart that draws to an image for `Watch Image` |
| Compare two states | `Snapshot.Diff` | **H** `+Model.Snapshot` (§2) |
| Excel finish | `Excel.WriteToFile` | **M** number formats, header style, column widths, more than one table per sheet |

## 7. Model manipulation

| Workflow | Works today with | Missing |
|---|---|---|
| Select | `Selection.Current`, `Captured Selection`, `Selection.SetCurrent`, `Selection.AddToCurrent`, `Selection.Clear`, `Selection.SelectAll`, `Selection.Resolve`, `List.SetDifference`, `List.SetUnion` | **M** `+Selection.Invert`, `+Selection.Remove` (subtracting through the full item list works but is slow on big models) — *API* |
| Show, hide, colour | `Appearance.Hide`, `Appearance.Show`, `Appearance.Isolate`, `Appearance.OverrideColor`, `Appearance.OverrideTransparency`, `Appearance.ColorByValues`, the temporary variants, `Appearance.Reset`, `Appearance.ResetAll`, `Appearance.ShowAll`, `ModelItem.IsHidden` | **M** one node for "focus: these items normal, everything else ghosted" (exists only as `Action.Ghost` inside a workflow): `+Appearance.Focus` — *API* |
| Move | `ModelItem.Translate`, `ModelItem.RotateAboutAxis`, `ModelItem.SetTransform` | **M** scale / move-to / transform builder (§3) |
| Cameras and views | `Camera.Current`, `Camera.LookAt`, `Camera.ZoomToItems`, `Camera.SetFieldOfView`, `Camera.SetProjection`, `Viewpoint.SaveCurrent`, `Viewpoint.SaveWithOverrides`, `SavedViewpoint.Apply` | **M** standard views (top / front / iso): `+Camera.SetStandardView`; read a saved view's camera (§3) — *API* |
| Section | `Viewpoint.SetSectionBox` | **H** it works on Navisworks 2024 only; plan views per level need section planes: `+Viewpoint.SetSectionPlane` — *verify* (the 2025+ clip-plane API changed) |
| 4D | `TimeLiner.Tasks`, `TimelinerTask.Create`, `TimelinerTask.SetDates`, `TimelinerTask.AttachSet`, `TimelinerTask.Items`, `TimelinerTask.Info`, `TimeLiner.AutoAttachByProperty` | **M** `+TimelinerTask.SetProgress`, `+TimelinerTask.SetActual`, `+TimelinerTask.Delete`, sub-tasks, task types — *API* |
| Mark up | `Markup.AddShape`, `Markup.AddText`, `Markup.AddCloud`, `Markup.AddNumberTag`, `Markup.List`, `Markup.Clear`, `SavedItem.AddComment` | (marked experimental already) |

---

## 8. Inputs

Seven input nodes: `Boolean`, `Number`, `Integer Slider`, `Number Slider`, `String`, `File Path`, `Directory Path` (plus `Color Picker` under Color and `Captured Selection`). Missing:

| Input | Why | Priority |
|---|---|---|
| Date | reports, "since" filters | **M** |
| Choice (dropdown with the author's own options) | scripts for the Player: "Discipline: Arch / MEP / Struct" | **H** |
| Integer (no slider) | counts, indexes | **M** |
| Multi-line text | notes, regex lists, one item per line | **M** |
| Point / vector (x, y, z) | the editor already has the vector field; no node uses it standalone | **L** |
| File list | batch and compile scripts | **M** |
| **Pick from the document**: a selection set, a saved viewpoint, a clash test, a property category / property, a model | every `…ByName` node and every category / property input is typed by hand today; one typo and the node fails. The inline editors need a dynamic list from the open document (a small host interface, then `[NodeChoicesFrom…]` on the parameter) | **H** |

## 9. The general-purpose half

Everything below is *pure* .NET unless noted, so it can be built and tested without Navisworks.

**Math** (15 nodes: `Add`, `Subtract`, `Multiply`, `Divide`, `Modulo`, `Math.Abs`, `Math.Ceiling`, `Math.Floor`, `Math.Round`, `Math.Sqrt`, `Math.Pow`, `Math.Min`, `Math.Max`, `Math.MapRange`, `Math.Random`). No trigonometry, logarithms or clamp at all. **H**: `+Math.Sin`, `+Math.Cos`, `+Math.Tan`, `+Math.Atan2`, `+Math.Log`, `+Math.Exp`, `+Math.Sign`, `+Math.Clamp`, `+Math.Truncate`, `+Math.Negate`, `+Math.Percent`, `+Math.RoundToMultiple`, `+Math.Radians`, `+Math.Degrees`, `+Math.Sequence` (start, count, step). **H, and the one that closes the rest at once:** `+Math.Formula` — type `a * b + 2`, name the inputs, get the result (a Dynamo code block for numbers; a small safe expression parser, no code generation).

**Lists** (43 nodes). The family is rich in structure (slice, chop, transpose, set operations, grouping) and has **no aggregate at all**: `+List.Sum`, `+List.Average`, `+List.Median`, `+List.Product`, `+List.StandardDeviation` (**H**). Also **M**: `+List.CountBy`, `+List.FilterByValue` (list, mode, value — replaces compare + mask + filter), `+List.WithIndex`, `+List.Zip`, `+List.Pairs`, `+List.Shuffle`; **L**: `+List.TakeWhile`, `+List.DropWhile`. (`List.MaximumItem` / `List.MinimumItem` exist.)

**Logic** (11). `+Logic.NotEquals`, `+Logic.Xor`, `+Logic.IsBetween`, `+Logic.Compare` (numbers, text and dates — `GreaterThan` and the others accept numbers only, so dates cannot be compared), `+Logic.Switch` (index → option). `And` / `Or` take exactly two inputs (`List.AllTrue` / `List.AnyTrue` cover lists).

**Strings** (14). Missing the staples of report text: `+String.Format` (`{0}`-style), `+String.Template` (named placeholders filled from a dictionary), `+String.RegexIsMatch` / `+String.RegexMatch` / `+String.RegexReplace`, `+String.PadLeft`, `+String.PadRight`, `+String.IndexOf`, `+String.Lines`, `+String.TrimStart`, `+String.TrimEnd`, `+String.ToTitleCase`, `+Number.Format` (decimals, thousands separator, unit suffix) — **H** for Format, Regex, Template and Number.Format; `String.Concat` joins exactly two (use `String.Join`).

**Date and time** (6). `+DateTime.Components` (year, month, day, weekday, ISO week), `+DateTime.AddMonths`, `+DateTime.AddHours`, `+DateTime.Today`, `+DateTime.Compare` — **M**; weekly coordination reports need the week number and "older than N days".

**Dictionary** (5) and **tables**: `+Dictionary.ContainsKey`, `+Dictionary.RemoveKey`, `+Dictionary.Merge`, `+Dictionary.Count`, `+Dictionary.FromRows`, `+Dictionary.ToRows` — **M**; the table toolkit is §6.

**Colour** (9). `+Color.ToHex` (there is `Color.FromHex` but no way back), `+Color.ByHSV`, `+Color.Palette` (named, colour-blind-safe sets) — **M**.

**Geometry** (13). Only `Vector.ByCoordinates`: no vector arithmetic at all. `+Vector.Add`, `+Vector.Subtract`, `+Vector.Scale`, `+Vector.Length`, `+Vector.Normalize`, `+Vector.Dot`, `+Vector.Cross`, `+Vector.Angle`, `+Vector.ByPoints`, `+Point.Add`, `+Point.Midpoint`, `+Point.Centroid`, `+BoundingBox.Volume`, `+BoundingBox.Corners`, `+BoundingBox.Expand`, `+BoundingBox.Overlap` — **M** (the transform and zone nodes already take vectors and points that users have to build by hand).

**Files and system** (11 file nodes). Beyond §3: `+Text.AppendToFile`, `+CSV.AppendToFile`, `+System.Environment`, `+System.OpenPath` (reveal in Explorer), `+Clipboard.Copy`, `+Zip.Create` / `+Zip.Extract` (deliverable packaging) — **M** / **L**.

**Flow and display** (4 flow nodes, 3 display nodes). `+Flow.When`, `+Flow.Try`, `+Flow.Wait`, `+Log.Write`, `+Watch Table` — **H** (see §4 and §6).

---

## 10. Cross-cutting findings

1. **There is no table.** Excel and CSV come in as "rows + headers" lists, and the only tool for them is `Table.JoinByKey`. Properties, QTO, rules, COBie, reports and KPIs are all tables. A small `Table.*` family on (rows, headers) pairs is the single most leverage-rich addition, and it is pure .NET.
2. **Names are typed by hand.** Categories, properties, set names, viewpoint names, clash test names. A dynamic pick-list (§8) plus `+Properties.Discover` removes the commonest cause of "node failed" in real scripts.
3. **No way to say "only if" or "carry on if it fails".** Everything in a graph runs; a missing file or an empty search is an error node and everything after it waits. `+Flow.When` and `+Flow.Try` are what unattended runs need.
4. **Nothing appends.** Logs, history, weekly KPIs: every writer overwrites.
5. **Things that exist but cannot be reached:** the IFC GUID codec (inside the BCF nodes), the mesh access (inside `Distance.BetweenItems`), `Model.Snapshot`-style keying (inside `Clash.SnapshotToFile`). Each is a node waiting to be extracted.
6. **Samples cover clash triage, colouring, sets, QTO roll-up, Excel export, fall hazard and viewpoints** — none shows compiling a federation, a naming-rule check, a weekly report or an IFC export. Samples are how each role discovers what the library is for.
7. **Several "gaps" are really missing recipes**: the clash matrix from sets (cross-product lacing), the OR of two searches (`List.SetUnion`), inverting a selection (`List.SetDifference`). A "recipes" page per role would remove a large part of the apparent gaps for little work.

## 11. Suggested order

Every name in this table is a proposal and does not exist yet, except the ones already in `NODE_CATALOG.md` (`SelectionSet.CreateFromSearch`, `Search.ByProperty`).

| Wave | What | Size | Testable here |
|---|---|---|---|
| A. Fundamentals | list aggregates and filters; math (trig, log, clamp, percent, sequence, `Math.Formula`); logic; strings (format, template, regex, pad); dates; dictionary; colour; vector / point / box maths; path, file and directory; append writers; `Flow.When` / `Flow.Try` / `Log.Write`; the missing inputs (date, choice, integer, multi-line, file list) | about 110 nodes | yes, all |
| B. The table | `Table.*` family, `Watch Table`, `Report.Html`, then `Properties.Discover`, `Properties.ToTable`, `Model.Snapshot`, `Model.Statistics`, `Takeoff.GroupBy`, `Search.ByGuid`, search-set modes | about 30 nodes | the table half yes; the Navisworks half compile-checked |
| C. Coordination and maintenance | `ClashTest.Edit` / `Delete` / `Duplicate` / `ClearResults`; `SelectionSet.Info` / `Duplicate`; `SavedViewpoint.Info` / `Update`; `Selection.Invert` / `Remove`; `TimelinerTask` edits; transforms (scale, move-to, builder); standard camera views; category split of Clash / Viewpoints / List | about 25 nodes | compile-checked |
| D. Open BIM and integration | `IFC.GuidEncode` / `Decode` and `ModelItem.IfcGuid` (do these first, they are small), `Audit.CheckRules`, `BCF` extensions, `Web.Get` / `Post`, `System.Run`, `Dialog.*`, the batch runner | about 15 nodes + host work | partly |
| E. Needs a probe first | section planes on 2025+, append options, per-model refresh, publish options, mesh volume | — | no |

Dynamic pick-lists (§8) are infrastructure and cut across waves B and C; they are the one item that needs design before code.
