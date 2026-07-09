namespace Dyncamelo.Core.Graph;

/// <summary>
/// Outcome of a <see cref="GraphModel.Connect"/> attempt.
/// </summary>
public class ConnectionResult
{
    private ConnectionResult(bool success, string? message, ConnectionModel? connection)
    {
        Success = success;
        Message = message;
        Connection = connection;
    }

    /// <summary>True when the connection was created.</summary>
    public bool Success { get; }

    /// <summary>Failure reason when <see cref="Success"/> is false; null otherwise.</summary>
    public string? Message { get; }

    /// <summary>The created connection when <see cref="Success"/> is true.</summary>
    public ConnectionModel? Connection { get; }

    /// <summary>Creates a success result.</summary>
    /// <param name="connection">The created connection.</param>
    public static ConnectionResult Ok(ConnectionModel connection) => new ConnectionResult(true, null, connection);

    /// <summary>Creates a failure result.</summary>
    /// <param name="message">Human-readable reason.</param>
    public static ConnectionResult Fail(string message) => new ConnectionResult(false, message, null);
}
