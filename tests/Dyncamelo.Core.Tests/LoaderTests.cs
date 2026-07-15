using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Graph;
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
    public void NodeFunction_Attribute_Overrides_NameHeuristic()
    {
        // Peek(x) would heuristically be Create-or-Modify, but the attribute wins.
        Assert.Equal(NodeFunction.Info, ZT.Definition("Peek").Function);
    }

    [Fact]
    public void NodeFunction_HeuristicsFromMethodName()
    {
        Assert.Equal(NodeFunction.Info, ZT.Definition("CountItems").Function);   // "Count…" reads
        Assert.Equal(NodeFunction.Create, ZT.Definition("MakeList").Function);   // "Make…" produces
    }

    [Fact]
    public void NodeFunction_ReaderNoun_IsInfo()
    {
        // A model op with no mutation verb and no factory prefix reads data → Info.
        Assert.Equal(NodeFunction.Info, ZT.Definition("Comments").Function);
    }

    [Fact]
    public void NodeFunction_ActionVerb_IsModify()
    {
        // "Isolate" is an action, not an "Is…" predicate.
        Assert.Equal(NodeFunction.Modify, ZT.Definition("Isolate").Function);
    }

    [Fact]
    public void NodeFunction_DataPackTransform_IsCreate()
    {
        // A mutating-sounding op in a pure-data category still produces a new value.
        Assert.Equal(NodeFunction.Create, ZT.Definition("SortValues").Function);
    }

    [Fact]
    public void ZeroTouchNode_ReportsDefinitionFunction()
    {
        Assert.Equal(NodeFunction.Info, ZT.Node("Peek").Function);
    }

    [Fact]
    public void ChoicesAttribute_PopulatesPortDescriptorChoices()
    {
        var option = ZT.Definition("Pick").Inputs.Single(p => p.Name == "option");
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, option.Choices);
    }

    [Fact]
    public void ParameterWithoutChoicesAttribute_HasNullChoices()
    {
        Assert.Null(ZT.Definition("AddStep").Inputs.Single(p => p.Name == "step").Choices);
    }

    [Fact]
    public void ZeroTouchNode_CarriesChoicesOntoInputPort()
    {
        var port = ZT.Node("Pick").InPorts.Single(p => p.Name == "option");
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, port.Choices);
    }

    [Fact]
    public void OptionalStructParameter_DeclaredDefault_GetsDefaultOfT()
    {
        // C# stores no compile-time constant for "= default" on non-nullable
        // structs like DateTime/Guid (RawDefaultValue is null); the loader must
        // synthesize default(T) so the defaulted port is usable.
        var when = ZT.Definition("StampIt").Inputs.Single(p => p.Name == "when");
        Assert.True(when.HasDefault);
        Assert.Equal(default(DateTime), when.DefaultValue);

        var id = ZT.Definition("GuidThing").Inputs.Single(p => p.Name == "id");
        Assert.True(id.HasDefault);
        Assert.Equal(Guid.Empty, id.DefaultValue);
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

    [Fact]
    public void Aliases_AreHarvestedFromAttribute()
    {
        Assert.Equal(
            new[] { "Dyncamelo.Core.Tests.Fixtures.MathFixtures.Doubler@double" },
            ZT.Definition("Doubler").Aliases);
        Assert.Empty(ZT.Definition("Add").Aliases);
    }

    [Fact]
    public void Registry_ResolvesLegacyAliasIds_ToTheCurrentDefinition()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterDefinitions(ZT.All);

        // The legacy id resolves...
        Assert.True(registry.TryGetDefinition(
            "Dyncamelo.Core.Tests.Fixtures.MathFixtures.Doubler@double", out var definition));

        // ...to the CURRENT definition (new id, new ports).
        Assert.Equal("Dyncamelo.Core.Tests.Fixtures.MathFixtures.Doubler@double,double", definition!.Id);

        var node = registry.CreateZeroTouchNode("Dyncamelo.Core.Tests.Fixtures.MathFixtures.Doubler@double");
        Assert.NotNull(node);
        Assert.Equal(new[] { "x", "scale" }, node!.InPorts.Select(p => p.Name));
        Assert.True(node.InPorts[1].UsingDefaultValue); // appended parameter keeps its default
    }

    [Fact]
    public void Alias_NeverShadowsADefinitionThatOwnsTheIdExactly()
    {
        var current = ZT.Definition("Doubler");
        // A (hypothetical) definition whose REAL id equals Doubler's alias.
        var owner = new NodeDefinition(
            "Dyncamelo.Core.Tests.Fixtures.MathFixtures.Doubler@double",
            current.AssemblyName,
            ZT.Definition("Sqrt").Method);

        // Exact ids win over aliases in either registration order.
        var registry = new NodeRegistry();
        registry.RegisterDefinition(current);
        registry.RegisterDefinition(owner);
        Assert.True(registry.TryGetDefinition(owner.Id, out var resolved));
        Assert.Same(owner, resolved);

        registry = new NodeRegistry();
        registry.RegisterDefinition(owner);
        registry.RegisterDefinition(current);
        Assert.True(registry.TryGetDefinition(owner.Id, out resolved));
        Assert.Same(owner, resolved);
    }
}
