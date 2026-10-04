using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using Xunit;
using static CamelGraph.Nodes.Tests.NodeRun;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Dictionary.ValueAtPath, SelectKeys, RemoveKeys and SetValues (COL-32, COL-33), the lacing sentences of the key nodes (COL-13,
/// ENG-10), XML.Parse listElements (COL-10), the DTD check (COL-15) and the List.Create description (COL-31).
/// </summary>
public class DictionaryPathAndKeysTests
{
    private static Dictionary<string, object?> D(params (string Key, object? Value)[] entries)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            dictionary[key] = value;
        }

        return dictionary;
    }

    private static string Messages(ZeroTouchNodeModel node) => string.Join(" | ", node.Messages.Select(m => m.Text));

    // ------------------------------------------------------------ COL-32 ValueAtPath

    private static Dictionary<string, object?> Project()
    {
        var tasks = L(
            D(("Name", "Dig"), ("@id", "1")),
            D(("Name", "Pour"), ("@id", "2")),
            D(("Name", "Cure"), ("@id", "3")));
        var project = D(("Name", "Tower"), ("Tasks", D(("Task", tasks))));
        return D(("Project", project));
    }

    [Fact]
    public void ValueAtPath_FollowsKeysAndListPositions()
    {
        var data = Project();

        Assert.Equal("Tower", DictionaryExtraNodes.ValueAtPath(data, "Project/Name"));
        Assert.Equal("Pour", DictionaryExtraNodes.ValueAtPath(data, "Project/Tasks/Task/1/Name"));
        Assert.Equal("Cure", DictionaryExtraNodes.ValueAtPath(data, "Project/Tasks/Task/-1/Name"));
        Assert.Equal("2", DictionaryExtraNodes.ValueAtPath(data, "Project.Tasks.Task.1.@id"));          // dots work when the path has no slash
        Assert.Same(data, DictionaryExtraNodes.ValueAtPath(data, ""));
        Assert.Equal("Tower", DictionaryExtraNodes.ValueAtPath(data, "/Project/Name/"));
    }

    [Fact]
    public void ValueAtPath_StarMapsOverAList()
    {
        var names = Assert.IsType<List<object?>>(DictionaryExtraNodes.ValueAtPath(Project(), "Project/Tasks/Task/*/Name"));

        Assert.Equal(new object?[] { "Dig", "Pour", "Cure" }, names);
        Assert.Equal(new object?[] { "1", "2", "3" }, (List<object?>)DictionaryExtraNodes.ValueAtPath(Project(), "Project/Tasks/Task/*/@id")!);
    }

    [Fact]
    public void ValueAtPath_AMissingStepGivesTheDefault()
    {
        var data = Project();

        Assert.Null(DictionaryExtraNodes.ValueAtPath(data, "Project/Missing"));
        Assert.Equal("n/a", DictionaryExtraNodes.ValueAtPath(data, "Project/Tasks/Task/7/Name", "n/a"));
        Assert.Equal("n/a", DictionaryExtraNodes.ValueAtPath(data, "Project/Name/Deeper", "n/a"));
        Assert.Equal("n/a", DictionaryExtraNodes.ValueAtPath(null, "Project/Name", "n/a"));
        Assert.Equal(new object?[] { "Dig", null, "Cure" }, (List<object?>)DictionaryExtraNodes.ValueAtPath(D(("T", L(D(("N", "Dig")), D(), D(("N", "Cure"))))), "T/*/N")!);
    }

    [Fact]
    public void ValueAtPath_ReadsAFileWithOneTaskLikeAFileWithMany()
    {
        // COL-10: <Task> occurring once is a dictionary, twice is a list; position 0 reads both.
        var one = XmlNodes.Parse("<Project><Tasks><Task><Name>Dig</Name></Task></Tasks></Project>");
        var many = XmlNodes.Parse("<Project><Tasks><Task><Name>Dig</Name></Task><Task><Name>Pour</Name></Task></Tasks></Project>");

        Assert.Equal("Dig", DictionaryExtraNodes.ValueAtPath(one, "Project/Tasks/Task/0/Name"));
        Assert.Equal("Dig", DictionaryExtraNodes.ValueAtPath(many, "Project/Tasks/Task/0/Name"));
        Assert.Equal(new object?[] { "Dig" }, (List<object?>)DictionaryExtraNodes.ValueAtPath(one, "Project/Tasks/Task/*/Name")!);
        Assert.Equal(new object?[] { "Dig", "Pour" }, (List<object?>)DictionaryExtraNodes.ValueAtPath(many, "Project/Tasks/Task/*/Name")!);
        Assert.Equal("n/a", DictionaryExtraNodes.ValueAtPath(one, "Project/Tasks/Task/1/Name", "n/a"));
    }

    [Fact]
    public void ValueAtPath_APathMustBeGiven_AndAListOfPathsGivesOneResultEach()
    {
        Assert.Throws<ArgumentNullException>(() => DictionaryExtraNodes.ValueAtPath(Project(), null!));

        var node = Run("Dictionary.ValueAtPath", Project(), L("Project/Name", "Project/Tasks/Task/0/@id"));
        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(new object?[] { "Tower", "1" }, Out(node).ToArray());
    }

    // ------------------------------------------------------------ COL-33 SelectKeys / RemoveKeys / SetValues

    [Fact]
    public void SelectKeys_KeepsOnlyTheListedKeys_InTheirOrder()
    {
        var bag = D(("Name", "Wall"), ("Level", "L1"), ("Length", 5.0), ("Id", 9));

        var result = DictionaryExtraNodes.SelectKeys(bag, L("Length", "Name", "Nope", "Name", null));

        Assert.Equal(new[] { "Length", "Name" }, result.Keys.ToArray());
        Assert.Equal(5.0, result["Length"]);
        Assert.Equal(4, bag.Count);                                    // the input is untouched
        Assert.Empty(DictionaryExtraNodes.SelectKeys(bag, L()));
    }

    [Fact]
    public void RemoveKeys_LeavesOutEveryListedKey_InOneDictionary()
    {
        var bag = D(("Name", "Wall"), ("Level", "L1"), ("Length", 5.0));

        var result = DictionaryExtraNodes.RemoveKeys(bag, L("Level", "Length", "Nope"));

        Assert.Equal(new[] { "Name" }, result.Keys.ToArray());
        Assert.Equal(3, bag.Count);
    }

    [Fact]
    public void SetValues_SetsSeveralKeysAtOnce()
    {
        var bag = D(("Name", "Wall"), ("Level", "L1"));

        var result = DictionaryExtraNodes.SetValues(bag, L("Level", "Mark", "Level"), L("L2", "W-1", "L3"));

        Assert.Equal("Wall", result["Name"]);
        Assert.Equal("L3", result["Level"]);                           // the last value wins
        Assert.Equal("W-1", result["Mark"]);
        Assert.Equal("L1", bag["Level"]);

        var ex = Assert.Throws<ArgumentException>(() => DictionaryExtraNodes.SetValues(bag, L("a", "b"), L(1)));
        Assert.Contains("same number of keys (2) and values (1)", ex.Message);
        var empty = Assert.Throws<ArgumentException>(() => DictionaryExtraNodes.SetValues(bag, L((object?)null), L(1)));
        Assert.Contains("key 1 is empty", empty.Message);
    }

    [Fact]
    public void TheKeyListsApplyToEveryDictionaryOfAList_UnderTheEngine()
    {
        var bags = L(D(("a", 1), ("b", 2), ("c", 3)), D(("a", 4), ("b", 5), ("c", 6)));

        var selected = Run("Dictionary.SelectKeys", bags, L("a", "c"));
        Assert.Equal(NodeState.Executed, selected.State);
        Assert.Equal(2, Out(selected).Count);
        Assert.All(Out(selected), item => Assert.Equal(new[] { "a", "c" }, ((Dictionary<string, object?>)item!).Keys.ToArray()));

        var removed = Run("Dictionary.RemoveKeys", bags, L("a", "b"));
        Assert.All(Out(removed), item => Assert.Equal(new[] { "c" }, ((Dictionary<string, object?>)item!).Keys.ToArray()));

        // OLD recipe for contrast: RemoveKey with a key list pairs keys with dictionaries and gives one dictionary per pair.
        var paired = Run("Dictionary.RemoveKey", bags, L("a", "b"));
        Assert.Equal(new[] { "b", "c" }, ((Dictionary<string, object?>)Out(paired)[0]!).Keys.ToArray());
        Assert.Equal(new[] { "a", "c" }, ((Dictionary<string, object?>)Out(paired)[1]!).Keys.ToArray());
    }

    [Fact]
    public void TheNewNodesAreInTheDictionaryCategory_WithSearchWords()
    {
        foreach (var name in new[] { "Dictionary.ValueAtPath", "Dictionary.SelectKeys", "Dictionary.RemoveKeys", "Dictionary.SetValues" })
        {
            var definition = Definition(name);
            Assert.Equal("Dictionary", definition.Category);
            Assert.NotEmpty(definition.SearchTags);
        }

        Assert.Contains("json", Definition("Dictionary.ValueAtPath").SearchTags);
    }

    // ------------------------------------------------------------ COL-13 / ENG-10 the lacing sentence

    [Theory]
    [InlineData("Dictionary.ValueAtKey")]
    [InlineData("Dictionary.ValueOrDefault")]
    [InlineData("Dictionary.ContainsKey")]
    [InlineData("Dictionary.SetValueAtKey")]
    [InlineData("Dictionary.RemoveKey")]
    public void TheKeyNodesSayThatListsPairUp_AndHowToGetEveryCombination(string name)
    {
        var description = Definition(name).Description;

        Assert.Contains("pair up item by item and stop at the shorter list", description);
        Assert.Contains("Cross-Product lacing", description);
    }

    // ------------------------------------------------------------ COL-10 listElements

    [Fact]
    public void ListElements_AlwaysWrapsTheNamedElementsInAList()
    {
        var one = "<Project><Tasks><Task><Name>Dig</Name></Task></Tasks><Owner>Ann</Owner></Project>";

        var plain = (Dictionary<string, object?>)((Dictionary<string, object?>)((Dictionary<string, object?>)XmlNodes.Parse(one)!)["Project"]!)["Tasks"]!;
        Assert.IsType<Dictionary<string, object?>>(plain["Task"]);                 // as before: a single element is not a list

        var wrapped = (Dictionary<string, object?>)((Dictionary<string, object?>)((Dictionary<string, object?>)XmlNodes.Parse(one, "Task")!)["Project"]!)["Tasks"]!;
        var tasks = Assert.IsType<List<object?>>(wrapped["Task"]);
        Assert.Single(tasks);

        var project = (Dictionary<string, object?>)((Dictionary<string, object?>)XmlNodes.Parse(one, " Task ; Owner")!)["Project"]!;
        Assert.IsType<List<object?>>(project["Owner"]);
    }

    [Fact]
    public void ListElements_KeepsRepeatedElementsInOneList_AndStarMeansEveryElement()
    {
        var many = "<Tasks><Task>Dig</Task><Task>Pour</Task><Task>Cure</Task></Tasks>";

        var tasks = (Dictionary<string, object?>)((Dictionary<string, object?>)XmlNodes.Parse(many, "Task")!)["Tasks"]!;
        Assert.Equal(new object?[] { "Dig", "Pour", "Cure" }, (List<object?>)tasks["Task"]!);

        var all = (Dictionary<string, object?>)((Dictionary<string, object?>)XmlNodes.Parse("<A><B>x</B><C><D>y</D></C></A>", "*")!)["A"]!;
        Assert.Equal(new object?[] { "x" }, (List<object?>)all["B"]!);
        var c = (List<object?>)all["C"]!;
        Assert.Equal(new object?[] { "y" }, (List<object?>)((Dictionary<string, object?>)c[0]!)["D"]!);
    }

    [Fact]
    public void ListElements_MatchesALocalNameAndAPrefixedName()
    {
        var xml = "<r xmlns:p=\"urn:p\"><p:item>1</p:item></r>";

        Assert.IsType<List<object?>>(((Dictionary<string, object?>)((Dictionary<string, object?>)XmlNodes.Parse(xml, "item")!)["r"]!)["p:item"]);
        Assert.IsType<List<object?>>(((Dictionary<string, object?>)((Dictionary<string, object?>)XmlNodes.Parse(xml, "p:item")!)["r"]!)["p:item"]);
    }

    [Fact]
    public void AGraphSavedWithTheOldXmlParseId_StillLoadsAndRuns()
    {
        var node = RunSavedAs(
            "XML.Parse",
            "CamelGraph.Nodes.XmlNodes.Parse@string",
            new (string, string)[0],
            new[] { (0, (object?)"<a><b>1</b></a>") },
            new (int, object?)[0]);

        Assert.Equal(NodeState.Executed, node.State);
        Assert.IsType<Dictionary<string, object?>>(node.OutPorts[0].Value);
    }

    // ------------------------------------------------------------ COL-15 DTD handling (verified, nothing to fix)

    [Fact]
    public void ABillionLaughsDocument_IsStoppedByTheEntityLimit_NotExpanded()
    {
        var bomb = "<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY lol \"lol\">" +
                   "<!ENTITY lol2 \"&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;\">" +
                   "<!ENTITY lol3 \"&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;\">" +
                   "<!ENTITY lol4 \"&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;\">" +
                   "<!ENTITY lol5 \"&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;\">" +
                   "<!ENTITY lol6 \"&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;\">" +
                   "<!ENTITY lol7 \"&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;\">" +
                   "<!ENTITY lol8 \"&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;\">" +
                   "<!ENTITY lol9 \"&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;\">]><lolz>&lol9;</lolz>";

        var ex = Assert.Throws<FormatException>(() => XmlNodes.Parse(bomb));
        Assert.Contains("not valid XML", ex.Message);
    }

    [Fact]
    public void AnExternalEntity_IsNeverReadFromDisk()
    {
        var path = System.IO.Path.GetTempFileName();
        System.IO.File.WriteAllText(path, "SECRET-CONTENT");
        try
        {
            var xml = "<!DOCTYPE r [<!ENTITY x SYSTEM \"" + new Uri(path).AbsoluteUri + "\">]><r>&x;</r>";
            string shown;
            try
            {
                shown = CamelGraph.Core.Types.TypeCoercion.FormatValue(XmlNodes.Parse(xml));
            }
            catch (Exception ex)
            {
                shown = ex.Message;
            }

            Assert.DoesNotContain("SECRET-CONTENT", shown);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    // ------------------------------------------------------------ COL-31 List.Create

    [Fact]
    public void ListCreate_SaysAWiredListStaysWhole_AndHasSearchWords()
    {
        var node = new ListCreateNode();

        Assert.Contains("kept whole as ONE item", node.Description);
        Assert.Contains("List.Merge", node.Description);
        Assert.Contains("array", node.SearchTags);
    }
}
