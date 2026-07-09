using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>Nodes that export model data to files (quantity take-off workflows).</summary>
[NodeCategory("Navisworks.Export")]
public static class ExportNodes
{
    /// <summary>Exports item properties to a CSV file.</summary>
    /// <param name="items">The model items to export (one row each).</param>
    /// <param name="filePath">Destination .csv path; the directory is created when missing.</param>
    /// <param name="categoryName">Property category to export; null exports every category as "Category.Property" columns.</param>
    /// <param name="propertyNames">Property names to export from the category; null exports all found.</param>
    /// <returns>The written file path.</returns>
    [NodeName("Export.ToCsv")]
    [NodeDescription("Writes one CSV row per model item with a Name column plus property columns. Useful for quantity take-offs.")]
    [NodeSearchTags("export", "csv", "qto", "takeoff", "report", "excel")]
    [return: NodeName("filePath")]
    public static string ToCsv(
        IEnumerable<ModelItem> items,
        string filePath,
        string? categoryName = null,
        IEnumerable<string>? propertyNames = null)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No model items provided.");
        }

        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        var itemList = NavisValues.ToItemList(items);
        var requestedProperties = propertyNames?.Where(n => !string.IsNullOrEmpty(n)).ToList();

        // Pass 1: gather each item's values keyed by column name, collecting the
        // column set in first-seen order so the header is stable and complete.
        var columns = new List<string>();
        var columnSet = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<Dictionary<string, object?>>(itemList.Count);
        foreach (var item in itemList)
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var category in item.PropertyCategories)
            {
                if (categoryName != null &&
                    !string.Equals(category.DisplayName, categoryName, StringComparison.Ordinal) &&
                    !string.Equals(category.Name, categoryName, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var property in category.Properties)
                {
                    if (requestedProperties != null && !requestedProperties.Contains(property.DisplayName) &&
                        !requestedProperties.Contains(property.Name))
                    {
                        continue;
                    }

                    var column = categoryName != null
                        ? property.DisplayName
                        : category.DisplayName + "." + property.DisplayName;
                    if (columnSet.Add(column))
                    {
                        columns.Add(column);
                    }

                    if (!row.ContainsKey(column))
                    {
                        row[column] = NavisValues.ToClrObject(property.Value);
                    }
                }
            }

            rows.Add(row);
        }

        // Requested property order wins over discovery order.
        if (requestedProperties != null && categoryName != null)
        {
            columns = requestedProperties.Where(columnSet.Contains)
                .Concat(columns.Where(c => !requestedProperties.Contains(c)))
                .ToList();
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", new[] { "Name" }.Concat(columns).Select(EscapeCsv)));
        for (int i = 0; i < itemList.Count; i++)
        {
            var cells = new List<string> { EscapeCsv(ModelItemNodes.DisplayName(itemList[i])) };
            foreach (var column in columns)
            {
                rows[i].TryGetValue(column, out var value);
                cells.Add(EscapeCsv(FormatCell(value)));
            }

            builder.AppendLine(string.Join(",", cells));
        }

        File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return filePath;
    }

    private static string FormatCell(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return value.ToString() ?? string.Empty;
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
