using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Dyncamelo.Core.Types;

/// <summary>
/// Gentle value coercion used at node-invocation time: exact match, numeric
/// widening, enum conversion, <see cref="IConvertible"/> fallback (invariant
/// culture) and element-wise list conversion. Pure static helpers — no state.
/// </summary>
public static class TypeCoercion
{
    /// <summary>
    /// Attempts to coerce <paramref name="value"/> to <paramref name="targetType"/>.
    /// Lists are never coerced to scalars or vice versa (replication owns that).
    /// </summary>
    /// <param name="value">The value to convert (may be null).</param>
    /// <param name="targetType">The requested CLR type.</param>
    /// <param name="result">The converted value on success.</param>
    /// <returns>True when the conversion succeeded.</returns>
    public static bool TryCoerce(object? value, Type targetType, out object? result)
    {
        if (targetType == null)
        {
            throw new ArgumentNullException(nameof(targetType));
        }

        if (value == null)
        {
            result = null;
            return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;
        }

        if (targetType.IsInstanceOfType(value))
        {
            result = value;
            return true;
        }

        var effective = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effective != targetType && effective.IsInstanceOfType(value))
        {
            result = value;
            return true;
        }

        if (effective.IsEnum)
        {
            return TryCoerceEnum(value, effective, out result);
        }

        // Element-wise list conversion (e.g. List<object> of boxed doubles -> IList<double>).
        var elementType = GetListElementType(effective);
        if (elementType != null && value is IList sourceList && !(value is string))
        {
            return TryCoerceList(sourceList, effective, elementType, out result);
        }

        // Numeric widening/narrowing, string parsing, bool conversion — all via
        // IConvertible with the invariant culture.
        if (value is IConvertible && IsConvertibleTarget(effective))
        {
            try
            {
                result = Convert.ChangeType(value, effective, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException)
            {
                result = null;
                return false;
            }
        }

        result = null;
        return false;
    }

    /// <summary>
    /// Coerces <paramref name="value"/> to <paramref name="targetType"/> or throws.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="targetType">The requested CLR type.</param>
    /// <exception cref="InvalidCastException">The conversion is not possible.</exception>
    public static object? Coerce(object? value, Type targetType)
    {
        if (TryCoerce(value, targetType, out var result))
        {
            return result;
        }

        throw new InvalidCastException(
            "Cannot convert value of type '" + (value?.GetType().FullName ?? "null") +
            "' to '" + targetType.FullName + "'.");
    }

    /// <summary>
    /// Loose compile-time compatibility check used when connecting ports. Errs on
    /// the permissive side: replication and runtime coercion handle the details;
    /// this only rejects connections that can never work.
    /// </summary>
    /// <param name="sourceType">Declared type of the output port.</param>
    /// <param name="targetType">Declared type of the input port.</param>
    public static bool CanConvert(Type sourceType, Type targetType)
    {
        if (sourceType == null || targetType == null)
        {
            return false;
        }

        var source = Nullable.GetUnderlyingType(sourceType) ?? sourceType;
        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (source == typeof(object) || target == typeof(object))
        {
            return true;
        }

        if (target.IsAssignableFrom(source) || source.IsAssignableFrom(target))
        {
            return true;
        }

        // Lists on either side: replication (list into scalar port) or element
        // coercion (list into differently-typed list port) may still succeed.
        if (IsListType(source) || IsListType(target))
        {
            return true;
        }

        // Scalar-to-scalar: anything IConvertible-friendly can be attempted.
        if (IsConvertibleTarget(source) && IsConvertibleTarget(target))
        {
            return true;
        }

        if (source.IsEnum || target.IsEnum)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Formats a value for display (Watch node, tooltips): invariant culture,
    /// nested lists as "[a, b, [c]]", dictionaries as "{key : value}", null as "null".
    /// </summary>
    /// <param name="value">The value to format.</param>
    public static string FormatValue(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is string s)
        {
            return s;
        }

        if (value is IDictionary dictionary)
        {
            var pairs = new List<string>();
            foreach (DictionaryEntry entry in dictionary)
            {
                pairs.Add(FormatValue(entry.Key) + " : " + FormatValue(entry.Value));
            }

            return "{" + string.Join(", ", pairs) + "}";
        }

        if (value is IEnumerable enumerable && !(value is string))
        {
            var items = new List<string>();
            foreach (var item in enumerable)
            {
                items.Add(FormatValue(item));
            }

            return "[" + string.Join(", ", items) + "]";
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return value.ToString() ?? string.Empty;
    }

    /// <summary>True for types the engine treats as lists (rank ≥ 1). Strings and dictionaries are scalars.</summary>
    /// <param name="type">The declared type to classify.</param>
    public static bool IsListType(Type type)
    {
        if (type == typeof(string) || IsDictionaryType(type))
        {
            return false;
        }

        return type.IsArray
            || typeof(IEnumerable).IsAssignableFrom(type)
            || GetListElementType(type) != null;
    }

    /// <summary>
    /// Returns the element type of a list-like declared type
    /// (arrays, <see cref="IEnumerable{T}"/> and friends), or null for scalars.
    /// </summary>
    /// <param name="type">The declared type to inspect.</param>
    public static Type? GetListElementType(Type type)
    {
        if (type == typeof(string) || IsDictionaryType(type))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(IEnumerable<>) || definition == typeof(ICollection<>) ||
                definition == typeof(IList<>) || definition == typeof(List<>) ||
                definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        // A concrete type implementing IEnumerable<T> exactly once.
        var enumerableInterface = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .ToList();
        if (enumerableInterface.Count == 1)
        {
            return enumerableInterface[0].GetGenericArguments()[0];
        }

        if (type == typeof(IList) || type == typeof(ICollection) || type == typeof(IEnumerable))
        {
            return typeof(object);
        }

        return null;
    }

    /// <summary>
    /// Materializes any enumerable into an <see cref="IList"/> so cached node
    /// outputs are stable and indexable (lazy sequences are enumerated once).
    /// Materialization is recursive: lazy sequences nested inside lists (e.g. a
    /// LINQ iterator returned as an element of another sequence) are converted
    /// too, so replication and coercion always see proper lists at every depth.
    /// Scalars, strings and dictionaries pass through unchanged, and lists whose
    /// elements need no conversion are returned as-is (never copied or mutated).
    /// </summary>
    /// <param name="value">A node output value.</param>
    public static object? MaterializeLists(object? value)
    {
        if (value == null || value is string || value is IDictionary)
        {
            return value;
        }

        if (value is IList list)
        {
            // Rebuild only when a nested lazy sequence is actually found, so
            // already-materialized (including typed) lists pass through untouched.
            List<object?>? rebuilt = null;
            for (int i = 0; i < list.Count; i++)
            {
                var element = list[i];
                var materialized = MaterializeLists(element);
                if (rebuilt == null && !ReferenceEquals(materialized, element))
                {
                    rebuilt = new List<object?>(list.Count);
                    for (int j = 0; j < i; j++)
                    {
                        rebuilt.Add(list[j]);
                    }
                }

                rebuilt?.Add(materialized);
            }

            return rebuilt ?? value;
        }

        if (value is IEnumerable enumerable)
        {
            var result = new List<object?>();
            foreach (var item in enumerable)
            {
                result.Add(MaterializeLists(item));
            }

            return result;
        }

        return value;
    }

    private static bool TryCoerceEnum(object value, Type enumType, out object? result)
    {
        try
        {
            if (value is string text)
            {
                result = Enum.Parse(enumType, text, ignoreCase: true);
                return true;
            }

            if (value is IConvertible)
            {
                var underlying = Convert.ChangeType(value, Enum.GetUnderlyingType(enumType), CultureInfo.InvariantCulture);
                result = Enum.ToObject(enumType, underlying);
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidCastException || ex is FormatException || ex is OverflowException)
        {
            // fall through to failure
        }

        result = null;
        return false;
    }

    private static bool TryCoerceList(IList source, Type targetType, Type elementType, out object? result)
    {
        var listType = typeof(List<>).MakeGenericType(elementType);
        var typed = (IList)Activator.CreateInstance(listType)!;
        foreach (var item in source)
        {
            if (!TryCoerce(item, elementType, out var element))
            {
                result = null;
                return false;
            }

            typed.Add(element);
        }

        if (targetType.IsArray)
        {
            var array = Array.CreateInstance(elementType, typed.Count);
            typed.CopyTo(array, 0);
            result = array;
            return targetType.IsInstanceOfType(array);
        }

        if (targetType.IsInstanceOfType(typed))
        {
            result = typed;
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>
    /// True for dictionary-shaped types, which the engine treats as scalars.
    /// Covers non-generic <see cref="IDictionary"/> implementers as well as the
    /// generic <see cref="IDictionary{TKey,TValue}"/> / <see cref="IReadOnlyDictionary{TKey,TValue}"/>
    /// interfaces (which do not inherit the non-generic one).
    /// </summary>
    private static bool IsDictionaryType(Type type)
    {
        if (typeof(IDictionary).IsAssignableFrom(type) || IsGenericDictionaryInterface(type))
        {
            return true;
        }

        return type.GetInterfaces().Any(IsGenericDictionaryInterface);
    }

    private static bool IsGenericDictionaryInterface(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>);
    }

    private static bool IsConvertibleTarget(Type type)
    {
        return type.IsPrimitive
            || type == typeof(decimal)
            || type == typeof(string)
            || type == typeof(DateTime);
    }
}
