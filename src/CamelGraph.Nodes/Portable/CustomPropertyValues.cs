using System;
using System.Collections;
using System.Globalization;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// What a user-defined property of a model item can hold, and the sentence that tells a person why a value cannot be stored.
/// A property holds ONE value: text, a number, true/false or a date. A list, a dictionary or an element handed to a property
/// node used to be written as the text of its .NET type ("System.Collections.Generic.List`1[System.Object]") into every item;
/// the property nodes now refuse such a value before they write anything. Pure (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class CustomPropertyValues
{
    /// <summary>
    /// True when a property can hold the value: nothing (stored as empty text), text, a character, true/false, any number, a date
    /// or time, a GUID or one name of a list of choices.
    /// </summary>
    /// <param name="value">The value a node was given for a property.</param>
    /// <returns>False for a list, a dictionary, an element or any other object.</returns>
    public static bool IsStorable(object? value)
    {
        switch (value)
        {
            case null:
            case string _:
            case char _:
            case bool _:
            case sbyte _:
            case byte _:
            case short _:
            case ushort _:
            case int _:
            case uint _:
            case long _:
            case ulong _:
            case float _:
            case double _:
            case decimal _:
            case DateTime _:
            case DateTimeOffset _:
            case TimeSpan _:
            case Guid _:
            case Enum _:
            case Uri _:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Says in words what a value that cannot be stored is ("a list of 3 values", "a dictionary of 2 entries", "a ModelItem").
    /// Never a .NET generic type name.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>A short phrase that follows "is".</returns>
    public static string Describe(object? value)
    {
        switch (value)
        {
            case null:
                return "empty";
            case string _:
                return "text";
            case IDictionary dictionary:
                return "a dictionary of " + Count(dictionary.Count, "entry", "entries");
            case IEnumerable sequence:
                var count = CountOf(sequence);
                return count < 0 ? "a list" : "a list of " + Count(count, "value", "values");
            default:
                var name = value.GetType().Name;
                return name.IndexOf('`') >= 0 ? "a collection of values" : "a " + name;
        }
    }

    /// <summary>The message for a value that a property cannot hold.</summary>
    /// <param name="nodeName">The node that was asked to write it ("Properties.SetCustom").</param>
    /// <param name="propertyName">The property it was meant for.</param>
    /// <param name="value">The value.</param>
    /// <returns>A sentence for the person at the keyboard: what is wrong and what to do.</returns>
    public static string Message(string nodeName, string propertyName, object? value)
    {
        var text = nodeName + ": the value for property '" + propertyName + "' is " + Describe(value) +
                   ", but a property holds one value: text, a number, true/false or a date. ";
        return value is IEnumerable && !(value is IDictionary)
            ? text + "To give every item its own row of values use Properties.SetCustomFromTable, or set List Levels @L2 on the 'values' input."
            : text + "Wire a single value, or convert it to text first.";
    }

    /// <summary>Throws when a property cannot hold the value; does nothing otherwise.</summary>
    /// <param name="nodeName">The node that was asked to write it ("Properties.SetCustom").</param>
    /// <param name="propertyName">The property it was meant for.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentException">The value is a list, a dictionary or another object.</exception>
    public static void Require(string nodeName, string propertyName, object? value)
    {
        if (!IsStorable(value))
        {
            throw new ArgumentException(Message(nodeName, propertyName, value), "values");
        }
    }

    private static int CountOf(IEnumerable sequence)
    {
        if (sequence is ICollection collection)
        {
            return collection.Count;
        }

        return -1;
    }

    private static string Count(int number, string one, string many) =>
        number.ToString(CultureInfo.InvariantCulture) + " " + (number == 1 ? one : many);
}
