using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Dyncamelo.Core.Loader;
using Dyncamelo.Nodes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>
/// Statically pins every shipped sample graph, including the ones that need
/// Navisworks and therefore cannot run on this build agent. For each .dyc
/// file it asserts that every zero-touch definition id resolves — general
/// definitions against the real registry (Core built-ins + Dyncamelo.Nodes),
/// Navisworks definitions against node signatures harvested from the
/// Dyncamelo.Navisworks C# source — that every serialized port matches the
/// definition's ports, and that every connector references ports that exist.
/// A renamed node, method, parameter or output in either library breaks the
/// corresponding sample here before a user ever opens it.
/// </summary>
public class SampleGraphStaticValidationTests
{
    private const string NavisworksAssemblyName = "Dyncamelo.Navisworks";

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
        var json = JObject.Parse(File.ReadAllText(
            Path.Combine(SampleGraphFileTests.SamplesDirectory(), fileName)));
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
                "' matches no public static method in the Dyncamelo.Navisworks source.");

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
            Assert.Equal(definition!.Inputs.Select(i => i.Name), inputNames);
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
    // Harvesting node signatures from the Dyncamelo.Navisworks C# source.
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

    /// <summary>Splits on a separator, ignoring separators nested in &lt;&gt;, () or strings.</summary>
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
                case '(': depth++; break;
                case '>':
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
