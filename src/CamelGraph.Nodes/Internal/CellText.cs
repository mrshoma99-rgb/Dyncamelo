using CamelGraph.Core.Types;

namespace CamelGraph.Nodes;

/// <summary>How a spreadsheet or CSV cell reads as text.</summary>
internal static class CellText
{
    /// <summary>Empty for null, the text itself for a string, otherwise the value as CamelGraph shows it everywhere.</summary>
    /// <param name="cell">The cell value.</param>
    internal static string Format(object? cell)
    {
        if (cell == null)
        {
            return string.Empty;
        }

        if (cell is string text)
        {
            return text;
        }

        return TypeCoercion.FormatValue(cell);
    }
}
