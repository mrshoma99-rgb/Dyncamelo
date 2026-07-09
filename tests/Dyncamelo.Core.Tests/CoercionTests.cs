using System;
using System.Collections.Generic;
using Dyncamelo.Core.Tests.Fixtures;
using Dyncamelo.Core.Types;
using Xunit;

namespace Dyncamelo.Core.Tests;

public class CoercionTests
{
    [Fact]
    public void ExactMatch_PassesThrough()
    {
        Assert.True(TypeCoercion.TryCoerce(1.5, typeof(double), out var result));
        Assert.Equal(1.5, result);
    }

    [Fact]
    public void NumericWidening_IntToDouble()
    {
        Assert.True(TypeCoercion.TryCoerce(3, typeof(double), out var result));
        Assert.Equal(3.0, result);
        Assert.IsType<double>(result);
    }

    [Fact]
    public void NumericConversion_IntToLong_And_DoubleToInt()
    {
        Assert.True(TypeCoercion.TryCoerce(3, typeof(long), out var asLong));
        Assert.Equal(3L, asLong);

        Assert.True(TypeCoercion.TryCoerce(3.0, typeof(int), out var asInt));
        Assert.Equal(3, asInt);
    }

    [Fact]
    public void IConvertibleFallback_StringToDouble_InvariantCulture()
    {
        Assert.True(TypeCoercion.TryCoerce("2.5", typeof(double), out var result));
        Assert.Equal(2.5, result);
    }

    [Fact]
    public void IConvertibleFallback_NumberToString_InvariantCulture()
    {
        Assert.True(TypeCoercion.TryCoerce(2.5, typeof(string), out var result));
        Assert.Equal("2.5", result);
    }

    [Fact]
    public void ImpossibleConversion_Fails()
    {
        Assert.False(TypeCoercion.TryCoerce("not a number", typeof(double), out _));
        Assert.False(TypeCoercion.TryCoerce(new object(), typeof(double), out _));
    }

    [Fact]
    public void Coerce_Throws_OnFailure()
    {
        Assert.Throws<InvalidCastException>(() => TypeCoercion.Coerce("abc", typeof(int)));
    }

    [Fact]
    public void Null_IsAllowedForReferenceAndNullableTargets_RejectedForValueTypes()
    {
        Assert.True(TypeCoercion.TryCoerce(null, typeof(string), out var s));
        Assert.Null(s);
        Assert.True(TypeCoercion.TryCoerce(null, typeof(double?), out var d));
        Assert.Null(d);
        Assert.False(TypeCoercion.TryCoerce(null, typeof(double), out _));
    }

    [Fact]
    public void Enum_FromString_CaseInsensitive()
    {
        Assert.True(TypeCoercion.TryCoerce("winter", typeof(Season), out var result));
        Assert.Equal(Season.Winter, result);
    }

    [Fact]
    public void Enum_FromUnderlyingNumber()
    {
        Assert.True(TypeCoercion.TryCoerce(1, typeof(Season), out var result));
        Assert.Equal(Season.Summer, result);
    }

    [Fact]
    public void Enum_FromInvalidString_Fails()
    {
        Assert.False(TypeCoercion.TryCoerce("NotASeason", typeof(Season), out _));
    }

    [Fact]
    public void ListOfObjects_CoercesElementWise_ToTypedList()
    {
        var source = new List<object?> { 1.0, 2, "3.5" };
        Assert.True(TypeCoercion.TryCoerce(source, typeof(IList<double>), out var result));
        var typed = Assert.IsType<List<double>>(result);
        Assert.Equal(new List<double> { 1.0, 2.0, 3.5 }, typed);
    }

    [Fact]
    public void ListCoercion_ToArray()
    {
        var source = new List<object?> { 1, 2, 3 };
        Assert.True(TypeCoercion.TryCoerce(source, typeof(int[]), out var result));
        Assert.Equal(new[] { 1, 2, 3 }, (int[])result!);
    }

    [Fact]
    public void ListCoercion_Fails_WhenAnyElementFails()
    {
        var source = new List<object?> { 1.0, "oops" };
        Assert.False(TypeCoercion.TryCoerce(source, typeof(IList<double>), out _));
    }

    [Fact]
    public void ListIsNeverCoercedToScalar_AndViceVersa()
    {
        Assert.False(TypeCoercion.TryCoerce(new List<object?> { 1.0 }, typeof(double), out _));
        Assert.False(TypeCoercion.TryCoerce(1.0, typeof(IList<double>), out _));
    }

    [Fact]
    public void FormatValue_Scalars_Lists_Dictionaries_Null()
    {
        Assert.Equal("null", TypeCoercion.FormatValue(null));
        Assert.Equal("1.5", TypeCoercion.FormatValue(1.5));
        Assert.Equal("text", TypeCoercion.FormatValue("text"));
        Assert.Equal("[1, [2, 3], null]", TypeCoercion.FormatValue(
            new List<object?> { 1.0, new List<object?> { 2.0, 3.0 }, null }));
        Assert.Equal("{a : 1}", TypeCoercion.FormatValue(
            new Dictionary<string, object> { { "a", 1 } }));
    }

    [Fact]
    public void CanConvert_IsPermissive_ButRejectsHopelessPairs()
    {
        Assert.True(TypeCoercion.CanConvert(typeof(object), typeof(double)));
        Assert.True(TypeCoercion.CanConvert(typeof(double), typeof(object)));
        Assert.True(TypeCoercion.CanConvert(typeof(int), typeof(double)));
        Assert.True(TypeCoercion.CanConvert(typeof(string), typeof(Season)));
        Assert.True(TypeCoercion.CanConvert(typeof(List<double>), typeof(double))); // replication
        Assert.False(TypeCoercion.CanConvert(typeof(Guid), typeof(Uri)));
    }
}
