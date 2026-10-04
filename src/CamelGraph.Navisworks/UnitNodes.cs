using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Loader;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks;

/// <summary>Nodes for document display units and unit conversion.</summary>
[NodeCategory("Navisworks.Units")]
public static class UnitNodes
{
    /// <summary>The display units of a document.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The unit name, e.g. "Meters" or "Feet".</returns>
    [NodeName("Units.Current")]
    [LiveState]
    [NodeDescription("The display units of a document (all API lengths, areas and volumes use them), read again on every run. The same text is the units output of Document.Info.")]
    [NodeSearchTags("units", "current", "document", "meters", "feet")]
    [return: NodeName("units")]
    public static string Current(Document? document = null)
    {
        return NavisworksContext.ResolveDocument(document).Units.ToString();
    }

    /// <summary>The multiplier that converts between two units of length, area or volume.</summary>
    /// <param name="fromUnits">Source units (e.g. "Feet"); unit names as in Units.Current.</param>
    /// <param name="toUnits">Target units (e.g. "Millimeters").</param>
    /// <param name="dimension">Length (default), Area or Volume: an area factor is the length factor squared, a volume factor is it cubed.</param>
    /// <returns>The scale factor (multiply a source value by it).</returns>
    [NodeName("Units.ScaleFactor")]
    [NodeAliases("CamelGraph.Navisworks.UnitNodes.ScaleFactor@Autodesk.Navisworks.Api.Units,Autodesk.Navisworks.Api.Units")]
    [NodeDescription("The multiplier that converts a length, an area or a volume from one unit to another. With dimension Length (the default) it is the factor for lengths; Area gives that factor squared and Volume cubed, which is what quantities such as the areas and volumes from Model.Statistics or takeoffs need (square feet to square meters is the length factor squared).")]
    [NodeSearchTags("units", "scale", "factor", "conversion", "area", "volume", "square", "cubic")]
    [return: NodeName("factor")]
    public static double ScaleFactor(
        Units fromUnits,
        Units toUnits,
        [NodeChoices("Length", "Area", "Volume")] string dimension = "Length")
    {
        return UnitDimension.Factor(UnitConversion.ScaleFactor(fromUnits, toUnits), dimension);
    }

    /// <summary>Converts a length, area or volume value between units.</summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="fromUnits">Source units (e.g. "Feet").</param>
    /// <param name="toUnits">Target units (e.g. "Millimeters").</param>
    /// <param name="dimension">Length (default), Area or Volume: what the value measures. An area is converted with the length factor squared, a volume with it cubed.</param>
    /// <returns>The converted value.</returns>
    [NodeName("Units.Convert")]
    [NodeAliases("CamelGraph.Navisworks.UnitNodes.Convert@double,Autodesk.Navisworks.Api.Units,Autodesk.Navisworks.Api.Units")]
    [NodeDescription("Converts a length, an area or a volume from one unit to another (e.g. Feet to Millimeters). Set dimension to Area for square units (square feet to square meters) or Volume for cubic units; the default, Length, converts a length as before.")]
    [NodeSearchTags("units", "convert", "length", "conversion", "area", "volume", "square", "cubic")]
    [return: NodeName("value")]
    public static double Convert(
        double value,
        Units fromUnits,
        Units toUnits,
        [NodeChoices("Length", "Area", "Volume")] string dimension = "Length")
    {
        return value * UnitDimension.Factor(UnitConversion.ScaleFactor(fromUnits, toUnits), dimension);
    }

    /// <summary>Every unit name the conversion nodes accept.</summary>
    /// <returns>The valid unit names (e.g. "Meters", "Feet", "Millimeters").</returns>
    [NodeName("Units.All")]
    [NodeDescription("Every unit name accepted by Units.Convert and Units.ScaleFactor.")]
    [NodeSearchTags("units", "all", "names", "list", "valid")]
    [return: NodeName("names")]
    public static List<string> All()
    {
        return new List<string>(Enum.GetNames(typeof(Units)));
    }
}
