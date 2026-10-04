using System;
using System.Globalization;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes;

/// <summary>
/// The standard IFC GUID compression: a 128-bit GUID as 22 characters of the
/// IFC base-64 alphabet (<c>0-9 A-Z a-z _ $</c>), most significant bits first
/// (the first character encodes only 2 bits). This is the form of an IFC
/// <c>GlobalId</c> and of the BCF component references (<c>IfcGuid</c>).
/// Pure .NET, shared by the IFC nodes and the BCF nodes of the Navisworks library.
/// </summary>
[IsVisibleInLibrary(false)]
public static class IfcGuidCodec
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    /// <summary>The length of an IFC GlobalId.</summary>
    public const int GlobalIdLength = 22;

    /// <summary>Encodes a GUID as its 22-character IFC form.</summary>
    /// <param name="guid">The GUID.</param>
    /// <returns>The 22-character IFC GlobalId.</returns>
    public static string Encode(Guid guid)
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
    /// <param name="text">The IFC GlobalId, or a GUID in any format <see cref="Guid.TryParse(string, out Guid)"/> reads.</param>
    /// <param name="guid">The GUID (empty when the method returns false).</param>
    /// <returns>True when the text was read.</returns>
    public static bool TryDecode(string? text, out Guid guid)
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

        return TryDecodeCore(trimmed, out guid, out _);
    }

    /// <summary>
    /// Decodes strictly a 22-character IFC GlobalId (a plain GUID is NOT accepted) and says what is wrong
    /// when it cannot be read.
    /// </summary>
    /// <param name="text">The IFC GlobalId (surrounding spaces are ignored).</param>
    /// <param name="guid">The GUID (empty when the method returns false).</param>
    /// <param name="problem">What is wrong with the text, or null when it was read.</param>
    /// <returns>True when the text is a valid GlobalId.</returns>
    public static bool TryDecodeGlobalId(string? text, out Guid guid, out string? problem)
    {
        guid = Guid.Empty;
        if (text == null)
        {
            problem = "no IFC GlobalId was given";
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            problem = "the IFC GlobalId is empty";
            return false;
        }

        if (TryDecodeCore(trimmed, out guid, out problem))
        {
            return true;
        }

        if (trimmed.Length != GlobalIdLength && Guid.TryParse(trimmed, out _))
        {
            problem = "'" + trimmed + "' is a standard GUID, not an IFC GlobalId; use IFC.GuidEncode to convert it";
        }

        return false;
    }

    /// <summary>
    /// Reads a standard GUID (hyphenated, hyphen-less, or in braces) and says what is wrong when it cannot be read.
    /// </summary>
    /// <param name="text">The GUID text (surrounding spaces are ignored).</param>
    /// <param name="guid">The GUID (empty when the method returns false).</param>
    /// <param name="problem">What is wrong with the text, or null when it was read.</param>
    /// <returns>True when the text is a valid GUID.</returns>
    public static bool TryParseGuid(string? text, out Guid guid, out string? problem)
    {
        guid = Guid.Empty;
        if (text == null)
        {
            problem = "no GUID was given";
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            problem = "the GUID is empty";
            return false;
        }

        if (Guid.TryParse(trimmed, out guid))
        {
            problem = null;
            return true;
        }

        problem = DescribeGuidProblem(trimmed);
        return false;
    }

    /// <summary>True when the text is a valid 22-character IFC GlobalId (surrounding spaces are ignored).</summary>
    /// <param name="text">The text to test.</param>
    /// <returns>True for a GlobalId.</returns>
    public static bool IsGlobalId(string? text)
    {
        return text != null && TryDecodeCore(text.Trim(), out _, out _);
    }

    private static bool TryDecodeCore(string trimmed, out Guid guid, out string? problem)
    {
        guid = Guid.Empty;
        if (trimmed.Length != GlobalIdLength)
        {
            problem = "'" + trimmed + "' has " + trimmed.Length.ToString(CultureInfo.InvariantCulture) +
                      " character(s); an IFC GlobalId has exactly 22";
            return false;
        }

        var bits = new byte[16];
        int bitPosition = -4;
        for (int index = 0; index < trimmed.Length; index++)
        {
            var ch = trimmed[index];
            int value = Alphabet.IndexOf(ch);
            if (value < 0)
            {
                problem = "'" + trimmed + "' contains the character '" + ch + "' at position " +
                          (index + 1).ToString(CultureInfo.InvariantCulture) +
                          ", which is not in the IFC alphabet (0-9, A-Z, a-z, _ and $)";
                return false;
            }

            for (int bit = 5; bit >= 0; bit--, bitPosition++)
            {
                if (bitPosition < 0)
                {
                    // The 4 pad bits must be zero (first char < '4' in the alphabet).
                    if (((value >> bit) & 1) != 0)
                    {
                        problem = "'" + trimmed + "' starts with '" + ch +
                                  "'; the first character of an IFC GlobalId can only be 0, 1, 2 or 3 (it carries just 2 bits)";
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
        problem = null;
        return true;
    }

    /// <summary>Explains why a text is not a GUID (wrong length, a character that is not hexadecimal, ...).</summary>
    private static string DescribeGuidProblem(string trimmed)
    {
        if (trimmed.Length == GlobalIdLength && IsGlobalId(trimmed))
        {
            return "'" + trimmed + "' is already an IFC GlobalId (22 characters); use IFC.GuidDecode to get the GUID";
        }

        var digits = trimmed;
        if (digits.Length >= 2 && digits[0] == '{' && digits[digits.Length - 1] == '}')
        {
            digits = digits.Substring(1, digits.Length - 2);
        }

        var hex = new System.Text.StringBuilder(32);
        foreach (var ch in digits)
        {
            if (ch == '-')
            {
                continue;
            }

            if (!Uri.IsHexDigit(ch))
            {
                return "'" + trimmed + "' contains the character '" + ch +
                       "', which is not a hexadecimal digit (0-9, a-f)";
            }

            hex.Append(ch);
        }

        if (hex.Length != 32)
        {
            return "'" + trimmed + "' has " + hex.Length.ToString(CultureInfo.InvariantCulture) +
                   " hexadecimal digit(s); a GUID has 32 (written 8-4-4-4-12 with hyphens)";
        }

        return "'" + trimmed + "' is not laid out like a GUID (8-4-4-4-12 hexadecimal digits)";
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
/// IFC identity nodes: convert between a standard GUID and the 22-character IFC GlobalId (the compressed base-64 form
/// every IFC file, BCF topic and COBie sheet uses). Pure text conversions — no model needed.
/// </summary>
[NodeCategory("IFC")]
public static class IfcGuidNodes
{
    /// <summary>Converts a standard GUID to the 22-character IFC GlobalId.</summary>
    /// <param name="guid">The GUID, e.g. "3f81e10a-25b0-49ff-9520-63f2a763150a" (with or without hyphens or braces).</param>
    /// <returns>The IFC GlobalId, e.g. "0$WU4A9R19$vKWO$AdOnKA".</returns>
    [NodeName("IFC.GuidEncode")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Create)]
    [return: NodeName("globalId")]
    [NodeDescription("Converts a standard GUID such as \"3f81e10a-25b0-49ff-9520-63f2a763150a\" to the 22-character IFC GlobalId (the IFC base-64 form, alphabet 0-9 A-Z a-z _ $) — the format of IFC files and BCF references. A GlobalId is refused here; for a column that mixes both forms use IFC.Normalize.")]
    [NodeSearchTags("ifc", "guid", "globalid", "uuid", "encode", "compress", "base64", "22")]
    public static string GuidEncode(string guid)
    {
        if (guid == null)
        {
            throw new ArgumentNullException(
                nameof(guid),
                "IFC.GuidEncode requires a GUID such as \"3f81e10a-25b0-49ff-9520-63f2a763150a\". Wire text into the 'guid' input.");
        }

        if (!IfcGuidCodec.TryParseGuid(guid, out var parsed, out var problem))
        {
            throw new ArgumentException("IFC.GuidEncode: " + problem + ".", nameof(guid));
        }

        return IfcGuidCodec.Encode(parsed);
    }

    /// <summary>Converts a 22-character IFC GlobalId to a standard GUID.</summary>
    /// <param name="globalId">The IFC GlobalId, e.g. "0$WU4A9R19$vKWO$AdOnKA".</param>
    /// <returns>The GUID in lower-case hyphenated form, e.g. "3f81e10a-25b0-49ff-9520-63f2a763150a".</returns>
    [NodeName("IFC.GuidDecode")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Create)]
    [return: NodeName("guid")]
    [NodeDescription("Converts a 22-character IFC GlobalId such as \"0$WU4A9R19$vKWO$AdOnKA\" back to a standard lower-case hyphenated GUID — the form Navisworks shows as an item's GUID. A plain GUID is refused here; for a column that mixes both forms use IFC.Normalize.")]
    [NodeSearchTags("ifc", "guid", "globalid", "uuid", "decode", "expand", "base64", "22")]
    public static string GuidDecode(string globalId)
    {
        if (globalId == null)
        {
            throw new ArgumentNullException(
                nameof(globalId),
                "IFC.GuidDecode requires an IFC GlobalId such as \"0$WU4A9R19$vKWO$AdOnKA\". Wire text into the 'globalId' input.");
        }

        if (!IfcGuidCodec.TryDecodeGlobalId(globalId, out var guid, out var problem))
        {
            throw new ArgumentException("IFC.GuidDecode: " + problem + ".", nameof(globalId));
        }

        return guid.ToString("D", CultureInfo.InvariantCulture);
    }

    /// <summary>Tells whether a text is a 22-character IFC GlobalId, so a column that mixes GlobalIds and plain GUIDs can be split.</summary>
    /// <param name="text">The text to test; a blank or missing text is simply not a GlobalId. Spaces around it are ignored.</param>
    /// <returns>True for a valid 22-character IFC GlobalId; false for a plain GUID, any other text, or nothing.</returns>
    [NodeName("IFC.IsGlobalId")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("isGlobalId")]
    [NodeDescription("True when a text is a 22-character IFC GlobalId (alphabet 0-9 A-Z a-z _ $, first character 0 to 3) and false for a plain GUID, any other text or an empty cell, so a column that mixes both forms can be split before IFC.GuidDecode or IFC.GuidEncode. A list gives one answer per item, empty cells included.")]
    [NodeSearchTags("ifc", "globalid", "guid", "uuid", "is", "check", "validate", "22", "test", "format", "which")]
    public static bool IsGlobalId([AcceptsNull] string? text)
    {
        return IfcGuidCodec.IsGlobalId(text);
    }

    /// <summary>
    /// Writes an id in one form whichever form it arrives in: a 22-character IFC GlobalId or a standard GUID
    /// (hyphenated, hyphen-less, or in braces) becomes a GlobalId or a lower-case hyphenated GUID.
    /// </summary>
    /// <param name="id">The id, in either form. Spaces around it are ignored.</param>
    /// <param name="form">The form to write: "globalId" (22 characters, the default) or "guid" (lower-case, hyphenated).</param>
    /// <returns>The id in the requested form.</returns>
    [NodeName("IFC.Normalize")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Create)]
    [return: NodeName("id")]
    [NodeDescription("Writes an id in one form whichever form it comes in: a 22-character IFC GlobalId or a standard GUID (with hyphens, without, or in braces) becomes the form you choose, a GlobalId (default) or a lower-case hyphenated GUID. Use it on a column that mixes both forms, where IFC.GuidEncode and IFC.GuidDecode would each refuse the other form. A text that is neither is an error naming the text (in a list: an empty result and one warning).")]
    [NodeSearchTags("ifc", "guid", "globalid", "uuid", "normalize", "normalise", "convert", "either", "mixed", "tolerant", "clean", "canonical", "22")]
    public static string Normalize(string id, [NodeChoices("globalId", "guid")] string form = "globalId")
    {
        var toGuid = ReadForm(form);
        if (id == null)
        {
            throw new ArgumentNullException(
                nameof(id),
                "IFC.Normalize requires an IFC GlobalId or a GUID. Wire text into the 'id' input.");
        }

        var trimmed = id.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("IFC.Normalize: the id is empty. Wire an IFC GlobalId or a GUID into the 'id' input.", nameof(id));
        }

        if (!IfcGuidCodec.TryDecode(trimmed, out var guid))
        {
            // Say what is wrong in the terms of the form the text looks like: 22 characters means a GlobalId was meant.
            string? problem;
            if (trimmed.Length == IfcGuidCodec.GlobalIdLength)
            {
                IfcGuidCodec.TryDecodeGlobalId(trimmed, out _, out problem);
            }
            else
            {
                IfcGuidCodec.TryParseGuid(trimmed, out _, out problem);
            }

            throw new ArgumentException(
                "IFC.Normalize: " + (problem ?? "'" + trimmed + "' is neither an IFC GlobalId nor a GUID") +
                ". Wire an IFC GlobalId (22 characters) or a GUID (32 hexadecimal digits).",
                nameof(id));
        }

        return toGuid
            ? guid.ToString("D", CultureInfo.InvariantCulture)
            : IfcGuidCodec.Encode(guid);
    }

    private static bool ReadForm(string? form)
    {
        var text = (form ?? string.Empty).Trim();
        if (text.Length == 0 || text.Equals("globalId", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Equals("guid", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new ArgumentException("IFC.Normalize: form must be 'globalId' or 'guid', not '" + text + "'.", nameof(form));
    }
}
