using System;
using System.Linq;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// How the search, selection and selection-set nodes of src/CamelGraph.Navisworks are declared after the audit: the merged
/// Search.ByProperty, the folder and mode inputs of the set creators, the live-state reads and the declared effects. That project cannot
/// be loaded on the Linux test hosts, so these tests read the source.
/// </summary>
public class SearchAndSetsWiringTests
{
    private const string ChangesModel = "[NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]";

    [Fact]
    public void SearchByPropertyTakesExistsAListOfValuesAndAScope()
    {
        var declaration = NavisworksMethodText.Declaration("SearchNodes.cs", "ByProperty");

        Assert.Contains("object? value = null", declaration);
        Assert.Contains("\"exists\")]", declaration);
        Assert.Contains("[MultiInput] IEnumerable<ModelItem>? within = null", declaration);
        Assert.Contains("[NodePanel(\"Advanced\")]", declaration);
        Assert.Contains("[NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]", declaration);
        // The id it had before the three inputs were added still loads.
        Assert.Contains("[NodeAliases(\"CamelGraph.Navisworks.SearchNodes.ByProperty@string,string,object,string,string,Autodesk.Navisworks.Api.Document\")]", declaration);
        // It keeps the search words of the nodes it replaces.
        foreach (var word in new[] { "\"has\"", "\"exists\"", "\"category\"", "\"tab\"", "\"scoped\"", "\"within\"", "\"refine\"", "\"one of\"" })
        {
            Assert.Contains(word, declaration);
        }
    }

    [Theory]
    [InlineData("HasProperty")]
    [InlineData("HasCategory")]
    [InlineData("InItems")]
    public void TheMergedNodesAreRetiredButStillRunThroughTheSharedSearch(string method)
    {
        var declaration = NavisworksMethodText.Declaration("SearchNodes.cs", method);
        var body = NavisworksMethodText.Body("SearchNodes.cs", method);

        Assert.Contains("[NodeDeprecated(\"Search.ByProperty\")]", declaration);
        Assert.Contains("[NodeAliases(\"CamelGraph.Navisworks.SearchNodes." + method + "@", declaration);
        Assert.Contains("return Find(", body);
    }

    [Fact]
    public void ExistsIsTheModeOfTheRetiredHasNodes()
    {
        Assert.Contains("\"exists\"", NavisworksMethodText.Body("SearchNodes.cs", "HasProperty"));
        Assert.Contains("\"exists\"", NavisworksMethodText.Body("SearchNodes.cs", "HasCategory"));
        // Scoped search keeps refusing an unwired items input instead of searching the whole model.
        var inItems = NavisworksMethodText.Body("SearchNodes.cs", "InItems");
        Assert.True(inItems.IndexOf("ArgumentNullException", StringComparison.Ordinal) < inItems.IndexOf("Find(", StringComparison.Ordinal));
    }

    [Fact]
    public void ASearchWithNoConditionIsNeverRunBecauseItWouldSelectEverything()
    {
        var body = NavisworksMethodText.Body("SearchNodes.cs", "BuildSearch");

        Assert.Contains("search.SearchConditions.Count == 0", body);
        Assert.Contains("throw new InvalidOperationException(", body);
    }

    [Fact]
    public void AnEmptyListOrScopeSearchesNothingAndSaysSo()
    {
        var body = NavisworksMethodText.Body("SearchNodes.cs", "Find");

        Assert.Contains("plan.MatchesNothing", body);
        Assert.Contains("NodeWarnings.Add(", body);
        Assert.Contains("scope.Count == 0", body);
    }

    [Fact]
    public void CreateFromSearchHasTheModesOfSearchByPropertyAndAFolder()
    {
        var declaration = NavisworksMethodText.Declaration("SelectionSetNodes.cs", "CreateFromSearch");

        Assert.Contains("object? value = null", declaration);
        Assert.Contains("\"exists\")]", declaration);
        Assert.Contains("string mode = \"equals\"", declaration);
        Assert.Contains("[ScalarInput][PortKinds(\"selection\")] object? folder = null", declaration);
        Assert.Contains("[NodeAliases(\"CamelGraph.Navisworks.SelectionSetNodes.CreateFromSearch@string,string,string,object,Autodesk.Navisworks.Api.Document\")]", declaration);
        Assert.Contains(ChangesModel, declaration);
        Assert.Contains("SearchNodes.CreateSearch(categoryName, propertyName, value, mode)", NavisworksMethodText.Body("SelectionSetNodes.cs", "CreateFromSearch"));
    }

    [Fact]
    public void CreateTakesAFolderAndKeepsItsEarlierId()
    {
        var declaration = NavisworksMethodText.Declaration("SelectionSetNodes.cs", "Create");

        Assert.Contains("[ScalarInput][PortKinds(\"selection\")] object? folder = null", declaration);
        Assert.Contains("[NodeAliases(\"CamelGraph.Navisworks.SelectionSetNodes.Create@string,System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,Autodesk.Navisworks.Api.Document\")]", declaration);
        Assert.Contains(ChangesModel, declaration);

        // The set is replaced in the place it goes to (the folder, or the top level), not always at the top level.
        var body = NavisworksMethodText.Body("SelectionSetNodes.cs", "Create");
        Assert.Contains("SelectionSetFolders.ResolveOrCreate(doc, folder)", body);
        Assert.DoesNotContain("FindTopLevelIndex", body);
    }

    [Fact]
    public void BulkSetsAcceptAFolderPathAndKeepEveryStorageVariantOfAValue()
    {
        var body = NavisworksMethodText.Body("SelectionSetNodes.cs", "BulkByPropertyValues");

        Assert.Contains("SelectionSetFolders.ResolveOrCreate(doc, folderName)", body);
        Assert.Contains("new DistinctValueGroups<VariantData>()", body);
        Assert.Contains("groups.VariantsOf(value)", body);
        Assert.Contains("groups.SortedKeys()", body);
        // Gathering the items that carry the property is the merged search, not the retired node.
        Assert.Contains("SearchNodes.Find(", body);
        Assert.DoesNotContain("SearchNodes.HasProperty(", body);
    }

    [Theory]
    [InlineData("SelectionNodes.cs", "Current")]
    [InlineData("SelectionExtraNodes.cs", "Invert")]
    [InlineData("SelectionExtraNodes.cs", "Info")]
    [InlineData("SelectionSetNodes.cs", "All")]
    [InlineData("SelectionSetNodes.cs", "Items")]
    [InlineData("SelectionSetNodes.cs", "ByName")]
    [InlineData("SelectionSetFolderNodes.cs", "InFolder")]
    public void NodesThatReadLiveHostStateAreRunOnEveryRun(string file, string method)
    {
        Assert.Contains("[LiveState]", NavisworksMethodText.Declaration(file, method));
    }

    [Fact]
    public void TheDescriptionsNoLongerPromiseWhatWasNotTrue()
    {
        // Selection.Current did not re-read the selection on a second run until it was marked live; its description now says it does.
        Assert.Contains("read again on every run", NavisworksMethodText.Declaration("SelectionNodes.cs", "Current"));
    }

    [Theory]
    [InlineData("SelectionSetNodes.cs", "Create")]
    [InlineData("SelectionSetNodes.cs", "CreateFromSearch")]
    [InlineData("SelectionSetNodes.cs", "Delete")]
    [InlineData("SelectionSetNodes.cs", "BulkByPropertyValues")]
    [InlineData("SelectionExtraNodes.cs", "Duplicate")]
    [InlineData("SavedItemTreeNodes.cs", "CreateFolder")]
    [InlineData("SavedItemTreeNodes.cs", "MoveToFolder")]
    [InlineData("SelectionSetFolderNodes.cs", "SortFolder")]
    [InlineData("SelectionSetFolderNodes.cs", "RenameFolder")]
    [InlineData("SelectionSetFolderNodes.cs", "DeleteFolder")]
    [InlineData("CustomPropertyNodes.cs", "SetCustom")]
    public void NodesThatEditTheDocumentDeclareThatTheyChangeTheModel(string file, string method)
    {
        // The SavedItemTreeNodes file also holds the viewpoint folder nodes; the sets versions are the ones after the second class.
        var declaration = file == "SavedItemTreeNodes.cs"
            ? SetsTreeDeclaration(method)
            : NavisworksMethodText.Declaration(file, method);

        Assert.Contains("NodeEffects.ChangesModel", declaration);
    }

    private static string SetsTreeDeclaration(string method)
    {
        var source = NavisworksSourceText.Source("SavedItemTreeNodes.cs");
        var sets = source.Substring(source.IndexOf("public static class SelectionSetTreeNodes", StringComparison.Ordinal));
        return NavisworksMethodText.DeclarationIn("\n" + sets.Substring(sets.IndexOf("{\n", StringComparison.Ordinal)), method);
    }

    [Fact]
    public void RenameAndMoveOfSetsLaceOverLists()
    {
        var source = NavisworksSourceText.Source("SavedItemTreeNodes.cs");
        var sets = source.Substring(source.IndexOf("public static class SelectionSetTreeNodes", StringComparison.Ordinal));

        Assert.Contains("Rename([ScalarInput][PortKinds(\"selection\")] object selectionSet, string newName", sets);
        Assert.Contains("MoveToFolder([ScalarInput][PortKinds(\"selection\")] object selectionSet, [ScalarInput][PortKinds(\"selection\")] object folder", sets);
        Assert.DoesNotContain("Batch-rename via lacing", sets);
    }

    [Fact]
    public void SelectionSetInfoCanSkipTheCountAndKeepsItsEarlierId()
    {
        var declaration = NavisworksMethodText.Declaration("SelectionExtraNodes.cs", "Info");
        var body = NavisworksMethodText.Body("SelectionExtraNodes.cs", "Info");

        Assert.Contains("bool includeCount = true", declaration);
        Assert.Contains("[NodeAliases(\"CamelGraph.Navisworks.SelectionExtraNodes.Info@Autodesk.Navisworks.Api.SelectionSet,Autodesk.Navisworks.Api.Document\")]", declaration);
        Assert.Contains("includeCount ?", body);
        Assert.Contains("IsStored(root, selectionSet)", body);
    }

    [Fact]
    public void SelectionSetByNameAndSearchByPropertyAreTintedAsLookUps()
    {
        Assert.Contains("NodeFunction.Info", NavisworksMethodText.Declaration("SearchNodes.cs", "ByProperty"));
        Assert.Contains("NodeFunction.Info", NavisworksMethodText.Declaration("SelectionSetNodes.cs", "ByName"));
    }

    [Fact]
    public void SearchByGuidTakesSeveralWires()
    {
        Assert.Contains("[MultiInput] IList<object?> guids", NavisworksMethodText.Declaration("SelectionExtraNodes.cs", "ByGuid"));
    }

    [Fact]
    public void TheSetsFolderNodesExistWithTheNamesTheViewpointFolderNodesHave()
    {
        var methods = NavisworksMethodText.PublicStaticMethods("SelectionSetFolderNodes.cs");
        Assert.Equal(new[] { "InFolder", "SortFolder", "RenameFolder", "DeleteFolder" }, methods);

        var source = NavisworksSourceText.Source("SelectionSetFolderNodes.cs");
        foreach (var name in new[] { "SelectionSets.InFolder", "SelectionSets.SortFolder", "SelectionSets.RenameFolder", "SelectionSets.DeleteFolder" })
        {
            Assert.Contains("[NodeName(\"" + name + "\")]", source);
        }
    }

    [Fact]
    public void DeletingAFolderThatStillHoldsThingsNeedsDeleteContents()
    {
        var body = NavisworksMethodText.Body("SelectionSetFolderNodes.cs", "DeleteFolder");

        Assert.Contains("bool deleteContents = false", NavisworksMethodText.Declaration("SelectionSetFolderNodes.cs", "DeleteFolder"));
        Assert.Contains("stored.Children.Count > 0 && !deleteContents", body);
        Assert.Contains("return false;", body);
    }

    [Fact]
    public void SortingMovesNothingWhenTheFolderIsAlreadyInOrder()
    {
        var source = NavisworksSourceText.Source("SelectionSetFolderNodes.cs");

        Assert.Contains("SavedTreePaths.MovesToFront(order)", source);
        Assert.Contains("SavedTreePaths.SortedOrder(", source);
    }
}
