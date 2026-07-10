using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;
using Dyncamelo.Nodes;
using NwColor = Autodesk.Navisworks.Api.Color;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Registers custom type converters so values produced by the general node
/// library flow into Navisworks ports and API calls: the plain
/// <see cref="DyncameloColor"/> (Color.ByARGB, Color Picker, ...) and
/// <see cref="System.Drawing.Color"/> both convert to
/// <c>Autodesk.Navisworks.Api.Color</c>. The registration hook is invoked by
/// <c>NodeRegistry.RegisterAssembly</c> when the host loads this node pack, and
/// the converters are then consulted by both connection compatibility checks
/// and runtime coercion in Dyncamelo.Core.
/// </summary>
[IsVisibleInLibrary(false)]
public static class NavisworksTypeConverters
{
    /// <summary>Registers the color converters. Idempotent; runs once per process.</summary>
    [TypeConverterRegistration]
    public static void RegisterConverters()
    {
        // Navisworks colors carry no alpha: transparency is a separate override
        // (Appearance.OverrideTransparency), so the alpha channel is dropped.
        TypeCoercion.RegisterConverter(
            typeof(DyncameloColor),
            typeof(NwColor),
            value =>
            {
                var color = (DyncameloColor)value;
                return NwColor.FromByteRGB(color.R, color.G, color.B);
            });

        TypeCoercion.RegisterConverter(
            typeof(System.Drawing.Color),
            typeof(NwColor),
            value =>
            {
                var color = (System.Drawing.Color)value;
                return NwColor.FromByteRGB(color.R, color.G, color.B);
            });
    }
}
