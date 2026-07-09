using System;
using System.Collections.Generic;

namespace Dyncamelo.Cli;

/// <summary>
/// Hand-rolled argument parser shared by all commands: splits the arguments
/// following the command name into positional values and repeated
/// <c>--pack &lt;dll-or-dir&gt;</c> options.
/// </summary>
internal sealed class CommandArguments
{
    private CommandArguments(List<string> positional, List<string> packs)
    {
        Positional = positional;
        Packs = packs;
    }

    /// <summary>Positional arguments in order (e.g. the graph path).</summary>
    public IReadOnlyList<string> Positional { get; }

    /// <summary>Values of every <c>--pack</c> option, in order.</summary>
    public IReadOnlyList<string> Packs { get; }

    /// <summary>Parses the arguments that follow the command name.</summary>
    /// <exception cref="CliError">An option is unknown or missing its value.</exception>
    public static CommandArguments Parse(string[] args, int startIndex)
    {
        var positional = new List<string>();
        var packs = new List<string>();

        for (int i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--pack", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length)
                {
                    throw new CliError("Option --pack requires a value (a node-pack .dll or a directory of .dlls).");
                }

                packs.Add(args[++i]);
            }
            else if (arg.StartsWith("-", StringComparison.Ordinal))
            {
                throw new CliError("Unknown option '" + arg + "'. Run 'dyncamelo help' for usage.");
            }
            else
            {
                positional.Add(arg);
            }
        }

        return new CommandArguments(positional, packs);
    }

    /// <summary>Returns the single required positional argument.</summary>
    /// <exception cref="CliError">Zero or more than one positional argument was given.</exception>
    public string RequireSinglePositional(string whatItIs)
    {
        if (Positional.Count == 0)
        {
            throw new CliError("Missing required argument: " + whatItIs + ".");
        }

        if (Positional.Count > 1)
        {
            throw new CliError("Unexpected extra argument '" + Positional[1] + "'.");
        }

        return Positional[0];
    }
}
