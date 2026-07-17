using System;
using System.Collections.Generic;
using Dyncamelo.Core.Loader;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>
/// The XML documentation-id builder must reproduce the compiler's member ids
/// exactly, or every port description lookup silently misses.
/// </summary>
public class XmlDocumentationTests
{
    private static string IdOf(string methodName) =>
        XmlDocumentation.GetMethodDocId(typeof(DocIdFixture).GetMethod(methodName)!);

    [Fact]
    public void Parameterless_HasNoParentheses()
    {
        Assert.Equal(
            "M:Dyncamelo.Core.Tests.XmlDocumentationTests.DocIdFixture.NoParams",
            IdOf(nameof(DocIdFixture.NoParams)));
    }

    [Fact]
    public void SimpleParameters_UseFullTypeNames()
    {
        Assert.Equal(
            "M:Dyncamelo.Core.Tests.XmlDocumentationTests.DocIdFixture.Simple(System.String,System.Double,System.Boolean)",
            IdOf(nameof(DocIdFixture.Simple)));
    }

    [Fact]
    public void GenericInstantiations_UseBraces()
    {
        Assert.Equal(
            "M:Dyncamelo.Core.Tests.XmlDocumentationTests.DocIdFixture.Generic(System.Collections.Generic.IEnumerable{System.Object},System.Collections.Generic.IDictionary{System.String,System.Object})",
            IdOf(nameof(DocIdFixture.Generic)));
    }

    [Fact]
    public void NullableAndArray_AreEncoded()
    {
        Assert.Equal(
            "M:Dyncamelo.Core.Tests.XmlDocumentationTests.DocIdFixture.NullableAndArray(System.Nullable{System.Double},System.String[])",
            IdOf(nameof(DocIdFixture.NullableAndArray)));
    }

    [Fact]
    public void NestedGeneric_IsEncodedRecursively()
    {
        Assert.Equal(
            "M:Dyncamelo.Core.Tests.XmlDocumentationTests.DocIdFixture.Nested(System.Collections.Generic.List{System.Collections.Generic.List{System.Int32}})",
            IdOf(nameof(DocIdFixture.Nested)));
    }

    /// <summary>Methods whose signatures cover the constructs zero-touch nodes use.</summary>
    public static class DocIdFixture
    {
        /// <summary>No parameters.</summary>
        public static void NoParams()
        {
        }

        /// <summary>Simple parameters.</summary>
        /// <param name="a">a.</param>
        /// <param name="b">b.</param>
        /// <param name="c">c.</param>
        public static void Simple(string a, double b, bool c)
        {
        }

        /// <summary>Generic parameters.</summary>
        /// <param name="items">items.</param>
        /// <param name="map">map.</param>
        public static void Generic(IEnumerable<object> items, IDictionary<string, object> map)
        {
        }

        /// <summary>Nullable + array parameters.</summary>
        /// <param name="value">value.</param>
        /// <param name="names">names.</param>
        public static void NullableAndArray(double? value, string[] names)
        {
        }

        /// <summary>Nested generic parameter.</summary>
        /// <param name="grid">grid.</param>
        public static void Nested(List<List<int>> grid)
        {
        }
    }
}
