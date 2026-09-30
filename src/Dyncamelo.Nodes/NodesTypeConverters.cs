using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Nodes;

/// <summary>
/// Converters for Dyncamelo's own value types. A colour typed into a node's
/// inline colour swatch is stored as a hex string ("#AARRGGBB"); this makes a
/// hex string acceptable wherever a <see cref="DyncameloColor"/> is expected.
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
            typeof(DyncameloColor),
            value => ColorNodes.FromHex((string)value));
    }
}
