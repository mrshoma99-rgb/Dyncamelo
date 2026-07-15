namespace Dyncamelo.Core.Graph;

/// <summary>
/// A node that captures a fixed set of model items and keeps it, so a graph
/// keeps running on that exact set even after the live host selection changes.
/// The host UI drives capture/clear through this interface without having to
/// reference the Navisworks node pack that implements it.
/// </summary>
public interface ICapturedSelectionNode
{
    /// <summary>Replaces the stored set with whatever is selected in the host right now.</summary>
    void CaptureFromCurrentSelection();

    /// <summary>Forgets the stored set.</summary>
    void ClearCapturedSelection();

    /// <summary>How many items are currently stored.</summary>
    int CapturedCount { get; }
}
