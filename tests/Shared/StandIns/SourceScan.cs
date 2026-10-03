using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Dyncamelo.TestSupport.StandIns;

/// <summary>An attribute as written in a C# source file: <c>[NodeRange(0, 100, Step = 5)]</c>.</summary>
internal sealed class SourceAttribute
{
    public SourceAttribute(string name, string? target, IReadOnlyList<string> positional, IReadOnlyDictionary<string, string> named, IReadOnlyDictionary<string, string> constants)
    {
        Name = name;
        Target = target;
        Positional = positional;
        Named = named;
        Constants = constants;
    }

    /// <summary>Named constants of the scanned sources (<c>const int Limit = 500;</c>), for arguments that name one.</summary>
    public IReadOnlyDictionary<string, string> Constants { get; }

    private double? Resolve(string text)
    {
        var t = text.Trim();
        if (TypeNames.TryParseDefault(t, typeof(double), out var d) && d is double x)
        {
            return x;
        }

        var name = t.Substring(t.LastIndexOf('.') + 1);
        return Constants.TryGetValue(name, out var value) && TypeNames.TryParseDefault(value, typeof(double), out var d2) && d2 is double y ? y : (double?)null;
    }

    /// <summary>The attribute's name without the <c>Attribute</c> suffix.</summary>
    public string Name { get; }

    /// <summary>The attribute target (<c>return</c>), or null.</summary>
    public string? Target { get; }

    /// <summary>The argument texts as written, in order.</summary>
    public IReadOnlyList<string> Positional { get; }

    /// <summary>The <c>Name = value</c> arguments.</summary>
    public IReadOnlyDictionary<string, string> Named { get; }

    /// <summary>The positional arguments read as string literals; an argument that is not one is skipped.</summary>
    public string[] Strings() => Positional.Select(ResolveString).Where(s => s != null).Select(s => s!).ToArray();

    public string? NamedString(string name) => Named.TryGetValue(name, out var v) ? ResolveString(v) : null;

    // A string literal, or the name of a string constant (NodeDataSource.Selection, or one declared in the scanned sources).
    private string? ResolveString(string text)
    {
        var literal = TypeNames.ParseStringLiteralExpression(text);
        if (literal != null)
        {
            return literal;
        }

        var t = text.Trim();
        if (t == "NodeDataSource.Selection" || t.EndsWith(".NodeDataSource.Selection", StringComparison.Ordinal))
        {
            return Dyncamelo.Core.Loader.NodeDataSource.Selection;
        }

        var name = t.Substring(t.LastIndexOf('.') + 1);
        return Constants.TryGetValue(name, out var value) ? TypeNames.ParseStringLiteralExpression(value) : null;
    }

    public double? NamedNumber(string name) => Named.TryGetValue(name, out var v) ? Resolve(v) : null;

    public bool NamedBool(string name) => Named.TryGetValue(name, out var v) && v.Trim() == "true";

    public double? Number(int index) => index < Positional.Count ? Resolve(Positional[index]) : null;
}

/// <summary>A parameter of a node method as written in the source.</summary>
internal sealed class SourceParameter
{
    public SourceParameter(string name, string typeText, string? defaultText, IReadOnlyList<SourceAttribute> attributes)
    {
        Name = name;
        TypeText = typeText;
        DefaultText = defaultText;
        Attributes = attributes;
    }

    public string Name { get; }

    public string TypeText { get; }

    public string? DefaultText { get; }

    public bool IsOptional => DefaultText != null;

    public IReadOnlyList<SourceAttribute> Attributes { get; }

    public SourceAttribute? Attribute(string name) => Attributes.FirstOrDefault(a => a.Name == name);
}

/// <summary>A public static method of a node class as written in the source.</summary>
internal sealed class SourceMethod
{
    public SourceMethod(
        string file,
        string namespaceName,
        string className,
        IReadOnlyList<SourceAttribute> classAttributes,
        string methodName,
        string returnText,
        IReadOnlyList<SourceParameter> parameters,
        IReadOnlyList<SourceAttribute> attributes)
    {
        File = file;
        Namespace = namespaceName;
        ClassName = className;
        ClassAttributes = classAttributes;
        MethodName = methodName;
        ReturnText = returnText;
        Parameters = parameters;
        Attributes = attributes;
    }

    public string File { get; }

    public string Namespace { get; }

    public string ClassName { get; }

    public IReadOnlyList<SourceAttribute> ClassAttributes { get; }

    public string MethodName { get; }

    /// <summary>The return type as written (<c>List&lt;ModelItem&gt;</c>, <c>void</c>).</summary>
    public string ReturnText { get; }

    public IReadOnlyList<SourceParameter> Parameters { get; }

    /// <summary>Attributes of the method, including <c>[return: ...]</c> ones (their <see cref="SourceAttribute.Target"/> is "return").</summary>
    public IReadOnlyList<SourceAttribute> Attributes { get; }

    /// <summary><c>Namespace.Class.Method</c>, the part of a definition id before the '@'.</summary>
    public string Path => Namespace + "." + ClassName + "." + MethodName;

    public SourceAttribute? Attribute(string name) => Attributes.FirstOrDefault(a => a.Name == name && a.Target == null);

    public SourceAttribute? ReturnAttribute(string name) => Attributes.FirstOrDefault(a => a.Name == name && a.Target == "return");

    public bool IsDeprecated => Attribute("NodeDeprecated") != null;

    public override string ToString() => Path + "(" + string.Join(", ", Parameters.Select(p => p.Name)) + ")";
}

/// <summary>
/// What the C# source of <c>Dyncamelo.Navisworks</c> says about its nodes, harvested from the text because that project cannot be
/// loaded where the tests run. Used for what the node catalogue does not carry: the drop-down choices, ranges, name-search buttons and
/// socket kinds of the parameters, the declared function of a node, and the retired nodes that old graphs still use.
/// </summary>
internal sealed class SourceScan
{
    private static readonly Regex NamespaceRegex = new Regex(@"^\s*namespace\s+([\w\.]+)\s*[;{]", RegexOptions.Multiline);

    private static readonly Regex ClassRegex = new Regex(
        @"(?<attrs>(?:^[ \t]*(?:\[(?:[^\]""]|""[^""]*"")*\]|//[^\r\n]*)[ \t]*\r?\n)*)" +
        @"^[ \t]*public\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+)*class\s+(?<name>\w+)",
        RegexOptions.Multiline);

    // The attributes (an attribute may span several lines) and comments directly above a public static method, then the method itself.
    private static readonly Regex MethodRegex = new Regex(
        @"(?<attrs>(?:^[ \t]*(?:\[(?:[^\]""]|""[^""]*"")*\]|//[^\r\n]*)[ \t]*\r?\n)*)" +
        @"^[ \t]*public\s+static\s+(?<ret>[\w\.\<\>\[\]\?,\s]+?)\s+(?<name>\w+)\s*\(",
        RegexOptions.Multiline);

    private static readonly Regex ConstantRegex = new Regex(
        @"\bconst\s+(?:int|long|double|float|decimal|string)\s+(?<name>\w+)\s*=\s*(?<value>[^;]+);");

    private readonly List<SourceMethod> _methods;

    private SourceScan(List<SourceMethod> methods)
    {
        _methods = methods;
    }

    public IReadOnlyList<SourceMethod> Methods => _methods;

    /// <summary>Reads every <c>.cs</c> file under a source folder (not <c>obj</c> or <c>bin</c>).</summary>
    public static SourceScan Read(string directory)
    {
        var texts = new List<(string File, string Text)>();
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var relative = file.Substring(directory.Length).Replace('\\', '/');
            if (relative.StartsWith("/obj/", StringComparison.Ordinal) || relative.StartsWith("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file).Replace("\r\n", "\n");
            texts.Add((Path.GetFileName(file), text));
            foreach (Match constant in ConstantRegex.Matches(text))
            {
                constants[constant.Groups["name"].Value] = constant.Groups["value"].Value.Trim();
            }
        }

        var methods = new List<SourceMethod>();
        foreach (var (name, text) in texts)
        {
            methods.AddRange(ParseFile(text, name, constants));
        }

        return new SourceScan(methods);
    }

    /// <summary>Reads the Navisworks node sources of this repository.</summary>
    public static SourceScan ReadNavisworks() => Read(RepoLocator.NavisworksSourceDirectory());

    /// <summary>The method a catalogue node comes from: same <c>Namespace.Class.Method</c> and the same parameter names in order.</summary>
    public SourceMethod? Find(string path, IEnumerable<string> parameterNames)
    {
        var names = parameterNames.ToList();
        return _methods.FirstOrDefault(m => m.Path == path && m.Parameters.Select(p => p.Name).SequenceEqual(names));
    }

    private static IEnumerable<SourceMethod> ParseFile(string text, string fileName, IReadOnlyDictionary<string, string> constants)
    {
        var ns = NamespaceRegex.Match(text);
        if (!ns.Success)
        {
            yield break;
        }

        var classes = ClassRegex.Matches(text).Cast<Match>().ToList();
        foreach (Match method in MethodRegex.Matches(text))
        {
            var owner = classes.LastOrDefault(c => c.Index < method.Index);
            if (owner == null)
            {
                continue;
            }

            var open = method.Index + method.Length - 1;
            var parameterText = ExtractBalanced(text, open);
            if (parameterText == null)
            {
                continue;
            }

            var parameters = TypeNames.SplitTopLevel(parameterText, ',').Select(p => ParseParameter(p, constants)).Where(p => p != null).Select(p => p!).ToList();
            yield return new SourceMethod(
                fileName,
                ns.Groups[1].Value,
                owner.Groups["name"].Value,
                ParseAttributes(owner.Groups["attrs"].Value, constants),
                method.Groups["name"].Value,
                Regex.Replace(method.Groups["ret"].Value.Trim(), @"\s+", " "),
                parameters,
                ParseAttributes(method.Groups["attrs"].Value, constants));
        }
    }

    private static SourceParameter? ParseParameter(string text, IReadOnlyDictionary<string, string> constants)
    {
        var rest = text.Trim();
        var attributes = new List<SourceAttribute>();
        while (rest.StartsWith("[", StringComparison.Ordinal))
        {
            var end = FindClosingBracket(rest, 0);
            if (end < 0)
            {
                break;
            }

            attributes.AddRange(ParseAttributes(rest.Substring(0, end + 1), constants));
            rest = rest.Substring(end + 1).Trim();
        }

        string? defaultText = null;
        var parts = SplitOnTopLevelEquals(rest);
        if (parts.Count > 1)
        {
            defaultText = parts[1].Trim();
        }

        var declaration = parts[0].Trim();
        var nameStart = declaration.Length;
        while (nameStart > 0 && (char.IsLetterOrDigit(declaration[nameStart - 1]) || declaration[nameStart - 1] == '_'))
        {
            nameStart--;
        }

        if (nameStart == 0 || nameStart == declaration.Length)
        {
            return null;
        }

        var typeText = Regex.Replace(declaration.Substring(0, nameStart).Trim(), @"\s+", " ");
        return new SourceParameter(declaration.Substring(nameStart), typeText, defaultText, attributes);
    }

    /// <summary>Splits "type name = default" at the first '=' that is not inside brackets or a string.</summary>
    private static List<string> SplitOnTopLevelEquals(string text)
    {
        var depth = 0;
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
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
            else if (c == '<' || c == '(' || c == '[')
            {
                depth++;
            }
            else if (c == '>' || c == ')' || c == ']')
            {
                depth--;
            }
            else if (c == '=' && depth == 0)
            {
                return new List<string> { text.Substring(0, i), text.Substring(i + 1) };
            }
        }

        return new List<string> { text };
    }

    /// <summary>Reads every <c>[...]</c> section of a text (comment lines are ignored) into attributes.</summary>
    private static List<SourceAttribute> ParseAttributes(string block, IReadOnlyDictionary<string, string> constants)
    {
        var attributes = new List<SourceAttribute>();
        var cleaned = string.Join("\n", block.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        var i = 0;
        while (i < cleaned.Length)
        {
            if (cleaned[i] != '[')
            {
                i++;
                continue;
            }

            var end = FindClosingBracket(cleaned, i);
            if (end < 0)
            {
                break;
            }

            var inner = cleaned.Substring(i + 1, end - i - 1);
            string? target = null;
            var targetMatch = Regex.Match(inner, @"^\s*(?<t>[a-z]+)\s*:(?!:)");
            if (targetMatch.Success)
            {
                target = targetMatch.Groups["t"].Value;
                inner = inner.Substring(targetMatch.Length);
            }

            foreach (var one in TypeNames.SplitTopLevel(inner, ','))
            {
                var attribute = ParseAttribute(one, target, constants);
                if (attribute != null)
                {
                    attributes.Add(attribute);
                }
            }

            i = end + 1;
        }

        return attributes;
    }

    private static SourceAttribute? ParseAttribute(string text, string? target, IReadOnlyDictionary<string, string> constants)
    {
        var m = Regex.Match(text.Trim(), @"^(?<name>[\w\.]+)\s*(?:\((?<args>.*)\))?\s*$", RegexOptions.Singleline);
        if (!m.Success)
        {
            return null;
        }

        var name = m.Groups["name"].Value;
        name = name.Substring(name.LastIndexOf('.') + 1);
        if (name.EndsWith("Attribute", StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - "Attribute".Length);
        }

        var positional = new List<string>();
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var arg in TypeNames.SplitTopLevel(m.Groups["args"].Value, ','))
        {
            var assignment = Regex.Match(arg, @"^(?<n>\w+)\s*=(?!=)\s*(?<v>.+)$", RegexOptions.Singleline);
            if (assignment.Success)
            {
                named[assignment.Groups["n"].Value] = assignment.Groups["v"].Value.Trim();
            }
            else
            {
                positional.Add(arg);
            }
        }

        return new SourceAttribute(name, target, positional, named, constants);
    }

    /// <summary>The index of the ']' matching the '[' at <paramref name="open"/>, or -1.</summary>
    private static int FindClosingBracket(string text, int open)
    {
        var depth = 0;
        var inString = false;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
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
                return i;
            }
        }

        return -1;
    }

    /// <summary>The text between the '(' at <paramref name="openIndex"/> and its balanced ')'.</summary>
    private static string? ExtractBalanced(string text, int openIndex)
    {
        var depth = 0;
        var inString = false;
        for (var i = openIndex; i < text.Length; i++)
        {
            var c = text[i];
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
}
