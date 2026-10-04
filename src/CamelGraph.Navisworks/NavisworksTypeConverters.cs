using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;
using CamelGraph.Nodes;
using NwBoundingBox3D = Autodesk.Navisworks.Api.BoundingBox3D;
using NwColor = Autodesk.Navisworks.Api.Color;
using NwModelItem = Autodesk.Navisworks.Api.ModelItem;
using NwPoint3D = Autodesk.Navisworks.Api.Point3D;

namespace CamelGraph.Navisworks;

/// <summary>
/// Registers custom type converters so values produced by the general node
/// library flow into Navisworks ports and API calls: the plain
/// <see cref="CamelGraphColor"/> (Color.ByARGB, Color Picker, ...) and
/// <see cref="System.Drawing.Color"/> both convert to
/// <c>Autodesk.Navisworks.Api.Color</c>. The registration hook is invoked by
/// <c>NodeRegistry.RegisterAssembly</c> when the host loads this node pack, and
/// the converters are then consulted by both connection compatibility checks
/// and runtime coercion in CamelGraph.Core.
/// </summary>
[IsVisibleInLibrary(false)]
public static class NavisworksTypeConverters
{
    /// <summary>Registers the color, point and bounding-box converters. Idempotent; runs once per process.</summary>
    [TypeConverterRegistration]
    public static void RegisterConverters()
    {
        // Navisworks colors carry no alpha: transparency is a separate override
        // (Appearance.OverrideTransparency), so the alpha channel is dropped.
        TypeCoercion.RegisterConverter(
            typeof(CamelGraphColor),
            typeof(NwColor),
            value =>
            {
                var color = (CamelGraphColor)value;
                return NwColor.FromByteRGB(color.R, color.G, color.B);
            });

        // Inline colour swatches store "#AARRGGBB" strings; Navisworks colour ports accept them too.
        TypeCoercion.RegisterConverter(
            typeof(string),
            typeof(NwColor),
            value =>
            {
                var color = ColorNodes.FromHex((string)value);
                return NwColor.FromByteRGB(color.R, color.G, color.B);
            });

        // Model-element inputs pin the picked items as "nw:<paths>" strings; they resolve to live items at run time.
        TypeCoercion.RegisterConverter(
            typeof(string),
            typeof(NwModelItem),
            value => NavisworksModelPicker.Resolve((string)value).FirstOrDefault());
        TypeCoercion.RegisterConverter(
            typeof(string),
            typeof(List<NwModelItem>),
            value => NavisworksModelPicker.Resolve((string)value));
        TypeCoercion.RegisterConverter(
            typeof(string),
            typeof(Autodesk.Navisworks.Api.ModelItemCollection),
            value => Internal.NavisValues.ToItemCollection(NavisworksModelPicker.Resolve((string)value)));

        TypeCoercion.RegisterConverter(
            typeof(System.Drawing.Color),
            typeof(NwColor),
            value =>
            {
                var color = (System.Drawing.Color)value;
                return NwColor.FromByteRGB(color.R, color.G, color.B);
            });

        // Points flow both ways: general Geometry nodes (Point.ByCoordinates)
        // feed Navisworks camera nodes, and Navisworks points (ClashResult.Center,
        // Camera.Current) feed general Geometry nodes.
        TypeCoercion.RegisterConverter(
            typeof(CamelGraphPoint),
            typeof(NwPoint3D),
            value =>
            {
                var point = (CamelGraphPoint)value;
                return new NwPoint3D(point.X, point.Y, point.Z);
            });

        TypeCoercion.RegisterConverter(
            typeof(NwPoint3D),
            typeof(CamelGraphPoint),
            value =>
            {
                var point = (NwPoint3D)value;
                return new CamelGraphPoint(point.X, point.Y, point.Z);
            });

        // Bounding boxes flow both ways: ModelItem.BoundingBox (a Navisworks
        // BoundingBox3D) feeds general geometry nodes like BoundingBox.Scale,
        // and the scaled general box feeds Navisworks consumers (section boxes,
        // zoom targets) back.
        TypeCoercion.RegisterConverter(
            typeof(NwBoundingBox3D),
            typeof(CamelGraphBoundingBox),
            value =>
            {
                var box = (NwBoundingBox3D)value;
                return new CamelGraphBoundingBox(
                    new CamelGraphPoint(box.Min.X, box.Min.Y, box.Min.Z),
                    new CamelGraphPoint(box.Max.X, box.Max.Y, box.Max.Z));
            });

        TypeCoercion.RegisterConverter(
            typeof(CamelGraphBoundingBox),
            typeof(NwBoundingBox3D),
            value =>
            {
                var box = (CamelGraphBoundingBox)value;
                return new NwBoundingBox3D(
                    new NwPoint3D(box.Min.X, box.Min.Y, box.Min.Z),
                    new NwPoint3D(box.Max.X, box.Max.Y, box.Max.Z));
            });

        // An element wired straight into a bounding-box port means "this
        // element's box" (replication maps lists item-by-item).
        TypeCoercion.RegisterConverter(
            typeof(NwModelItem),
            typeof(CamelGraphBoundingBox),
            value =>
            {
                var box = ((NwModelItem)value).BoundingBox(false);
                return new CamelGraphBoundingBox(
                    new CamelGraphPoint(box.Min.X, box.Min.Y, box.Min.Z),
                    new CamelGraphPoint(box.Max.X, box.Max.Y, box.Max.Z));
            });
    }
}
