using System;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Portable;

/// <summary>One GUID the user asked for: the GUID itself and the text it was given as.</summary>
[IsVisibleInLibrary(false)]
public sealed class GuidRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="guid">The GUID to look for.</param>
    /// <param name="text">How the user wrote it (shown when it is not found).</param>
    public GuidRequest(Guid guid, string text)
    {
        Guid = guid;
        Text = text;
    }

    /// <summary>The GUID to look for.</summary>
    public Guid Guid { get; }

    /// <summary>How the user wrote it.</summary>
    public string Text { get; }
}

/// <summary>
/// The pure half of Search.ByGuid: reading the wired GUIDs (with the position of any bad one in the error) and
/// putting what one pass over the model found back in the order that was asked for, with the GUIDs that were not
/// found listed separately. Pure (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class GuidLookup
{
    /// <summary>
    /// Reads a list of GUIDs. Each entry may be a <see cref="Guid"/> or text: a GUID in any common layout or a
    /// 22-character IFC GlobalId (decoded).
    /// </summary>
    /// <param name="guids">The wired values.</param>
    /// <param name="nodeName">The node asking, for the error messages.</param>
    /// <returns>One request per entry, in order (duplicates kept).</returns>
    /// <exception cref="ArgumentNullException">The list is null.</exception>
    /// <exception cref="ArgumentException">An entry is empty or not a GUID; the message names its index.</exception>
    public static List<GuidRequest> ParseRequests(IList<object?> guids, string nodeName)
    {
        if (guids == null)
        {
            throw new ArgumentNullException(
                nameof(guids),
                nodeName + " requires the GUIDs to look for. Wire a list of GUID text into the 'guids' input.");
        }

        var requests = new List<GuidRequest>(guids.Count);
        for (int i = 0; i < guids.Count; i++)
        {
            var entry = guids[i];
            var position = "the GUID at index " + i.ToString(CultureInfo.InvariantCulture) + " (counting from 0)";
            switch (entry)
            {
                case null:
                    throw new ArgumentException(nodeName + ": " + position + " is empty.", nameof(guids));
                case Guid guid:
                    requests.Add(new GuidRequest(guid, guid.ToString("D", CultureInfo.InvariantCulture)));
                    break;
                case string text:
                    var trimmed = text.Trim();
                    if (IfcGuidCodec.TryDecode(trimmed, out var decoded))
                    {
                        requests.Add(new GuidRequest(decoded, trimmed));
                        break;
                    }

                    IfcGuidCodec.TryParseGuid(trimmed, out _, out var problem);
                    throw new ArgumentException(
                        nodeName + ": " + position + " is not a GUID: " + problem + ".",
                        nameof(guids));
                default:
                    throw new ArgumentException(
                        nodeName + ": " + position + " is a " + entry.GetType().Name + ", not text or a GUID.",
                        nameof(guids));
            }
        }

        return requests;
    }

    /// <summary>
    /// Puts the items found for each GUID in the order the GUIDs were asked for. A GUID asked for twice yields its
    /// items once; GUIDs with no item are returned in <paramref name="missing"/> (as the user wrote them).
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="requests">The GUIDs asked for, in order.</param>
    /// <param name="found">The items found, by GUID.</param>
    /// <param name="missing">The GUIDs (as written) for which nothing was found, in order, without repeats.</param>
    /// <returns>The items found, in the order of the requests.</returns>
    public static List<T> Collect<T>(IList<GuidRequest> requests, IDictionary<Guid, List<T>> found, out List<string> missing)
    {
        if (requests == null)
        {
            throw new ArgumentNullException(nameof(requests));
        }

        if (found == null)
        {
            throw new ArgumentNullException(nameof(found));
        }

        var items = new List<T>();
        missing = new List<string>();
        var done = new HashSet<Guid>();
        foreach (var request in requests)
        {
            if (!done.Add(request.Guid))
            {
                continue;
            }

            if (found.TryGetValue(request.Guid, out var matches) && matches.Count > 0)
            {
                items.AddRange(matches);
            }
            else
            {
                missing.Add(request.Text);
            }
        }

        return items;
    }
}

/// <summary>
/// The pure half of ModelItem.IfcGuid: which property names carry an IFC GlobalId, in order of preference, and how a
/// property value is turned into a 22-character GlobalId. Pure (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class IfcGuidLookup
{
    /// <summary>
    /// How well a property name says "this is the IFC GlobalId": 1 for GlobalId / IfcGlobalId, 2 for IfcGUID /
    /// IFC GUID, 3 for a plain Guid, 0 for any other name. Case, spaces, underscores, hyphens and dots are ignored.
    /// </summary>
    /// <param name="propertyName">The property's display name or internal name.</param>
    /// <returns>The rank; a lower number is a better match, 0 is no match.</returns>
    public static int Rank(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return 0;
        }

        var buffer = new System.Text.StringBuilder(propertyName!.Length);
        foreach (var ch in propertyName)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer.Append(char.ToLowerInvariant(ch));
            }
        }

        switch (buffer.ToString())
        {
            case "globalid":
            case "ifcglobalid":
                return 1;
            case "ifcguid":
                return 2;
            case "guid":
                return 3;
            default:
                return 0;
        }
    }

    /// <summary>
    /// Turns a property value into an IFC GlobalId: a valid 22-character GlobalId is returned as it is, and a
    /// standard GUID is encoded. Anything else is not an IFC identity.
    /// </summary>
    /// <param name="value">The property value.</param>
    /// <param name="globalId">The 22-character GlobalId, or null.</param>
    /// <returns>True when the value is a GlobalId or a GUID.</returns>
    public static bool TryNormalize(string? value, out string? globalId)
    {
        globalId = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value!.Trim();
        if (IfcGuidCodec.IsGlobalId(trimmed))
        {
            globalId = trimmed;
            return true;
        }

        if (Guid.TryParse(trimmed, out var guid) && guid != Guid.Empty)
        {
            globalId = IfcGuidCodec.Encode(guid);
            return true;
        }

        return false;
    }
}
