using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Dyncamelo.Navisworks.Internal;

// ---------------------------------------------------------------------------
// Pure BCF 2.1 file logic: topic model, .bcfzip writer/reader, IFC GUID codec
// and quaternion camera math. Deliberately free of Autodesk types so the
// logic can be exercised headlessly (Linux CI / scratch harness); the node
// layer (BcfNodes.cs) owns every Navisworks call.
// ---------------------------------------------------------------------------

/// <summary>Camera of a BCF viewpoint. Lengths are in meters (the BCF convention).</summary>
internal sealed class BcfCamera
{
    /// <summary>True for a perspective camera, false for orthogonal.</summary>
    internal bool IsPerspective;

    /// <summary>Camera position (meters), x/y/z.</summary>
    internal double[] Position = new double[3];

    /// <summary>View direction (unit vector), x/y/z.</summary>
    internal double[] Direction = new double[3];

    /// <summary>Up vector (unit vector), x/y/z.</summary>
    internal double[] Up = new double[3];

    /// <summary>Vertical field of view in DEGREES (perspective cameras only).</summary>
    internal double FieldOfViewDegrees;

    /// <summary>View-to-world scale = visible view height in meters (orthogonal cameras only).</summary>
    internal double ViewToWorldScale;
}

/// <summary>One comment of a BCF topic.</summary>
internal sealed class BcfTopicComment
{
    internal string Guid = string.Empty;
    internal string Text = string.Empty;
    internal string Author = string.Empty;
    internal DateTime Date = DateTime.UtcNow;
}

/// <summary>One BCF topic: markup + viewpoint components/camera + snapshot.</summary>
internal sealed class BcfTopic
{
    internal string Guid = string.Empty;
    internal string Title = string.Empty;
    internal string TopicType = "Issue";
    internal string TopicStatus = "Open";
    internal string Description = string.Empty;
    internal string CreationAuthor = string.Empty;
    internal DateTime CreationDate = DateTime.UtcNow;
    internal List<BcfTopicComment> Comments = new List<BcfTopicComment>();

    /// <summary>Component references as 22-character IFC GUID strings.</summary>
    internal List<string> ComponentIfcGuids = new List<string>();

    internal BcfCamera? Camera;

    /// <summary>PNG bytes of the topic snapshot, or null when none.</summary>
    internal byte[]? SnapshotPng;
}

/// <summary>
/// Reads and writes BCF 2.1 packages (.bcfzip): a zip containing
/// <c>bcf.version</c> plus one folder per topic (named by the topic GUID) with
/// <c>markup.bcf</c>, <c>viewpoint.bcfv</c> and <c>snapshot.png</c>.
/// The writer emits schema-ordered XML; the reader is tolerant (it takes what
/// it finds and skips what it does not know), so packages written by
/// BIMcollab, BCFier, Revizto etc. read back fine.
/// </summary>
internal static class BcfFile
{
    /// <summary>Writes a BCF 2.1 package. Creates the parent directory when missing.</summary>
    internal static void Write(string path, IReadOnlyList<BcfTopic> topics)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("No file path provided.", nameof(path));
        }

        if (topics == null || topics.Count == 0)
        {
            throw new ArgumentException("No topics to export.", nameof(topics));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "bcf.version", writer =>
            {
                writer.WriteStartElement("Version");
                writer.WriteAttributeString("VersionId", "2.1");
                writer.WriteElementString("DetailedVersion", "2.1");
                writer.WriteEndElement();
            });

            foreach (var topic in topics)
            {
                var folder = topic.Guid;
                var viewpointGuid = System.Guid.NewGuid().ToString("D");
                var snapshotPng = topic.SnapshotPng;
                var hasSnapshot = snapshotPng != null && snapshotPng.Length > 0;
                var hasViewpoint = topic.Camera != null || topic.ComponentIfcGuids.Count > 0 || hasSnapshot;

                WriteEntry(zip, folder + "/markup.bcf", writer =>
                    WriteMarkup(writer, topic, hasViewpoint, viewpointGuid, hasSnapshot));

                if (hasViewpoint)
                {
                    WriteEntry(zip, folder + "/viewpoint.bcfv", writer =>
                        WriteVisualizationInfo(writer, topic, viewpointGuid));
                }

                if (hasSnapshot)
                {
                    var entry = zip.CreateEntry(folder + "/snapshot.png");
                    using (var entryStream = entry.Open())
                    {
                        entryStream.Write(snapshotPng!, 0, snapshotPng!.Length);
                    }
                }
            }
        }
    }

    /// <summary>Reads a BCF package (2.0/2.1 markup shape). Snapshot bytes are included.</summary>
    internal static List<BcfTopic> Read(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("No file path provided.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("BCF file not found: '" + path + "'.", path);
        }

        var topics = new List<BcfTopic>();
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                // Group entries by topic folder (the segment before the first '/').
                var byFolder = new Dictionary<string, Dictionary<string, ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    var slash = name.IndexOf('/');
                    if (slash <= 0 || slash == name.Length - 1)
                    {
                        continue; // top-level files (bcf.version, project.bcfp)
                    }

                    var folder = name.Substring(0, slash);
                    var file = name.Substring(slash + 1);
                    if (!byFolder.TryGetValue(folder, out var files))
                    {
                        files = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                        byFolder[folder] = files;
                    }

                    files[file] = entry;
                }

                foreach (var pair in byFolder)
                {
                    if (!pair.Value.TryGetValue("markup.bcf", out var markupEntry))
                    {
                        continue; // not a topic folder
                    }

                    var topic = new BcfTopic { Guid = pair.Key };
                    string? viewpointFile = null;
                    string? snapshotFile = null;
                    using (var markupStream = markupEntry.Open())
                    {
                        ReadMarkup(markupStream, topic, out viewpointFile, out snapshotFile);
                    }

                    // Fall back to the conventional file names when the markup
                    // does not reference them explicitly.
                    if (viewpointFile == null && pair.Value.ContainsKey("viewpoint.bcfv"))
                    {
                        viewpointFile = "viewpoint.bcfv";
                    }

                    if (snapshotFile == null && pair.Value.ContainsKey("snapshot.png"))
                    {
                        snapshotFile = "snapshot.png";
                    }

                    if (viewpointFile != null && pair.Value.TryGetValue(viewpointFile, out var bcfvEntry))
                    {
                        using (var bcfvStream = bcfvEntry.Open())
                        {
                            ReadVisualizationInfo(bcfvStream, topic);
                        }
                    }

                    if (snapshotFile != null && pair.Value.TryGetValue(snapshotFile, out var pngEntry))
                    {
                        using (var pngStream = pngEntry.Open())
                        using (var buffer = new MemoryStream())
                        {
                            pngStream.CopyTo(buffer);
                            topic.SnapshotPng = buffer.ToArray();
                        }
                    }

                    topics.Add(topic);
                }
            }
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException(
                "'" + path + "' is not a BCF package (not a valid zip archive).", ex);
        }

        // Stable ordering: by creation date, then title.
        topics.Sort((a, b) =>
        {
            var byDate = a.CreationDate.CompareTo(b.CreationDate);
            return byDate != 0 ? byDate : string.CompareOrdinal(a.Title, b.Title);
        });
        return topics;
    }

    // ----------------------------------------------------------- writing

    private static void WriteEntry(ZipArchive zip, string entryName, Action<XmlWriter> writeBody)
    {
        var entry = zip.CreateEntry(entryName);
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            CloseOutput = false,
        };
        using (var entryStream = entry.Open())
        using (var writer = XmlWriter.Create(entryStream, settings))
        {
            writer.WriteStartDocument();
            writeBody(writer);
            writer.WriteEndDocument();
        }
    }

    /// <summary>Markup per the BCF 2.1 markup.xsd element order.</summary>
    private static void WriteMarkup(
        XmlWriter writer, BcfTopic topic, bool hasViewpoint, string viewpointGuid, bool hasSnapshot)
    {
        writer.WriteStartElement("Markup");

        writer.WriteStartElement("Topic");
        writer.WriteAttributeString("Guid", topic.Guid);
        writer.WriteAttributeString("TopicType", topic.TopicType);
        writer.WriteAttributeString("TopicStatus", topic.TopicStatus);
        writer.WriteElementString("Title", topic.Title);
        writer.WriteElementString("CreationDate", FormatDate(topic.CreationDate));
        writer.WriteElementString("CreationAuthor",
            string.IsNullOrEmpty(topic.CreationAuthor) ? "Dyncamelo" : topic.CreationAuthor);
        if (!string.IsNullOrEmpty(topic.Description))
        {
            writer.WriteElementString("Description", topic.Description);
        }

        writer.WriteEndElement(); // Topic

        foreach (var comment in topic.Comments)
        {
            writer.WriteStartElement("Comment");
            writer.WriteAttributeString("Guid",
                string.IsNullOrEmpty(comment.Guid) ? System.Guid.NewGuid().ToString("D") : comment.Guid);
            writer.WriteElementString("Date", FormatDate(comment.Date));
            writer.WriteElementString("Author",
                string.IsNullOrEmpty(comment.Author) ? "Dyncamelo" : comment.Author);
            writer.WriteElementString("Comment", comment.Text);
            writer.WriteEndElement();
        }

        if (hasViewpoint)
        {
            writer.WriteStartElement("Viewpoints");
            writer.WriteAttributeString("Guid", viewpointGuid);
            writer.WriteElementString("Viewpoint", "viewpoint.bcfv");
            if (hasSnapshot)
            {
                writer.WriteElementString("Snapshot", "snapshot.png");
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement(); // Markup
    }

    /// <summary>viewpoint.bcfv per the BCF 2.1 visinfo.xsd element order.</summary>
    private static void WriteVisualizationInfo(XmlWriter writer, BcfTopic topic, string viewpointGuid)
    {
        writer.WriteStartElement("VisualizationInfo");
        writer.WriteAttributeString("Guid", viewpointGuid);

        if (topic.ComponentIfcGuids.Count > 0)
        {
            writer.WriteStartElement("Components");
            writer.WriteStartElement("Selection");
            foreach (var ifcGuid in topic.ComponentIfcGuids)
            {
                writer.WriteStartElement("Component");
                writer.WriteAttributeString("IfcGuid", ifcGuid);
                writer.WriteEndElement();
            }

            writer.WriteEndElement(); // Selection
            writer.WriteStartElement("Visibility");
            writer.WriteAttributeString("DefaultVisibility", "true");
            writer.WriteEndElement();
            writer.WriteEndElement(); // Components
        }

        var camera = topic.Camera;
        if (camera != null)
        {
            writer.WriteStartElement(camera.IsPerspective ? "PerspectiveCamera" : "OrthogonalCamera");
            WriteXyz(writer, "CameraViewPoint", camera.Position);
            WriteXyz(writer, "CameraDirection", camera.Direction);
            WriteXyz(writer, "CameraUpVector", camera.Up);
            if (camera.IsPerspective)
            {
                writer.WriteElementString("FieldOfView", FormatDouble(camera.FieldOfViewDegrees));
            }
            else
            {
                writer.WriteElementString("ViewToWorldScale", FormatDouble(camera.ViewToWorldScale));
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement(); // VisualizationInfo
    }

    private static void WriteXyz(XmlWriter writer, string elementName, double[] xyz)
    {
        writer.WriteStartElement(elementName);
        writer.WriteElementString("X", FormatDouble(xyz[0]));
        writer.WriteElementString("Y", FormatDouble(xyz[1]));
        writer.WriteElementString("Z", FormatDouble(xyz[2]));
        writer.WriteEndElement();
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
            : value.ToUniversalTime();
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    // ----------------------------------------------------------- reading

    private static void ReadMarkup(Stream stream, BcfTopic topic, out string? viewpointFile, out string? snapshotFile)
    {
        viewpointFile = null;
        snapshotFile = null;
        var doc = LoadXml(stream);
        var root = doc.DocumentElement;
        if (root == null)
        {
            return;
        }

        foreach (XmlNode node in root.ChildNodes)
        {
            var element = node as XmlElement;
            if (element == null)
            {
                continue;
            }

            switch (element.LocalName)
            {
                case "Topic":
                    var guid = element.GetAttribute("Guid");
                    if (!string.IsNullOrEmpty(guid))
                    {
                        topic.Guid = guid;
                    }

                    topic.TopicType = FirstNonEmpty(element.GetAttribute("TopicType"), topic.TopicType);
                    topic.TopicStatus = FirstNonEmpty(element.GetAttribute("TopicStatus"), topic.TopicStatus);
                    topic.Title = ChildText(element, "Title") ?? string.Empty;
                    topic.Description = ChildText(element, "Description") ?? string.Empty;
                    topic.CreationAuthor = ChildText(element, "CreationAuthor") ?? string.Empty;
                    topic.CreationDate = ParseDate(ChildText(element, "CreationDate"));
                    break;

                case "Comment":
                    var comment = new BcfTopicComment
                    {
                        Guid = element.GetAttribute("Guid"),
                        Text = ChildText(element, "Comment") ?? string.Empty,
                        Author = ChildText(element, "Author") ?? string.Empty,
                        Date = ParseDate(ChildText(element, "Date")),
                    };
                    topic.Comments.Add(comment);
                    break;

                case "Viewpoints":
                    if (viewpointFile == null)
                    {
                        viewpointFile = ChildText(element, "Viewpoint");
                        snapshotFile = ChildText(element, "Snapshot");
                    }

                    break;
            }
        }
    }

    private static void ReadVisualizationInfo(Stream stream, BcfTopic topic)
    {
        var doc = LoadXml(stream);
        var root = doc.DocumentElement;
        if (root == null)
        {
            return;
        }

        // Components/Selection/Component@IfcGuid — any nesting depth of the
        // Components block is tolerated.
        foreach (XmlElement component in ElementsByLocalName(root, "Component"))
        {
            var ifcGuid = component.GetAttribute("IfcGuid");
            if (!string.IsNullOrEmpty(ifcGuid))
            {
                topic.ComponentIfcGuids.Add(ifcGuid);
            }
        }

        foreach (XmlElement cameraElement in ElementsByLocalName(root, "PerspectiveCamera"))
        {
            topic.Camera = ReadCamera(cameraElement, isPerspective: true);
            break;
        }

        if (topic.Camera == null)
        {
            foreach (XmlElement cameraElement in ElementsByLocalName(root, "OrthogonalCamera"))
            {
                topic.Camera = ReadCamera(cameraElement, isPerspective: false);
                break;
            }
        }
    }

    private static BcfCamera ReadCamera(XmlElement element, bool isPerspective)
    {
        var camera = new BcfCamera { IsPerspective = isPerspective };
        camera.Position = ReadXyz(element, "CameraViewPoint");
        camera.Direction = ReadXyz(element, "CameraDirection");
        camera.Up = ReadXyz(element, "CameraUpVector");
        if (isPerspective)
        {
            camera.FieldOfViewDegrees = ParseDouble(ChildText(element, "FieldOfView"));
        }
        else
        {
            camera.ViewToWorldScale = ParseDouble(ChildText(element, "ViewToWorldScale"));
        }

        return camera;
    }

    private static double[] ReadXyz(XmlElement parent, string childName)
    {
        foreach (XmlNode node in parent.ChildNodes)
        {
            var element = node as XmlElement;
            if (element != null && element.LocalName == childName)
            {
                return new[]
                {
                    ParseDouble(ChildText(element, "X")),
                    ParseDouble(ChildText(element, "Y")),
                    ParseDouble(ChildText(element, "Z")),
                };
            }
        }

        return new double[3];
    }

    private static XmlDocument LoadXml(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };
        var doc = new XmlDocument { XmlResolver = null };
        using (var reader = XmlReader.Create(stream, settings))
        {
            doc.Load(reader);
        }

        return doc;
    }

    private static IEnumerable<XmlElement> ElementsByLocalName(XmlElement root, string localName)
    {
        foreach (XmlNode node in root.GetElementsByTagName("*"))
        {
            var element = node as XmlElement;
            if (element != null && element.LocalName == localName)
            {
                yield return element;
            }
        }
    }

    private static string? ChildText(XmlElement parent, string childName)
    {
        foreach (XmlNode node in parent.ChildNodes)
        {
            var element = node as XmlElement;
            if (element != null && element.LocalName == childName)
            {
                return element.InnerText;
            }
        }

        return null;
    }

    private static string FirstNonEmpty(string value, string fallback)
    {
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    private static DateTime ParseDate(string? text)
    {
        if (!string.IsNullOrEmpty(text) &&
            DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return DateTime.UtcNow;
    }

    private static double ParseDouble(string? text)
    {
        if (!string.IsNullOrEmpty(text) &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0.0;
    }
}

/// <summary>
/// The standard IFC GUID compression: a 128-bit GUID as 22 characters of the
/// IFC base-64 alphabet (<c>0-9 A-Z a-z _ $</c>), most significant bits first
/// (the first character encodes only 2 bits). This is the format BCF
/// component references (<c>IfcGuid</c>) use.
/// </summary>
internal static class IfcGuidCodec
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    /// <summary>Encodes a GUID as its 22-character IFC form.</summary>
    internal static string Encode(Guid guid)
    {
        var bits = ToBigEndianBytes(guid);

        // 132 bits = 4 zero pad bits + 128 GUID bits, chunked MSB-first into
        // 22 six-bit groups.
        var chars = new char[22];
        int bitPosition = -4; // pad
        for (int i = 0; i < 22; i++)
        {
            int value = 0;
            for (int bit = 0; bit < 6; bit++, bitPosition++)
            {
                value <<= 1;
                if (bitPosition >= 0)
                {
                    int byteIndex = bitPosition >> 3;
                    int bitIndex = 7 - (bitPosition & 7);
                    value |= (bits[byteIndex] >> bitIndex) & 1;
                }
            }

            chars[i] = Alphabet[value];
        }

        return new string(chars);
    }

    /// <summary>
    /// Decodes a 22-character IFC GUID (or a plain GUID string) back to a
    /// <see cref="Guid"/>. Returns false for anything else.
    /// </summary>
    internal static bool TryDecode(string? text, out Guid guid)
    {
        guid = Guid.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var trimmed = text!.Trim();
        if (Guid.TryParse(trimmed, out guid))
        {
            return true;
        }

        if (trimmed.Length != 22)
        {
            return false;
        }

        var bits = new byte[16];
        int bitPosition = -4;
        foreach (var ch in trimmed)
        {
            int value = Alphabet.IndexOf(ch);
            if (value < 0)
            {
                return false;
            }

            for (int bit = 5; bit >= 0; bit--, bitPosition++)
            {
                if (bitPosition < 0)
                {
                    // The 4 pad bits must be zero (first char < '4' in the alphabet).
                    if (((value >> bit) & 1) != 0)
                    {
                        return false;
                    }

                    continue;
                }

                if (((value >> bit) & 1) != 0)
                {
                    int byteIndex = bitPosition >> 3;
                    int bitIndex = 7 - (bitPosition & 7);
                    bits[byteIndex] |= (byte)(1 << bitIndex);
                }
            }
        }

        guid = FromBigEndianBytes(bits);
        return true;
    }

    /// <summary>The GUID's 16 bytes in canonical big-endian (RFC 4122 text) order.</summary>
    private static byte[] ToBigEndianBytes(Guid guid)
    {
        var little = guid.ToByteArray(); // Data1..Data3 little-endian
        return new[]
        {
            little[3], little[2], little[1], little[0],
            little[5], little[4],
            little[7], little[6],
            little[8], little[9], little[10], little[11],
            little[12], little[13], little[14], little[15],
        };
    }

    private static Guid FromBigEndianBytes(byte[] big)
    {
        var little = new[]
        {
            big[3], big[2], big[1], big[0],
            big[5], big[4],
            big[7], big[6],
            big[8], big[9], big[10], big[11],
            big[12], big[13], big[14], big[15],
        };
        return new Guid(little);
    }
}

/// <summary>
/// Quaternion math for BCF camera export/import, on plain doubles so it can be
/// unit-tested headlessly. Navisworks <c>Rotation3D</c> is the quaternion
/// (A, B, C) = imaginary part, D = scalar part; the camera looks along the
/// rotated (0, 0, -1) with up = rotated (0, 1, 0).
/// </summary>
internal static class BcfCameraMath
{
    /// <summary>Rotates vector v by quaternion (a, b, c; d = scalar): v' = q·v·q⁻¹.</summary>
    internal static double[] Rotate(double a, double b, double c, double d, double vx, double vy, double vz)
    {
        // t = 2 q × v;  v' = v + d t + q × t
        double tx = 2.0 * (b * vz - c * vy);
        double ty = 2.0 * (c * vx - a * vz);
        double tz = 2.0 * (a * vy - b * vx);
        return new[]
        {
            vx + d * tx + (b * tz - c * ty),
            vy + d * ty + (c * tx - a * tz),
            vz + d * tz + (a * ty - b * tx),
        };
    }

    /// <summary>The camera view direction for a Navisworks rotation quaternion.</summary>
    internal static double[] ViewDirection(double a, double b, double c, double d)
    {
        return Rotate(a, b, c, d, 0.0, 0.0, -1.0);
    }

    /// <summary>The camera up vector for a Navisworks rotation quaternion.</summary>
    internal static double[] UpVector(double a, double b, double c, double d)
    {
        return Rotate(a, b, c, d, 0.0, 1.0, 0.0);
    }
}
