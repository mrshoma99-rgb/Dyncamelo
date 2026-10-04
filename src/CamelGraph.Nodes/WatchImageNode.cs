using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Types;
using Newtonsoft.Json.Linq;

namespace CamelGraph.Nodes;

/// <summary>
/// Displays the picture at an incoming file path directly on the canvas —
/// wire an image-producing node (the fall-hazard heat map, a saved viewpoint
/// export, ...) into it to see the result without opening the file. The path
/// passes through unchanged so the chain can continue. A list of paths shows the first image
/// and says how many there are.
/// </summary>
public class WatchImageNode : NodeModel, CamelGraph.Core.Player.IPlayerOutputNode
{
    /// <summary>Serialized type tag.</summary>
    public const string TypeName = "WatchImage";

    private string _imagePath = string.Empty;
    private IReadOnlyList<string> _paths = new List<string>();
    private bool _hasImage;
    private int _imageVersion;
    private double _viewWidth;
    private double _viewHeight;

    /// <summary>Creates the node with a path input and a pass-through output.</summary>
    public WatchImageNode()
    {
        Name = "Watch Image";
        Category = "Display";
        Description = "Displays the image file at the incoming path (PNG, JPG, BMP). Given a list of paths it shows the first image and says how many there are. When the node does not run the picture is cleared.";
        AddInput("imagePath", typeof(object), "Path of the image file to display, or a list of paths (the first is shown).");
        AddOutput("imagePath", typeof(object), "The incoming path, passed through.");
    }

    /// <summary>What the Script Player shows: the path, or for a list the count and one path per line.</summary>
    public string PlayerText => _paths.Count <= 1 ? ImagePath : ValueText.Count(_paths.Count) + " images\n" + string.Join("\n", _paths.Take(ImageListLines));

    /// <summary>The most paths the Player text lists.</summary>
    public const int ImageListLines = 298;

    /// <summary>How many image paths arrived (1 for a single path, 0 when there is none).</summary>
    public int ImageCount => _paths.Count;

    /// <summary>Path of the image shown (the first of a list), or empty when there is nothing to show.</summary>
    public string ImagePath
    {
        get => _imagePath;
        private set
        {
            _imagePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImage));
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(PlayerText));
            OnPropertyChanged(nameof(ImageCount));
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

    /// <summary>True when <see cref="ImagePath"/> pointed at an existing file when the node last ran (checked once per run, not on every redraw).</summary>
    public bool HasImage => _hasImage;

    /// <summary>File name of the shown image (caption under the picture), with "(1 of 3)" when several paths arrived.</summary>
    public string FileName =>
        _imagePath.Length == 0
            ? string.Empty
            : Path.GetFileName(_imagePath) + (_paths.Count > 1 ? " (1 of " + _paths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" : string.Empty);

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
        _paths = Paths(value);
        _hasImage = _paths.Count > 0 && File.Exists(_paths[0]);
        ImagePath = _paths.Count > 0 ? _paths[0] : string.Empty;
        ImageVersion++;
        return new object?[] { value };
    }

    /// <summary>The picture is cleared: an old image is never shown as the current result.</summary>
    public override void OnNotRun()
    {
        _paths = new List<string>();
        _hasImage = false;
        ImagePath = string.Empty;
        ImageVersion++;
    }

    // A path, or a (nested) list of paths: the non-empty texts in order.
    private static List<string> Paths(object? value)
    {
        var result = new List<string>();
        Collect(value, result);
        return result;
    }

    private static void Collect(object? value, List<string> result)
    {
        if (value == null)
        {
            return;
        }

        if (value is IEnumerable list && !(value is string))
        {
            foreach (var item in list)
            {
                Collect(item, result);
            }

            return;
        }

        var text = value.ToString()?.Trim() ?? string.Empty;
        if (text.Length > 0)
        {
            result.Add(text);
        }
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
