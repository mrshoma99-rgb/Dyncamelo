using System;

namespace Dyncamelo.Core.Graph;

/// <summary>
/// Severity of a <see cref="NodeMessage"/>.
/// </summary>
public enum MessageSeverity
{
    /// <summary>Informational message (e.g. "input not connected").</summary>
    Info = 0,

    /// <summary>Data-level problem; the run continued (possibly with partial/null results).</summary>
    Warning = 1,

    /// <summary>Structural problem or unhandled exception; the node produced no results.</summary>
    Error = 2,
}

/// <summary>
/// A single diagnostic attached to a node during execution.
/// </summary>
public class NodeMessage
{
    /// <summary>Creates a message.</summary>
    /// <param name="severity">Message severity.</param>
    /// <param name="text">Human-readable message text.</param>
    public NodeMessage(MessageSeverity severity, string text)
    {
        Severity = severity;
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>Severity of the message.</summary>
    public MessageSeverity Severity { get; }

    /// <summary>Human-readable message text.</summary>
    public string Text { get; }

    /// <inheritdoc />
    public override string ToString() => Severity + ": " + Text;
}
