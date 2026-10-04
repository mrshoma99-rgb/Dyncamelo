using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>
/// What the current <c>CamelGraph.Navisworks</c> source says about every node it defines. That project cannot be compiled or
/// loaded on this build agent, so (like <see cref="SampleGraphStaticValidationTests"/>, <see cref="LegacyDefinitionIdAliasTests"/>
/// and <see cref="RetiredNodesTests"/>) its public static methods are harvested from the C# text: the definition path, the
/// parameter names, types and defaults, the output names, the <c>[NodeAliases]</c> (earlier ids) and <c>[PortAlias]</c> (earlier
/// port names) it carries, and whether it is <c>[NodeDeprecated]</c> (retired, but kept so old graphs still load).
/// </summary>
internal sealed class NavisworksSourceNode
{
    public NavisworksSourceNode(
        string path,
        string file,
        IReadOnlyList<NavisworksSourceParameter> parameters,
        IReadOnlyList<string> outputs,
        IReadOnlyList<string> aliasIds,
        IReadOnlyList<(string Old, string Current)> portAliases,
        bool deprecated)
    {
        Path = path;
        File = file;
        Parameters = parameters;
        Outputs = outputs;
        AliasIds = aliasIds;
        PortAliases = portAliases;
        Deprecated = deprecated;
    }

    /// <summary><c>Namespace.Class.Method</c> — the part of a definition id before the '@'.</summary>
    public string Path { get; }

    public string File { get; }

    public IReadOnlyList<NavisworksSourceParameter> Parameters { get; }

    public IReadOnlyList<string> Outputs { get; }

    /// <summary>Earlier full definition ids (<c>[NodeAliases]</c>) that now resolve to this method.</summary>
    public IReadOnlyList<string> AliasIds { get; }

    public IReadOnlyList<(string Old, string Current)> PortAliases { get; }

    public bool Deprecated { get; }

    /// <summary>Whether the id names this method with its current signature.</summary>
    public bool HasSignatureOf(string definitionId)
    {
        var (path, mangled) = NavisworksSourceIndex.SplitId(definitionId);
        if (path != Path || mangled.Count != Parameters.Count)
        {
            return false;
        }

        for (int i = 0; i < mangled.Count; i++)
        {
            if (NavisworksSourceIndex.NormalizeType(mangled[i]) != NavisworksSourceIndex.NormalizeType(Parameters[i].Type))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a saved graph can still resolve this id to this method: its current signature, or a recorded earlier one.</summary>
    public bool Resolves(string definitionId) => HasSignatureOf(definitionId) || AliasIds.Contains(definitionId, StringComparer.Ordinal);

    /// <summary>The current name of a saved port name, or null when the port is gone (and no <c>[PortAlias]</c> says where it went).</summary>
    public string? CurrentInputName(string savedName)
    {
        if (Parameters.Any(p => p.Name == savedName))
        {
            return savedName;
        }

        return PortAliases.Where(a => a.Old == savedName && Parameters.Any(p => p.Name == a.Current)).Select(a => a.Current).FirstOrDefault();
    }

    /// <inheritdoc cref="CurrentInputName"/>
    public string? CurrentOutputName(string savedName)
    {
        if (Outputs.Contains(savedName))
        {
            return savedName;
        }

        return PortAliases.Where(a => a.Old == savedName && Outputs.Contains(a.Current)).Select(a => a.Current).FirstOrDefault();
    }
}

internal sealed class NavisworksSourceParameter
{
    public NavisworksSourceParameter(string name, string type, bool optional)
    {
        Name = name;
        Type = type;
        Optional = optional;
    }

    public string Name { get; }

    public string Type { get; }

    public bool Optional { get; }
}

internal static class NavisworksSourceIndex
{
    private static readonly Lazy<IReadOnlyList<NavisworksSourceNode>> All = new Lazy<IReadOnlyList<NavisworksSourceNode>>(Harvest);

    public static IReadOnlyList<NavisworksSourceNode> Nodes => All.Value;

    /// <summary>Every harvested method a definition id can resolve to (current signature or a recorded earlier id).</summary>
    public static IReadOnlyList<NavisworksSourceNode> Resolve(string definitionId)
    {
        return Nodes.Where(n => n.Resolves(definitionId)).ToList();
    }

    /// <summary>Splits <c>Ns.Class.Method@type1,type2</c> into the path and the parameter types.</summary>
    public static (string Path, List<string> Types) SplitId(string definitionId)
    {
        var at = definitionId.IndexOf('@');
        var path = at < 0 ? definitionId : definitionId.Substring(0, at);
        var types = at < 0 ? new List<string>() : SplitTopLevel(definitionId.Substring(at + 1), ',');
        return (path, types);
    }

    /// <summary>
    /// Compares a runtime type name from a definition id with a type as written in the source: namespaces are dropped (the source
    /// relies on <c>using</c>s), as are whitespace and the '?' nullable annotation.
    /// </summary>
    public static string NormalizeType(string type)
    {
        var withoutNamespaces = Regex.Replace(type.Trim(), @"\b(?:[A-Za-z_]\w*\.)+(?=[A-Za-z_])", string.Empty);
        return Regex.Replace(withoutNamespaces, @"[\s\?]", string.Empty);
    }

    private static readonly Regex NamespaceRegex = new Regex(@"^\s*namespace\s+([\w\.]+)\s*;", RegexOptions.Multiline);

    private static readonly Regex ClassRegex = new Regex(
        @"^\s*public\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)",
        RegexOptions.Multiline);

    // The attributes (an attribute may span several lines) and comments directly above a public static method, then the method itself.
    private static readonly Regex MethodRegex = new Regex(
        @"(?<attrs>(?:^[ \t]*(?:\[(?:[^\]""]|""[^""]*"")*\]|//[^\r\n]*)[ \t]*\r?\n)*)" +
        @"^[ \t]*public\s+static\s+(?<ret>[\w\.\<\>\[\]\?,\s]+?)\s+(?<name>\w+)\s*\(",
        RegexOptions.Multiline);

    private static IReadOnlyList<NavisworksSourceNode> Harvest()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(SampleGraphFileTests.SamplesDirectory())!,
            "src",
            "CamelGraph.Navisworks");
        Assert.True(Directory.Exists(directory), "Source directory not found: " + directory);

        var nodes = new List<NavisworksSourceNode>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(System.IO.Path.DirectorySeparatorChar + "obj" + System.IO.Path.DirectorySeparatorChar))
            {
                continue;
            }

            nodes.AddRange(ParseFile(File.ReadAllText(file).Replace("\r\n", "\n"), System.IO.Path.GetFileName(file)));
        }

        Assert.True(nodes.Count > 50, "Suspiciously few node methods harvested from CamelGraph.Navisworks: " + nodes.Count);
        return nodes;
    }

    private static IEnumerable<NavisworksSourceNode> ParseFile(string text, string fileName)
    {
        var namespaceMatch = NamespaceRegex.Match(text);
        if (!namespaceMatch.Success)
        {
            yield break;
        }

        var ns = namespaceMatch.Groups[1].Value;
        var classes = ClassRegex.Matches(text).Cast<Match>().ToList();

        foreach (Match method in MethodRegex.Matches(text))
        {
            var owner = classes.LastOrDefault(c => c.Index < method.Index);
            if (owner == null)
            {
                continue;
            }

            var parameterList = ExtractBalanced(text, method.Index + method.Length - 1);
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
                outputs = Regex.Matches(multiReturn.Groups[1].Value, "\"([^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            }
            else
            {
                var returnName = Regex.Match(attrs, @"\[return:\s*NodeName\(""([^""]*)""\)\]");
                outputs = new List<string> { returnName.Success ? returnName.Groups[1].Value : "result" };
            }

            var aliasIds = new List<string>();
            foreach (Match alias in Regex.Matches(attrs, @"\[NodeAliases\((?<args>(?:[^\)""]|""[^""]*"")*)\)\]"))
            {
                aliasIds.AddRange(Regex.Matches(alias.Groups["args"].Value, "\"([^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value));
            }

            var portAliases = Regex.Matches(attrs, @"\[PortAlias\(\s*""([^""]*)""\s*,\s*""([^""]*)""\s*\)\]")
                .Cast<Match>()
                .Select(m => (m.Groups[1].Value, m.Groups[2].Value))
                .ToList();

            yield return new NavisworksSourceNode(
                ns + "." + owner.Groups[1].Value + "." + method.Groups["name"].Value,
                fileName,
                parameters,
                outputs,
                aliasIds,
                portAliases,
                attrs.Contains("[NodeDeprecated(", StringComparison.Ordinal));
        }
    }

    private static NavisworksSourceParameter ParseParameter(string parameter)
    {
        parameter = StripLeadingAttributes(parameter);
        var declaration = SplitTopLevel(parameter, '=');
        var typeAndName = declaration[0].Trim();
        int nameStart = typeAndName.Length;
        while (nameStart > 0 && (char.IsLetterOrDigit(typeAndName[nameStart - 1]) || typeAndName[nameStart - 1] == '_'))
        {
            nameStart--;
        }

        return new NavisworksSourceParameter(
            typeAndName.Substring(nameStart),
            typeAndName.Substring(0, nameStart).Trim(),
            declaration.Count > 1);
    }

    private static string StripLeadingAttributes(string text)
    {
        text = text.Trim();
        while (text.StartsWith("[", StringComparison.Ordinal))
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
                else if (c == ']' && --depth == 0)
                {
                    end = i;
                    break;
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

    /// <summary>The text between the '(' at <paramref name="openIndex"/> and its balanced ')'.</summary>
    private static string? ExtractBalanced(string text, int openIndex)
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

            if (c == '"')
            {
                inString = true;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && --depth == 0)
            {
                return text.Substring(openIndex + 1, i - openIndex - 1);
            }
        }

        return null;
    }

    /// <summary>Splits on a separator that sits outside &lt;&gt;, (), [] and string literals.</summary>
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
}
