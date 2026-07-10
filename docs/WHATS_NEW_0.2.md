# What's new in Dyncamelo v0.2 (in progress)

> Living document — generated from the code on the v0.2 branch; the team is
> still adding Navisworks coordination nodes, so this list grows until release.

## Fixes
- **Color → Appearance.OverrideColor now connects.** The engine gained a
  pluggable type-converter registry (checked at wire time and at run time);
  the Navisworks library registers a Dyncamelo-color → Autodesk color converter.
- **Color Picker propagates changes** — edits now mark the node dirty and
  re-execute downstream (AutoRun) / on the next Run.

## Editor quality-of-life
- Copy / paste / duplicate nodes (Ctrl+C/V/D) with connections preserved
- Node **groups** (Ctrl+G): titled, colored, move together, saved in .dyc
- Click-select a wire + Delete, or right-click **Disconnect**
- **Value preview** bubbles under nodes after a run (toggleable) — no Watch needed
- **Error balloons** on top of failing nodes
- Required vs optional input ports styled differently (tooltip shows defaults)
- Watch List shows item count and is **resizable**; sliders show live values
- Double-click empty canvas → quick String node at the cursor
- Library: **tooltips with node descriptions**, collapse/expand all,
  **★ Favourites** (persisted), bigger text, **Recent files** menu,
  BIMCamel logo in the header

## New nodes (102 so far)

### Color
| Node | Description |
|---|---|
| `Color.Components` | Splits a color into its red, green, blue and alpha channels (0-255). |
| `Color.FromHex` | Parses a hex color string ("#RRGGBB" or "#AARRGGBB"). |
| `Color.Lerp` | Interpolates between two colors (t clamped to 0-1). |

### DateTime
| Node | Description |
|---|---|
| `DateTime.AddDays` | Offsets a date/time by a number of days (fractional and negative values allowed). |
| `DateTime.ByDate` | Creates a date from year, month and day numbers. |
| `DateTime.DaysBetween` | Returns the signed number of days between two date/times (end minus start). |
| `DateTime.Parse` | Parses text as a date/time, optionally with an exact .NET format string. |

### Dictionary
| Node | Description |
|---|---|
| `Dictionary.Keys` | Returns all keys of a dictionary as a list. |
| `Dictionary.SetValueAtKey` | Returns a copy of the dictionary with the given key set or updated. |
| `Dictionary.Values` | Returns all values of a dictionary as a list. |

### File
| Node | Description |
|---|---|
| `Directory.GetFiles` | Lists the files in a folder (optionally filtered by a wildcard such as "*.nwd"). |
| `File.Exists` | Tests whether a file exists at the given path. |
| `JSON.Parse` | Parses a JSON string into dictionaries, lists and values. |
| `JSON.Stringify` | Serializes any value to a JSON string. |
| `Path.Combine` | Joins a folder path and a file name with the correct separator. |

### Geometry
| Node | Description |
|---|---|
| `BoundingBox.Intersects` | Tests whether two bounding boxes overlap (touching counts as intersecting). |
| `BoundingBox.Size` | Returns a bounding box's size along each axis and its min/max corner points. |
| `Point.DistanceTo` | Returns the straight-line distance between two points. |
| `Vector.ByCoordinates` | Creates a 3D direction vector from X, Y and Z components. |

### List
| Node | Description |
|---|---|
| `List.AddItemToEnd` | Appends a value to the end of a list (returns a new list). |
| `List.Contains` | Tests whether a list contains a value (numbers compare by value regardless of numeric type). |
| `List.GroupByKey` | Groups list elements by a parallel key list; returns the groups and their unique keys. |
| `List.IndexOf` | Returns the index of the first occurrence of a value in a list (-1 when absent). |
| `List.Join` | Concatenates two lists into one. |
| `List.LastItem` | Returns the last element of a list. |
| `List.RemoveItemAtIndex` | Removes the element at the given index (negative indexes count from the end). |
| `List.Reverse` | Returns the list in reverse order. |
| `List.SortByKey` | Sorts list elements by a parallel key list; returns the sorted elements and keys. |

### Logic
| Node | Description |
|---|---|
| `GreaterThanOrEqual` | Returns true when the first number is greater than or equal to the second. |
| `LessThanOrEqual` | Returns true when the first number is less than or equal to the second. |

### Math
| Node | Description |
|---|---|
| `Math.Abs` | Returns the absolute value of a number. |
| `Math.Ceiling` | Rounds a number up to the nearest integer. |
| `Math.Floor` | Rounds a number down to the nearest integer. |
| `Math.MapRange` | Linearly remaps a value from one range to another (values outside the range extrapolate). |
| `Math.Pow` | Raises the first number to the power of the second. |
| `Math.Random` | Returns a random number in a range (seed >= 0 makes it deterministic). |
| `Math.Sqrt` | Returns the square root of a non-negative number. |

### Navisworks.Appearance
| Node | Description |
|---|---|
| `Appearance.ColorByValues` | One-node color-coding: pairs each item with its value, colors each distinct value (categorical palette, or a blue→red gradient when every value is numeric) and outputs the legend. |
| `Appearance.Isolate` | Shows only these items and hides everything else (undo with Appearance.Show on Models.RootItems). |
| `Appearance.ResetAll` | Removes every permanent color/transparency override in the model — a clean slate before re-coloring. |

### Navisworks.Application
| Node | Description |
|---|---|
| `Application.Version` | The running Navisworks product name and API version (report headers, compatibility checks). |

### Navisworks.Audit
| Node | Description |
|---|---|
| `Audit.DuplicateItems` | Finds duplicated geometry (double-exported elements) by running a temporary Duplicate clash test over the items. |
| `Audit.MissingProperty` | Finds every item that does NOT carry the given property — the data-completeness audit. Lace over a property list to batch-audit. |

### Navisworks.Camera
| Node | Description |
|---|---|
| `Camera.Current` | The current camera position, focal distance and vertical field height. |
| `Camera.LookAt` | Moves the camera to 'eye' looking at 'target' (up stays +Z). |
| `Camera.ZoomToItems` | Frames the given items in the current view (per-item close-ups, screenshot staging). |

### Navisworks.Clash
| Node | Description |
|---|---|
| `Clash.GroupResultsByLevel` | Groups a test's results by nearest level below each clash point (wire your level names and elevations) — per-floor triage. |
| `Clash.GroupResultsByProximity` | Groups a test's results into clusters whose clash points lie within a radius of the cluster seed — one issue per hotspot. |
| `Clash.GroupResultsBySameItem` | Groups a test's results so every clash involving the same element lands in one group (named after the element) — turns thousands of raw clashes into one issue per element. |
| `Clash.RunAllTests` | Runs every Clash Detective test in the document — the weekly coordination re-run in one node. |
| `ClashResult.Assign` | Assigns a clash result to a person or trade — bulk assignment via lacing. |
| `ClashResult.SaveImage` | Renders a clash snapshot (scene plus clash highlight) to a .png/.jpg/.bmp file — the picture half of every clash report. |
| `ClashResult.SetDescription` | Sets a clash result's description text (context for reports and reviews). |
| `ClashResult.SetStatus` | Sets a clash result's status — with lacing this is bulk triage by rule (e.g. distance < 10 mm → Reviewed). |
| `ClashResult.Viewpoint` | The camera viewpoint Navisworks generates for a clash result; optionally applies it to the current view. |
| `ClashTest.ByName` | Finds a clash test by its display name (searches folders too). |
| `ClashTest.Create` | Creates a clash test between two item selections — script the weekly test matrix instead of clicking it. An existing top-level test with the same name is replaced. |
| `ClashTest.ResultsByStatus` | The results of a test that have the given status (New/Active/Reviewed/Approved/Resolved). |
| `ClashTest.Run` | Runs one clash test now and reports the result count. |

### Navisworks.Document
| Node | Description |
|---|---|
| `Document.Save` | Saves the document as .nwf (references) or .nwd (published snapshot) to the given path. |

### Navisworks.Export
| Node | Description |
|---|---|
| `Export.ClashReportCsv` | One-node clash report: writes test, group, result, status, distance, assignee, both item paths and GUIDs, and the clash point to a CSV file (Excel-ready). |
| `Export.ClashReportHtml` | Self-contained HTML clash report — one section per test, one row per result, optionally with embedded snapshots. Shareable as a single file. |
| `Export.NWD` | Saves the document as a published .nwd snapshot (appearance overrides baked in). |
| `Export.ViewpointImage` | Renders the current view to a .png/.jpg/.bmp file via the Navisworks image exporter. |

### Navisworks.Model
| Node | Description |
|---|---|
| `Model.FileName` | The cached and original source file paths of a model (federated-file inventory). |
| `Model.Units` | The native units of a model's source file (unit-mismatch audits across appended files). |

### Navisworks.ModelItem
| Node | Description |
|---|---|
| `ModelItem.Ancestors` | The chain of parents of a model item, up to its model root. |
| `ModelItem.ClassInfo` | The internal and localized class names of a model item (layer/group/geometry detection). |
| `ModelItem.GeometryLeaves` | Flattens items to their unique geometry-bearing descendants (the items QTO and coloring actually want). |
| `ModelItem.InstanceGuid` | The stable instance GUID of a model item ("" when absent) — cross-run identity for reports. |
| `ModelItem.IsHidden` | True when the model item is currently hidden in the viewport. |
| `ModelItem.Parent` | The parent of a model item (null for a model root). |

### Navisworks.Properties
| Node | Description |
|---|---|
| `Properties.AsDictionary` | Every property of an item flattened to a "Category.Property" → value dictionary (full data dump). |
| `Properties.HasProperty` | True when the item carries the property — drives model-QA missing-data masks. |
| `Properties.InCategory` | All property names and values inside one category of an item. Returns empty lists when the item lacks the category. |
| `Properties.ValueAsString` | Reads a property value as text. Returns "" when the property is absent. |
| `Property.Info` | The internal name, display name and plain value of a raw data property. |

### Navisworks.Search
| Node | Description |
|---|---|
| `Search.ByPropertyCompare` | Finds every model item whose numeric property is >, >=, < or <= a value (e.g. pipes with Diameter > 100). |
| `Search.ByPropertyWildcard` | Finds every model item whose property text matches a wildcard pattern (* and ?). |
| `Search.HasCategory` | Finds every model item that carries a property tab (e.g. every item with "TimeLiner" data). |
| `Search.HasProperty` | Finds every model item that carries the property at all, regardless of value. |
| `Search.InItems` | Scoped search: finds items whose property equals the value, looking only inside the given items (chained refinement). |

### Navisworks.Selection
| Node | Description |
|---|---|
| `Selection.AddToCurrent` | Adds items to the existing Navisworks selection (union) and returns the result. |
| `Selection.SelectAll` | Selects everything in the Navisworks UI and returns the selected items. |

### Navisworks.SelectionSets
| Node | Description |
|---|---|
| `SelectionSet.CreateFromSearch` | Creates a live SEARCH set from a property-equals rule — it re-evaluates as the model changes. An existing top-level set with the same name is replaced. |
| `SelectionSet.Delete` | Deletes a saved selection or search set by name (searches folders too). Returns false when absent — safe for clean re-runs. |
| `SelectionSet.Name` | The display name of a saved selection or search set. |
| `SelectionSets.BulkByPropertyValues` | One search set per distinct value of a property (e.g. one set per Level) — bulk set generation without the Find Items dialog. Existing same-named sets in the target location are replaced. |
| `SelectionSets.CreateFolder` | Creates a top-level folder in the Sets window (an existing same-named folder is reused, so re-runs are clean). |

### Navisworks.Takeoff
| Node | Description |
|---|---|
| `Takeoff.SumPropertyByGroup` | One-node QTO rollup: groups items by a property value and sums a numeric property per group (e.g. Volume per Level). Items without the grouping property land in "(none)". |

### Navisworks.TimeLiner
| Node | Description |
|---|---|
| `TimelinerTask.AttachSet` | Attaches a saved selection/search set to a task as a LIVE link (like Attach Set in the UI) — the core 4D-linking automation. |
| `TimelinerTask.SetDates` | Updates a task's planned start/end dates in place — bulk schedule edits without a re-import. |

### Navisworks.Units
| Node | Description |
|---|---|
| `Units.All` | Every unit name accepted by Units.Convert and Units.ScaleFactor. |

### Navisworks.Viewpoints
| Node | Description |
|---|---|
| `SavedViewpoint.Delete` | Deletes a saved viewpoint by name (searches folders too). Returns false when absent — safe for clean re-runs of batch generation. |
| `SavedViewpoint.Name` | The display name of a saved viewpoint. |
| `Viewpoints.FromClashResults` | Batch-generates one saved viewpoint per clash result, camera aimed at the clash and named after the result — the clash-triage staple. Existing same-named viewpoints in the folder are replaced. |

### String
| Node | Description |
|---|---|
| `String.EndsWith` | Tests whether a string ends with the given suffix. |
| `String.StartsWith` | Tests whether a string starts with the given prefix. |
| `String.Substring` | Extracts part of a string from a start index (-1 length = to the end). |
| `String.ToLower` | Converts a string to lowercase. |
| `String.ToUpper` | Converts a string to uppercase. |
| `String.Trim` | Removes leading and trailing whitespace from a string. |
