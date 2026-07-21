using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Execution;

/// <summary>
/// The tie-break rule for nodes whose data dependencies leave their relative
/// order open: canvas top-to-bottom (Y), then left-to-right (X), then creation
/// index. Side-effect nodes with no data edge between them therefore run in
/// the order they appear on the canvas — visible and editable — instead of the
/// invisible order they happened to be added to the file, which reshuffled on
/// every edit or copy/paste and made loops of write nodes look flaky.
/// </summary>
internal static class ExecutionOrder
{
    /// <summary>Compares two nodes for ready-set selection (smaller runs first).</summary>
    /// <param name="a">First node.</param>
    /// <param name="b">Second node.</param>
    internal static int Compare(NodeModel a, NodeModel b)
    {
        int byY = a.Y.CompareTo(b.Y);
        if (byY != 0)
        {
            return byY;
        }

        int byX = a.X.CompareTo(b.X);
        return byX != 0 ? byX : a.CreationIndex.CompareTo(b.CreationIndex);
    }
}
