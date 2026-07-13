using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Core.Tests.Fixtures;

public enum Season
{
    Spring = 0,
    Summer = 1,
    Autumn = 2,
    Winter = 3,
}

/// <summary>Zero-touch fixture methods exercised by the loader and engine tests.</summary>
public static class MathFixtures
{
    public static double Add(double a, double b) => a + b;

    public static double Add3(double a, double b, double c) => a + b + c;

    [NodeName("Square Root")]
    [NodeDescription("Returns the square root of a number.")]
    [NodeSearchTags("sqrt", "root")]
    public static double Sqrt(double x) => Math.Sqrt(x);

    public static double Sum(IList<double> values) => values.Sum();

    public static int CountItems(IList<object> items) => items.Count;

    public static List<string> DictKeys(IDictionary<string, object> dict) => dict.Keys.ToList();

    public static List<double> MakeList(double a, double b, double c) => new List<double> { a, b, c };

    [MultiReturn("quotient", "remainder")]
    public static Dictionary<string, object> DivMod(int a, int b) =>
        new Dictionary<string, object> { { "quotient", a / b }, { "remainder", a % b } };

    [MultiReturn("a", "b")]
    public static Dictionary<string, object> OnlyA() =>
        new Dictionary<string, object> { { "a", 1 } };

    public static double AddStep(double x, double step = 1.0) => x + step;

    public static double Fail(double x) => throw new InvalidOperationException("boom");

    [NodeCategory("Custom.Category")]
    public static int Answer() => 42;

    public static int Overloaded(int a) => a;

    public static int Overloaded(int a, int b) => a + b;

    [IsVisibleInLibrary(false)]
    public static int Hidden() => 0;

    public static bool TryParse(string text, out int value) => int.TryParse(text, out value);

    public static string SeasonName(Season season) => season.ToString();

    /// <summary>Choice-parameter fixture: a defaulted port with a fixed set of allowed values.</summary>
    public static string Pick([NodeChoices("Alpha", "Beta", "Gamma")] string option = "Alpha") => option;

    public static void Consume(object item)
    {
        // side-effect placeholder; void methods pass their first input through
    }

    /// <summary>Returns nested LAZY sequences (LINQ iterators), never materialized lists.</summary>
    public static IEnumerable<IEnumerable<double>> LazyGrid() =>
        Enumerable.Range(0, 2).Select(i => Enumerable.Range(0, 3).Select(j => (double)(i * 10 + j)));

    /// <summary>Optional non-nullable struct parameter with no compile-time constant ("= default").</summary>
    public static string StampIt(string text, DateTime when = default) =>
        text + "|" + when.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Optional Guid parameter declared "= default".</summary>
    public static Guid GuidThing(Guid id = default) => id;

    /// <summary>
    /// Simulates a shipped node whose signature grew an optional parameter:
    /// the pre-change definition id ("...Doubler@double") is kept loadable via
    /// <see cref="NodeAliasesAttribute"/> and the appended parameter's default
    /// preserves the old behavior exactly.
    /// </summary>
    [NodeAliases("Dyncamelo.Core.Tests.Fixtures.MathFixtures.Doubler@double")]
    public static double Doubler(double x, double scale = 2.0) => x * scale;
}

/// <summary>Node that reports its own failure via an Error message without throwing.</summary>
public class SelfReportedErrorNode : NodeModel
{
    public SelfReportedErrorNode()
    {
        Name = "SelfError";
        AddInput("value", typeof(object), defaultValue: null);
        AddOutput("result", typeof(object));
    }

    public override string NodeType => "TestSelfError";

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        AddMessage(MessageSeverity.Error, "self-reported failure");
        return new object?[] { null };
    }
}

/// <summary>Hand-written node emitting an arbitrary object; used to feed lists into graphs.</summary>
public class ValueNode : NodeModel
{
    private object? _value;

    public ValueNode()
    {
        Name = "Value";
        AddOutput("value", typeof(object));
    }

    public object? Value
    {
        get => _value;
        set
        {
            _value = value;
            MarkDirty();
        }
    }

    public override string NodeType => "TestValue";

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
    {
        return new object?[] { _value };
    }
}

/// <summary>Helpers shared by the test classes.</summary>
public static class ZT
{
    private static readonly List<NodeDefinition> Definitions = AssemblyNodeLoader.LoadType(typeof(MathFixtures));

    public static NodeDefinition Definition(string methodName) =>
        Definitions.Single(d => d.Method.Name == methodName);

    public static NodeDefinition DefinitionById(string id) =>
        Definitions.Single(d => d.Id == id);

    public static IReadOnlyList<NodeDefinition> All => Definitions;

    /// <summary>Creates a zero-touch node for a (non-overloaded) fixture method.</summary>
    public static ZeroTouchNodeModel Node(string methodName) =>
        new ZeroTouchNodeModel(Definition(methodName));

    /// <summary>Adds a ValueNode with the given payload to the graph.</summary>
    public static ValueNode Value(GraphModel graph, object? value)
    {
        var node = new ValueNode { Value = value };
        graph.AddNode(node);
        return node;
    }

    /// <summary>Connects two nodes by port index and asserts success.</summary>
    public static void Wire(GraphModel graph, NodeModel from, int fromPort, NodeModel to, int toPort)
    {
        var result = graph.Connect(from.OutPorts[fromPort], to.InPorts[toPort]);
        if (!result.Success)
        {
            throw new InvalidOperationException("Test wiring failed: " + result.Message);
        }
    }
}
