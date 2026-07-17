using System.IO;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Newtonsoft.Json.Linq;

namespace Dyncamelo.Nodes;

/// <summary>
/// Displays the picture at an incoming file path directly on the canvas —
/// wire an image-producing node (the fall-hazard heat map, a saved viewpoint
/// export, ...) into it to see the result without opening the file. The path
/// passes through unchanged so the chain can continue.
/// </summary>
public class WatchImageNode : NodeModel
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "WatchImage";

    private string _imagePath = string.Empty;
    private int _imageVersion;
    private double _viewWidth;
    private double _viewHeight;

    /// <summary>Creates the node with a path input and a pass-through output.</summary>
    public WatchImageNode()
    {
        Name = "Watch Image";
        Category = "Display";
        Description = "Displays the image file at the incoming path (PNG, JPG, BMP).";
        AddInput("imagePath", typeof(object), "Path of the image file to display.");
        AddOutput("imagePath", typeof(object), "The incoming path, passed through.");
    }

    /// <summary>Path of the image shown, or empty when there is nothing to show.</summary>
    public string ImagePath
    {
        get => _imagePath;
        private set
        {
            _imagePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImage));
            OnPropertyChanged(nameof(FileName));
        }
    }

    /// <summary>
    /// Bumped on every run so the editor reloads the bitmap even when the path
    /// is unchanged (analysis nodes overwrite the same file run after run).
    /// </summary>
    public int ImageVersion
    {
        get => _imageVersion;
        private set
        {
            _imageVersion = value;
            OnPropertyChanged();
        }
    }

    /// <summary>True when <see cref="ImagePath"/> points at an existing file.</summary>
    public bool HasImage => _imagePath.Length > 0 && File.Exists(_imagePath);

    /// <summary>File name of the shown image (caption under the picture).</summary>
    public string FileName => _imagePath.Length > 0 ? Path.GetFileName(_imagePath) : string.Empty;

    /// <summary>
    /// User-chosen width of the display area (0 = automatic). Pure view state:
    /// changing it never dirties the node. Persisted in the .dyc payload.
    /// </summary>
    public double ViewWidth
    {
        get => _viewWidth;
        set => SetField(ref _viewWidth, value);
    }

    /// <summary>
    /// User-chosen height of the display area (0 = automatic). Pure view state:
    /// changing it never dirties the node. Persisted in the .dyc payload.
    /// </summary>
    public double ViewHeight
    {
        get => _viewHeight;
        set => SetField(ref _viewHeight, value);
    }

    /// <inheritdoc />
    public override string NodeType => TypeName;

    /// <inheritdoc />
    public override NodeFunction Function => NodeFunction.Info;

    /// <inheritdoc />
    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        var value = inputs.Length > 0 ? inputs[0] : null;
        ImagePath = value?.ToString()?.Trim() ?? string.Empty;
        ImageVersion++;
        return new object?[] { value };
    }

    /// <inheritdoc />
    public override void SerializeData(JObject data)
    {
        // Additive, optional fields: absent (or zero) means "size automatically",
        // so files written by older versions keep loading unchanged.
        data["ViewWidth"] = ViewWidth;
        data["ViewHeight"] = ViewHeight;
    }

    /// <inheritdoc />
    public override void DeserializeData(JObject data)
    {
        ViewWidth = data.Value<double?>("ViewWidth") ?? 0d;
        ViewHeight = data.Value<double?>("ViewHeight") ?? 0d;
    }
}
