using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The Action.* builder nodes live in the Navisworks project, which the Linux test projects cannot load, so these read its source.
/// They pin the audit fixes for SYS-26 and SYS-25: a builder only makes a value (role Create, so the Script Player does not list it
/// as changing the model), an action prints as what it does, and the template tokens of Action.SaveViewpoint are written down.
/// </summary>
public class WorkflowActionNodesSourceTests
{
    private static readonly string Source = NavisworksSourceText.Source("WorkflowActionNodes.cs");

    private static readonly string[] Builders =
    {
        "Action.Isolate", "Action.ShowAll", "Action.ZoomTo", "Action.SaveViewpoint", "Action.OverrideColor",
        "Action.ResetAppearance", "Action.Highlight", "Action.Ghost", "Action.ResetTemporaryAppearance",
    };

    [Fact]
    public void AllNineBuildersAreExactlyTheOnesThatAreChecked()
    {
        var found = Regex.Matches(Source, "\\[NodeName\\(\"(Action\\.[A-Za-z]+)\"\\)\\]").Cast<Match>().Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(Builders.OrderBy(n => n), found.OrderBy(n => n));
    }

    [Fact]
    public void EveryBuilderIsMarkedCreateSoItIsNotTakenForAModelChange()
    {
        foreach (var builder in Builders)
        {
            // The attribute block of one node: from its [NodeName] to the next "public static".
            var block = Regex.Match(Source, "\\[NodeName\\(\"" + Regex.Escape(builder) + "\"\\)\\](?<attrs>.*?)public static", RegexOptions.Singleline);
            Assert.True(block.Success, builder + " not found");
            Assert.Contains("[NodeFunction(CamelGraph.Core.Graph.NodeFunction.Create)]", block.Groups["attrs"].Value);
            Assert.DoesNotContain("NodeFunction.Modify", block.Groups["attrs"].Value);
        }
    }

    [Fact]
    public void EveryActionPrintsAsWhatItDoes()
    {
        var classes = Regex.Matches(Source, "private sealed class (\\w+Action) : IWorkflowAction").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(9, classes.Count);

        // Watch on a list of actions would otherwise print "CamelGraph.Navisworks.WorkflowActionNodes+IsolateAction".
        Assert.Equal(classes.Count, Regex.Matches(Source, "public override string ToString\\(\\) => Describe\\(\\);").Count);
    }

    [Fact]
    public void HighlightHasOneCallForTheTransparencyNotTwoBranches()
    {
        var highlight = NavisworksSourceText.Source("WorkflowActionNodes.cs");
        var start = highlight.IndexOf("private sealed class HighlightAction", System.StringComparison.Ordinal);
        var end = highlight.IndexOf("private sealed class GhostAction", System.StringComparison.Ordinal);
        var body = highlight.Substring(start, end - start);

        Assert.Equal(1, Regex.Matches(body, "OverrideTransparencyTemporary\\(").Count);
        Assert.DoesNotContain("else", body);
    }

    [Fact]
    public void SaveViewpointListsEveryTemplateToken()
    {
        var block = Regex.Match(Source, "\\[NodeName\\(\"Action\\.SaveViewpoint\"\\)\\].*?public static", RegexOptions.Singleline).Value;

        foreach (var token in new[] { "{name}", "{item}", "{index}", "{index1}", "{n}", "{count}" })
        {
            Assert.Contains(token, block);
        }
    }
}
