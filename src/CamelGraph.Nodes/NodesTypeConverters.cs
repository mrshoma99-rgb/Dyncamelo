using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;

namespace CamelGraph.Nodes;

/// <summary>
/// Converters for CamelGraph's own value types. A colour typed into a node's
/// inline colour swatch is stored as a hex string ("#AARRGGBB"); this makes a
/// hex string acceptable wherever a <see cref="CamelGraphColor"/> is expected.
/// </summary>
[IsVisibleInLibrary(false)]
public static class NodesTypeConverters
{
    /// <summary>Registers the string → colour converter. Idempotent; runs once per process.</summary>
    [TypeConverterRegistration]
    public static void RegisterConverters()
    {
        TypeCoercion.RegisterConverter(
            typeof(string),
            typeof(CamelGraphColor),
            value => ColorNodes.FromHex((string)value));
    }
}
