using System.Collections.Generic;

namespace Dyncamelo.UI.Services;

/// <summary>
/// Reflects a node's output back into the host so users can see what a node
/// collected — the editor calls it when the selected node changes. The UI does
/// not reference the host (Navisworks), so outputs are passed as opaque objects
/// and the host implementation extracts what it understands (e.g. model items).
/// </summary>
public interface IPreviewService
{
    /// <summary>Highlights the model items found in a node's output values in the host viewport.</summary>
    /// <param name="outputs">The selected node's output port values (opaque to the UI).</param>
    void ShowPreview(IReadOnlyList<object?> outputs);

    /// <summary>Clears any preview highlight.</summary>
    void ClearPreview();
}

/// <summary>No-op preview used when no host is attached (e.g. the standalone/test shell).</summary>
public sealed class NullPreviewService : IPreviewService
{
    /// <inheritdoc />
    public void ShowPreview(IReadOnlyList<object?> outputs)
    {
    }

    /// <inheritdoc />
    public void ClearPreview()
    {
    }
}
