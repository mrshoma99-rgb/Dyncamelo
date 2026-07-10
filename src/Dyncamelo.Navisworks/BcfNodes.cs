using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>
/// BCF 2.1 exchange nodes (WS-E): export clash results or saved viewpoints as
/// .bcfzip issues and read them back — the vendor-neutral bridge to BIMcollab,
/// Newforma Konekt, Revizto and ACC (none of which expose a public issues
/// API). Component references use the IFC GlobalId property when the source
/// model has one, else the item's InstanceGuid in IFC-compressed form — lossy
/// for sources without stable GUIDs (documented per node).
/// </summary>
[NodeCategory("Navisworks.Exchange")]
public static class BcfNodes
{
    private const int SnapshotWidth = 1280;
    private const int SnapshotHeight = 720;

    /// <summary>Exports clash results and/or saved viewpoints as a BCF 2.1 package.</summary>
    /// <param name="filePath">Destination .bcfzip (or .bcf) path; the directory is created when missing.</param>
    /// <param name="results">Clash results or result groups to export as topics (from ClashTest.Results).</param>
    /// <param name="viewpoints">Saved viewpoints to export as topics (instead of, or besides, results).</param>
    /// <param name="includeSnapshots">True to render a snapshot.png per topic (slower on big result lists).</param>
    /// <param name="statusMap">
    /// Optional Navisworks-status → BCF-topic-status map (e.g. {"New": "Open", "Approved": "Closed"}).
    /// Unmapped statuses use the default map: New/Active → Open, Reviewed → In Progress, Approved/Resolved → Closed.
    /// </param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written path and the number of topics exported.</returns>
    [NodeName("BCF.ExportIssues")]
    [NodeDescription("Exports clash results (or saved viewpoints) as BCF 2.1 issues (.bcfzip: markup, camera viewpoint, component GUIDs, snapshot) — the vendor-neutral bridge into BIMcollab / Konekt / Revizto / ACC. Components use the IFC GlobalId when present, else the InstanceGuid (lossy for non-IFC sources). Cameras are written in meters per the BCF convention.")]
    [NodeSearchTags("bcf", "export", "issues", "bcfzip", "bimcollab", "revizto", "konekt", "acc", "exchange")]
    [MultiReturn("filePath", "topicCount")]
    public static Dictionary<string, object?> ExportIssues(
        string filePath,
        IEnumerable<SavedItem>? results = null,
        IEnumerable<SavedViewpoint>? viewpoints = null,
        bool includeSnapshots = true,
        IDictionary? statusMap = null,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var toMeters = UnitConversion.ScaleFactor(doc.Units, Units.Meters);

        var topics = new List<BcfTopic>();
        var usedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (results != null)
        {
            var clash = ClashHelpers.RequireClash(doc);
            foreach (var item in results)
            {
                if (item == null)
                {
                    continue;
                }

                var clashResult = item as IClashResult
                    ?? throw new ArgumentException(
                        "'" + item.DisplayName + "' is not a clash result or result group. " +
                        "Wire results from ClashTest.Results (saved viewpoints go into the viewpoints port).",
                        nameof(results));
                topics.Add(TopicFromClashResult(doc, clash, item, clashResult, toMeters, includeSnapshots,
                    statusMap, usedGuids));
            }
        }

        if (viewpoints != null)
        {
            foreach (var savedViewpoint in viewpoints)
            {
                if (savedViewpoint != null)
                {
                    topics.Add(TopicFromSavedViewpoint(doc, savedViewpoint, toMeters, includeSnapshots, usedGuids));
                }
            }
        }

        if (topics.Count == 0)
        {
            throw new ArgumentException(
                "Nothing to export — wire clash results (from ClashTest.Results) and/or saved viewpoints.",
                nameof(results));
        }

        BcfFile.Write(filePath, topics);
        return new Dictionary<string, object?>
        {
            ["filePath"] = filePath,
            ["topicCount"] = topics.Count,
        };
    }

    /// <summary>Reads a BCF package back into topic data and matched model items.</summary>
    /// <param name="filePath">The .bcfzip (or .bcf) file to read.</param>
    /// <param name="applyCameraTopicIndex">
    /// Index of a topic whose camera should be applied to the current view (-1 = leave the camera alone).
    /// </param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Topic dictionaries and, per topic, the model items its component GUIDs resolve to.</returns>
    [NodeName("BCF.ImportIssues")]
    [NodeDescription("Reads a BCF 2.0/2.1 package: per topic title/status/description/comments/component GUIDs/camera, plus the model items each topic's components resolve to (matched by IFC GlobalId, then InstanceGuid). Optionally applies one topic's camera to the current view — feeds ClashResult.SetStatus and Selection.SetCurrent for the issue-sync return leg.")]
    [NodeSearchTags("bcf", "import", "issues", "bcfzip", "read", "roundtrip", "exchange")]
    [MultiReturn("topics", "modelItems")]
    public static Dictionary<string, object?> ImportIssues(
        string filePath,
        int applyCameraTopicIndex = -1,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var bcfTopics = BcfFile.Read(filePath);
        if (bcfTopics.Count == 0)
        {
            throw new InvalidDataException(
                "'" + filePath + "' contains no BCF topics (no folder with a markup.bcf was found).");
        }

        var fromMeters = 1.0 / UnitConversion.ScaleFactor(doc.Units, Units.Meters);
        var itemsByComponent = ResolveComponents(doc, bcfTopics);

        var topics = new List<Dictionary<string, object?>>(bcfTopics.Count);
        var modelItems = new List<List<ModelItem>>(bcfTopics.Count);
        foreach (var topic in bcfTopics)
        {
            topics.Add(TopicToDictionary(topic, fromMeters));

            var matched = new List<ModelItem>();
            var seen = new ModelItemSet();
            foreach (var componentGuid in topic.ComponentIfcGuids)
            {
                if (itemsByComponent.TryGetValue(componentGuid, out var found))
                {
                    foreach (var item in found)
                    {
                        if (seen.Add(item))
                        {
                            matched.Add(item);
                        }
                    }
                }
            }

            modelItems.Add(matched);
        }

        if (applyCameraTopicIndex >= 0)
        {
            if (applyCameraTopicIndex >= bcfTopics.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(applyCameraTopicIndex),
                    "Topic index " + applyCameraTopicIndex + " is out of range — the package has " +
                    bcfTopics.Count + " topics.");
            }

            var camera = bcfTopics[applyCameraTopicIndex].Camera
                ?? throw new InvalidOperationException(
                    "Topic " + applyCameraTopicIndex + " ('" + bcfTopics[applyCameraTopicIndex].Title +
                    "') has no camera in its viewpoint.");
            ApplyCamera(doc, camera, fromMeters);
        }

        return new Dictionary<string, object?>
        {
            ["topics"] = topics,
            ["modelItems"] = modelItems,
        };
    }

    // -------------------------------------------------------------- export

    private static BcfTopic TopicFromClashResult(
        Document doc,
        DocumentClash clash,
        SavedItem item,
        IClashResult clashResult,
        double toMeters,
        bool includeSnapshots,
        IDictionary? statusMap,
        HashSet<string> usedGuids)
    {
        var topic = new BcfTopic
        {
            Guid = UniqueTopicGuid(item.Guid, usedGuids),
            Title = string.IsNullOrEmpty(item.DisplayName) ? "Clash" : item.DisplayName,
            TopicType = "Clash",
            TopicStatus = MapStatus(clashResult.Status.ToString(), statusMap),
            Description = clashResult.Description ?? string.Empty,
            CreationDate = clashResult.CreatedTime ?? DateTime.UtcNow,
        };

        foreach (var comment in item.Comments)
        {
            topic.Comments.Add(new BcfTopicComment
            {
                Guid = Guid.NewGuid().ToString("D"),
                Text = comment.Body ?? string.Empty,
                Author = comment.Author ?? string.Empty,
                Date = comment.CreationDate,
            });
        }

        CollectComponents(clashResult, topic.ComponentIfcGuids);

        var viewpoint = clash.TestsData.TestsViewpointForResult(clashResult);
        if (viewpoint != null)
        {
            topic.Camera = CaptureCamera(viewpoint, toMeters);
        }

        if (includeSnapshots)
        {
            using (var bitmap = clash.TestsData.TestsImageForResult(
                clashResult, ImageGenerationStyle.ScenePlusOverlay, SnapshotWidth, SnapshotHeight))
            {
                topic.SnapshotPng = ToPng(bitmap);
            }
        }

        return topic;
    }

    private static BcfTopic TopicFromSavedViewpoint(
        Document doc,
        SavedViewpoint savedViewpoint,
        double toMeters,
        bool includeSnapshots,
        HashSet<string> usedGuids)
    {
        var topic = new BcfTopic
        {
            Guid = UniqueTopicGuid(savedViewpoint.Guid, usedGuids),
            Title = string.IsNullOrEmpty(savedViewpoint.DisplayName) ? "Viewpoint" : savedViewpoint.DisplayName,
            TopicType = "Issue",
            TopicStatus = "Open",
        };

        foreach (var comment in savedViewpoint.Comments)
        {
            topic.Comments.Add(new BcfTopicComment
            {
                Guid = Guid.NewGuid().ToString("D"),
                Text = comment.Body ?? string.Empty,
                Author = comment.Author ?? string.Empty,
                Date = comment.CreationDate,
            });
        }

        var viewpoint = savedViewpoint.Viewpoint;
        if (viewpoint != null)
        {
            topic.Camera = CaptureCamera(viewpoint, toMeters);
        }

        if (includeSnapshots && viewpoint != null)
        {
            // Render through the live view: apply the viewpoint, grab the
            // image, restore the user's camera.
            var restore = doc.CurrentViewpoint.CreateCopy();
            try
            {
                doc.CurrentViewpoint.CopyFrom(viewpoint);
                using (var bitmap = doc.GenerateImage(
                    ImageGenerationStyle.Scene, SnapshotWidth, SnapshotHeight, true))
                {
                    topic.SnapshotPng = ToPng(bitmap);
                }
            }
            finally
            {
                doc.CurrentViewpoint.CopyFrom(restore);
            }
        }

        return topic;
    }

    /// <summary>Camera in BCF terms: meters, direction/up from the rotation quaternion.</summary>
    private static BcfCamera CaptureCamera(Viewpoint viewpoint, double toMeters)
    {
        var rotation = viewpoint.Rotation;
        var position = viewpoint.Position;
        var camera = new BcfCamera
        {
            IsPerspective = viewpoint.Projection == ViewpointProjection.Perspective,
            Position = new[] { position.X * toMeters, position.Y * toMeters, position.Z * toMeters },
            Direction = BcfCameraMath.ViewDirection(rotation.A, rotation.B, rotation.C, rotation.D),
            Up = BcfCameraMath.UpVector(rotation.A, rotation.B, rotation.C, rotation.D),
        };

        if (camera.IsPerspective)
        {
            // HeightField = vertical field of view in radians; BCF wants degrees.
            camera.FieldOfViewDegrees = viewpoint.HeightField * 180.0 / Math.PI;
        }
        else
        {
            // HeightField = visible view height in document units (RUNTIME-CHECK).
            camera.ViewToWorldScale = viewpoint.HeightField * toMeters;
        }

        return camera;
    }

    /// <summary>Adds the clashing items' GUIDs (IFC GlobalId, else InstanceGuid) to a topic.</summary>
    private static void CollectComponents(IClashResult clashResult, List<string> componentGuids)
    {
        const int maxComponents = 200; // groups can aggregate thousands of items
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var items = new List<ModelItem?>();
        if (clashResult is ClashResult single)
        {
            items.Add(single.Item1);
            items.Add(single.Item2);
        }
        else
        {
            foreach (ModelItem item in clashResult.CompositeItemSelection1)
            {
                items.Add(item);
            }

            foreach (ModelItem item in clashResult.CompositeItemSelection2)
            {
                items.Add(item);
            }
        }

        foreach (var item in items)
        {
            if (componentGuids.Count >= maxComponents)
            {
                break;
            }

            var componentGuid = ComponentGuid(item);
            if (componentGuid != null && seen.Add(componentGuid))
            {
                componentGuids.Add(componentGuid);
            }
        }
    }

    /// <summary>
    /// The BCF component GUID of one item: its IFC GlobalId property when the
    /// source model carries one, else its InstanceGuid in the 22-character IFC
    /// form. Null when the item has neither (documented lossiness).
    /// </summary>
    private static string? ComponentGuid(ModelItem? item)
    {
        if (item == null)
        {
            return null;
        }

        var property = item.PropertyCategories.FindPropertyByDisplayName("IFC", "GlobalId")
            ?? item.PropertyCategories.FindPropertyByName("IFC", "GlobalId");
        if (property != null)
        {
            var value = NavisValues.ToClrObject(property.Value) as string;
            if (!string.IsNullOrEmpty(value))
            {
                return value!.Trim();
            }
        }

        var guid = item.InstanceGuid;
        return guid != Guid.Empty ? IfcGuidCodec.Encode(guid) : null;
    }

    private static string UniqueTopicGuid(Guid itemGuid, HashSet<string> usedGuids)
    {
        var candidate = itemGuid != Guid.Empty ? itemGuid.ToString("D") : Guid.NewGuid().ToString("D");
        while (!usedGuids.Add(candidate))
        {
            candidate = Guid.NewGuid().ToString("D");
        }

        return candidate;
    }

    private static string MapStatus(string navisStatus, IDictionary? statusMap)
    {
        if (statusMap != null)
        {
            foreach (DictionaryEntry entry in statusMap)
            {
                var key = Convert.ToString(entry.Key, System.Globalization.CultureInfo.InvariantCulture);
                if (string.Equals(key, navisStatus, StringComparison.OrdinalIgnoreCase))
                {
                    var mapped = Convert.ToString(entry.Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(mapped))
                    {
                        return mapped!;
                    }
                }
            }
        }

        switch (navisStatus)
        {
            case "New":
            case "Active":
                return "Open";
            case "Reviewed":
                return "In Progress";
            case "Approved":
            case "Resolved":
                return "Closed";
            default:
                return "Open";
        }
    }

    private static byte[] ToPng(System.Drawing.Bitmap bitmap)
    {
        using (var buffer = new MemoryStream())
        {
            bitmap.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
            return buffer.ToArray();
        }
    }

    // -------------------------------------------------------------- import

    /// <summary>
    /// Resolves every component GUID in the package to model items: one pass
    /// over the scene matches InstanceGuids, then IFC GlobalId searches cover
    /// the rest (IFC-authored GUIDs usually decode to the same instance GUID,
    /// so the search fallback rarely runs).
    /// </summary>
    private static Dictionary<string, List<ModelItem>> ResolveComponents(
        Document doc,
        List<BcfTopic> topics)
    {
        // Component string → decoded .NET Guid (when decodable).
        var wanted = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var topic in topics)
        {
            foreach (var componentGuid in topic.ComponentIfcGuids)
            {
                if (!wanted.ContainsKey(componentGuid))
                {
                    wanted[componentGuid] = IfcGuidCodec.TryDecode(componentGuid, out var decoded)
                        ? decoded
                        : Guid.Empty;
                }
            }
        }

        var found = new Dictionary<string, List<ModelItem>>(StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return found;
        }

        // Pass 1: single walk of the scene matching InstanceGuid.
        var componentsByGuid = new Dictionary<Guid, List<string>>();
        foreach (var pair in wanted)
        {
            if (pair.Value == Guid.Empty)
            {
                continue;
            }

            if (!componentsByGuid.TryGetValue(pair.Value, out var names))
            {
                names = new List<string>();
                componentsByGuid[pair.Value] = names;
            }

            names.Add(pair.Key);
        }

        if (componentsByGuid.Count > 0)
        {
            foreach (var item in doc.Models.RootItemDescendantsAndSelf)
            {
                var instanceGuid = item.InstanceGuid;
                if (instanceGuid == Guid.Empty || !componentsByGuid.TryGetValue(instanceGuid, out var names))
                {
                    continue;
                }

                foreach (var name in names)
                {
                    if (!found.TryGetValue(name, out var list))
                    {
                        list = new List<ModelItem>();
                        found[name] = list;
                    }

                    list.Add(item);
                }
            }
        }

        // Pass 2: IFC GlobalId property search for anything still unmatched.
        foreach (var pair in wanted)
        {
            if (found.ContainsKey(pair.Key))
            {
                continue;
            }

            var search = new Search();
            search.Selection.SelectAll();
            search.Locations = SearchLocations.DescendantsAndSelf;
            search.SearchConditions.Add(
                SearchCondition.HasPropertyByDisplayName("IFC", "GlobalId")
                    .EqualValue(VariantData.FromDisplayString(pair.Key)));

            var matches = NavisValues.ToItemList(search.FindAll(doc, false));
            if (matches.Count > 0)
            {
                found[pair.Key] = matches;
            }
        }

        return found;
    }

    private static Dictionary<string, object?> TopicToDictionary(BcfTopic topic, double fromMeters)
    {
        var comments = new List<string>();
        var commentAuthors = new List<string>();
        var commentDates = new List<DateTime>();
        foreach (var comment in topic.Comments)
        {
            comments.Add(comment.Text);
            commentAuthors.Add(comment.Author);
            commentDates.Add(comment.Date);
        }

        Dictionary<string, object?>? camera = null;
        var bcfCamera = topic.Camera;
        if (bcfCamera != null)
        {
            camera = new Dictionary<string, object?>
            {
                ["isPerspective"] = bcfCamera.IsPerspective,
                // Back into document units so points chain into Camera/geometry nodes.
                ["position"] = ScaledList(bcfCamera.Position, fromMeters),
                ["direction"] = new List<double>(bcfCamera.Direction),
                ["up"] = new List<double>(bcfCamera.Up),
                ["fieldOfView"] = bcfCamera.FieldOfViewDegrees,
                ["viewToWorldScale"] = bcfCamera.ViewToWorldScale * fromMeters,
            };
        }

        return new Dictionary<string, object?>
        {
            ["guid"] = topic.Guid,
            ["title"] = topic.Title,
            ["status"] = topic.TopicStatus,
            ["type"] = topic.TopicType,
            ["description"] = topic.Description,
            ["creationAuthor"] = topic.CreationAuthor,
            ["creationDate"] = topic.CreationDate,
            ["comments"] = comments,
            ["commentAuthors"] = commentAuthors,
            ["commentDates"] = commentDates,
            ["componentGuids"] = new List<string>(topic.ComponentIfcGuids),
            ["camera"] = camera,
            ["hasSnapshot"] = topic.SnapshotPng != null && topic.SnapshotPng.Length > 0,
        };
    }

    private static void ApplyCamera(Document doc, BcfCamera camera, double fromMeters)
    {
        var viewpoint = new Viewpoint();
        var px = camera.Position[0] * fromMeters;
        var py = camera.Position[1] * fromMeters;
        var pz = camera.Position[2] * fromMeters;
        viewpoint.Position = new Point3D(px, py, pz);
        viewpoint.PointAt(new Point3D(
            px + camera.Direction[0],
            py + camera.Direction[1],
            pz + camera.Direction[2]));
        viewpoint.AlignUp(new Vector3D(camera.Up[0], camera.Up[1], camera.Up[2]));
        if (camera.IsPerspective)
        {
            viewpoint.Projection = ViewpointProjection.Perspective;
            viewpoint.HeightField = camera.FieldOfViewDegrees * Math.PI / 180.0;
        }
        else
        {
            viewpoint.Projection = ViewpointProjection.Orthographic;
            viewpoint.HeightField = camera.ViewToWorldScale * fromMeters;
        }

        doc.CurrentViewpoint.CopyFrom(viewpoint);
    }

    private static List<double> ScaledList(double[] values, double scale)
    {
        var list = new List<double>(values.Length);
        foreach (var value in values)
        {
            list.Add(value * scale);
        }

        return list;
    }
}
