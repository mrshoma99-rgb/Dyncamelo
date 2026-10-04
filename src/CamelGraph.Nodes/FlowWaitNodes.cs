using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes;

/// <summary>
/// Timing helpers for workflows. Kept apart from <see cref="FlowNodes"/> (execution ordering) because these nodes
/// really take time.
/// </summary>
[NodeCategory("Workflow")]
public static class FlowWaitNodes
{
    /// <summary>The longest single sleep, so a long wait is a series of short ones.</summary>
    private const int SliceMilliseconds = 100;

    /// <summary>
    /// Pauses the graph for a number of seconds and then passes <paramref name="value"/> through unchanged. Wire a node's
    /// output into <paramref name="value"/> to make everything downstream wait, e.g. to give an external program time to
    /// finish writing a file.
    /// </summary>
    /// <param name="value">The value to pass through after the pause (it is also what makes the pause happen in the right place).</param>
    /// <param name="seconds">How long to wait, 0 to 3600 seconds (fractions allowed).</param>
    /// <returns>The <paramref name="value"/> input, unchanged.</returns>
    [NodeName("Flow.Wait")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Create)]
    [return: NodeName("value")]
    [NodeDescription("Waits the given number of seconds, then passes the value through unchanged - use it to pause between steps of a workflow.")]
    [NodeSearchTags("wait", "sleep", "pause", "delay", "timer", "throttle", "seconds")]
    public static object? Wait(object? value, [NodeRange(0, 3600)] double seconds = 1)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > 3600)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                "Flow.Wait: 'seconds' must be a number between 0 and 3600, not " + seconds.ToString(CultureInfo.InvariantCulture) + ".");
        }

        // The engine does not hand its cancellation token to static nodes, so the wait cannot be cut short from here;
        // sleeping in short slices at least keeps the thread responsive to being interrupted or aborted.
        var clock = Stopwatch.StartNew();
        var total = TimeSpan.FromSeconds(seconds);
        while (true)
        {
            var remaining = total - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var slice = remaining < TimeSpan.FromMilliseconds(SliceMilliseconds)
                ? remaining
                : TimeSpan.FromMilliseconds(SliceMilliseconds);
            Thread.Sleep(slice);
        }

        return value;
    }
}
