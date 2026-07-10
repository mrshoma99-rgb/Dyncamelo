using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>
/// Fixture for the loader hook: the marked method must run exactly once per
/// process when the test assembly is registered with a <see cref="NodeRegistry"/>.
/// Hidden from the library so it never contributes node definitions.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ConverterHookFixture
{
    /// <summary>Number of times the hook ran (must end up exactly 1 per process).</summary>
    public static int InvocationCount;

    [TypeConverterRegistration]
    public static void Register()
    {
        InvocationCount++;
    }
}

/// <summary>
/// Fixture proving a [TypeConverterRegistration] method inside a VISIBLE node
/// class is still excluded from zero-touch import (it is infrastructure, not a node).
/// </summary>
public static class ConverterHookNodeScanFixture
{
    [TypeConverterRegistration]
    public static void RegisterNothing()
    {
        // intentionally empty — idempotent no-op registration
    }

    public static double Twice(double x) => x * 2;
}

/// <summary>
/// Tests for the pluggable type-converter registry (TypeCoercion.RegisterConverter):
/// connection compatibility, runtime coercion, element-wise list coercion, and
/// the assembly registration hook. Regression coverage for the "Color node
/// output cannot feed Appearance.OverrideColor" bug.
/// </summary>
public class TypeConverterRegistryTests
{
    // Local stand-ins for a node-pack color type and a host API color type:
    // unrelated classes with no common base and no IConvertible support.
    private class PackColor
    {
        public PackColor(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    private sealed class DerivedPackColor : PackColor
    {
        public DerivedPackColor(int value)
            : base(value)
        {
        }
    }

    private sealed class HostColor
    {
        public HostColor(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    private sealed class PackColorSourceNode : NodeModel
    {
        public PackColorSourceNode()
        {
            Name = "PackColorSource";
            AddOutput("color", typeof(PackColor));
        }

        public override string NodeType => "TestPackColorSource";

        public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
        {
            return new object?[] { new PackColor(7) };
        }
    }

    private sealed class HostColorTargetNode : NodeModel
    {
        public HostColorTargetNode()
        {
            Name = "HostColorTarget";
            AddInput("color", typeof(HostColor));
            AddOutput("result", typeof(object));
        }

        public object? Received { get; private set; }

        public override string NodeType => "TestHostColorTarget";

        public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
        {
            Received = inputs[0];
            return new object?[] { inputs[0] };
        }
    }

    private static IDisposable RegisterPackToHost()
    {
        TypeCoercion.RegisterConverter(
            typeof(PackColor),
            typeof(HostColor),
            value => new HostColor(((PackColor)value).Value));
        return new Unregister(typeof(PackColor), typeof(HostColor));
    }

    private sealed class Unregister : IDisposable
    {
        private readonly Type _source;
        private readonly Type _target;

        public Unregister(Type source, Type target)
        {
            _source = source;
            _target = target;
        }

        public void Dispose()
        {
            TypeCoercion.UnregisterConverter(_source, _target);
        }
    }

    [Fact]
    public void CanConvert_UnrelatedTypesWithoutConverter_IsFalse()
    {
        Assert.False(TypeCoercion.CanConvert(typeof(PackColor), typeof(HostColor)));
    }

    [Fact]
    public void CanConvert_BecomesTrueWithConverter_AndFalseAgainAfterUnregister()
    {
        using (RegisterPackToHost())
        {
            Assert.True(TypeCoercion.CanConvert(typeof(PackColor), typeof(HostColor)));
        }

        Assert.False(TypeCoercion.CanConvert(typeof(PackColor), typeof(HostColor)));
    }

    [Fact]
    public void TryCoerce_UsesRegisteredConverter()
    {
        using (RegisterPackToHost())
        {
            Assert.True(TypeCoercion.TryCoerce(new PackColor(42), typeof(HostColor), out var result));
            var host = Assert.IsType<HostColor>(result);
            Assert.Equal(42, host.Value);
        }
    }

    [Fact]
    public void TryCoerce_WithoutConverter_Fails()
    {
        Assert.False(TypeCoercion.TryCoerce(new PackColor(42), typeof(HostColor), out _));
        Assert.Throws<InvalidCastException>(() => TypeCoercion.Coerce(new PackColor(42), typeof(HostColor)));
    }

    [Fact]
    public void TryCoerce_ConverterAppliesToDerivedSourceInstances()
    {
        using (RegisterPackToHost())
        {
            Assert.True(TypeCoercion.TryCoerce(new DerivedPackColor(5), typeof(HostColor), out var result));
            Assert.Equal(5, Assert.IsType<HostColor>(result).Value);
        }
    }

    [Fact]
    public void TryCoerce_ConvertsListElementsElementWise()
    {
        using (RegisterPackToHost())
        {
            var source = new List<object> { new PackColor(1), new PackColor(2) };
            Assert.True(TypeCoercion.TryCoerce(source, typeof(List<HostColor>), out var result));
            var converted = Assert.IsType<List<HostColor>>(result);
            Assert.Equal(new[] { 1, 2 }, converted.Select(c => c.Value));
        }
    }

    [Fact]
    public void TryCoerce_ThrowingConverter_ReturnsFalseInsteadOfThrowing()
    {
        TypeCoercion.RegisterConverter(
            typeof(PackColor),
            typeof(HostColor),
            value => throw new InvalidOperationException("boom"));
        try
        {
            Assert.False(TypeCoercion.TryCoerce(new PackColor(1), typeof(HostColor), out var result));
            Assert.Null(result);
        }
        finally
        {
            TypeCoercion.UnregisterConverter(typeof(PackColor), typeof(HostColor));
        }
    }

    [Fact]
    public void Connect_RejectedWithoutConverter_AcceptedWithConverter()
    {
        var graph = new GraphModel();
        var source = new PackColorSourceNode();
        var target = new HostColorTargetNode();
        graph.AddNode(source);
        graph.AddNode(target);

        var rejected = graph.Connect(source.OutPorts[0], target.InPorts[0]);
        Assert.False(rejected.Success);

        using (RegisterPackToHost())
        {
            var accepted = graph.Connect(source.OutPorts[0], target.InPorts[0]);
            Assert.True(accepted.Success);
        }
    }

    [Fact]
    public void Engine_CoercesConnectedValueThroughConverterAtRunTime()
    {
        using (RegisterPackToHost())
        {
            var graph = new GraphModel();
            var source = new PackColorSourceNode();
            var target = new HostColorTargetNode();
            graph.AddNode(source);
            graph.AddNode(target);
            Assert.True(graph.Connect(source.OutPorts[0], target.InPorts[0]).Success);

            var engine = new GraphEngine();
            engine.Run(graph);

            Assert.Equal(NodeState.Executed, target.State);
            var received = Assert.IsType<HostColor>(target.Received);
            Assert.Equal(7, received.Value);
        }
    }

    [Fact]
    public void RegisterAssembly_RunsConverterRegistrationHooks_ExactlyOncePerProcess()
    {
        var registry = new NodeRegistry();
        registry.RegisterAssembly(typeof(ConverterHookFixture).Assembly);
        Assert.Equal(1, ConverterHookFixture.InvocationCount);

        // A second registration (same or different registry) must not re-run the hook.
        registry.RegisterAssembly(typeof(ConverterHookFixture).Assembly);
        new NodeRegistry().RegisterAssembly(typeof(ConverterHookFixture).Assembly);
        Assert.Equal(1, ConverterHookFixture.InvocationCount);
    }

    [Fact]
    public void TypeConverterRegistrationMethods_AreNeverImportedAsNodes()
    {
        var definitions = AssemblyNodeLoader.LoadType(typeof(ConverterHookNodeScanFixture));
        var definition = Assert.Single(definitions);
        Assert.Contains("Twice", definition.Id);
    }
}
