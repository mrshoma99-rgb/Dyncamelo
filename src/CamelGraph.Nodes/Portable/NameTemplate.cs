using System;
using System.Collections.Generic;
using System.Text;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// Fills in a name format such as <c>{test} - {name}</c>: each <c>{token}</c> is replaced by the value of that token, ignoring
/// case. Used by the nodes that name saved items after clash results (<c>nameFormat</c>). An unknown token is an error that
/// lists the tokens the node knows, so a typo is not silently written into every name. Pure (no Navisworks types), so it is
/// unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class NameTemplate
{
    /// <summary>Whether the text holds at least one <c>{token}</c>.</summary>
    /// <param name="format">The format text.</param>
    public static bool HasTokens(string? format)
    {
        if (string.IsNullOrEmpty(format))
        {
            return false;
        }

        var open = format!.IndexOf('{');
        return open >= 0 && format.IndexOf('}', open + 1) > open;
    }

    /// <summary>Replaces the tokens of a format.</summary>
    /// <param name="format">The format, for example <c>{test} - {name}</c>.</param>
    /// <param name="values">The value of each token, by name (compared ignoring case).</param>
    /// <param name="parameterName">The input's name, for the error message.</param>
    /// <returns>The text with every token replaced.</returns>
    public static string Apply(string format, IReadOnlyDictionary<string, string> values, string parameterName)
    {
        if (format == null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        if (values == null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            lookup[pair.Key] = pair.Value ?? string.Empty;
        }

        var builder = new StringBuilder(format.Length + 16);
        var i = 0;
        while (i < format.Length)
        {
            var c = format[i];
            if (c != '{')
            {
                builder.Append(c);
                i++;
                continue;
            }

            var close = format.IndexOf('}', i + 1);
            if (close < 0)
            {
                builder.Append(c);
                i++;
                continue;
            }

            var token = format.Substring(i + 1, close - i - 1).Trim();
            if (!lookup.TryGetValue(token, out var value))
            {
                var known = new List<string>();
                foreach (var key in values.Keys)
                {
                    known.Add("{" + key + "}");
                }

                throw new ArgumentException(
                    "'{" + token + "}' in " + parameterName + " is not a known token. Use " + string.Join(", ", known) + ".",
                    parameterName);
            }

            builder.Append(value);
            i = close + 1;
        }

        return builder.ToString();
    }
}
