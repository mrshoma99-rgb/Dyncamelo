using Dyncamelo.Core.Workflow;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>
/// Unit tests for the per-item workflow context: name resolution (duck-typed,
/// no host dependency), template expansion, and the collect/bind lifecycle the
/// loop node drives.
/// </summary>
public class WorkflowContextTests
{
    private sealed class Named
    {
        public Named(string displayName) => DisplayName = displayName;

        public string DisplayName { get; }
    }

    private sealed class OnlyName
    {
        public OnlyName(string name) => Name = name;

        public string Name { get; }
    }

    private sealed class Anonymous
    {
        public override string ToString() => "anon";
    }

    private static WorkflowContext Bound(object? item, int index = 0, int count = 1)
    {
        var context = new WorkflowContext();
        context.Bind(item, index, count);
        return context;
    }

    [Fact]
    public void ItemName_UsesStringItself()
    {
        Assert.Equal("Beam-01", Bound("Beam-01").ItemName);
    }

    [Fact]
    public void ItemName_DuckTypesDisplayNameProperty()
    {
        // A Navisworks ModelItem/SavedItem exposes DisplayName; this resolves it
        // without Core depending on the Navisworks assembly.
        Assert.Equal("Level 2", Bound(new Named("Level 2")).ItemName);
    }

    [Fact]
    public void ItemName_FallsBackToNameProperty_ThenToString()
    {
        Assert.Equal("Room 5", Bound(new OnlyName("Room 5")).ItemName);
        Assert.Equal("anon", Bound(new Anonymous()).ItemName);
    }

    [Fact]
    public void ItemName_NullItem_IsEmpty()
    {
        Assert.Equal(string.Empty, Bound(null).ItemName);
    }

    [Fact]
    public void Expand_SubstitutesNameIndexAndCountTokens()
    {
        var context = Bound(new Named("Duct A"), index: 2, count: 5);
        Assert.Equal("v3-Duct A", context.Expand("v{index1}-{name}"));
        Assert.Equal("2 of 5", context.Expand("{index} of {count}"));
        Assert.Equal("View 3", context.Expand("View {n}"));
    }

    [Fact]
    public void Expand_IsCaseInsensitiveAndTrimsTokens()
    {
        var context = Bound(new Named("Pipe"), index: 0, count: 1);
        Assert.Equal("Pipe / Pipe", context.Expand("{NAME} / { item }"));
    }

    [Fact]
    public void Expand_EmptyOrNullTemplate_YieldsItemName()
    {
        var context = Bound(new Named("Wall"));
        Assert.Equal("Wall", context.Expand(null));
        Assert.Equal("Wall", context.Expand(string.Empty));
    }

    [Fact]
    public void Expand_UnknownToken_IsKeptLiteral_SoTyposAreVisible()
    {
        var context = Bound("x", index: 0, count: 1);
        Assert.Equal("a {foo} b", context.Expand("a {foo} b"));
    }

    [Fact]
    public void Expand_UnterminatedBrace_EmitsRemainderVerbatim()
    {
        var context = Bound("x");
        Assert.Equal("ok {index", context.Expand("ok {index"));
    }

    [Fact]
    public void Bind_ClearsCollectedResults_BetweenIterations()
    {
        var context = new WorkflowContext();
        context.Bind("a", 0, 2);
        context.Collect("first");
        Assert.Equal(new object?[] { "first" }, context.Collected);

        context.Bind("b", 1, 2);
        Assert.Empty(context.Collected);
        Assert.Equal("b", context.CurrentItem);
        Assert.Equal(1, context.Index);
        Assert.Equal(2, context.Index1);
        Assert.Equal(2, context.Count);
    }
}
