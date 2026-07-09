using System;
using Dyncamelo.Cli.Commands;

namespace Dyncamelo.Cli;

/// <summary>
/// Entry point of the <c>dyncamelo</c> headless graph runner. Lets every part
/// of Dyncamelo that does not need Navisworks or WPF (Core, Nodes, .dyc files,
/// third-party node packs) be executed and tested on any OS.
/// </summary>
internal static class Program
{
    internal static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage(Console.Error);
            return ExitCodes.UsageOrIoError;
        }

        var command = args[0];
        if (command == "help" || command == "-h" || command == "--help")
        {
            PrintUsage(Console.Out);
            return ExitCodes.Success;
        }

        try
        {
            var rest = CommandArguments.Parse(args, startIndex: 1);
            switch (command)
            {
                case "run":
                    return RunCommand.Execute(rest, Console.Out);
                case "list-nodes":
                    return ListNodesCommand.Execute(rest, Console.Out);
                case "validate":
                    return ValidateCommand.Execute(rest, Console.Out);
                case "write-samples":
                    return WriteSamplesCommand.Execute(rest, Console.Out);
                default:
                    throw new CliError("Unknown command '" + command + "'. Run 'dyncamelo help' for usage.");
            }
        }
        catch (CliError error)
        {
            Console.Error.WriteLine("error: " + error.Message);
            return error.ExitCode;
        }
    }

    private static void PrintUsage(System.IO.TextWriter output)
    {
        output.WriteLine("dyncamelo — headless Dyncamelo graph runner");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine("  dyncamelo run <graph.dyc> [--pack <dll-or-dir>]...");
        output.WriteLine("      Execute a graph and print per-node states/timings, Watch values and diagnostics.");
        output.WriteLine("      Exit code: 0 = no error-state nodes, 1 = at least one node errored, 2 = unreadable input.");
        output.WriteLine();
        output.WriteLine("  dyncamelo list-nodes [--pack <dll-or-dir>]...");
        output.WriteLine("      Print the node registry (built-ins, Dyncamelo.Nodes and extra packs) grouped by category.");
        output.WriteLine();
        output.WriteLine("  dyncamelo validate <graph.dyc> [--pack <dll-or-dir>]...");
        output.WriteLine("      Load a graph without running it; report unresolved node definitions and dropped connectors.");
        output.WriteLine();
        output.WriteLine("  dyncamelo write-samples <directory>");
        output.WriteLine("      Regenerate the shipped sample graphs (developer command).");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine("  --pack <dll-or-dir>   Load an extra zero-touch node pack (repeatable). A directory loads all *.dll files.");
    }
}
