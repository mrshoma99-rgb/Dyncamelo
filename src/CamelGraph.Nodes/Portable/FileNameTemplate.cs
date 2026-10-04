using System;
using System.Text;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// The <c>{name}</c> placeholder of a picture file path: <c>C:\out\{name}.png</c> becomes <c>C:\out\Level 1.png</c> for a viewpoint
/// called "Level 1", so one node can write a picture per viewpoint of a list. The name is made safe for a file name first.
/// Pure (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class FileNameTemplate
{
    /// <summary>The placeholder that stands for the viewpoint's name.</summary>
    public const string NameToken = "{name}";

    /// <summary>Whether the path holds the <c>{name}</c> placeholder.</summary>
    /// <param name="path">The file path.</param>
    public static bool HasNameToken(string? path)
    {
        return path != null && path.IndexOf(NameToken, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Replaces <c>{name}</c> (any case) in a path by the name, made safe for a file name.</summary>
    /// <param name="path">The file path with the placeholder.</param>
    /// <param name="name">The name to put in (a viewpoint's display name).</param>
    /// <returns>The path with the name in it; the path itself when it has no placeholder.</returns>
    public static string Apply(string path, string? name)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (!HasNameToken(path))
        {
            return path;
        }

        var safe = Sanitize(name);
        var builder = new StringBuilder(path.Length + safe.Length);
        var i = 0;
        while (i < path.Length)
        {
            if (string.Compare(path, i, NameToken, 0, NameToken.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                builder.Append(safe);
                i += NameToken.Length;
            }
            else
            {
                builder.Append(path[i]);
                i++;
            }
        }

        return builder.ToString();
    }

    /// <summary>A name as a file name: the characters Windows does not allow become "_", the ends are trimmed, an empty name becomes "viewpoint".</summary>
    /// <param name="name">The name.</param>
    public static string Sanitize(string? name)
    {
        var builder = new StringBuilder();
        foreach (var c in name ?? string.Empty)
        {
            builder.Append(c < ' ' || "\\/:*?\"<>|".IndexOf(c) >= 0 ? '_' : c);
        }

        var text = builder.ToString().Trim().TrimEnd('.');
        return text.Length == 0 ? "viewpoint" : text;
    }
}
