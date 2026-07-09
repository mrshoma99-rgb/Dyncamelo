using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes;

/// <summary>
/// Color construction nodes. Colors are <see cref="DyncameloColor"/> values;
/// host layers (UI, Navisworks appearance overrides) convert them to their
/// native color types.
/// </summary>
[NodeCategory("Color")]
public static class ColorNodes
{
    /// <summary>
    /// Builds a color from alpha, red, green and blue channels. Channel values
    /// outside 0–255 are clamped.
    /// </summary>
    /// <param name="a">Alpha channel, 0–255 (default fully opaque).</param>
    /// <param name="r">Red channel, 0–255.</param>
    /// <param name="g">Green channel, 0–255.</param>
    /// <param name="b">Blue channel, 0–255.</param>
    /// <returns>The color.</returns>
    [NodeName("Color.ByARGB")]
    [return: NodeName("color")]
    [NodeDescription("Creates a color from alpha, red, green and blue values (0-255).")]
    [NodeSearchTags("rgb", "argb", "rgba")]
    public static DyncameloColor ByArgb(int a = 255, int r = 0, int g = 0, int b = 0)
    {
        return new DyncameloColor(a, r, g, b);
    }
}
