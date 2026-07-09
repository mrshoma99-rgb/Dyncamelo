using System;

namespace Dyncamelo.Core.Serialization;

/// <summary>
/// Thrown when a .dyc file cannot be read at all (not JSON, wrong envelope, or
/// written by a future format the current reader is not allowed to open).
/// Recoverable problems (unknown nodes, unknown fields) never throw — they
/// degrade to placeholders instead.
/// </summary>
public class GraphFormatException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Reason the file cannot be read.</param>
    public GraphFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an inner cause.</summary>
    /// <param name="message">Reason the file cannot be read.</param>
    /// <param name="innerException">Underlying parse error.</param>
    public GraphFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
