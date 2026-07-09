using System;

namespace Dyncamelo.Cli;

/// <summary>
/// A user-facing CLI failure (bad arguments, unreadable file, missing pack).
/// The message is printed to stderr and the process exits with <see cref="ExitCode"/>.
/// </summary>
internal sealed class CliError : Exception
{
    public CliError(string message, int exitCode = ExitCodes.UsageOrIoError)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}

/// <summary>Process exit codes used by every command.</summary>
internal static class ExitCodes
{
    /// <summary>The command completed and found no error-state nodes / issues.</summary>
    public const int Success = 0;

    /// <summary>The command completed but the graph has error-state nodes or validation issues.</summary>
    public const int GraphHasErrors = 1;

    /// <summary>Bad arguments, unreadable/missing files or packs.</summary>
    public const int UsageOrIoError = 2;
}
