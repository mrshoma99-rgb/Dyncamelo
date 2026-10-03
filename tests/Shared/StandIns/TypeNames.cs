using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Dyncamelo.TestSupport.StandIns;

/// <summary>Helpers for the text of type names: splitting, and the name a definition id gives a type.</summary>
internal static class TypeNames
{
    /// <summary>Splits on a separator that sits outside &lt;&gt;, (), [] and string literals; every part is trimmed and empty parts are dropped.</summary>
    public static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var inString = false;
        var start = 0;
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

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '<':
                case '[':
                case '(':
                case '{':
                    depth++;
                    break;
                case '>':
                case ']':
                case ')':
                case '}':
                    // "=>" and ">=" are operators, not brackets.
                    if (c == '>' && i > 0 && (text[i - 1] == '=' || text[i - 1] == '-'))
                    {
                        break;
                    }

                    depth--;
                    break;
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
        return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }

    /// <summary>
    /// The text <c>AssemblyNodeLoader.GetFunctionSignature</c> writes into a definition id for a parameter type:
    /// C# keywords for the built-in types, full names otherwise, <c>Name&lt;arg,arg&gt;</c> for generics, <c>T[]</c> and <c>T?</c>.
    /// </summary>
    public static string Mangle(Type type)
    {
        if (type.IsArray)
        {
            return Mangle(type.GetElementType()!) + "[]";
        }

        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null)
        {
            return Mangle(nullable) + "?";
        }

        if (type.IsGenericType)
        {
            var definitionName = type.GetGenericTypeDefinition().FullName ?? type.Name;
            var backtick = definitionName.IndexOf('`');
            if (backtick >= 0)
            {
                definitionName = definitionName.Substring(0, backtick);
            }

            return definitionName + "<" + string.Join(",", type.GetGenericArguments().Select(Mangle)) + ">";
        }

        if (type == typeof(bool)) return "bool";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(sbyte)) return "sbyte";
        if (type == typeof(char)) return "char";
        if (type == typeof(short)) return "short";
        if (type == typeof(ushort)) return "ushort";
        if (type == typeof(int)) return "int";
        if (type == typeof(uint)) return "uint";
        if (type == typeof(long)) return "long";
        if (type == typeof(ulong)) return "ulong";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(string)) return "string";
        if (type == typeof(object)) return "object";
        return type.FullName ?? type.Name;
    }

    /// <summary>The built-in types the C# keywords stand for.</summary>
    public static readonly IReadOnlyDictionary<string, Type> Keywords = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        { "bool", typeof(bool) },
        { "byte", typeof(byte) },
        { "sbyte", typeof(sbyte) },
        { "char", typeof(char) },
        { "short", typeof(short) },
        { "ushort", typeof(ushort) },
        { "int", typeof(int) },
        { "uint", typeof(uint) },
        { "long", typeof(long) },
        { "ulong", typeof(ulong) },
        { "float", typeof(float) },
        { "double", typeof(double) },
        { "decimal", typeof(decimal) },
        { "string", typeof(string) },
        { "object", typeof(object) },
    };

    /// <summary>
    /// Reads a default value written as in C# source (<c>"Self"</c>, <c>0.5</c>, <c>-1</c>, <c>true</c>, <c>double.PositiveInfinity</c>, <c>null</c>)
    /// or as the node catalogue writes it (<c>PositiveInfinity</c>, <c>90.0</c>) into a value of <paramref name="type"/>.
    /// Returns false when the text is not understood.
    /// </summary>
    public static bool TryParseDefault(string text, Type type, out object? value)
    {
        value = null;
        var t = text.Trim();
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (t == "null" || t == "default" || t == "default!" || t == "null!")
        {
            return !underlying.IsValueType || Nullable.GetUnderlyingType(type) != null || t.StartsWith("default", StringComparison.Ordinal);
        }

        if (t.StartsWith("\"", StringComparison.Ordinal))
        {
            var s = ParseStringLiteralExpression(t);
            if (s == null || underlying != typeof(string) && underlying != typeof(object))
            {
                return false;
            }

            value = s;
            return true;
        }

        if (underlying == typeof(bool))
        {
            if (t == "true" || t == "false")
            {
                value = t == "true";
                return true;
            }

            return false;
        }

        // Strip the type prefix of constants such as double.PositiveInfinity, then read the number.
        var number = t;
        var dot = number.LastIndexOf('.');
        if (dot > 0 && char.IsLetter(number[0]) && !char.IsDigit(number[dot - 1]))
        {
            number = number.Substring(dot + 1);
        }

        double d;
        switch (number)
        {
            case "PositiveInfinity":
            case "Infinity":
                d = double.PositiveInfinity;
                break;
            case "NegativeInfinity":
                d = double.NegativeInfinity;
                break;
            case "NaN":
                d = double.NaN;
                break;
            case "MaxValue":
                d = underlying == typeof(int) ? int.MaxValue : underlying == typeof(long) ? long.MaxValue : double.MaxValue;
                break;
            case "MinValue":
                d = underlying == typeof(int) ? int.MinValue : underlying == typeof(long) ? long.MinValue : double.MinValue;
                break;
            default:
                var trimmed = number.TrimEnd('d', 'D', 'f', 'F', 'm', 'M', 'L', 'l');
                if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                {
                    return false;
                }

                break;
        }

        try
        {
            if (underlying.IsEnum)
            {
                value = Enum.ToObject(underlying, (long)d);
            }
            else if (underlying == typeof(double))
            {
                value = d;
            }
            else if (underlying == typeof(float))
            {
                value = (float)d;
            }
            else if (underlying == typeof(object))
            {
                return false;
            }
            else
            {
                value = Convert.ChangeType(d, underlying, CultureInfo.InvariantCulture);
            }

            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException || ex is OverflowException || ex is FormatException)
        {
            return false;
        }
    }

    /// <summary>The value of one C# string literal, or of several joined with '+'; null when the text is not made of string literals.</summary>
    public static string? ParseStringLiteralExpression(string text)
    {
        var builder = new StringBuilder();
        var i = 0;
        var any = false;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) || c == '+')
            {
                i++;
                continue;
            }

            var verbatim = false;
            if (c == '@' && i + 1 < text.Length && text[i + 1] == '"')
            {
                verbatim = true;
                i++;
                c = '"';
            }

            if (c != '"')
            {
                return null;
            }

            i++;
            while (true)
            {
                if (i >= text.Length)
                {
                    return null;
                }

                c = text[i];
                if (verbatim)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            builder.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    builder.Append(c);
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    i++;
                    break;
                }

                if (c == '\\' && i + 1 < text.Length)
                {
                    var next = text[i + 1];
                    switch (next)
                    {
                        case 'n': builder.Append('\n'); break;
                        case 't': builder.Append('\t'); break;
                        case 'r': builder.Append('\r'); break;
                        case '0': builder.Append('\0'); break;
                        case 'u':
                            if (i + 5 < text.Length && int.TryParse(text.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            {
                                builder.Append((char)code);
                                i += 4;
                            }

                            break;
                        default: builder.Append(next); break;
                    }

                    i += 2;
                    continue;
                }

                builder.Append(c);
                i++;
            }

            any = true;
        }

        return any ? builder.ToString() : null;
    }
}
