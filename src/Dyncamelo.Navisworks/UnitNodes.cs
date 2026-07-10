using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Navisworks;

/// <summary>Nodes for document display units and unit conversion.</summary>
[NodeCategory("Navisworks.Units")]
public static class UnitNodes
{
    /// <summary>The display units of a document.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The unit name, e.g. "Meters" or "Feet".</returns>
    [NodeName("Units.Current")]
    [NodeDescription("The display units of a document (all API lengths, areas and volumes use them).")]
    [NodeSearchTags("units", "current", "document", "meters", "feet")]
    [return: NodeName("units")]
    public static string Current(Document? document = null)
    {
        return NavisworksContext.ResolveDocument(document).Units.ToString();
    }

    /// <summary>The multiplier that converts between two length units.</summary>
    /// <param name="fromUnits">Source units (e.g. "Feet"); unit names as in Units.Current.</param>
    /// <param name="toUnits">Target units (e.g. "Millimeters").</param>
    /// <returns>The scale factor (multiply a source value by it).</returns>
    [NodeName("Units.ScaleFactor")]
    [NodeDescription("The multiplier that converts a length from one unit to another.")]
    [NodeSearchTags("units", "scale", "factor", "conversion")]
    [return: NodeName("factor")]
    public static double ScaleFactor(Units fromUnits, Units toUnits)
    {
        return UnitConversion.ScaleFactor(fromUnits, toUnits);
    }

    /// <summary>Converts a length value between units.</summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="fromUnits">Source units (e.g. "Feet").</param>
    /// <param name="toUnits">Target units (e.g. "Millimeters").</param>
    /// <returns>The converted value.</returns>
    [NodeName("Units.Convert")]
    [NodeDescription("Converts a length value from one unit to another (e.g. Feet to Millimeters).")]
    [NodeSearchTags("units", "convert", "length", "conversion")]
    [return: NodeName("value")]
    public static double Convert(double value, Units fromUnits, Units toUnits)
    {
        return value * UnitConversion.ScaleFactor(fromUnits, toUnits);
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
