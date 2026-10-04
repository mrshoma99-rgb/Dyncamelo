using System;
using System.Collections.Generic;
using System.Threading;

namespace CamelGraph.Core.Execution;

/// <summary>
/// The warning channel of a zero-touch node. A node that finished but wants to say "this is not quite what you asked for" calls
/// <see cref="Add"/> and returns its result as usual: the node shows the amber <c>Warning</c> badge with the message and its
/// outputs stay available to the nodes after it. Throwing is still the way to report a failure.
/// <para>
/// The engine collects the messages per call of the node's method. When the node is laced over a list, the messages of all
/// calls are summarised in one line ("3 of 40 calls: first message"), so a thousand elements cannot flood the badge. Called
/// outside a run (a unit test, a helper used by another tool) <see cref="Add"/> does nothing and never throws.
/// </para>
/// </summary>
public static class NodeWarnings
{
    private static readonly AsyncLocal<WarningCollector?> Active = new AsyncLocal<WarningCollector?>();

    /// <summary>
    /// Reports a warning for the call of the node that is running right now. Safe to call from any depth of the node's own
    /// code and from helper threads it starts; ignored when no node is running or the message is empty.
    /// </summary>
    /// <param name="message">What the user should know, as a plain sentence ("2 of 10 values were not numbers and were skipped").</param>
    public static void Add(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Active.Value?.Add(message.Trim());
    }

    /// <summary>
    /// Reports "<paramref name="what"/> is not a finite number (NaN)." (or Infinity) when <paramref name="value"/> is not a finite
    /// number, and does nothing otherwise. For nodes whose arithmetic can quietly produce NaN or Infinity (a division by zero, a
    /// scale by NaN): call it on the result or on the input that caused it, and return the value as usual.
    /// </summary>
    /// <param name="value">The number to check.</param>
    /// <param name="what">What the number is, as the user knows it ("The result", "The factor").</param>
    /// <returns>True when the value is NaN or infinite.</returns>
    public static bool WarnIfNotFinite(double value, string what)
    {
        if (!double.IsNaN(value) && !double.IsInfinity(value))
        {
            return false;
        }

        var kind = double.IsNaN(value) ? "NaN" : value > 0 ? "Infinity" : "-Infinity";
        Add((string.IsNullOrWhiteSpace(what) ? "The value" : what.Trim()) + " is not a finite number (" + kind + ").");
        return true;
    }

    /// <summary>True while a node call is collecting warnings.</summary>
    internal static bool IsCollecting => Active.Value != null;

    /// <summary>Starts collecting for one call of a node; dispose to stop (the previous collector, if any, takes over again).</summary>
    internal static WarningCollector Begin()
    {
        var collector = new WarningCollector(Active.Value);
        Active.Value = collector;
        return collector;
    }

    /// <summary>
    /// Puts what one un-laced call reported on the node as warnings: each distinct message (with how often it came), at most
    /// five, then one line counting the rest.
    /// </summary>
    internal static void Report(CamelGraph.Core.Graph.NodeModel node, WarningCollector call)
    {
        const int shown = 5;
        var items = call.Snapshot();
        var covered = 0;
        for (var i = 0; i < items.Count && i < shown; i++)
        {
            covered += items[i].Value;
            node.AddMessage(
                CamelGraph.Core.Graph.MessageSeverity.Warning,
                items[i].Value > 1
                    ? items[i].Key + " (" + items[i].Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " times)"
                    : items[i].Key);
        }

        var more = call.Total - covered;
        if (more > 0)
        {
            node.AddMessage(
                CamelGraph.Core.Graph.MessageSeverity.Warning,
                "… and " + more.ToString(System.Globalization.CultureInfo.InvariantCulture) + " more warning(s).");
        }
    }

    /// <summary>The messages one node call reported.</summary>
    internal sealed class WarningCollector : IDisposable
    {
        /// <summary>Most distinct messages kept per call (further ones are only counted).</summary>
        public const int MaxDistinct = 20;

        private readonly WarningCollector? _previous;
        private readonly object _gate = new object();
        private List<string>? _order;                 // created by the first message: most calls never warn
        private Dictionary<string, int>? _counts;
        private bool _disposed;

        public WarningCollector(WarningCollector? previous)
        {
            _previous = previous;
        }

        /// <summary>Number of times <see cref="NodeWarnings.Add"/> was called for this call.</summary>
        public int Total { get; private set; }

        /// <summary>Messages that did not fit under <see cref="MaxDistinct"/>.</summary>
        public int Dropped { get; private set; }

        /// <summary>The first message reported, or null.</summary>
        public string? First
        {
            get
            {
                lock (_gate)
                {
                    return _order != null && _order.Count > 0 ? _order[0] : null;
                }
            }
        }

        /// <summary>The distinct messages in the order they were first reported, each with how often it was reported.</summary>
        public List<KeyValuePair<string, int>> Snapshot()
        {
            lock (_gate)
            {
                var list = new List<KeyValuePair<string, int>>(_order?.Count ?? 0);
                if (_order != null && _counts != null)
                {
                    foreach (var message in _order)
                    {
                        list.Add(new KeyValuePair<string, int>(message, _counts[message]));
                    }
                }

                return list;
            }
        }

        public void Add(string message)
        {
            lock (_gate)
            {
                Total++;
                _order ??= new List<string>();
                _counts ??= new Dictionary<string, int>(StringComparer.Ordinal);
                if (_counts.TryGetValue(message, out var count))
                {
                    _counts[message] = count + 1;
                }
                else if (_order.Count < MaxDistinct)
                {
                    _order.Add(message);
                    _counts[message] = 1;
                }
                else
                {
                    Dropped++;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Active.Value = _previous;
        }
    }
}
