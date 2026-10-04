using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>The inline colour swatch stores "#AARRGGBB"; the engine must turn that back into a colour.</summary>
public class InlineColourConverterTests
{
    [Fact]
    public void HexStringCoercesToAColourOnceTheNodePackIsRegistered()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        Assert.True(TypeCoercion.CanConvert(typeof(string), typeof(CamelGraphColor)));
        var colour = Assert.IsType<CamelGraphColor>(TypeCoercion.Coerce("#80FF8800", typeof(CamelGraphColor)));
        Assert.Equal(128, colour.A);
        Assert.Equal(255, colour.R);
        Assert.Equal(136, colour.G);
        Assert.Equal(0, colour.B);
    }
}
