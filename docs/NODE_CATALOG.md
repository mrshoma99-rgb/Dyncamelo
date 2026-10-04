# CamelGraph node catalogue

> Generated from the source by `tools/generate_node_catalog.py` — do not edit by hand. Regenerate with `python3 tools/generate_node_catalog.py` after adding, renaming or retiring a node; CI fails when this file or `camelgraph-nodes.json` is out of date.

**588 nodes in 46 categories.** A `?` after an input marks it as optional. Retired nodes (still loadable in old graphs) are listed at the end.

| Category | Nodes |
|---|---|
| [Annotation](#annotation) | 1 |
| [Color](#color) | 17 |
| [Data](#data) | 5 |
| [DateTime](#datetime) | 18 |
| [Dictionary](#dictionary) | 17 |
| [Display](#display) | 4 |
| [File](#file) | 37 |
| [Geometry](#geometry) | 42 |
| [IFC](#ifc) | 4 |
| [Input](#input) | 11 |
| [List](#list) | 50 |
| [List.Statistics](#liststatistics) | 12 |
| [Logic](#logic) | 18 |
| [Math](#math) | 38 |
| [Navisworks.Analysis](#navisworksanalysis) | 9 |
| [Navisworks.Appearance](#navisworksappearance) | 13 |
| [Navisworks.Camera](#navisworkscamera) | 7 |
| [Navisworks.Clash.Filter](#navisworksclashfilter) | 8 |
| [Navisworks.Clash.Group](#navisworksclashgroup) | 9 |
| [Navisworks.Clash.Report](#navisworksclashreport) | 4 |
| [Navisworks.Clash.Results](#navisworksclashresults) | 15 |
| [Navisworks.Clash.Tests](#navisworksclashtests) | 16 |
| [Navisworks.Comments](#navisworkscomments) | 4 |
| [Navisworks.Document](#navisworksdocument) | 9 |
| [Navisworks.Export](#navisworksexport) | 14 |
| [Navisworks.Grids](#navisworksgrids) | 3 |
| [Navisworks.Markup](#navisworksmarkup) | 6 |
| [Navisworks.Model](#navisworksmodel) | 5 |
| [Navisworks.ModelItem](#navisworksmodelitem) | 15 |
| [Navisworks.Properties](#navisworksproperties) | 13 |
| [Navisworks.Search](#navisworkssearch) | 2 |
| [Navisworks.Selection](#navisworksselection) | 9 |
| [Navisworks.SelectionSets](#navisworksselectionsets) | 17 |
| [Navisworks.TimeLiner](#navisworkstimeliner) | 10 |
| [Navisworks.Transform](#navisworkstransform) | 7 |
| [Navisworks.Units](#navisworksunits) | 4 |
| [Navisworks.Viewpoints](#navisworksviewpoints) | 17 |
| [Navisworks.Viewpoints.Files](#navisworksviewpointsfiles) | 2 |
| [Navisworks.Viewpoints.Folders](#navisworksviewpointsfolders) | 5 |
| [Report](#report) | 2 |
| [String](#string) | 36 |
| [System](#system) | 7 |
| [Table](#table) | 28 |
| [Utility](#utility) | 1 |
| [Workflow](#workflow) | 8 |
| [Workflow.Actions](#workflowactions) | 9 |

## Annotation

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Note` *(interactive)* | — | — | A free-floating text note on the canvas |

## Color

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Color.ByARGB` | a?, r?, g?, b? | color | Creates a color from alpha, red, green and blue values (0-255) |
| `Color.ByHSV` | hue?, saturation?, value?, alpha? | color | Creates a color from hue (degrees, wraps around), saturation and value (0-1, clamped) |
| `Color.ByValues` | values, colors? | colors, uniqueValues, uniqueColors | One color per value, equal values sharing a color |
| `Color.Components` | color | red, green, blue, alpha | Splits a color into its red, green, blue and alpha channels (0-255) |
| `Color.ContrastText` | background | color | Black or white, whichever reads better on a background color (WCAG contrast) |
| `Color.Darken` | color, amount? | color | Makes a color darker by shifting its HSL lightness down by amount (0-1, clamped) |
| `Color.FromHex` | hex | color | Parses a hex color string ("#RRGGBB" or "#AARRGGBB") |
| `Color.Gradient` | count?, start?, end? | colors | A list of N colors evenly blended between two colors (endpoints included |
| `Color.Invert` | color | color | Inverts a color's red, green and blue channels (the photographic negative) |
| `Color.Lerp` | start, end, t | color | Interpolates between two colors (t clamped to 0-1) |
| `Color.Lighten` | color, amount? | color | Makes a color lighter by shifting its HSL lightness up by amount (0-1, clamped) |
| `Color.Palette` | name?, count? | colors | A named palette as a list of colors: colourblind-safe, tableau, pastel, status (green/amber/red/grey) or the viridis, heat and grey ramps interpolated to count |
| `Color.Random` | seed? | color | A pseudo-random color, stable per seed: the same seed always gives the same color (re-runs stay consistent) |
| `Color.RandomList` | count?, seed? | colors | A list of visually distinct pseudo-random colors (golden-angle hues), stable per seed |
| `Color.ToHex` | color, includeAlpha? | hex | Formats a color as hex text, "#RRGGBB" (or "#AARRGGBB" with includeAlpha) |
| `Color.ToHSV` | color | hue, saturation, value | Splits a color into hue (degrees, 0-360), saturation and value (0-1) |
| `Color.WithAlpha` | color, alpha? | color | Returns a color with its alpha (opacity) replaced, 0 = transparent to 255 = opaque |

## Data

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `JSON.Parse` | json | value | Parses a JSON string into dictionaries, lists and values |
| `JSON.Stringify` | value, indented? | json | Serializes any value to a JSON string |
| `Snapshot.Diff` | oldValue, newValue | addedKeys, removedKeys, changedKeys | Diffs two GUID-keyed dictionaries: added/removed/changed keys (values compared by JSON equality |
| `XML.Parse` | xml, listElements? | value | Parses XML into dictionaries, lists and strings (attributes as "@name", repeated elements as lists, mixed text as "#text") |
| `XML.ReadFromFile` | path | value | Reads an XML file into dictionaries, lists and strings, in the same shape as XML.Parse (attributes as "@name", repeated elements as lists, mixed text as "#text") |

## DateTime

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `DateTime.Add` | dateTime, amount, unit? | dateTime | Adds an amount of time to a date/time in a chosen unit |
| `DateTime.AddWorkdays` | dateTime, days, weekend? | dateTime | Moves a date/time by a number of working days, skipping the weekend (Saturday and Sunday, or Friday and Saturday): Friday + 1 is Monday, Monday - 1 is Friday, Saturday +… |
| `DateTime.ByDate` | year, month, day | dateTime | Creates a date from year, month and day numbers |
| `DateTime.Compare` | a, b | result | Compares two date/times: -1 when the first is earlier, 0 when equal, 1 when it is later |
| `DateTime.Components` | dateTime | year, month, day, hour, minute, second, weekday, dayOfYear, isoWeek, isoYear, quarter | Splits a date/time into year, month, day, hour, minute, second, English weekday name, day of year, ISO week, ISO year and quarter |
| `DateTime.DaysBetween` | start, end | days | Returns the signed number of days between two date/times (end minus start), with the time of day as a fraction |
| `DateTime.Difference` | start, end, unit? | difference | How far apart two date/times are in a chosen unit |
| `DateTime.EndOf` | dateTime, unit?, firstDayOfWeek? | dateTime | Returns the last millisecond of the day, week, month, quarter or year containing a date/time (start of the next period minus 1 ms) |
| `DateTime.Format` | dateTime, format? | text | Formats a date/time as text using a .NET format string (invariant culture): yyyy-MM-dd gives 2026-07-10, dd/MM/yyyy gives 10/07/2026, HH:mm gives 14:30, MMMM gives the E… |
| `DateTime.FromExcelSerial` | serial, dateSystem? | dateTime | Converts an Excel date serial number (days since 1899-12-30, the time of day as the fraction |
| `DateTime.FromUnixSeconds` | seconds | dateTime | Converts Unix seconds since 1970-01-01 UTC to a (UTC) date/time |
| `DateTime.IsWeekend` | dateTime, weekend? | isWeekend | True when a date falls on the weekend (Saturday and Sunday, or Friday and Saturday) |
| `DateTime.Now` | — | dateTime | Returns the current local date and time (captured at execution) |
| `DateTime.Parse` | text, format?, dayFirst? | dateTime | Parses text as a date/time |
| `DateTime.Range` | start, end, step?, unit? | dates | Creates a list of dates from a start to an end (both included) with a step in days, weeks, months or years ("every month" lands on the same day of each month, clamped to… |
| `DateTime.StartOf` | dateTime, unit?, firstDayOfWeek? | dateTime | Returns midnight at the start of the day, week, month, quarter or year containing a date/time |
| `DateTime.Today` | — | dateTime | Returns today's local date at midnight (captured at execution) |
| `DateTime.ToUnixSeconds` | dateTime | seconds | Converts a date/time to Unix seconds since 1970-01-01 UTC (a value without a time zone is taken as UTC) |

## Dictionary

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Dictionary.ByKeysValues` | keys, values | dictionary | Creates a dictionary from a list of keys and a list of values of the same length |
| `Dictionary.ContainsKey` | dictionary, key | hasKey | Tests whether a dictionary has the given key (case-sensitive) |
| `Dictionary.Count` | dictionary | count | Returns the number of entries in a dictionary |
| `Dictionary.FromRows` | rows | dictionary | Builds a dictionary from a list of [key, value] rows (a later row wins for a repeated key) |
| `Dictionary.Invert` | dictionary | dictionary | Swaps keys and values into a new dictionary (values become text keys |
| `Dictionary.Keys` | dictionary | keys | Returns all keys of a dictionary as a list |
| `Dictionary.Merge` | dictionaries | dictionary | Combines several dictionaries into a new one |
| `Dictionary.RemoveKey` | dictionary, key | dictionary | Returns a copy of the dictionary without the given key (a missing key is fine) |
| `Dictionary.RemoveKeys` | dictionary, keys | dictionary | Returns a copy of the dictionary without any of the listed keys (a key it does not have is fine) |
| `Dictionary.SelectKeys` | dictionary, keys | dictionary | Returns a copy of the dictionary with only the listed keys, in the order they are listed (trim a property bag to the few keys a report needs) |
| `Dictionary.SetValueAtKey` | dictionary, key, value | dictionary | Returns a copy of the dictionary with the given key set or updated |
| `Dictionary.SetValues` | dictionary, keys, values | dictionary | Returns a copy of the dictionary with several keys set or updated at once, from a list of keys and a list of values of the same length (the several-key version of Dictio… |
| `Dictionary.ToRows` | dictionary | rows | Converts a dictionary to a list of [key, value] rows |
| `Dictionary.ValueAtKey` | dictionary, key | value | Returns the value stored under the given key |
| `Dictionary.ValueAtPath` | value, path, defaultValue? | value | Follows a path into nested data (the result of JSON.Parse or XML.Parse) and returns what it finds there, or a default value when any step is missing |
| `Dictionary.ValueOrDefault` | dictionary, key, defaultValue? | value | Returns the value stored under a key, or a default value when the key is missing |
| `Dictionary.Values` | dictionary | values | Returns all values of a dictionary as a list |

## Display

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Watch` *(interactive)* | value | value | Displays the incoming value |
| `Watch Image` *(interactive)* | imagePath | imagePath | Displays the image file at the incoming path (PNG, JPG, BMP) |
| `Watch List` *(interactive)* | list | list | Displays the elements of a list, one per line |
| `Watch Table` *(interactive)* | table | table | Displays a table as a grid: column names on top, one line per row |

## File

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `CSV.AppendToFile` | path, rows, delimiter?, headers?, encoding? | path | Appends rows to a CSV file (same quoting and date format as CSV.WriteToFile), writing the optional headers only when the file is new or empty |
| `CSV.ReadFromFile` | path, delimiter?, numbers?, encoding? | data | Reads a CSV file into a list of rows |
| `CSV.WriteToFile` | path, data, delimiter?, encoding? | path | Writes a list of rows to a CSV file, replacing what was there, and creates missing folders |
| `Directory.Copy` | source, destination, overwrite? | path | Copies a folder with all its files and sub-folders to another place (creates the destination |
| `Directory.Create` | path | path | Creates a folder including any missing parent folders (does nothing when it already exists) |
| `Directory.Delete` | path, recursive? | deleted | Deletes a folder (an empty one, or with its whole content when recursive is true) |
| `Directory.Exists` | path | exists | Tests whether a folder exists at the given path |
| `Directory.Find` | path, pattern?, kind?, recursive?, sortBy?, descending?, limit? | paths | Finds files or folders under a folder by wildcard (several allowed, separated by ";"), in every sub-folder unless recursive is off |
| `Directory.Move` | source, destination, overwrite? | path | Moves or renames a folder with everything in it |
| `Excel.ReadFromFile` | path, sheet?, hasHeaders?, trimEmptyRows? | rows, headers, sheetNames | Reads an .xlsx worksheet into rows + headers + the sheet names (dates arrive as Excel serial numbers, convert them with DateTime.FromExcelSerial |
| `Excel.WriteToFile` | path, rows, headers?, sheet?, append? | path | Writes rows (+ optional headers) to an .xlsx worksheet |
| `File.Copy` | source, destination, overwrite? | path | Copies a file to a new path (creates the destination folder |
| `File.Delete` | path | deleted | Deletes a file |
| `File.Exists` | path | exists | Tests whether a file exists at the given path |
| `File.Hash` | path, algorithm? | hash | Computes a checksum of a file's content (SHA256, SHA1 or MD5) as lower-case hex text - handy for change detection |
| `File.Info` | path | exists, name, extension, directory, sizeBytes, modified, created | Reads a file's name, extension, folder, size in bytes and modified / created dates |
| `File.Move` | source, destination, overwrite? | path | Moves or renames a file (creates the destination folder |
| `JSON.ReadFromFile` | path, encoding? | data | Reads a JSON file into dictionaries, lists and values (every number becomes a double) |
| `JSON.WriteToFile` | path, data, indented?, encoding? | path | Writes any value to a JSON file, replacing what was there, and creates missing folders |
| `Log.Write` | path, message, level?, encoding? | line | Appends one line "yyyy-MM-dd HH:mm:ss LEVEL message" (local time) to a log file and returns that line |
| `Path.ChangeExtension` | path, extension | path | Replaces the extension of a path ("a.nwd" + "nwf" gives "a.nwf") |
| `Path.Combine` | directory, fileName | path | Joins a folder path and a file name with the correct separator |
| `Path.GetDirectory` | path | directory | Returns the folder part of a path ("C:\Models\site.nwd" gives "C:\Models") |
| `Path.GetExtension` | path | extension | Returns the extension of a path with its leading dot (".nwd"), or empty text when there is none |
| `Path.GetFileName` | path | fileName | Returns the file name of a path including its extension ("C:\Models\site.nwd" gives "site.nwd") |
| `Path.GetFileNameWithoutExtension` | path | name | Returns the file name of a path without its extension ("C:\Models\site.nwd" gives "site") |
| `Path.GetFullPath` | path | path | Resolves a path to an absolute path the way the file nodes do: a relative path starts in the graph's folder (the folder of the saved graph file |
| `Path.GetRelativePath` | path, baseDirectory | path | Expresses a path relative to a base folder |
| `Path.IsAbsolute` | path | isAbsolute | Tests whether a path is absolute (starts at a drive, network share or root) rather than relative |
| `Path.Join` | parts | path | Joins any number of folder and file name parts into one path with the correct separator, for example C:\Projects + North + HVAC + model.nwd |
| `Path.Normalize` | path | path | Cleans a path as text: one separator style, no trailing separator, "." and ".." resolved (the disk is never read) |
| `Text.AppendToFile` | path, text, newLine?, encoding? | path | Appends text (plus a line break by default) to the end of a text file, creating the file and folder if needed |
| `Text.ReadFromFile` | path, encoding? | text | Reads the entire content of a text file as one text |
| `Text.WriteToFile` | path, text, encoding? | path | Writes text to a file, replacing what was there, and creates missing folders |
| `Zip.Create` | sources, zipPath, overwrite? | path | Packs files and folders into a zip archive (a folder is stored with its structure under its own name) |
| `Zip.Extract` | zipPath, directory, overwrite? | directory | Unpacks a zip archive into a folder |
| `Zip.List` | zipPath | entries | Lists the entry names inside a zip archive without extracting it |

## Geometry

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `BoundingBox.ByCorners` | cornerA, cornerB | boundingBox | Creates an axis-aligned bounding box spanning two opposite corner points, given in any order (the smaller coordinates become the box's min corner, the larger its max cor… |
| `BoundingBox.Center` | boundingBox | point | Returns the center point of a bounding box |
| `BoundingBox.Contains` | boundingBox, point | contains | Tests whether a point lies inside a bounding box (points on the boundary count as inside) |
| `BoundingBox.ContainsBox` | outer, inner | contains | True when the inner box fits entirely inside the outer box (touching faces count as inside) |
| `BoundingBox.Corners` | boundingBox | corners | The 8 corner points of a bounding box: the bottom face counter-clockwise from the Min corner (0-3), then the top face in the same order (4-7) |
| `BoundingBox.Expand` | boundingBox, amount | boundingBox | Grows a bounding box by an amount on every side (negative shrinks it |
| `BoundingBox.Footprint` | boundingBox | area | The plan (floor) area of a bounding box: size X times size Y, ignoring height |
| `BoundingBox.Intersects` | boundingBox, other | intersects | Tests whether two bounding boxes overlap (touching counts as intersecting) |
| `BoundingBox.Overlap` | a, b | boundingBox | The bounding box shared by two overlapping boxes |
| `BoundingBox.PlanGap` | outer, inner | gap | The widest strip of open floor between an inner box (equipment) and the outer box (opening) in plan |
| `BoundingBox.Scale` | boundingBox, factor | boundingBox | Scales a bounding box about its center by a factor (2 = double, 0.5 = half) |
| `BoundingBox.Size` | boundingBox | sizeX, sizeY, sizeZ, min, max | Returns a bounding box's size along each axis and its min/max corner points |
| `BoundingBox.SurfaceArea` | boundingBox | area | The total area of a bounding box's six faces (square model units) |
| `BoundingBox.Translate` | boundingBox, offset | boundingBox | Moves a bounding box by an offset vector, keeping its size |
| `BoundingBox.Union` | geometry | boundingBox | ONE bounding box fitting every box and/or point wired in |
| `BoundingBox.Volume` | boundingBox | volume | The volume of a bounding box (cubic model units) |
| `Point.ByCoordinates` | x?, y?, z? | point | Creates a 3D point from X, Y and Z coordinates |
| `Point.Centroid` | points | point | The centroid (average position) of a list of points |
| `Point.Components` | point | x, y, z | Splits a point into its X, Y and Z coordinates |
| `Point.Distance2D` | a, b | distance | The distance between two points measured in plan (XY only, Z ignored) |
| `Point.DistanceTo` | a, b | distance | Returns the straight-line distance between two points |
| `Point.Lerp` | a, b, t | point | Interpolates between two points: t = 0 is the first, t = 1 the second |
| `Point.Midpoint` | a, b | point | The point halfway between two points |
| `Point.Round` | point, digits? | point | Rounds a point's X, Y and Z to the given number of decimal digits (midpoints round away from zero) |
| `Point.Translate` | point, vector | point | Offsets a point by a vector, returning a new point |
| `Vector.Add` | a, b | vector | Adds two vectors (a + b) |
| `Vector.Angle` | a, b | degrees | The angle between two vectors in degrees, 0 (same direction) to 180 (opposite) |
| `Vector.ByCoordinates` | x?, y?, z? | vector | Creates a 3D direction vector from X, Y and Z components |
| `Vector.ByPoints` | from, to | vector | Creates the vector that leads from one point to another (end minus start) |
| `Vector.Components` | vector | x, y, z | Splits a vector into its X, Y and Z components |
| `Vector.Cross` | a, b | vector | The cross product of two vectors: a vector perpendicular to both (right-hand rule), zero when they are parallel |
| `Vector.Dot` | a, b | dot | The dot product of two vectors: positive when they point the same way, 0 when perpendicular, negative when opposed |
| `Vector.IsParallel` | a, b, tolerance? | isParallel | True when two vectors lie along the same line (same or opposite direction) within a length-independent tolerance |
| `Vector.IsPerpendicular` | a, b, tolerance? | isPerpendicular | True when two vectors meet at a right angle within a length-independent tolerance |
| `Vector.Length` | vector | length | Returns the length (magnitude) of a vector |
| `Vector.Negate` | vector | vector | Reverses a vector so it points the opposite way |
| `Vector.Normalize` | vector | vector | Scales a vector to length 1, keeping its direction (a zero-length vector has no direction and is an error) |
| `Vector.Scale` | vector, factor | vector | Multiplies a vector by a number (2 doubles its length, -1 reverses it) |
| `Vector.Subtract` | a, b | vector | Subtracts one vector from another (a - b) |
| `Vector.XAxis` | — | vector | The unit vector along the X axis, (1, 0, 0) |
| `Vector.YAxis` | — | vector | The unit vector along the Y axis, (0, 1, 0) |
| `Vector.ZAxis` | — | vector | The unit vector along the Z axis, (0, 0, 1) |

## IFC

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `IFC.GuidDecode` | globalId | guid | Converts a 22-character IFC GlobalId such as "0$WU4A9R19$vKWO$AdOnKA" back to a standard lower-case hyphenated GUID |
| `IFC.GuidEncode` | guid | globalId | Converts a standard GUID such as "3f81e10a-25b0-49ff-9520-63f2a763150a" to the 22-character IFC GlobalId (the IFC base-64 form, alphabet 0-9 A-Z a-z _ $) |
| `IFC.IsGlobalId` | text | isGlobalId | True when a text is a 22-character IFC GlobalId (alphabet 0-9 A-Z a-z _ $, first character 0 to 3) and false for a plain GUID, any other text or an empty cell, so a colu… |
| `IFC.Normalize` | id, form? | id | Writes an id in one form whichever form it comes in: a 22-character IFC GlobalId or a standard GUID (with hyphens, without, or in braces) becomes the form you choose, a… |

## Input

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Boolean` *(interactive)* | — | value | A true/false toggle |
| `Choice` *(interactive)* | — | value, index | A pick-list with your own options (one per line) |
| `Color Picker` *(interactive)* | — | color | A color chosen with a picker |
| `Date` *(interactive)* | — | value | A date (and optional time), typed as 2026-10-01 or 2026-10-01 14:30 |
| `Directory Path` *(interactive)* | — | path | A path to a directory |
| `File Path` *(interactive)* | — | path | A path to a file |
| `Integer` *(interactive)* | — | value | A whole number (no slider range): a count, an index, a level |
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
| `List.Contains` | list, item | contains | Tests whether a list contains a value |
| `List.Count` | list | count | Returns the number of elements in a list |
| `List.CountTrue` | list | trueCount, falseCount | Counts the true and not-true elements of a mask |
| `List.Create` *(interactive)* | item0 … itemN | list | Builds a list from the wired item inputs, in order |
| `List.Cycle` | list, amount | list | Repeats the whole list a number of times, end-to-end ([a,b] × 3 → [a,b,a,b,a,b]) |
| `List.DropItems` | list, amount | list | Drops elements from the start of the list |
| `List.DropWhile` | list, mask | list | Drops items from the start of the list for as long as the mask is true |
| `List.FilterByBoolMask` | list, mask | in, out | Splits a list into elements whose mask entry is true ("in") and the rest ("out") |
| `List.FilterByValue` | list, test?, value?, keys?, ignoreCase? | matched, rejected, mask | Keeps the items that pass a test such as > 100, contains "wall", matches "A-*" or in ["L01", "L02"] |
| `List.FirstItem` | list | item | Returns the first element of a list |
| `List.Flatten` | list, amount? | list | Flattens a nested list by a given number of levels (-1 = completely) |
| `List.GetItemAtIndex` | list, index | item | Returns the element at the given index (negative indexes count from the end) |
| `List.GroupByKey` | list, keys | groups, uniqueKeys | Groups list elements by a parallel key list of the same length |
| `List.IndexOf` | list, item | index | Returns the index of the first occurrence of a value in a list (-1 when absent) |
| `List.Insert` | list, item, index | list | Returns a new list with the value inserted at the index (0 = front |
| `List.LastIndexOf` | list, item | index | The zero-based index of the LAST occurrence of the item (-1 when absent) |
| `List.LastItem` | list | item | Returns the last element of a list |
| `List.MaximumItem` | list | item | The largest element of a list (numbers, texts or dates) |
| `List.Merge` | lists | list | Concatenates any number of lists into one |
| `List.MinimumItem` | list | item | The smallest element of a list (numbers, texts or dates) |
| `List.OfRepeatedItem` | item, amount | list | A list of one value repeated N times |
| `List.Pairs` | list, cyclic? | pairs | Pairs each item with the next one: [[a, b], [b, c], …] |
| `List.Range` | start, end, step? | list | Creates a sequence of numbers from start to end (both included) using the given step |
| `List.RemoveItemAtIndex` | list, indices | list | Removes the elements at the given indices and returns one new list (negative indexes count from the end) |
| `List.ReplaceItemAtIndex` | list, index, item | list | Returns a new list with the element at the index replaced (negative indexes count from the end) |
| `List.ReplaceNulls` | list, substitute | list | Replaces every null element with a substitute value, at every nesting level |
| `List.RestOfItems` | list | list | Everything but the first element |
| `List.Reverse` | list | list | Returns the list in reverse order |
| `List.SetDifference` | list1, list2 | list | The distinct elements of the FIRST list that are NOT in the second |
| `List.SetIntersection` | list1, list2 | list | The distinct elements present in BOTH lists (ordered as in the first) |
| `List.SetUnion` | list1, list2 | list | The distinct elements present in EITHER list (first-seen order) |
| `List.ShiftIndices` | list, amount | list | Rotates the list: +1 moves every element one place towards the end and wraps the last to the front ([a,b,c] → [c,a,b]) |
| `List.Shuffle` | list, seed? | list | Shuffles a list |
| `List.Slice` | list, start, end?, step? | list | A sub-range of the list: from start (inclusive) to end (exclusive), taking every step-th element |
| `List.Sort` | list, descending? | list | Returns the list sorted ascending, or largest first with descending |
| `List.SortByKey` | list, keys, descending? | sorted, sortedKeys | Sorts list elements by a parallel key list of the same length, smallest key first or largest first with descending |
| `List.TakeEveryNthItem` | list, n, offset? | list | Takes the n-th, 2n-th, 3n-th  |
| `List.TakeItems` | list, amount | list | Takes elements from the start of the list |
| `List.TakeWhile` | list, mask | list | Takes items from the start of the list for as long as the mask is true (stops at the first false) |
| `List.Transpose` | list | lists | Swaps rows and columns of a list of lists |
| `List.UniqueItems` | list | list | Removes duplicate elements from a list, keeping the first of each and the original order |
| `List.WithIndex` | list | pairs | Pairs each item with its position: [[0, item0], [1, item1], …] |
| `List.Zip` | first, second | pairs | Pairs two lists by position: [[a0, b0], [a1, b1], …], as long as the shorter list |

## List.Statistics

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `List.Average` | list | average | The arithmetic mean of the numbers of a list |
| `List.CountValues` | list | values, counts | Tallies a list: each distinct value, in order of first appearance, with the number of times it occurs |
| `List.CumulativeSum` | list | totals | A running total: each item is the sum of the list up to and including that position |
| `List.Duplicates` | list | duplicates, counts | The values that occur more than once, with how many times each occurs |
| `List.Histogram` | list, bins? | lower, upper, counts, labels | Splits the range of the numbers into equal bins and counts how many fall in each |
| `List.Median` | list | median | The middle value of the numbers of a list (the mean of the two middle ones when the count is even) |
| `List.MostCommon` | list | item, count | The value that occurs most often in a list (the earliest wins a tie) and how many times |
| `List.Percentile` | list, percent? | value | The value below which a given percentage of the numbers lie (linear interpolation, like Excel's PERCENTILE.INC) |
| `List.Product` | list | product | Multiplies the numbers of a list |
| `List.StandardDeviation` | list, sample? | standardDeviation | The standard deviation of the numbers of a list (population by default |
| `List.Statistics` | list | count, sum, min, max, average, median, standardDeviation | Count, sum, minimum, maximum, average, median and standard deviation of a list of numbers in one node |
| `List.Sum` | list | sum | Adds up the numbers of a list |

## Logic

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `And` | a, b | result | Returns true only when both inputs are true |
| `Equals` | a, b | result | Tests whether two values are equal, the same way as Logic.Compare with ==: numbers compare by value (2 equals 2.0), text ignores upper and lower case, text that reads as… |
| `GreaterThan` | a, b | result | Returns true when the first number is greater than the second |
| `GreaterThanOrEqual` | a, b | result | Returns true when the first number is greater than or equal to the second |
| `If` | test, trueValue, falseValue | result | Returns one of two values depending on a boolean condition |
| `IsNull` | value | isNull | True when the value is null (nothing came out) |
| `IsNullOrEmpty` | value | isEmpty | True when the value is null, an empty string, an empty list or an empty dictionary |
| `LessThan` | a, b | result | Returns true when the first number is less than the second |
| `LessThanOrEqual` | a, b | result | Returns true when the first number is less than or equal to the second |
| `Logic.Choose` | index, options | value | Picks one of several options by position (0 = first) |
| `Logic.Compare` | a, b?, test?, ignoreCase? | result | Compares two values |
| `Logic.IsBetween` | value, min, max, inclusive? | result | True when a number, text or date lies between a lower and an upper bound (bounds included by default) |
| `Logic.NotEquals` | a, b | result | True when two values are different |
| `Logic.Switch` | value, cases, results, fallback? | value | Gives the result that goes with the first case equal to the value, otherwise the fallback |
| `Logic.TypeOf` | value | type | Names the kind of a value |
| `Logic.Xor` | a, b | result | True when exactly one of the two inputs is true |
| `Not` | value | result | Inverts a boolean value |
| `Or` | a, b | result | Returns true when at least one input is true |

## Math

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Add` | a, b | result | Adds two numbers |
| `Divide` | a, b | result | Divides the first number by the second |
| `Math.Abs` | number | result | Returns the absolute value of a number |
| `Math.Acos` | value, unit? | angle | Inverse cosine: the angle whose cosine is the value (result in degrees by default) |
| `Math.Asin` | value, unit? | angle | Inverse sine: the angle whose sine is the value (result in degrees by default) |
| `Math.Atan` | value, unit? | angle | Inverse tangent: the angle for a slope (result in degrees by default) |
| `Math.Atan2` | y, x, unit? | angle | Angle of the direction (x, y) from the X axis, from -180 to 180 degrees |
| `Math.Ceiling` | number | result | Rounds a number up to the nearest integer |
| `Math.Clamp` | value, min, max | value | Limits a number to a range: below the minimum gives the minimum, above the maximum gives the maximum |
| `Math.Cos` | angle, unit? | value | Cosine of an angle (degrees by default) |
| `Math.Degrees` | radians | degrees | Converts radians to degrees |
| `Math.Exp` | power | value | e raised to a power (the inverse of the natural logarithm) |
| `Math.Floor` | number | result | Rounds a number down to the nearest integer (-2.5 becomes -3 |
| `Math.Formula` | expression, a?, b?, c?, d?, e?, f? | result | Evaluates a formula such as "a * b + 2" or "if(a > 10, a - 10, 0)" over the inputs a to f |
| `Math.IsClose` | a, b, tolerance? | result | True when two numbers differ by no more than the tolerance (the same unit as the numbers) |
| `Math.Lerp` | a, b, t | value | Linear interpolation: a at t = 0, b at t = 1, in between for values between (not limited, so t = 2 goes past b |
| `Math.Ln` | value | value | Natural logarithm (base e) of a positive number |
| `Math.Log` | value, logBase? | value | Logarithm of a positive number to a base (10 by default) |
| `Math.MapRange` | value, fromLow, fromHigh, toLow, toHigh | result | Linearly remaps a value from one range to another (values outside the range extrapolate) |
| `Math.Max` | a, b | result | Returns the larger of two numbers |
| `Math.Min` | a, b | result | Returns the smaller of two numbers |
| `Math.Negate` | number | value | Flips the sign of a number (5 becomes -5) |
| `Math.Percent` | part, total | percent | What percentage the part is of the total (37 of 340 gives 10.88) |
| `Math.Pi` | — | pi | The constant pi (3.14159…) |
| `Math.Pow` | @base, exponent | result | Raises the first number to the power of the second |
| `Math.Radians` | degrees | radians | Converts degrees to radians |
| `Math.Random` | min?, max?, seed? | result | Returns a random number in a range |
| `Math.Round` | number, digits? | result | Rounds a number to the given number of decimal digits (midpoints round away from zero) |
| `Math.RoundToMultiple` | value, multiple | value | Rounds to the nearest multiple of a step, e.g |
| `Math.Sequence` | start, count, step? | numbers | A list of count numbers starting at start and growing by step (the count-based sibling of List.Range) |
| `Math.Sign` | number | sign | -1 for a negative number, 0 for zero, 1 for a positive number |
| `Math.Sin` | angle, unit? | value | Sine of an angle (degrees by default) |
| `Math.Sqrt` | number | result | Returns the square root of a non-negative number |
| `Math.Tan` | angle, unit? | value | Tangent of an angle (degrees by default) |
| `Math.Truncate` | number | value | Drops the decimals, towards zero (2.7 becomes 2, -2.7 becomes -2) |
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
| `Appearance.Focus` | items, otherTransparency?, resetFirst?, document? | items | Focus on some items: they stay as they are and everything else in the model fades by otherTransparency percent (0 = opaque, 100 = invisible) with a TEMPORARY transparenc… |
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
| `Camera.SetStandardView` | view?, items?, paddingFactor?, document? | viewpoint | Sets the camera to a standard view (top, bottom, front, back, left, right, iso) and frames the given items |
| `Camera.ZoomToItems` | items, paddingFactor?, document? | done | Frames the given items in the current view (per-item close-ups, screenshot staging) |
| `Viewpoint.SetSectionBox` | boundingBox, enabled?, document? | done | Applies a section box around a region on the current view (Sectioning > Box, scriptable) |

## Navisworks.Clash.Filter

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Clash.Deduplicate` | results | results, duplicates | Keeps ONE clash per unique item pair |
| `Clash.FilterByAngle` | results, minDegrees?, maxDegrees? | results | Keeps only the clash results whose crossing angle (see ClashResult.Angle) is within a degree range |
| `Clash.FilterByDepth` | results, minDepth?, maxDepth?, units?, document? | results | Keeps clashes whose penetration depth falls in a range, in the unit you name |
| `Clash.FilterByItemProperty` | results, category, property, value1, value2?, mode?, searchAncestors?, caseSensitive? | results | Keeps clashes whose two items' PROPERTY values match a pair of texts, in either order |
| `Clash.FilterByOrientation` | results, shape1?, shape2? | results | Keeps only the clashes between elements of the given box shapes, matched in either order |
| `Clash.FilterBySet` | results, set, which?, invert?, document? | results | Keeps clashes whose items belong to a selection/search set (either one, both, or a specific side) |
| `Clash.FilterBySnapshot` | results, snapshotPath, keep? | results, others | Turns "what is new since last week" into live clash results you can select, group, assign or report: compares the results with a snapshot saved by Clash.SnapshotToFile,… |
| `Clash.FilterByStatus` | results, status | results | Keeps only the clash results with the given status(es) |

## Navisworks.Clash.Group

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Clash.AllGroups` | document? | groups, names, testNames, counts | Every result group of every clash test in the document, as ONE flat list |
| `Clash.GroupResults` | results, groupName, moveExisting?, document? | test, group, added, moved, skipped | Puts an explicit list of clash results into a named group in Clash Detective |
| `Clash.GroupResultsByGridIntersection` | test, document? | test, groupCount | Groups a test's results by the model's own grid: each group is named after the nearest grid intersection and level (e.g |
| `Clash.GroupResultsByLevel` | test, levelNames, levelElevations, units?, document? | test, groupCount | Groups a test's results by nearest level below each clash point (wire your level names and elevations) |
| `Clash.GroupResultsByProximity` | test, radius, units?, document? | test, groupCount | Groups a test's results into clusters whose clash points lie within a radius of the cluster seed |
| `Clash.GroupResultsBySameItem` | test, useItem1?, document? | test, groupCount | Groups a test's results so every clash involving the same element lands in one group (named after the element) |
| `Clash.GroupResultsByStatus` | test, document? | test, groupCount | Groups a test's results by status (New/Active/Reviewed/Approved/Resolved) |
| `ClashGroup.ByName` | test, groupName, document? | group, results, status, count | Finds a clash result group by test name + group name and opens it up: the results inside, the group's own status, and the count |
| `ClashGroup.Info` | group | results, name, status, count, test, testName | Everything about a clash result group, straight from the group object |

## Navisworks.Clash.Report

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Clash.CompareSnapshots` | oldPath, newPath | newResults, resolved, persisting, counts | Diffs two clash snapshots: clashes NEW since the baseline, clashes RESOLVED (disappeared), and clashes PERSISTING in both (with their previous status) |
| `Clash.ResultsTable` | results, units? | table | Makes a table of the clash results you wire in: one row per result with its test, group, name, GUID, status, distance, assignee, description, creation date, the two item… |
| `Clash.SnapshotToFile` | filePath, tests?, document? | filePath, resultCount | Saves a clash-run snapshot (per result: test, item identities, status, distance, clash point) as JSON |
| `Clash.SummaryTable` | tests?, document? | rows, headers, table | Per-test clash counts by status (test × Total/New/Active/Reviewed/Approved/Resolved) |

## Navisworks.Clash.Results

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `ClashResult.Angle` | result | degrees | The angle in degrees (0–90) between the two clashing elements, taken from each element's overall direction (its bounding-box diagonal) |
| `ClashResult.Assign` | result, assignedTo, document? | result | Assigns a clash result, or a whole result group, to a person or trade |
| `ClashResult.ByGuid` | guids, tests?, document? | results, missing | Finds clash results again from their GUIDs |
| `ClashResult.Center` | result | point | The clash point of a result, in document units |
| `ClashResult.Documentation` | result | hasViewpoint, hasRedlines, commentCount | How documented a clash already is |
| `ClashResult.Focus` | results, isolate?, zoom?, select?, paddingFactor?, document? | items | Focuses the view on clash results the way double-clicking one in Clash Detective does: hides everything else (isolate), zooms the camera to the clashing pair, and option… |
| `ClashResult.Info` | result | name, status, distance, description, assignedTo, createdTime, guid, testName, group | Name, status, distance, description, assignee and creation time of a clash result, plus its GUID (what BCF topics and ClashResult.ByGuid use), the name of its test and t… |
| `ClashResult.Items` | result | item1, item2 | The two model items involved in a clash result |
| `ClashResult.Orientation` | result | degrees, shape1, shape2, slope1, slope2 | ClashResult.Angle with world context: the crossing angle PLUS each element's bounding-box shape |
| `ClashResult.Rename` | result, newName, document? | result | Renames a clash result or result group |
| `ClashResult.SaveImage` | result, filePath, width?, height?, document? | filePath | Renders a clash snapshot (scene plus clash highlight) to a .png/.jpg/.bmp file |
| `ClashResult.SetDescription` | result, description, document? | result | Sets the description text of a clash result, or of a whole result group (context for reports and reviews) |
| `ClashResult.SetStatus` | result, status, document? | result | Sets the status of a clash result, or of a whole result group (wire the group from ClashTest.Groups or ClashGroup.ByName) |
| `ClashResult.Size` | result | volume | The size of the clash overlap region |
| `ClashResult.Viewpoint` | result, apply?, document? | viewpoint | The camera viewpoint Navisworks generates for a clash result |

## Navisworks.Clash.Tests

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Clash.RunAllTests` | document? | tests | Runs every Clash Detective test in the document |
| `Clash.Status` | status? | status | A clash status as a dropdown (New/Active/Reviewed/Approved/Resolved) |
| `Clash.Statuses` | newStatus?, active?, reviewed?, approved?, resolved? | statuses | Pick SEVERAL clash statuses with toggles |
| `Clash.Tests` | document? | tests | All Clash Detective tests in a document, including those inside folders |
| `ClashTest.ByName` | name, document? | test | Finds a clash test by its display name (searches folders too) |
| `ClashTest.ClearResults` | test, document? | test | Removes every result (and result group) of a clash test and leaves the test and its settings in place |
| `ClashTest.Create` | name, itemsA, itemsB, testType?, tolerance?, units?, ifExists?, folder?, document? | test | Creates a clash test between two item selections |
| `ClashTest.Delete` | test, document? | deleted | Deletes a clash test and all of its results from the document |
| `ClashTest.Duplicate` | test, newName?, document? | test | Duplicates a clash test |
| `ClashTest.Edit` | test, newName?, testType?, tolerance?, mergeComposites?, itemsA?, itemsB?, units?, document? | test | Edits an existing clash test in place |
| `ClashTest.Groups` | test, document? | groups, names, statuses, counts | All result groups of a clash test |
| `ClashTest.Info` | test, units? | name, status, testType, tolerance, lastRun, resultCount | Name, status, type, tolerance, last run time and result count of a clash test |
| `ClashTest.Name` | test | name | The display name of a clash test |
| `ClashTest.Rename` | test, newName, document? | test | Renames a clash test |
| `ClashTest.Results` | test | results | The individual results of a clash test (grouped results are flattened) |
| `ClashTest.Run` | test, document? | test, resultCount | Runs one clash test now and reports the result count |

## Navisworks.Comments

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `SavedItem.AddComment` | item, body, status?, author?, document? | item | Adds a comment to a saved viewpoint, selection/search set, folder, clash result or result group |
| `SavedItem.ClearComments` | item, document? | item | Deletes every comment on a saved viewpoint, selection/search set, folder, clash result or result group (replace-all with an empty thread) |
| `SavedItem.Comments` | item | bodies, authors, statuses, dates | The comment thread on any saved item (viewpoint, set, folder, clash test): bodies, authors, statuses and creation dates, index-aligned |
| `SavedItem.SetCommentStatus` | item, status, index?, document? | item | Changes the status (New, Active, Approved, Resolved) of one comment, or of every comment, on a saved viewpoint, selection/search set, folder, clash result or result grou… |

## Navisworks.Document

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Application.Version` | — | product, apiVersion | The running Navisworks product name and API version (report headers, compatibility checks) |
| `Document.AppendFiles` | filePaths, document? | document, models | Appends design files to the document |
| `Document.Current` | — | document | The active Navisworks document |
| `Document.Info` | document? | fileName, title, units, modelCount | File name, title, display units and model count of a document, read again on every run |
| `Document.Merge` | filePath, document? | document | Merges another Navisworks file into the document with duplicate resolution |
| `Document.Models` | document? | models | The models (appended source files) loaded in a document, read again on every run |
| `Document.Open` | filePath, document? | document | Opens ONE file into the document, REPLACING its current contents (the headless batch driver) |
| `Document.Refresh` | document? | updated | Refreshes every linked/appended file from disk |
| `Document.Save` | filePath, document? | filePath | Saves the document as .nwf (references to the source files) or .nwd (a published snapshot with the appearance overrides baked in) to the given path |

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
| `Markup.AddCloud` | viewpoint, points, color?, thickness?, document? | viewpoint | EXPERIMENTAL: draws a revision-cloud redline through the given markup-space points (undocumented Navisworks API) |
| `Markup.AddNumberTag` | viewpoint, number, x, y, radius?, comment?, color?, thickness?, document? | viewpoint | EXPERIMENTAL: draws a circled number on a saved viewpoint and optionally attaches a comment |
| `Markup.AddShape` | viewpoint, shape, x1, y1, x2, y2, color?, thickness?, document? | viewpoint | EXPERIMENTAL: draws a line, arrow or ellipse redline on a saved viewpoint (undocumented Navisworks API) |
| `Markup.AddText` | viewpoint, text, x, y, color?, thickness?, document? | viewpoint | EXPERIMENTAL: draws a text redline on a saved viewpoint (undocumented Navisworks API) |
| `Markup.Clear` | viewpoint, document? | viewpoint | EXPERIMENTAL: removes every redline markup from a saved viewpoint (undocumented Navisworks API) |
| `Markup.List` | viewpoint, document? | count, types, texts, positions | EXPERIMENTAL: lists the redline markups on a saved viewpoint |

## Navisworks.Model

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Model.Info` | model | fileName, sourceFileName, units, rootItem | The cached and original source file paths of a model, its native units (unit-mismatch audits across appended files) and its root model item |
| `Model.Remove` | model, document? | removed | Removes a WHOLE appended source model from the document (accepts a Model, a 0-based index, or a file name) |
| `Model.Snapshot` | items, properties | snapshot | Captures the named properties of the given items as a dictionary keyed by each item's instance GUID (items without one are keyed "path:" plus their tree path) |
| `Model.Statistics` | items?, by?, document? | table | Counts items per model file, class or layer: how many items, how many carry geometry and each group's share of all items |
| `Models.RootItems` | document? | rootItems | The root model items of every model loaded in a document, read again on every run |

## Navisworks.ModelItem

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `ModelItem.AncestorNameMatches` | item, text, mode?, includeSelf?, caseSensitive? | matches, ancestor, ancestorName | Walks an item's ancestor chain (nearest first) and tests each name against the text: contains / starts with / ends with, or their "doesn't" negations |
| `ModelItem.AncestorPropertyMatches` | item, categoryName, propertyName, text, mode?, includeSelf?, caseSensitive? | matches, ancestor, value | The general form of ModelItem.AncestorNameMatches: walks an item's ancestor chain (nearest first) and tests ANY property you name |
| `ModelItem.Ancestors` | item, includeSelf? | ancestors | The chain of parents of a model item, up to its model root |
| `ModelItem.BoundingBox` | item, ignoreHidden? | boundingBox | The axis-aligned bounding box of a model item, in document units |
| `ModelItem.Children` | item | children | The direct children of a model item |
| `ModelItem.CombinedBoundingBox` | items, ignoreHidden? | boundingBox | ONE bounding box fitting all the given items together (per-group when wired a list of groups) |
| `ModelItem.CommonAncestor` | items | ancestor | The deepest common ancestor of the given items in the selection tree |
| `ModelItem.Descendants` | item | descendants | All descendants of a model item (the whole subtree below it) |
| `ModelItem.IfcGuid` | item | ifcGuid | The 22-character IFC GlobalId of an item: the value of its GlobalId / IfcGUID / IFC GUID / Guid property when it has one (as the Navisworks IFC import creates it), other… |
| `ModelItem.Info` | item | name, className, classDisplayName, guid, hasGeometry, isHidden | Everything quick to know about a model item in one node: its display name (the class name when it has none), class name and localized class name (layer/group/geometry de… |
| `ModelItem.ModelName` | item | modelName | The name of the model/file an item comes from (the root of its selection tree, e.g |
| `ModelItem.Parent` | item | parent | The parent of a model item (null for a model root) |
| `ModelItem.Path` | item, separator? | path | The selection-tree path of an item as one text, from its model file down to the item ("Structure.nwc > Level 1 > Walls > Basic Wall") |
| `ModelItem.ReferencePoints` | item | bboxCenter, bboxMin, localOrigin | Candidate base/reference points of an item, in world document units |
| `ModelItem.SourceInfo` | item | sourceFileName, itemType, model | One-node answer to "which file did this element come from and what is it": the source file name, the Item-tab Type (falling back to the item's class name), and the ownin… |

## Navisworks.Properties

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Properties.AsDictionary` | item | properties | Every property of an item flattened to a "Category.Property" → value dictionary (full data dump) |
| `Properties.Categories` | item | categories | The property category names available on a model item |
| `Properties.CustomTabs` | modelItem | tabNames | The user-defined property tabs on an item (discovery/QA before SetCustom or RemoveCustomTab) |
| `Properties.Discover` | items, maxSamples? | table | Lists every property the given items carry, one row per category and property with how many items have it (Items), how many different values it holds (Distinct, counted… |
| `Properties.HasProperty` | item, categoryName, propertyName | hasProperty | True when the item carries the property |
| `Properties.InCategory` | item, categoryName | names, values | All property names and values inside one category of an item |
| `Properties.RemoveCustomTab` | modelItems, tabName | modelItems, removedCount | Removes a user-defined property tab from items |
| `Properties.RenameCustomTab` | modelItems, tabName, newTabName | modelItems | Renames a user-defined property tab in place (same properties, same internal name |
| `Properties.SetCustom` | modelItems, names, values, tabName?, merge? | modelItems | Writes ONE set of names and values as a user-defined property tab onto every item you give it |
| `Properties.SetCustomFromTable` | table, modelItems?, columns?, tabName?, keyColumn?, merge?, document? | modelItems, written, missing | Writes a table onto model items as a user-defined property tab, a DIFFERENT row for every item |
| `Properties.ToTable` | items, properties? | table | Reads the named properties of every item into a table with one row per item and one column per name ("Element.Category", "Item\|Layer", a bare property name, or @Name, @P… |
| `Properties.Value` | item, categoryName, propertyName | value | Reads a property value from a model item, converted to a plain value |
| `Properties.ValueAsString` | item, categoryName, propertyName | text | Reads a property value as text |

## Navisworks.Search

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Search.ByGuid` | guids, document? | items, missing | Finds the items whose instance GUID equals any of the given GUIDs (text, a 22-character IFC GlobalId, or GUID values) in one pass over the model |
| `Search.ByProperty` | categoryName, propertyName, value?, mode?, resolveTo?, within?, document? | items | Finds every model item by one property, like Find Items with its condition drop-down: equals a value, contains text, matches a wildcard pattern (* and ?), is >, >=, < or… |

## Navisworks.Selection

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Captured Selection` *(interactive)* | — | items | Snapshots the current Navisworks selection and keeps it, so the graph runs on that fixed set even after you select something else (Selection.Current, in contrast, reads… |
| `Selection.AddToCurrent` | items, document? | items | Adds items to the existing Navisworks selection (union) and returns the result |
| `Selection.Clear` | document? | cleared | Clears the interactive Navisworks selection |
| `Selection.Current` | resolveTo?, document? | items | The model items currently selected in Navisworks, read again on every run (press Run after selecting something else and the new selection comes through) |
| `Selection.Invert` | document? | items | The items that are NOT in the current selection (Navisworks' own invert, so whole untouched branches come back as one item each) |
| `Selection.Remove` | items, document? | items | Takes the given items out of the current Navisworks selection and returns what is still selected |
| `Selection.Resolve` | items, level? | items | Re-selects items at another selection-tree level |
| `Selection.SelectAll` | document? | items | Selects everything in the Navisworks UI and returns the selected items |
| `Selection.SetCurrent` | items, document? | items | Replaces the interactive Navisworks selection with the given items |

## Navisworks.SelectionSets

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `SelectionSet.ByName` | name, document? | selectionSet | Finds a saved selection or search set by its display name (searches folders too) |
| `SelectionSet.Create` | name, items, folder?, document? | selectionSet | Creates a saved selection set from the given items, filed in the folder you name (a folder from SelectionSets.CreateFolder, a folder name, or a path such as "Walls/Level… |
| `SelectionSet.CreateFromSearch` | name, categoryName, propertyName, value?, mode?, folder?, document? | selectionSet | Creates a live SEARCH set from a property rule, with the same modes as Search.ByProperty (equals, contains, wildcard, >, >=, <, <= or exists |
| `SelectionSet.Delete` | name, document? | deleted | Deletes a saved selection or search set by name (searches folders too) |
| `SelectionSet.Duplicate` | selectionSet, newName?, document? | selectionSet | Duplicates a saved selection or search set in its folder (a search set stays a live search) |
| `SelectionSet.Info` | selectionSet, includeCount?, document? | name, kind, itemCount, folder | What a saved set is: its name, whether it is a fixed "selection" or a live "search" set, how many items it selects right now (a search set is evaluated once to count |
| `SelectionSet.Items` | selectionSet, document? | items | The model items a saved set selects |
| `SelectionSet.MoveToFolder` | selectionSet, folder, document? | selectionSet | Moves a saved selection or search set into a folder (appended at the end) |
| `SelectionSet.Name` | selectionSet | name | The display name of a saved selection or search set |
| `SelectionSet.Rename` | selectionSet, newName, document? | selectionSet | Renames a saved selection or search set (accepts the set or its current name |
| `SelectionSets.All` | document? | selectionSets | All saved selection and search sets in a document, including those inside folders |
| `SelectionSets.BulkByPropertyValues` | categoryName, propertyName, folderName?, document? | selectionSets, values | One search set per distinct value of a property (e.g |
| `SelectionSets.CreateFolder` | name, parentFolder?, document? | folder | Creates a folder in the Sets window, optionally nested under a parent folder |
| `SelectionSets.DeleteFolder` | folder, deleteContents?, document? | deleted | Deletes a Sets window folder |
| `SelectionSets.InFolder` | folder?, recursive?, document? | selectionSets, names, subfolders, count | All saved selection and search sets inside a folder, in Sets window order |
| `SelectionSets.RenameFolder` | folder, newName, document? | folder | Renames a Sets window folder (accepts the folder, its current name or a path like "Walls/Level 1" |
| `SelectionSets.SortFolder` | folder?, recursive?, document? | folder | Sorts a Sets window folder's contents alphabetically by name (A to Z, ignoring case |

## Navisworks.TimeLiner

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `TimeLiner.AutoAttachByProperty` | category, property, document? | attachedCount, unmatchedTasks | For every TimeLiner task (subtasks included), finds all items whose property value equals the task name and attaches them |
| `TimeLiner.Tasks` | document? | tasks | All TimeLiner tasks in a document, with subtasks flattened into one list |
| `TimelinerTask.AttachSet` | task, setName, document? | task | Attaches a saved selection/search set to a task as a LIVE link (like Attach Set in the UI) |
| `TimelinerTask.Create` | name, plannedStart, plannedEnd, items?, taskType?, document? | task | Creates a top-level TimeLiner task with planned dates and optionally attaches model items |
| `TimelinerTask.Delete` | task, document? | deleted | Deletes a TimeLiner task together with its subtasks |
| `TimelinerTask.Info` | task | name, displayId, plannedStart, plannedEnd, actualStart, actualEnd, taskType, progress | Name, id, planned and actual dates, task type and progress of a TimeLiner task |
| `TimelinerTask.Items` | task, document? | items | The model items attached to a TimeLiner task |
| `TimelinerTask.SetActual` | task, start, end, document? | task | Sets a task's ACTUAL start and end dates (the planned dates are untouched |
| `TimelinerTask.SetDates` | task, plannedStart, plannedEnd, document? | task | Updates a task's planned start/end dates in place |
| `TimelinerTask.SetProgress` | task, percent, document? | task | Sets a task's percent complete (0-100 |

## Navisworks.Transform

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `ModelItem.GetTransform` | item | origin, matrix, hasOverride | Reads an item's current (active) transform: origin = its translation (a practical base point), matrix = 16 numbers row-major (feed ModelItem.SetTransform to round-trip),… |
| `ModelItem.MoveTo` | items, target, document? | items | Moves model items so the centre of their combined bounding box lands on a target point |
| `ModelItem.ResetTransform` | items?, resetAll?, document? | items | Removes permanent transform overrides, restoring items to their original position |
| `ModelItem.RotateAboutAxis` | items, origin, axis, degrees, accumulate?, document? | items | Rotates model items by an angle (degrees) about an axis through a point |
| `ModelItem.Scale` | items, factor, about?, accumulate?, document? | items | Scales model items uniformly about a point |
| `ModelItem.SetTransform` | items, matrix, document? | items | Sets the permanent transform override of model items to an absolute 4×4 matrix (16 numbers, row-major, translation at indices 3/7/11) |
| `ModelItem.Translate` | items, vector, accumulate?, document? | items | Moves model items by a vector, in document units (chain Units.Convert for meters/feet) |

## Navisworks.Units

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Units.All` | — | names | Every unit name accepted by Units.Convert and Units.ScaleFactor |
| `Units.Convert` | value, fromUnits, toUnits, dimension? | value | Converts a length, an area or a volume from one unit to another (e.g |
| `Units.Current` | document? | units | The display units of a document (all API lengths, areas and volumes use them), read again on every run |
| `Units.ScaleFactor` | fromUnits, toUnits, dimension? | factor | The multiplier that converts a length, an area or a volume from one unit to another |

## Navisworks.Viewpoints

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `SavedViewpoint.Apply` | viewpoint, document? | viewpoint | Makes a saved viewpoint the current view (camera, plus any saved overrides) |
| `SavedViewpoint.ByName` | name, document? | viewpoint | Finds a saved viewpoint by its display name (searches folders too) |
| `SavedViewpoint.CopyOverrides` | fromViewpoint, toViewpoint, document? | viewpoint | Copies the appearance the way one saved view looks |
| `SavedViewpoint.Delete` | name, document? | deleted | Deletes a saved viewpoint by name (searches folders too) |
| `SavedViewpoint.Duplicate` | viewpoint, newName?, document? | viewpoint | Duplicates a saved viewpoint in its folder, copying its camera and any baked appearance overrides |
| `SavedViewpoint.Folder` | viewpoint, document? | folderPath, folder | The folder containing a saved viewpoint: its path as "A/B" ("" for top-level viewpoints) and the folder itself |
| `SavedViewpoint.Info` | viewpoint, document? | name, folder, hasSection, hasOverrides, commentCount, position, lookAt | Reads a saved viewpoint: its name, folder path ("A/B", "" at the top level), whether it carries a section box, whether it has baked appearance or visibility overrides, i… |
| `SavedViewpoint.MoveToFolder` | viewpoint, folder, document? | viewpoint | Moves a saved viewpoint into a folder (appended at the end) |
| `SavedViewpoint.Name` | viewpoint | name | The display name of a saved viewpoint |
| `SavedViewpoint.Rename` | viewpoint, newName, document? | viewpoint | Renames a saved viewpoint (accepts the viewpoint or its current name |
| `SavedViewpoint.Update` | viewpoint, document? | viewpoint | Re-captures the current view into an existing saved viewpoint |
| `Viewpoint.SaveCurrent` | name, document? | viewpoint | Saves the current view as a new saved viewpoint |
| `Viewpoint.SaveWithOverrides` | name, folderName?, document? | viewpoint | Saves the current view AND the current temporary color/transparency/hidden overrides into the viewpoint (Navisworks CaptureRuntimeOverrides) |
| `Viewpoint.VisibleItems` | items, viewpoint?, fullyInside?, document? | visibleItems, outsideItems, mask, containsAny, report | Checks which of the given items a viewpoint can see (bounding box vs the camera frustum) |
| `ViewpointPackageFile.Parse` | json | result | Parses a package from JSON with node-friendly errors: malformed text and files written by a newer CamelGraph both fail with a message that says what to do, never a raw s… |
| `Viewpoints.All` | document? | viewpoints | All saved viewpoints in a document, including those inside folders |
| `Viewpoints.FromClashResults` | results, folderName?, document? | viewpoints | Batch-generates one saved viewpoint per clash result, camera aimed at the clash and named after the result |

## Navisworks.Viewpoints.Files

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Viewpoints.ExportFile` | filePath, viewpoints?, document? | filePath, count, report | Exports saved viewpoints |
| `Viewpoints.ImportFile` | filePath, folderName?, overwrite?, document? | viewpoints, count, report | Rebuilds the viewpoints from a Viewpoints.ExportFile package in THIS model: camera, section box and folder structure |

## Navisworks.Viewpoints.Folders

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Viewpoints.CreateFolder` | name, parentFolder?, document? | folder | Creates a folder in the Saved Viewpoints window, optionally nested under a parent folder |
| `Viewpoints.DuplicateFolder` | folder, newName, document? | folder | Duplicates a Saved Viewpoints folder |
| `Viewpoints.InFolder` | folder?, recursive?, document? | viewpoints, names, subfolders, count | All saved viewpoints inside a folder, in Saved Viewpoints window order |
| `Viewpoints.RenameFolder` | folder, newName, document? | folder | Renames a Saved Viewpoints folder (accepts the folder or its current name |
| `Viewpoints.SortFolder` | folder?, recursive?, document? | folder | Sorts a Saved Viewpoints folder's contents alphabetically by name (A→Z) |

## Report

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Report.Html` | title, sections, subtitle? | html | Builds a self-contained HTML report from tables and text ("# Heading" makes a heading) |
| `Report.Markdown` | title, sections, subtitle? | markdown | Builds a Markdown report from tables and text |

## String

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `String.Concat` | a, b | result | Joins two strings into one |
| `String.Contains` | text, searchFor, ignoreCase? | result | Tests whether a string contains the given substring |
| `String.EndsWith` | text, searchFor, ignoreCase? | result | Tests whether a string ends with the given suffix |
| `String.Format` | format, values | text | Fills a .NET composite format such as "{0} is {1:0.00} m" with the wired values (invariant culture, "{{" and "}}" are literal braces) |
| `String.FromNumber` | number, decimals?, thousandsSeparator?, prefix?, suffix? | text | Formats a number as text with fixed decimals, an optional thousands separator and a prefix/suffix (invariant culture) |
| `String.FromObject` | obj | result | Converts any value (including whole lists) to its display string |
| `String.IndexOf` | text, search, ignoreCase?, startIndex? | index | Returns the zero-based index of the first occurrence of a text (-1 when absent) |
| `String.IsBlank` | text | isBlank | Tests whether a text is null, empty or only whitespace |
| `String.Join` | separator, list | result | Joins the elements of a list into a single string with a separator |
| `String.LastIndexOf` | text, search, ignoreCase? | index | Returns the zero-based index of the last occurrence of a text (-1 when absent) |
| `String.Left` | text, count | text | Returns the first characters of a text (the whole text when the count is larger) |
| `String.Length` | text | result | Returns the number of characters in a string |
| `String.Lines` | text, removeEmpty? | lines | Splits a text into a list of lines (handles \r\n, \n and \r), optionally dropping the empty ones |
| `String.PadLeft` | text, width, padChar? | text | Pads a text on the left with a character up to a total width ("7" becomes "007" with width 3 and padChar 0) |
| `String.PadRight` | text, width, padChar? | text | Pads a text on the right with a character up to a total width |
| `String.RegexIsMatch` | text, pattern, ignoreCase? | isMatch | Tests whether a .NET regular expression matches anywhere in a text (use ^ and $ to match the whole text) |
| `String.RegexMatch` | text, pattern, ignoreCase? | found, match, groups | Finds the first match of a regular expression: whether it was found, the matched text and the capture groups 1..n |
| `String.RegexMatches` | text, pattern, ignoreCase? | matches | Returns the text of every match of a regular expression as a list (empty when nothing matches) |
| `String.RegexReplace` | text, pattern, replacement, ignoreCase? | text | Replaces every match of a regular expression |
| `String.RegexSplit` | text, pattern | list | Splits a text into a list of parts wherever a regular expression matches |
| `String.RemoveDiacritics` | text | text | Removes accents from letters ("é" becomes "e") so names compare and sort without them |
| `String.Repeat` | text, count, separator? | text | Repeats a text a number of times (0 to 10000), optionally with a separator between the copies |
| `String.Replace` | text, searchFor, replaceWith, ignoreCase? | result | Replaces all occurrences of a substring with another string |
| `String.Reverse` | text | text | Reverses the characters of a text |
| `String.Right` | text, count | text | Returns the last characters of a text (the whole text when the count is larger) |
| `String.Split` | text, separator, removeEmpty?, trim? | list | Splits a string into a list of substrings around a separator |
| `String.StartsWith` | text, searchFor, ignoreCase? | result | Tests whether a string starts with the given prefix |
| `String.Substring` | text, startIndex, length? | result | Extracts part of a string from a start index (-1 length = to the end) |
| `String.Template` | template, dictionary, onMissing? | text | Replaces {name} placeholders in a text with the values of a dictionary ("{{" and "}}" are literal braces) |
| `String.ToLower` | text | result | Converts a string to lowercase |
| `String.ToNumber` | text, decimalSeparator?, ignoreUnits? | result | Converts a numeric string to a number |
| `String.ToTitleCase` | text | text | Capitalises the first letter of every word and lowercases the rest ("bim COORDINATION" becomes "Bim Coordination") |
| `String.ToUpper` | text | result | Converts a string to uppercase |
| `String.Trim` | text, chars? | result | Removes whitespace (or the given characters) from both ends of a string |
| `String.TrimEnd` | text, chars? | text | Removes whitespace (or the given characters) from the end of a text |
| `String.TrimStart` | text, chars? | text | Removes whitespace (or the given characters) from the start of a text |

## System

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Graph.Folder` | — | folder | The folder of the graph file, which is where a relative path in a file node starts (for a graph that was never saved: Documents\CamelGraph |
| `System.Environment` | — | userName, machineName, osVersion, currentDirectory, tempPath, documentsPath, appDataPath | Reports the Windows user, computer name, operating system and the current, temp, Documents and AppData folders |
| `System.OpenPath` | path, reveal? | path | Opens a file or folder with its default application, or shows it selected in Explorer when reveal is true |
| `System.Run` | executable, arguments?, workingDirectory?, timeoutSeconds? | exitCode, output, error | Runs a program with arguments, waits for it (stopped after Advanced > timeoutSeconds, 60 by default) and returns its exit code, output and error text |
| `Web.Download` | url, path, overwrite?, headers?, timeoutSeconds? | path, status, ok, sizeBytes | Downloads a file (IFC, BCF, zip, picture, anything) from an http(s) address and saves it byte for byte |
| `Web.Get` | url, headers?, timeoutSeconds? | status, body, ok | Downloads text from an http(s) address with GET |
| `Web.Post` | url, body, contentType?, headers?, timeoutSeconds? | status, body, ok | Sends data to an http(s) address with POST (JSON by default, e.g |

## Table

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Table.AddColumn` | table, name, values | table | Adds a column at the end: a list with one value per row, or a single value repeated on every row |
| `Table.AddFormulaColumn` | table, name, formula | table | Adds a column calculated per row from the others, e.g |
| `Table.Column` | table, column | values | The cells of one column, top to bottom |
| `Table.Concat` | tables | table | Stacks tables one below the other, matching columns by name |
| `Table.Distinct` | table, columns? | table | Keeps only the first row for each distinct value of the given columns (a list of names or one text with commas), or of the whole row when none are given |
| `Table.Filter` | table, column, test?, value?, ignoreCase? | matched, rejected | Splits a table by a test on one column |
| `Table.FromColumns` | columns, headers? | table | Makes a table from columns: a list of lists, one per column, with optional names |
| `Table.FromCsvFile` | path, delimiter?, firstRowIsHeader? | table | Reads a CSV file straight into a table (numbers become numbers, everything else stays text) |
| `Table.FromDictionaries` | dictionaries | table | Makes a table from a list of dictionaries (one row each) |
| `Table.FromExcelFile` | path, sheet?, firstRowIsHeader? | table | Reads an Excel worksheet straight into a table |
| `Table.FromRows` | rows, headers?, firstRowIsHeader? | table | Makes a table from rows of cells and column names (or from the first row) |
| `Table.GroupBy` | table, by, aggregations | table | Groups rows by one or more columns (a list of names or one text with commas) and works out count, sum, average, min, max, median, first, last, list or distinct per group |
| `Table.Info` | table | rowCount, columnCount, headers | How many rows and columns a table has, and its column names (the headers output is the list of names, in column order) |
| `Table.Join` | left, right, leftKey, rightKey?, kind? | table | Joins two tables on one or more key columns (inner, left or outer) |
| `Table.Pivot` | table, rowColumn, columnColumn, valueColumn?, aggregation? | table | Cross-tabulates: rows from one column, columns from another, each cell the sum (or count, average …) of a third |
| `Table.RemoveColumns` | table, columns | table | Drops the listed columns and keeps the rest |
| `Table.RenameColumn` | table, column, newName | table | Renames one column |
| `Table.Row` | table, index | row | One row of a table as a dictionary from column name to cell (0 is the first row |
| `Table.Rows` | table | rows | The rows of a table as a list of lists of cells (for Excel.WriteToFile, CSV.WriteToFile and the List nodes) |
| `Table.SelectColumns` | table, columns | table | Keeps only the listed columns, in that order |
| `Table.SetColumn` | table, name, values | table | Replaces the cells of a column in place, keeping its position and name, or adds the column at the end when there is none of that name |
| `Table.Slice` | table, start?, count? | table | Takes count rows from a starting row (count -1 takes all the rest) |
| `Table.Sort` | table, columns, descending? | table | Sorts the rows by one or more columns, as a list of names or one text ("Level, -Length" sorts by level, then longest first) |
| `Table.ToCsvFile` | table, path, delimiter? | path | Writes one table, with its column names, to a CSV file |
| `Table.ToDictionaries` | table | dictionaries | The rows of a table as dictionaries (column name to cell) |
| `Table.ToExcelFile` | table, path, sheet?, append? | path | Writes one table, with its column names, to an Excel worksheet |
| `Table.ToText` | table, format? | text | Renders a table as Markdown, CSV, tab-separated or an HTML table |
| `Table.Unmatched` | left, right, leftKey, rightKey? | table | The rows of the left table that find no partner in the right table, using the same keys and the same matching as Table.Join (GUIDs match whatever their case, a blank key… |

## Utility

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Reroute` *(interactive)* | in | out | A wire waypoint that passes its value through unchanged |

## Workflow

| Node | Inputs | Outputs | What it does |
|---|---|---|---|
| `Flow.Require` | value, condition, message? | value | Stops with your own error message when a condition is false |
| `Flow.Then` | value, after | value | Passes a value through unchanged AFTER the wired 'after' nodes have run |
| `Flow.Try` | value, fallback? | result, failed, error | Carries on after a failure: gives the node's result, or your fallback plus the error text when that node failed |
| `Flow.Wait` | value, seconds? | value | Waits the given number of seconds (at most 600 |
| `Flow.When` | value, condition | value | Runs the nodes wired after it only when the condition is true |
| `Loop.Collect` *(interactive)* | loop, value | results | Closes a loop and collects one value per iteration |
| `Loop.Item` *(interactive)* | items | item, index, count, loop | Yields the current item of a loop |
| `Workflow.ForEach` | items, actions, onError? | results | Runs a sequence of actions on each item, one item fully before the next (zoom, isolate, save viewpoint, then the next item), the per-item ordered loop that wiring and la… |

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

## Retired nodes

These still load and run in saved graphs, but are no longer offered in the library. Use the replacement in new graphs.

| Retired node | Use instead |
|---|---|
| `BoundingBox.FromPoints` | Use BoundingBox.Union |
| `ClashResult.Comments` | SavedItem.Comments |
| `ClashTest.ResultsByStatus` | ClashTest.Results followed by Clash.FilterByStatus |
| `DateTime.AddDays` | DateTime.Add |
| `DateTime.AddHours` | DateTime.Add |
| `DateTime.AddMinutes` | DateTime.Add |
| `DateTime.AddMonths` | DateTime.Add |
| `DateTime.AddYears` | DateTime.Add |
| `DateTime.AgeInDays` | DateTime.DaysBetween |
| `Directory.FindFiles` | Directory.Find |
| `Directory.GetDirectories` | Directory.Find |
| `Directory.GetFiles` | Directory.Find |
| `List.Join` | List.Merge |
| `List.SortDescending` | List.Sort with 'descending' ticked |
| `Markup.AddArrow` | Markup.AddShape |
| `Markup.AddEllipse` | Markup.AddShape |
| `Markup.AddLine` | Markup.AddShape |
| `Model.FileName` | Model.Info |
| `Model.RootItem` | Model.Info |
| `Model.Units` | Model.Info |
| `ModelItem.ClassInfo` | ModelItem.Info |
| `ModelItem.DisplayName` | ModelItem.Info |
| `ModelItem.GeometryLeaves` | Selection.Resolve |
| `ModelItem.HasGeometry` | ModelItem.Info |
| `ModelItem.InstanceGuid` | ModelItem.Info |
| `ModelItem.IsHidden` | ModelItem.Info |
| `ModelItem.ObjectAncestor` | Selection.Resolve |
| `Property.Info` | Properties.InCategory |
| `Search.ByPropertyCompare` | Search.ByProperty |
| `Search.ByPropertyContains` | Search.ByProperty |
| `Search.ByPropertyValue` | Search.ByProperty |
| `Search.ByPropertyWildcard` | Search.ByProperty |
| `Search.HasCategory` | Search.ByProperty |
| `Search.HasProperty` | Search.ByProperty |
| `Search.InItems` | Search.ByProperty |
| `Table.Headers` | Table.Info |
| `Table.JoinByKey` | Table.Join |

