using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// VectorNodes: components, arithmetic, length / normalise, dot and cross products, the angle
/// between vectors, axes, ByPoints and the parallel / perpendicular tests.
/// </summary>
public class VectorNodesTests
{
    private static DyncameloVector V(double x, double y, double z) => new DyncameloVector(x, y, z);

    private static void AssertVector(double x, double y, double z, DyncameloVector actual, int precision = 12)
    {
        Assert.Equal(x, actual.X, precision);
        Assert.Equal(y, actual.Y, precision);
        Assert.Equal(z, actual.Z, precision);
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    // ------------------------------------------------------ Vector.Components

    [Fact]
    public void Components_ReturnsXyz_InPortOrder()
    {
        var parts = VectorNodes.Components(V(1.5, -2, 3));

        Assert.Equal(new[] { "x", "y", "z" }, parts.Keys.ToArray());
        Assert.Equal(1.5, parts["x"]);
        Assert.Equal(-2d, parts["y"]);
        Assert.Equal(3d, parts["z"]);
    }

    [Fact]
    public void Components_NullVector_NamesTheNode()
    {
        var error = Assert.Throws<ArgumentNullException>(() => VectorNodes.Components(null!));
        Assert.Contains("Vector.Components", error.Message);
        Assert.Contains("'vector'", error.Message);
    }

    // ----------------------------------------- Vector.Add / Subtract / Scale / Negate

    [Fact]
    public void Add_SumsComponents()
    {
        Assert.Equal(V(5, 7, 9), VectorNodes.Add(V(1, 2, 3), V(4, 5, 6)));
        Assert.Equal(V(1, 2, 3), VectorNodes.Add(V(1, 2, 3), V(0, 0, 0)));
    }

    [Fact]
    public void Subtract_IsFirstMinusSecond()
    {
        Assert.Equal(V(-3, -3, -3), VectorNodes.Subtract(V(1, 2, 3), V(4, 5, 6)));
        Assert.Equal(V(0, 0, 0), VectorNodes.Subtract(V(1, 2, 3), V(1, 2, 3)));
    }

    [Fact]
    public void AddThenSubtract_Roundtrips()
    {
        var a = V(0.5, -4, 12);
        var b = V(7, 7, -1);
        Assert.Equal(a, VectorNodes.Subtract(VectorNodes.Add(a, b), b));
    }

    [Fact]
    public void AddAndSubtract_NullArguments_NameTheNodeAndPort()
    {
        var add = Assert.Throws<ArgumentNullException>(() => VectorNodes.Add(null!, V(1, 1, 1)));
        Assert.Contains("Vector.Add", add.Message);
        Assert.Contains("'a'", add.Message);
        Assert.Contains("'b'", Assert.Throws<ArgumentNullException>(() => VectorNodes.Add(V(1, 1, 1), null!)).Message);

        Assert.Contains("Vector.Subtract", Assert.Throws<ArgumentNullException>(() => VectorNodes.Subtract(null!, V(1, 1, 1))).Message);
        Assert.Contains("'b'", Assert.Throws<ArgumentNullException>(() => VectorNodes.Subtract(V(1, 1, 1), null!)).Message);
    }

    [Fact]
    public void Scale_MultipliesEveryComponent()
    {
        Assert.Equal(V(2, 4, 6), VectorNodes.Scale(V(1, 2, 3), 2));
        Assert.Equal(V(-1, -2, -3), VectorNodes.Scale(V(1, 2, 3), -1));
        Assert.Equal(V(0.5, 1, 1.5), VectorNodes.Scale(V(1, 2, 3), 0.5));
        Assert.Equal(0d, VectorNodes.Scale(V(1, 2, 3), 0).Length);
    }

    [Fact]
    public void Scale_ChangesLengthByTheAbsoluteFactor()
    {
        Assert.Equal(15d, VectorNodes.Length(VectorNodes.Scale(V(3, 0, 4), -3)), 12);
    }

    [Fact]
    public void Scale_NullVector_Throws()
    {
        Assert.Contains("Vector.Scale", Assert.Throws<ArgumentNullException>(() => VectorNodes.Scale(null!, 2)).Message);
    }

    [Fact]
    public void Negate_ReversesEveryComponent()
    {
        Assert.Equal(V(-1, 2, -3), VectorNodes.Negate(V(1, -2, 3)));
    }

    [Fact]
    public void Negate_ZeroComponentsStayPositiveZero()
    {
        var result = VectorNodes.Negate(V(0, 1, 0));

        Assert.Equal(0L, Bits(result.X));
        Assert.Equal(0L, Bits(result.Z));
        Assert.Equal(V(0, -1, 0), result);
    }

    [Fact]
    public void Negate_NullVector_Throws()
    {
        Assert.Contains("Vector.Negate", Assert.Throws<ArgumentNullException>(() => VectorNodes.Negate(null!)).Message);
    }

    // ------------------------------------------------ Vector.Length / Normalize

    [Theory]
    [InlineData(3, 4, 0, 5)]
    [InlineData(1, 2, 2, 3)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(-3, -4, 0, 5)]
    [InlineData(0, 0, 7, 7)]
    public void Length_IsTheEuclideanMagnitude(double x, double y, double z, double expected)
    {
        Assert.Equal(expected, VectorNodes.Length(V(x, y, z)), 12);
    }

    [Fact]
    public void Length_NullVector_Throws()
    {
        Assert.Contains("Vector.Length", Assert.Throws<ArgumentNullException>(() => VectorNodes.Length(null!)).Message);
    }

    [Fact]
    public void Normalize_GivesAUnitVectorWithTheSameDirection()
    {
        AssertVector(0.6, 0, 0.8, VectorNodes.Normalize(V(3, 0, 4)));
        AssertVector(0, -1, 0, VectorNodes.Normalize(V(0, -250, 0)));
    }

    [Fact]
    public void Normalize_ResultHasLengthOne_ForAnyMagnitude()
    {
        foreach (var scale in new[] { 1e-6, 0.001, 1, 1000, 1e9 })
        {
            var unit = VectorNodes.Normalize(V(1 * scale, -2 * scale, 2 * scale));
            Assert.Equal(1d, unit.Length, 12);
            AssertVector(1d / 3, -2d / 3, 2d / 3, unit);
        }
    }

    [Fact]
    public void Normalize_ZeroVector_IsAClearError()
    {
        var error = Assert.Throws<ArgumentException>(() => VectorNodes.Normalize(V(0, 0, 0)));

        Assert.Contains("Vector.Normalize", error.Message);
        Assert.Contains("zero length", error.Message);
        Assert.Contains("'vector'", error.Message);
    }

    [Fact]
    public void Normalize_NonFiniteVector_IsAnError()
    {
        Assert.Contains("not a number", Assert.Throws<ArgumentException>(() => VectorNodes.Normalize(V(double.NaN, 0, 0))).Message);
        Assert.Contains("too large", Assert.Throws<ArgumentException>(() => VectorNodes.Normalize(V(double.PositiveInfinity, 0, 0))).Message);
    }

    [Fact]
    public void Normalize_NullVector_Throws()
    {
        Assert.Contains("Vector.Normalize", Assert.Throws<ArgumentNullException>(() => VectorNodes.Normalize(null!)).Message);
    }

    // ------------------------------------------------------- Vector.Dot / Cross

    [Fact]
    public void Dot_ComputesTheScalarProduct()
    {
        Assert.Equal(32d, VectorNodes.Dot(V(1, 2, 3), V(4, 5, 6)));
        Assert.Equal(0d, VectorNodes.Dot(V(1, 0, 0), V(0, 1, 0)));
        Assert.Equal(-4d, VectorNodes.Dot(V(2, 0, 0), V(-2, 5, 0)));
    }

    [Fact]
    public void Dot_OfAVectorWithItself_IsLengthSquared_AndIsSymmetric()
    {
        var a = V(1, -2, 4);
        var b = V(3, 3, -1);

        Assert.Equal(21d, VectorNodes.Dot(a, a));
        Assert.Equal(VectorNodes.Dot(a, b), VectorNodes.Dot(b, a));
    }

    [Fact]
    public void Dot_NullArguments_Throw()
    {
        Assert.Contains("Vector.Dot", Assert.Throws<ArgumentNullException>(() => VectorNodes.Dot(null!, V(1, 1, 1))).Message);
        Assert.Contains("'b'", Assert.Throws<ArgumentNullException>(() => VectorNodes.Dot(V(1, 1, 1), null!)).Message);
    }

    [Fact]
    public void Cross_FollowsTheRightHandRule()
    {
        Assert.Equal(V(0, 0, 1), VectorNodes.Cross(V(1, 0, 0), V(0, 1, 0)));
        Assert.Equal(V(1, 0, 0), VectorNodes.Cross(V(0, 1, 0), V(0, 0, 1)));
        Assert.Equal(V(0, 1, 0), VectorNodes.Cross(V(0, 0, 1), V(1, 0, 0)));
        Assert.Equal(V(-3, 6, -3), VectorNodes.Cross(V(1, 2, 3), V(4, 5, 6)));
    }

    [Fact]
    public void Cross_IsAntiCommutative_AndPerpendicularToBothInputs()
    {
        var a = V(2, -1, 4);
        var b = V(-3, 5, 1);
        var cross = VectorNodes.Cross(a, b);

        Assert.Equal(VectorNodes.Negate(cross), VectorNodes.Cross(b, a));
        Assert.Equal(0d, VectorNodes.Dot(cross, a), 12);
        Assert.Equal(0d, VectorNodes.Dot(cross, b), 12);
    }

    [Fact]
    public void Cross_ParallelVectors_GiveTheZeroVector()
    {
        Assert.Equal(0d, VectorNodes.Cross(V(1, 2, 3), V(2, 4, 6)).Length);
        Assert.Equal(0d, VectorNodes.Cross(V(1, 2, 3), V(1, 2, 3)).Length);
    }

    [Fact]
    public void Cross_NullArguments_Throw()
    {
        Assert.Contains("Vector.Cross", Assert.Throws<ArgumentNullException>(() => VectorNodes.Cross(null!, V(1, 1, 1))).Message);
        Assert.Contains("'b'", Assert.Throws<ArgumentNullException>(() => VectorNodes.Cross(V(1, 1, 1), null!)).Message);
    }

    // ------------------------------------------------------------ Vector.Angle

    [Theory]
    [InlineData(1, 0, 0, 1, 0, 0, 0)]
    [InlineData(1, 0, 0, 0, 1, 0, 90)]
    [InlineData(1, 0, 0, -1, 0, 0, 180)]
    [InlineData(1, 0, 0, 1, 1, 0, 45)]
    [InlineData(0, 0, 1, 0, 1, 1, 45)]
    [InlineData(1, 1, 1, -1, -1, -1, 180)]
    [InlineData(1, 0, 0, -1, 1, 0, 135)]
    public void Angle_KnownAngles(double ax, double ay, double az, double bx, double by, double bz, double degrees)
    {
        Assert.Equal(degrees, VectorNodes.Angle(V(ax, ay, az), V(bx, by, bz)), 9);
    }

    [Fact]
    public void Angle_SixtyDegrees_AndTheAngleBetweenSkewVectors()
    {
        Assert.Equal(60d, VectorNodes.Angle(V(1, 0, 0), V(1, Math.Sqrt(3), 0)), 9);
        Assert.Equal(Math.Acos(32d / Math.Sqrt(14d * 77d)) * 180d / Math.PI, VectorNodes.Angle(V(1, 2, 3), V(4, 5, 6)), 9);
    }

    [Fact]
    public void Angle_IsIndependentOfLength_AndSymmetric()
    {
        var a = V(2, 1, -1);
        var b = V(-1, 3, 2);
        var angle = VectorNodes.Angle(a, b);

        Assert.Equal(angle, VectorNodes.Angle(VectorNodes.Scale(a, 1e6), VectorNodes.Scale(b, 1e-6)), 9);
        Assert.Equal(angle, VectorNodes.Angle(b, a), 12);
    }

    [Fact]
    public void Angle_VeryNarrowAngles_StayAccurate()
    {
        // acos(dot / (|a||b|)) rounds the cosine to exactly 1 here and would report 0 degrees.
        var angle = VectorNodes.Angle(V(1, 0, 0), V(1, 1e-9, 0));
        var expected = 1e-9 * 180d / Math.PI;

        Assert.Equal(expected, angle, 15);
        Assert.True(angle > 0d);
    }

    [Fact]
    public void Angle_ZeroLengthVector_NamesTheOffendingInput()
    {
        var first = Assert.Throws<ArgumentException>(() => VectorNodes.Angle(V(0, 0, 0), V(1, 0, 0)));
        Assert.Contains("Vector.Angle", first.Message);
        Assert.Contains("'a'", first.Message);
        Assert.Contains("zero length", first.Message);

        var second = Assert.Throws<ArgumentException>(() => VectorNodes.Angle(V(1, 0, 0), V(0, 0, 0)));
        Assert.Contains("'b'", second.Message);
    }

    [Fact]
    public void Angle_NullArguments_Throw()
    {
        Assert.Contains("Vector.Angle", Assert.Throws<ArgumentNullException>(() => VectorNodes.Angle(null!, V(1, 0, 0))).Message);
        Assert.Contains("'b'", Assert.Throws<ArgumentNullException>(() => VectorNodes.Angle(V(1, 0, 0), null!)).Message);
    }

    // ---------------------------------------------------------- Vector.ByPoints

    [Fact]
    public void ByPoints_IsEndMinusStart()
    {
        Assert.Equal(V(3, 3, 3), VectorNodes.ByPoints(new DyncameloPoint(1, 2, 3), new DyncameloPoint(4, 5, 6)));
        Assert.Equal(V(-3, -3, -3), VectorNodes.ByPoints(new DyncameloPoint(4, 5, 6), new DyncameloPoint(1, 2, 3)));
        Assert.Equal(V(0, 0, 0), VectorNodes.ByPoints(new DyncameloPoint(1, 2, 3), new DyncameloPoint(1, 2, 3)));
    }

    [Fact]
    public void ByPoints_LengthIsThePointDistance_AndAddingItReachesTheEnd()
    {
        var from = new DyncameloPoint(1, 1, 1);
        var to = new DyncameloPoint(4, 5, 1);
        var vector = VectorNodes.ByPoints(from, to);

        Assert.Equal(GeometryNodes.PointDistanceTo(from, to), VectorNodes.Length(vector), 12);
        Assert.Equal(to, SpatialNodes.PointTranslate(from, vector));
    }

    [Fact]
    public void ByPoints_NullArguments_NameTheMissingPort()
    {
        var noStart = Assert.Throws<ArgumentNullException>(() => VectorNodes.ByPoints(null!, new DyncameloPoint(0, 0, 0)));
        Assert.Contains("Vector.ByPoints", noStart.Message);
        Assert.Contains("'from'", noStart.Message);

        var noEnd = Assert.Throws<ArgumentNullException>(() => VectorNodes.ByPoints(new DyncameloPoint(0, 0, 0), null!));
        Assert.Contains("'to'", noEnd.Message);
    }

    // ------------------------------------------------------------------ axes

    [Fact]
    public void Axes_AreTheThreeUnitVectors()
    {
        Assert.Equal(V(1, 0, 0), VectorNodes.XAxis());
        Assert.Equal(V(0, 1, 0), VectorNodes.YAxis());
        Assert.Equal(V(0, 0, 1), VectorNodes.ZAxis());
        Assert.Equal(1d, VectorNodes.XAxis().Length);
    }

    [Fact]
    public void Axes_AreRightHanded_AndMutuallyPerpendicular()
    {
        Assert.Equal(VectorNodes.ZAxis(), VectorNodes.Cross(VectorNodes.XAxis(), VectorNodes.YAxis()));
        Assert.Equal(VectorNodes.XAxis(), VectorNodes.Cross(VectorNodes.YAxis(), VectorNodes.ZAxis()));
        Assert.Equal(90d, VectorNodes.Angle(VectorNodes.XAxis(), VectorNodes.ZAxis()), 12);
    }

    // ------------------------------------------------- Vector.IsParallel

    [Fact]
    public void IsParallel_SameAndOppositeDirections_AreParallel()
    {
        Assert.True(VectorNodes.IsParallel(V(1, 2, 3), V(2, 4, 6)));
        Assert.True(VectorNodes.IsParallel(V(1, 2, 3), V(-1, -2, -3)));
        Assert.True(VectorNodes.IsParallel(V(1, 2, 3), V(1, 2, 3)));
        Assert.True(VectorNodes.IsParallel(VectorNodes.XAxis(), V(-1e9, 0, 0)));
    }

    [Fact]
    public void IsParallel_OtherDirections_AreNot()
    {
        Assert.False(VectorNodes.IsParallel(V(1, 0, 0), V(0, 1, 0)));
        Assert.False(VectorNodes.IsParallel(V(1, 2, 3), V(1, 2, 4)));
    }

    [Fact]
    public void IsParallel_ToleranceIsTheSineOfTheAngle_NotDependentOnLength()
    {
        var a = V(1, 0, 0);
        var slightlyOff = V(1, 1e-6, 0);   // sine of the angle is about 1e-6

        Assert.False(VectorNodes.IsParallel(a, slightlyOff));                 // default 1e-9
        Assert.False(VectorNodes.IsParallel(a, slightlyOff, 1e-7));
        Assert.True(VectorNodes.IsParallel(a, slightlyOff, 1e-5));

        // Same directions, vastly different magnitudes: the verdict must not change.
        Assert.False(VectorNodes.IsParallel(VectorNodes.Scale(a, 1e6), VectorNodes.Scale(slightlyOff, 1e-6)));
        Assert.True(VectorNodes.IsParallel(VectorNodes.Scale(a, 1e6), VectorNodes.Scale(slightlyOff, 1e-6), 1e-5));
    }

    [Fact]
    public void IsParallel_ZeroTolerance_NeedsExactlyParallel()
    {
        Assert.True(VectorNodes.IsParallel(V(1, 1, 0), V(3, 3, 0), 0));
        Assert.False(VectorNodes.IsParallel(V(1, 1, 0), V(3, 3.0000001, 0), 0));
    }

    [Fact]
    public void IsParallel_ZeroLengthVector_IsAnError()
    {
        var error = Assert.Throws<ArgumentException>(() => VectorNodes.IsParallel(V(0, 0, 0), V(1, 0, 0)));
        Assert.Contains("Vector.IsParallel", error.Message);
        Assert.Contains("'a'", error.Message);
        Assert.Contains("'b'", Assert.Throws<ArgumentException>(() => VectorNodes.IsParallel(V(1, 0, 0), V(0, 0, 0))).Message);
    }

    [Theory]
    [InlineData(-1e-9)]
    [InlineData(double.NaN)]
    public void IsParallel_BadTolerance_IsAnError(double tolerance)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => VectorNodes.IsParallel(V(1, 0, 0), V(1, 0, 0), tolerance));
        Assert.Contains("Vector.IsParallel", error.Message);
        Assert.Contains("tolerance", error.Message);
    }

    [Fact]
    public void IsParallel_NullArguments_Throw()
    {
        Assert.Contains("Vector.IsParallel", Assert.Throws<ArgumentNullException>(() => VectorNodes.IsParallel(null!, V(1, 0, 0))).Message);
        Assert.Contains("'b'", Assert.Throws<ArgumentNullException>(() => VectorNodes.IsParallel(V(1, 0, 0), null!)).Message);
    }

    // ------------------------------------------------- Vector.IsPerpendicular

    [Fact]
    public void IsPerpendicular_RightAngles_Are()
    {
        Assert.True(VectorNodes.IsPerpendicular(V(1, 0, 0), V(0, 1, 0)));
        Assert.True(VectorNodes.IsPerpendicular(V(1, 1, 0), V(1, -1, 0)));
        Assert.True(VectorNodes.IsPerpendicular(V(2, -3, 5), VectorNodes.Cross(V(2, -3, 5), V(1, 1, 1)), 1e-9));
    }

    [Fact]
    public void IsPerpendicular_OtherAngles_AreNot()
    {
        Assert.False(VectorNodes.IsPerpendicular(V(1, 0, 0), V(1, 1, 0)));
        Assert.False(VectorNodes.IsPerpendicular(V(1, 0, 0), V(-1, 0, 0)));   // opposite, not perpendicular
        Assert.False(VectorNodes.IsPerpendicular(V(1, 0, 0), V(1, 0, 0)));
    }

    [Fact]
    public void IsPerpendicular_ToleranceIsTheCosineOfTheAngle_NotDependentOnLength()
    {
        var a = V(1, 0, 0);
        var almost = V(1e-6, 1, 0);        // cosine of the angle is about 1e-6

        Assert.False(VectorNodes.IsPerpendicular(a, almost));
        Assert.True(VectorNodes.IsPerpendicular(a, almost, 1e-5));
        Assert.False(VectorNodes.IsPerpendicular(VectorNodes.Scale(a, 1e6), VectorNodes.Scale(almost, 1e-6)));
        Assert.True(VectorNodes.IsPerpendicular(VectorNodes.Scale(a, 1e6), VectorNodes.Scale(almost, 1e-6), 1e-5));
    }

    [Fact]
    public void IsPerpendicular_ZeroLengthVector_IsAnError()
    {
        var error = Assert.Throws<ArgumentException>(() => VectorNodes.IsPerpendicular(V(1, 0, 0), V(0, 0, 0)));
        Assert.Contains("Vector.IsPerpendicular", error.Message);
        Assert.Contains("'b'", error.Message);
    }

    [Fact]
    public void IsPerpendicular_BadTolerance_IsAnErrorWithInvariantNumberText()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            comma.NumberFormat.NumberDecimalSeparator = ",";
            CultureInfo.CurrentCulture = comma;

            var error = Assert.Throws<ArgumentOutOfRangeException>(() => VectorNodes.IsPerpendicular(V(1, 0, 0), V(0, 1, 0), -0.5));
            Assert.Contains("-0.5", error.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void IsPerpendicular_NullArguments_Throw()
    {
        Assert.Contains("Vector.IsPerpendicular", Assert.Throws<ArgumentNullException>(() => VectorNodes.IsPerpendicular(null!, V(1, 0, 0))).Message);
    }

    // ---------------------------------------------- registration and engine

    private static NodeRegistry CreateRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    private static NodeDefinition Definition(NodeRegistry registry, string name) =>
        registry.Definitions.Single(d => d.Name == name);

    [Fact]
    public void Registration_EveryNodeIsImported_WithDescriptionAndTags_InTheGeometryCategory()
    {
        var registry = CreateRegistry();
        var names = new[]
        {
            "Vector.Components", "Vector.Add", "Vector.Subtract", "Vector.Scale", "Vector.Negate", "Vector.Length",
            "Vector.Normalize", "Vector.Dot", "Vector.Cross", "Vector.Angle", "Vector.ByPoints", "Vector.XAxis",
            "Vector.YAxis", "Vector.ZAxis", "Vector.IsParallel", "Vector.IsPerpendicular",
        };

        foreach (var name in names)
        {
            var definition = Definition(registry, name);   // Single: the name is unique in the whole library
            Assert.Equal("Geometry", definition.Category);
            Assert.False(string.IsNullOrWhiteSpace(definition.Description), name + " has no description");
            Assert.NotEmpty(definition.SearchTags);
        }
    }

    [Fact]
    public void Registration_MeasuringAndTestingNodesAreInfo_BuildingNodesAreCreate()
    {
        var registry = CreateRegistry();

        foreach (var name in new[] { "Vector.Components", "Vector.Length", "Vector.Dot", "Vector.Angle", "Vector.IsParallel", "Vector.IsPerpendicular" })
        {
            Assert.Equal(NodeFunction.Info, Definition(registry, name).Function);
        }

        foreach (var name in new[] { "Vector.Add", "Vector.Subtract", "Vector.Scale", "Vector.Negate", "Vector.Normalize", "Vector.Cross", "Vector.ByPoints", "Vector.XAxis", "Vector.YAxis", "Vector.ZAxis" })
        {
            Assert.Equal(NodeFunction.Create, Definition(registry, name).Function);
        }
    }

    [Fact]
    public void Registration_ComponentsHasThreeNumberPorts_AndAxesTakeNoInputs()
    {
        var registry = CreateRegistry();

        var components = Definition(registry, "Vector.Components");
        Assert.Equal(new[] { "x", "y", "z" }, components.Outputs.Select(o => o.Name));
        Assert.All(components.Outputs, o => Assert.Equal("number", o.Kind));

        foreach (var name in new[] { "Vector.XAxis", "Vector.YAxis", "Vector.ZAxis" })
        {
            Assert.Empty(Definition(registry, name).Inputs);
        }

        var parallel = Definition(registry, "Vector.IsParallel");
        Assert.Equal(new[] { "a", "b", "tolerance" }, parallel.Inputs.Select(i => i.Name));
        Assert.Equal(1e-9, parallel.Inputs[2].DefaultValue);
    }

    [Fact]
    public void Engine_PointsToVectorToLength_RunsAThreeFourFiveTriangle()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var three = new NumberInputNode { Value = 3 };
        var four = new NumberInputNode { Value = 4 };
        var start = new ZeroTouchNodeModel(Definition(registry, "Point.ByCoordinates"));
        var end = new ZeroTouchNodeModel(Definition(registry, "Point.ByCoordinates"));
        var between = new ZeroTouchNodeModel(Definition(registry, "Vector.ByPoints"));
        var length = new ZeroTouchNodeModel(Definition(registry, "Vector.Length"));
        var normalize = new ZeroTouchNodeModel(Definition(registry, "Vector.Normalize"));
        var parts = new ZeroTouchNodeModel(Definition(registry, "Vector.Components"));
        foreach (var node in new NodeModel[] { three, four, start, end, between, length, normalize, parts })
        {
            graph.AddNode(node);
        }

        Assert.True(graph.Connect(three.OutPorts[0], end.InPorts[1]).Success);   // y = 3
        Assert.True(graph.Connect(four.OutPorts[0], end.InPorts[2]).Success);    // z = 4
        Assert.True(graph.Connect(start.OutPorts[0], between.InPorts[0]).Success);
        Assert.True(graph.Connect(end.OutPorts[0], between.InPorts[1]).Success);
        Assert.True(graph.Connect(between.OutPorts[0], length.InPorts[0]).Success);
        Assert.True(graph.Connect(between.OutPorts[0], normalize.InPorts[0]).Success);
        Assert.True(graph.Connect(normalize.OutPorts[0], parts.InPorts[0]).Success);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(5d, (double)length.OutPorts[0].Value!, 12);
        Assert.Equal(0d, (double)parts.OutPorts[0].Value!, 12);
        Assert.Equal(0.6, (double)parts.OutPorts[1].Value!, 12);
        Assert.Equal(0.8, (double)parts.OutPorts[2].Value!, 12);
    }

    [Fact]
    public void Engine_ScaleReplicatesOverAListOfFactors()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var factors = new ListCreateNode();
        var one = new NumberInputNode { Value = 1 };
        var two = new NumberInputNode { Value = 2 };
        var axis = new ZeroTouchNodeModel(Definition(registry, "Vector.XAxis"));
        var scale = new ZeroTouchNodeModel(Definition(registry, "Vector.Scale"));
        foreach (var node in new NodeModel[] { factors, one, two, axis, scale })
        {
            graph.AddNode(node);
        }

        factors.AddItemPort();
        Assert.True(graph.Connect(one.OutPorts[0], factors.InPorts[0]).Success);
        Assert.True(graph.Connect(two.OutPorts[0], factors.InPorts[1]).Success);
        Assert.True(graph.Connect(axis.OutPorts[0], scale.InPorts[0]).Success);
        Assert.True(graph.Connect(factors.OutPorts[0], scale.InPorts[1]).Success);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Executed, scale.State);
        var vectors = Assert.IsAssignableFrom<IEnumerable<object?>>(scale.OutPorts[0].Value).ToArray();
        Assert.Equal(new object?[] { V(1, 0, 0), V(2, 0, 0) }, vectors);
    }

    [Fact]
    public void Engine_ZeroVectorNormalize_BecomesAnErrorNodeWithTheMessage()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var zero = new ZeroTouchNodeModel(Definition(registry, "Vector.ByCoordinates"));
        var normalize = new ZeroTouchNodeModel(Definition(registry, "Vector.Normalize"));
        graph.AddNode(zero);
        graph.AddNode(normalize);
        Assert.True(graph.Connect(zero.OutPorts[0], normalize.InPorts[0]).Success);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Error, normalize.State);
        Assert.Contains("zero length", normalize.StateMessage);
    }
}
