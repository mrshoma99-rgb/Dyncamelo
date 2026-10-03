using System;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Nodes.Internal;

/// <summary>
/// One table cell read once for sorting. Table.Sort used to ask <see cref="ValueTests.Order"/> about every pair of cells and catch
/// the exceptions it throws for a pair that cannot be compared (a number against text throws twice per comparison); sorting tens
/// of thousands of rows of a mixed column took that many exceptions times the comparisons. A key remembers what the cell is
/// (empty, number, text or something else), its number, and, only when a comparison needs it, whether the text reads as a number
/// and the text the cell is shown as. <see cref="Compare"/> then gives, for every pair, exactly the answer the old comparer gave:
/// <list type="bullet">
/// <item>an empty cell (null or "") is after every other cell, in either direction, and equal to another empty cell;</item>
/// <item>two numbers (any boxed numeric type) compare by value;</item>
/// <item>a number and text compare by value when the text reads as a number (invariant culture, surrounding spaces ignored),
/// otherwise as text;</item>
/// <item>two texts compare ignoring case, as text even when both read as numbers ("10" is before "9");</item>
/// <item>everything else (booleans, dates, lists, ...): the same object is equal to itself, two values of one comparable type
/// compare by <see cref="IComparable"/>, anything else compares as the text the two cells are shown as, ignoring case.</item>
/// </list>
/// These rules do not always make a total order (10 &lt; "12", "12" &lt; "9" and "9" &lt; 10), which is why the comparisons, not
/// a re-derived order, are what is preserved.
/// </summary>
internal sealed class CellSortKey
{
    private enum Kind : byte
    {
        Empty,
        Number,
        Text,
        Other,
    }

    private readonly Kind _kind;
    private readonly object? _value;
    private readonly double _number;
    private readonly string? _text;
    private bool _readTextAsNumber;
    private bool _textIsNumber;
    private double _textNumber;
    private string? _shownAs;

    private CellSortKey(Kind kind, object? value, double number, string? text)
    {
        _kind = kind;
        _value = value;
        _number = number;
        _text = text;
    }

    /// <summary>Reads one cell.</summary>
    /// <param name="cell">The cell value.</param>
    internal static CellSortKey Of(object? cell)
    {
        if (cell == null)
        {
            return new CellSortKey(Kind.Empty, null, 0, null);
        }

        if (cell is string text)
        {
            return text.Length == 0
                ? new CellSortKey(Kind.Empty, text, 0, null)
                : new CellSortKey(Kind.Text, text, 0, text);
        }

        return ValueComparison.IsNumeric(cell)
            ? new CellSortKey(Kind.Number, cell, ValueComparison.ToDouble(cell), null)
            : new CellSortKey(Kind.Other, cell, 0, null);
    }

    /// <summary>Compares two cells the way the old Table.Sort comparer did.</summary>
    /// <param name="x">The first key.</param>
    /// <param name="y">The second key.</param>
    /// <param name="descending">True to reverse the order of cells that are not empty (empty cells stay last).</param>
    internal static int Compare(CellSortKey x, CellSortKey y, bool descending)
    {
        var xEmpty = x._kind == Kind.Empty;
        var yEmpty = y._kind == Kind.Empty;
        if (xEmpty || yEmpty)
        {
            return xEmpty == yEmpty ? 0 : xEmpty ? 1 : -1;
        }

        var order = Order(x, y);
        return descending ? -order : order;
    }

    /// <summary>An <see cref="IComparer{T}"/> of keys, for <c>OrderBy</c>.</summary>
    /// <param name="descending">True to sort the cells that are not empty largest first.</param>
    internal static IComparer<CellSortKey> Comparer(bool descending) => new KeyComparer(descending);

    private static int Order(CellSortKey x, CellSortKey y)
    {
        if (x._kind == Kind.Number)
        {
            if (y._kind == Kind.Number)
            {
                return x._number.CompareTo(y._number);
            }

            if (y._kind == Kind.Text)
            {
                return y.TextAsNumber(out var number) ? x._number.CompareTo(number) : ShownOrder(x, y);
            }
        }
        else if (x._kind == Kind.Text)
        {
            if (y._kind == Kind.Text)
            {
                return string.Compare(x._text, y._text, StringComparison.OrdinalIgnoreCase);
            }

            if (y._kind == Kind.Number)
            {
                return x.TextAsNumber(out var number) ? number.CompareTo(y._number) : ShownOrder(x, y);
            }
        }

        // At least one of the cells is neither a number nor text: booleans, dates, lists, objects.
        if (ReferenceEquals(x._value, y._value))
        {
            return 0;
        }

        if (x._value!.GetType() == y._value!.GetType() && x._value is IComparable comparable)
        {
            try
            {
                return comparable.CompareTo(y._value);
            }
            catch (InvalidOperationException)
            {
                // A value that refuses the comparison is compared by its text, like a pair of different types.
            }
        }

        return ShownOrder(x, y);
    }

    private static int ShownOrder(CellSortKey x, CellSortKey y) =>
        string.Compare(x.ShownAs(), y.ShownAs(), StringComparison.OrdinalIgnoreCase);

    // Whether a text cell reads as a number, parsed the first time it is asked.
    private bool TextAsNumber(out double number)
    {
        if (!_readTextAsNumber)
        {
            _readTextAsNumber = true;
            _textIsNumber = double.TryParse(_text!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _textNumber);
        }

        number = _textNumber;
        return _textIsNumber;
    }

    // The text the cell is shown as everywhere else in Dyncamelo, made the first time it is asked.
    private string ShownAs() => _shownAs ?? (_shownAs = _text ?? TypeCoercion.FormatValue(_value));

    private sealed class KeyComparer : IComparer<CellSortKey>
    {
        private readonly bool _descending;

        public KeyComparer(bool descending)
        {
            _descending = descending;
        }

        public int Compare(CellSortKey? x, CellSortKey? y) => CellSortKey.Compare(x!, y!, _descending);
    }
}
