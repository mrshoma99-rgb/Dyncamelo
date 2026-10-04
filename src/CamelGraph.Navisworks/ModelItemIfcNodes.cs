using System;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes;

namespace CamelGraph.Navisworks;

/// <summary>Model-item nodes for the IFC identity of an element.</summary>
[NodeCategory("Navisworks.ModelItem")]
public static class ModelItemIfcNodes
{
    /// <summary>The IFC GlobalId of a model item.</summary>
    /// <param name="item">The model item.</param>
    /// <returns>The 22-character IFC GlobalId, or null when the item has none.</returns>
    [NodeName("ModelItem.IfcGuid")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("ifcGuid")]
    [NodeDescription("The 22-character IFC GlobalId of an item: the value of its GlobalId / IfcGUID / IFC GUID / Guid property when it has one (as the Navisworks IFC import creates it), otherwise its InstanceGuid encoded the same way; null when it has neither. Wire a list of items to get one id per item.")]
    [NodeSearchTags("ifc", "guid", "globalid", "identity", "id", "bim", "bcf", "item")]
    public static string? IfcGuid(ModelItem item)
    {
        var modelItem = NavisValues.RequireItem(item);

        var fromProperties = ModelDataReader.FindIfcGlobalId(modelItem);
        if (fromProperties != null)
        {
            return fromProperties;
        }

        // No property carries it: fall back to the instance GUID in IFC form, which is what the BCF nodes
        // already do for component references (documented in the node description).
        var guid = modelItem.InstanceGuid;
        return guid != Guid.Empty ? IfcGuidCodec.Encode(guid) : null;
    }
}
