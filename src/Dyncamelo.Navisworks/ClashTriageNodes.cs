using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;
using Dyncamelo.Nodes.Spatial;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Clash triage nodes (user wishlist, v0.27): explicit result grouping with
/// move-or-skip semantics, orientation-aware angle filtering (a pipe through a
/// wall is not a pipe through a floor, even when both cross at 90°), group
/// lookup by name with the group's own status, focus-in-view, and small
/// plumbing pieces (test name accessor, status dropdown).
/// </summary>
[NodeCategory("Navisworks.Clash")]
public static class ClashTriageNodes
{
    // ------------------------------------------------------------- grouping

    /// <summary>Puts the given clash results into a named group of their test.</summary>
    /// <param name="results">The clash results to group (from ClashTest.Results, Clash.FilterBy*, ...). All must belong to the same test.</param>
    /// <param name="groupName">The group's display name — an existing same-named group is extended, otherwise one is created.</param>
    /// <param name="moveExisting">True pulls results that already sit in OTHER groups into this one; false leaves them where they are (they are counted as skipped).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The test, the stored group, and how many results were added, moved and skipped.</returns>
    [NodeName("Clash.GroupResults")]
    [NodeDescription(
        "Puts an explicit list of clash results into a named group in Clash Detective — YOUR grouping " +
        "rule, not a built-in one: filter results any way you like, then group what came out. Results " +
        "already in other groups are left alone (skipped) unless moveExisting is true, which pulls them " +
        "into this group. An existing same-named group is extended; re-runs are clean.")]
    [NodeSearchTags("clash", "group", "results", "move", "triage", "bucket", "organize")]
    [MultiReturn("test", "group", "added", "moved", "skipped")]
    public static Dictionary<string, object?> GroupResults(
        IEnumerable<ClashResult> results,
        string groupName,
        bool moveExisting = false,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(groupName))
        {
            throw new ArgumentException("No group name provided.", nameof(groupName));
        }

        var resultList = MaterializeResults(results);
        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);

        // Canonicalize onto the document's stored instance — parent-walks hand
        // out a fresh wrapper object per call, so identity, never references.
        var stored = ClashHelpers.ResolveStoredTest(clash, OwningStoredTest(resultList[0]));
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var result in resultList)
        {
            var owner = OwningStoredTest(result);
            if (!SameTest(owner, stored))
            {
                throw new ArgumentException(
                    "The results span more than one clash test ('" + stored.DisplayName +
                    "' and '" + owner.DisplayName +
                    "') — group one test's results at a time.", nameof(results));
            }

            wanted.Add(ResultKey(result));
        }

        // Rebuild the tree on a copy and commit in one edit (the established
        // TestsEditTestFromCopy path). Untouched groups are carried over as
        // whole copies so their names, comments and members survive.
        var copy = (ClashTest)stored.CreateCopy();
        var newTop = new List<SavedItem>();
        var targetChildren = new List<ClashResult>();
        int added = 0, moved = 0, skipped = 0;
        int targetPosition = -1;

        foreach (var child in copy.Children)
        {
            switch (child)
            {
                case ClashResult loose:
                    var looseCopy = (ClashResult)loose.CreateCopy();
                    if (wanted.Contains(ResultKey(loose)))
                    {
                        targetChildren.Add(looseCopy);
                        added++;
                    }
                    else
                    {
                        newTop.Add(looseCopy);
                    }

                    break;

                case ClashResultGroup group when string.Equals(group.DisplayName, groupName, StringComparison.Ordinal):
                    // The target group already exists — its members stay, the
                    // new ones are appended, and it keeps its position.
                    targetPosition = newTop.Count;
                    newTop.Add(null!); // placeholder, replaced below
                    foreach (var member in group.Children)
                    {
                        if (member is ClashResult existing)
                        {
                            targetChildren.Add((ClashResult)existing.CreateCopy());
                        }
                    }

                    break;

                case ClashResultGroup group:
                    var keptMembers = new List<ClashResult>();
                    foreach (var member in group.Children)
                    {
                        if (!(member is ClashResult memberResult))
                        {
                            continue;
                        }

                        if (wanted.Contains(ResultKey(memberResult)))
                        {
                            if (moveExisting)
                            {
                                targetChildren.Add((ClashResult)memberResult.CreateCopy());
                                moved++;
                                continue;
                            }

                            skipped++;
                        }

                        keptMembers.Add((ClashResult)memberResult.CreateCopy());
                    }

                    var groupCopy = new ClashResultGroup { DisplayName = group.DisplayName };
                    foreach (var member in keptMembers)
                    {
                        groupCopy.Children.Add(member);
                    }

                    newTop.Add(groupCopy);
                    break;

                default:
                    // Anything unexpected in the tree is carried over untouched.
                    newTop.Add(child.CreateCopy());
                    break;
            }
        }

        var target = new ClashResultGroup { DisplayName = groupName };
        foreach (var member in targetChildren)
        {
            target.Children.Add(member);
        }

        if (targetPosition >= 0)
        {
            newTop[targetPosition] = target;
        }
        else
        {
            newTop.Add(target);
        }

        copy.Children.Clear();
        foreach (var item in newTop)
        {
            copy.Children.Add(item);
        }

        clash.TestsData.TestsEditTestFromCopy(stored, copy);

        return new Dictionary<string, object?>
        {
            ["test"] = stored,
            ["group"] = FindGroup(stored, groupName),
            ["added"] = added,
            ["moved"] = moved,
            ["skipped"] = skipped,
        };
    }

    /// <summary>Finds a clash result group by test and group name.</summary>
    /// <param name="test">The clash test, or its display name.</param>
    /// <param name="groupName">The group's display name inside that test.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The group, the results inside it, the group's own status, and the result count.</returns>
    [NodeName("ClashGroup.ByName")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription(
        "Finds a clash result group by test name + group name and opens it up: the results inside, the " +
        "group's own status, and the count. The lookup half of group-based triage — feed the results to " +
        "any filter/report node, or the group to ClashResult.SetStatus/Rename/AddComment.")]
    [NodeSearchTags("clash", "group", "name", "fetch", "results", "status", "lookup")]
    [MultiReturn("group", "results", "status", "count")]
    public static Dictionary<string, object?> GroupByName(object test, string groupName, Document? document = null)
    {
        if (string.IsNullOrEmpty(groupName))
        {
            throw new ArgumentException("No group name provided.", nameof(groupName));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);
        var stored = ClashHelpers.ResolveStoredTest(clash, test);

        var group = FindGroup(stored, groupName);
        if (group == null)
        {
            var available = new List<string>();
            foreach (var child in stored.Children)
            {
                if (child is ClashResultGroup g)
                {
                    available.Add("'" + g.DisplayName + "'");
                }
            }

            throw new InvalidOperationException(
                "The test '" + stored.DisplayName + "' has no result group named '" + groupName + "'." +
                (available.Count > 0
                    ? " Its groups: " + string.Join(", ", available) + "."
                    : " It has no groups yet — Clash.GroupResults creates them."));
        }

        var members = GroupMembers(group);
        return new Dictionary<string, object?>
        {
            ["group"] = group,
            ["results"] = members,
            ["status"] = group.Status.ToString(),
            ["count"] = members.Count,
        };
    }

    /// <summary>All result groups of a clash test.</summary>
    /// <param name="test">The clash test, or its display name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The groups with their names, statuses and result counts, index-aligned.</returns>
    [NodeName("ClashTest.Groups")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription("All result groups of a clash test — groups, names, each group's own status and result count, index-aligned. The overview half of group-based triage; ClashGroup.ByName opens a single one.")]
    [NodeSearchTags("clash", "test", "groups", "list", "names", "statuses", "overview")]
    [MultiReturn("groups", "names", "statuses", "counts")]
    public static Dictionary<string, object?> Groups(object test, Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);
        var stored = ClashHelpers.ResolveStoredTest(clash, test);

        var groups = new List<ClashResultGroup>();
        var names = new List<string>();
        var statuses = new List<string>();
        var counts = new List<int>();
        foreach (var child in stored.Children)
        {
            if (child is ClashResultGroup group)
            {
                groups.Add(group);
                names.Add(group.DisplayName ?? string.Empty);
                statuses.Add(group.Status.ToString());
                counts.Add(GroupMembers(group).Count);
            }
        }

        return new Dictionary<string, object?>
        {
            ["groups"] = groups,
            ["names"] = names,
            ["statuses"] = statuses,
            ["counts"] = counts,
        };
    }

    // ---------------------------------------------------------- orientation

    /// <summary>The crossing angle of a clash plus each element's shape and slope.</summary>
    /// <param name="result">The clash result.</param>
    /// <returns>The crossing angle, each element's box shape (slab/wall/riser/run/block, "none" without geometry) and its slope from horizontal (0–90°).</returns>
    [NodeName("ClashResult.Orientation")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription(
        "ClashResult.Angle with world context: the crossing angle PLUS each element's bounding-box shape — " +
        "\"slab\" (thin horizontal), \"wall\" (thin upright), \"riser\" (long vertical), \"run\" (long " +
        "horizontal) or \"block\" — and its slope from horizontal (0–90°). This is what tells a pipe " +
        "through a wall (run + wall) from a pipe through a floor (run/riser + slab) when the crossing " +
        "angle alone is 90° in both cases. Box-based: a strongly diagonal linear element can read as " +
        "planar; \"none\" means the element has no measurable box.")]
    [NodeSearchTags("clash", "orientation", "angle", "wall", "floor", "slab", "pipe", "shape", "slope", "vertical", "horizontal")]
    [MultiReturn("degrees", "shape1", "shape2", "slope1", "slope2")]
    public static Dictionary<string, object?> Orientation(ClashResult result)
    {
        var clashResult = ClashHelpers.RequireResult(result);
        DescribeItem(clashResult.Item1, out var shape1, out var slope1);
        DescribeItem(clashResult.Item2, out var shape2, out var slope2);

        return new Dictionary<string, object?>
        {
            ["degrees"] = ClashNodes.Angle(clashResult),
            ["shape1"] = shape1,
            ["shape2"] = shape2,
            ["slope1"] = slope1,
            ["slope2"] = slope2,
        };
    }

    /// <summary>Keeps only the clashes between elements of the given shapes.</summary>
    /// <param name="results">The clash results (e.g. from ClashTest.Results).</param>
    /// <param name="shape1">Required shape of one element: any, slab, wall, riser, run or block.</param>
    /// <param name="shape2">Required shape of the other element (the pair is matched in either order).</param>
    /// <returns>The matching results, input order preserved (results without geometry are dropped).</returns>
    [NodeName("Clash.FilterByOrientation")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription(
        "Keeps only the clashes between elements of the given box shapes, matched in either order — " +
        "shape1=run + shape2=wall keeps pipes through walls, run/riser + slab keeps pipes through " +
        "floors; Clash.FilterByAngle alone cannot tell those apart (both cross at ~90°). Shapes are " +
        "ClashResult.Orientation's: slab, wall, riser, run, block, or any.")]
    [NodeSearchTags("clash", "filter", "orientation", "wall", "floor", "slab", "pipe", "shape", "crossing")]
    [return: NodeName("results")]
    public static List<ClashResult> FilterByOrientation(
        IEnumerable<ClashResult> results,
        [NodeChoices("any", "slab", "wall", "riser", "run", "block")]
        string shape1 = "any",
        [NodeChoices("any", "slab", "wall", "riser", "run", "block")]
        string shape2 = "any")
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results), "No clash results provided.");
        }

        var matched = new List<ClashResult>();
        foreach (var result in results)
        {
            if (result == null)
            {
                continue;
            }

            DescribeItem(result.Item1, out var a, out _);
            DescribeItem(result.Item2, out var b, out _);
            if (a == "none" || b == "none")
            {
                continue;
            }

            if (BoxOrientation.PairMatches(a, b, shape1, shape2))
            {
                matched.Add(result);
            }
        }

        return matched;
    }

    // -------------------------------------------------------------- utility

    /// <summary>The display name of a clash test.</summary>
    /// <param name="test">The clash test.</param>
    /// <returns>The test's display name.</returns>
    [NodeName("ClashTest.Name")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription("The display name of a clash test — the one-output companion of ClashTest.Info for wiring names into reports, group lookups and file paths.")]
    [NodeSearchTags("clash", "test", "name", "display", "title")]
    [return: NodeName("name")]
    public static string Name(ClashTest test)
    {
        return ClashHelpers.RequireTest(test).DisplayName ?? string.Empty;
    }

    /// <summary>A clash status value, picked from a dropdown.</summary>
    /// <param name="status">New, Active, Reviewed, Approved or Resolved.</param>
    /// <returns>The chosen status text.</returns>
    [NodeName("Clash.Status")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Create)]
    [NodeDescription("A clash status as a dropdown (New/Active/Reviewed/Approved/Resolved) — wire it into ClashResult.SetStatus, Clash.FilterByStatus or ClashTest.ResultsByStatus instead of typing the text.")]
    [NodeSearchTags("clash", "status", "dropdown", "choice", "new", "active", "reviewed", "approved", "resolved")]
    [return: NodeName("status")]
    public static string Status(
        [NodeChoices("New", "Active", "Reviewed", "Approved", "Resolved")]
        string status = "New")
    {
        return ClashHelpers.ParseResultStatus(status).ToString();
    }

    /// <summary>Several clash statuses, picked with toggles.</summary>
    /// <param name="newStatus">Include New results.</param>
    /// <param name="active">Include Active results.</param>
    /// <param name="reviewed">Include Reviewed results.</param>
    /// <param name="approved">Include Approved results.</param>
    /// <param name="resolved">Include Resolved results.</param>
    /// <returns>The picked statuses as comma-separated text (e.g. "New,Active") — the form every status input accepts.</returns>
    [NodeName("Clash.Statuses")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Create)]
    [NodeDescription(
        "Pick SEVERAL clash statuses with toggles — the multi-select for Clash.FilterByStatus and " +
        "ClashTest.ResultsByStatus: switch on New and Active to work everything not yet reviewed. " +
        "Outputs comma-separated text (\"New,Active\"), which every status input accepts.")]
    [NodeSearchTags("clash", "status", "statuses", "multiple", "select", "toggle", "new", "active", "reviewed", "approved", "resolved")]
    [return: NodeName("statuses")]
    public static string Statuses(
        bool newStatus = true,
        bool active = false,
        bool reviewed = false,
        bool approved = false,
        bool resolved = false)
    {
        var picked = new List<string>();
        if (newStatus)
        {
            picked.Add("New");
        }

        if (active)
        {
            picked.Add("Active");
        }

        if (reviewed)
        {
            picked.Add("Reviewed");
        }

        if (approved)
        {
            picked.Add("Approved");
        }

        if (resolved)
        {
            picked.Add("Resolved");
        }

        if (picked.Count == 0)
        {
            throw new InvalidOperationException(
                "No status toggled on — switch on at least one of New/Active/Reviewed/Approved/Resolved.");
        }

        return string.Join(",", picked);
    }

    /// <summary>Focuses the view on clash results: isolate, zoom, optionally select.</summary>
    /// <param name="results">The clash result(s) to focus — one, or a list to frame together.</param>
    /// <param name="isolate">True hides everything except the clashing elements (Appearance.ShowAll undoes it).</param>
    /// <param name="zoom">True zooms the camera to the clashing elements.</param>
    /// <param name="select">True also selects the elements (fills the Selection window).</param>
    /// <param name="paddingFactor">Zoom padding: 1 = tight framing, larger = more context.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The clashing model items (pass-through for chaining, e.g. into Flow.Then or a viewpoint save).</returns>
    [NodeName("ClashResult.Focus")]
    [NodeDescription(
        "Focuses the view on clash results the way double-clicking one in Clash Detective does: hides " +
        "everything else (isolate), zooms the camera to the clashing pair, and optionally selects the " +
        "elements. Wire one result, or a list to frame a whole group together. Follow with " +
        "Viewpoint.SaveCurrent to keep the view; Appearance.ShowAll brings the model back.")]
    [NodeSearchTags("clash", "focus", "isolate", "zoom", "view", "show", "frame", "select")]
    [return: NodeName("items")]
    public static List<ModelItem> Focus(
        IEnumerable<ClashResult> results,
        bool isolate = true,
        bool zoom = true,
        bool select = false,
        double paddingFactor = 1.5,
        Document? document = null)
    {
        var resultList = MaterializeResults(results);
        var items = new List<ModelItem>();
        var seen = new HashSet<ModelItem>();
        foreach (var result in resultList)
        {
            AddItem(result.Item1, items, seen);
            AddItem(result.Item2, items, seen);
        }

        if (items.Count == 0)
        {
            throw new InvalidOperationException(
                "The clash result(s) reference no model items — were the clashing files unloaded? " +
                "Re-run the test (ClashTest.Run) against the current model.");
        }

        if (isolate)
        {
            AppearanceNodes.Isolate(items, document);
        }

        if (select)
        {
            SelectionNodes.SetCurrent(items, document);
        }

        if (zoom)
        {
            CameraNodes.ZoomToItems(items, paddingFactor, document);
        }

        return items;
    }

    // ------------------------------------------------------------ privates

    private static List<ClashResult> MaterializeResults(IEnumerable<ClashResult>? results)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results), "No clash results provided.");
        }

        var list = new List<ClashResult>();
        foreach (var result in results)
        {
            if (result != null)
            {
                list.Add(result);
            }
        }

        if (list.Count == 0)
        {
            throw new ArgumentException("The clash results list is empty.", nameof(results));
        }

        return list;
    }

    /// <summary>The stored test a result belongs to, found by walking its parents.</summary>
    private static ClashTest OwningStoredTest(ClashResult result)
    {
        for (SavedItem? current = result; current != null; current = current.Parent)
        {
            if (current is ClashTest test)
            {
                return test;
            }
        }

        throw new ArgumentException(
            "The clash result '" + result.DisplayName + "' is not attached to a stored test — " +
            "wire results straight from ClashTest.Results (or a filter of them), not detached copies.");
    }

    /// <summary>
    /// Whether two test wrappers denote the SAME stored test. The Navisworks
    /// API returns a fresh wrapper object per Parent access, so reference
    /// equality is meaningless across walks — compare by Guid (name as the
    /// last resort when a format assigns no Guids).
    /// </summary>
    private static bool SameTest(ClashTest a, ClashTest b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Guid != Guid.Empty || b.Guid != Guid.Empty)
        {
            return a.Guid == b.Guid;
        }

        return string.Equals(a.DisplayName, b.DisplayName, StringComparison.Ordinal);
    }

    /// <summary>A stable identity for matching a result between a stored test and its copy.</summary>
    private static string ResultKey(ClashResult result)
    {
        return result.Guid != Guid.Empty
            ? "g:" + result.Guid.ToString("N")
            : "n:" + result.DisplayName;
    }

    private static ClashResultGroup? FindGroup(ClashTest test, string groupName)
    {
        foreach (var child in test.Children)
        {
            if (child is ClashResultGroup group &&
                string.Equals(group.DisplayName, groupName, StringComparison.Ordinal))
            {
                return group;
            }
        }

        return null;
    }

    private static List<ClashResult> GroupMembers(ClashResultGroup group)
    {
        var members = new List<ClashResult>();
        foreach (var child in group.Children)
        {
            if (child is ClashResult result)
            {
                members.Add(result);
            }
        }

        return members;
    }

    private static void DescribeItem(ModelItem? item, out string shape, out double slope)
    {
        var box = item?.BoundingBox(false);
        if (box == null)
        {
            shape = "none";
            slope = double.NaN;
            return;
        }

        var dx = box.Max.X - box.Min.X;
        var dy = box.Max.Y - box.Min.Y;
        var dz = box.Max.Z - box.Min.Z;
        shape = BoxOrientation.Classify(dx, dy, dz);
        slope = BoxOrientation.SlopeDegrees(dx, dy, dz);
    }

    private static void AddItem(ModelItem? item, List<ModelItem> items, HashSet<ModelItem> seen)
    {
        if (item != null && seen.Add(item))
        {
            items.Add(item);
        }
    }
}
