using System;
using System.Collections.Generic;
using System.Linq;

namespace Dyncamelo.Core.Editing;

/// <summary>Ranks catalogue commands against what the user typed into the command palette.</summary>
public static class CommandSearch
{
    /// <summary>
    /// Commands matching every word of <paramref name="query"/>, best first: a title that starts with the text,
    /// then a title word that does, then any part of the title, then the category or keywords. An empty query lists
    /// everything in catalogue order.
    /// </summary>
    /// <param name="query">What was typed.</param>
    /// <param name="commands">The commands to search (usually those that can run right now).</param>
    /// <param name="maxResults">Cap on the list.</param>
    public static IReadOnlyList<CommandInfo> Rank(string? query, IEnumerable<CommandInfo> commands, int maxResults = 60)
    {
        var words = (query ?? string.Empty)
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .ToArray();
        var all = commands.ToList();
        if (words.Length == 0)
        {
            return all.Take(maxResults).ToList();
        }

        var scored = new List<(int Score, int Order, CommandInfo Info)>();
        for (var i = 0; i < all.Count; i++)
        {
            var total = 0;
            var ok = true;
            foreach (var word in words)
            {
                var score = ScoreWord(word, all[i]);
                if (score < 0)
                {
                    ok = false;
                    break;
                }

                total += score;
            }

            if (ok)
            {
                scored.Add((total, i, all[i]));
            }
        }

        return scored.OrderBy(s => s.Score).ThenBy(s => s.Order).Take(maxResults).Select(s => s.Info).ToList();
    }

    private static int ScoreWord(string word, CommandInfo info)
    {
        var title = info.Title.ToLowerInvariant();
        if (title.StartsWith(word, StringComparison.Ordinal))
        {
            return 0;
        }

        if (title.Split(' ', '/', '-', '&').Any(t => t.StartsWith(word, StringComparison.Ordinal)))
        {
            return 1;
        }

        if (title.Contains(word))
        {
            return 2;
        }

        if (info.Category.ToLowerInvariant().StartsWith(word, StringComparison.Ordinal) ||
            info.Keywords.ToLowerInvariant().Split(' ').Any(k => k.StartsWith(word, StringComparison.Ordinal)))
        {
            return 3;
        }

        return info.Keywords.ToLowerInvariant().Contains(word) ? 4 : -1;
    }
}
