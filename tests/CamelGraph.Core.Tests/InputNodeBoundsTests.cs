using CamelGraph.Core.Execution;
using CamelGraph.Core.Nodes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>
/// Input nodes keep their own data consistent (audit VAL-07): a loaded Choice names an option it really offers, and a slider's Min never
/// exceeds its Max.
/// </summary>
public class InputNodeBoundsTests
{
    // ── Choice ──────────────────────────────────────────────────────────────

    [Fact]
    public void Choice_LoadedWithAValueThatIsNotAnOption_FallsBackToTheFirstOption()
    {
        var node = new ChoiceInputNode();

        node.DeserializeData(new JObject { ["Options"] = "A\nB", ["Value"] = "Z" });

        // OLD: Value stayed 'Z' while the output said 'A' (and the Player dropdown showed nothing selected).
        Assert.Equal("A", node.Value);
        var outputs = node.Evaluate(new object?[0], new EvaluationContext());
        Assert.Equal("A", outputs[0]);
        Assert.Equal(0L, outputs[1]);
    }

    [Fact]
    public void Choice_LoadedWithAnOptionThatExists_KeepsIt()
    {
        var node = new ChoiceInputNode();

        node.DeserializeData(new JObject { ["Options"] = "A\nB\nC", ["Value"] = "C" });

        Assert.Equal("C", node.Value);
    }

    [Fact]
    public void Choice_LoadedWithoutOptionsOrValue_StaysUsable()
    {
        var empty = new ChoiceInputNode();
        empty.DeserializeData(new JObject { ["Options"] = "  \n ", ["Value"] = "Z" });
        Assert.Equal(string.Empty, empty.Value);

        var bare = new ChoiceInputNode();
        bare.DeserializeData(new JObject());
        Assert.Equal("Option A", bare.Value);
    }

    // ── Number Slider ───────────────────────────────────────────────────────

    [Fact]
    public void NumberSlider_LoadedWithMinAboveMax_ReadsThemLowestFirst()
    {
        var node = new NumberSliderNode();

        node.DeserializeData(new JObject { ["Min"] = 10d, ["Max"] = 5d, ["Value"] = 7d, ["Step"] = 1d });

        // OLD: loaded as Min 10 / Max 5 and the value 7 became 5.
        Assert.Equal(5d, node.Min);
        Assert.Equal(10d, node.Max);
        Assert.Equal(7d, node.Value);
    }

    [Fact]
    public void NumberSlider_RaisingMinAboveMax_PushesMaxUp()
    {
        var node = new NumberSliderNode { Min = 0, Max = 100, Value = 70 };

        node.Min = 150;

        Assert.Equal(150d, node.Min);
        Assert.Equal(150d, node.Max);
        Assert.Equal(150d, node.Value);
    }

    [Fact]
    public void NumberSlider_LoweringMaxBelowMin_PushesMinDown()
    {
        var node = new NumberSliderNode { Min = 20, Max = 100, Value = 50 };

        node.Max = 10;

        Assert.Equal(10d, node.Min);
        Assert.Equal(10d, node.Max);
        Assert.Equal(10d, node.Value);
    }

    [Fact]
    public void NumberSlider_OrdinaryEditsStayAsTheyWere()
    {
        var node = new NumberSliderNode { Min = 0, Max = 100, Value = 70 };

        node.Min = 10;
        node.Max = 80;

        Assert.Equal(10d, node.Min);
        Assert.Equal(80d, node.Max);
        Assert.Equal(70d, node.Value);
    }

    // ── Integer Slider ──────────────────────────────────────────────────────

    [Fact]
    public void IntegerSlider_LoadedWithMinAboveMax_ReadsThemLowestFirst()
    {
        var node = new IntegerSliderNode();

        node.DeserializeData(new JObject { ["Min"] = 10L, ["Max"] = 5L, ["Value"] = 7L });

        Assert.Equal(5L, node.Min);
        Assert.Equal(10L, node.Max);
        Assert.Equal(7L, node.Value);
    }

    [Fact]
    public void IntegerSlider_MinAndMaxPushEachOther()
    {
        var node = new IntegerSliderNode { Min = 0, Max = 100, Value = 70 };

        node.Min = 150;
        Assert.Equal(150L, node.Max);
        Assert.Equal(150L, node.Value);

        node.Max = 40;
        Assert.Equal(40L, node.Min);
        Assert.Equal(40L, node.Value);
    }
}
