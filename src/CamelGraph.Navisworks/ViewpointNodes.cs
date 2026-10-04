using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks;

/// <summary>Nodes for saved viewpoints.</summary>
[NodeCategory("Navisworks.Viewpoints")]
public static class ViewpointNodes
{
    /// <summary>All saved viewpoints in a document.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Every saved viewpoint, including those nested in folders (animations are skipped).</returns>
    [NodeName("Viewpoints.All")]
    [LiveState]
    [NodeDescription("All saved viewpoints in a document, including those inside folders. Read again on every run, so a viewpoint added or removed since the last run is seen.")]
    [NodeSearchTags("viewpoints", "views", "saved", "camera", "all")]
    [return: NodeName("viewpoints")]
    public static List<SavedViewpoint> All(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        return NavisValues.FlattenSavedItems<SavedViewpoint>(doc.SavedViewpoints.RootItem.Children);
    }

    /// <summary>Finds a saved viewpoint by display name.</summary>
    /// <param name="name">The viewpoint's display name, or its folder path and name such as "Reviews/Week 12/Level 1".</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored saved viewpoint.</returns>
    [NodeName("SavedViewpoint.ByName")]
    [LiveState]
    [NodeDescription(
        "Finds a saved viewpoint by its display name (searches folders too). When several viewpoints share the name the first one in the " +
        "Saved Viewpoints window is used and the node shows a warning; give the folder path as the name (\"Reviews/Week 12/Level 1\") to " +
        "choose one exactly. Read again on every run.")]
    [NodeSearchTags("viewpoint", "view", "byname", "find", "camera", "path", "folder")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint ByName(string name, Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No viewpoint name provided.", nameof(name));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoint = SavedItemTreeHelpers.FindByNameOrPath<SavedViewpoint>(doc.SavedViewpoints.RootItem, name, "saved viewpoint");
        return viewpoint ?? throw new InvalidOperationException(
            "No saved viewpoint named '" + name + "' exists in the document.");
    }

    /// <summary>Applies a saved viewpoint to the current view.</summary>
    /// <param name="viewpoint">The saved viewpoint to apply.</param>
    /// <param name="after">Anything at all, only to run this node after the node it comes from: the view changes when this node runs, and the engine does not order nodes that share no wire.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The applied viewpoint (pass-through).</returns>
    [NodeName("SavedViewpoint.Apply")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.ViewpointNodes.Apply@Autodesk.Navisworks.Api.SavedViewpoint,Autodesk.Navisworks.Api.Document")]
    [NodeDescription(
        "Makes a saved viewpoint the current view (camera, plus any saved overrides). Over a list of viewpoints it applies each in turn, " +
        "leaving the last one on screen; to take a picture of each use Export.ViewpointImage with the viewpoints wired to its own " +
        "'viewpoint' input. Wire the output of the node that must run before this one into 'after'.")]
    [NodeSearchTags("viewpoint", "view", "apply", "goto", "camera")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint Apply(SavedViewpoint viewpoint, object? after = null, Document? document = null)
    {
        if (viewpoint == null)
        {
            throw new ArgumentNullException(nameof(viewpoint), "No saved viewpoint provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        doc.SavedViewpoints.CurrentSavedViewpoint = viewpoint;
        return viewpoint;
    }

    /// <summary>Saves the current view as a saved viewpoint, optionally with its colour, transparency and hidden-item overrides.</summary>
    /// <param name="name">Display name for the viewpoint.</param>
    /// <param name="folder">Where to file it: empty for the top level, a folder name, or a path such as "Reviews/Week 12". Folders that do not exist are created.</param>
    /// <param name="bakeOverrides">True saves the temporary colour, transparency and hidden-item overrides with the camera; false saves the camera only.</param>
    /// <param name="after">Anything at all, only to run this node after the node it comes from: the view is read when this node runs, and the engine does not order nodes that share no wire.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored saved viewpoint.</returns>
    [NodeName("Viewpoint.Save")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription(
        "Saves the current view as a saved viewpoint in the Saved Viewpoints window. folder files it in a folder (a name, or a path such as " +
        "\"Reviews/Week 12\"; folders that do not exist are created); leave it empty for the top level. A viewpoint with the same name in " +
        "that folder is replaced, so running the graph again updates it instead of piling up. Turn bakeOverrides on to save the temporary " +
        "colour, transparency and hidden-item overrides (Appearance.OverrideColorTemporary and friends) with the camera, so recalling the " +
        "viewpoint restores that exact look; off saves the camera only. The view is read when this node runs: set the camera and the " +
        "overrides first and wire the last of those nodes into 'after'.")]
    [NodeSearchTags(
        "viewpoint", "view", "save", "capture", "camera", "overrides", "appearance", "isolate", "color", "freeze", "folder",
        "saved viewpoint", "savecurrent", "savewithoverrides")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint Save(
        string name,
        string? folder = null,
        bool bakeOverrides = false,
        object? after = null,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No viewpoint name provided.", nameof(name));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;

        // CaptureRuntimeOverrides snapshots the camera PLUS the current temporary appearance and hidden state, so the viewpoint
        // keeps its own look; without it only the camera is saved.
        var saved = bakeOverrides
            ? viewpoints.CaptureRuntimeOverrides()
            : new SavedViewpoint(doc.CurrentViewpoint.ToViewpoint());
        saved.DisplayName = name;

        var target = ViewpointStore.ResolveFolder(viewpoints, folder);
        var stored = ViewpointStore.Put(viewpoints, saved, target);
        if (!bakeOverrides)
        {
            return stored;
        }

        // CaptureRuntimeOverrides keeps the appearance/visibility overrides but not the camera (it leaves a default origin/top
        // view); ReplaceFromCurrentView pulls the current camera into the stored viewpoint, preserving the overrides.
        viewpoints.ReplaceFromCurrentView(stored);
        var children = target != null ? target.Children : viewpoints.Value;
        var storedIndex = NavisValues.FindTopLevelIndex<SavedViewpoint>(children, name);
        return storedIndex >= 0 ? (SavedViewpoint)children[storedIndex] : stored;
    }

    /// <summary>Saves the current view as a new saved viewpoint.</summary>
    /// <param name="name">Display name for the new viewpoint.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored saved viewpoint.</returns>
    [NodeName("Viewpoint.SaveCurrent")]
    [NodeDeprecated("Viewpoint.Save")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Saves the current view (camera only) as a saved viewpoint at the top level; a viewpoint with the same name there is replaced.")]
    [NodeSearchTags("viewpoint", "view", "save", "capture", "camera")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint SaveCurrent(string name, Document? document = null)
    {
        return Save(name, null, false, null, document);
    }

    /// <summary>Saves the current view WITH its appearance/visibility overrides baked in.</summary>
    /// <param name="name">Display name for the new viewpoint.</param>
    /// <param name="folderName">Viewpoint folder to file it under (null/empty stores it at the top level).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored saved viewpoint.</returns>
    [NodeName("Viewpoint.SaveWithOverrides")]
    [NodeDeprecated("Viewpoint.Save")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription(
        "Saves the current view AND the current temporary color/transparency/hidden overrides into the viewpoint, so recalling it restores that exact look. " +
        "An existing same-named viewpoint is replaced.")]
    [NodeSearchTags("viewpoint", "view", "save", "overrides", "appearance", "capture", "isolate", "color", "freeze")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint SaveWithOverrides(string name, string? folderName = null, Document? document = null)
    {
        return Save(name, folderName, true, null, document);
    }

    /// <summary>Copies one viewpoint's appearance/visibility overrides onto another's view.</summary>
    /// <param name="fromViewpoint">The viewpoint whose colour/transparency/hidden overrides to copy (or its name).</param>
    /// <param name="toViewpoint">The viewpoint to update — keeps its own camera, gains the source's look (or its name).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The updated target viewpoint.</returns>
    [NodeName("SavedViewpoint.CopyOverrides")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription(
        "Copies the appearance the way one saved view looks — its colour, transparency and hidden-item overrides — onto another saved view, WITHOUT changing that view's camera. Apply one view's highlighting scheme to many others in one graph: a list of target views is handled one view at a time. Both views must have their overrides baked in (Viewpoint.Save with bakeOverrides on).")]
    [NodeSearchTags("viewpoint", "view", "override", "appearance", "copy", "color", "transparency", "hidden", "apply")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint CopyOverrides(
        [ScalarInput] object fromViewpoint,
        [ScalarInput] object toViewpoint,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;
        var source = SavedItemTreeHelpers.ResolveStored<SavedViewpoint>(
            viewpoints.RootItem, fromViewpoint, "saved viewpoint");
        var target = SavedItemTreeHelpers.ResolveStored<SavedViewpoint>(
            viewpoints.RootItem, toViewpoint, "saved viewpoint");

        if (ReferenceEquals(source, target))
        {
            return target; // nothing to do — copying a view's look onto itself
        }

        // Apply the source view so its overrides become the live runtime state,
        // then snapshot them (CaptureRuntimeOverrides ignores the camera, leaving
        // a default one — the target's camera is restored below).
        viewpoints.CurrentSavedViewpoint = source;
        var captured = viewpoints.CaptureRuntimeOverrides();
        captured.DisplayName = target.DisplayName;

        // Put the target's own camera on the live view so ReplaceFromCurrentView
        // restores it onto the replacement while keeping the captured overrides.
        doc.CurrentViewpoint.CopyFrom(target.Viewpoint);

        // Replace the target in place (same folder, same position).
        var parentFolder = ReferenceEquals(target.Parent, viewpoints.RootItem)
            ? null
            : target.Parent as FolderItem;
        var siblings = parentFolder != null ? parentFolder.Children : viewpoints.Value;
        int index = SavedItemTreeHelpers.IndexByIdentity(siblings, target);
        if (index < 0)
        {
            throw new InvalidOperationException(
                "Could not locate the target viewpoint '" + target.DisplayName + "' under its parent.");
        }

        if (parentFolder != null)
        {
            viewpoints.ReplaceWithCopy(parentFolder, index, captured);
        }
        else
        {
            viewpoints.ReplaceWithCopy(index, captured);
        }

        siblings = parentFolder != null ? parentFolder.Children : viewpoints.Value;
        var updated = (SavedViewpoint)siblings[index];
        viewpoints.ReplaceFromCurrentView(updated); // camera = target's; overrides = source's
        return updated;
    }

    /// <summary>The display name of a saved viewpoint.</summary>
    /// <param name="viewpoint">The saved viewpoint.</param>
    /// <returns>The viewpoint's display name.</returns>
    [NodeName("SavedViewpoint.Name")]
    [NodeDescription("The display name of a saved viewpoint.")]
    [NodeSearchTags("viewpoint", "view", "name", "displayname")]
    [return: NodeName("name")]
    public static string Name(SavedViewpoint viewpoint)
    {
        if (viewpoint == null)
        {
            throw new ArgumentNullException(nameof(viewpoint), "No saved viewpoint provided.");
        }

        return viewpoint.DisplayName ?? string.Empty;
    }

    /// <summary>Deletes a saved viewpoint.</summary>
    /// <param name="viewpoint">The saved viewpoint (for example from Viewpoints.InFolder), or its name or folder path and name such as "Reviews/Week 12/Level 1".</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when a viewpoint was deleted; false when there is no such viewpoint.</returns>
    [NodeName("SavedViewpoint.Delete")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.ViewpointNodes.Delete@string,Autodesk.Navisworks.Api.Document")]
    [PortAlias("name", "viewpoint")]
    [NodeDescription(
        "Deletes a saved viewpoint: wire the viewpoint itself (so exactly that one goes, whatever its folder), or its name, or its folder path " +
        "and name (\"Reviews/Week 12/Level 1\"). A list deletes one viewpoint per entry. A name shared by several viewpoints deletes the first " +
        "one in the tree and shows a warning. Returns false when there is no such viewpoint, so a clean-up step can run twice.")]
    [NodeSearchTags("viewpoint", "view", "delete", "remove", "clean")]
    [return: NodeName("deleted")]
    public static bool Delete([ScalarInput] object viewpoint, Document? document = null)
    {
        if (viewpoint == null)
        {
            throw new ArgumentNullException(nameof(viewpoint), "No saved viewpoint provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;
        SavedViewpoint? found;
        switch (viewpoint)
        {
            case string name:
                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentException("No viewpoint name provided.", nameof(viewpoint));
                }

                found = SavedItemTreeHelpers.FindByNameOrPath<SavedViewpoint>(viewpoints.RootItem, name, "saved viewpoint");
                break;
            case SavedViewpoint item:
                try
                {
                    found = SavedItemTreeHelpers.FindStoredEquivalent(viewpoints.RootItem, item);
                }
                catch (Exception ex) when (ClashHelpers.IsDisposed(ex))
                {
                    NodeWarnings.Add("A viewpoint wired to SavedViewpoint.Delete was already removed or replaced by an earlier edit, so it was not deleted again.");
                    return false;
                }

                break;
            default:
                throw new ArgumentException(
                    "Cannot interpret a value of type '" + viewpoint.GetType().Name +
                    "' as a saved viewpoint. Wire the saved viewpoint itself, its name or its folder path and name.", nameof(viewpoint));
        }

        if (found == null)
        {
            return false;
        }

        var parent = found.Parent;
        return parent == null ? viewpoints.Remove(found) : viewpoints.Remove(parent, found);
    }

    /// <summary>Creates one saved viewpoint per clash result, aimed at the clash.</summary>
    /// <param name="results">The clash results (e.g. from ClashTest.Results).</param>
    /// <param name="folderName">Viewpoint folder to file them under: a name or a path such as "Clash Views/Week 12" (null/empty stores them at the top level).</param>
    /// <param name="nameFormat">How to name each viewpoint, with {name} (the result's name), {test} (its clash test) and {index} (its position in the list, from 1). Empty names it after the result, with the test in front when the results come from more than one test.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored viewpoints, one per result, named after the result.</returns>
    [NodeName("Viewpoints.FromClashResults")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.ViewpointNodes.FromClashResults@System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.Clash.ClashResult>,string,Autodesk.Navisworks.Api.Document")]
    [NodeDescription(
        "Batch-generates one saved viewpoint per clash result, camera aimed at the clash — the clash-triage staple. Existing same-named " +
        "viewpoints in the folder are replaced. Navisworks numbers results per test (Clash1, Clash2 ... in every test), so when the " +
        "results come from more than one test each viewpoint is named \"Test - Clash1\" and none replaces another; nameFormat sets the " +
        "name yourself ({test}, {name}, {index}). Names that would still repeat are numbered (2), (3) and the node shows a warning.")]
    [NodeSearchTags("viewpoints", "clash", "results", "batch", "generate", "triage", "name", "folder")]
    [return: NodeName("viewpoints")]
    public static List<SavedViewpoint> FromClashResults(
        IEnumerable<ClashResult> results,
        string? folderName = "Clash Views",
        [NodePanel("Advanced")] string? nameFormat = "",
        Document? document = null)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results), "No clash results provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);
        var viewpoints = doc.SavedViewpoints;

        // The names are decided for the whole batch first (a result's name alone is not unique across tests), so the list is read once.
        var batch = new List<ClashResult>();
        foreach (var candidate in results)
        {
            if (candidate != null)
            {
                batch.Add(candidate);
            }
        }

        results = batch;
        var viewpointNames = NameViewpoints(batch, nameFormat);

        var folder = ViewpointStore.ResolveFolder(viewpoints, folderName);

        // Where each viewpoint of the target folder is, by name, read once and kept up to date as viewpoints are
        // added: searching the growing folder twice per result made the run slower with every viewpoint it made.
        var names = NavisValues.BuildNameIndex<SavedViewpoint>(folder != null ? folder.Children : viewpoints.Value);

        var stored = new List<SavedViewpoint>();
        var position = 0;
        foreach (var result in results)
        {
            var camera = clash.TestsData.TestsViewpointForResult(result);
            var name = viewpointNames[position++];
            var saved = new SavedViewpoint(camera) { DisplayName = name };

            var children = folder != null ? folder.Children : viewpoints.Value;
            int expectedIndex;
            if (names.TryGetIndex(name, out var existingIndex))
            {
                if (folder != null)
                {
                    viewpoints.ReplaceWithCopy(folder, existingIndex, saved);
                }
                else
                {
                    viewpoints.ReplaceWithCopy(existingIndex, saved);
                }

                expectedIndex = existingIndex;
            }
            else
            {
                if (folder != null)
                {
                    viewpoints.AddCopy(folder, saved);
                }
                else
                {
                    viewpoints.AddCopy(saved);
                }

                expectedIndex = names.Append(name);
            }

            // AddCopy/ReplaceWithCopy store a copy — hand the stored instance downstream.
            stored.Add(NavisValues.ConfirmStored<SavedViewpoint>(
                children,
                () => folder != null ? folder.Children : viewpoints.Value,
                names,
                name,
                expectedIndex) ?? saved);
        }

        return stored;
    }

    /// <summary>The name of the viewpoint made for each result of a batch: unique within the batch.</summary>
    private static IReadOnlyList<string> NameViewpoints(List<ClashResult> batch, string? nameFormat)
    {
        var baseNames = new List<string>(batch.Count);
        var tests = new List<string?>(batch.Count);
        foreach (var result in batch)
        {
            baseNames.Add(string.IsNullOrEmpty(result.DisplayName) ? "Clash" : result.DisplayName);
            tests.Add(ClashNaming.TestNameOf(result));
        }

        DisambiguatedNames unique;
        if (NameTemplate.HasTokens(nameFormat))
        {
            var formatted = new List<string>(batch.Count);
            for (int i = 0; i < batch.Count; i++)
            {
                formatted.Add(NameTemplate.Apply(
                    nameFormat!,
                    new Dictionary<string, string>
                    {
                        ["test"] = tests[i] ?? string.Empty,
                        ["name"] = baseNames[i],
                        ["index"] = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    },
                    nameof(nameFormat)));
            }

            unique = NameDisambiguator.Make(formatted, new string?[batch.Count]);
        }
        else
        {
            unique = NameDisambiguator.Make(baseNames, tests);
        }

        if (unique.Renumbered > 0)
        {
            NodeWarnings.Add(
                unique.Renumbered + " viewpoint(s) would have had the same name as another one and were numbered (2), (3) ... " +
                "Use nameFormat with {test}, {name} or {index} to name them as you like.");
        }

        return unique.Names;
    }
}
