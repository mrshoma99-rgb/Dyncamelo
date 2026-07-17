using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Dyncamelo.Core.Loader;

/// <summary>
/// Reads the compiler-generated XML documentation file that ships next to a
/// node-pack assembly and exposes per-method docs: the <c>&lt;summary&gt;</c>,
/// each <c>&lt;param&gt;</c> and the <c>&lt;returns&gt;</c> text. The loader
/// uses these to give zero-touch ports real descriptions (port tooltips)
/// without hand-tagging every parameter. A missing or unreadable file simply
/// yields no docs — loading never fails because of documentation.
/// </summary>
public static class XmlDocumentation
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, MemberDoc>> Cache =
        new ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, MemberDoc>>();

    /// <summary>Documentation of one member (method) from the XML file.</summary>
    public sealed class MemberDoc
    {
        /// <summary>The <c>&lt;summary&gt;</c> text, whitespace-normalized.</summary>
        public string Summary { get; internal set; } = string.Empty;

        /// <summary>The <c>&lt;returns&gt;</c> text, whitespace-normalized.</summary>
        public string Returns { get; internal set; } = string.Empty;

        /// <summary>Per-parameter <c>&lt;param&gt;</c> texts keyed by parameter name.</summary>
        public IReadOnlyDictionary<string, string> Parameters { get; internal set; } =
            new Dictionary<string, string>();
    }

    /// <summary>
    /// Returns the documentation of a method, or <c>null</c> when the
    /// assembly ships no XML documentation (or the method has none).
    /// </summary>
    /// <param name="method">The method to document.</param>
    public static MemberDoc? ForMethod(MethodInfo method)
    {
        if (method?.DeclaringType == null)
        {
            return null;
        }

        var docs = Cache.GetOrAdd(method.DeclaringType.Assembly, LoadAssemblyDocs);
        return docs.TryGetValue(GetMethodDocId(method), out var doc) ? doc : null;
    }

    private static IReadOnlyDictionary<string, MemberDoc> LoadAssemblyDocs(Assembly assembly)
    {
        var result = new Dictionary<string, MemberDoc>(StringComparer.Ordinal);
        try
        {
            var location = assembly.Location;
            if (string.IsNullOrEmpty(location))
            {
                return result;
            }

            var xmlPath = Path.ChangeExtension(location, ".xml");
            if (!File.Exists(xmlPath))
            {
                return result;
            }

            using (var reader = XmlReader.Create(xmlPath, new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                DtdProcessing = DtdProcessing.Prohibit,
            }))
            {
                while (reader.ReadToFollowing("member"))
                {
                    var name = reader.GetAttribute("name");
                    if (name == null || !name.StartsWith("M:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    using (var member = reader.ReadSubtree())
                    {
                        result[name] = ReadMember(member);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is XmlException || ex is UnauthorizedAccessException || ex is NotSupportedException)
        {
            // Docs are an enhancement, never a load requirement.
        }

        return result;
    }

    private static MemberDoc ReadMember(XmlReader member)
    {
        var doc = new MemberDoc();
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        member.Read();
        while (member.Read())
        {
            if (member.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (member.Name)
            {
                case "summary":
                    doc.Summary = Normalize(ReadElementText(member));
                    break;
                case "returns":
                    doc.Returns = Normalize(ReadElementText(member));
                    break;
                case "param":
                    var paramName = member.GetAttribute("name");
                    var text = Normalize(ReadElementText(member));
                    if (!string.IsNullOrEmpty(paramName) && text.Length > 0)
                    {
                        parameters[paramName!] = text;
                    }

                    break;
            }
        }

        doc.Parameters = parameters;
        return doc;
    }

    /// <summary>
    /// Flattens an element to plain text, rendering inline doc tags readably:
    /// <c>&lt;see cref="T:Foo.Bar"/&gt;</c> becomes <c>Bar</c>, and
    /// <c>&lt;c&gt;</c>/<c>&lt;paramref&gt;</c> keep their text/name.
    /// </summary>
    private static string ReadElementText(XmlReader element)
    {
        var text = new StringBuilder();
        using (var subtree = element.ReadSubtree())
        {
            subtree.Read();
            while (subtree.Read())
            {
                switch (subtree.NodeType)
                {
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                        text.Append(subtree.Value);
                        break;
                    case XmlNodeType.Element when subtree.Name == "see" || subtree.Name == "seealso":
                        text.Append(ShortCref(subtree.GetAttribute("cref") ?? subtree.GetAttribute("langword") ?? string.Empty));
                        break;
                    case XmlNodeType.Element when subtree.Name == "paramref" || subtree.Name == "typeparamref":
                        text.Append(subtree.GetAttribute("name"));
                        break;
                }
            }
        }

        return text.ToString();
    }

    private static string ShortCref(string cref)
    {
        // "T:Autodesk.Navisworks.Api.ModelItem" -> "ModelItem".
        var name = cref;
        int colon = name.IndexOf(':');
        if (colon >= 0)
        {
            name = name.Substring(colon + 1);
        }

        int paren = name.IndexOf('(');
        if (paren >= 0)
        {
            name = name.Substring(0, paren);
        }

        int dot = name.LastIndexOf('.');
        return dot >= 0 ? name.Substring(dot + 1) : name;
    }

    private static string Normalize(string text)
    {
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Builds the compiler's documentation id for a method
    /// (<c>M:Namespace.Type.Method(System.String,System.Collections.Generic.IEnumerable{Foo.Bar})</c>).
    /// Zero-touch methods are non-generic statics, so only the constructs they
    /// can use are handled: generic type instantiations, arrays and nested types.
    /// </summary>
    internal static string GetMethodDocId(MethodInfo method)
    {
        var id = new StringBuilder("M:");
        id.Append(GetTypeDocName(method.DeclaringType!));
        id.Append('.').Append(method.Name);

        var parameters = method.GetParameters();
        if (parameters.Length > 0)
        {
            id.Append('(');
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                {
                    id.Append(',');
                }

                id.Append(GetParameterDocName(parameters[i].ParameterType));
            }

            id.Append(')');
        }

        return id.ToString();
    }

    private static string GetTypeDocName(Type type)
    {
        // Nested types use '.' in doc ids (runtime full names use '+').
        return (type.FullName ?? type.Name).Replace('+', '.');
    }

    private static string GetParameterDocName(Type type)
    {
        if (type.IsArray)
        {
            return GetParameterDocName(type.GetElementType()!) + "[]";
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var fullName = (definition.FullName ?? definition.Name).Replace('+', '.');
            int tick = fullName.IndexOf('`');
            if (tick >= 0)
            {
                fullName = fullName.Substring(0, tick);
            }

            var args = type.GetGenericArguments();
            var name = new StringBuilder(fullName).Append('{');
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0)
                {
                    name.Append(',');
                }

                name.Append(GetParameterDocName(args[i]));
            }

            return name.Append('}').ToString();
        }

        return GetTypeDocName(type);
    }
}
