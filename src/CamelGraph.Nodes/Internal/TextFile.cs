using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CamelGraph.Core.Execution;

namespace CamelGraph.Nodes;

/// <summary>The text encodings the file nodes offer (a dropdown under "Advanced").</summary>
internal enum FileEncoding
{
    /// <summary>Reading only: a byte-order mark decides, else UTF-8 when the bytes are valid UTF-8, else Windows-1252.</summary>
    Auto,

    /// <summary>UTF-8 without a byte-order mark when writing; a mark in a file being read is skipped.</summary>
    Utf8,

    /// <summary>Writing only: UTF-8 with a byte-order mark, which is what makes Excel open a CSV as UTF-8.</summary>
    Utf8Bom,

    /// <summary>The Western European "ANSI" code page that Windows programs and European Excel exports use.</summary>
    Windows1252,

    /// <summary>UTF-16 little endian with a byte-order mark ("Unicode" in Windows programs).</summary>
    Utf16,
}

/// <summary>
/// Reads and writes text files in the encodings the file nodes offer. The Windows-1252 code page is built in here because the
/// library targets netstandard2.0, where the code-page encodings are not available without a host-registered provider.
/// </summary>
internal static class TextFile
{
    /// <summary>The choices of a reading node's encoding dropdown, in the order shown.</summary>
    internal const string ReaderChoicesText = "auto, UTF-8, Windows-1252, UTF-16";

    // The Windows-1252 characters 0x80-0x9F (the rest of the table is the same as Latin-1). 0 marks the five unassigned positions.
    private static readonly char[] Cp1252High =
    {
        '€', '\u0081', '‚', 'ƒ', '„', '…', '†', '‡',
        'ˆ', '‰', 'Š', '‹', 'Œ', '\u008D', 'Ž', '\u008F',
        '\u0090', '‘', '’', '“', '”', '•', '–', '—',
        '˜', '™', 'š', '›', 'œ', '\u009D', 'ž', 'Ÿ',
    };

    /// <summary>Understands the name chosen in a reading node's dropdown; blank means auto.</summary>
    internal static FileEncoding ParseForReading(string? name, string nodeName)
    {
        var key = Normalize(name);
        switch (key)
        {
            case "":
            case "auto":
            case "detect":
            case "automatic":
                return FileEncoding.Auto;
            case "utf8":
            case "utf8bom":
            case "utf8withbom":
                return FileEncoding.Utf8;
            case "windows1252":
            case "cp1252":
            case "1252":
            case "ansi":
            case "latin1":
                return FileEncoding.Windows1252;
            case "utf16":
            case "utf16le":
            case "unicode":
                return FileEncoding.Utf16;
            default:
                throw new ArgumentException(
                    nodeName + ": the encoding '" + name + "' is not one of the choices (" + ReaderChoicesText + "). Pick one from the 'encoding' list.",
                    "encoding");
        }
    }

    /// <summary>Understands the name chosen in a writing node's dropdown; blank means UTF-8 without a mark.</summary>
    internal static FileEncoding ParseForWriting(string? name, string nodeName)
    {
        var key = Normalize(name);
        switch (key)
        {
            case "":
            case "utf8":
                return FileEncoding.Utf8;
            case "utf8bom":
            case "utf8withbom":
            case "bom":
                return FileEncoding.Utf8Bom;
            case "windows1252":
            case "cp1252":
            case "1252":
            case "ansi":
            case "latin1":
                return FileEncoding.Windows1252;
            case "utf16":
            case "utf16le":
            case "unicode":
                return FileEncoding.Utf16;
            default:
                throw new ArgumentException(
                    nodeName + ": the encoding '" + name + "' is not one of the choices (UTF-8, UTF-8 with BOM, Windows-1252, UTF-16). Pick one from the 'encoding' list.",
                    "encoding");
        }
    }

    /// <summary>Reads a whole text file.</summary>
    internal static string ReadAllText(string path, FileEncoding encoding)
    {
        return Decode(File.ReadAllBytes(path), encoding);
    }

    /// <summary>Turns the bytes of a file into text.</summary>
    internal static string Decode(byte[] bytes, FileEncoding encoding)
    {
        switch (encoding)
        {
            case FileEncoding.Windows1252:
                return DecodeWindows1252(bytes, 0);
            case FileEncoding.Utf16:
                return DecodeUtf16(bytes);
            case FileEncoding.Utf8:
            case FileEncoding.Utf8Bom:
                return new UTF8Encoding(false).GetString(bytes, Utf8BomLength(bytes), bytes.Length - Utf8BomLength(bytes));
            default:
                return DecodeAuto(bytes);
        }
    }

    /// <summary>Writes a whole text file, replacing what was there.</summary>
    internal static void WriteAllText(string path, string text, FileEncoding encoding)
    {
        File.WriteAllBytes(path, Encode(text, encoding, withMark: true, FileNodesWarnings.Cp1252Loss));
    }

    /// <summary>Appends text to a file; the byte-order mark of the encoding is written only when the file is new or empty.</summary>
    internal static void AppendAllText(string path, string text, FileEncoding encoding)
    {
        var fresh = !File.Exists(path) || new FileInfo(path).Length == 0;
        var bytes = Encode(text, encoding, fresh, FileNodesWarnings.Cp1252Loss);
        using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    /// <summary>
    /// The bytes of a text in an encoding. <paramref name="withMark"/> adds the byte-order mark of encodings that have one (UTF-8 with
    /// BOM, UTF-16); <paramref name="onUnmappable"/> hears how many characters Windows-1252 could not hold (they become "?").
    /// </summary>
    internal static byte[] Encode(string text, FileEncoding encoding, bool withMark, Action<int>? onUnmappable = null)
    {
        text = text ?? string.Empty;
        switch (encoding)
        {
            case FileEncoding.Windows1252:
                return EncodeWindows1252(text, onUnmappable);
            case FileEncoding.Utf16:
            {
                var body = Encoding.Unicode.GetBytes(text);
                return withMark ? Concat(new byte[] { 0xFF, 0xFE }, body) : body;
            }

            case FileEncoding.Utf8Bom:
            {
                var body = new UTF8Encoding(false).GetBytes(text);
                return withMark ? Concat(new byte[] { 0xEF, 0xBB, 0xBF }, body) : body;
            }

            default:
                return new UTF8Encoding(false).GetBytes(text);
        }
    }

    private static string DecodeAuto(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            return DecodeUtf16(bytes);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8: the file was saved by a program that writes the Windows "ANSI" code page (Excel's "CSV (Comma delimited)").
            return DecodeWindows1252(bytes, 0);
        }
    }

    private static int Utf8BomLength(byte[] bytes)
    {
        return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
    }

    private static string DecodeUtf16(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        var skip = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE ? 2 : 0;
        return Encoding.Unicode.GetString(bytes, skip, bytes.Length - skip);
    }

    private static string DecodeWindows1252(byte[] bytes, int start)
    {
        var chars = new char[bytes.Length - start];
        for (var i = start; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i - start] = b >= 0x80 && b <= 0x9F ? Cp1252High[b - 0x80] : (char)b;
        }

        return new string(chars);
    }

    private static byte[] EncodeWindows1252(string text, Action<int>? onUnmappable)
    {
        var bytes = new List<byte>(text.Length);
        var lost = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c < 0x80 || (c >= 0xA0 && c <= 0xFF))
            {
                bytes.Add((byte)c);
                continue;
            }

            var index = Array.IndexOf(Cp1252High, c);
            if (index >= 0 && c != '\u0081' && c != '\u008D' && c != '\u008F' && c != '\u0090' && c != '\u009D')
            {
                bytes.Add((byte)(0x80 + index));
                continue;
            }

            lost++;
            bytes.Add((byte)'?');
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++; // one picture is one lost character, not two
            }
        }

        if (lost > 0)
        {
            onUnmappable?.Invoke(lost);
        }

        return bytes.ToArray();
    }

    private static byte[] Concat(byte[] first, byte[] second)
    {
        var all = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, all, 0, first.Length);
        Buffer.BlockCopy(second, 0, all, first.Length, second.Length);
        return all;
    }

    private static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name!.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}

/// <summary>The warnings the file nodes raise while they work (a no-op outside a graph run).</summary>
internal static class FileNodesWarnings
{
    /// <summary>Some characters do not exist in Windows-1252 and were written as "?".</summary>
    internal static void Cp1252Loss(int count)
    {
        NodeWarnings.Add(
            count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            (count == 1 ? " character does" : " characters do") +
            " not exist in Windows-1252 and " + (count == 1 ? "was" : "were") +
            " written as \"?\". Choose UTF-8 or UTF-16 under Advanced > encoding to keep them.");
    }
}
