using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using CamelGraph.Core.Types;
using CamelGraph.Nodes.Internal;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The order Table.Sort gives on columns that mix numbers, text and everything else. The expectations were written against
/// the first implementation (one comparer that asked <see cref="ValueTests.Order"/> and caught its exceptions) and pin the
/// result row for row, so the faster implementation (a sort key read once per cell) has to give exactly the same order:
/// "12" against 5 is numeric but "12" against "9" is text, an empty cell is last in either direction, equal cells keep their
/// order, and a column whose rules do not make a total order (5, "12", "9", 10) still comes out the same.
/// </summary>
public class TableSortOrderTests
{
    private static readonly string[] Headers = { "A", "B", "C", "Id" };

    private static CamelGraphTable Table(IList<object?[]> rows, int columns = 4)
    {
        var headers = Headers.Take(columns - 1).Concat(new[] { "Id" });
        return new CamelGraphTable(headers, rows.Select((cells, i) => (IReadOnlyList<object?>)cells.Concat(new object?[] { i }).ToArray()));
    }

    private static CamelGraphTable SingleColumn(params object?[] cells) => new CamelGraphTable(
        new[] { "A", "Id" },
        cells.Select((cell, i) => (IReadOnlyList<object?>)new object?[] { cell, i }));

    private static object?[] Column(CamelGraphTable table, int column) => table.Rows.Select(r => r[column]).ToArray();

    // ── What the current order is, case by case ─────────────────────────────

    [Fact]
    public void NumbersAndLetterTextOrderNumbersFirstThenTextIgnoringCase()
    {
        var table = SingleColumn(10, "b", 2, "a", 3.5, "B", 7L);

        // 2, 3.5, 7, 10 numerically; every number text ("2") sorts before a letter; "b" and "B" are equal, so they keep their order.
        Assert.Equal(new object?[] { 2, 3.5, 7L, 10, "a", "b", "B" }, Column(TableToolkitNodes.Sort(table, "A"), 0));

        // Descending reverses every comparison, but the tie of "b" and "B" is still decided by the original order.
        Assert.Equal(new object?[] { "b", "B", "a", 10, 7L, 3.5, 2 }, Column(TableToolkitNodes.Sort(table, "-A"), 0));
        Assert.Equal(new object?[] { "b", "B", "a", 10, 7L, 3.5, 2 }, Column(TableToolkitNodes.Sort(table, "A", descending: true), 0));
        Assert.Equal(new object?[] { 2, 3.5, 7L, 10, "a", "b", "B" }, Column(TableToolkitNodes.Sort(table, "A asc", descending: true), 0));
    }

    [Fact]
    public void EmptyCellsAreLastInEitherDirection_AndKeepTheirOrder()
    {
        var table = SingleColumn(null, 3, "", "x", null, 1, " ", "");

        // A blank " " is text, not empty: it sorts as the text " " (before "x", but after the numbers since "1" < " " is false: '1' 0x31 > ' ' 0x20).
        Assert.Equal(new object?[] { " ", 1, 3, "x", null, "", null, "" }, Column(TableToolkitNodes.Sort(table, "A"), 0));
        Assert.Equal(new object?[] { "x", 3, 1, " ", null, "", null, "" }, Column(TableToolkitNodes.Sort(table, "A desc"), 0));

        // The empty cells (rows 0, 2, 4 and 7) keep their order after the others, whichever way the rest is sorted.
        Assert.Equal(new object?[] { 6, 5, 1, 3, 0, 2, 4, 7 }, Column(TableToolkitNodes.Sort(table, "A"), 1));
        Assert.Equal(new object?[] { 3, 1, 5, 6, 0, 2, 4, 7 }, Column(TableToolkitNodes.Sort(table, "A desc"), 1));
    }

    [Fact]
    public void TextThatReadsAsANumberIsNumericOnlyAgainstANumber()
    {
        // 5 against "12" is numeric (5 < 12); "12" against "9" is text ("12" < "9"); "abc" against 5 is text ("5" < "abc").
        var table = SingleColumn("12", 5, "9", "abc", 40);

        // 5 < "12" < "9" < 40 < "abc": numbers and numeric text meet only as numbers, text and text only as text.
        Assert.Equal(new object?[] { 1, 0, 2, 4, 3 }, Column(TableToolkitNodes.Sort(table, "A"), 1));
        Assert.Equal(new object?[] { 3, 4, 2, 0, 1 }, Column(TableToolkitNodes.Sort(table, "-A"), 1));
    }

    [Fact]
    public void ARingOfCellsThatPreferEachOtherSortsLikeTheFirstImplementation()
    {
        // 9 < 10 and 10 < "12" are numbers, "12" < "9" is text: the rules are not a total order, so what the sort makes of such a
        // column depends on which pairs it compares and in which order. The key-based sort has to make the same comparisons.
        var arrangements = new[]
        {
            new object?[] { "12", "9", 10, 9, "x" },
            new object?[] { "x", "12", 9, "9", 10 },
            new object?[] { 10, "9", "12", 9, "x" },
            new object?[] { "9", 10, "12", "x", 9 },
            new object?[] { 10, 9, "12", "9" },
        };
        foreach (var cells in arrangements)
        {
            var table = SingleColumn(cells);
            Assert.Equal(OriginalSort(table, new[] { (0, false) }), CurrentSort(table, new[] { (0, false) }));
            Assert.Equal(OriginalSort(table, new[] { (0, true) }), CurrentSort(table, new[] { (0, true) }));
        }

        // Pinned on the current runtime: for "x", "12", 9, "9", 10 the result is not the order the rules suggest.
        Assert.Equal(new object?[] { 2, 1, 3, 4, 0 }, Column(TableToolkitNodes.Sort(SingleColumn(arrangements[1]), "A"), 1));
    }

    [Fact]
    public void NumericTextAlonePrefersTextOrder()
    {
        // No number in the column: "10" < "9" like text.
        var table = SingleColumn("9", "10", "1", "100", "2");
        Assert.Equal(new object?[] { "1", "10", "100", "2", "9" }, Column(TableToolkitNodes.Sort(table, "A"), 0));
    }

    [Fact]
    public void NumbersOfAnyBoxedTypeCompareByValue_AndNotANumberComesFirst()
    {
        var table = SingleColumn(2.5, 2, 2L, 2.0f, 2m, (byte)1, double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.0, 0.0);

        var sorted = Column(TableToolkitNodes.Sort(table, "A"), 1);
        // NaN sorts below everything, then -inf, then -0 and 0 (equal, so in their original order), then 1, then 2, 2L, 2f and 2m
        // (one value, in their original order), 2.5 and +inf.
        Assert.Equal(new object?[] { 6, 8, 9, 10, 5, 1, 2, 3, 4, 0, 7 }, sorted);
    }

    [Fact]
    public void BooleansDatesAndListsFollowTheirOwnRules()
    {
        var late = new DateTime(2025, 6, 1);
        var early = new DateTime(2024, 1, 1);
        var table = SingleColumn(true, late, false, early, "text", 5, new List<object?> { 1, 2 }, new List<object?> { 1 });

        // Two booleans or two dates compare by value (false < true, earlier < later); every other pair compares the text of the two
        // cells ignoring case: "01/01/2024 00:00:00" < "06/01/2025 00:00:00" < "5" < "False" < "text" < "True" < "[1, 2]" < "[1]".
        var order = Column(TableToolkitNodes.Sort(table, "A"), 1);
        Assert.Equal(new object?[] { 3, 1, 5, 2, 4, 0, 6, 7 }, order);
    }

    [Fact]
    public void SeveralColumnsSortStably_WithADirectionPerColumn()
    {
        var rows = new List<object?[]>
        {
            new object?[] { "L2", 5, null },
            new object?[] { "L1", "x", 1 },
            new object?[] { "L2", 5.0, 2 },
            new object?[] { "L1", 12, 1 },
            new object?[] { "l1", 12L, "1" },
            new object?[] { "L2", "x", null },
        };
        var table = Table(rows);

        // "L1" and "l1" are the same group; inside it "x" is above 12 (text), 12 and 12L are equal (so the original order decides).
        Assert.Equal(new object?[] { 1, 3, 4, 5, 0, 2 }, Column(TableToolkitNodes.Sort(table, "A, -B"), 3));
        // 5 and 5.0 tie on B, then C decides (2 before the empty cell).
        Assert.Equal(new object?[] { 3, 4, 1, 2, 0, 5 }, Column(TableToolkitNodes.Sort(table, "A, B, C"), 3));
        Assert.Equal(new object?[] { 2, 0, 5, 1, 3, 4 }, Column(TableToolkitNodes.Sort(table, "-A, C desc"), 3));
    }

    [Fact]
    public void AnEmptyOrOneRowTableSortsToItself()
    {
        Assert.Equal(0, TableToolkitNodes.Sort(SingleColumn(), "A").RowCount);
        Assert.Equal(new object?[] { "only" }, Column(TableToolkitNodes.Sort(SingleColumn("only"), "A"), 0));
    }

    // ── The first implementation as the oracle ──────────────────────────────

    // The comparer and sort exactly as they were before the sort keys.
    private sealed class OriginalCellComparer : IComparer<object?>
    {
        private readonly bool _descending;

        public OriginalCellComparer(bool descending)
        {
            _descending = descending;
        }

        public int Compare(object? x, object? y)
        {
            var xEmpty = x == null || (x is string sx && sx.Length == 0);
            var yEmpty = y == null || (y is string sy && sy.Length == 0);
            if (xEmpty || yEmpty)
            {
                return xEmpty == yEmpty ? 0 : xEmpty ? 1 : -1;
            }

            int order;
            try
            {
                order = ValueTests.Order(x, y, "Table.Sort");
            }
            catch (InvalidOperationException)
            {
                order = string.Compare(TypeCoercion.FormatValue(x), TypeCoercion.FormatValue(y), StringComparison.OrdinalIgnoreCase);
            }

            return _descending ? -order : order;
        }
    }

    private static string OriginalSort(CamelGraphTable table, IReadOnlyList<(int Index, bool Descending)> keys)
    {
        try
        {
            IOrderedEnumerable<object?[]>? ordered = null;
            foreach (var key in keys)
            {
                var comparer = new OriginalCellComparer(key.Descending);
                var index = key.Index;
                ordered = ordered == null ? table.Rows.OrderBy(r => r[index], comparer) : ordered.ThenBy(r => r[index], comparer);
            }

            return string.Join(",", ordered!.Select(r => r[table.ColumnCount - 1]));
        }
        catch (Exception ex)
        {
            return "throws " + ex.GetType().Name;
        }
    }

    private static string CurrentSort(CamelGraphTable table, IReadOnlyList<(int Index, bool Descending)> keys)
    {
        var spec = string.Join(", ", keys.Select(k => (k.Descending ? "-" : string.Empty) + table.Headers[k.Index]));
        try
        {
            return string.Join(",", TableToolkitNodes.Sort(table, spec).Rows.Select(r => r[table.ColumnCount - 1]));
        }
        catch (Exception ex)
        {
            return "throws " + ex.GetType().Name;
        }
    }

    private static readonly object SharedBoxed = new object();
    private static readonly object[] Pool =
    {
        null!, null!, "", "", " ", "a", "A", "b", "B", "abc", "ABC", "Abc", "x1", "x10", "x2", "1", "2", "9", "10", "12", " 12 ", "9.5", "1e3", "-4", "NaN", "Infinity", "-Infinity", "0x10", "٣",
        0, 1, 2, 5, 9, 10, 12, -4, 7L, 12L, 3.5, 9.5, 1000.0, 0.0, -0.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity,
        2.5f, 12m, (byte)9, (short)-4, (uint)7, true, false, true, new DateTime(2024, 1, 1), new DateTime(2025, 6, 1), Guid.Empty,
        new List<object?> { 1 }, new List<object?> { 1, 2 }, new object[0], SharedBoxed, SharedBoxed,
    };

    private static CamelGraphTable RandomTable(Random random, int rows, int columns, Func<Random, object?> cell)
    {
        var list = new List<object?[]>();
        for (int r = 0; r < rows; r++)
        {
            var cells = new object?[columns];
            for (int c = 0; c < columns; c++)
            {
                cells[c] = cell(random);
            }

            list.Add(cells);
        }

        return Table(list, columns + 1);
    }

    private static List<(int Index, bool Descending)> RandomKeys(Random random, int columns)
    {
        var keys = new List<(int, bool)>();
        var count = random.Next(1, columns + 1);
        for (int i = 0; i < count; i++)
        {
            keys.Add((random.Next(columns), random.Next(2) == 0));
        }

        return keys;
    }

    [Fact]
    public void EveryPairOfCellsComparesExactlyLikeTheFirstImplementation()
    {
        // The pairwise answer is what the sort depends on, so it is compared for every pair of the pool, both ways round, in both
        // directions, with the keys read once and used for every pair (the parse of a text and its shown text are made lazily).
        var keys = Pool.Select(CellSortKey.Of).ToList();
        foreach (var descending in new[] { false, true })
        {
            var original = new OriginalCellComparer(descending);
            var current = CellSortKey.Comparer(descending);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < Pool.Length; i++)
                {
                    for (int j = 0; j < Pool.Length; j++)
                    {
                        Assert.True(
                            original.Compare(Pool[i], Pool[j]) == current.Compare(keys[i], keys[j]),
                            "cells " + Describe(Pool[i]) + " and " + Describe(Pool[j]) + (descending ? " descending" : " ascending"));
                    }
                }
            }
        }
    }

    private static string Describe(object? cell) => cell == null ? "null" : cell.GetType().Name + " " + TypeCoercion.FormatValue(cell);

    [Fact]
    public void RandomMixedColumns_GiveTheSameRowOrderAsTheFirstImplementation()
    {
        var random = new Random(20240601);
        var compared = 0;
        for (int round = 0; round < 400; round++)
        {
            var columns = random.Next(1, 4);
            var table = RandomTable(random, random.Next(0, 70), columns, r => Pool[r.Next(Pool.Length)]);
            var keys = RandomKeys(random, columns);

            Assert.Equal(OriginalSort(table, keys), CurrentSort(table, keys));
            compared++;
        }

        Assert.Equal(400, compared);
    }

    [Fact]
    public void RandomNumbersWithNumericText_GiveTheSameRowOrderAsTheFirstImplementation()
    {
        // The numeric-text mix is where the rules are not a total order; the same comparisons must give the same result.
        var random = new Random(77);
        for (int round = 0; round < 400; round++)
        {
            var table = RandomTable(random, random.Next(2, 80), 2, r =>
            {
                var n = r.Next(0, 30);
                switch (r.Next(5))
                {
                    case 0: return (object?)n;
                    case 1: return n.ToString(CultureInfo.InvariantCulture);
                    case 2: return (n / 4.0).ToString(CultureInfo.InvariantCulture);
                    case 3: return r.Next(3) == 0 ? null : (object?)(n * 1.5);
                    default: return r.Next(2) == 0 ? "n" + n : "";
                }
            });
            var keys = RandomKeys(random, 2);

            Assert.Equal(OriginalSort(table, keys), CurrentSort(table, keys));
        }
    }

    [Fact]
    public void RandomPlainColumns_GiveTheSameRowOrderAsTheFirstImplementation()
    {
        var random = new Random(5);
        for (int round = 0; round < 200; round++)
        {
            var numeric = round % 2 == 0;
            var table = RandomTable(random, random.Next(0, 120), 2, r =>
                r.Next(10) == 0 ? null : numeric ? (object?)(r.Next(0, 20) / 2.0) : new[] { "a", "B", "b", "C", "c10", "c9" }[r.Next(6)]);
            var keys = RandomKeys(random, 2);

            Assert.Equal(OriginalSort(table, keys), CurrentSort(table, keys));
        }
    }

    // ── What the faster implementation has to add ───────────────────────────

    [Fact]
    public void SortingAMixedColumnThrowsNothingInternally()
    {
        // The first implementation threw and caught two exceptions per comparison of a number against text.
        var random = new Random(3);
        var table = RandomTable(random, 3000, 1, r => Pool[r.Next(Pool.Length)]);
        var thread = Environment.CurrentManagedThreadId;
        var thrown = 0;
        EventHandler<FirstChanceExceptionEventArgs> count = (_, __) =>
        {
            if (Environment.CurrentManagedThreadId == thread)
            {
                thrown++;
            }
        };

        AppDomain.CurrentDomain.FirstChanceException += count;
        try
        {
            TableToolkitNodes.Sort(table, "A");
            TableToolkitNodes.Sort(table, "-A");
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= count;
        }

        Assert.Equal(0, thrown);
    }
}
