using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>The inline colour swatch stores "#AARRGGBB"; the engine must turn that back into a colour.</summary>
public class InlineColourConverterTests
{
    [Fact]
    public void HexStringCoercesToAColourOnceTheNodePackIsRegistered()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        Assert.True(TypeCoercion.CanConvert(typeof(string), typeof(DyncameloColor)));
        var colour = Assert.IsType<DyncameloColor>(TypeCoercion.Coerce("#80FF8800", typeof(DyncameloColor)));
        Assert.Equal(128, colour.A);
        Assert.Equal(255, colour.R);
        Assert.Equal(136, colour.G);
        Assert.Equal(0, colour.B);
    }
}
