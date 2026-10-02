using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Dyncamelo.Nodes.Internal;

/// <summary>
/// Regular expressions built once and reused. A node that runs per item is called once for every item of a list (200 000 times for a
/// big Search), and <c>new Regex(pattern)</c> parses the pattern and builds the matcher on every call: several microseconds and
/// about 3 KB each, so a filter over a large list spent more time building the same expression than matching with it. The patterns
/// of one run repeat, so they are remembered. A <see cref="Regex"/> is immutable and safe to share between threads.
/// </summary>
internal static class RegexCache
{
    // The few patterns one graph uses; when a graph churns through more (a pattern per item) the cache is simply started again.
    private const int Capacity = 128;

    private static readonly ConcurrentDictionary<(string Pattern, RegexOptions Options, long TimeoutTicks), Regex> Cache =
        new ConcurrentDictionary<(string, RegexOptions, long), Regex>();

    /// <summary>The expression for a pattern, building it on first use. An invalid pattern throws <see cref="ArgumentException"/> every time (nothing is cached).</summary>
    /// <param name="pattern">The regular expression.</param>
    /// <param name="options">Regex options.</param>
    /// <param name="timeout">How long one match may take.</param>
    internal static Regex Get(string pattern, RegexOptions options, TimeSpan timeout)
    {
        var key = (pattern, options, timeout.Ticks);
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var created = new Regex(pattern, options, timeout);
        if (Cache.Count >= Capacity)
        {
            Cache.Clear();
        }

        Cache[key] = created;
        return created;
    }
}
