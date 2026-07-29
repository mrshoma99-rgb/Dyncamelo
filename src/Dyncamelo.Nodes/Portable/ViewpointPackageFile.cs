using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Dyncamelo.Nodes.Portable;

/// <summary>
/// The on-disk model for a portable viewpoint package (Viewpoints.ExportFile /
/// Viewpoints.ImportFile): a versioned JSON document carrying each viewpoint's
/// camera and section box — deliberately NOT its element overrides, which are
/// bound to model-item paths inside the source file and cannot survive into a
/// different model. Length values are in the source document's units
/// (<see cref="SourceUnits"/>); <see cref="ScaleLengths"/> converts the whole
/// package into another unit before the views are rebuilt.
/// </summary>
public sealed class ViewpointPackageFile
{
    /// <summary>Format version written by this build. Newer files are rejected on parse.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The package format version.</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>The source document's units name (e.g. "Meters", "Feet") — drives import-side conversion.</summary>
    public string? SourceUnits { get; set; }

    /// <summary>The source document's file name. Informational only.</summary>
    public string? SourceDocument { get; set; }

    /// <summary>The exported viewpoints, in export order.</summary>
    public List<PortableViewpoint> Views { get; set; } = new List<PortableViewpoint>();

    /// <summary>Serializes the package as indented JSON.</summary>
    public string ToJson()
    {
        return JsonConvert.SerializeObject(this, Formatting.Indented, JsonSettings);
    }

    /// <summary>
    /// Parses a package from JSON with node-friendly errors: malformed text and
    /// files written by a newer Dyncamelo both fail with a message that says
    /// what to do, never a raw serializer stack trace.
    /// </summary>
    public static ViewpointPackageFile Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("The viewpoint package file is empty.", nameof(json));
        }

        ViewpointPackageFile? package;
        try
        {
            package = JsonConvert.DeserializeObject<ViewpointPackageFile>(json, JsonSettings);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "The file is not a valid viewpoint package (" + ex.Message +
                "). Export one with Viewpoints.ExportFile.");
        }

        if (package == null)
        {
            throw new InvalidOperationException(
                "The file is not a valid viewpoint package. Export one with Viewpoints.ExportFile.");
        }

        if (package.Version > CurrentVersion)
        {
            throw new InvalidOperationException(
                "This viewpoint package was created by a newer Dyncamelo (format version " +
                package.Version + "; this build reads up to " + CurrentVersion +
                "). Update Dyncamelo to import it.");
        }

        package.Views ??= new List<PortableViewpoint>();
        package.Views.RemoveAll(v => v == null);
        foreach (var view in package.Views)
        {
            if (string.IsNullOrEmpty(view.Name))
            {
                throw new InvalidOperationException(
                    "The viewpoint package contains a view without a name — the file is damaged.");
            }
        }

        return package;
    }

    /// <summary>
    /// Converts every length in the package by <paramref name="factor"/>
    /// (source units → target units): camera positions, focal distances,
    /// section-box corners, and — for orthographic views only — the height
    /// field, which is a world-space extent there but a field-of-view angle in
    /// perspective views. Rotations and aspect ratios are unit-free. The raw
    /// camera strings are dropped because they embed unconverted coordinates.
    /// </summary>
    public void ScaleLengths(double factor)
    {
        if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), "The unit scale factor must be a positive number.");
        }

        if (factor == 1.0)
        {
            return;
        }

        foreach (var view in Views)
        {
            view.RawCamera = null;
            Scale(view.Position, factor);
            if (view.FocalDistance.HasValue)
            {
                view.FocalDistance = view.FocalDistance.Value * factor;
            }

            if (view.IsOrthographic)
            {
                view.HeightField *= factor;
            }

            if (view.SectionBox != null)
            {
                Scale(view.SectionBox.Min, factor);
                Scale(view.SectionBox.Max, factor);
            }
        }
    }

    private static void Scale(double[]? values, double factor)
    {
        if (values == null)
        {
            return;
        }

        for (int i = 0; i < values.Length; i++)
        {
            values[i] *= factor;
        }
    }

    private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
    };
}

/// <summary>One exported viewpoint: name, folder location, camera, section box.</summary>
public sealed class PortableViewpoint
{
    /// <summary>The viewpoint's display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Folder segments from the Saved Viewpoints root down to the viewpoint's
    /// containing folder (empty = top level). A list, not a joined string, so
    /// folder names containing separators survive.
    /// </summary>
    public List<string> FolderPath { get; set; } = new List<string>();

    /// <summary>
    /// The camera exactly as Navisworks serialized it (Viewpoint.GetCamera) —
    /// the bit-perfect restore path when no unit conversion is needed. Null
    /// after <see cref="ViewpointPackageFile.ScaleLengths"/>.
    /// </summary>
    public string? RawCamera { get; set; }

    /// <summary>Camera eye position [x, y, z], in source-document units.</summary>
    public double[] Position { get; set; } = new double[3];

    /// <summary>Camera rotation quaternion [a, b, c, d] (d = scalar part), from identity looking down −Z with +Y up.</summary>
    public double[] Rotation { get; set; } = { 0, 0, 0, 1 };

    /// <summary>"Perspective" or "Orthographic".</summary>
    public string Projection { get; set; } = "Perspective";

    /// <summary>Vertical field of view (radians) for perspective views; view height in source units for orthographic ones.</summary>
    public double HeightField { get; set; }

    /// <summary>View width over height. Unit-free.</summary>
    public double AspectRatio { get; set; } = 1.0;

    /// <summary>Distance to the focal (orbit) point, in source units. Null when the view has none.</summary>
    public double? FocalDistance { get; set; }

    /// <summary>World up vector [x, y, z]. Null when the view has none recorded.</summary>
    public double[]? WorldUp { get; set; }

    /// <summary>The view's section box. Null when the view is not sectioned (or the box could not be read).</summary>
    public PortableSectionBox? SectionBox { get; set; }

    /// <summary>Whether this view is orthographic (its height field is a length, not an angle).</summary>
    [JsonIgnore]
    public bool IsOrthographic =>
        string.Equals(Projection, "Orthographic", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A box-mode sectioning region, in source-document units.</summary>
public sealed class PortableSectionBox
{
    /// <summary>Whether sectioning was enabled on the view (always true for v1 exports).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Box minimum corner [x, y, z] in the box's local frame.</summary>
    public double[] Min { get; set; } = new double[3];

    /// <summary>Box maximum corner [x, y, z] in the box's local frame.</summary>
    public double[] Max { get; set; } = new double[3];

    /// <summary>Box orientation quaternion [a, b, c, d], or null for an axis-aligned box.</summary>
    public double[]? Rotation { get; set; }
}
