# What's new in 0.45

About 220 new nodes, three new inputs, a table type, conditional flow, and fixes to the editor and the ribbon. The full list is the [node catalogue](NODE_CATALOG.md); [RECIPES.md](RECIPES.md) shows how the new nodes fit together by role, and [plans/node-library-gaps.md](plans/node-library-gaps.md) is the review that asked for them.

## Tables

A table is one value on one wire. `Table.FromRows`, `FromDictionaries`, `FromColumns`, `FromCsvFile`, `FromExcelFile` make one; `SelectColumns`, `RemoveColumns`, `RenameColumn`, `AddColumn`, `AddFormulaColumn`, `Filter`, `Sort`, `Distinct`, `Concat`, `Slice`, `GroupBy`, `Pivot`, `Join` change it; `Rows`, `Headers`, `Info`, `Column`, `Row`, `ToDictionaries`, `ToText`, `ToCsvFile`, `ToExcelFile` read it back. **Watch Table** shows a table as a grid. `Report.Html` and `Report.Markdown` build a report from tables and text. `Properties.ToTable` turns model items and property names into a table, `Properties.Discover` lists what properties a model actually has. The sample *Table Summary from Text* shows the pattern.

## Conditional flow

* `Flow.When` runs everything after it only when a condition is true; when it is false those nodes are skipped (shown idle, not red).
* `Flow.Try` carries on after a failure: the result, a fallback when the node before it failed, and the error text.
* `Flow.Wait` pauses; `Logic.Choose` and `Logic.Switch` pick between many values; `Logic.Compare` compares numbers, text and dates with a test chosen from a list.

## Fundamentals

* **Math:** trigonometry (degrees or radians), logarithms, `Clamp`, `Lerp`, `Percent`, `RoundToMultiple`, `Sequence`, and `Math.Formula` — type `a * b + 2` or `if(a > 10, a - 10, 0)` over the inputs a to f.
* **List:** `Sum`, `Average`, `Median`, `Percentile`, `StandardDeviation`, `Statistics`, `CumulativeSum`, `CountBy`, `Histogram`, `Duplicates`, `MostCommon`, `FilterByValue`, `Zip`, `Pairs`, `WithIndex`, `SortDescending`, `Shuffle`, `TakeWhile`, `DropWhile`.
* **String:** `Format`, `Template`, regular expressions (`RegexIsMatch`, `RegexMatch`, `RegexMatches`, `RegexReplace`, `RegexSplit`), padding, `Lines`, `IndexOf`, `ToTitleCase`, `Number.Format` and more. **DateTime:** `Components` (ISO week), `AddMonths`, `StartOf`, `EndOf`, `Range`, `AgeInDays`, `Compare`. **Dictionary:** `Merge`, `ContainsKey`, `RemoveKey`, `FromRows`, `ToRows`, `Invert`.
* **Colour, geometry:** `Color.ToHex`, `ByHSV`, `Palette` (colour-blind safe), vector arithmetic, point and bounding-box maths.
* **Files and system:** path nodes, `File.Info/Hash/Copy/Move/Delete`, `Directory.FindFiles` (sorted, limited), append writers (`Text.AppendToFile`, `CSV.AppendToFile`), `Log.Write`, `Zip.*`, `System.Environment/OpenPath/Run`, `Web.Get/Post`. Everything that writes, launches or posts is marked as changing things, so the Script Player asks before running it.
* **Inputs:** `Integer`, `Date` and `Choice` (a pick-list with your own options; a dropdown in the Player). The Number, Number Slider and Integer Slider nodes now use the same scrub field as every other number.

## For the Navisworks side

`IFC.GuidEncode/GuidDecode` and `ModelItem.IfcGuid` (the IFC GlobalId bridge), `Search.ByGuid`, `Model.Snapshot`, `Model.Statistics`, `Selection.Invert/Remove`, `SelectionSet.Info/Duplicate`, `ClashTest.Edit/Delete/Duplicate/ClearResults`, `SavedViewpoint.Info/Update`, `Camera.SetStandardView`, `Appearance.Focus`, `TimelinerTask.SetProgress/SetActual/Delete`, `ModelItem.Scale/MoveTo`.

The Navisworks nodes in this list could only be compiled and unit-tested for their logic here — nothing can run the Navisworks API outside Navisworks — so please try them on a real model and report anything odd. The ones that rely on behaviour not seen in the code before: `Selection.Remove`, `ModelItem.IfcGuid` (which property names an IFC import creates), `Model.Statistics` by layer, `SelectionSet.Duplicate`, `ClashTest.Edit/ClearResults/Delete`, `SavedViewpoint.Update`, `SavedViewpoint.Info` (section on 2025+ is not reported), `Camera.SetStandardView` (conventions: Z up, +Y north), `TimelinerTask.SetActual`, `ModelItem.Scale/MoveTo`, `Appearance.Focus`.

## Editor and ribbon

* The **Run Last** ribbon button is gone and the Player button is black and white like the Dyncamelo one.
* The node library is easier to browse: `Navisworks.Clash` is split into Tests, Results, Filter, Group and Report; Viewpoints into Folders and Files; list statistics sit under `List.Statistics`. Categories are not part of a node's identity: saved graphs are unaffected.
* The crash guard now also covers a node whose visual cannot be built (a WPF/XAML load failure soon after an editor action): it is written to `%APPDATA%\Dyncamelo\errors.log` with the XAML file and position, the node is taken off the canvas and the status bar says so, instead of Navisworks closing. If Navisworks still closes when you add a node, please send that file.
* The node catalogue names the kind of every multi-output node's sockets.
