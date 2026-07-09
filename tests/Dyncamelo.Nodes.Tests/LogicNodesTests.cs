using System;
using System.Collections.Generic;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class LogicNodesTests
{
    [Fact]
    public void If_SelectsBranchByCondition()
    {
        Assert.Equal("yes", LogicNodes.If(true, "yes", "no"));
        Assert.Equal("no", LogicNodes.If(false, "yes", "no"));
    }

    [Fact]
    public void If_PassesWholeListsThrough()
    {
        var trueBranch = new List<object> { 1, 2, 3 };
        Assert.Same(trueBranch, LogicNodes.If(true, trueBranch, "fallback"));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void And_IsLogicalConjunction(bool a, bool b, bool expected)
    {
        Assert.Equal(expected, LogicNodes.And(a, b));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    public void Or_IsLogicalDisjunction(bool a, bool b, bool expected)
    {
        Assert.Equal(expected, LogicNodes.Or(a, b));
    }

    [Fact]
    public void Not_Inverts()
    {
        Assert.False(LogicNodes.Not(true));
        Assert.True(LogicNodes.Not(false));
    }

    [Fact]
    public void EqualTo_ComparesNumbersAcrossBoxedTypes()
    {
        Assert.True(LogicNodes.EqualTo(2, 2.0));
        Assert.True(LogicNodes.EqualTo(2L, (byte)2));
        Assert.False(LogicNodes.EqualTo(2, 2.5));
    }

    [Fact]
    public void EqualTo_ComparesStringsAndNulls()
    {
        Assert.True(LogicNodes.EqualTo("abc", "abc"));
        Assert.False(LogicNodes.EqualTo("abc", "ABC"));
        Assert.True(LogicNodes.EqualTo(null, null));
        Assert.False(LogicNodes.EqualTo(null, "x"));
    }

    [Fact]
    public void EqualTo_NumberAndStringAreNotEqual()
    {
        Assert.False(LogicNodes.EqualTo(2.0, "2"));
    }

    [Fact]
    public void GreaterThan_And_LessThan_CompareNumerically()
    {
        Assert.True(LogicNodes.GreaterThan(3, 2));
        Assert.False(LogicNodes.GreaterThan(2, 2));
        Assert.True(LogicNodes.LessThan(-1, 0));
        Assert.False(LogicNodes.LessThan(0, 0));
    }
}
