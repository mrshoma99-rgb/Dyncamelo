using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Dyncamelo.Core.Types;

/// <summary>
/// Gentle value coercion used at node-invocation time: exact match, registered
/// custom converters, numeric widening, enum conversion, <see cref="IConvertible"/>
/// fallback (invariant culture) and element-wise list conversion. Node packs may
/// contribute converters between their own boundary types via
/// <see cref="RegisterConverter"/>; those are consulted both by the runtime
/// coercion (<see cref="TryCoerce"/>) and by the connection compatibility check
/// (<see cref="CanConvert"/>).
/// </summary>
public static class TypeCoercion
{
    /// <summary>
    /// Custom converters keyed by (source, target) type pair. Registered by node
    /// packs (e.g. Dyncamelo.Nodes color → Navisworks color) via a
    /// <c>[TypeConverterRegistration]</c> hook when their assembly is loaded.
    /// </summary>
    private static readonly ConcurrentDictionary<ConverterKey, Func<object, object?>> Converters =
        new ConcurrentDictionary<ConverterKey, Func<object, object?>>();

    /// <summary>
    /// Registers (or replaces) a custom converter from <paramref name="sourceType"/>
    /// to <paramref name="targetType"/>. The converter receives a non-null instance
    /// of <paramref name="sourceType"/> (or a subtype) and returns the converted
    /// value. Registration is process-wide and idempotent per type pair.
    /// </summary>
    /// <param name="sourceType">The value type the converter accepts.</param>
    /// <param name="targetType">The type the converter produces.</param>
    /// <param name="converter">The conversion function.</param>
    public static void RegisterConverter(Type sourceType, Type targetType, Func<object, object?> converter)
    {
        if (sourceType == null)
        {
            throw new ArgumentNullException(nameof(sourceType));
        }

        if (targetType == null)
        {
            throw new ArgumentNullException(nameof(targetType));
        }

        if (converter == null)
        {
            throw new ArgumentNullException(nameof(converter));
        }

        Converters[new ConverterKey(sourceType, targetType)] = converter;
    }

    /// <summary>Removes a previously registered custom converter.</summary>
    /// <param name="sourceType">The converter's source type.</param>
    /// <param name="targetType">The converter's target type.</param>
    /// <returns>True when a converter for the exact type pair was removed.</returns>
    public static bool UnregisterConverter(Type sourceType, Type targetType)
    {
        if (sourceType == null)
        {
            throw new ArgumentNullException(nameof(sourceType));
        }

        if (targetType == null)
        {
            throw new ArgumentNullException(nameof(targetType));
        }

        return Converters.TryRemove(new ConverterKey(sourceType, targetType), out _);
    }

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

        // Custom converters take precedence over the generic fallbacks so a node
        // pack can define exactly how its boundary types translate.
        var converter = FindConverter(value.GetType(), effective);
        if (converter != null)
        {
            try
            {
                var converted = converter(value);
                if (converted == null &&
                    targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                {
                    // A converter returning null cannot satisfy a non-nullable
                    // value-type target; treat it as "not convertible" so the
                    // caller gets the descriptive type-pair error instead of a
                    // null smuggled into a value-typed input.
                    result = null;
                    return false;
                }

                result = converted;
                return true;
            }
            catch (Exception)
            {
                // TryCoerce never throws: a failing custom converter simply
                // means "not convertible" (Coerce then reports the type pair).
                result = null;
                return false;
            }
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

        // A registered custom converter can bridge otherwise-unrelated types
        // (e.g. a node-pack color type into a host API color type).
        return HasConverterFor(source, target);
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

    /// <summary>
    /// Finds a converter applicable to a runtime value of <paramref name="valueType"/>
    /// requested as <paramref name="targetType"/>: exact pair first, then any
    /// converter whose source accepts the value and whose product satisfies the target.
    /// </summary>
    private static Func<object, object?>? FindConverter(Type valueType, Type targetType)
    {
        if (Converters.IsEmpty)
        {
            return null;
        }

        if (Converters.TryGetValue(new ConverterKey(valueType, targetType), out var exact))
        {
            return exact;
        }

        foreach (var entry in Converters)
        {
            if (entry.Key.SourceType.IsAssignableFrom(valueType) &&
                targetType.IsAssignableFrom(entry.Key.TargetType))
            {
                return entry.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Loose declared-type check for <see cref="CanConvert"/>: a converter is a
    /// potential bridge when its source relates to the output port's declared type
    /// and its product relates to the input port's declared type (either
    /// assignability direction — declared types are often wider than runtime types).
    /// </summary>
    private static bool HasConverterFor(Type source, Type target)
    {
        if (Converters.IsEmpty)
        {
            return false;
        }

        foreach (var entry in Converters)
        {
            var from = entry.Key.SourceType;
            var to = entry.Key.TargetType;
            if ((from.IsAssignableFrom(source) || source.IsAssignableFrom(from)) &&
                (target.IsAssignableFrom(to) || to.IsAssignableFrom(target)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Dictionary key for the custom-converter registry: an ordered (source, target) type pair.</summary>
    private readonly struct ConverterKey : IEquatable<ConverterKey>
    {
        public ConverterKey(Type sourceType, Type targetType)
        {
            SourceType = sourceType;
            TargetType = targetType;
        }

        public Type SourceType { get; }

        public Type TargetType { get; }

        public bool Equals(ConverterKey other)
        {
            return SourceType == other.SourceType && TargetType == other.TargetType;
        }

        public override bool Equals(object? obj)
        {
            return obj is ConverterKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (SourceType.GetHashCode() * 397) ^ TargetType.GetHashCode();
            }
        }
    }
}
