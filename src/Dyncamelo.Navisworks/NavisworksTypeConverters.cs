using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;
using Dyncamelo.Nodes;
using NwBoundingBox3D = Autodesk.Navisworks.Api.BoundingBox3D;
using NwColor = Autodesk.Navisworks.Api.Color;
using NwModelItem = Autodesk.Navisworks.Api.ModelItem;
using NwPoint3D = Autodesk.Navisworks.Api.Point3D;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Registers custom type converters so values produced by the general node
/// library flow into Navisworks ports and API calls: the plain
/// <see cref="DyncameloColor"/> (Color.ByARGB, Color Picker, ...) and
/// <see cref="System.Drawing.Color"/> both convert to
/// <c>Autodesk.Navisworks.Api.Color</c>. The registration hook is invoked by
/// <c>NodeRegistry.RegisterAssembly</c> when the host loads this node pack, and
/// the converters are then consulted by both connection compatibility checks
/// and runtime coercion in Dyncamelo.Core.
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
            typeof(DyncameloColor),
            typeof(NwColor),
            value =>
            {
                var color = (DyncameloColor)value;
                return NwColor.FromByteRGB(color.R, color.G, color.B);
            });

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
            typeof(DyncameloPoint),
            typeof(NwPoint3D),
            value =>
            {
                var point = (DyncameloPoint)value;
                return new NwPoint3D(point.X, point.Y, point.Z);
            });

        TypeCoercion.RegisterConverter(
            typeof(NwPoint3D),
            typeof(DyncameloPoint),
            value =>
            {
                var point = (NwPoint3D)value;
                return new DyncameloPoint(point.X, point.Y, point.Z);
            });

        // Bounding boxes flow both ways: ModelItem.BoundingBox (a Navisworks
        // BoundingBox3D) feeds general geometry nodes like BoundingBox.Scale,
        // and the scaled general box feeds Navisworks consumers (section boxes,
        // zoom targets) back.
        TypeCoercion.RegisterConverter(
            typeof(NwBoundingBox3D),
            typeof(DyncameloBoundingBox),
            value =>
            {
                var box = (NwBoundingBox3D)value;
                return new DyncameloBoundingBox(
                    new DyncameloPoint(box.Min.X, box.Min.Y, box.Min.Z),
                    new DyncameloPoint(box.Max.X, box.Max.Y, box.Max.Z));
            });

        TypeCoercion.RegisterConverter(
            typeof(DyncameloBoundingBox),
            typeof(NwBoundingBox3D),
            value =>
            {
                var box = (DyncameloBoundingBox)value;
                return new NwBoundingBox3D(
                    new NwPoint3D(box.Min.X, box.Min.Y, box.Min.Z),
                    new NwPoint3D(box.Max.X, box.Max.Y, box.Max.Z));
            });

        // An element wired straight into a bounding-box port means "this
        // element's box" (replication maps lists item-by-item).
        TypeCoercion.RegisterConverter(
            typeof(NwModelItem),
            typeof(DyncameloBoundingBox),
            value =>
            {
                var box = ((NwModelItem)value).BoundingBox(false);
                return new DyncameloBoundingBox(
                    new DyncameloPoint(box.Min.X, box.Min.Y, box.Min.Z),
                    new DyncameloPoint(box.Max.X, box.Max.Y, box.Max.Z));
            });
    }
}
