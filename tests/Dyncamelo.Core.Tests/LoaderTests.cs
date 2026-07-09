using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Tests.Fixtures;
using Xunit;

namespace Dyncamelo.Core.Tests;

public class LoaderTests
{
    [Fact]
    public void FunctionSignature_IsStableManglingOfNamespaceClassMethodAndParams()
    {
        var definition = ZT.Definition("Add");
        Assert.Equal("Dyncamelo.Core.Tests.Fixtures.MathFixtures.Add@double,double", definition.Id);
    }

    [Fact]
    public void ParameterlessMethod_HasNoParameterSuffix()
    {
        var definition = ZT.Definition("Answer");
        Assert.Equal("Dyncamelo.Core.Tests.Fixtures.MathFixtures.Answer", definition.Id);
    }

    [Fact]
    public void ListParameter_IsMangledWithGenericArguments()
    {
        var definition = ZT.Definition("Sum");
        Assert.Equal(
            "Dyncamelo.Core.Tests.Fixtures.MathFixtures.Sum@System.Collections.Generic.IList<double>",
            definition.Id);
    }

    [Fact]
    public void DefaultName_IsClassDotMethod_AttributeOverrides()
    {
        Assert.Equal("MathFixtures.Add", ZT.Definition("Add").Name);
        Assert.Equal("Square Root", ZT.Definition("Sqrt").Name);
    }

    [Fact]
    public void Description_And_SearchTags_ComeFromAttributes()
    {
        var definition = ZT.Definition("Sqrt");
        Assert.Equal("Returns the square root of a number.", definition.Description);
        Assert.Equal(new[] { "sqrt", "root" }, definition.SearchTags);
    }

    [Fact]
    public void Category_DefaultsToNamespaceMinusAssemblyPrefix_PlusClassName()
    {
        Assert.Equal("Fixtures.MathFixtures", ZT.Definition("Add").Category);
    }

    [Fact]
    public void Category_AttributeOverrides()
    {
        Assert.Equal("Custom.Category", ZT.Definition("Answer").Category);
    }

    [Fact]
    public void OptionalParameter_BecomesDefaultedPort()
    {
        var definition = ZT.Definition("AddStep");
        var step = definition.Inputs.Single(p => p.Name == "step");
        Assert.True(step.HasDefault);
        Assert.Equal(1.0, step.DefaultValue);
        Assert.False(definition.Inputs.Single(p => p.Name == "x").HasDefault);
    }

    [Fact]
    public void MultiReturn_ProducesOnePortPerKey_InAttributeOrder()
    {
        var definition = ZT.Definition("DivMod");
        Assert.Equal(new[] { "quotient", "remainder" }, definition.Outputs.Select(o => o.Name));
        Assert.Equal(new[] { "quotient", "remainder" }, definition.MultiReturnKeys);
    }

    [Fact]
    public void Overloads_GetDistinctDefinitionIds()
    {
        var overloads = ZT.All.Where(d => d.Method.Name == "Overloaded").ToList();
        Assert.Equal(2, overloads.Count);
        Assert.Contains(overloads, d => d.Id.EndsWith("Overloaded@int"));
        Assert.Contains(overloads, d => d.Id.EndsWith("Overloaded@int,int"));
        Assert.Equal(overloads[0].Name, overloads[1].Name); // same display name
    }

    [Fact]
    public void HiddenMethods_And_RefOutMethods_AreSkipped()
    {
        Assert.DoesNotContain(ZT.All, d => d.Method.Name == "Hidden");
        Assert.DoesNotContain(ZT.All, d => d.Method.Name == "TryParse"); // has out parameter
    }

    [Fact]
    public void VoidMethod_GetsPassThroughOutputPort()
    {
        var definition = ZT.Definition("Consume");
        Assert.True(definition.IsVoid);
        Assert.Single(definition.Outputs);
        Assert.Equal("result", definition.Outputs[0].Name);
    }

    [Fact]
    public void LoadFrom_Assembly_FindsFixtureDefinitions()
    {
        var definitions = AssemblyNodeLoader.LoadFrom(typeof(MathFixtures).Assembly);
        Assert.Contains(definitions, d => d.Id == "Dyncamelo.Core.Tests.Fixtures.MathFixtures.Add@double,double");
    }

    [Fact]
    public void ZeroTouchNode_BuildsPortsFromDefinition()
    {
        var node = ZT.Node("AddStep");
        Assert.Equal(new[] { "x", "step" }, node.InPorts.Select(p => p.Name));
        Assert.Single(node.OutPorts);
        Assert.True(node.InPorts[1].HasDefault);
        Assert.True(node.InPorts[1].UsingDefaultValue);
        Assert.Equal(typeof(double), node.InPorts[0].DeclaredType);
    }

    [Fact]
    public void Registry_ResolvesDefinitions_And_BuiltInNodeTypes()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterDefinitions(ZT.All);

        Assert.True(registry.TryGetDefinition("Dyncamelo.Core.Tests.Fixtures.MathFixtures.Add@double,double", out var definition));
        Assert.NotNull(definition);

        var zeroTouch = registry.CreateZeroTouchNode(definition!.Id);
        Assert.NotNull(zeroTouch);

        var slider = registry.CreateNode(NumberSliderNode.TypeName);
        Assert.IsType<NumberSliderNode>(slider);

        Assert.Null(registry.CreateZeroTouchNode("No.Such.Method@double"));
        Assert.Null(registry.CreateNode("NoSuchType"));
    }
}
