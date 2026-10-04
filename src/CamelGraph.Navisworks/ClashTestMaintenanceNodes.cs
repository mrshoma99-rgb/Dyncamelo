using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes.Coordination;

namespace CamelGraph.Navisworks;

/// <summary>
/// Clash test maintenance: edit the settings of an existing test, delete it, duplicate it and
/// clear its results — the weekly "adjust the matrix" chores that ClashTest.Create / Rename /
/// Run did not cover. Every edit goes through <c>DocumentClash.TestsData</c> on the STORED test
/// (a stored test is read-only in place) and hands back the re-located stored instance, because
/// a test wrapper can go stale after an edit.
/// </summary>
[NodeCategory("Navisworks.Clash.Tests")]
public static class ClashTestMaintenanceNodes
{
    /// <summary>Changes the settings of an existing clash test.</summary>
    /// <param name="test">The stored clash test (from Clash.Tests, ClashTest.ByName or ClashTest.Create).</param>
    /// <param name="newName">A new display name ("" keeps the current name).</param>
    /// <param name="testType">Hard, HardConservative, Clearance, Duplicate or Custom ("unchanged" keeps the current type).</param>
    /// <param name="tolerance">The new tolerance, in the unit chosen under "units" (document units by default) — the clearance distance for clearance tests (any negative number, the default -1, keeps the current tolerance).</param>
    /// <param name="mergeComposites">"yes" or "no" to switch the merge-composites option, "unchanged" to keep it.</param>
    /// <param name="itemsA">Items to use as the test's selection A (leave unwired to keep the current selection).</param>
    /// <param name="itemsB">Items to use as the test's selection B (leave unwired to keep the current selection).</param>
    /// <param name="units">Unit of the tolerance: "document" uses the file's internal unit (often feet!), or name the unit your number is in.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The edited stored test.</returns>
    [NodeName("ClashTest.Edit")]
    [NodeAliases("CamelGraph.Navisworks.ClashTestMaintenanceNodes.Edit@Autodesk.Navisworks.Api.Clash.ClashTest,string,string,double,string,System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,Autodesk.Navisworks.Api.Document")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeDescription(
        "Edits an existing clash test in place — name, test type, tolerance, merge-composites and the two selections; " +
        "every input left at its default (\"\", \"unchanged\", -1, unwired) keeps the current value. The tolerance is in document " +
        "units unless units names another unit. The test's existing results " +
        "are kept but are stale after a change: re-run it with ClashTest.Run.")]
    [NodeSearchTags("clash", "test", "edit", "modify", "tolerance", "type", "clearance", "hard", "selection", "update")]
    [return: NodeName("test")]
    public static ClashTest Edit(
        ClashTest test,
        string newName = "",
        [NodeChoices("unchanged", "Hard", "HardConservative", "Clearance", "Duplicate", "Custom")]
        string testType = "unchanged",
        [NodeRange(-1, 1000000, SoftMin = -1, SoftMax = 1)] double tolerance = -1,
        [NodeChoices("unchanged", "yes", "no")]
        string mergeComposites = "unchanged",
        [MultiInput] IEnumerable<ModelItem>? itemsA = null,
        [MultiInput] IEnumerable<ModelItem>? itemsB = null,
        [NodePanel("Advanced")][NodeChoicesFromEnum(typeof(Units), "document")] string units = "document",
        Document? document = null)
    {
        RequireTestInput(test, "ClashTest.Edit");

        // A negative tolerance means "keep the current one": only a real number is converted to document units.
        if (tolerance >= 0)
        {
            tolerance *= NavisValues.ResolveUnitsScale(NavisworksContext.ResolveDocument(document), units);
        }

        var listA = itemsA == null ? null : NavisValues.ToItemList(itemsA);
        var listB = itemsB == null ? null : NavisValues.ToItemList(itemsB);
        if (listA != null && listA.Count == 0)
        {
            throw new ArgumentException(
                "ClashTest.Edit got an empty list for 'itemsA'. Leave 'itemsA' unwired to keep the test's selection A, " +
                "or wire at least one model item.", nameof(itemsA));
        }

        if (listB != null && listB.Count == 0)
        {
            throw new ArgumentException(
                "ClashTest.Edit got an empty list for 'itemsB'. Leave 'itemsB' unwired to keep the test's selection B, " +
                "or wire at least one model item.", nameof(itemsB));
        }

        var plan = ClashEditRules.Plan(newName, testType, tolerance, mergeComposites, listA != null, listB != null);

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);
        var stored = ResolveStored(clash, test);
        if (!plan.HasChanges)
        {
            return stored; // every input left at "unchanged": nothing to do
        }

        var guid = stored.Guid;
        var name = stored.DisplayName ?? string.Empty;

        if (plan.NewName != null &&
            !string.Equals(plan.NewName, name, StringComparison.Ordinal) &&
            NavisValues.FindSavedItemByName<ClashTest>(clash.TestsData.Tests, plan.NewName) != null)
        {
            throw new InvalidOperationException(
                "ClashTest.Edit cannot rename '" + name + "' to '" + plan.NewName + "': another clash test already has that name. " +
                "Pick a different name, or delete the other test first (ClashTest.Delete).");
        }

        using (var transaction = doc.BeginTransaction("Edit clash test (" + ClashEditRules.Describe(plan) + ")"))
        {
            if (plan.HasSettingsEdit)
            {
                // Stored tests are read-only: edit a detached copy, then apply its settings to the stored test.
                var copy = (ClashTest)stored.CreateCopy();
                if (plan.TestType != null)
                {
                    copy.TestType = ClashHelpers.ParseTestType(plan.TestType);
                }

                if (plan.Tolerance.HasValue)
                {
                    copy.Tolerance = plan.Tolerance.Value;
                }

                if (plan.MergeComposites.HasValue)
                {
                    copy.MergeComposites = plan.MergeComposites.Value;
                }

                if (listA != null)
                {
                    copy.SelectionA.Selection.CopyFrom(NavisValues.ToItemCollection(listA));
                }

                if (listB != null)
                {
                    copy.SelectionB.Selection.CopyFrom(NavisValues.ToItemCollection(listB));
                }

                clash.TestsData.TestsEditTestFromCopy(stored, copy);
                stored = Relocate(clash, guid, name);
            }

            if (plan.NewName != null && !string.Equals(plan.NewName, name, StringComparison.Ordinal))
            {
                clash.TestsData.TestsEditDisplayName(stored, plan.NewName);
                name = plan.NewName;
            }

            transaction.Commit();
        }

        return Relocate(clash, guid, name);
    }

    /// <summary>Deletes a clash test from the document.</summary>
    /// <param name="test">The clash test to delete.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when the test was removed; false when it was not in the document.</returns>
    [NodeName("ClashTest.Delete")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeDescription(
        "Deletes a clash test and all of its results from the document. Returns false (and changes nothing) when the test is " +
        "not there, so a clean-up step can run twice. Undoable in Navisworks.")]
    [NodeSearchTags("clash", "test", "delete", "remove", "clean", "matrix")]
    [return: NodeName("deleted")]
    public static bool Delete(ClashTest test, Document? document = null)
    {
        RequireTestInput(test, "ClashTest.Delete");

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);

        Guid guid;
        string name;
        try
        {
            guid = test.Guid;
            name = test.DisplayName ?? string.Empty;
        }
        catch (Exception ex) when (ClashHelpers.IsDisposed(ex))
        {
            throw ClashHelpers.StaleInputError("clash test", ex);
        }

        if (!TryLocate(clash.TestsData.Tests, null, guid, name, out var parent, out var stored) || stored == null)
        {
            return false;
        }

        using (var transaction = doc.BeginTransaction("Delete clash test"))
        {
            if (parent == null)
            {
                clash.TestsData.TestsRemove(stored);
            }
            else
            {
                clash.TestsData.TestsRemove(parent, stored);
            }

            transaction.Commit();
        }

        // TestsRemove reports nothing: confirm the test really is gone.
        return !TryLocate(clash.TestsData.Tests, null, guid, name, out _, out _);
    }

    /// <summary>Duplicates a clash test (settings and selections, without results).</summary>
    /// <param name="test">The clash test to copy.</param>
    /// <param name="newName">Name for the copy ("" uses "&lt;name&gt; copy").</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The new stored test, placed in the same folder as the original.</returns>
    [NodeName("ClashTest.Duplicate")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeDescription(
        "Duplicates a clash test — type, tolerance, rules and both selections — as a new test named \"<name> copy\" (or newName) " +
        "in the same folder. The copy starts un-run: its results are cleared, so run it with ClashTest.Run. " +
        "Errors if a test with that name already exists, so re-runs do not pile up copies — over a list of tests, " +
        "leave newName empty or give each test its own name.")]
    [NodeSearchTags("clash", "test", "duplicate", "copy", "clone", "variant", "matrix")]
    [return: NodeName("test")]
    public static ClashTest Duplicate(ClashTest test, string newName = "", Document? document = null)
    {
        RequireTestInput(test, "ClashTest.Duplicate");

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);
        var stored = ResolveStored(clash, test);

        var name = ClashEditRules.CopyName(stored.DisplayName, newName);
        if (NavisValues.FindSavedItemByName<ClashTest>(clash.TestsData.Tests, name) != null)
        {
            throw new InvalidOperationException(
                "A clash test named '" + name + "' already exists. Give ClashTest.Duplicate a different 'newName', " +
                "or delete the existing one first (ClashTest.Delete).");
        }

        TryLocate(clash.TestsData.Tests, null, stored.Guid, stored.DisplayName ?? string.Empty, out var parent, out _);

        ClashTest duplicate;
        using (var transaction = doc.BeginTransaction("Duplicate clash test"))
        {
            // The copy facility of every saved item: a deep copy, detached and editable.
            var copy = (ClashTest)stored.CreateCopy();
            copy.DisplayName = name;
            if (parent == null)
            {
                clash.TestsData.TestsAddCopy(copy);
            }
            else
            {
                clash.TestsData.TestsAddCopy(parent, copy);
            }

            // TestsAddCopy stores a copy — find the stored instance (the name is unique, checked above).
            duplicate = NavisValues.FindSavedItemByName<ClashTest>(clash.TestsData.Tests, name)
                ?? throw new InvalidOperationException(
                    "The duplicate '" + name + "' could not be found after it was added to the document.");

            // The copy carries the original's results; a duplicate is a fresh, un-run test.
            clash.TestsData.TestsClearResults(duplicate);
            transaction.Commit();
        }

        return NavisValues.FindSavedItemByName<ClashTest>(clash.TestsData.Tests, name) ?? duplicate;
    }

    /// <summary>Clears the results of a clash test, leaving the test itself.</summary>
    /// <param name="test">The clash test to clear.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored test, now without results.</returns>
    [NodeName("ClashTest.ClearResults")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeDescription(
        "Removes every result (and result group) of a clash test and leaves the test and its settings in place — " +
        "the clean slate before a re-run. Statuses, assignments and comments on those results are lost.")]
    [NodeSearchTags("clash", "test", "clear", "results", "reset", "empty", "clean", "rerun")]
    [return: NodeName("test")]
    public static ClashTest ClearResults(ClashTest test, Document? document = null)
    {
        RequireTestInput(test, "ClashTest.ClearResults");

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);
        var stored = ResolveStored(clash, test);
        var guid = stored.Guid;
        var name = stored.DisplayName ?? string.Empty;

        using (var transaction = doc.BeginTransaction("Clear clash results"))
        {
            clash.TestsData.TestsClearResults(stored);
            transaction.Commit();
        }

        return Relocate(clash, guid, name);
    }

    // ------------------------------------------------------------ privates

    private static void RequireTestInput(ClashTest? test, string nodeName)
    {
        if (test == null)
        {
            throw new ArgumentNullException(
                nameof(test),
                nodeName + " requires a clash test. Wire one from Clash.Tests, ClashTest.ByName or ClashTest.Create into the 'test' input.");
        }
    }

    /// <summary>Resolves the wired test to the STORED instance; a wrapper Navisworks has disposed gets the standard stale-input error.</summary>
    private static ClashTest ResolveStored(DocumentClash clash, ClashTest test)
    {
        try
        {
            return ClashHelpers.ResolveStoredTest(clash, test);
        }
        catch (Exception ex) when (ClashHelpers.IsDisposed(ex))
        {
            throw ClashHelpers.StaleInputError("clash test", ex);
        }
    }

    /// <summary>Re-fetches the stored test after an edit (by Guid, then name): edits can invalidate wrappers handed out earlier.</summary>
    private static ClashTest Relocate(DocumentClash clash, Guid guid, string name)
    {
        return ClashHelpers.FindStoredTest(clash, guid, name)
            ?? throw new InvalidOperationException(
                "The clash test '" + name + "' could not be found after the edit — it may have been deleted or renamed while the graph ran.");
    }

    /// <summary>
    /// Finds a test in the tests tree by Guid (or name when it has none): the stored instance and
    /// its parent folder (null for a top-level test). Tests nested in folders are found too.
    /// </summary>
    private static bool TryLocate(
        IEnumerable<SavedItem> items,
        GroupItem? owner,
        Guid guid,
        string name,
        out GroupItem? parent,
        out ClashTest? found)
    {
        foreach (var item in items)
        {
            if (item is ClashTest candidate)
            {
                var same = guid != Guid.Empty
                    ? candidate.Guid == guid
                    : string.Equals(candidate.DisplayName, name, StringComparison.Ordinal);
                if (same)
                {
                    parent = owner;
                    found = candidate;
                    return true;
                }
            }
            else if (item is GroupItem group &&
                     TryLocate(group.Children, group, guid, name, out parent, out found))
            {
                return true;
            }
        }

        parent = null;
        found = null;
        return false;
    }
}
