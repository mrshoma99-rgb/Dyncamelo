using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;
using Dyncamelo.Nodes.Text;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Rule-based clash noise reduction (user wishlist, v0.30): property-pair
/// filtering ("Pipe vs Wall" by data, not geometry), set-membership filtering
/// (ignore-lists, scopes), penetration-depth filtering with units, and
/// duplicate-pair removal. The geometric counterpart lives in
/// <see cref="ClashTriageNodes"/> (Clash.FilterByOrientation).
/// </summary>
[NodeCategory("Navisworks.Clash")]
public static class ClashFilterNodes
{
    /// <summary>Keeps clashes whose two items' property values match a pair of texts.</summary>
    /// <param name="results">The clash results (e.g. from ClashTest.Results).</param>
    /// <param name="category">The property category (tab) name, as the Properties window shows it (e.g. "Element", "Item").</param>
    /// <param name="property">The property name inside that category (e.g. "Category", "System").</param>
    /// <param name="value1">Text one item's property value must match. Empty = any.</param>
    /// <param name="value2">Text the other item's property value must match (either order). Empty = any.</param>
    /// <param name="mode">How the texts must match: contains, equals, starts with or ends with.</param>
    /// <param name="searchAncestors">True (default) also reads the property from the item's ancestors when the item itself lacks it — clash items are often geometry leaves while the data sits on the element or file above.</param>
    /// <param name="caseSensitive">True matches exact casing; false (default) ignores case.</param>
    /// <returns>The matching results, input order preserved.</returns>
    [NodeName("Clash.FilterByItemProperty")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription(
        "Keeps clashes whose two items' PROPERTY values match a pair of texts, in either order — the " +
        "semantic clash rule: category \"Element\", property \"Category\", value1 \"Pipe\", value2 " +
        "\"Wall\" keeps pipe-vs-wall clashes whichever side the pipe landed on. An empty value matches " +
        "anything (one-sided rules). Looks up the property on the item and, by default, its ancestors — " +
        "clash items are often bare geometry while the data lives on the element above. The geometric " +
        "sibling is Clash.FilterByOrientation.")]
    [NodeSearchTags("clash", "filter", "property", "category", "discipline", "pair", "pipe", "wall", "rule", "semantic")]
    [return: NodeName("results")]
    public static List<ClashResult> FilterByItemProperty(
        IEnumerable<ClashResult> results,
        string category,
        string property,
        string value1,
        string value2 = "",
        [NodeChoices("contains", "equals", "starts with", "ends with")]
        string mode = "contains",
        bool searchAncestors = true,
        bool caseSensitive = false)
    {
        RequireResults(results);
        if (string.IsNullOrEmpty(category))
        {
            throw new ArgumentException("No property category name provided.", nameof(category));
        }

        if (string.IsNullOrEmpty(property))
        {
            throw new ArgumentException("No property name provided.", nameof(property));
        }

        if (string.IsNullOrEmpty(value1) && string.IsNullOrEmpty(value2))
        {
            throw new ArgumentException(
                "Both value texts are empty — the filter would keep everything. Provide value1 (and optionally value2).",
                nameof(value1));
        }

        var matched = new List<ClashResult>();
        foreach (var result in results)
        {
            if (result == null)
            {
                continue;
            }

            var text1 = PropertyText(result.Item1, category, property, searchAncestors);
            var text2 = PropertyText(result.Item2, category, property, searchAncestors);
            if (TextPairFilter.PairMatches(text1, text2, value1, value2, mode, caseSensitive))
            {
                matched.Add(result);
            }
        }

        return matched;
    }

    /// <summary>Keeps (or drops) clashes by their items' membership in a selection/search set.</summary>
    /// <param name="results">The clash results (e.g. from ClashTest.Results).</param>
    /// <param name="set">The set to test against: a selection/search set, its name, or a list of model items.</param>
    /// <param name="which">Which items must be in the set: either, both, item1 or item2.</param>
    /// <param name="invert">True keeps the results that do NOT satisfy the test — the ignore-list mode.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The matching results, input order preserved.</returns>
    [NodeName("Clash.FilterBySet")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription(
        "Keeps clashes whose items belong to a selection/search set (either one, both, or a specific " +
        "side) — sets are the coordination scope language: \"only my package\", \"only against the " +
        "existing building\". invert turns it into the classic ignore-list: drop every clash touching " +
        "the \"Accepted penetrations\" set. An item counts as in the set when itself OR any ancestor " +
        "is a member, matching how Navisworks selections include descendants.")]
    [NodeSearchTags("clash", "filter", "set", "selection", "search", "scope", "ignore", "exclude", "membership")]
    [return: NodeName("results")]
    public static List<ClashResult> FilterBySet(
        IEnumerable<ClashResult> results,
        object set,
        [NodeChoices("either", "both", "item1", "item2")]
        string which = "either",
        bool invert = false,
        Document? document = null)
    {
        RequireResults(results);
        var doc = NavisworksContext.ResolveDocument(document);
        var resolved = new List<ModelItem>();
        ResolveSetItems(set, doc, resolved);
        var members = new HashSet<ModelItem>(resolved);

        var normalizedWhich = (which ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedWhich != "either" && normalizedWhich != "both" &&
            normalizedWhich != "item1" && normalizedWhich != "item2")
        {
            throw new ArgumentException(
                "Unknown which '" + which + "'. Use either, both, item1 or item2.", nameof(which));
        }

        var matched = new List<ClashResult>();
        foreach (var result in results)
        {
            if (result == null)
            {
                continue;
            }

            bool in1 = IsInSet(result.Item1, members);
            bool in2 = IsInSet(result.Item2, members);
            bool hit;
            switch (normalizedWhich)
            {
                case "both": hit = in1 && in2; break;
                case "item1": hit = in1; break;
                case "item2": hit = in2; break;
                default: hit = in1 || in2; break;
            }

            if (invert ? !hit : hit)
            {
                matched.Add(result);
            }
        }

        return matched;
    }

    /// <summary>Keeps clashes by penetration depth, in a unit you choose.</summary>
    /// <param name="results">The clash results (e.g. from ClashTest.Results).</param>
    /// <param name="minDepth">Smallest penetration depth to keep. Positive = overlap; clearance-test gaps read as negative depths.</param>
    /// <param name="maxDepth">Largest penetration depth to keep (default: unlimited).</param>
    /// <param name="units">Unit of the depth numbers: "document" uses the file's internal unit (often feet!), or name the unit your numbers are in.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The matching results, input order preserved.</returns>
    [NodeName("Clash.FilterByDepth")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription(
        "Keeps clashes whose penetration depth falls in a range, in the unit you name — minDepth 0.025 " +
        "with units Meters drops every grazing clash shallower than 25 mm, the #1 noise reducer. Depth " +
        "is positive for overlaps (hard clashes); clearance-test results carry their gap as a NEGATIVE " +
        "depth, so a range like -1..0 keeps clearance violations instead.")]
    [NodeSearchTags("clash", "filter", "depth", "penetration", "distance", "grazing", "significant", "units", "tolerance")]
    [return: NodeName("results")]
    public static List<ClashResult> FilterByDepth(
        IEnumerable<ClashResult> results,
        double minDepth = 0.0,
        double maxDepth = double.PositiveInfinity,
        [NodeChoices("document", "Meters", "Millimeters", "Centimeters", "Feet", "Inches")]
        string units = "document",
        Document? document = null)
    {
        RequireResults(results);
        var doc = NavisworksContext.ResolveDocument(document);
        var scale = ResolveUnitsScale(doc, units);
        var min = minDepth * scale;
        var max = double.IsPositiveInfinity(maxDepth) ? double.PositiveInfinity : maxDepth * scale;

        var matched = new List<ClashResult>();
        foreach (var result in results)
        {
            if (result == null)
            {
                continue;
            }

            // Navisworks reports a hard clash's penetration as a NEGATIVE
            // distance; flip it so "depth" reads naturally (deeper = bigger).
            var depth = -result.Distance;
            if (depth >= min && depth <= max)
            {
                matched.Add(result);
            }
        }

        return matched;
    }

    /// <summary>Removes duplicate clashes: one representative per unique item pair.</summary>
    /// <param name="results">The clash results — from one test, or several (mirrored A-vs-B / B-vs-A test pairs dedupe too).</param>
    /// <returns>The first result of each unique unordered item pair, and the duplicates that were dropped.</returns>
    [NodeName("Clash.Deduplicate")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription(
        "Keeps ONE clash per unique item pair — the same two elements clashing at five points, or " +
        "mirrored across an A-vs-B and B-vs-A test matrix, count once. Pairs are unordered and matched " +
        "by stable item identity (InstanceGuid, else tree path); the first result in input order is the " +
        "representative, the rest come out on duplicates. 400 raw results, 60 real issues.")]
    [NodeSearchTags("clash", "deduplicate", "duplicates", "unique", "pair", "mirror", "matrix", "noise", "merge")]
    [MultiReturn("results", "duplicates")]
    public static Dictionary<string, object?> Deduplicate(IEnumerable<ClashResult> results)
    {
        RequireResults(results);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<ClashResult>();
        var duplicates = new List<ClashResult>();
        foreach (var result in results)
        {
            if (result == null)
            {
                continue;
            }

            var id1 = ItemIdentity(result.Item1);
            var id2 = ItemIdentity(result.Item2);
            if (id1.Length == 0 && id2.Length == 0)
            {
                kept.Add(result); // unidentifiable items — never merge blindly
                continue;
            }

            var key = string.CompareOrdinal(id1, id2) <= 0 ? id1 + "|" + id2 : id2 + "|" + id1;
            if (seen.Add(key))
            {
                kept.Add(result);
            }
            else
            {
                duplicates.Add(result);
            }
        }

        return new Dictionary<string, object?>
        {
            ["results"] = kept,
            ["duplicates"] = duplicates,
        };
    }

    // ------------------------------------------------------------ privates

    private static void RequireResults(IEnumerable<ClashResult>? results)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results), "No clash results provided.");
        }
    }

    /// <summary>
    /// The property's value as text, read from the item and (optionally) its
    /// ancestors, nearest first. Null when nothing in the chain has it.
    /// </summary>
    private static string? PropertyText(ModelItem? item, string category, string property, bool searchAncestors)
    {
        for (var current = item; current != null; current = searchAncestors ? current.Parent : null)
        {
            var found = current.PropertyCategories.FindPropertyByDisplayName(category, property)
                ?? current.PropertyCategories.FindPropertyByName(category, property);
            if (found != null)
            {
                var value = NavisValues.ToClrObject(found.Value);
                return value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            if (!searchAncestors)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>An item is in the set when itself or any ancestor is a member.</summary>
    private static bool IsInSet(ModelItem? item, HashSet<ModelItem> members)
    {
        if (members.Count == 0)
        {
            return false;
        }

        for (var current = item; current != null; current = current.Parent)
        {
            if (members.Contains(current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Flattens the set input into model items: a selection/search set, a set
    /// name, a single item, or any nesting of those in lists (same contract as
    /// Viewpoint.VisibleItems' items input).
    /// </summary>
    private static void ResolveSetItems(object? value, Document doc, List<ModelItem> into)
    {
        switch (value)
        {
            case null:
                throw new ArgumentNullException(nameof(value), "No set provided. Wire a selection/search set, its name, or a list of model items.");
            case ModelItem item:
                into.Add(item);
                return;
            case SelectionSet set:
                into.AddRange(NavisValues.ToItemList(set.GetSelectedItems(doc)));
                return;
            case string name when name.Trim().Length > 0:
                var named = NavisValues.FindSavedItemByName<SelectionSet>(doc.SelectionSets.RootItem.Children, name.Trim());
                if (named == null)
                {
                    throw new InvalidOperationException("No selection set named '" + name.Trim() + "' exists in the document.");
                }

                into.AddRange(NavisValues.ToItemList(named.GetSelectedItems(doc)));
                return;
            case IEnumerable sequence when !(value is string):
                foreach (var element in sequence)
                {
                    if (element != null)
                    {
                        ResolveSetItems(element, doc, into);
                    }
                }

                return;
            default:
                throw new ArgumentException(
                    "Cannot read set members from a " + value.GetType().Name +
                    " — wire a selection/search set, its name, or model items.");
        }
    }

    /// <summary>Stable identity for pair matching: InstanceGuid, else tree path (same scheme as clash snapshots).</summary>
    private static string ItemIdentity(ModelItem? item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        var guid = item.InstanceGuid;
        return guid != Guid.Empty
            ? "guid:" + guid.ToString("N")
            : "path:" + NavisValues.ItemPath(item);
    }

    /// <summary>Scale from a named unit onto document units ("document" = 1).</summary>
    private static double ResolveUnitsScale(Document doc, string? units)
    {
        var trimmed = (units ?? string.Empty).Trim();
        if (trimmed.Length == 0 || string.Equals(trimmed, "document", StringComparison.OrdinalIgnoreCase))
        {
            return 1.0;
        }

        if (!Enum.TryParse<Units>(trimmed, true, out var parsed))
        {
            throw new ArgumentException(
                "Unknown units '" + units + "'. Use \"document\" or a Navisworks unit name " +
                "(e.g. \"Meters\", \"Millimeters\", \"Feet\").", nameof(units));
        }

        return UnitConversion.ScaleFactor(parsed, doc.Units);
    }
}
