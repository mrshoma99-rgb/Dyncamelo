using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using static CamelGraph.Nodes.Tests.NavisworksSourceText;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Source-reading tests (the Navisworks node project cannot be loaded here) for the TimeLiner nodes fixed in the node-library
/// audit: the order of the flat task list, creating under a parent and not twice, optional actual dates, a delete that can run over
/// a task and its subtasks, looking a task up by name, and the pickers and dropdowns of the inputs.
/// </summary>
public class NavisworksTimelinerSourceTests
{
    private static string Nodes => Source("TimeLiner", "TimelinerNodes.cs");

    private static string Progress => Source("TimeLiner", "TimelinerProgressNodes.cs");

    private static string Auto => Source("TimeLiner", "TimelinerAutoNodes.cs");

    private static string WithoutComments(string text) => Regex.Replace(text, @"//[^\n]*", string.Empty);

    private static string Attributes(string source, string nodeName)
    {
        var at = source.IndexOf("[NodeName(\"" + nodeName + "\")]", StringComparison.Ordinal);
        Assert.True(at >= 0, "node not found: " + nodeName);
        var start = Math.Max(source.LastIndexOf("</returns>", at, StringComparison.Ordinal), 0);
        return source.Substring(start, source.IndexOf("public static", at, StringComparison.Ordinal) - start);
    }

    // ------------------------------------------------------------------ NVC-13

    [Fact]
    public void TheTaskListCanBeReadChildrenFirstSoAWholeListCanBeEditedOrDeleted()
    {
        var collect = WithoutComments(Body(Nodes, "private static void CollectTasks("));
        var firstAdd = collect.IndexOf("tasks.Add(task);", StringComparison.Ordinal);
        var recurse = collect.IndexOf("CollectTasks(task.Children, tasks, childrenFirst);", StringComparison.Ordinal);
        var lastAdd = collect.LastIndexOf("tasks.Add(task);", StringComparison.Ordinal);

        Assert.True(firstAdd >= 0 && firstAdd < recurse && recurse < lastAdd, "parents are added before the recursion, children-first adds them after it");
        Assert.Contains("if (!childrenFirst)", collect);

        var tasks = Attributes(Nodes, "TimeLiner.Tasks");
        Assert.Contains("[NodeChoices(\"parents first\", \"children first\")] string order = \"parents first\"", Nodes); // the default keeps the old order
        Assert.Contains("[LiveState]", tasks);
        Assert.Contains("children first", tasks);
    }

    [Fact]
    public void TheEditingNodesTellTheUserHowToHandleASubtaskList()
    {
        foreach (var name in new[] { "TimelinerTask.SetDates", "TimelinerTask.SetProgress", "TimelinerTask.SetActual", "TimelinerTask.Delete" })
        {
            var source = name == "TimelinerTask.SetDates" ? Nodes : Progress;
            Assert.Contains("children first", Attributes(source, name));
        }
    }

    [Fact]
    public void ADeleteOverATaskAndItsSubtasksAnswersFalseForTheOnesAlreadyGone()
    {
        var delete = Body(Progress, "public static bool Delete(");
        Assert.Contains("TryGetIndexPath(timeliner, timelinerTask, staleMeansGone: true)", delete);

        var path = Body(Progress, "private static System.Collections.ObjectModel.Collection<int>? TryGetIndexPath(");
        Assert.Contains("if (staleMeansGone)", path);
        Assert.Contains("NodeWarnings.Add(", path);
        Assert.Contains("return null;", path);
        Assert.Contains("is stale", path); // the other nodes still get the plain sentence
    }

    [Fact]
    public void CreatingATaskTwiceUpdatesItUnlessToldOtherwiseAndCanGoUnderAParent()
    {
        var create = WithoutComments(Body(Nodes, "public static TimelinerTask Create("));

        Assert.Contains("[ScalarInput] object? parent = null,", create);
        Assert.Contains("[NodePanel(\"Advanced\")] [NodeChoices(\"update\", \"add\", \"error\")] string onExisting = \"update\",", create);
        Assert.Contains("[NodeChoices(\"Construct\", \"Demolish\", \"Temporary\")] string taskType = \"Construct\",", create);
        // The siblings are looked at by name first; "update" edits the stored task, "add" falls through to the append.
        Assert.True(create.IndexOf("existing != null && mode != \"add\"", StringComparison.Ordinal) < create.IndexOf("timeliner.TaskAddCopy(task);", StringComparison.Ordinal));
        Assert.Contains("return CommitTaskEdit(doc, existing, edited);", create);
        Assert.Contains("mode == \"error\"", create);
        Assert.Contains("timeliner.TaskAddCopy(storedParent, task);", create);
        Assert.Contains("timeliner.TaskResolveIndexPath(parentPath!)", create); // the parent is read again after the add
    }

    [Fact]
    public void SetActualTakesEitherDateAndKeepsTheOneThatIsNotWired()
    {
        var setActual = WithoutComments(Body(Progress, "public static TimelinerTask SetActual("));

        Assert.Contains("DateTime? start = null,", setActual);
        Assert.Contains("DateTime? end = null,", setActual);
        Assert.Contains("if (start == null && end == null)", setActual);
        Assert.Contains("NodeWarnings.Add(", setActual);
        Assert.Contains("copy.ActualStartDate = start.Value;", setActual);
        Assert.Contains("copy.ActualEndDate = end.Value;", setActual);
        Assert.Contains("if (start != null && end != null)", setActual); // the order of the dates is checked only when both are given
    }

    // ------------------------------------------------------------------ NVC-42, NVC-33

    [Fact]
    public void ATaskCanBeFoundByNameOrPathAndSaysWhichParentItHas()
    {
        var byName = Body(Nodes, "public static TimelinerTask ByName(");
        Assert.Contains("FindTaskByNameOrPath(timeliner, name)", byName);

        var find = Body(Nodes, "internal static TimelinerTask? FindTaskByNameOrPath(");
        Assert.Contains("NodeWarnings.Add(", find);
        Assert.Contains("SavedItemPath.IsPath(text)", find);
        Assert.Contains("level = next.Children;", find);

        var info = Attributes(Nodes, "TimelinerTask.Info");
        Assert.Contains("\"progress\", \"parent\", \"depth\"", info);
        Assert.Contains("[\"parent\"] = parentName,", Nodes);
        Assert.Contains("[\"depth\"] = depth,", Nodes);
    }

    [Fact]
    public void AttachSetTakesTheSetItselfItsNameOrItsPath()
    {
        var attach = Body(Nodes, "public static TimelinerTask AttachSet(");

        Assert.Contains("[ScalarInput] object set", attach);
        Assert.Contains("SavedItemTreeHelpers.ResolveStored<SelectionSet>(doc.SelectionSets.RootItem, set, \"selection set\")", attach);
        Assert.Contains("[PortAlias(\"setName\", \"set\")]", Attributes(Nodes, "TimelinerTask.AttachSet"));
    }

    [Fact]
    public void AutoAttachCanMatchTheDisplayIdAndOffersThePropertyPickers()
    {
        var auto = Auto;
        var attach = WithoutComments(Body(auto, "public static Dictionary<string, object?> AutoAttachByProperty("));

        Assert.Contains("[NodeTabChoice(NodeDataSource.Selection)] string category,", attach);
        Assert.Contains("[NodePropertyChoice(NodeDataSource.Selection, \"category\")] string property,", attach);
        Assert.Contains("[NodeChoices(\"Name\", \"Display id\")] string matchOn = \"Name\",", attach);
        Assert.Contains("var taskName = byDisplayId ? stored.DisplayId : stored.DisplayName;", attach);
    }

    // ------------------------------------------------------------------ NVC-14

    [Theory]
    [InlineData("TimelinerNodes.cs", "TimelinerTask.Create")]
    [InlineData("TimelinerNodes.cs", "TimelinerTask.AttachSet")]
    [InlineData("TimelinerNodes.cs", "TimelinerTask.SetDates")]
    [InlineData("TimelinerProgressNodes.cs", "TimelinerTask.SetProgress")]
    [InlineData("TimelinerProgressNodes.cs", "TimelinerTask.SetActual")]
    [InlineData("TimelinerProgressNodes.cs", "TimelinerTask.Delete")]
    [InlineData("TimelinerAutoNodes.cs", "TimeLiner.AutoAttachByProperty")]
    public void EveryTimelinerNodeThatEditsTheScheduleDeclaresThatItChangesTheModel(string file, string nodeName)
    {
        Assert.Contains(
            "[NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]",
            Attributes(File.ReadAllText(Path.Combine(Root(), "src", "CamelGraph.Navisworks", "TimeLiner", file)).Replace("\r\n", "\n"), nodeName));
    }
}
