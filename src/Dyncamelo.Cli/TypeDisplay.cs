using System;
using System.Linq;

namespace Dyncamelo.Cli;

/// <summary>Renders CLR types the way a C# programmer reads them ("IList&lt;double&gt;", "double").</summary>
internal static class TypeDisplay
{
    /// <summary>Formats a type as short C#-style text.</summary>
    public static string Format(Type type)
    {
        if (type.IsArray)
        {
            return Format(type.GetElementType()!) + "[]";
        }

        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null)
        {
            return Format(nullable) + "?";
        }

        if (type.IsGenericType)
        {
            var name = type.Name;
            var backtick = name.IndexOf('`');
            if (backtick >= 0)
            {
                name = name.Substring(0, backtick);
            }

            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(Format)) + ">";
        }

        if (type == typeof(bool)) return "bool";
        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "long";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(string)) return "string";
        if (type == typeof(object)) return "object";

        return type.Name;
    }
}
