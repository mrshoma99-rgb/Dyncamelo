namespace Dyncamelo.Core.Editing;

/// <summary>
/// Socket/wire colours per <see cref="PortFamily"/>. Okabe–Ito-derived so the
/// families stay distinguishable under the common colour-vision deficiencies;
/// colour is never the only signal (shape encodes structure, tooltips name the
/// type). Values are semantic and fixed — they do not follow the UI palette.
/// </summary>
public static class PortKindPalette
{
    /// <summary>All families that have a colour.</summary>
    public static readonly PortFamily[] Families =
    {
        PortFamily.Any, PortFamily.Number, PortFamily.Integer, PortFamily.Boolean, PortFamily.Text,
        PortFamily.DateTime, PortFamily.Colour, PortFamily.Geometry, PortFamily.Item, PortFamily.Selection,
        PortFamily.Viewpoint, PortFamily.Clash, PortFamily.Document, PortFamily.Data, PortFamily.File,
        PortFamily.Action,
    };

    /// <summary>The sRGB colour of a family as #RRGGBB.</summary>
    public static string Hex(PortFamily family)
    {
        switch (family)
        {
            case PortFamily.Number: return "#56B4E9";
            case PortFamily.Integer: return "#2E82D8";
            case PortFamily.Boolean: return "#D55E00";
            case PortFamily.Text: return "#F0E442";
            case PortFamily.DateTime: return "#CC79A7";
            case PortFamily.Colour: return "#E8E8E8";
            case PortFamily.Geometry: return "#009E73";
            case PortFamily.Item: return "#E69F00";
            case PortFamily.Selection: return "#9C7229";
            case PortFamily.Viewpoint: return "#7B68EE";
            case PortFamily.Clash: return "#E0503F";
            case PortFamily.Document: return "#6B7785";
            case PortFamily.Data: return "#8FBC8F";
            case PortFamily.File: return "#A9865B";
            case PortFamily.Action: return "#F47AA5";
            default: return "#B4BBC4";
        }
    }

    /// <summary>
    /// A single character that names the family, drawn inside sockets for the colour-blind aid
    /// (so colour is never the only way to tell number from text from geometry).
    /// </summary>
    public static string Glyph(PortFamily family)
    {
        switch (family)
        {
            case PortFamily.Number: return "N";
            case PortFamily.Integer: return "I";
            case PortFamily.Boolean: return "B";
            case PortFamily.Text: return "T";
            case PortFamily.DateTime: return "D";
            case PortFamily.Colour: return "C";
            case PortFamily.Geometry: return "G";
            case PortFamily.Item: return "E";
            case PortFamily.Selection: return "S";
            case PortFamily.Viewpoint: return "V";
            case PortFamily.Clash: return "X";
            case PortFamily.Document: return "F";
            case PortFamily.Data: return "{";
            case PortFamily.File: return "P";
            case PortFamily.Action: return "A";
            default: return string.Empty;
        }
    }
}
