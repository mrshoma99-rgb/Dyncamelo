using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Serialization;
using Dyncamelo.Nodes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>
/// Every graph the project ever shipped must still open. <c>Compat/&lt;tag&gt;/*.dyc</c> holds the sample graphs exactly as
/// earlier releases wrote them (see <c>Compat/README.md</c>); these tests open each one with today's code, so a user's saved
/// script survives an upgrade. A failure here means a shipped node changed in a way saved graphs cannot follow. The fix is an
/// alias in the node library (<c>[NodeAliases]</c>, <c>[PortAlias]</c>, a <c>[NodeDeprecated]</c> stub), never an edit of the file.
///
/// <para>Nodes that load (<c>Dyncamelo.Nodes</c>, the Core input nodes): the graph is read with <see cref="GraphSerializer"/> and
/// nothing may be dropped. Every node is there (not a <see cref="MissingNodeModel"/>), every wire is there between the ports it
/// named, every typed-in value, default flag and list-level setting is restored, the node's saved <c>Data</c> comes back out
/// unchanged, and <see cref="GraphSerializer.LoadWarnings"/> is empty. Graphs without Navisworks nodes also run, with the results the
/// samples of the time promised.</para>
///
/// <para>Navisworks nodes (<c>Dyncamelo.Navisworks.*</c>): that project needs the Navisworks API and is not referenced here, so the
/// serializer turns them into placeholders (asserted, so the day the project becomes loadable the test says so). The rule that
/// replaces loading: the node's definition id must resolve against the current C# source, exactly like
/// <see cref="SampleGraphStaticValidationTests"/> does for today's samples. It resolves when a <c>public static</c> method has that
/// path and that parameter-type list (the node is current, or retired with <c>[NodeDeprecated]</c>, which stays in the source), or
/// when the id is listed in that method's <c>[NodeAliases]</c> (an earlier signature). On top of the id, each port the file wires or
/// types a value into must still exist on the method, directly or through <c>[PortAlias]</c>; a default the file relies on must
/// still be optional; and a parameter the file does not know (added later) must be optional, because the old file cannot feed it.
/// The catalogue (<c>docs/dyncamelo-nodes.json</c>) is not used for this: it has display names, no ids, and leaves retired nodes out.</para>
/// </summary>
public class OldGraphFilesTests
{
    private const string NavisworksAssembly = "Dyncamelo.Navisworks";

    private static readonly Regex TagFolder = new Regex(@"^v\d+\.\d+\.\d+$");

    /// <summary>
    /// Breaks found that this repository does not fix (yet): file to the problems it is expected to report, as substrings. Empty
    /// is the goal. A listed file must report exactly those problems: others mean a new break, none means the break is gone and
    /// the entry must be deleted. Every entry needs a comment saying what is wrong and what would fix it.
    /// </summary>
    private static readonly Dictionary<string, string[]> ExpectedBreaks = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        // Not an upgrade regression but a defect in the sample since it was written (v0.32.0), still there today: the
        // "Viewpoints created" WatchList node was saved with an input and an output called "value", but WatchList has always called
        // both "list" (the v0.9.0 files say so). The wire Loop.Collect.results -> Viewpoints created is dropped on every open, in
        // every release, and the node shows nothing. Fix for the lead (data only): rename that node's two ports to "list" in
        // samples/Clash Group Viewpoints per Test.dyc. A user who saved their own copy has the same file; honouring "value" for
        // them needs a port alias on a hand-written NodeModel, which only Core can offer (PortModel.Aliases has an internal setter).
        ["v0.32.0/Clash Group Viewpoints per Test.dyc"] = new[] { "wires, the graph", "Viewpoints created" },
        ["v0.33.3/Clash Group Viewpoints per Test.dyc"] = new[] { "wires, the graph", "Viewpoints created" },
        ["samples/Clash Group Viewpoints per Test.dyc"] = new[] { "wires, the graph", "Viewpoints created" },
    };

    // ------------------------------------------------------------------
    // The stored set
    // ------------------------------------------------------------------

    internal static string CompatDirectory()
    {
        var root = Path.GetDirectoryName(SampleGraphFileTests.SamplesDirectory())!;
        var directory = Path.Combine(root, "tests", "Dyncamelo.Integration.Tests", "Compat");
        Assert.True(Directory.Exists(directory), "Compat directory not found: " + directory);
        return directory;
    }

    /// <summary>Relative paths (<c>v0.9.0/hello-math.dyc</c>) of every stored file, in a stable order.</summary>
    internal static List<string> CompatFiles()
    {
        var directory = CompatDirectory();
        return Directory.EnumerateFiles(directory, "*.dyc", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(directory, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    public static IEnumerable<object[]> AllFiles() => CompatFiles().Select(p => new object[] { p });

    /// <summary>The samples of today's repository, by <c>samples/&lt;name&gt;</c>: the newest versions of the same graphs.</summary>
    public static IEnumerable<object[]> CurrentSamples()
    {
        return Directory.EnumerateFiles(SampleGraphFileTests.SamplesDirectory(), "*.dyc")
            .Select(p => "samples/" + Path.GetFileName(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new object[] { p });
    }

    public static IEnumerable<object[]> FilesWithoutNavisworksNodes()
    {
        return CompatFiles()
            .Where(p => !NavisworksNodes(Read(p)).Any())
            .Select(p => new object[] { p });
    }

    public static IEnumerable<object[]> FilesWithNavisworksNodes()
    {
        return CompatFiles()
            .Where(p => NavisworksNodes(Read(p)).Any())
            .Select(p => new object[] { p });
    }

    [Fact]
    public void StoredSet_SpansManyReleasesAndIsDocumented()
    {
        var files = CompatFiles();
        var tags = files.Select(f => f.Split('/')[0]).Distinct(StringComparer.Ordinal).ToList();

        Assert.All(tags, t => Assert.Matches(TagFolder, t));
        Assert.True(tags.Count >= 8, "Expected graphs from at least 8 releases, found " + tags.Count + ".");
        Assert.True(files.Count >= 17, "Expected every sample of the newest tag to be stored, found only " + files.Count + " files.");

        // Roughly 1.5 MB is the budget for the whole set.
        var bytes = Directory.EnumerateFiles(CompatDirectory(), "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
        Assert.True(bytes < 1_500_000, "Compat files take " + bytes + " bytes; keep the set under 1.5 MB.");

        var readme = File.ReadAllText(Path.Combine(CompatDirectory(), "README.md"));
        foreach (var file in files)
        {
            Assert.True(readme.Contains(file, StringComparison.Ordinal), "Compat/README.md does not say where " + file + " came from.");
        }

        // No two stored files with the same name may be byte-identical: the README promises one copy per distinct content.
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var key = Path.GetFileName(file) + "|" + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(CompatDirectory(), file)));
            Assert.False(seen.TryGetValue(key, out var other), file + " is a byte-identical duplicate of " + other + ".");
            seen[key] = file;
        }
    }

    // ------------------------------------------------------------------
    // Opening
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFiles))]
    public void OldGraph_OpensWithNothingDropped(string relativePath) => AssertOpensWithNothingDropped(relativePath);

    /// <summary>The same check on the samples as they are today (the newest version of every stored graph, plus the ones with no older release).</summary>
    [Theory]
    [MemberData(nameof(CurrentSamples))]
    public void CurrentSample_OpensWithNothingDropped(string relativePath) => AssertOpensWithNothingDropped(relativePath);

    private static void AssertOpensWithNothingDropped(string relativePath)
    {
        var json = Read(relativePath);
        var registry = Pipeline.CreateRegistry();
        var serializer = new GraphSerializer(registry);

        var graph = serializer.LoadFromFile(FullPath(relativePath));

        var problems = new List<string>();
        CheckNodes(json, graph, problems);
        CheckWires(json, graph, problems);
        CheckPorts(json, graph, problems);
        foreach (var warning in serializer.LoadWarnings)
        {
            problems.Add("load warning: " + warning);
        }

        // What the file saved for each node must come back out when the graph is saved again with today's code.
        var resaved = JObject.Parse(serializer.Serialize(graph));
        CheckSavedData(json, resaved, problems);

        // ... and that saved copy must open without losing anything either.
        var second = new GraphSerializer(registry);
        var reopened = second.Deserialize(resaved.ToString());
        if (reopened.Nodes.Count != graph.Nodes.Count || reopened.Connections.Count != graph.Connections.Count)
        {
            problems.Add("re-saving and reopening changed the graph: " + graph.Nodes.Count + " nodes / " + graph.Connections.Count +
                " wires became " + reopened.Nodes.Count + " / " + reopened.Connections.Count + ".");
        }

        problems.AddRange(second.LoadWarnings.Select(w => "warning on reopening the re-saved graph: " + w));

        AssertNoProblems(relativePath, problems, allowExpectedBreaks: true);
    }

    [Theory]
    [MemberData(nameof(FilesWithNavisworksNodes))]
    public void OldGraph_NavisworksNodesStillResolveInTheSource(string relativePath)
    {
        var json = Read(relativePath);
        var problems = new List<string>();
        var connectors = ((JArray)json["Connectors"]!).OfType<JObject>().ToList();

        foreach (var node in NavisworksNodes(json))
        {
            var label = "node '" + node.Value<string>("Name") + "'";
            var id = node.Value<string>("DefinitionId") ?? string.Empty;
            var candidates = NavisworksSourceIndex.Resolve(id);
            if (candidates.Count == 0)
            {
                var path = NavisworksSourceIndex.SplitId(id).Path;
                var sameMethod = NavisworksSourceIndex.Nodes.Where(n => n.Path == path).ToList();
                problems.Add(label + ": definition id '" + id + "' resolves to no method in the Dyncamelo.Navisworks source" +
                    (sameMethod.Count == 0
                        ? " (no method '" + path + "' at all: the node was removed or renamed; keep a [NodeDeprecated] stub)."
                        : " (the method exists with a different signature; add [NodeAliases(\"" + id + "\")] to it)."));
                continue;
            }

            // Several methods may share the id's path (overloads); the node resolves when ANY candidate keeps all of the file's ports.
            var failures = candidates.Select(c => PortProblems(c, node, connectors, label)).ToList();
            if (failures.All(f => f.Count > 0))
            {
                problems.AddRange(failures[0]);
            }
        }

        AssertNoProblems(relativePath, problems);
    }

    private static List<string> PortProblems(NavisworksSourceNode method, JObject node, List<JObject> connectors, string label)
    {
        var problems = new List<string>();
        var nodeId = node.Value<string>("Id");
        var savedInputs = ((JArray)node["InputPorts"]!).OfType<JObject>().ToList();
        var savedOutputs = ((JArray)node["OutputPorts"]!).OfType<JObject>().ToList();

        foreach (var port in savedInputs)
        {
            var name = port.Value<string>("Name") ?? string.Empty;
            var wired = connectors.Any(c => c.Value<string>("ToNode") == nodeId && c.Value<string>("ToPort") == name);
            var current = method.CurrentInputName(name);
            if (current == null)
            {
                if (wired || port["UserValue"] != null)
                {
                    problems.Add(label + ": input '" + name + "' no longer exists on " + method.Path + " (" + method.File + ") and has no [PortAlias]; its " +
                        (wired ? "wire" : "typed-in value") + " would be dropped.");
                }

                continue;
            }

            if (port.Value<bool?>("UsingDefaultValue") == true && !method.Parameters.First(p => p.Name == current).Optional)
            {
                problems.Add(label + ": input '" + name + "' used its default in the file but " + method.Path + " now requires a value.");
            }
        }

        foreach (var port in savedOutputs)
        {
            var name = port.Value<string>("Name") ?? string.Empty;
            var wired = connectors.Any(c => c.Value<string>("FromNode") == nodeId && c.Value<string>("FromPort") == name);
            if (wired && method.CurrentOutputName(name) == null)
            {
                problems.Add(label + ": output '" + name + "' no longer exists on " + method.Path + " (" + method.File + ") and has no [PortAlias]; its wire would be dropped.");
            }
        }

        foreach (var parameter in method.Parameters.Where(p => !p.Optional))
        {
            var known = savedInputs.Any(s => method.CurrentInputName(s.Value<string>("Name") ?? string.Empty) == parameter.Name);
            if (!known)
            {
                problems.Add(label + ": " + method.Path + " has a required parameter '" + parameter.Name + "' the file never had; make it optional.");
            }
        }

        return problems;
    }

    // ------------------------------------------------------------------
    // Running
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(FilesWithoutNavisworksNodes))]
    public void OldGraph_WithoutNavisworksNodes_StillRunsGreen(string relativePath)
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphSerializer(registry).LoadFromFile(FullPath(relativePath));

        Assert.DoesNotContain(graph.Nodes, n => n is MissingNodeModel);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success, relativePath + " no longer runs: " + string.Join("; ", graph.Nodes.Where(n => n.State == NodeState.Error).Select(n => n.Name)));
        Assert.All(graph.Nodes, n => Assert.True(n.State == NodeState.Executed, relativePath + ": '" + n.Name + "' ended " + n.State + "."));

        AssertPinnedResults(Path.GetFileName(relativePath), graph);
    }

    [Theory]
    [MemberData(nameof(FilesWithNavisworksNodes))]
    public void OldGraph_WithNavisworksNodes_IsAPlaceholderHereAndTheEngineTakesIt(string relativePath)
    {
        // Without the Navisworks project loaded these nodes cannot run. What must hold: the placeholders are exactly the Navisworks
        // nodes (everything else resolved), and running the graph reports errors instead of throwing.
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphSerializer(registry).LoadFromFile(FullPath(relativePath));

        var expectedMissing = NavisworksNodes(Read(relativePath)).Select(n => n.Value<string>("Name")).OrderBy(n => n, StringComparer.Ordinal);
        var missing = graph.Nodes.OfType<MissingNodeModel>().Select(n => n.Name).OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(expectedMissing, missing);

        new GraphEngine().Run(graph);

        // A placeholder fails itself (Error) or is stopped because a placeholder feeds it (Warning); it never pretends to have run.
        Assert.All(graph.Nodes.OfType<MissingNodeModel>(), n => Assert.True(n.State == NodeState.Error || n.State == NodeState.Warning, n.Name + " ended " + n.State + "."));
    }

    /// <summary>
    /// The results each runnable old graph produced when it was shipped. The files are byte-identical to what the release wrote, so
    /// a change here is a change of behaviour a user would see in a graph they saved.
    /// </summary>
    private static void AssertPinnedResults(string fileName, GraphModel graph)
    {
        switch (fileName)
        {
            case "Getting Started - Math and Watch.dyc":
                Assert.Equal("32", Watch(graph, "Area"));
                Assert.Equal("Area = 32", Watch(graph, "Report"));
                break;

            case "hello-math.dyc":
                Assert.Equal("85", Watch(graph, "Result"));
                break;

            case "list-lacing.dyc":
                Assert.Equal(new[] { 11d, 22d }, Pipeline.AsDoubles(Pipeline.Output(graph, "Shortest")));
                Assert.Equal(new[] { 11d, 22d, 23d }, Pipeline.AsDoubles(Pipeline.Output(graph, "Longest")));
                var cross = Pipeline.AsList(Pipeline.Output(graph, "Cross Product"));
                Assert.Equal(3, cross.Count);
                Assert.Equal(new[] { 11d, 21d }, Pipeline.AsDoubles(cross[0]));
                Assert.Equal(new[] { 12d, 22d }, Pipeline.AsDoubles(cross[1]));
                Assert.Equal(new[] { 13d, 23d }, Pipeline.AsDoubles(cross[2]));
                break;

            case "string-report.dyc":
                // This file still names the Concat/Join input "str"; today's port is "text" ([PortAlias("str", "text")]).
                Assert.Equal("Word count: 4", Watch(graph, "Count report"));
                Assert.Equal("dyncamelo, makes, navisworks, programmable", Watch(graph, "Joined words"));
                break;

            case "csv-roundtrip.dyc":
                var rows = Pipeline.AsList(Pipeline.Output(graph, "Round-tripped rows"));
                Assert.Equal(2, rows.Count);
                Assert.Equal(new[] { 1d, 2d, 3d }, Pipeline.AsDoubles(rows[0]));
                Assert.Equal(new[] { 4d, 5d, 6d }, Pipeline.AsDoubles(rows[1]));
                break;

            default:
                Assert.Fail("Old graph '" + fileName + "' runs without Navisworks but has no pinned results — add them to AssertPinnedResults.");
                break;
        }
    }

    private static string Watch(GraphModel graph, string nodeName)
    {
        var node = Pipeline.Node(graph, nodeName);
        return node is WatchNode watch ? watch.FormattedValue : ((WatchListNode)node).FormattedValue;
    }

    // ------------------------------------------------------------------
    // Checks on a loaded graph
    // ------------------------------------------------------------------

    private static void CheckNodes(JObject json, GraphModel graph, List<string> problems)
    {
        var saved = ((JArray)json["Nodes"]!).OfType<JObject>().ToList();
        if (graph.Nodes.Count != saved.Count)
        {
            problems.Add("the file has " + saved.Count + " nodes, the graph " + graph.Nodes.Count + ".");
        }

        foreach (var token in saved)
        {
            var label = "node '" + token.Value<string>("Name") + "'";
            var node = FindNode(graph, token);
            if (node == null)
            {
                problems.Add(label + " is not in the graph.");
                continue;
            }

            var isNavisworks = IsNavisworks(token);
            if (node is MissingNodeModel missing && !isNavisworks)
            {
                problems.Add(label + " did not load: " + missing.Reason);
            }
            else if (isNavisworks && !(node is MissingNodeModel))
            {
                // Not a failure of the file: the Navisworks project became loadable here. Make the rule above obsolete knowingly.
                problems.Add(label + " loaded although Dyncamelo.Navisworks is not referenced; extend this test to treat it like any other node.");
            }

            if (Enum.TryParse<LacingMode>(token.Value<string>("Lacing") ?? string.Empty, true, out var lacing) && node.Lacing != lacing)
            {
                problems.Add(label + ": lacing " + lacing + " became " + node.Lacing + ".");
            }

            if ((token.Value<bool?>("IsFrozen") ?? false) != node.IsFrozen)
            {
                problems.Add(label + ": the frozen flag changed.");
            }

            if (token.Value<string>("Name") != node.Name)
            {
                problems.Add(label + " is now called '" + node.Name + "'.");
            }
        }
    }

    private static void CheckWires(JObject json, GraphModel graph, List<string> problems)
    {
        var connectors = ((JArray)json["Connectors"]!).OfType<JObject>().ToList();
        if (graph.Connections.Count != connectors.Count)
        {
            problems.Add("the file has " + connectors.Count + " wires, the graph " + graph.Connections.Count + ".");
        }

        foreach (var connector in connectors)
        {
            var fromNode = graph.Nodes.FirstOrDefault(n => SameId(n.Id, connector.Value<string>("FromNode")));
            var toNode = graph.Nodes.FirstOrDefault(n => SameId(n.Id, connector.Value<string>("ToNode")));
            var describe = connector.Value<string>("FromPort") + " -> " + connector.Value<string>("ToPort");
            if (fromNode == null || toNode == null)
            {
                problems.Add("wire " + describe + " joins a node that is not in the graph.");
                continue;
            }

            var from = fromNode.FindOutPort(connector.Value<string>("FromPort"));
            var to = toNode.FindInPort(connector.Value<string>("ToPort"));
            var wire = from == null || to == null
                ? null
                : graph.Connections.FirstOrDefault(c => c.Source == from && c.Target == to);
            var wireLabel = "wire " + fromNode.Name + "." + describe.Replace(" -> ", " -> " + toNode.Name + ".");
            if (wire == null)
            {
                problems.Add(wireLabel + " was dropped" + (from == null ? " (the output no longer exists)" : to == null ? " (the input no longer exists)" : "") + ".");
            }
            else if ((connector.Value<bool?>("Muted") ?? false) != wire.IsMuted)
            {
                problems.Add(wireLabel + ": the muted flag changed.");
            }
        }
    }

    private static void CheckPorts(JObject json, GraphModel graph, List<string> problems)
    {
        foreach (var token in ((JArray)json["Nodes"]!).OfType<JObject>())
        {
            var node = FindNode(graph, token);
            if (node == null || node is MissingNodeModel)
            {
                continue;
            }

            var label = "node '" + node.Name + "'";
            var covered = new HashSet<PortModel>();
            foreach (var saved in ((JArray)token["InputPorts"]!).OfType<JObject>())
            {
                var name = saved.Value<string>("Name");
                var port = node.FindInPort(name);
                if (port == null)
                {
                    var wired = ((JArray)json["Connectors"]!).OfType<JObject>().Any(c => SameId(node.Id, c.Value<string>("ToNode")) && c.Value<string>("ToPort") == name);
                    if (wired || saved["UserValue"] != null)
                    {
                        problems.Add(label + ": input '" + name + "' no longer exists; its " + (wired ? "wire" : "typed-in value") + " was dropped.");
                    }

                    continue;
                }

                covered.Add(port);
                var portLabel = label + ", input '" + name + "'";
                if (saved["UserValue"] is JValue userValue && !(port.HasUserValue && Equals(port.UserValue, userValue.Value)))
                {
                    problems.Add(portLabel + ": the typed-in value " + (userValue.Value ?? "null") + " was dropped.");
                }

                if (saved.Value<bool?>("UsingDefaultValue") == true && !(port.HasDefault && port.UsingDefaultValue))
                {
                    problems.Add(portLabel + " used its default in the file but " + (port.HasDefault ? "no longer does" : "has no default now") + ".");
                }

                if ((saved.Value<int?>("Level") ?? -1) != port.Level
                    || (saved.Value<bool?>("UseLevels") ?? false) != port.UseLevels
                    || (saved.Value<bool?>("KeepListStructure") ?? false) != port.KeepListStructure)
                {
                    problems.Add(portLabel + ": its list-level settings changed.");
                }
            }

            // An input the file never knew (added since) cannot be fed by the old graph: it must be optional.
            foreach (var port in node.InPorts.Where(p => !covered.Contains(p) && !p.HasDefault && !p.IsMultiInput))
            {
                problems.Add(label + ": input '" + port.Name + "' did not exist when the file was saved and has no default, so the old graph cannot feed it.");
            }
        }
    }

    /// <summary>Each key a node saved in its <c>Data</c> bag must be written back with the same value.</summary>
    private static void CheckSavedData(JObject original, JObject resaved, List<string> problems)
    {
        var after = ((JArray)resaved["Nodes"]!).OfType<JObject>().ToDictionary(n => n.Value<string>("Id")!, StringComparer.OrdinalIgnoreCase);
        foreach (var token in ((JArray)original["Nodes"]!).OfType<JObject>())
        {
            if (!(token["Data"] is JObject data) || !after.TryGetValue(token.Value<string>("Id")!, out var current))
            {
                continue;
            }

            var label = "node '" + token.Value<string>("Name") + "' (" + token.Value<string>("NodeType") + ")";
            var currentData = current["Data"] as JObject;
            foreach (var property in data.Properties())
            {
                if (currentData == null || !currentData.TryGetValue(property.Name, out var now))
                {
                    problems.Add(label + ": saved data '" + property.Name + "' is no longer written back.");
                }
                else if (!SameValue(property.Value, now))
                {
                    problems.Add(label + ": saved data '" + property.Name + "' was " + property.Value.ToString(Newtonsoft.Json.Formatting.None) +
                        " and is now " + now.ToString(Newtonsoft.Json.Formatting.None) + ".");
                }
            }
        }
    }

    private static bool SameValue(JToken a, JToken b)
    {
        if (IsNumber(a) && IsNumber(b))
        {
            return Convert.ToDouble(((JValue)a).Value, CultureInfo.InvariantCulture) == Convert.ToDouble(((JValue)b).Value, CultureInfo.InvariantCulture);
        }

        if (a is JObject ao && b is JObject bo)
        {
            return ao.Properties().All(p => bo.TryGetValue(p.Name, out var other) && SameValue(p.Value, other));
        }

        if (a is JArray aa && b is JArray ba)
        {
            return aa.Count == ba.Count && aa.Zip(ba, SameValue).All(x => x);
        }

        return JToken.DeepEquals(a, b);
    }

    private static bool IsNumber(JToken token) => token.Type == JTokenType.Integer || token.Type == JTokenType.Float;

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static void AssertNoProblems(string relativePath, List<string> problems, bool allowExpectedBreaks = false)
    {
        if (allowExpectedBreaks && ExpectedBreaks.TryGetValue(relativePath, out var expected))
        {
            var unexpected = problems.Where(p => !expected.Any(e => p.Contains(e, StringComparison.Ordinal))).ToList();
            var gone = expected.Where(e => !problems.Any(p => p.Contains(e, StringComparison.Ordinal))).ToList();
            Assert.True(
                unexpected.Count == 0 && gone.Count == 0,
                relativePath + " is listed in ExpectedBreaks but reports different problems." + Environment.NewLine +
                "Not expected: " + string.Join(" | ", unexpected) + Environment.NewLine +
                "Expected but gone (delete the entry if the break is fixed): " + string.Join(" | ", gone));
            return;
        }

        Assert.True(
            problems.Count == 0,
            relativePath + " no longer opens cleanly with today's code:" + Environment.NewLine + " - " + string.Join(Environment.NewLine + " - ", problems));
    }

    private static string FullPath(string relativePath)
    {
        const string samples = "samples/";
        return relativePath.StartsWith(samples, StringComparison.Ordinal)
            ? Path.Combine(SampleGraphFileTests.SamplesDirectory(), relativePath.Substring(samples.Length))
            : Path.Combine(CompatDirectory(), relativePath);
    }

    private static JObject Read(string relativePath) => JObject.Parse(File.ReadAllText(FullPath(relativePath)));

    private static bool IsNavisworks(JObject node) =>
        node.Value<string>("NodeType") == "ZeroTouch" && node.Value<string>("Assembly") == NavisworksAssembly;

    private static List<JObject> NavisworksNodes(JObject json) => ((JArray)json["Nodes"]!).OfType<JObject>().Where(IsNavisworks).ToList();

    private static bool SameId(Guid id, string? text) => Guid.TryParse(text, out var parsed) && parsed == id;

    private static NodeModel? FindNode(GraphModel graph, JObject token)
    {
        var text = token.Value<string>("Id");
        return Guid.TryParse(text, out var id) ? graph.Nodes.FirstOrDefault(n => n.Id == id) : null;
    }
}
