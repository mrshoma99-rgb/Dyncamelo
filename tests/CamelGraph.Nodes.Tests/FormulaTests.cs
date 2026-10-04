using System;
using Xunit;

namespace CamelGraph.Nodes.Tests;

public class FormulaTests
{
    private static double Eval(string expression, double a = 0, double b = 0, double c = 0, double d = 0, double e = 0, double f = 0) =>
        MathExtraNodes.Formula(expression, a, b, c, d, e, f);

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("10 - 4 - 3", 3)]
    [InlineData("100 / 10 / 5", 2)]
    [InlineData("2 ^ 3 ^ 2", 512)]
    [InlineData("-2 ^ 2", -4)]
    [InlineData("2 ^ -1", 0.5)]
    [InlineData("7 % 4", 3)]
    [InlineData("1.5e2 + 1", 151)]
    [InlineData(".5 + .25", 0.75)]
    [InlineData("--3", 3)]
    [InlineData("+3", 3)]
    public void Arithmetic_FollowsTheUsualPrecedence(string expression, double expected)
    {
        Assert.Equal(expected, Eval(expression), 9);
    }

    [Fact]
    public void Variables_AreTheSixInputs()
    {
        Assert.Equal(1 + 20 + 300 + 4000 + 50000 + 600000, Eval("a + b + c + d + e + f", 1, 20, 300, 4000, 50000, 600000));
        Assert.Equal(6d, Eval("A * B", 2, 3));
    }

    [Fact]
    public void ANumberFollowedByTheVariableE_IsAnImplicitProductError_NotAnExponent()
    {
        // "2e" is not a number with an exponent (no digits follow), so it is "2" then the name "e": a syntax error rather than 2 * e.
        Assert.Throws<FormatException>(() => Eval("2e", e: 3));
        Assert.Equal(6d, Eval("2*e", e: 3));
    }

    [Theory]
    [InlineData("abs(-3)", 3)]
    [InlineData("sqrt(16)", 4)]
    [InlineData("pow(2, 10)", 1024)]
    [InlineData("min(4, 2, 8)", 2)]
    [InlineData("max(4, 2, 8)", 8)]
    [InlineData("round(2.345, 2)", 2.35)]
    [InlineData("round(2.5)", 3)]
    [InlineData("floor(2.9)", 2)]
    [InlineData("ceil(2.1)", 3)]
    [InlineData("trunc(-2.9)", -2)]
    [InlineData("sign(-9)", -1)]
    [InlineData("clamp(15, 0, 10)", 10)]
    [InlineData("mod(7, 4)", 3)]
    [InlineData("hypot(3, 4)", 5)]
    [InlineData("ln(1)", 0)]
    [InlineData("log(1000)", 3)]
    [InlineData("log(8, 2)", 3)]
    [InlineData("exp(0)", 1)]
    [InlineData("deg(pi)", 180)]
    [InlineData("rad(180)", Math.PI)]
    [InlineData("sin(pi / 2)", 1)]
    [InlineData("cos(0)", 1)]
    [InlineData("atan2(1, 1) * 4", Math.PI)]
    [InlineData("tau", 2 * Math.PI)]
    [InlineData("SQRT(9)", 3)]
    public void Functions_AndConstants(string expression, double expected)
    {
        Assert.Equal(expected, Eval(expression), 9);
    }

    [Theory]
    [InlineData("3 > 2", 1)]
    [InlineData("3 < 2", 0)]
    [InlineData("3 >= 3", 1)]
    [InlineData("3 <= 2", 0)]
    [InlineData("3 == 3", 1)]
    [InlineData("3 = 3", 1)]
    [InlineData("3 != 3", 0)]
    [InlineData("1 && 0", 0)]
    [InlineData("1 || 0", 1)]
    [InlineData("!0", 1)]
    [InlineData("!(2 > 1)", 0)]
    [InlineData("true + true", 2)]
    [InlineData("if(2 > 1, 10, 20)", 10)]
    [InlineData("if(0, 10, 20)", 20)]
    [InlineData("1 + 1 == 2 && 2 * 2 == 4", 1)]
    public void Comparisons_AndLogic_UseOneAndZero(string expression, double expected)
    {
        Assert.Equal(expected, Eval(expression));
    }

    [Fact]
    public void ARealWorldFormula()
    {
        // overage over a 10 m allowance, never negative
        Assert.Equal(3.5, Eval("max(a - 10, 0)", 13.5));
        Assert.Equal(0d, Eval("max(a - 10, 0)", 4));
        // percent of total
        Assert.Equal(25d, Eval("a / b * 100", 1, 4));
    }

    [Fact]
    public void DivisionByZero_FollowsTheDivideNode()
    {
        Assert.Equal(double.PositiveInfinity, Eval("1 / 0"));
        Assert.True(double.IsNaN(Eval("0 / 0")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyExpression_IsRejected(string expression)
    {
        Assert.Throws<ArgumentException>(() => Eval(expression));
    }

    [Theory]
    [InlineData("1 +", "end of the expression")]
    [InlineData("(1 + 2", "expected ')'")]
    [InlineData("1 + * 2", "'*'")]
    [InlineData("1 2", "unexpected '2'")]
    [InlineData("1 $ 2", "unexpected character '$'")]
    [InlineData("x + 1", "unknown name 'x'")]
    [InlineData("foo(1)", "unknown function 'foo'")]
    [InlineData("sqrt(1, 2)", "takes 1 argument")]
    [InlineData("min(1)", "takes 2–16 argument")]
    [InlineData("sin()", "takes 1 argument")]
    public void SyntaxProblems_SayWhereAndWhat(string expression, string expectedFragment)
    {
        var ex = Assert.Throws<FormatException>(() => Eval(expression));
        Assert.Contains(expectedFragment, ex.Message);
        Assert.Contains("position", ex.Message);
        Assert.Contains(expression, ex.Message);
    }

    [Fact]
    public void TheSameExpressionCanBeEvaluatedRepeatedlyWithDifferentValues()
    {
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(i * 2 + 1, Eval("a * 2 + 1", i));
        }
    }
}
