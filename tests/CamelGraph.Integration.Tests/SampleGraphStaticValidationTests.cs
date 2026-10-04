using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CamelGraph.Core.Loader;
using CamelGraph.Nodes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>
/// Statically pins every shipped sample graph, including the ones that need
/// Navisworks and therefore cannot run on this build agent. For each .dyc
/// file it asserts that every zero-touch definition id resolves — general
/// definitions against the real registry (Core built-ins + CamelGraph.Nodes),
/// Navisworks definitions against node signatures harvested from the
/// CamelGraph.Navisworks C# source — that every serialized port matches the
/// definition's ports, and that every connector references ports that exist.
/// A renamed node, method, parameter or output in either library breaks the
/// corresponding sample here before a user ever opens it. The same checks run on
/// the how-to graphs the wiki offers for download (docs/wiki-src/graphs).
/// </summary>
public class SampleGraphStaticValidationTests
{
    private const string NavisworksAssemblyName = "CamelGraph.Navisworks";

    public static IEnumerable<object[]> SampleFiles()
    {
        return Directory.EnumerateFiles(SampleGraphFileTests.SamplesDirectory(), "*.dyc")
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new object[] { Path.GetFileName(p) });
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void SampleGraph_StaticStructureIsValid(string fileName)
    {
        ValidateGraphFile(Path.Combine(SampleGraphFileTests.SamplesDirectory(), fileName), fileName);
    }

    /// <summary>
    /// The static checks shared by the shipped samples and the wiki graphs: every node resolves, every serialized port
    /// matches its definition and every connector references real ports.
    /// </summary>
    private static void ValidateGraphFile(string path, string fileName)
    {
        var json = JObject.Parse(File.ReadAllText(path));
        Assert.NotNull(json["Dyncamelo"]);

        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var navisworksMethods = HarvestedNavisworksMethods.Value;

        var nodes = ((JArray)json["Nodes"]!).OfType<JObject>().ToList();
        var nodesById = new Dictionary<string, JObject>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            var id = node.Value<string>("Id");
            Assert.False(string.IsNullOrEmpty(id), fileName + ": node without an Id.");
            Assert.False(nodesById.ContainsKey(id!), fileName + ": duplicate node id " + id);
            nodesById[id!] = node;

            var nodeType = node.Value<string>("NodeType");
            var label = fileName + " node '" + node.Value<string>("Name") + "'";
            if (nodeType == "ZeroTouch")
            {
                ValidateZeroTouchNode(node, label, registry, navisworksMethods);
            }
            else
            {
                Assert.True(
                    registry.CreateNode(nodeType ?? string.Empty) != null,
                    label + ": unknown node type '" + nodeType + "'.");
            }
        }

        foreach (var connector in ((JArray)json["Connectors"]!).OfType<JObject>())
        {
            var label = fileName + " connector " + connector.Value<string>("Id");
            Assert.True(
                nodesById.TryGetValue(connector.Value<string>("FromNode") ?? string.Empty, out var fromNode),
                label + ": FromNode does not exist.");
            Assert.True(
                nodesById.TryGetValue(connector.Value<string>("ToNode") ?? string.Empty, out var toNode),
                label + ": ToNode does not exist.");

            var fromPort = connector.Value<string>("FromPort");
            var toPort = connector.Value<string>("ToPort");
            Assert.True(
                PortNames(fromNode!, "OutputPorts").Contains(fromPort),
                label + ": output port '" + fromPort + "' does not exist on node '" +
                fromNode!.Value<string>("Name") + "'.");
            Assert.True(
                PortNames(toNode!, "InputPorts").Contains(toPort),
                label + ": input port '" + toPort + "' does not exist on node '" +
                toNode!.Value<string>("Name") + "'.");
        }
    }

    // ------------------------------------------------------------------
    // The how-to graphs the wiki offers for download (docs/wiki-src/graphs).
    // They are built by tools/wiki_graph.py from the specs next to them, so
    // the checks below also hold the two together.
    // ------------------------------------------------------------------

    private static string WikiGraphsDirectory()
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(SampleGraphFileTests.SamplesDirectory())!, "docs", "wiki-src", "graphs");
        Assert.True(Directory.Exists(directory), "Wiki graphs directory not found: " + directory);
        return directory;
    }

    public static IEnumerable<object[]> WikiGraphFiles()
    {
        return Directory.EnumerateFiles(WikiGraphsDirectory(), "*.dyc")
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new object[] { Path.GetFileName(p) });
    }

    [Fact]
    public void WikiGraphs_ExistAndEachOneHasItsSpec()
    {
        var directory = WikiGraphsDirectory();
        var graphs = Directory.EnumerateFiles(directory, "*.dyc")
            .Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var specs = Directory.EnumerateFiles(Path.Combine(directory, "specs"), "*.json")
            .Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.NotEmpty(graphs);
        Assert.Equal(specs, graphs);
    }

    [Theory]
    [MemberData(nameof(WikiGraphFiles))]
    public void WikiGraph_StaticStructureIsValid(string fileName)
    {
        ValidateGraphFile(Path.Combine(WikiGraphsDirectory(), fileName), fileName);
    }

    /// <summary>
    /// What a graph offered for download must also be: named and described, set to run manually, every input of every
    /// node either wired or given a value (a default counts), no value typed into a wired input, ids that are
    /// unique 32-digit hex numbers, and no two nodes drawn on top of each other.
    /// </summary>
    [Theory]
    [MemberData(nameof(WikiGraphFiles))]
    public void WikiGraph_IsCompleteAndTidy(string fileName)
    {
        var json = JObject.Parse(File.ReadAllText(Path.Combine(WikiGraphsDirectory(), fileName)));
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var navisworksMethods = HarvestedNavisworksMethods.Value;

        Assert.False(string.IsNullOrWhiteSpace(json.Value<string>("Name")), fileName + ": the graph has no Name.");
        var description = (json.Value<string>("Description") ?? string.Empty).Trim();
        Assert.True(description.Length > 0 && description.EndsWith(".", StringComparison.Ordinal),
            fileName + ": the Description is one sentence ending with a full stop.");
        Assert.False(Regex.IsMatch(description.Substring(0, description.Length - 1), @"[.!?]\s+[A-Z]"),
            fileName + ": the Description runs to more than one sentence.");
        Assert.Equal("Manual", json["View"]?["RunType"]?.Value<string>());
        Assert.True(Regex.IsMatch(json.Value<string>("Uuid") ?? string.Empty, "^[0-9a-f]{32}$"), fileName + ": Uuid is not 32 hex digits.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var wiredPorts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connector in ((JArray)json["Connectors"]!).OfType<JObject>())
        {
            Assert.True(ids.Add(connector.Value<string>("Id")!), fileName + ": duplicate id " + connector.Value<string>("Id"));
            wiredPorts.Add(connector.Value<string>("ToNode") + "/" + connector.Value<string>("ToPort"));
        }

        var positions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in ((JArray)json["Nodes"]!).OfType<JObject>())
        {
            var id = node.Value<string>("Id")!;
            var label = fileName + " node '" + node.Value<string>("Name") + "'";
            Assert.True(Regex.IsMatch(id, "^[0-9a-f]{32}$"), label + ": id is not 32 hex digits.");
            Assert.True(ids.Add(id), label + ": duplicate id.");
            Assert.True(positions.Add(node.Value<double>("X") + "," + node.Value<double>("Y")), label + ": drawn exactly on top of another node.");

            var optional = InputsAreOptional(node, registry, navisworksMethods);
            var ports = ((JArray)node["InputPorts"]!).OfType<JObject>().ToList();
            Assert.Equal(optional.Count, ports.Count);
            for (int i = 0; i < ports.Count; i++)
            {
                var name = ports[i].Value<string>("Name");
                var wired = wiredPorts.Contains(id + "/" + name);
                var typed = ports[i]["UserValue"] != null;
                Assert.True(wired || typed || optional[i],
                    label + ": required input '" + name + "' is neither wired nor given a value.");
                Assert.False(wired && typed, label + ": input '" + name + "' has a value typed into it and a wire.");
                if (wired)
                {
                    Assert.False(ports[i].Value<bool>("UsingDefaultValue"),
                        label + ": wired input '" + name + "' still says it uses its default.");
                }
            }
        }

        foreach (var note in ((JArray)json["Notes"]!).OfType<JObject>())
        {
            Assert.True(ids.Add(note.Value<string>("Id")!), fileName + ": duplicate note id.");
            Assert.False(string.IsNullOrWhiteSpace(note.Value<string>("Text")), fileName + ": a note is empty.");
        }
    }

    /// <summary>For each serialized input of a node, whether the node's definition gives it a default.</summary>
    private static List<bool> InputsAreOptional(
        JObject node,
        NodeRegistry registry,
        IReadOnlyList<SourceMethod> navisworksMethods)
    {
        var nodeType = node.Value<string>("NodeType");
        if (nodeType != "ZeroTouch")
        {
            var instance = registry.CreateNode(nodeType ?? string.Empty);
            Assert.NotNull(instance);
            if (node["Data"] is JObject data)
            {
                instance!.DeserializeData(data); // rebuilds the ports of nodes such as List.Create
            }

            return instance!.InPorts.Select(p => p.HasDefault).ToList();
        }

        var definitionId = node.Value<string>("DefinitionId") ?? string.Empty;
        if (node.Value<string>("Assembly") == NavisworksAssemblyName)
        {
            var inputNames = ((JArray)node["InputPorts"]!).OfType<JObject>().Select(p => p.Value<string>("Name")).ToList();
            var method = navisworksMethods.First(m =>
                m.MatchesDefinitionId(definitionId) && m.ParameterNames.SequenceEqual(inputNames));
            return method.ParameterIsOptional.ToList();
        }

        Assert.True(registry.TryGetDefinition(definitionId, out var definition), "definition '" + definitionId + "' is not registered.");
        if (!string.Equals(definition!.Id, definitionId, StringComparison.Ordinal))
        {
            // Saved under an earlier id ([NodeAliases]): the file lists the ports of that earlier signature, so answer for those.
            return ((JArray)node["InputPorts"]!).OfType<JObject>()
                .Select(p => p.Value<string>("Name"))
                .Select(name => definition.Inputs.Any(i => i.Name == name || i.Aliases.Contains(name))
                    && definition.Inputs.First(i => i.Name == name || i.Aliases.Contains(name)).HasDefault)
                .ToList();
        }

        return definition.Inputs.Select(i => i.HasDefault).ToList();
    }

    private static void ValidateZeroTouchNode(
        JObject node,
        string label,
        NodeRegistry registry,
        IReadOnlyList<SourceMethod> navisworksMethods)
    {
        var definitionId = node.Value<string>("DefinitionId") ?? string.Empty;
        var inputPorts = ((JArray)node["InputPorts"]!).OfType<JObject>().ToList();
        var inputNames = inputPorts.Select(p => p.Value<string>("Name")).ToList();
        var outputNames = PortNames(node, "OutputPorts").ToList();

        if (node.Value<string>("Assembly") == NavisworksAssemblyName)
        {
            // No compiled assembly is loadable here; validate against source.
            var candidates = navisworksMethods
                .Where(m => m.MatchesDefinitionId(definitionId))
                .ToList();
            Assert.True(
                candidates.Count > 0,
                label + ": definition id '" + definitionId +
                "' matches no public static method in the CamelGraph.Navisworks source.");

            Assert.True(
                candidates.Any(m =>
                    m.ParameterNames.SequenceEqual(inputNames) &&
                    m.OutputNames.SequenceEqual(outputNames) &&
                    InputDefaultsAreConsistent(m, inputPorts)),
                label + ": serialized ports [" + string.Join(", ", inputNames) + "] -> [" +
                string.Join(", ", outputNames) + "] do not match the source signature of '" +
                definitionId + "'.");
        }
        else
        {
            Assert.True(
                registry.TryGetDefinition(definitionId, out var definition),
                label + ": definition id '" + definitionId + "' is not registered.");
            if (!string.Equals(definition!.Id, definitionId, StringComparison.Ordinal))
            {
                // The sample was saved under an earlier id of this node (a [NodeAliases] entry: its signature grew or changed
                // since). The loader finds each saved wire and value by port name or earlier port name, so that is what is
                // pinned here: every saved port still resolves to a port of the node as it is now.
                foreach (var name in inputNames)
                {
                    Assert.True(
                        definition.Inputs.Any(i => i.Name == name || i.Aliases.Contains(name)),
                        label + ": saved input '" + name + "' no longer exists on '" + definition.Id + "' and has no [PortAlias].");
                }

                foreach (var name in outputNames)
                {
                    Assert.True(
                        definition.Outputs.Any(o => o.Name == name || o.Aliases.Contains(name)),
                        label + ": saved output '" + name + "' no longer exists on '" + definition.Id + "' and has no [PortAlias].");
                }

                return;
            }

            Assert.Equal(definition.Inputs.Select(i => i.Name), inputNames);
            Assert.Equal(definition.Outputs.Select(o => o.Name), outputNames);
            for (int i = 0; i < inputPorts.Count; i++)
            {
                if (inputPorts[i].Value<bool?>("UsingDefaultValue") == true)
                {
                    Assert.True(
                        definition.Inputs[i].HasDefault,
                        label + ": port '" + inputNames[i] + "' claims a default the definition lacks.");
                }
            }
        }
    }

    /// <summary>A serialized default is only usable when the parameter is optional.</summary>
    private static bool InputDefaultsAreConsistent(SourceMethod method, List<JObject> inputPorts)
    {
        for (int i = 0; i < inputPorts.Count; i++)
        {
            if (inputPorts[i].Value<bool?>("UsingDefaultValue") == true && !method.ParameterIsOptional[i])
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string?> PortNames(JObject node, string listName)
    {
        return ((JArray)node[listName]!).OfType<JObject>().Select(p => p.Value<string>("Name"));
    }

    // ------------------------------------------------------------------
    // Harvesting node signatures from the CamelGraph.Navisworks C# source.
    // ------------------------------------------------------------------

    private static readonly Lazy<IReadOnlyList<SourceMethod>> HarvestedNavisworksMethods =
        new Lazy<IReadOnlyList<SourceMethod>>(HarvestNavisworksSource);

    private static IReadOnlyList<SourceMethod> HarvestNavisworksSource()
    {
        var sourceDirectory = Path.Combine(
            Path.GetDirectoryName(SampleGraphFileTests.SamplesDirectory())!,
            "src", NavisworksAssemblyName);
        Assert.True(Directory.Exists(sourceDirectory), "Source directory not found: " + sourceDirectory);

        var methods = new List<SourceMethod>();
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            {
                continue;
            }

            methods.AddRange(ParseFile(File.ReadAllText(file)));
        }

        Assert.True(methods.Count > 50, "Suspiciously few node methods harvested: " + methods.Count);
        return methods;
    }

    private static readonly Regex NamespaceRegex =
        new Regex(@"^\s*namespace\s+([\w\.]+)\s*;", RegexOptions.Multiline);

    private static readonly Regex ClassRegex =
        new Regex(
            @"^\s*public\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)",
            RegexOptions.Multiline);

    // One attribute line: brackets inside string literals do not end the attribute.
    private static readonly Regex MethodRegex = new Regex(
        @"(?<attrs>(?:^[ \t]*\[(?:[^\]\r\n""]|""[^""\r\n]*"")*\][ \t]*\r?\n)*)^[ \t]*public\s+static\s+(?<ret>[\w\.\<\>\[\]\?,\s]+?)\s+(?<name>\w+)\s*\(",
        RegexOptions.Multiline);

    private static IEnumerable<SourceMethod> ParseFile(string text)
    {
        var namespaceMatch = NamespaceRegex.Match(text);
        if (!namespaceMatch.Success)
        {
            yield break;
        }

        var ns = namespaceMatch.Groups[1].Value;
        var classMatches = ClassRegex.Matches(text).Cast<Match>().ToList();

        foreach (Match method in MethodRegex.Matches(text))
        {
            // The class a method belongs to is the nearest class declared above it.
            var owner = classMatches.LastOrDefault(c => c.Index < method.Index);
            if (owner == null)
            {
                continue;
            }

            var parameterList = ExtractBalancedParenthesized(text, method.Index + method.Length - 1);
            if (parameterList == null)
            {
                continue;
            }

            var parameters = SplitTopLevel(parameterList, ',')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Select(ParseParameter)
                .ToList();

            var attrs = method.Groups["attrs"].Value;
            var multiReturn = Regex.Match(attrs, @"\[MultiReturn\(([^\)]*)\)\]");
            List<string> outputs;
            if (multiReturn.Success)
            {
                outputs = Regex.Matches(multiReturn.Groups[1].Value, "\"([^\"]*)\"")
                    .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            }
            else
            {
                var returnName = Regex.Match(attrs, @"\[return:\s*NodeName\(""([^""]*)""\)\]");
                outputs = new List<string> { returnName.Success ? returnName.Groups[1].Value : "result" };
            }

            yield return new SourceMethod(
                ns + "." + owner.Groups[1].Value + "." + method.Groups["name"].Value,
                parameters,
                outputs);
        }
    }

    /// <summary>Returns the text between the '(' at <paramref name="openIndex"/> and its balanced ')'.</summary>
    private static string? ExtractBalancedParenthesized(string text, int openIndex)
    {
        int depth = 0;
        bool inString = false;
        for (int i = openIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(openIndex + 1, i - openIndex - 1);
                    }

                    break;
            }
        }

        return null;
    }

    /// <summary>Splits on a separator, ignoring separators nested in &lt;&gt;, (), [] or strings.</summary>
    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        int depth = 0;
        bool inString = false;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '<':
                case '[':
                case '(': depth++; break;
                case '>':
                case ']':
                case ')': depth--; break;
                default:
                    if (c == separator && depth == 0)
                    {
                        parts.Add(text.Substring(start, i - start));
                        start = i + 1;
                    }

                    break;
            }
        }

        parts.Add(text.Substring(start));
        return parts;
    }

    private static SourceParameter ParseParameter(string parameter)
    {
        parameter = StripLeadingAttributes(parameter);
        var declaration = SplitTopLevel(parameter, '=');
        bool optional = declaration.Count > 1;
        var typeAndName = declaration[0].Trim();
        int nameStart = typeAndName.Length;
        while (nameStart > 0 && (char.IsLetterOrDigit(typeAndName[nameStart - 1]) || typeAndName[nameStart - 1] == '_'))
        {
            nameStart--;
        }

        return new SourceParameter(
            typeAndName.Substring(nameStart),
            typeAndName.Substring(0, nameStart).Trim(),
            optional);
    }

    /// <summary>
    /// Removes any leading parameter attributes (e.g. <c>[NodeChoices("a", "b")] </c>)
    /// so the remaining text is just the type, name and optional default. String- and
    /// nesting-aware so commas/brackets inside the attribute don't confuse it.
    /// </summary>
    private static string StripLeadingAttributes(string text)
    {
        text = text.Trim();
        while (text.StartsWith("["))
        {
            int depth = 0;
            bool inString = false;
            int end = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                }
                else if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }

            if (end < 0)
            {
                break;
            }

            text = text.Substring(end + 1).Trim();
        }

        return text;
    }

    private sealed class SourceParameter
    {
        public SourceParameter(string name, string type, bool optional)
        {
            Name = name;
            Type = type;
            Optional = optional;
        }

        public string Name { get; }
        public string Type { get; }
        public bool Optional { get; }
    }

    private sealed class SourceMethod
    {
        private readonly List<SourceParameter> _parameters;

        public SourceMethod(string fullPath, List<SourceParameter> parameters, List<string> outputs)
        {
            FullPath = fullPath;
            _parameters = parameters;
            OutputNames = outputs;
        }

        /// <summary>Namespace.Class.Method.</summary>
        public string FullPath { get; }

        public IReadOnlyList<string> OutputNames { get; }

        public IEnumerable<string> ParameterNames => _parameters.Select(p => p.Name);

        public IReadOnlyList<bool> ParameterIsOptional => _parameters.Select(p => p.Optional).ToList();

        /// <summary>
        /// Whether a serialized definition id ("Namespace.Class.Method@mangledType1,...",
        /// per AssemblyNodeLoader.GetFunctionSignature) plausibly denotes this method:
        /// the method path must match exactly and every mangled parameter type must
        /// loosely match the corresponding C# source type (exact for language
        /// keywords, simple-name comparison for everything else, since the source
        /// uses short type names resolved through usings).
        /// </summary>
        public bool MatchesDefinitionId(string definitionId)
        {
            var at = definitionId.IndexOf('@');
            var path = at < 0 ? definitionId : definitionId.Substring(0, at);
            if (path != FullPath)
            {
                return false;
            }

            var mangled = at < 0
                ? new List<string>()
                : SplitTopLevel(definitionId.Substring(at + 1), ',');
            if (mangled.Count != _parameters.Count)
            {
                return false;
            }

            for (int i = 0; i < mangled.Count; i++)
            {
                if (!TypesLooselyMatch(mangled[i].Trim(), _parameters[i].Type))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Compares a mangled runtime type against C# source text: arrays and
        /// nullable markers must agree structurally (reference-type '?' annotations
        /// in source are erased, like the runtime does), generic arguments compare
        /// recursively and plain names compare by their final dotted segment.
        /// </summary>
        private static bool TypesLooselyMatch(string mangled, string source)
        {
            mangled = mangled.Trim();
            source = source.Trim();

            if (mangled.EndsWith("[]", StringComparison.Ordinal) &&
                source.EndsWith("[]", StringComparison.Ordinal))
            {
                return TypesLooselyMatch(
                    mangled.Substring(0, mangled.Length - 2),
                    source.Substring(0, source.Length - 2));
            }

            // The runtime keeps '?' only for Nullable<T>; the source also writes it
            // on reference types where it is a compile-time annotation. Strip from
            // both sides — the port/name checks still pin the signature.
            if (mangled.EndsWith("?", StringComparison.Ordinal))
            {
                mangled = mangled.Substring(0, mangled.Length - 1);
            }

            if (source.EndsWith("?", StringComparison.Ordinal))
            {
                source = source.Substring(0, source.Length - 1);
            }

            int mangledOpen = mangled.IndexOf('<');
            int sourceOpen = source.IndexOf('<');
            if (mangledOpen >= 0 != sourceOpen >= 0)
            {
                return false;
            }

            if (mangledOpen >= 0)
            {
                if (!SimpleNamesMatch(mangled.Substring(0, mangledOpen), source.Substring(0, sourceOpen)))
                {
                    return false;
                }

                var mangledArguments = SplitTopLevel(
                    mangled.Substring(mangledOpen + 1, mangled.LastIndexOf('>') - mangledOpen - 1), ',');
                var sourceArguments = SplitTopLevel(
                    source.Substring(sourceOpen + 1, source.LastIndexOf('>') - sourceOpen - 1), ',');
                return mangledArguments.Count == sourceArguments.Count &&
                    mangledArguments.Zip(sourceArguments, TypesLooselyMatch).All(m => m);
            }

            return SimpleNamesMatch(mangled, source);
        }

        private static bool SimpleNamesMatch(string mangled, string source)
        {
            return LastSegment(mangled) == LastSegment(source);
        }

        private static string LastSegment(string typeName)
        {
            typeName = typeName.Trim();
            int dot = typeName.LastIndexOf('.');
            return dot < 0 ? typeName : typeName.Substring(dot + 1);
        }
    }
}
