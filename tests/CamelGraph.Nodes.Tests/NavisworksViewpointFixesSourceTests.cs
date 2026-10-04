using System;
using System.Text.RegularExpressions;
using Xunit;
using static CamelGraph.Nodes.Tests.NavisworksSourceText;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The Navisworks node project cannot be loaded by the Linux test projects, so this reads the source of the viewpoint, camera and
/// appearance nodes that were fixed in the node-library audit and pins how they are written: what each node refuses, warns about,
/// reuses or leaves alone. The plain .NET logic behind them (names, paths, sorting) has its own unit tests
/// (<see cref="ViewpointNamingHelpersTests"/>).
/// </summary>
public class NavisworksViewpointFixesSourceTests
{
    private static string WithoutComments(string text) => Regex.Replace(text, @"//[^\n]*", string.Empty);

    /// <summary>The attributes of one node: from its [NodeName] to the method's "public static".</summary>
    private static string Attributes(string source, string nodeName)
    {
        // Some nodes put [NodeFunction] or [NodeEffects] above [NodeName], so the block starts at the end of the XML comment.
        var at = source.IndexOf("[NodeName(\"" + nodeName + "\")]", StringComparison.Ordinal);
        Assert.True(at >= 0, "node not found: " + nodeName);
        var start = source.LastIndexOf("</returns>", at, StringComparison.Ordinal);
        var end = source.IndexOf("public static", at, StringComparison.Ordinal);
        return source.Substring(Math.Max(start, 0), end - Math.Max(start, 0));
    }

    // ------------------------------------------------------------------ NVC-03

    [Fact]
    public void DuplicateFolderRefusesToCopyAFolderIntoItselfBeforeCopyingAnything()
    {
        var body = WithoutComments(Body(Source("SavedItemTreeNodes.cs"), "public static FolderItem DuplicateFolder("));

        Assert.Contains("ReferenceEquals(target, source)", body);
        Assert.True(
            body.IndexOf("ReferenceEquals(target, source)", StringComparison.Ordinal) < body.IndexOf("CopyFolderContents(viewpoints, source, target)", StringComparison.Ordinal),
            "the check has to come before the copy");
        Assert.Contains("throw new InvalidOperationException", body);
    }

    [Fact]
    public void DuplicateFolderTopsAFolderUpInsteadOfAddingEverythingAgain()
    {
        var body = WithoutComments(Body(Source("SavedItemTreeNodes.cs"), "private static void CopyFolderContents("));

        // What the target holds is read once, a same-named viewpoint of the same kind is replaced, anything else added ...
        Assert.Contains("existing.TryGetValue(child.DisplayName", body);
        Assert.Contains("viewpoints.ReplaceWithCopy(target, index, child)", body);
        Assert.Contains("viewpoints.AddCopy(target, child)", body);
        // ... and the source's own list is copied first, so adding to the target never changes the list being walked.
        Assert.True(
            body.IndexOf("items.Add(child)", StringComparison.Ordinal) < body.IndexOf("viewpoints.AddCopy(target, child)", StringComparison.Ordinal));
        Assert.DoesNotContain("foreach (var child in source.Children)\n        {\n            if (child is FolderItem subFolder)", body);
    }

    // ------------------------------------------------------------------ NVC-06

    [Theory]
    [InlineData("SavedItemTreeNodes.cs", "public static SavedViewpoint Rename(", "[ScalarInput] object viewpoint")]
    [InlineData("SavedItemTreeNodes.cs", "public static SavedViewpoint Duplicate(", "[ScalarInput] object viewpoint")]
    [InlineData("SavedItemTreeNodes.cs", "public static SavedViewpoint MoveToFolder(", "[ScalarInput] object viewpoint, [ScalarInput] object folder")]
    [InlineData("SavedItemTreeNodes.cs", "public static Dictionary<string, object?> Folder(", "[ScalarInput] object viewpoint")]
    [InlineData("SavedItemTreeNodes.cs", "public static FolderItem RenameFolder(", "[ScalarInput] object folder")]
    [InlineData("SavedItemTreeNodes.cs", "public static FolderItem DuplicateFolder(", "[ScalarInput] object folder")]
    [InlineData("ViewpointNodes.cs", "public static bool Delete(", "[ScalarInput] object viewpoint")]
    public void TheObjectPortsThatTakeOneViewpointOrFolderMapOverAList(string file, string signature, string port)
    {
        Assert.Contains(port, Body(Source(file), signature));
    }

    [Fact]
    public void TheMultiLineSignaturesAlsoMarkTheirSinglePorts()
    {
        var tree = Source("SavedItemTreeNodes.cs");
        Assert.Contains("[ScalarInput] object? folder = null,\n        bool recursive = true,", tree); // Viewpoints.InFolder
        Assert.Contains("[ScalarInput] object? folder = null,\n        bool recursive = false,\n        bool numeric = false,", tree); // SortFolder
        Assert.Contains("[ScalarInput] object folder,\n        bool contentsOnly = false,", tree); // DeleteFolder
        Assert.Contains("[ScalarInput] object fromViewpoint,\n        [ScalarInput] object toViewpoint,", Source("ViewpointNodes.cs")); // CopyOverrides
        Assert.Contains("[ScalarInput] object? viewpoint = null,", Source("ViewpointVisibilityNodes.cs")); // Viewpoint.VisibleItems
    }

    [Fact]
    public void TheDescriptionsNoLongerPromiseLacingThatDidNotWork()
    {
        var tree = Source("SavedItemTreeNodes.cs");
        Assert.DoesNotContain("Batch-rename via lacing.", tree.Substring(0, tree.IndexOf("public static class SelectionSetTreeNodes", StringComparison.Ordinal)));
    }

    // ------------------------------------------------------------------ NVC-07

    [Fact]
    public void IsolatingNothingDoesNothingAndSaysSoInsteadOfHidingTheWholeModel()
    {
        var body = WithoutComments(Body(Source("AppearanceNodes.cs"), "public static List<ModelItem> Isolate("));

        Assert.Contains("list.Count == 0", body);
        Assert.Contains("NodeWarnings.Add(", body);
        Assert.True(
            body.IndexOf("NodeWarnings.Add(", StringComparison.Ordinal) < body.IndexOf("ResetAllHidden()", StringComparison.Ordinal),
            "the empty list must be answered before the hidden state is touched");
        Assert.True(
            body.IndexOf("return list;", StringComparison.Ordinal) < body.IndexOf("ResetAllHidden()", StringComparison.Ordinal),
            "the node returns before anything is hidden");
    }

    // ------------------------------------------------------------------ NVC-19

    [Fact]
    public void ByNameAndDeleteAcceptAFolderPathAndWarnAboutASharedName()
    {
        var helpers = Source("Internal", "SavedItemTreeHelpers.cs");
        var find = WithoutComments(Body(helpers, "internal static T? FindByNameOrPath<T>("));
        Assert.Contains("NodeWarnings.Add(", find);
        Assert.Contains("SavedItemPath.IsPath(text)", find);
        Assert.Contains("the first one in the tree was used", find);

        var nodes = Source("ViewpointNodes.cs");
        Assert.Contains("FindByNameOrPath<SavedViewpoint>(doc.SavedViewpoints.RootItem, name, \"saved viewpoint\")", Body(nodes, "public static SavedViewpoint ByName("));
        var delete = Body(nodes, "public static bool Delete(");
        Assert.Contains("FindByNameOrPath<SavedViewpoint>(", delete);
        Assert.Contains("case SavedViewpoint item:", delete); // the viewpoint itself, not only a name
        Assert.Contains("FindByNameOrPath<T>(root, name, kindLabel)", Body(helpers, "internal static T ResolveStored<T>("));
    }

    [Fact]
    public void AStaleViewpointIsReportedInPlainWordsNotAsADisposedHandle()
    {
        var helpers = Source("Internal", "SavedItemTreeHelpers.cs");
        var stale = Body(helpers, "internal static T? FindStoredEquivalentOrStale<T>(");
        Assert.Contains("ClashHelpers.IsDisposed(ex)", stale);
        Assert.Contains("is stale", stale);
        Assert.Contains("FindStoredEquivalentOrStale(root, item, kindLabel)", Body(helpers, "internal static T ResolveStored<T>("));
    }

    // ------------------------------------------------------------------ NVC-26, NVC-29, NVC-04

    [Fact]
    public void TheTwoRetiredSaveNodesForwardToViewpointSave()
    {
        var source = Source("ViewpointNodes.cs");

        Assert.Contains("return Save(name, null, false, null, document);", Body(source, "public static SavedViewpoint SaveCurrent("));
        Assert.Contains("return Save(name, folderName, true, null, document);", Body(source, "public static SavedViewpoint SaveWithOverrides("));
        Assert.Matches(new Regex(@"\[NodeName\(""Viewpoint\.SaveCurrent""\)\]\s*\[NodeDeprecated\(""Viewpoint\.Save""\)\]"), source);
        Assert.Matches(new Regex(@"\[NodeName\(""Viewpoint\.SaveWithOverrides""\)\]\s*\[NodeDeprecated\(""Viewpoint\.Save""\)\]"), source);
    }

    [Fact]
    public void ViewpointSaveBakesTheOverridesOnlyWhenAskedAndFilesTheViewpointThroughTheSharedHelper()
    {
        var body = WithoutComments(Body(Source("ViewpointNodes.cs"), "public static SavedViewpoint Save("));

        Assert.Contains("bakeOverrides\n            ? viewpoints.CaptureRuntimeOverrides()\n            : new SavedViewpoint(doc.CurrentViewpoint.ToViewpoint())", body);
        Assert.Contains("ViewpointStore.ResolveFolder(viewpoints, folder)", body);
        Assert.Contains("ViewpointStore.Put(viewpoints, saved, target)", body);
        Assert.True(
            body.IndexOf("if (!bakeOverrides)", StringComparison.Ordinal) < body.IndexOf("viewpoints.ReplaceFromCurrentView(stored)", StringComparison.Ordinal),
            "the camera is pulled into the stored viewpoint only when the overrides were baked");
    }

    [Fact]
    public void EveryNodeThatFilesAViewpointInAFolderUsesTheSameFolderRule()
    {
        Assert.Contains("ViewpointStore.ResolveFolder(viewpoints, folderName)", Body(Source("ViewpointNodes.cs"), "public static List<SavedViewpoint> FromClashResults("));
        Assert.Contains("ViewpointStore.ResolveFolder(tree, segments)", Body(Source("ViewpointTransferNodes.cs"), "private static FolderItem? ResolveTargetFolder("));

        var store = Source("Internal", "ViewpointStore.cs");
        Assert.Contains("SavedItemPath.Split(folder)", store);
        Assert.Contains("NavisValues.FindTopLevelIndex<SavedViewpoint>(children, name)", store);
    }

    [Fact]
    public void ViewpointsFromClashResultsNamesTheBatchBeforeItStoresAnything()
    {
        var body = WithoutComments(Body(Source("ViewpointNodes.cs"), "public static List<SavedViewpoint> FromClashResults("));

        Assert.Contains("var viewpointNames = NameViewpoints(batch, nameFormat);", body);
        Assert.True(
            body.IndexOf("NameViewpoints(batch, nameFormat)", StringComparison.Ordinal) < body.IndexOf("AddCopy", StringComparison.Ordinal));
        Assert.DoesNotContain("result.DisplayName", body.Substring(body.IndexOf("foreach (var result in results)", StringComparison.Ordinal)));

        var naming = Body(Source("ViewpointNodes.cs"), "private static IReadOnlyList<string> NameViewpoints(");
        Assert.Contains("NameDisambiguator.Make(baseNames, tests)", naming);
        Assert.Contains("ClashNaming.TestNameOf(result)", naming);
        Assert.Contains("NodeWarnings.Add(", naming);
        Assert.Contains("NameTemplate.Apply(", naming);
    }

    // ------------------------------------------------------------------ NVC-09, NVC-39

    [Theory]
    [InlineData("CameraNodes.cs", "public static Dictionary<string, object?> Current(", "object? after = null, Document? document = null")]
    [InlineData("CameraNodes.cs", "public static bool LookAt(", "object? after = null, Document? document = null")]
    [InlineData("CameraNodes.cs", "public static bool SetProjection(", "object? after = null, Document? document = null")]
    [InlineData("CameraNodes.cs", "public static bool SetFieldOfView(", "object? after = null, Document? document = null")]
    [InlineData("CameraNodes.cs", "public static bool ZoomToItems(", "object? after = null, Document? document = null")]
    [InlineData("ViewpointNodes.cs", "public static SavedViewpoint Apply(", "object? after = null, Document? document = null")]
    [InlineData("ViewpointExtraNodes.cs", "public static SavedViewpoint Update(", "object? after = null, Document? document = null")]
    public void TheNodesThatReadOrDriveTheLiveViewHaveAnAfterInputLikeResetTemporary(string file, string signature, string tail)
    {
        Assert.Contains(tail, Body(Source(file), signature));
    }

    [Fact]
    public void TheNodesThatReadTheLiveViewOrTheSavedViewpointsAreRunOnEveryRun()
    {
        var camera = Source("CameraNodes.cs");
        Assert.Contains("[LiveState]", Attributes(camera, "Camera.Current"));
        Assert.Contains("[LiveState]", Attributes(camera, "Camera.Save"));
        var nodes = Source("ViewpointNodes.cs");
        Assert.Contains("[LiveState]", Attributes(nodes, "Viewpoints.All"));
        Assert.Contains("[LiveState]", Attributes(nodes, "SavedViewpoint.ByName"));
        var tree = Source("SavedItemTreeNodes.cs");
        Assert.Contains("[LiveState]", Attributes(tree, "Viewpoints.InFolder"));
        Assert.Contains("[LiveState]", Attributes(tree, "SavedViewpoint.Folder"));
        Assert.Contains("[LiveState]", Attributes(Source("ViewpointExtraNodes.cs"), "SavedViewpoint.Info"));
        Assert.Contains("[LiveState]", Attributes(Source("ViewpointVisibilityNodes.cs"), "Viewpoint.VisibleItems"));
    }

    [Fact]
    public void CameraSaveKeepsACopyOfTheCameraAndRestorePutsItBack()
    {
        var camera = WithoutComments(Source("CameraNodes.cs"));
        var save = Body(camera, "public static Viewpoint Save(");
        Assert.Contains("doc.CurrentViewpoint.CreateCopy()", save);

        var restore = Body(camera, "public static bool Restore(");
        Assert.Contains("doc.CurrentViewpoint.CopyFrom(camera)", restore);
        Assert.Contains("viewpoint is SavedViewpoint saved", restore);
        Assert.Contains("[ScalarInput] object viewpoint", restore);
    }

    // ------------------------------------------------------------------ NVC-35, NVC-40

    [Fact]
    public void SortFolderCanSortNumbersByValue()
    {
        var tree = Source("SavedItemTreeNodes.cs");
        var sort = Body(tree, "public static FolderItem? SortFolder(");

        Assert.Contains("numeric", sort);
        Assert.Contains("NaturalNameComparer.Instance", sort);
        Assert.Contains("StringComparer.OrdinalIgnoreCase", sort); // the default stays what it was
        Assert.Contains("comparer.Compare(a.DisplayName, b.DisplayName)", Body(tree, "private static void SortFolderContents("));
    }

    [Fact]
    public void ViewpointsDeleteFolderEmptiesOrRemovesAFolderAndAnswersFalseWhenItIsGone()
    {
        var body = WithoutComments(Body(Source("SavedItemTreeNodes.cs"), "public static Dictionary<string, object?> DeleteFolder("));

        Assert.Contains("viewpoints.RemoveAt(stored, i)", body);
        Assert.Contains("viewpoints.Remove(parent, stored)", body);
        Assert.Contains("[\"deleted\"] = false", body);
        Assert.Contains("contentsOnly", body);
    }

    [Fact]
    public void ColourByValuesHasATemporaryVariantThatSharesTheColouring()
    {
        var source = Source("AppearanceNodes.cs");
        Assert.Contains("ColorByValuesCore(items, values, palette, document, false)", Body(source, "public static Dictionary<string, object?> ColorByValues("));
        Assert.Contains("ColorByValuesCore(items, values, palette, document, true)", Body(source, "public static Dictionary<string, object?> ColorByValuesTemporary("));
        var core = Body(source, "private static Dictionary<string, object?> ColorByValuesCore(");
        Assert.Contains("OverrideTemporaryColor(buckets[keys[i]], color)", core);
        Assert.Contains("OverridePermanentColor(buckets[keys[i]], color)", core);
    }

    // ------------------------------------------------------------------ NVC-14

    [Theory]
    [InlineData("ViewpointNodes.cs", "SavedViewpoint.Apply")]
    [InlineData("ViewpointNodes.cs", "Viewpoint.Save")]
    [InlineData("ViewpointNodes.cs", "Viewpoints.FromClashResults")]
    [InlineData("ViewpointNodes.cs", "SavedViewpoint.Delete")]
    [InlineData("ViewpointNodes.cs", "SavedViewpoint.CopyOverrides")]
    [InlineData("SavedItemTreeNodes.cs", "Viewpoints.CreateFolder")]
    [InlineData("SavedItemTreeNodes.cs", "Viewpoints.DeleteFolder")]
    [InlineData("SavedItemTreeNodes.cs", "Viewpoints.DuplicateFolder")]
    [InlineData("SavedItemTreeNodes.cs", "SavedViewpoint.Rename")]
    [InlineData("ViewpointExtraNodes.cs", "SavedViewpoint.Update")]
    [InlineData("ViewpointTransferNodes.cs", "Viewpoints.ImportFile")]
    [InlineData("AppearanceNodes.cs", "Appearance.OverrideColor")]
    [InlineData("AppearanceNodes.cs", "Appearance.Isolate")]
    [InlineData("AppearanceNodes.cs", "Appearance.Hide")]
    [InlineData("AppearanceNodes.cs", "Appearance.ColorByValues")]
    public void EveryNodeThatEditsTheDocumentDeclaresThatItChangesTheModel(string file, string nodeName)
    {
        Assert.Contains("[NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]", Attributes(Source(file), nodeName));
    }

    [Fact]
    public void ViewpointFilesAreWrittenAndReadThroughThePathResolver()
    {
        var transfer = Source("ViewpointTransferNodes.cs");

        // (Both methods hold #if lines at the left margin, which the method reader cannot cross, so the file is searched.)
        Assert.Contains("[NodePath(NodePathMode.Save, Filter = \"Viewpoint package (*.json)|*.json|All files (*.*)|*.*\")] string filePath,", transfer);
        Assert.Contains("[NodePath(NodePathMode.Open, Filter = \"Viewpoint package (*.json)|*.json|All files (*.*)|*.*\")] string filePath,", transfer);
        Assert.Contains("ViewpointPackagePaths.ResolveForWrite(\n            PathResolver.Resolve(filePath),", transfer);
        Assert.Contains("ViewpointPackagePaths.ResolveForRead(\n            PathResolver.Resolve(filePath),", transfer);
        Assert.Contains("[NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]", Attributes(transfer, "Viewpoints.ExportFile"));
    }
}
