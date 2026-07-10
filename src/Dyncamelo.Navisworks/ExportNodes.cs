using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;
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

    /// <summary>Exports clash results to a CSV file.</summary>
    /// <param name="filePath">Destination .csv path; the directory is created when missing.</param>
    /// <param name="tests">The clash tests to report (null reports every test in the document).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written file path and the number of result rows.</returns>
    [NodeName("Export.ClashReportCsv")]
    [NodeDescription("One-node clash report: writes test, group, result, status, distance, assignee, both item paths and GUIDs, and the clash point to a CSV file (Excel-ready).")]
    [NodeSearchTags("export", "clash", "report", "csv", "excel", "triage")]
    [MultiReturn("filePath", "rowCount")]
    public static Dictionary<string, object?> ClashReportCsv(
        string filePath,
        IEnumerable<ClashTest>? tests = null,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var rows = CollectClashRows(doc, tests);

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", ClashReportColumns.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        EnsureDirectory(filePath);
        File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return new Dictionary<string, object?>
        {
            ["filePath"] = filePath,
            ["rowCount"] = rows.Count,
        };
    }

    /// <summary>Exports clash results to a self-contained HTML report.</summary>
    /// <param name="filePath">Destination .html path; the directory is created when missing.</param>
    /// <param name="tests">The clash tests to report (null reports every test in the document).</param>
    /// <param name="includeImages">True to embed a snapshot per result (larger file, needs the Navisworks viewport).</param>
    /// <param name="imageWidth">Snapshot width in pixels.</param>
    /// <param name="imageHeight">Snapshot height in pixels.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written file path and the number of result rows.</returns>
    [NodeName("Export.ClashReportHtml")]
    [NodeDescription("Self-contained HTML clash report — one section per test, one row per result, optionally with embedded snapshots. Shareable as a single file.")]
    [NodeSearchTags("export", "clash", "report", "html", "snapshot", "share")]
    [MultiReturn("filePath", "rowCount")]
    public static Dictionary<string, object?> ClashReportHtml(
        string filePath,
        IEnumerable<ClashTest>? tests = null,
        bool includeImages = false,
        int imageWidth = 320,
        int imageHeight = 240,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        if (includeImages && (imageWidth <= 0 || imageHeight <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image width and height must be positive.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var testList = ResolveTests(doc, tests);
        var clash = includeImages ? ClashHelpers.RequireClash(doc) : null;

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html><head><meta charset=\"utf-8\"><title>Clash Report</title><style>");
        html.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}");
        html.AppendLine("h1{font-size:20px} h2{font-size:16px;margin-top:28px}");
        html.AppendLine("table{border-collapse:collapse;width:100%;font-size:12px}");
        html.AppendLine("th,td{border:1px solid #ccc;padding:4px 8px;text-align:left;vertical-align:top}");
        html.AppendLine("th{background:#f0f0f0} img{display:block}");
        html.AppendLine("</style></head><body>");
        html.AppendLine("<h1>Clash Report</h1>");
        html.AppendLine("<p>Generated " + Html(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)) +
                        " — " + Html(doc.Title ?? string.Empty) + "</p>");

        var rowCount = 0;
        foreach (var test in testList)
        {
            var results = new List<ClashResult>();
            var groupNames = new List<string>();
            ClashHelpers.FlattenResultsWithGroups(test, results, groupNames);

            html.AppendLine("<h2>" + Html(test.DisplayName ?? string.Empty) +
                            " <small>(" + results.Count + " results, last run " +
                            Html(test.LastRun?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "never") +
                            ")</small></h2>");
            html.AppendLine("<table><tr>" +
                            (includeImages ? "<th>Snapshot</th>" : string.Empty) +
                            "<th>Group</th><th>Result</th><th>Status</th><th>Distance</th><th>Assigned To</th>" +
                            "<th>Description</th><th>Item 1</th><th>Item 2</th><th>Clash Point</th></tr>");

            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                html.Append("<tr>");
                if (includeImages)
                {
                    html.Append("<td><img width=\"" + imageWidth + "\" height=\"" + imageHeight +
                                "\" src=\"data:image/png;base64," +
                                SnapshotBase64(clash!, result, imageWidth, imageHeight) + "\"/></td>");
                }

                var center = result.Center;
                html.Append("<td>" + Html(groupNames[i]) + "</td>");
                html.Append("<td>" + Html(result.DisplayName ?? string.Empty) + "</td>");
                html.Append("<td>" + Html(result.Status.ToString()) + "</td>");
                html.Append("<td>" + Html(FormatNumber(result.Distance)) + "</td>");
                html.Append("<td>" + Html(result.AssignedTo ?? string.Empty) + "</td>");
                html.Append("<td>" + Html(result.Description ?? string.Empty) + "</td>");
                html.Append("<td>" + Html(NavisValues.ItemPath(result.Item1)) + "</td>");
                html.Append("<td>" + Html(NavisValues.ItemPath(result.Item2)) + "</td>");
                html.Append("<td>" + Html(FormatNumber(center.X) + ", " + FormatNumber(center.Y) + ", " + FormatNumber(center.Z)) + "</td>");
                html.AppendLine("</tr>");
                rowCount++;
            }

            html.AppendLine("</table>");
        }

        html.AppendLine("</body></html>");

        EnsureDirectory(filePath);
        File.WriteAllText(filePath, html.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return new Dictionary<string, object?>
        {
            ["filePath"] = filePath,
            ["rowCount"] = rowCount,
        };
    }

    /// <summary>Renders the current view to an image file.</summary>
    /// <param name="filePath">Destination .png, .jpg or .bmp path; the directory is created when missing.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written file path. After SavedViewpoint.Apply in a lacing loop this is a batch screenshot factory.</returns>
    [NodeName("Export.ViewpointImage")]
    [NodeDescription("Renders the current view to a .png/.jpg/.bmp file via the Navisworks image exporter.")]
    [NodeSearchTags("export", "image", "screenshot", "render", "viewpoint", "png")]
    [return: NodeName("filePath")]
    public static string ViewpointImage(
        string filePath,
        int width = 1920,
        int height = 1080,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image width and height must be positive.");
        }

        var formatCode = ImageFormatCode(filePath);

        // Ensure a document is open before driving the app-global COM exporter.
        NavisworksContext.ResolveDocument(document);
        EnsureDirectory(filePath);

        var state = ComApiBridge.State;
        var options = state.GetIOPluginOptions("lcodpimage");
        foreach (InwOaProperty option in options.Properties())
        {
            switch (option.name)
            {
                case "export.image.format":
                    option.value = formatCode;
                    break;
                case "export.image.width":
                    option.value = width;
                    break;
                case "export.image.height":
                    option.value = height;
                    break;
            }
        }

        var status = state.DriveIOPlugin("lcodpimage", Path.GetFullPath(filePath), options);
        if (status != nwEExportStatus.eExport_OK)
        {
            throw new InvalidOperationException(
                "Navisworks image export failed with status '" + status +
                "'. Check that the path is writable and a model is visible in the viewport.");
        }

        return filePath;
    }

    /// <summary>Publishes the document as an .nwd file.</summary>
    /// <param name="filePath">Destination .nwd path; the directory is created when missing.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written file path.</returns>
    [NodeName("Export.NWD")]
    [NodeDescription("Saves the document as a published .nwd snapshot (appearance overrides baked in).")]
    [NodeSearchTags("export", "nwd", "publish", "save", "snapshot")]
    [return: NodeName("filePath")]
    public static string Nwd(string filePath, Document? document = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        if (!string.Equals(Path.GetExtension(filePath), ".nwd", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("'" + filePath + "' must end in .nwd.", nameof(filePath));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        EnsureDirectory(filePath);
        doc.SaveFile(filePath);
        return filePath;
    }

    private static readonly string[] ClashReportColumns =
    {
        "Test", "Group", "Result", "Status", "Distance", "Assigned To", "Description", "Created",
        "Item 1", "Item 1 GUID", "Item 2", "Item 2 GUID", "Center X", "Center Y", "Center Z",
    };

    private static List<string[]> CollectClashRows(Document doc, IEnumerable<ClashTest>? tests)
    {
        var rows = new List<string[]>();
        foreach (var test in ResolveTests(doc, tests))
        {
            var results = new List<ClashResult>();
            var groupNames = new List<string>();
            ClashHelpers.FlattenResultsWithGroups(test, results, groupNames);
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                var center = result.Center;
                rows.Add(new[]
                {
                    test.DisplayName ?? string.Empty,
                    groupNames[i],
                    result.DisplayName ?? string.Empty,
                    result.Status.ToString(),
                    FormatNumber(result.Distance),
                    result.AssignedTo ?? string.Empty,
                    result.Description ?? string.Empty,
                    result.CreatedTime?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty,
                    NavisValues.ItemPath(result.Item1),
                    GuidText(result.Item1),
                    NavisValues.ItemPath(result.Item2),
                    GuidText(result.Item2),
                    FormatNumber(center.X),
                    FormatNumber(center.Y),
                    FormatNumber(center.Z),
                });
            }
        }

        return rows;
    }

    private static List<ClashTest> ResolveTests(Document doc, IEnumerable<ClashTest>? tests)
    {
        if (tests == null)
        {
            return NavisValues.FlattenSavedItems<ClashTest>(ClashHelpers.RequireClash(doc).TestsData.Tests);
        }

        var list = new List<ClashTest>();
        foreach (var test in tests)
        {
            if (test != null)
            {
                list.Add(test);
            }
        }

        return list;
    }

    private static string SnapshotBase64(DocumentClash clash, ClashResult result, int width, int height)
    {
        using (var bitmap = clash.TestsData.TestsImageForResult(
            result, ImageGenerationStyle.ScenePlusOverlay, width, height))
        using (var stream = new MemoryStream())
        {
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToBase64String(stream.ToArray());
        }
    }

    private static string GuidText(ModelItem? item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        var guid = item.InstanceGuid;
        return guid == Guid.Empty ? string.Empty : guid.ToString();
    }

    private static string ImageFormatCode(string filePath)
    {
        var extension = (Path.GetExtension(filePath) ?? string.Empty).ToLowerInvariant();
        switch (extension)
        {
            case ".png": return "lcodpexpng";
            case ".jpg":
            case ".jpeg": return "lcodpexjpg";
            case ".bmp": return "lcodpexbmp";
            default:
                throw new ArgumentException("'" + filePath + "' must end in .png, .jpg or .bmp.", nameof(filePath));
        }
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Html(string text)
    {
        return System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
    }

    private static void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
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
