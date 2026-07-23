using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>
/// EXPERIMENTAL redline markup on saved viewpoints — text, lines, arrows,
/// ellipses, revision clouds and a numbered tag substitute. Built on the
/// hidden-but-public redline surface of the Navisworks .NET API
/// (<c>SavedViewpoint.EditRedlines()</c> + the
/// <c>Autodesk.Navisworks.Api.Interop.LcOpRedline*</c> wrappers), which
/// Autodesk ships but does not document: behaviour may differ between
/// Navisworks releases. Coordinates are in the viewpoint's 2D markup space —
/// (0,0) is the view centre; draw one markup by hand and read it back with
/// Markup.List to calibrate the scale, or start with values around −1…1.
/// Real numbered tags (the Find Tags panel) remain UI-only: their redline
/// type is internal to the API.
/// </summary>
[NodeCategory("Navisworks.Markup")]
public static class RedlineNodes
{
    /// <summary>Adds a text markup to a saved viewpoint.</summary>
    /// <param name="viewpoint">The saved viewpoint (or its name).</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="x">Markup-space X of the text anchor (0 = view centre).</param>
    /// <param name="y">Markup-space Y of the text anchor (0 = view centre).</param>
    /// <param name="color">Line colour: a Color, hex "#RRGGBB" or [r,g,b] (empty keeps the Navisworks default).</param>
    /// <param name="thickness">Line thickness in pixels.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored viewpoint (pass-through for chaining).</returns>
    [NodeName("Markup.AddText")]
    [NodeDescription("EXPERIMENTAL: draws a text redline on a saved viewpoint (undocumented Navisworks API). Coordinates are markup space — (0,0) is the view centre; calibrate with Markup.List.")]
    [NodeSearchTags("markup", "redline", "text", "annotate", "label", "viewpoint", "tag")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint AddText(
        object viewpoint,
        string text,
        double x,
        double y,
        object? color = null,
        int thickness = 2,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("No markup text provided.", nameof(text));
        }

        return EditRedlines(viewpoint, document, list =>
            list.Add(Styled(new LcOpRedlineText(text, new Point2D(x, y)), color, thickness)));
    }

    /// <summary>Adds a straight line markup to a saved viewpoint.</summary>
    /// <param name="viewpoint">The saved viewpoint (or its name).</param>
    /// <param name="x1">Markup-space X of the start point.</param>
    /// <param name="y1">Markup-space Y of the start point.</param>
    /// <param name="x2">Markup-space X of the end point.</param>
    /// <param name="y2">Markup-space Y of the end point.</param>
    /// <param name="color">Line colour: a Color, hex "#RRGGBB" or [r,g,b] (empty keeps the Navisworks default).</param>
    /// <param name="thickness">Line thickness in pixels.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored viewpoint (pass-through for chaining).</returns>
    [NodeName("Markup.AddLine")]
    [NodeDescription("EXPERIMENTAL: draws a line redline on a saved viewpoint (undocumented Navisworks API). Markup-space coordinates; calibrate with Markup.List.")]
    [NodeSearchTags("markup", "redline", "line", "draw", "viewpoint")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint AddLine(
        object viewpoint,
        double x1,
        double y1,
        double x2,
        double y2,
        object? color = null,
        int thickness = 2,
        Document? document = null)
    {
        return EditRedlines(viewpoint, document, list =>
            list.Add(Styled(new LcOpRedlineLine(new Point2D(x1, y1), new Point2D(x2, y2)), color, thickness)));
    }

    /// <summary>Adds an arrow markup to a saved viewpoint (points from start to end).</summary>
    /// <param name="viewpoint">The saved viewpoint (or its name).</param>
    /// <param name="x1">Markup-space X of the arrow tail.</param>
    /// <param name="y1">Markup-space Y of the arrow tail.</param>
    /// <param name="x2">Markup-space X of the arrow head.</param>
    /// <param name="y2">Markup-space Y of the arrow head.</param>
    /// <param name="color">Line colour: a Color, hex "#RRGGBB" or [r,g,b] (empty keeps the Navisworks default).</param>
    /// <param name="thickness">Line thickness in pixels.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored viewpoint (pass-through for chaining).</returns>
    [NodeName("Markup.AddArrow")]
    [NodeDescription("EXPERIMENTAL: draws an arrow redline on a saved viewpoint (undocumented Navisworks API). Markup-space coordinates; calibrate with Markup.List.")]
    [NodeSearchTags("markup", "redline", "arrow", "pointer", "viewpoint")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint AddArrow(
        object viewpoint,
        double x1,
        double y1,
        double x2,
        double y2,
        object? color = null,
        int thickness = 2,
        Document? document = null)
    {
        return EditRedlines(viewpoint, document, list =>
            list.Add(Styled(new LcOpRedlineArrow(new Point2D(x1, y1), new Point2D(x2, y2)), color, thickness)));
    }

    /// <summary>Adds an ellipse markup to a saved viewpoint (fits the corner-to-corner box).</summary>
    /// <param name="viewpoint">The saved viewpoint (or its name).</param>
    /// <param name="x1">Markup-space X of the first box corner.</param>
    /// <param name="y1">Markup-space Y of the first box corner.</param>
    /// <param name="x2">Markup-space X of the opposite corner.</param>
    /// <param name="y2">Markup-space Y of the opposite corner.</param>
    /// <param name="color">Line colour: a Color, hex "#RRGGBB" or [r,g,b] (empty keeps the Navisworks default).</param>
    /// <param name="thickness">Line thickness in pixels.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored viewpoint (pass-through for chaining).</returns>
    [NodeName("Markup.AddEllipse")]
    [NodeDescription("EXPERIMENTAL: draws an ellipse redline on a saved viewpoint, fitted corner-to-corner (undocumented Navisworks API). Markup-space coordinates; calibrate with Markup.List.")]
    [NodeSearchTags("markup", "redline", "ellipse", "circle", "ring", "viewpoint")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint AddEllipse(
        object viewpoint,
        double x1,
        double y1,
        double x2,
        double y2,
        object? color = null,
        int thickness = 2,
        Document? document = null)
    {
        return EditRedlines(viewpoint, document, list =>
            list.Add(Styled(new LcOpRedlineEllipse(new Point2D(x1, y1), new Point2D(x2, y2)), color, thickness)));
    }

    /// <summary>Adds a revision-cloud markup through the given points.</summary>
    /// <param name="viewpoint">The saved viewpoint (or its name).</param>
    /// <param name="points">Cloud outline: a list of [x, y] pairs, or a flat list x1, y1, x2, y2, … (3+ points).</param>
    /// <param name="color">Line colour: a Color, hex "#RRGGBB" or [r,g,b] (empty keeps the Navisworks default).</param>
    /// <param name="thickness">Line thickness in pixels.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored viewpoint (pass-through for chaining).</returns>
    [NodeName("Markup.AddCloud")]
    [NodeDescription("EXPERIMENTAL: draws a revision-cloud redline through the given markup-space points (undocumented Navisworks API). Accepts [x,y] pairs or a flat x1,y1,x2,y2,… list.")]
    [NodeSearchTags("markup", "redline", "cloud", "revision", "revcloud", "viewpoint")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint AddCloud(
        object viewpoint,
        IList<object?> points,
        object? color = null,
        int thickness = 2,
        Document? document = null)
    {
        var coords = ReadPointPairs(points);
        if (coords.Count < 3)
        {
            throw new ArgumentException(
                "A cloud needs at least three points — wire [x,y] pairs or a flat x1,y1,x2,y2,… list.", nameof(points));
        }

        return EditRedlines(viewpoint, document, list =>
        {
            var outline = new LcOpRedlinePointList();
            foreach (var point in coords)
            {
                outline.Add(new Point2D(point.Key, point.Value));
            }

            list.Add(Styled(new LcOpRedlineCloud(outline), color, thickness));
        });
    }

    /// <summary>
    /// Adds a numbered tag-style markup: a circled number, with an optional
    /// comment attached to the viewpoint. A substitute for Navisworks tags —
    /// real Find-Tags tags have no public API.
    /// </summary>
    /// <param name="viewpoint">The saved viewpoint (or its name).</param>
    /// <param name="number">The tag number to draw.</param>
    /// <param name="x">Markup-space X of the tag centre.</param>
    /// <param name="y">Markup-space Y of the tag centre.</param>
    /// <param name="radius">Circle radius in markup-space units.</param>
    /// <param name="comment">Optional comment attached to the viewpoint (shows in the Comments panel).</param>
    /// <param name="color">Line colour: a Color, hex "#RRGGBB" or [r,g,b] (empty keeps the Navisworks default).</param>
    /// <param name="thickness">Line thickness in pixels.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored viewpoint (pass-through for chaining).</returns>
    [NodeName("Markup.AddNumberTag")]
    [NodeDescription("EXPERIMENTAL: draws a circled number on a saved viewpoint and optionally attaches a comment — a tag substitute (real Find-Tags tags have no public API). Markup-space coordinates; calibrate with Markup.List.")]
    [NodeSearchTags("markup", "redline", "tag", "number", "bubble", "comment", "viewpoint")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint AddNumberTag(
        object viewpoint,
        int number,
        double x,
        double y,
        double radius = 0.08,
        string comment = "",
        object? color = null,
        int thickness = 2,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var label = number.ToString(CultureInfo.InvariantCulture);
        var stored = EditRedlines(viewpoint, document, list =>
        {
            list.Add(Styled(new LcOpRedlineEllipse(new Point2D(x - radius, y - radius), new Point2D(x + radius, y + radius)), color, thickness));
            list.Add(Styled(new LcOpRedlineText(label, new Point2D(x, y)), color, thickness));
        });

        if (!string.IsNullOrEmpty(comment))
        {
            doc.SavedViewpoints.AddComment(
                stored, doc.CreateCommentWithUniqueId("Tag " + label + ": " + comment, CommentStatus.New));
        }

        return stored;
    }

    /// <summary>Reads the markups stored on a saved viewpoint.</summary>
    /// <param name="viewpoint">The saved viewpoint (or its name).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Markup count, per-markup type names, texts (empty for shapes) and anchor positions as [x, y] — the calibration reference for the Add nodes.</returns>
    [NodeName("Markup.List")]
    [NodeDescription("EXPERIMENTAL: lists the redline markups on a saved viewpoint — types, texts and anchor positions. Draw one markup by hand and read it here to learn the coordinate scale for the Add nodes.")]
    [NodeSearchTags("markup", "redline", "list", "read", "count", "calibrate", "viewpoint")]
    [MultiReturn("count", "types", "texts", "positions")]
    public static Dictionary<string, object?> List(object viewpoint, Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var stored = SavedItemTreeHelpers.ResolveStored<SavedViewpoint>(
            doc.SavedViewpoints.RootItem, viewpoint, "saved viewpoint");

        var types = new List<string>();
        var texts = new List<string>();
        var positions = new List<List<double>>();

        var redlines = stored.Redlines;
        int count = redlines != null ? redlines.Size() : 0;
        for (int i = 0; i < count; i++)
        {
            var redline = redlines!.ItemAt(i);
            types.Add(redline != null ? redline.GetType().Name.Replace("LcOpRedline", string.Empty) : "?");
            switch (redline)
            {
                case LcOpRedlineText text:
                    texts.Add(text.GetText() ?? string.Empty);
                    positions.Add(ToPair(text.GetPos2d()));
                    break;
                case LcOpRedlineLine line:
                    texts.Add(string.Empty);
                    positions.Add(ToPair(line.GetStart()));
                    break;
                case LcOpRedlineEllipse ellipse:
                    texts.Add(string.Empty);
                    positions.Add(ToPair(ellipse.GetStart()));
                    break;
                default:
                    texts.Add(string.Empty);
                    positions.Add(new List<double>());
                    break;
            }
        }

        return new Dictionary<string, object?>
        {
            { "count", count },
            { "types", types },
            { "texts", texts },
            { "positions", positions },
        };
    }

    /// <summary>Removes every markup from a saved viewpoint.</summary>
    /// <param name="viewpoint">The saved viewpoint (or its name).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored viewpoint (pass-through for chaining).</returns>
    [NodeName("Markup.Clear")]
    [NodeDescription("EXPERIMENTAL: removes every redline markup from a saved viewpoint (undocumented Navisworks API).")]
    [NodeSearchTags("markup", "redline", "clear", "delete", "remove", "viewpoint")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint Clear(object viewpoint, Document? document = null)
    {
        return EditRedlines(viewpoint, document, list => list.Clear());
    }

    // ----- internals -----------------------------------------------------------

    /// <summary>
    /// Runs an edit against the stored viewpoint's redline list. Tries the
    /// in-place <c>EditRedlines()</c> first (the API's sanctioned edit path);
    /// if the stored instance refuses (read-only wrapper), falls back to
    /// copy → edit → ReplaceWithCopy at the same tree position.
    /// </summary>
    private static SavedViewpoint EditRedlines(object viewpoint, Document? document, Action<LcOpRedlineList> edit)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var root = doc.SavedViewpoints.RootItem;
        var stored = SavedItemTreeHelpers.ResolveStored<SavedViewpoint>(root, viewpoint, "saved viewpoint");

        try
        {
            edit(stored.EditRedlines());
            return stored;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException ||
            ex is NotSupportedException ||
            ex is UnauthorizedAccessException ||
            ex is System.Runtime.InteropServices.COMException ||
            ex is System.Runtime.InteropServices.SEHException)
        {
            // Read-only stored instance: deep-copy, edit the copy, swap it in.
            var parent = (GroupItem?)stored.Parent ?? root;
            int index = SavedItemTreeHelpers.IndexByIdentity(parent.Children, stored);
            if (index < 0)
            {
                throw new InvalidOperationException(
                    "Could not locate the viewpoint '" + stored.DisplayName + "' in the tree to update its markups.");
            }

            var copy = (SavedViewpoint)stored.CreateCopy();
            edit(copy.EditRedlines());
            if (ReferenceEquals(parent, root))
            {
                doc.SavedViewpoints.ReplaceWithCopy(index, copy);
            }
            else
            {
                doc.SavedViewpoints.ReplaceWithCopy(parent, index, copy);
            }

            var replaced = SavedItemTreeHelpers.FindStoredEquivalent(root, copy);
            return replaced ?? copy;
        }
    }

    /// <summary>Applies colour/thickness to a fresh redline and returns it.</summary>
    private static LcOpRedline Styled(LcOpRedline redline, object? color, int thickness)
    {
        if (thickness > 0)
        {
            redline.SetLineThickness(thickness);
        }

        if (color != null && !(color is string s && s.Length == 0))
        {
            redline.SetLineColor(NavisValues.ToNavisColor(color));
        }

        return redline;
    }

    /// <summary>Reads cloud points: nested [x,y] pairs or a flat x1,y1,x2,y2,… list.</summary>
    private static List<KeyValuePair<double, double>> ReadPointPairs(IList<object?> points)
    {
        var result = new List<KeyValuePair<double, double>>();
        if (points == null || points.Count == 0)
        {
            return result;
        }

        if (points[0] is IEnumerable && !(points[0] is string))
        {
            foreach (var entry in points)
            {
                var pair = new List<double>();
                if (entry is IEnumerable coords && !(entry is string))
                {
                    foreach (var coordinate in coords)
                    {
                        pair.Add(ToDouble(coordinate));
                    }
                }

                if (pair.Count < 2)
                {
                    throw new ArgumentException("Each cloud point needs [x, y] — got '" + (entry ?? "null") + "'.");
                }

                result.Add(new KeyValuePair<double, double>(pair[0], pair[1]));
            }

            return result;
        }

        if (points.Count % 2 != 0)
        {
            throw new ArgumentException("A flat cloud point list needs an even number of values (x1, y1, x2, y2, …).");
        }

        for (int i = 0; i < points.Count; i += 2)
        {
            result.Add(new KeyValuePair<double, double>(ToDouble(points[i]), ToDouble(points[i + 1])));
        }

        return result;
    }

    private static double ToDouble(object? value)
    {
        switch (value)
        {
            case null:
                throw new ArgumentException("A cloud point coordinate is empty.");
            case double d:
                return d;
            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                return parsed;
            case IConvertible convertible:
                return convertible.ToDouble(CultureInfo.InvariantCulture);
            default:
                throw new ArgumentException("Cannot read '" + value + "' as a coordinate number.");
        }
    }

    private static List<double> ToPair(Point2D point) =>
        point != null ? new List<double> { point.X, point.Y } : new List<double>();
}
