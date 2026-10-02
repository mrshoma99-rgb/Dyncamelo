using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Dyncamelo.Core.Editing;

/// <summary>The SHA-256 of a file's bytes, in hex: what "the user agreed to run this file as it is" is remembered against.</summary>
public static class FileFingerprint
{
    /// <summary>Hashes bytes.</summary>
    /// <param name="bytes">The content.</param>
    public static string Of(byte[] bytes)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        using (var sha = SHA256.Create())
        {
            return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>Hashes a file.</summary>
    /// <param name="path">The file.</param>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static string OfFile(string path) => Of(File.ReadAllBytes(path));
}
