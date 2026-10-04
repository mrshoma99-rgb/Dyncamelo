using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using CamelGraph.Core.Loader;
using CamelGraph.Nodes;

namespace CamelGraph.TestSupport.StandIns;

/// <summary>
/// Turns the type names of a node signature into <see cref="Type"/>s. Types of the .NET library and of CamelGraph's own assemblies
/// are the real ones; the types of the host application (<c>Autodesk.Navisworks.Api.ModelItem</c>, <c>Document</c>...) are empty
/// public classes of the same full name made in a dynamic module, so a definition id built from them is the real one and the editor
/// colours their sockets by name exactly as it does for the real types.
/// </summary>
internal sealed class StandInTypes
{
    /// <summary>The namespace a host type gets when nothing says better (only used for types that never appear in a definition id).</summary>
    public const string DefaultNamespace = "Autodesk.Navisworks.Api";

    // The members of the Autodesk.Navisworks.Api.Units enum (Navisworks 2024-2026), which Units.Convert and Units.ScaleFactor take.
    private static readonly string[] UnitsMembers =
    {
        "Meters", "Centimeters", "Millimeters", "Feet", "Inches", "Yards", "Kilometers", "Miles", "Micrometers", "Mils", "Microinches",
    };

    private static readonly Dictionary<string, Type> GenericDefinitions = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        { "System.Collections.Generic.IEnumerable", typeof(IEnumerable<>) },
        { "System.Collections.Generic.ICollection", typeof(ICollection<>) },
        { "System.Collections.Generic.IList", typeof(IList<>) },
        { "System.Collections.Generic.List", typeof(List<>) },
        { "System.Collections.Generic.IReadOnlyList", typeof(IReadOnlyList<>) },
        { "System.Collections.Generic.IReadOnlyCollection", typeof(IReadOnlyCollection<>) },
        { "System.Collections.Generic.IDictionary", typeof(IDictionary<,>) },
        { "System.Collections.Generic.Dictionary", typeof(Dictionary<,>) },
        { "System.Nullable", typeof(Nullable<>) },
    };

    private static readonly Dictionary<string, Type> SimpleSystemTypes = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        { "DateTime", typeof(DateTime) },
        { "DateTimeOffset", typeof(DateTimeOffset) },
        { "TimeSpan", typeof(TimeSpan) },
        { "Guid", typeof(Guid) },
        { "IDictionary", typeof(IDictionary) },
        { "IEnumerable", typeof(IEnumerable) },
        { "IList", typeof(IList) },
    };

    // The catalogue's short names for CamelGraph's own value types (generate_node_catalog.py: CAMELGRAPH_TYPES).
    private static readonly Dictionary<string, Type> CatalogueAliases = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        { "Point", typeof(CamelGraphPoint) },
        { "Vector", typeof(CamelGraphVector) },
        { "Color", typeof(CamelGraphColor) },
        { "BoundingBox", typeof(CamelGraphBoundingBox) },
    };

    private readonly ModuleBuilder _module;
    private readonly Dictionary<string, Type> _made = new Dictionary<string, Type>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fullBySimple = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _ownBySimple;

    public StandInTypes(ModuleBuilder module)
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
        _ownBySimple = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var assembly in new[] { typeof(NodeRegistry).Assembly, typeof(NodeLibrary).Assembly })
        {
            foreach (var type in assembly.GetExportedTypes())
            {
                if (!type.IsGenericTypeDefinition && !_ownBySimple.ContainsKey(type.Name))
                {
                    _ownBySimple[type.Name] = type;
                }
            }
        }
    }

    /// <summary>The stand-in types made so far (full name to type).</summary>
    public IReadOnlyDictionary<string, Type> StandIns => _made;

    /// <summary>
    /// Remembers which full names the parameter types of a definition id use, so a short name written in the source
    /// (<c>ModelItem</c>) can be turned into the full name (<c>Autodesk.Navisworks.Api.ModelItem</c>) the id needs.
    /// </summary>
    public void Learn(string definitionId)
    {
        var at = definitionId.IndexOf('@');
        if (at < 0)
        {
            return;
        }

        foreach (Match token in Regex.Matches(definitionId.Substring(at + 1), @"[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)+"))
        {
            var full = token.Value;
            if (full.StartsWith("System.", StringComparison.Ordinal))
            {
                continue;
            }

            var simple = full.Substring(full.LastIndexOf('.') + 1);
            if (!_fullBySimple.ContainsKey(simple))
            {
                _fullBySimple[simple] = full;
            }
        }
    }

    /// <summary>The type a parameter of a definition id has (<c>System.Collections.Generic.IEnumerable&lt;Autodesk.Navisworks.Api.ModelItem&gt;</c>).</summary>
    public Type FromId(string mangled)
    {
        var position = 0;
        var type = ParseMangled(mangled.Trim(), ref position);
        if (position != mangled.Trim().Length)
        {
            throw new FormatException("Unexpected text in the type '" + mangled + "' at " + position + ".");
        }

        return type;
    }

    /// <summary>The type of a C# type as written in the source (<c>List&lt;ModelItem&gt;</c>, <c>string?</c>, <c>IList&lt;object?&gt;</c>).</summary>
    public Type FromSource(string text)
    {
        var t = Regex.Replace(text.Trim(), @"\s+", " ");
        if (t == "void")
        {
            return typeof(void);
        }

        if (t.StartsWith("(", StringComparison.Ordinal))
        {
            return typeof(object); // a tuple: nothing about it matters for a socket
        }

        var nullable = false;
        if (t.EndsWith("?", StringComparison.Ordinal))
        {
            t = t.Substring(0, t.Length - 1);
            nullable = true;
        }

        Type result;
        if (t.EndsWith("[]", StringComparison.Ordinal))
        {
            result = FromSource(t.Substring(0, t.Length - 2)).MakeArrayType();
        }
        else
        {
            var lt = t.IndexOf('<');
            if (lt > 0 && t.EndsWith(">", StringComparison.Ordinal))
            {
                var name = t.Substring(0, lt).Trim();
                var args = TypeNames.SplitTopLevel(t.Substring(lt + 1, t.Length - lt - 2), ',').Select(FromSource).ToArray();
                result = GenericDefinition(name, args.Length).MakeGenericType(args);
            }
            else
            {
                result = FromSimpleName(t);
            }
        }

        return nullable && result.IsValueType && Nullable.GetUnderlyingType(result) == null ? typeof(Nullable<>).MakeGenericType(result) : result;
    }

    /// <summary>The type of a port as the node catalogue names it (<c>number</c>, <c>string</c>, <c>ModelItem[]</c>, <c>any</c>).</summary>
    public Type FromCatalogue(string text)
    {
        var t = text.Trim();
        if (t.EndsWith("[]", StringComparison.Ordinal))
        {
            return typeof(List<>).MakeGenericType(FromCatalogue(t.Substring(0, t.Length - 2)));
        }

        switch (t)
        {
            case "number": return typeof(double);
            case "integer": return typeof(int);
            case "boolean": return typeof(bool);
            case "string": return typeof(string);
            case "any": return typeof(object);
            case "nothing": return typeof(void);
            case "datetime": return typeof(DateTime);
            case "duration": return typeof(TimeSpan);
            case "guid": return typeof(Guid);
            case "dict": return typeof(Dictionary<string, object>);
            case "file": return typeof(string);
        }

        return CatalogueAliases.TryGetValue(t, out var alias) ? alias : FromSimpleName(t);
    }

    /// <summary>The real type with this full name when it is a .NET or CamelGraph type, otherwise the stand-in of that name.</summary>
    public Type FromFullName(string fullName)
    {
        var known = Type.GetType(fullName, false);
        if (known != null && fullName.StartsWith("System.", StringComparison.Ordinal))
        {
            return known;
        }

        if (fullName.StartsWith("CamelGraph.", StringComparison.Ordinal))
        {
            var own = typeof(NodeRegistry).Assembly.GetType(fullName, false) ?? typeof(NodeLibrary).Assembly.GetType(fullName, false);
            if (own != null)
            {
                return own;
            }
        }

        return MakeStandIn(fullName);
    }

    private Type GenericDefinition(string name, int arity)
    {
        var simple = name.Substring(name.LastIndexOf('.') + 1);
        foreach (var pair in GenericDefinitions)
        {
            if (pair.Key.EndsWith("." + simple, StringComparison.Ordinal) && pair.Value.GetGenericArguments().Length == arity)
            {
                return pair.Value;
            }
        }

        throw new NotSupportedException("The generic type '" + name + "' with " + arity + " arguments is not known to the stand-in builder.");
    }

    private Type FromSimpleName(string name)
    {
        if (name.Contains("."))
        {
            // Written with its namespace (Autodesk.Navisworks.Api.Units): that is already the full name.
            return FromFullName(name);
        }

        if (TypeNames.Keywords.TryGetValue(name, out var keyword))
        {
            return keyword;
        }

        if (SimpleSystemTypes.TryGetValue(name, out var system))
        {
            return system;
        }

        if (_fullBySimple.TryGetValue(name, out var full))
        {
            return FromFullName(full);
        }

        if (_ownBySimple.TryGetValue(name, out var own))
        {
            return own;
        }

        return MakeStandIn(DefaultNamespace + "." + name);
    }

    private Type ParseMangled(string text, ref int position)
    {
        var start = position;
        while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_' || text[position] == '.' || text[position] == '`' || text[position] == '+'))
        {
            position++;
        }

        var name = text.Substring(start, position - start);
        if (name.Length == 0)
        {
            throw new FormatException("A type name was expected in '" + text + "' at " + start + ".");
        }

        Type result;
        if (position < text.Length && text[position] == '<')
        {
            position++;
            var args = new List<Type>();
            while (true)
            {
                args.Add(ParseMangled(text, ref position));
                if (position < text.Length && text[position] == ',')
                {
                    position++;
                    continue;
                }

                break;
            }

            if (position >= text.Length || text[position] != '>')
            {
                throw new FormatException("A '>' was expected in '" + text + "' at " + position + ".");
            }

            position++;
            if (!GenericDefinitions.TryGetValue(name, out var definition) || definition.GetGenericArguments().Length != args.Count)
            {
                throw new NotSupportedException("The generic type '" + name + "' with " + args.Count + " arguments is not known to the stand-in builder.");
            }

            result = definition.MakeGenericType(args.ToArray());
        }
        else if (TypeNames.Keywords.TryGetValue(name, out var keyword))
        {
            result = keyword;
        }
        else
        {
            result = FromFullName(name);
        }

        while (position + 1 < text.Length && text[position] == '[' && text[position + 1] == ']')
        {
            result = result.MakeArrayType();
            position += 2;
        }

        if (position < text.Length && text[position] == '?')
        {
            position++;
            result = typeof(Nullable<>).MakeGenericType(result);
        }

        return result;
    }

    private Type MakeStandIn(string fullName)
    {
        if (_made.TryGetValue(fullName, out var existing))
        {
            return existing;
        }

        Type created;
        if (fullName == "Autodesk.Navisworks.Api.Units")
        {
            var enumBuilder = _module.DefineEnum(fullName, TypeAttributes.Public, typeof(int));
            for (var i = 0; i < UnitsMembers.Length; i++)
            {
                enumBuilder.DefineLiteral(UnitsMembers[i], i);
            }

            created = enumBuilder.CreateType()!;
        }
        else
        {
            var builder = _module.DefineType(fullName, TypeAttributes.Public | TypeAttributes.Class);
            builder.DefineDefaultConstructor(MethodAttributes.Public);
            created = builder.CreateType()!;
        }

        _made[fullName] = created;
        return created;
    }
}
