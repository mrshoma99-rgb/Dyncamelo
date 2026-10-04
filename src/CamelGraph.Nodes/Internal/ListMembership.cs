using System;
using System.Collections;
using System.Collections.Generic;

namespace CamelGraph.Nodes.Internal;

/// <summary>
/// The <c>in</c> / <c>notIn</c> tests of <c>List.FilterByValue</c>: is the value one of several allowed values? The allowed values
/// are the items of a list, or the pieces of a text separated by commas or semicolons ("L01, L02, L03"); a single value of any
/// other kind is the only member. Members and subjects compare as <c>==</c> does: numbers by value, text that reads as a number
/// against a number as a number, text with or without regard to case.
/// </summary>
internal sealed class ListMembership
{
    private readonly HashSet<string> _texts;
    private readonly List<object?> _others;
    private readonly bool _ignoreCase;

    private ListMembership(bool ignoreCase)
    {
        _ignoreCase = ignoreCase;
        _texts = new HashSet<string>(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        _others = new List<object?>();
    }

    /// <summary>True when the test name is the membership test (<c>in</c>) or its opposite (<c>notIn</c>, also written <c>!in</c> or <c>not in</c>).</summary>
    /// <param name="test">The test as chosen or typed.</param>
    /// <param name="negate">True for the opposite.</param>
    internal static bool TryParse(string? test, out bool negate)
    {
        negate = false;
        var key = (test ?? string.Empty).Trim().Replace(" ", string.Empty).ToLowerInvariant();
        switch (key)
        {
            case "in":
                return true;
            case "notin":
            case "!in":
                negate = true;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Collects the allowed values from the value wired to the node.</summary>
    /// <param name="value">A list of values, a text with values separated by commas or semicolons, any other single value, or null (nothing is allowed).</param>
    /// <param name="ignoreCase">True to ignore upper and lower case in text.</param>
    internal static ListMembership Members(object? value, bool ignoreCase)
    {
        var members = new ListMembership(ignoreCase);
        if (value is string text)
        {
            foreach (var piece in text.Split(',', ';'))
            {
                var trimmed = piece.Trim();
                if (trimmed.Length > 0)
                {
                    members.Add(trimmed);
                }
            }
        }
        else if (value is IList list && !(value is IDictionary))
        {
            foreach (var item in list)
            {
                members.Add(item);
            }
        }
        else if (value != null)
        {
            members.Add(value);
        }

        return members;
    }

    /// <summary>Whether the subject equals one of the members.</summary>
    /// <param name="subject">The value to look for.</param>
    internal bool Contains(object? subject)
    {
        if (subject is string text && _texts.Contains(text))
        {
            return true;
        }

        if (_others.Count == 0 && (subject is string || subject == null))
        {
            return false;
        }

        // A number against a member that is text that reads as a number (and the other way round), a date, a list...
        foreach (var member in _others)
        {
            if (ValueTests.AreEqual(subject, member, _ignoreCase))
            {
                return true;
            }
        }

        if (!(subject is string) && subject != null)
        {
            foreach (var member in _texts)
            {
                if (ValueTests.AreEqual(subject, member, _ignoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void Add(object? item)
    {
        if (item is string text)
        {
            _texts.Add(text);
        }
        else
        {
            _others.Add(item);
        }
    }
}
