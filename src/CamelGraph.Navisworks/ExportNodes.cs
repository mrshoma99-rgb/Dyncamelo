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
using CamelGraph.Core.Execution;
using CamelGraph.Core.Files;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks;

/// <summary>Nodes that export model data to files (quantity take-off workflows).</summary>
[NodeCategory("Navisworks.Export")]
public static class ExportNodes
{
    /// <summary>Exports item properties to a CSV file.</summary>
    /// <param name="items">The model items to export (one row each).</param>
    /// <param name="filePath">Destination .csv path; the directory is created when missing. A relative path is next to the graph file.</param>
    /// <param name="categoryName">Property category to export; null exports every category as "Category.Property" columns.</param>
    /// <param name="propertyNames">Property names to export from the category; null exports all found.</param>
    /// <returns>The written file path.</returns>
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeName("Export.ToCsv")]
    [NodeDescription(
        "Writes one CSV row per model item with a Name column plus property columns, straight from the model: a quick quantity take-off file. " +
        "For anything you want to shape first (sort, filter, group, add columns, dates as dates, an Excel workbook) use Properties.ToTable " +
        "and the Table nodes, then Table.ToCsvFile or Table.ToExcelFile: they share the CSV and Excel options of the rest of the library.")]
    [NodeSearchTags("export", "csv", "qto", "takeoff", "report", "excel", "table", "properties")]
    [return: NodeName("filePath")]
    public static string ToCsv(
        [MultiInput] IEnumerable<ModelItem> items,
        [NodePath(NodePathMode.Save, Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*")] string filePath,
        [NodeTabChoice("items")] string? categoryName = null,
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

        filePath = PathResolver.Resolve(filePath);

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

    /// <summary>Writes a clash report; the file type follows the extension (.csv or .html).</summary>
    /// <param name="filePath">Destination .csv or .html (.htm) path; the directory is created when missing. A relative path is next to the graph file.</param>
    /// <param name="tests">The clash tests to report. Leave unwired for every test in the document; an empty list reports no test.</param>
    /// <param name="includeImages">HTML only: true embeds a snapshot per result (larger file, needs the Navisworks viewport).</param>
    /// <param name="imageWidth">HTML only: snapshot width in pixels.</param>
    /// <param name="imageHeight">HTML only: snapshot height in pixels.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written file path and the number of result rows.</returns>
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeName("Export.ClashReport")]
    [NodeDescription(
        "One-node clash report. A .csv path writes one row per result (test, group, result, status, distance, assignee, description, both item " +
        "paths and GUIDs, the clash point; Excel-ready). A .html path writes a self-contained page, one section per test, optionally with a " +
        "snapshot of every result (Advanced: includeImages, imageWidth, imageHeight), shareable as one file. Unwired tests reports every test " +
        "in the document; an empty list reports none, so a filter that finds nothing gives an empty report instead of a report on everything.")]
    [NodeSearchTags("export", "clash", "report", "csv", "html", "excel", "triage", "snapshot", "share", "clashreportcsv", "clashreporthtml")]
    [MultiReturn("filePath", "rowCount")]
    [PortKinds("file", "integer")]
    public static Dictionary<string, object?> ClashReport(
        [NodePath(NodePathMode.Save, Filter = "Clash report (*.csv;*.html)|*.csv;*.html|All files (*.*)|*.*")] string filePath,
        IEnumerable<ClashTest>? tests = null,
        [NodePanel("Advanced")] bool includeImages = false,
        [NodePanel("Advanced")] [NodeRange(16, 4096, SoftMin = 160, SoftMax = 1280, Unit = "px")] int imageWidth = 320,
        [NodePanel("Advanced")] [NodeRange(16, 4096, SoftMin = 160, SoftMax = 1280, Unit = "px")] int imageHeight = 240,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        var extension = (Path.GetExtension(PathResolver.Clean(filePath)) ?? string.Empty).ToLowerInvariant();
        switch (extension)
        {
            case ".csv":
                return WriteClashCsv(filePath, tests, document);
            case ".html":
            case ".htm":
                return WriteClashHtml(filePath, tests, includeImages, imageWidth, imageHeight, document);
            default:
                throw new ArgumentException(
                    "'" + filePath + "' must end in .csv (a table) or .html (a report page): the file type follows the extension.",
                    nameof(filePath));
        }
    }

    /// <summary>Exports clash results to a CSV file.</summary>
    /// <param name="filePath">Destination .csv path; the directory is created when missing.</param>
    /// <param name="tests">The clash tests to report (null reports every test in the document).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written file path and the number of result rows.</returns>
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeName("Export.ClashReportCsv")]
    [NodeDeprecated("Export.ClashReport")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeDescription("Writes the clash results of the tests to a CSV file (Excel-ready).")]
    [NodeSearchTags("export", "clash", "report", "csv", "excel", "triage")]
    [MultiReturn("filePath", "rowCount")]
    [PortKinds("file", "integer")]
    public static Dictionary<string, object?> ClashReportCsv(
        string filePath,
        IEnumerable<ClashTest>? tests = null,
        Document? document = null)
    {
        return WriteClashCsv(filePath, tests, document);
    }

    /// <summary>Exports clash results to a self-contained HTML report.</summary>
    /// <param name="filePath">Destination .html path; the directory is created when missing.</param>
    /// <param name="tests">The clash tests to report (null reports every test in the document).</param>
    /// <param name="includeImages">True to embed a snapshot per result (larger file, needs the Navisworks viewport).</param>
    /// <param name="imageWidth">Snapshot width in pixels.</param>
    /// <param name="imageHeight">Snapshot height in pixels.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written file path and the number of result rows.</returns>
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeName("Export.ClashReportHtml")]
    [NodeDeprecated("Export.ClashReport")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeDescription("Writes the clash results of the tests to a self-contained HTML report, optionally with embedded snapshots.")]
    [NodeSearchTags("export", "clash", "report", "html", "snapshot", "share")]
    [MultiReturn("filePath", "rowCount")]
    [PortKinds("file", "integer")]
    public static Dictionary<string, object?> ClashReportHtml(
        string filePath,
        IEnumerable<ClashTest>? tests = null,
        bool includeImages = false,
        [NodeRange(16, 4096, SoftMin = 160, SoftMax = 1280, Unit = "px")] int imageWidth = 320,
        [NodeRange(16, 4096, SoftMin = 160, SoftMax = 1280, Unit = "px")] int imageHeight = 240,
        Document? document = null)
    {
        return WriteClashHtml(filePath, tests, includeImages, imageWidth, imageHeight, document);
    }

    private static Dictionary<string, object?> WriteClashCsv(string filePath, IEnumerable<ClashTest>? tests, Document? document)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        filePath = PathResolver.Resolve(filePath);
        var doc = NavisworksContext.ResolveDocument(document);
        var rows = CollectClashRows(doc, tests);

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", ClashReportColumns.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        NavisValues.EnsureDirectory(filePath);
        File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return new Dictionary<string, object?>
        {
            ["filePath"] = filePath,
            ["rowCount"] = rows.Count,
        };
    }

    private static Dictionary<string, object?> WriteClashHtml(
        string filePath,
        IEnumerable<ClashTest>? tests,
        bool includeImages,
        int imageWidth,
        int imageHeight,
        Document? document)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        if (includeImages && (imageWidth <= 0 || imageHeight <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image width and height must be positive.");
        }

        filePath = PathResolver.Resolve(filePath);
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
                html.Append("<td>" + Html(AssigneeText(result)) + "</td>");
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

        NavisValues.EnsureDirectory(filePath);
        File.WriteAllText(filePath, html.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return new Dictionary<string, object?>
        {
            ["filePath"] = filePath,
            ["rowCount"] = rowCount,
        };
    }

    /// <summary>Renders the current view to an image file.</summary>
    /// <param name="filePath">Destination .png, .jpg or .bmp path; the directory is created when missing. A relative path is next to the graph file. With a viewpoint, {name} in the path becomes the viewpoint's name.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="viewpoint">Optional: the viewpoint to show first (a saved viewpoint, its name or folder path, or a camera from Camera.Save). A list takes one picture per viewpoint. Empty takes the picture of the view as it is.</param>
    /// <param name="after">Anything at all, only to run this node after the node it comes from: the picture is of the view at the moment this node runs, and the engine does not order nodes that share no wire.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written file path.</returns>
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeName("Export.ViewpointImage")]
    [NodeAliases("CamelGraph.Navisworks.ExportNodes.ViewpointImage@string,int,int,Autodesk.Navisworks.Api.Document")]
    [NodeDescription(
        "Renders a view to a .png/.jpg/.bmp file via the Navisworks image exporter. Without 'viewpoint' it is the view as it is when the node " +
        "runs: wire what sets the view into 'after'. Wire saved viewpoints into 'viewpoint' and the node shows each one itself and takes its " +
        "picture; put {name} in the file path (\"C:\\Pictures\\{name}.png\") for one file per viewpoint. That is the batch for a folder: " +
        "Viewpoints.InFolder into 'viewpoint'. (SavedViewpoint.Apply followed by this node over a list does not work: every Apply runs " +
        "first, then every picture shows the last view.) The view is left on the last viewpoint shown; Camera.Save and Camera.Restore put it back.")]
    [NodeSearchTags("export", "image", "screenshot", "render", "viewpoint", "png", "picture", "batch")]
    [return: NodeName("filePath")]
    public static string ViewpointImage(
        [NodePath(NodePathMode.Save, Filter = "Pictures (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp")] string filePath,
        [NodeRange(16, 8192, SoftMin = 320, SoftMax = 3840, Unit = "px")] int width = 1920,
        [NodeRange(16, 8192, SoftMin = 320, SoftMax = 3840, Unit = "px")] int height = 1080,
        [ScalarInput] object? viewpoint = null,
        object? after = null,
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

        filePath = PathResolver.Resolve(filePath);
        if (viewpoint == null && FileNameTemplate.HasNameToken(filePath))
        {
            throw new ArgumentException(
                "The file path holds {name} but no viewpoint is wired, so there is no name to put in. Wire the viewpoints into 'viewpoint', or write the file name out.",
                nameof(filePath));
        }

        if (viewpoint != null)
        {
            var view = ResolveViewpointToShow(NavisworksContext.ResolveDocument(document), viewpoint, out var viewName);
            filePath = FileNameTemplate.Apply(filePath, viewName);
            ShowViewpoint(NavisworksContext.ResolveDocument(document), view);
        }

        var formatCode = ImageFormatCode(filePath);

        // The COM exporter below is application-global and always renders the
        // ACTIVE document's viewport. Ensure a document is open, and refuse an
        // explicitly wired document that is not the active one — otherwise the
        // node would silently write an image of the wrong document.
        var doc = NavisworksContext.ResolveDocument(document);
        if (document != null)
        {
            var active = NavisworksContext.ResolveDocument(null);
            if (!ReferenceEquals(doc, active) && !doc.Equals(active))
            {
                throw new ArgumentException(
                    "Export.ViewpointImage can only render the active document's viewport; " +
                    "the wired 'document' is not the active document.", nameof(document));
            }
        }

        NavisValues.EnsureDirectory(filePath);

        var state = ComApiBridge.State;
        InwOaPropertyVec? options = null;
        InwOaPropertyColl? properties = null;
        try
        {
            options = state.GetIOPluginOptions("lcodpimage");
            properties = options.Properties();
            foreach (InwOaProperty option in properties)
            {
                try
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
                finally
                {
                    ComBridge.Release(option);
                }
            }

            var status = state.DriveIOPlugin("lcodpimage", Path.GetFullPath(filePath), options);
            if (status != nwEExportStatus.eExport_OK)
            {
                throw new InvalidOperationException(
                    "Navisworks image export failed with status '" + status +
                    "'. Check that the path is writable and a model is visible in the viewport.");
            }
        }
        finally
        {
            ComBridge.Release(properties, options);
        }

        return filePath;
    }

    /// <summary>Publishes the document as an .nwd file.</summary>
    /// <param name="filePath">Destination .nwd path; the directory is created when missing.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written file path.</returns>
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeName("Export.NWD")]
    [NodeDeprecated("Document.Save")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeDescription("Saves the document as an .nwd file; the same call as Document.Save with an .nwd path.")]
    [NodeSearchTags("export", "nwd", "publish", "save", "snapshot")]
    [return: NodeName("filePath")]
    public static string Nwd(string filePath, Document? document = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        if (!string.Equals(Path.GetExtension(PathResolver.Clean(filePath)), ".nwd", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("'" + filePath + "' must end in .nwd.", nameof(filePath));
        }

        return DocumentNodes.Save(filePath, document);
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
                    AssigneeText(result),
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

    // ClashResult.AssignedTo is a string through Navisworks 2025 but an Assignee
    // object in 2026; normalise to display text for reports on every year.
    private static string AssigneeText(ClashResult result)
    {
#if NAV2026
        return result.AssignedTo?.ToString() ?? string.Empty;
#else
        return result.AssignedTo ?? string.Empty;
#endif
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

    /// <summary>The viewpoint a picture is to show: a saved viewpoint (by itself, name or folder path) or a raw camera.</summary>
    private static object ResolveViewpointToShow(Document doc, object viewpoint, out string name)
    {
        switch (viewpoint)
        {
            case SavedViewpoint saved:
                name = saved.DisplayName ?? string.Empty;
                return saved;
            case Viewpoint camera:
                name = string.Empty;
                return camera;
            case string text when text.Trim().Length > 0:
                var found = SavedItemTreeHelpers.FindByNameOrPath<SavedViewpoint>(doc.SavedViewpoints.RootItem, text.Trim(), "saved viewpoint")
                    ?? throw new InvalidOperationException("No saved viewpoint named '" + text.Trim() + "' exists in the document.");
                name = found.DisplayName ?? string.Empty;
                return found;
            default:
                throw new ArgumentException(
                    "Cannot take a picture of a " + viewpoint.GetType().Name +
                    ". Wire a saved viewpoint, its name or folder path, or the camera from Camera.Save.", nameof(viewpoint));
        }
    }

    private static void ShowViewpoint(Document doc, object view)
    {
        if (view is SavedViewpoint saved)
        {
            // The same call SavedViewpoint.Apply makes: the camera and any saved overrides.
            doc.SavedViewpoints.CurrentSavedViewpoint = saved;
        }
        else
        {
            doc.CurrentViewpoint.CopyFrom((Viewpoint)view);
        }
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
