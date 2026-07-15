using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Newtonsoft.Json.Linq;

namespace Dyncamelo.Core.Nodes;

/// <summary>
/// A directory path chosen by the user (the UI shows a browse dialog; the model only holds the string).
/// </summary>
public class DirectoryPathNode : NodeModel
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "DirectoryPath";

    private string _path = string.Empty;

    /// <summary>Creates the node with one string output.</summary>
    public DirectoryPathNode()
    {
        Name = "Directory Path";
        Category = "Input";
        Description = "A path to a directory.";
        AddOutput("path", typeof(string));
    }

    /// <summary>The selected path. Changing it dirties the node.</summary>
    public string Path
    {
        get => _path;
        set
        {
            if (SetField(ref _path, value))
            {
                MarkDirty();
            }
        }
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override NodeFunction Function => NodeFunction.Create;

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        return new object?[] { Path };
    }

    /// <inheritdoc />
    public override void SerializeData(JObject data)
    {
        data["Path"] = Path;
    }

    /// <inheritdoc />
    public override void DeserializeData(JObject data)
    {
        Path = data.Value<string>("Path") ?? string.Empty;
    }
}
