# Dyncamelo node catalogue

> Generated from the source by `tools/generate_node_catalog.py` — do not edit by hand. Regenerate with `python3 tools/generate_node_catalog.py` after adding, renaming or retiring a node; CI fails when this file or `dyncamelo-nodes.json` is out of date.

**367 nodes in 35 categories.** A `?` after an input marks it as optional. Retired nodes (still loadable in old graphs) are not listed.

| Category | Nodes |
|---|---|
| [Annotation](#annotation) | 1 |
| [Color](#color) | 9 |
| [Data](#data) | 5 |
| [DateTime](#datetime) | 6 |
| [Dictionary](#dictionary) | 5 |
| [Display](#display) | 3 |
| [File](#file) | 11 |
| [Geometry](#geometry) | 13 |
| [Input](#input) | 7 |
| [List](#list) | 44 |
| [Logic](#logic) | 11 |
| [Math](#math) | 15 |
| [Navisworks.Analysis](#navisworksanalysis) | 9 |
| [Navisworks.Appearance](#navisworksappearance) | 12 |
| [Navisworks.Camera](#navisworkscamera) | 6 |
| [Navisworks.Clash](#navisworksclash) | 48 |
| [Navisworks.Comments](#navisworkscomments) | 3 |
| [Navisworks.Document](#navisworksdocument) | 9 |
| [Navisworks.Export](#navisworksexport) | 14 |
| [Navisworks.Grids](#navisworksgrids) | 3 |
| [Navisworks.Markup](#navisworksmarkup) | 8 |
| [Navisworks.Model](#navisworksmodel) | 5 |
| [Navisworks.ModelItem](#navisworksmodelitem) | 19 |
| [Navisworks.Properties](#navisworksproperties) | 11 |
| [Navisworks.Search](#navisworkssearch) | 7 |
| [Navisworks.Selection](#navisworksselection) | 6 |
| [Navisworks.SelectionSets](#navisworksselectionsets) | 11 |
| [Navisworks.TimeLiner](#navisworkstimeliner) | 7 |
| [Navisworks.Transform](#navisworkstransform) | 5 |
| [Navisworks.Units](#navisworksunits) | 4 |
| [Navisworks.Viewpoints](#navisworksviewpoints) | 22 |
| [String](#string) | 14 |
| [Utility](#utility) | 1 |
| [Workflow](#workflow) | 4 |
| [Workflow.Actions](#workflowactions) | 9 |

## Annotation

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Note` *(interactive)* | — | — | A free-floating text note on the canvas |

## Color

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Color Picker` *(interactive)* | — | color | A color chosen with a picker |
| `Color.ByARGB` | a?, r?, g?, b? | color | Creates a color from alpha, red, green and blue values (0-255) |
| `Color.ByValues` | values, colors? | colors, uniqueValues, uniqueColors | One color per value, equal values sharing a color |
| `Color.Components` | color | red, green, blue, alpha | Splits a color into its red, green, blue and alpha channels (0-255) |
| `Color.FromHex` | hex | color | Parses a hex color string ("#RRGGBB" or "#AARRGGBB") |
| `Color.Gradient` | count, start?, end? | colors | A list of N colors evenly blended between two colors (endpoints included |
| `Color.Lerp` | start, end, t | color | Interpolates between two colors (t clamped to 0-1) |
| `Color.Random` | seed? | color | A pseudo-random color, stable per seed: the same seed always gives the same color (re-runs stay consistent) |
| `Color.RandomList` | count, seed? | colors | A list of visually distinct pseudo-random colors (golden-angle hues), stable per seed |

## Data

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `JSON.Parse` | json | value | Parses a JSON string into dictionaries, lists and values |
| `JSON.Stringify` | value, indented? | json | Serializes any value to a JSON string |
| `Snapshot.Diff` | oldValue, newValue | addedKeys, removedKeys, changedKeys | Diffs two GUID-keyed dictionaries: added/removed/changed keys (values compared by JSON equality |
| `Table.JoinByKey` | rows, headers, keys, keyColumn | matchedRows, unmatchedKeys | Joins spreadsheet rows to a key list: one matched row per key (null when unmatched), plus the keys that matched nothing |
| `XML.Parse` | xml | value | Parses XML into dictionaries, lists and strings (attributes as "@name", repeated elements as lists, mixed text as "#text") |

## DateTime

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `DateTime.AddDays` | dateTime, days | dateTime | Offsets a date/time by a number of days (fractional and negative values allowed) |
| `DateTime.ByDate` | year, month, day | dateTime | Creates a date from year, month and day numbers |
| `DateTime.DaysBetween` | start, end | days | Returns the signed number of days between two date/times (end minus start) |
| `DateTime.Format` | dateTime, format? | text | Formats a date/time as text using a .NET format string (invariant culture) |
| `DateTime.Now` | — | dateTime | Returns the current local date and time (captured at execution) |
| `DateTime.Parse` | text, format? | dateTime | Parses text as a date/time, optionally with an exact .NET format string |

## Dictionary

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Dictionary.ByKeysValues` | keys, values | dictionary | Creates a dictionary from a list of keys and a list of values of the same length |
| `Dictionary.Keys` | dictionary | keys | Returns all keys of a dictionary as a list |
| `Dictionary.SetValueAtKey` | dictionary, key, value | dictionary | Returns a copy of the dictionary with the given key set or updated |
| `Dictionary.ValueAtKey` | dictionary, key | value | Returns the value stored under the given key |
| `Dictionary.Values` | dictionary | values | Returns all values of a dictionary as a list |

## Display

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Watch` *(interactive)* | value | value | Displays the incoming value |
| `Watch Image` *(interactive)* | imagePath | imagePath | Displays the image file at the incoming path (PNG, JPG, BMP) |
| `Watch List` *(interactive)* | list | list | Displays the elements of a list, one per line |

## File

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `CSV.ReadFromFile` | path, delimiter? | data | Reads a CSV file into a list of rows (numeric cells become numbers) |
| `CSV.WriteToFile` | path, data, delimiter? | path | Writes a list of rows to a CSV file (overwrites |
| `Directory.GetFiles` | path, pattern? | files | Lists the files in a folder (optionally filtered by a wildcard such as "*.nwd") |
| `Excel.ReadFromFile` | path, sheet?, hasHeaders? | rows, headers, sheetNames | Reads an .xlsx worksheet into rows + headers (dates arrive as Excel serial numbers |
| `Excel.WriteToFile` | path, rows, headers?, sheet?, append? | path | Writes rows (+ optional headers) to an .xlsx worksheet |
| `File.Exists` | path | exists | Tests whether a file exists at the given path |
| `JSON.ReadFromFile` | path | data | Reads a JSON file into dictionaries, lists and values |
| `JSON.WriteToFile` | path, data, indented? | path | Writes any value to a JSON file (overwrites |
| `Path.Combine` | directory, fileName | path | Joins a folder path and a file name with the correct separator |
| `Text.ReadFromFile` | path | text | Reads the entire content of a text file |
| `Text.WriteToFile` | path, text | path | Writes text to a file (overwrites |

## Geometry

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `BoundingBox.ByCorners` | min, max | boundingBox | Creates an axis-aligned bounding box spanning two corner points |
| `BoundingBox.Center` | boundingBox | point | Returns the center point of a bounding box |
| `BoundingBox.Contains` | boundingBox, point | contains | Tests whether a point lies inside a bounding box (points on the boundary count as inside) |
| `BoundingBox.Intersects` | boundingBox, other | intersects | Tests whether two bounding boxes overlap (touching counts as intersecting) |
| `BoundingBox.PlanGap` | outer, inner | gap | The widest strip of open floor between an inner box (equipment) and the outer box (opening) in plan |
| `BoundingBox.Scale` | boundingBox, factor | boundingBox | Scales a bounding box about its center by a factor (2 = double, 0.5 = half) |
| `BoundingBox.Size` | boundingBox | sizeX, sizeY, sizeZ, min, max | Returns a bounding box's size along each axis and its min/max corner points |
| `BoundingBox.Union` | geometry | boundingBox | ONE bounding box fitting every box and/or point wired in ([x,y,z] triples work too |
| `Point.ByCoordinates` | x?, y?, z? | point | Creates a 3D point from X, Y and Z coordinates |
| `Point.Components` | point | x, y, z | Splits a point into its X, Y and Z coordinates |
| `Point.DistanceTo` | point, other | distance | Returns the straight-line distance between two points |
| `Point.Translate` | point, vector | point | Offsets a point by a vector, returning a new point |
| `Vector.ByCoordinates` | x?, y?, z? | vector | Creates a 3D direction vector from X, Y and Z components |

## Input

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Boolean` *(interactive)* | — | value | A true/false toggle |
| `Directory Path` *(interactive)* | — | path | A path to a directory |
| `File Path` *(interactive)* | — | path | A path to a file |
| `Integer Slider` *(interactive)* | — | value | An integer selected with a slider |
| `Number` *(interactive)* | — | value | A number literal |
| `Number Slider` *(interactive)* | — | value | A number selected with a slider |
| `String` *(interactive)* | — | value | A string literal |

## List

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `List.AddItemToEnd` | list, item | list | Appends a value to the end of a list (returns a new list) |
| `List.AddItemToFront` | list, item | list | Returns a new list with the value prepended |
| `List.AllIndicesOf` | list, item | indices | Every zero-based index at which the item occurs in the list |
| `List.AllTrue` | list | allTrue | True when EVERY element of the list is true |
| `List.AnyTrue` | list | anyTrue | True when AT LEAST ONE element of the list is true |
| `List.Chop` | list, lengths | lists | Chops a list into consecutive sublists: one length chops evenly ([1..7] by 3 → [1,2,3],[4,5,6],[7]) |
| `List.Clean` | list, removeEmptyLists? | list | Removes null elements from a list, at every nesting level |
| `List.Contains` | list, item | contains | Tests whether a list contains a value (numbers compare by value regardless of numeric type) |
| `List.Count` | list | count | Returns the number of elements in a list |
| `List.CountTrue` | list | trueCount, falseCount | Counts the true and not-true elements of a mask |
| `List.Create` *(interactive)* | item0 … itemN | list | Builds a list from the wired item inputs |
| `List.Cycle` | list, amount | list | Repeats the whole list a number of times, end-to-end ([a,b] × 3 → [a,b,a,b,a,b]) |
| `List.DropItems` | list, amount | list | Drops elements from the start of the list |
| `List.FilterByBoolMask` | list, mask | in, out | Splits a list into elements whose mask entry is true ("in") and the rest ("out") |
| `List.FirstItem` | list | item | Returns the first element of a list |
| `List.Flatten` | list, amount? | list | Flattens a nested list by a given number of levels (-1 = completely) |
| `List.GetItemAtIndex` | list, index | item | Returns the element at the given index (negative indexes count from the end) |
| `List.GroupByKey` | list, keys | groups, uniqueKeys | Groups list elements by a parallel key list |
| `List.IndexOf` | list, item | index | Returns the index of the first occurrence of a value in a list (-1 when absent) |
| `List.Insert` | list, item, index | list | Returns a new list with the value inserted at the index (0 = front |
| `List.Join` | listA, listB | list | Concatenates two lists into one |
| `List.LastIndexOf` | list, item | index | The zero-based index of the LAST occurrence of the item (-1 when absent) |
| `List.LastItem` | list | item | Returns the last element of a list |
| `List.MaximumItem` | list | item | The largest element of a list (numbers, texts or dates |
| `List.Merge` | lists | list | Concatenates any number of lists into one |
| `List.MinimumItem` | list | item | The smallest element of a list (numbers, texts or dates |
| `List.OfRepeatedItem` | item, amount | list | A list of one value repeated N times |
| `List.Range` | start, end, step? | list | Creates a sequence of numbers from start to end using the given step |
| `List.RemoveItemAtIndex` | list, index | list | Removes the element at the given index (negative indexes count from the end) |
| `List.ReplaceItemAtIndex` | list, index, item | list | Returns a new list with the element at the index replaced (negative indexes count from the end) |
| `List.ReplaceNulls` | list, substitute | list | Replaces every null element with a substitute value, at every nesting level |
| `List.RestOfItems` | list | list | Everything but the first element |
| `List.Reverse` | list | reversed | Returns the list in reverse order |
| `List.SetDifference` | list1, list2 | list | The distinct elements of the FIRST list that are NOT in the second (value equality) |
| `List.SetIntersection` | list1, list2 | list | The distinct elements present in BOTH lists (value equality, ordered as in the first) |
| `List.SetUnion` | list1, list2 | list | The distinct elements present in EITHER list (value equality, first-seen order) |
| `List.ShiftIndices` | list, amount | list | Rotates the list: +1 moves every element one place towards the end and wraps the last to the front ([a,b,c] → [c,a,b]) |
| `List.Slice` | list, start, end, step? | list | A sub-range of the list: from start (inclusive) to end (exclusive), taking every step-th element |
| `List.Sort` | list | list | Returns the list sorted ascending (numbers numerically, strings alphabetically) |
| `List.SortByKey` | list, keys | sorted, sortedKeys | Sorts list elements by a parallel key list |
| `List.TakeEveryNthItem` | list, n, offset? | list | Every n-th element, optionally after skipping offset elements |
| `List.TakeItems` | list, amount | list | Takes elements from the start of the list |
| `List.Transpose` | list | lists | Swaps rows and columns of a list of lists |
| `List.UniqueItems` | list | list | Removes duplicate elements from a list, preserving the original order |

## Logic

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `And` | a, b | result | Returns true only when both inputs are true |
| `Equals` | a, b | result | Tests whether two values are equal (numbers compare by value regardless of numeric type) |
| `GreaterThan` | a, b | result | Returns true when the first number is greater than the second |
| `GreaterThanOrEqual` | a, b | result | Returns true when the first number is greater than or equal to the second |
| `If` | test, trueValue, falseValue | result | Returns one of two values depending on a boolean condition |
| `IsNull` | value | isNull | True when the value is null |
| `IsNullOrEmpty` | value | isEmpty | True when the value is null, an empty string, an empty list or an empty dictionary |
| `LessThan` | a, b | result | Returns true when the first number is less than the second |
| `LessThanOrEqual` | a, b | result | Returns true when the first number is less than or equal to the second |
| `Not` | value | result | Inverts a boolean value |
| `Or` | a, b | result | Returns true when at least one input is true |

## Math

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Add` | a, b | result | Adds two numbers |
| `Divide` | a, b | result | Divides the first number by the second |
| `Math.Abs` | number | result | Returns the absolute value of a number |
| `Math.Ceiling` | number | result | Rounds a number up to the nearest integer |
| `Math.Floor` | number | result | Rounds a number down to the nearest integer |
| `Math.MapRange` | value, fromLow, fromHigh, toLow, toHigh | result | Linearly remaps a value from one range to another (values outside the range extrapolate) |
| `Math.Max` | a, b | result | Returns the larger of two numbers |
| `Math.Min` | a, b | result | Returns the smaller of two numbers |
| `Math.Pow` | @base, exponent | result | Raises the first number to the power of the second |
| `Math.Random` | min?, max?, seed? | result | Returns a random number in a range (seed >= 0 makes it deterministic) |
| `Math.Round` | number, digits? | result | Rounds a number to the given number of decimal digits (midpoints round away from zero) |
| `Math.Sqrt` | number | result | Returns the square root of a non-negative number |
| `Modulo` | a, b | result | Returns the remainder of dividing the first number by the second |
| `Multiply` | a, b | result | Multiplies two numbers |
| `Subtract` | a, b | result | Subtracts the second number from the first |

## Navisworks.Analysis

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Audit.DuplicateItems` | items, tolerance?, document? | items1, items2, count | Finds duplicated geometry (double-exported elements) by running a temporary Duplicate clash test over the items |
| `Audit.MissingProperty` | categoryName, propertyName, items?, geometryOnly?, document? | items, count | Finds every item that does NOT carry the given property |
| `Distance.BetweenItems` | itemsA, itemsB, method?, document? | distance, pointA, pointB | Shortest distance between two selections, with the closest (witness) point on each side |
| `FallHazard.EdgeHandrailCheck` | floors, level, handrails, obstructions?, band?, cellSize?, limit?, handrailTolerance?, minPassage?, units?, imagePath?, pixelsPerCell?, showOverage?, dangerousColor?, protectedColor?, safeColor?, document? | imagePath, dangerousLength, protectedLength, safeLength, report | Marks the floor edges around voids: green where the gap across the void is under the limit (safe), red where it is over the limit and there is no handrail (needs one), a… |
| `FallHazard.FloorOpeningMap` | floors, level, obstructions?, band?, cellSize?, minGap?, units?, imagePath?, saveViewpoints?, pixelsPerCell?, showOverage?, lowColor?, highColor?, document? | imagePath, openingCount, widestGaps, centers, viewpoints, report | Whole-floor fall-hazard heat map |
| `Proximity.Cluster` | items, tolerance?, units?, method?, propertyName?, tabName?, document? | groups, clusterNumbers, clusterCount, sizes, report | Groups items into clusters of touching geometry (gap <= tolerance, chained) |
| `Proximity.NearestDistance` | items, targets, method?, document? | distances | For each item, the distance to the NEAREST of the targets (document units), so you can flag items with nothing close by |
| `Takeoff.SumPropertyByGroup` | items, groupCategoryName, groupPropertyName, valueCategoryName, valuePropertyName | keys, sums, counts | One-node QTO rollup: groups items by a property value and sums a numeric property per group (e.g |
| `Zone.AssignByVolumes` | zoneItems, zoneNames, targetItems, tabName?, propertyName? | items, assignedCount | Tags each target with the name of the zone volume containing its bounding-box center |

## Navisworks.Appearance

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Appearance.ColorByValues` | items, values, palette?, document? | items, legend | One-node color-coding: pairs each item with its value, colors each distinct value (categorical palette, or a blue→red gradient when every value is numeric) and outputs t… |
| `Appearance.Hide` | items, document? | items | Hides model items in the viewport |
| `Appearance.Isolate` | items, document? | items | Shows only these items and hides everything else (undo with Appearance.ShowAll) |
| `Appearance.OverrideColor` | items, color, document? | items | Overrides the color of model items (a permanent override: saved with the file and undoable) |
| `Appearance.OverrideColorTemporary` | items, color, document? | items | Applies a TEMPORARY (viewpoint-scoped) color override |
| `Appearance.OverrideTransparency` | items, transparency, document? | items | Overrides the transparency of model items (0 = opaque, 1 = invisible) |
| `Appearance.OverrideTransparencyTemporary` | items, transparency, document? | items | Applies a TEMPORARY (viewpoint-scoped) transparency override (0 = opaque, 1 = invisible) |
| `Appearance.Reset` | items, document? | items | Removes permanent color and transparency overrides from model items, restoring their original materials |
| `Appearance.ResetAll` | document? | done | Removes every permanent color/transparency override in the model |
| `Appearance.ResetTemporary` | after?, document? | done, after | Clears every TEMPORARY color/transparency override in the model |
| `Appearance.Show` | items, document? | items | Shows (un-hides) model items in the viewport |
| `Appearance.ShowAll` | document? | done | Shows (un-hides) every item in the model |

## Navisworks.Camera

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Camera.Current` | document? | position, focalDistance, heightField | The current camera position, focal distance and vertical field height |
| `Camera.LookAt` | eye, target, document? | done | Moves the camera to 'eye' looking at 'target' (up stays +Z) |
| `Camera.SetFieldOfView` | degrees, document? | done | Sets the camera's vertical field of view in degrees (perspective camera) |
| `Camera.SetProjection` | perspective, document? | done | Switches the camera between perspective (true) and orthographic (false) projection |
| `Camera.ZoomToItems` | items, paddingFactor?, document? | done | Frames the given items in the current view (per-item close-ups, screenshot staging) |
| `Viewpoint.SetSectionBox` | boundingBox, enabled?, document? | done | Applies a section box around a region on the current view (Sectioning > Box, scriptable) |

## Navisworks.Clash

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Clash.AllGroups` | document? | groups, names, testNames, counts | Every result group of every clash test in the document, as ONE flat list |
| `Clash.CompareSnapshots` | oldPath, newPath | newResults, resolved, persisting, counts | Diffs two clash snapshots: clashes NEW since the baseline, clashes RESOLVED (disappeared), and clashes PERSISTING in both (with their previous status) |
| `Clash.Deduplicate` | results | results, duplicates | Keeps ONE clash per unique item pair |
| `Clash.FilterByAngle` | results, minDegrees?, maxDegrees? | results | Keeps only the clash results whose crossing angle (see ClashResult.Angle) is within a degree range |
| `Clash.FilterByDepth` | results, minDepth?, maxDepth?, units?, document? | results | Keeps clashes whose penetration depth falls in a range, in the unit you name |
| `Clash.FilterByItemProperty` | results, category, property, value1, value2?, mode?, searchAncestors?, caseSensitive? | results | Keeps clashes whose two items' PROPERTY values match a pair of texts, in either order |
| `Clash.FilterByOrientation` | results, shape1?, shape2? | results | Keeps only the clashes between elements of the given box shapes, matched in either order |
| `Clash.FilterBySet` | results, set, which?, invert?, document? | results | Keeps clashes whose items belong to a selection/search set (either one, both, or a specific side) |
| `Clash.FilterByStatus` | results, status | results | Keeps only the clash results with the given status(es) |
| `Clash.GroupResults` | results, groupName, moveExisting?, document? | test, group, added, moved, skipped | Puts an explicit list of clash results into a named group in Clash Detective |
| `Clash.GroupResultsByGridIntersection` | test, document? | test, groupCount | Groups a test's results by the model's own grid: each group is named after the nearest grid intersection and level (e.g |
| `Clash.GroupResultsByLevel` | test, levelNames, levelElevations, document? | test, groupCount | Groups a test's results by nearest level below each clash point (wire your level names and elevations) |
| `Clash.GroupResultsByProximity` | test, radius, document? | test, groupCount | Groups a test's results into clusters whose clash points lie within a radius of the cluster seed |
| `Clash.GroupResultsBySameItem` | test, useItem1?, document? | test, groupCount | Groups a test's results so every clash involving the same element lands in one group (named after the element) |
| `Clash.GroupResultsByStatus` | test, document? | test, groupCount | Groups a test's results by status (New/Active/Reviewed/Approved/Resolved) |
| `Clash.RunAllTests` | document? | tests | Runs every Clash Detective test in the document |
| `Clash.SnapshotToFile` | filePath, tests?, document? | filePath, resultCount | Saves a clash-run snapshot (per result: test, item identities, status, distance, clash point) as JSON |
| `Clash.Status` | status? | status | A clash status as a dropdown (New/Active/Reviewed/Approved/Resolved) |
| `Clash.Statuses` | newStatus?, active?, reviewed?, approved?, resolved? | statuses | Pick SEVERAL clash statuses with toggles |
| `Clash.SummaryTable` | tests?, document? | rows, headers | Per-test clash counts by status (test × Total/New/Active/Reviewed/Approved/Resolved) |
| `Clash.Tests` | document? | tests | All Clash Detective tests in a document, including those inside folders |
| `ClashGroup.ByName` | test, groupName, document? | group, results, status, count | Finds a clash result group by test name + group name and opens it up: the results inside, the group's own status, and the count |
| `ClashGroup.Info` | group | results, name, status, count, test, testName | Everything about a clash result group, straight from the group object |
| `ClashResult.AddComment` | result, body, status?, author?, document? | result | Appends a comment to a clash result or group |
| `ClashResult.Angle` | result | degrees | The angle in degrees (0–90) between the two clashing elements, taken from each element's overall direction (its bounding-box diagonal) |
| `ClashResult.Assign` | result, assignedTo, document? | result | Assigns a clash result to a person or trade |
| `ClashResult.Center` | result | point | The clash point of a result, in document units |
| `ClashResult.Comments` | result | comments, authors, statuses, dates | The comment thread of a clash result or group: texts, authors, statuses and dates, index-aligned |
| `ClashResult.Documentation` | result | hasViewpoint, hasRedlines, commentCount | How documented a clash already is |
| `ClashResult.Focus` | results, isolate?, zoom?, select?, paddingFactor?, document? | items | Focuses the view on clash results the way double-clicking one in Clash Detective does: hides everything else (isolate), zooms the camera to the clashing pair, and option… |
| `ClashResult.Info` | result | name, status, distance, description, assignedTo, createdTime | Name, status, distance, description, assignee and creation time of a clash result |
| `ClashResult.Items` | result | item1, item2 | The two model items involved in a clash result |
| `ClashResult.Orientation` | result | degrees, shape1, shape2, slope1, slope2 | ClashResult.Angle with world context: the crossing angle PLUS each element's bounding-box shape |
| `ClashResult.Rename` | result, newName, document? | result | Renames a clash result or result group |
| `ClashResult.SaveImage` | result, filePath, width?, height?, document? | filePath | Renders a clash snapshot (scene plus clash highlight) to a .png/.jpg/.bmp file |
| `ClashResult.SetDescription` | result, description, document? | result | Sets a clash result's description text (context for reports and reviews) |
| `ClashResult.SetStatus` | result, status, document? | result | Sets a clash result's status |
| `ClashResult.Size` | result | volume | The size of the clash overlap region |
| `ClashResult.Viewpoint` | result, apply?, document? | viewpoint | The camera viewpoint Navisworks generates for a clash result |
| `ClashTest.ByName` | name, document? | test | Finds a clash test by its display name (searches folders too) |
| `ClashTest.Create` | name, itemsA, itemsB, testType?, tolerance?, document? | test | Creates a clash test between two item selections |
| `ClashTest.Groups` | test, document? | groups, names, statuses, counts | All result groups of a clash test |
| `ClashTest.Info` | test | name, status, testType, tolerance, lastRun, resultCount | Name, status, type, tolerance, last run time and result count of a clash test |
| `ClashTest.Name` | test | name | The display name of a clash test |
| `ClashTest.Rename` | test, newName, document? | test | Renames a clash test |
| `ClashTest.Results` | test | results | The individual results of a clash test (grouped results are flattened) |
| `ClashTest.ResultsByStatus` | test, status | results | The results of a test that have the given status(es) |
| `ClashTest.Run` | test, document? | test, resultCount | Runs one clash test now and reports the result count |

## Navisworks.Comments

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `SavedItem.AddComment` | item, body, status?, author?, document? | item | Adds a comment to a saved viewpoint, selection/search set or folder |
| `SavedItem.ClearComments` | item, document? | item | Deletes every comment on a saved viewpoint, selection/search set or folder (replace-all with an empty thread) |
| `SavedItem.Comments` | item | bodies, authors, statuses, dates | The comment thread on any saved item (viewpoint, set, folder, clash test): bodies, authors, statuses and creation dates, index-aligned |

## Navisworks.Document

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Application.Version` | — | product, apiVersion | The running Navisworks product name and API version (report headers, compatibility checks) |
| `Document.AppendFiles` | filePaths, document? | document, models | Appends design files to the document |
| `Document.Current` | — | document | The active Navisworks document |
| `Document.Info` | document? | fileName, title, units, modelCount | File name, title, display units and model count of a document |
| `Document.Merge` | filePath, document? | document | Merges another Navisworks file into the document with duplicate resolution |
| `Document.Models` | document? | models | The models (appended source files) loaded in a document |
| `Document.Open` | filePath, document? | document | Opens a file into the document, REPLACING its current contents (the headless batch driver) |
| `Document.Refresh` | document? | updated | Refreshes every linked/appended file from disk |
| `Document.Save` | filePath, document? | filePath | Saves the document as .nwf (references) or .nwd (published snapshot) to the given path |

## Navisworks.Export

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `BCF.ExportIssues` | filePath, results?, viewpoints?, includeSnapshots?, statusMap?, document? | filePath, topicCount | Exports clash results (or saved viewpoints) as BCF 2.1 issues (.bcfzip: markup, camera viewpoint, component GUIDs, snapshot) |
| `BCF.ImportIssues` | filePath, applyCameraTopicIndex?, document? | topics, modelItems | Reads a BCF 2.0/2.1 package: per topic title/status/description/comments/component GUIDs/camera, plus the model items each topic's components resolve to (matched by IFC… |
| `Export.ClashReportCsv` | filePath, tests?, document? | filePath, rowCount | One-node clash report: writes test, group, result, status, distance, assignee, both item paths and GUIDs, and the clash point to a CSV file (Excel-ready) |
| `Export.ClashReportHtml` | filePath, tests?, includeImages?, imageWidth?, imageHeight?, document? | filePath, rowCount | Self-contained HTML clash report |
| `Export.IfcClasses` | — | classes | Lists the friendly IFC class names (Wall, Beam, Door, …) accepted by Export.IfcSetClassMap |
| `Export.IfcCoordinates` | basePoint?, eastings?, northings?, elevation?, rotationDegrees?, writeGeoref? | coordinates | Base-point and georeferencing options for Export.ToIfc: geometry/model/custom origin, rotation and IFC4 georeferencing |
| `Export.IfcParameterRule` | source, targetPset?, targetName?, sourceCategory? | rule | One property rename/relocate rule for Export.ToIfc |
| `Export.IfcRoles` | typeProperty?, typeCategory?, levelProperty?, levelCategory?, materialProperty?, materialCategory?, classificationProperty?, classificationCategory? | roles | Maps Navisworks source properties to IFC roles |
| `Export.IfcSetClassMap` | setNames, ifcClasses, predefinedTypes?, document? | classMap | Assigns an IFC class to every item of the named saved/search sets (set→class), producing the classMap for Export.ToIfc |
| `Export.IfcSpatialNames` | project?, site?, building?, storey? | spatialNames | Names for the IFC spatial tree (Project/Site/Building/default Storey) used by Export.ToIfc |
| `Export.NWD` | filePath, document? | filePath | Saves the document as a published .nwd snapshot (appearance overrides baked in) |
| `Export.ToCsv` | items, filePath, categoryName?, propertyNames? | filePath | Writes one CSV row per model item with a Name column plus property columns |
| `Export.ToIfc` | items, filePath, schema?, instancing?, properties?, materials?, quantities?, units?, quality?, coordinates?, spatialNames?, roles?, parameterRules?, categoryFilter?, classMap?, splitMegabytes?, validate?, document? | filePath, fileCount, elementCount, triangleCount, fileSizeKb | Exports model items to IFC (IFC4/IFC2x3) via the BIMCamel exporter: spatial tree, instancing, property sets, materials, base quantities and georeferencing |
| `Export.ViewpointImage` | filePath, width?, height?, document? | filePath | Renders the current view to a .png/.jpg/.bmp file via the Navisworks image exporter |

## Navisworks.Grids

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Grids.ClosestIntersection` | point, document? | name, position, levelName, label | The grid intersection and level nearest to any point |
| `Grids.Intersections` | document?, samples? | names, points, levelNames | All grid intersections of the active grid system, per level |
| `Grids.Levels` | document? | names, elevations, levels | The model's own levels from the active grid system |

## Navisworks.Markup

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Markup.AddArrow` | viewpoint, x1, y1, x2, y2, color?, thickness?, document? | viewpoint | EXPERIMENTAL: draws an arrow redline on a saved viewpoint (undocumented Navisworks API) |
| `Markup.AddCloud` | viewpoint, points, color?, thickness?, document? | viewpoint | EXPERIMENTAL: draws a revision-cloud redline through the given markup-space points (undocumented Navisworks API) |
| `Markup.AddEllipse` | viewpoint, x1, y1, x2, y2, color?, thickness?, document? | viewpoint | EXPERIMENTAL: draws an ellipse redline on a saved viewpoint, fitted corner-to-corner (undocumented Navisworks API) |
| `Markup.AddLine` | viewpoint, x1, y1, x2, y2, color?, thickness?, document? | viewpoint | EXPERIMENTAL: draws a line redline on a saved viewpoint (undocumented Navisworks API) |
| `Markup.AddNumberTag` | viewpoint, number, x, y, radius?, comment?, color?, thickness?, document? | viewpoint | EXPERIMENTAL: draws a circled number on a saved viewpoint and optionally attaches a comment |
| `Markup.AddText` | viewpoint, text, x, y, color?, thickness?, document? | viewpoint | EXPERIMENTAL: draws a text redline on a saved viewpoint (undocumented Navisworks API) |
| `Markup.Clear` | viewpoint, document? | viewpoint | EXPERIMENTAL: removes every redline markup from a saved viewpoint (undocumented Navisworks API) |
| `Markup.List` | viewpoint, document? | count, types, texts, positions | EXPERIMENTAL: lists the redline markups on a saved viewpoint |

## Navisworks.Model

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Model.FileName` | model | fileName, sourceFileName | The cached and original source file paths of a model (federated-file inventory) |
| `Model.Remove` | model, document? | removed | Removes a WHOLE appended source model from the document (accepts a Model, a 0-based index, or a file name) |
| `Model.RootItem` | model | rootItem | The root model item of a model |
| `Model.Units` | model | units | The native units of a model's source file (unit-mismatch audits across appended files) |
| `Models.RootItems` | document? | rootItems | The root model items of every model loaded in a document |

## Navisworks.ModelItem

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `ModelItem.AncestorNameMatches` | item, text, mode?, includeSelf?, caseSensitive? | matches, ancestor, ancestorName | Walks an item's ancestor chain (nearest first) and tests each name against the text: contains / starts with / ends with, or their "doesn't" negations |
| `ModelItem.AncestorPropertyMatches` | item, category, property, text, mode?, includeSelf?, caseSensitive? | matches, ancestor, value | The general form of ModelItem.AncestorNameMatches: walks an item's ancestor chain (nearest first) and tests ANY property you name |
| `ModelItem.Ancestors` | item, includeSelf? | ancestors | The chain of parents of a model item, up to its model root |
| `ModelItem.BoundingBox` | item, ignoreHidden? | boundingBox | The axis-aligned bounding box of a model item, in document units |
| `ModelItem.Children` | item | children | The direct children of a model item |
| `ModelItem.ClassInfo` | item | className, classDisplayName | The internal and localized class names of a model item (layer/group/geometry detection) |
| `ModelItem.CombinedBoundingBox` | items, ignoreHidden? | boundingBox | ONE bounding box fitting all the given items together (per-group when wired a list of groups) |
| `ModelItem.CommonAncestor` | items | ancestor | The deepest common ancestor of the given items in the selection tree |
| `ModelItem.Descendants` | item | descendants | All descendants of a model item (the whole subtree below it) |
| `ModelItem.DisplayName` | item | name | The display name of a model item (falls back to its class name when unnamed) |
| `ModelItem.GeometryLeaves` | items | leaves | Flattens items to their unique geometry-bearing descendants (the items QTO and coloring actually want) |
| `ModelItem.HasGeometry` | item | hasGeometry | True when the model item carries geometry |
| `ModelItem.InstanceGuid` | item | guid | The stable instance GUID of a model item ("" when absent) |
| `ModelItem.IsHidden` | item | isHidden | True when the model item is currently hidden in the viewport |
| `ModelItem.ModelName` | item | modelName | The name of the model/file an item comes from (the root of its selection tree, e.g |
| `ModelItem.ObjectAncestor` | item | object | Walks up the selection tree to the whole object/element a geometry item belongs to (Navisworks' first composite-object ancestor) |
| `ModelItem.Parent` | item | parent | The parent of a model item (null for a model root) |
| `ModelItem.ReferencePoints` | item | bboxCenter, bboxMin, localOrigin | Candidate base/reference points of an item, in world document units |
| `ModelItem.SourceInfo` | item | sourceFileName, itemType, model | One-node answer to "which file did this element come from and what is it": the source file name, the Item-tab Type (falling back to the item's class name), and the ownin… |

## Navisworks.Properties

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Properties.AsDictionary` | item | properties | Every property of an item flattened to a "Category.Property" → value dictionary (full data dump) |
| `Properties.Categories` | item | categories | The property category names available on a model item |
| `Properties.CustomTabs` | modelItem | tabNames | The user-defined property tabs on an item (discovery/QA before SetCustom or RemoveCustomTab) |
| `Properties.HasProperty` | item, categoryName, propertyName | hasProperty | True when the item carries the property |
| `Properties.InCategory` | item, categoryName | names, values | All property names and values inside one category of an item |
| `Properties.RemoveCustomTab` | modelItems, tabName | modelItems, removedCount | Removes a user-defined property tab from items |
| `Properties.RenameCustomTab` | modelItems, tabName, newTabName | modelItems | Renames a user-defined property tab in place (same properties, same internal name |
| `Properties.SetCustom` | modelItems, names, values, tabName?, merge? | modelItems | Writes a user-defined property tab onto items |
| `Properties.Value` | item, categoryName, propertyName | value | Reads a property value from a model item, converted to a plain value |
| `Properties.ValueAsString` | item, categoryName, propertyName | text | Reads a property value as text |
| `Property.Info` | property | name, displayName, value | The internal name, display name and plain value of a raw data property |

## Navisworks.Search

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Search.ByPropertyCompare` | categoryName, propertyName, comparison, value, resolveTo?, document? | items | Finds every model item whose numeric property is >, >=, < or <= a value (e.g |
| `Search.ByPropertyContains` | categoryName, propertyName, value, resolveTo?, document? | items | Finds every model item whose property text contains the given substring |
| `Search.ByPropertyValue` | categoryName, propertyName, value, resolveTo?, document? | items | Finds every model item whose property exactly equals the given value |
| `Search.ByPropertyWildcard` | categoryName, propertyName, pattern, resolveTo?, document? | items | Finds every model item whose property text matches a wildcard pattern (* and ?) |
| `Search.HasCategory` | categoryName, resolveTo?, document? | items | Finds every model item that carries a property tab (e.g |
| `Search.HasProperty` | categoryName, propertyName, resolveTo?, document? | items | Finds every model item that carries the property at all, regardless of value |
| `Search.InItems` | items, categoryName, propertyName, value, resolveTo?, document? | items | Scoped search: finds items whose property equals the value, looking only inside the given items (chained refinement) |

## Navisworks.Selection

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Selection.AddToCurrent` | items, document? | items | Adds items to the existing Navisworks selection (union) and returns the result |
| `Selection.Clear` | document? | cleared | Clears the interactive Navisworks selection |
| `Selection.Current` | resolveTo?, document? | items | The model items currently selected in Navisworks |
| `Selection.Resolve` | modelItems, level? | items | Re-selects items at another selection-tree level |
| `Selection.SelectAll` | document? | items | Selects everything in the Navisworks UI and returns the selected items |
| `Selection.SetCurrent` | items, document? | items | Replaces the interactive Navisworks selection with the given items |

## Navisworks.SelectionSets

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `SelectionSet.ByName` | name, document? | selectionSet | Finds a saved selection or search set by its display name (searches folders too) |
| `SelectionSet.Create` | name, items, document? | selectionSet | Creates a saved selection set from the given items |
| `SelectionSet.CreateFromSearch` | name, categoryName, propertyName, value, document? | selectionSet | Creates a live SEARCH set from a property-equals rule |
| `SelectionSet.Delete` | name, document? | deleted | Deletes a saved selection or search set by name (searches folders too) |
| `SelectionSet.Items` | selectionSet, document? | items | The model items a saved set selects |
| `SelectionSet.MoveToFolder` | selectionSet, folder, document? | selectionSet | Moves a saved selection or search set into a folder (appended at the end) |
| `SelectionSet.Name` | selectionSet | name | The display name of a saved selection or search set |
| `SelectionSet.Rename` | selectionSet, newName, document? | selectionSet | Renames a saved selection or search set (accepts the set or its current name |
| `SelectionSets.All` | document? | selectionSets | All saved selection and search sets in a document, including those inside folders |
| `SelectionSets.BulkByPropertyValues` | categoryName, propertyName, folderName?, document? | selectionSets, values | One search set per distinct value of a property (e.g |
| `SelectionSets.CreateFolder` | name, parentFolder?, document? | folder | Creates a folder in the Sets window, optionally nested under a parent folder |

## Navisworks.TimeLiner

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `TimeLiner.AutoAttachByProperty` | category, property, document? | attachedCount, unmatchedTasks | For every TimeLiner task (subtasks included), finds all items whose property value equals the task name and attaches them |
| `TimeLiner.Tasks` | document? | tasks | All TimeLiner tasks in a document, with subtasks flattened into one list |
| `TimelinerTask.AttachSet` | task, setName, document? | task | Attaches a saved selection/search set to a task as a LIVE link (like Attach Set in the UI) |
| `TimelinerTask.Create` | name, plannedStart, plannedEnd, items?, taskType?, document? | task | Creates a top-level TimeLiner task with planned dates and optionally attaches model items |
| `TimelinerTask.Info` | task | name, displayId, plannedStart, plannedEnd, actualStart, actualEnd, taskType, progress | Name, id, planned and actual dates, task type and progress of a TimeLiner task |
| `TimelinerTask.Items` | task, document? | items | The model items attached to a TimeLiner task |
| `TimelinerTask.SetDates` | task, plannedStart, plannedEnd, document? | task | Updates a task's planned start/end dates in place |

## Navisworks.Transform

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `ModelItem.GetTransform` | item | origin, matrix, hasOverride | Reads an item's current (active) transform: origin = its translation (a practical base point), matrix = 16 numbers row-major (feed ModelItem.SetTransform to round-trip),… |
| `ModelItem.ResetTransform` | items?, resetAll?, document? | items | Removes permanent transform overrides, restoring items to their original position |
| `ModelItem.RotateAboutAxis` | items, origin, axis, degrees, document? | items | Rotates model items by an angle (degrees) about an axis through a point |
| `ModelItem.SetTransform` | items, matrix, document? | items | Sets the permanent transform override of model items to an absolute 4×4 matrix (16 numbers, row-major, translation at indices 3/7/11) |
| `ModelItem.Translate` | items, vector, document? | items | Moves model items by a vector, in document units (chain Units.Convert for meters/feet) |

## Navisworks.Units

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Units.All` | — | names | Every unit name accepted by Units.Convert and Units.ScaleFactor |
| `Units.Convert` | value, fromUnits, toUnits | value | Converts a length value from one unit to another (e.g |
| `Units.Current` | document? | units | The display units of a document (all API lengths, areas and volumes use them) |
| `Units.ScaleFactor` | fromUnits, toUnits | factor | The multiplier that converts a length from one unit to another |

## Navisworks.Viewpoints

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `SavedViewpoint.Apply` | viewpoint, document? | viewpoint | Makes a saved viewpoint the current view (camera, plus any saved overrides) |
| `SavedViewpoint.ByName` | name, document? | viewpoint | Finds a saved viewpoint by its display name (searches folders too) |
| `SavedViewpoint.CopyOverrides` | fromViewpoint, toViewpoint, document? | viewpoint | Copies the appearance the way one saved view looks |
| `SavedViewpoint.Delete` | name, document? | deleted | Deletes a saved viewpoint by name (searches folders too) |
| `SavedViewpoint.Duplicate` | viewpoint, newName?, document? | viewpoint | Duplicates a saved viewpoint in its folder, copying its camera and any baked appearance overrides |
| `SavedViewpoint.Folder` | viewpoint, document? | folderPath, folder | The folder containing a saved viewpoint: its path as "A/B" ("" for top-level viewpoints) and the folder itself |
| `SavedViewpoint.MoveToFolder` | viewpoint, folder, document? | viewpoint | Moves a saved viewpoint into a folder (appended at the end) |
| `SavedViewpoint.Name` | viewpoint | name | The display name of a saved viewpoint |
| `SavedViewpoint.Rename` | viewpoint, newName, document? | viewpoint | Renames a saved viewpoint (accepts the viewpoint or its current name |
| `Viewpoint.SaveCurrent` | name, document? | viewpoint | Saves the current view as a new saved viewpoint |
| `Viewpoint.SaveWithOverrides` | name, folderName?, document? | viewpoint | Saves the current view AND the current temporary color/transparency/hidden overrides into the viewpoint (Navisworks CaptureRuntimeOverrides) |
| `Viewpoint.VisibleItems` | items, viewpoint?, fullyInside?, document? | visibleItems, outsideItems, mask, containsAny, report | Checks which of the given items a viewpoint can see (bounding box vs the camera frustum) |
| `ViewpointPackageFile.Parse` | json | result | Parses a package from JSON with node-friendly errors: malformed text and files written by a newer Dyncamelo both fail with a message that says what to do, never a raw se… |
| `Viewpoints.All` | document? | viewpoints | All saved viewpoints in a document, including those inside folders |
| `Viewpoints.CreateFolder` | name, parentFolder?, document? | folder | Creates a folder in the Saved Viewpoints window, optionally nested under a parent folder |
| `Viewpoints.DuplicateFolder` | folder, newName, document? | folder | Duplicates a Saved Viewpoints folder |
| `Viewpoints.ExportFile` | filePath, viewpoints?, document? | filePath, count, report | Exports saved viewpoints |
| `Viewpoints.FromClashResults` | results, folderName?, document? | viewpoints | Batch-generates one saved viewpoint per clash result, camera aimed at the clash and named after the result |
| `Viewpoints.ImportFile` | filePath, folderName?, overwrite?, document? | viewpoints, count, report | Rebuilds the viewpoints from a Viewpoints.ExportFile package in THIS model: camera, section box and folder structure |
| `Viewpoints.InFolder` | folder?, recursive?, document? | viewpoints, names, subfolders, count | All saved viewpoints inside a folder, in Saved Viewpoints window order |
| `Viewpoints.RenameFolder` | folder, newName, document? | folder | Renames a Saved Viewpoints folder (accepts the folder or its current name |
| `Viewpoints.SortFolder` | folder?, recursive?, document? | folder | Sorts a Saved Viewpoints folder's contents alphabetically by name (A→Z) |

## String

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `String.Concat` | a, b | result | Joins two strings into one |
| `String.Contains` | str, searchFor, ignoreCase? | result | Tests whether a string contains the given substring |
| `String.EndsWith` | text, searchFor, ignoreCase? | result | Tests whether a string ends with the given suffix |
| `String.FromObject` | obj | result | Converts any value (including whole lists) to its display string |
| `String.Join` | separator, list | result | Joins the elements of a list into a single string with a separator |
| `String.Length` | str | result | Returns the number of characters in a string |
| `String.Replace` | str, searchFor, replaceWith | result | Replaces all occurrences of a substring with another string |
| `String.Split` | str, separator | list | Splits a string into a list of substrings around a separator |
| `String.StartsWith` | text, searchFor, ignoreCase? | result | Tests whether a string starts with the given prefix |
| `String.Substring` | text, startIndex, length? | result | Extracts part of a string from a start index (-1 length = to the end) |
| `String.ToLower` | text | result | Converts a string to lowercase |
| `String.ToNumber` | str | result | Converts a numeric string (invariant culture, e.g |
| `String.ToUpper` | text | result | Converts a string to uppercase |
| `String.Trim` | text | result | Removes leading and trailing whitespace from a string |

## Utility

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Reroute` *(interactive)* | in | out | A wire waypoint that passes its value through unchanged |

## Workflow

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Flow.Then` | value, after, after2?, after3? | value | Passes a value through unchanged AFTER the wired 'after' nodes have run |
| `Loop.Collect` *(interactive)* | loop, value | results | Closes a loop and collects one value per iteration |
| `Loop.Item` *(interactive)* | items | item, index, count, loop | Yields the current item of a loop |
| `Workflow.ForEach` | items, actions | results | Runs a sequence of actions on each item, one item fully before the next |

## Workflow.Actions

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Action.Ghost` | items, transparency? | action | Per item: applies a TEMPORARY transparency to the given items (wire the whole found set) so the highlighted item reads against a faded context |
| `Action.Highlight` | color, transparency? | action | Per item: applies a TEMPORARY color to the current item so it stands out |
| `Action.Isolate` | — | action | Per item: shows the current item and hides everything else (Appearance.Isolate) |
| `Action.OverrideColor` | color | action | Per item: overrides the current item's color (Appearance.OverrideColor) |
| `Action.ResetAppearance` | — | action | Per item: removes color and transparency overrides from the current item (Appearance.Reset) |
| `Action.ResetTemporaryAppearance` | — | action | Per item: clears all TEMPORARY color/transparency overrides (Appearance.ResetTemporary) |
| `Action.SaveViewpoint` | name?, folder?, bakeOverrides? | action | Per item: saves the current view as a saved viewpoint named from the item |
| `Action.ShowAll` | — | action | Per item: un-hides every item in the model (Appearance.ShowAll) |
| `Action.ZoomTo` | paddingFactor? | action | Per item: frames the current item in the view (Camera.ZoomToItems) |

