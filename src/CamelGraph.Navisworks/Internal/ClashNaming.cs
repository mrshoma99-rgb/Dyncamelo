using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace CamelGraph.Navisworks.Internal;

/// <summary>
/// Names for things made from clash results (saved viewpoints, BCF topics). Internal, never surfaced as nodes.
/// </summary>
internal static class ClashNaming
{
    /// <summary>
    /// The display name of the clash test a result belongs to (found by walking up through its result groups), or null when the
    /// result has no test above it.
    /// </summary>
    /// <param name="item">A clash result or result group.</param>
    internal static string? TestNameOf(SavedItem item)
    {
        for (var parent = item.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is ClashTest test)
            {
                return test.DisplayName;
            }
        }

        return null;
    }
}
