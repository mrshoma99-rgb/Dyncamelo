using System.IO;
using System.Linq;
using Dyncamelo.Core.Serialization;

namespace Dyncamelo.Cli.Commands;

/// <summary>
/// <c>dyncamelo write-samples &lt;directory&gt;</c>: regenerates the shipped
/// sample graphs (authored in <see cref="SampleGraphs"/>) as .dyc files.
/// Developer command — used to keep <c>samples/</c> in sync with the code.
/// </summary>
internal static class WriteSamplesCommand
{
    public static int Execute(CommandArguments args, TextWriter output)
    {
        var directory = args.RequireSinglePositional("destination directory for the sample .dyc files");
        if (args.Packs.Count > 0)
        {
            throw new CliError("write-samples does not accept --pack.");
        }

        Directory.CreateDirectory(directory);

        var registry = RegistryBuilder.Build(Enumerable.Empty<string>());
        var serializer = new GraphSerializer(registry);
        foreach (var sample in SampleGraphs.BuildAll(registry))
        {
            var path = Path.Combine(directory, sample.Key);
            serializer.SaveToFile(sample.Value, path);
            output.WriteLine("Wrote " + path);
        }

        return ExitCodes.Success;
    }
}
