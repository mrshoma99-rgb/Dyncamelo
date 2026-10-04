using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;

namespace CamelGraph.Navisworks;

/// <summary>Model QA nodes: data completeness and duplicate-geometry audits.</summary>
[NodeCategory("Navisworks.Analysis")]
public static class AuditNodes
{
    /// <summary>Finds items that are missing a property.</summary>
    /// <param name="categoryName">Category display name (e.g. "Element"). Internal names do not match.</param>
    /// <param name="propertyName">Property display name (e.g. "Level"). Internal names do not match.</param>
    /// <param name="items">Items to audit (defaults to the whole model).</param>
    /// <param name="geometryOnly">True to report only geometry-bearing items (skips containers).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The offending items and how many there are.</returns>
    [NodeName("Audit.MissingProperty")]
    [NodeDescription("Finds every item that does NOT carry the given property — the data-completeness audit. Lace over a property list to batch-audit.")]
    [NodeSearchTags("audit", "missing", "property", "qa", "completeness", "not defined")]
    [MultiReturn("items", "count")]
    [PortKinds("item*", "integer")]
    public static Dictionary<string, object?> MissingProperty(
        [NodeTabChoice("items")] string categoryName,
        [NodePropertyChoice("items", "categoryName")] string propertyName,
        IEnumerable<ModelItem>? items = null,
        bool geometryOnly = true,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(categoryName))
        {
            throw new ArgumentException("No property category name provided.", nameof(categoryName));
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            throw new ArgumentException("No property name provided.", nameof(propertyName));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var search = new Search();
        if (items != null)
        {
            search.Selection.CopyFrom(NavisValues.ToItemCollection(items));
        }
        else
        {
            search.Selection.SelectAll();
        }

        search.Locations = SearchLocations.DescendantsAndSelf;
        search.SearchConditions.Add(
            SearchCondition.HasPropertyByDisplayName(categoryName, propertyName).Negate());

        // reportProgress must stay false: progress pumping can re-enter the host.
        var found = NavisValues.ToItemList(search.FindAll(doc, false));
        if (geometryOnly)
        {
            found = found.FindAll(item => item.HasGeometry);
        }

        return new Dictionary<string, object?>
        {
            ["items"] = found,
            ["count"] = found.Count,
        };
    }

    /// <summary>Finds pairs of duplicated (double-exported) geometry among items.</summary>
    /// <param name="items">The model items to audit.</param>
    /// <param name="tolerance">Duplicate tolerance, in the unit of the units input.</param>
    /// <param name="units">The unit of the tolerance: "document" (the file's own unit, often feet) or a unit name such as Meters or Millimeters.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Index-aligned lists of duplicate pairs and the pair count.</returns>
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.AuditNodes.DuplicateItems@System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,double,Autodesk.Navisworks.Api.Document")]
    [NodeName("Audit.DuplicateItems")]
    [NodeDescription(
        "Finds duplicated geometry (double-exported elements) by running a temporary Duplicate clash test over the items. The test is added to the " +
        "Clash Detective tests for the moment the check runs and removed again, so the node changes the clash tests (and the Script Player says so). " +
        "tolerance is in the unit of the Advanced input units; \"document\" means the file's own unit, which is often feet, so name the unit when you know it.")]
    [NodeSearchTags("audit", "duplicate", "geometry", "double", "export", "qa")]
    [MultiReturn("items1", "items2", "count")]
    [PortKinds("item*", "item*", "integer")]
    public static Dictionary<string, object?> DuplicateItems(
        [MultiInput] IEnumerable<ModelItem> items,
        [NodeRange(0, 1000000, SoftMin = 0, SoftMax = 1)] double tolerance = 0.001,
        [NodePanel("Advanced")] [NodeChoicesFromEnum(typeof(Units), "document")] string units = "document",
        Document? document = null)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No model items provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);
        var collection = NavisValues.ToItemCollection(items);
        var worldTolerance = tolerance * NavisValues.ResolveUnitsScale(doc, units);

        var test = new ClashTest
        {
            DisplayName = "CamelGraph Duplicate Audit",
            TestType = ClashTestType.Duplicate,
            Tolerance = worldTolerance,
        };
        test.SelectionA.Selection.CopyFrom(collection);
        test.SelectionB.Selection.CopyFrom(collection);
        test.SelectionA.SelfIntersect = true;
        test.SelectionB.SelfIntersect = true;

        var tests = clash.TestsData;
        tests.TestsAddCopy(test);

        // TestsAddCopy stores a copy at the end — run/remove need the stored instance.
        var stored = (ClashTest)tests.Tests[tests.Tests.Count - 1];
        var items1 = new List<ModelItem>();
        var items2 = new List<ModelItem>();
        try
        {
            tests.TestsRunTest(stored);
            foreach (var result in ClashHelpers.FlattenResults(stored))
            {
                items1.Add(result.Item1);
                items2.Add(result.Item2);
            }
        }
        finally
        {
            // The audit test is a throwaway — never leave it in the document.
            tests.TestsRemove(stored);
        }

        return new Dictionary<string, object?>
        {
            ["items1"] = items1,
            ["items2"] = items2,
            ["count"] = items1.Count,
        };
    }
}
