using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;
using CamelGraph.Nodes.Internal;

namespace CamelGraph.Nodes;

/// <summary>
/// Builds a readable report from tables and text: one self-contained HTML page (works in a browser, in an e-mail and printed)
/// or Markdown. The node returns the text; write it with Text.WriteToFile.
/// </summary>
[NodeCategory("Report")]
public static class ReportNodes
{
    private const string Style =
        "body{font:14px/1.45 -apple-system,Segoe UI,Roboto,sans-serif;max-width:1100px;margin:2em auto;padding:0 1em;color:#1d2230;background:#fff}" +
        "h1{font-size:1.6em;margin-bottom:.2em}h2{font-size:1.25em;margin-top:1.8em;border-bottom:1px solid #d5d9e2;padding-bottom:.2em}h3{font-size:1.05em;margin-top:1.4em}" +
        ".meta{color:#667;margin:0 0 1.2em}table{border-collapse:collapse;margin:.6em 0 1.2em;font-size:13px}" +
        "th,td{border:1px solid #d5d9e2;padding:.3em .7em;text-align:left;vertical-align:top}th{background:#f1f3f8}td.num{text-align:right;font-variant-numeric:tabular-nums}" +
        "tbody tr:nth-child(even){background:#fafbfd}pre{background:#f1f3f8;padding:.6em .9em;overflow:auto}" +
        "@media (prefers-color-scheme:dark){body{color:#e4e7ee;background:#15181f}h2{border-color:#343a48}.meta{color:#99a}th,td{border-color:#343a48}th{background:#222733}tbody tr:nth-child(even){background:#1a1e27}pre{background:#222733}}";

    /// <summary>Builds an HTML report.</summary>
    /// <param name="title">The report title.</param>
    /// <param name="sections">What goes in the report, top to bottom: tables, text paragraphs ("# Heading" and "## Subheading" make headings), lists and other values (shown as text). Wire several into the one socket.</param>
    /// <param name="subtitle">A line under the title, e.g. the model name and date.</param>
    /// <returns>A complete HTML page.</returns>
    [NodeName("Report.Html")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Create)]
    [return: NodeName("html")]
    [NodeDescription("Builds a self-contained HTML report from tables and text (\"# Heading\" makes a heading) — light and dark, printable, ready for Text.WriteToFile or an e-mail.")]
    [NodeSearchTags("report", "html", "page", "weekly", "summary", "export", "publish", "document")]
    public static string Html(string title, [MultiInput] IList<object?> sections, string subtitle = "")
    {
        if (sections == null)
        {
            throw new ArgumentNullException(nameof(sections), "Report.Html requires sections. Wire tables or text into the 'sections' input.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append("<title>").Append(TableText.Escape(title ?? string.Empty)).AppendLine("</title>");
        builder.Append("<style>").Append(Style).AppendLine("</style></head><body>");
        builder.Append("<h1>").Append(TableText.Escape(title ?? string.Empty)).AppendLine("</h1>");
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            builder.Append("<p class=\"meta\">").Append(TableText.Escape(subtitle)).AppendLine("</p>");
        }

        foreach (var section in sections)
        {
            switch (section)
            {
                case null:
                    break;
                case CamelGraphTable table:
                    builder.Append(TableText.Html(table));
                    break;
                case string text:
                    AppendText(builder, text);
                    break;
                case IDictionary _:
                    builder.Append("<pre>").Append(TableText.Escape(Pretty(section))).AppendLine("</pre>");
                    break;
                case IList list when list.Cast<object?>().All(item => item is CamelGraphTable || item is string):
                    foreach (var item in list)
                    {
                        if (item is CamelGraphTable inner)
                        {
                            builder.Append(TableText.Html(inner));
                        }
                        else
                        {
                            AppendText(builder, (string)item!);
                        }
                    }

                    break;
                default:
                    builder.Append("<pre>").Append(TableText.Escape(Pretty(section))).AppendLine("</pre>");
                    break;
            }
        }

        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    /// <summary>Builds a Markdown report.</summary>
    /// <param name="title">The report title.</param>
    /// <param name="sections">What goes in the report, top to bottom: tables, text (a "# Heading" line stays a heading) and other values.</param>
    /// <param name="subtitle">A line under the title.</param>
    /// <returns>The Markdown text.</returns>
    [NodeName("Report.Markdown")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Create)]
    [return: NodeName("markdown")]
    [NodeDescription("Builds a Markdown report from tables and text — for Teams, issue trackers and wikis.")]
    [NodeSearchTags("report", "markdown", "md", "weekly", "summary", "wiki", "document")]
    public static string Markdown(string title, [MultiInput] IList<object?> sections, string subtitle = "")
    {
        if (sections == null)
        {
            throw new ArgumentNullException(nameof(sections), "Report.Markdown requires sections. Wire tables or text into the 'sections' input.");
        }

        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(title ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            builder.AppendLine().Append("_").Append(subtitle).AppendLine("_");
        }

        foreach (var section in sections)
        {
            if (section == null)
            {
                continue;
            }

            builder.AppendLine();
            if (section is CamelGraphTable table)
            {
                builder.Append(TableText.Markdown(table));
            }
            else if (section is string text)
            {
                builder.AppendLine(text);
            }
            else
            {
                builder.AppendLine("```").AppendLine(Pretty(section)).AppendLine("```");
            }
        }

        return builder.ToString();
    }

    private static void AppendText(StringBuilder builder, string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                builder.Append("<h3>").Append(TableText.Escape(line.Substring(3))).AppendLine("</h3>");
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                builder.Append("<h2>").Append(TableText.Escape(line.Substring(2))).AppendLine("</h2>");
            }
            else if (line.Trim().Length > 0)
            {
                builder.Append("<p>").Append(TableText.Escape(line)).AppendLine("</p>");
            }
        }
    }

    private static string Pretty(object value)
    {
        if (value is IDictionary dictionary)
        {
            return string.Join(Environment.NewLine, dictionary.Keys.Cast<object>().Select(k => TypeCoercion.FormatValue(k) + ": " + TypeCoercion.FormatValue(dictionary[k])));
        }

        if (value is IList list && !(value is string))
        {
            return string.Join(Environment.NewLine, list.Cast<object?>().Select(TypeCoercion.FormatValue));
        }

        return TypeCoercion.FormatValue(value);
    }
}
