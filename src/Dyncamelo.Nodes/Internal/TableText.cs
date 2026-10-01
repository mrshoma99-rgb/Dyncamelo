using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace Dyncamelo.Nodes.Internal;

/// <summary>Renders a <see cref="DyncameloTable"/> as text: Markdown, delimited (CSV / TSV) or an HTML table.</summary>
internal static class TableText
{
    internal static string Markdown(DyncameloTable table)
    {
        if (table.ColumnCount == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append("| ").Append(string.Join(" | ", table.Headers.Select(MarkdownCell))).AppendLine(" |");
        builder.Append("|").Append(string.Concat(table.Headers.Select((_, i) => IsNumberColumn(table, i) ? " ---: |" : " --- |"))).AppendLine();
        foreach (var row in table.Rows)
        {
            builder.Append("| ").Append(string.Join(" | ", row.Select(c => MarkdownCell(DyncameloTable.CellText(c))))).AppendLine(" |");
        }

        return builder.ToString();
    }

    internal static string Delimited(DyncameloTable table, char delimiter)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(delimiter.ToString(), table.Headers.Select(h => Quote(h, delimiter))));
        foreach (var row in table.Rows)
        {
            builder.AppendLine(string.Join(delimiter.ToString(), row.Select(c => Quote(DyncameloTable.CellText(c), delimiter))));
        }

        return builder.ToString();
    }

    internal static string Html(DyncameloTable table)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<table>");
        builder.Append("<thead><tr>");
        foreach (var header in table.Headers)
        {
            builder.Append("<th>").Append(Escape(header)).Append("</th>");
        }

        builder.AppendLine("</tr></thead>");
        builder.AppendLine("<tbody>");
        var numeric = Enumerable.Range(0, table.ColumnCount).Select(i => IsNumberColumn(table, i)).ToArray();
        foreach (var row in table.Rows)
        {
            builder.Append("<tr>");
            for (int i = 0; i < row.Length; i++)
            {
                builder.Append(numeric[i] ? "<td class=\"num\">" : "<td>").Append(Escape(DyncameloTable.CellText(row[i]))).Append("</td>");
            }

            builder.AppendLine("</tr>");
        }

        builder.AppendLine("</tbody>");
        builder.AppendLine("</table>");
        return builder.ToString();
    }

    /// <summary>HTML-escapes text.</summary>
    internal static string Escape(string text) => WebUtility.HtmlEncode(text);

    // A column is numeric when it holds at least one number and nothing but numbers and blanks.
    private static bool IsNumberColumn(DyncameloTable table, int column)
    {
        var any = false;
        foreach (var row in table.Rows)
        {
            var cell = row[column];
            if (cell == null)
            {
                continue;
            }

            if (!ValueComparison.IsNumeric(cell))
            {
                return false;
            }

            any = true;
        }

        return any;
    }

    private static string MarkdownCell(string text) =>
        text.Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");

    private static string Quote(string text, char delimiter)
    {
        if (text.IndexOf(delimiter) < 0 && text.IndexOf('"') < 0 && text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0)
        {
            return text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
