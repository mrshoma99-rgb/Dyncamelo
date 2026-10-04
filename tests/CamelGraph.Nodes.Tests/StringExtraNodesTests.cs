using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using Xunit;

namespace CamelGraph.Nodes.Tests;

public class StringExtraNodesTests
{
    private static List<object?> Values(params object?[] items) => new List<object?>(items);

    // Runs the action with the given current culture (when the machine has it), so a test can prove that a node
    // ignores the culture of the computer it runs on.
    private static void WithCulture(string name, Action action)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
            }
            catch (CultureNotFoundException)
            {
                // Globalization-invariant machine: the test still checks the invariant result.
            }

            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ── String.Format ───────────────────────────────────────────────────────

    [Fact]
    public void Format_FillsNumberedPlaceholders()
    {
        Assert.Equal("Wall is 3.46 m high", StringExtraNodes.Format("{0} is {1:0.00} m high", Values("Wall", 3.456)));
    }

    [Fact]
    public void Format_ValuesCanRepeatAndBeAligned()
    {
        Assert.Equal("[   ab] ab ab", StringExtraNodes.Format("[{0,5}] {0} {0}", Values("ab")));
    }

    [Fact]
    public void Format_DoublesBraces()
    {
        Assert.Equal("{5}", StringExtraNodes.Format("{{{0}}}", Values(5)));
        Assert.Equal("no placeholders", StringExtraNodes.Format("no placeholders", Values()));
    }

    [Fact]
    public void Format_IsInvariantCulture()
    {
        WithCulture("de-DE", () =>
        {
            Assert.Equal("1.50 and 1234.5", StringExtraNodes.Format("{0:0.00} and {1}", Values(1.5, 1234.5)));
            Assert.Equal("2026-07-10", StringExtraNodes.Format("{0:yyyy-MM-dd}", Values(new DateTime(2026, 7, 10))));
        });
    }

    [Fact]
    public void Format_NonFormattableValuesAreShownLikeFromObject_AndAGapIsEmptyText()
    {
        // Audit ENG-11: a missing value is empty text (as in String.Concat), no longer the word "null".
        Assert.Equal("|True|[1, 2]", StringExtraNodes.Format("{0}|{1}|{2}", Values(null, true, new List<object?> { 1, 2 })));
    }

    [Fact]
    public void Format_MissingValue_NamesThePlaceholder()
    {
        var ex = Assert.Throws<ArgumentException>(() => StringExtraNodes.Format("{0} and {2}", Values("a", "b")));
        Assert.Contains("{2}", ex.Message);
        Assert.Contains("2 value(s)", ex.Message);
        Assert.Contains("{0} to {1}", ex.Message);
    }

    [Fact]
    public void Format_NoValuesWired_SaysSo()
    {
        var ex = Assert.Throws<ArgumentException>(() => StringExtraNodes.Format("{0}", Values()));
        Assert.Contains("{0}", ex.Message);
        Assert.Contains("no values are wired", ex.Message);
    }

    [Fact]
    public void Format_EscapedBraceIsNotAPlaceholder()
    {
        Assert.Equal("{3}", StringExtraNodes.Format("{{3}}", Values()));
    }

    [Fact]
    public void Format_NamedPlaceholder_PointsToTemplate()
    {
        var ex = Assert.Throws<FormatException>(() => StringExtraNodes.Format("Hello {name}", Values("x")));
        Assert.Contains("String.Format", ex.Message);
        Assert.Contains("String.Template", ex.Message);
    }

    [Fact]
    public void Format_NullInputs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.Format(null!, Values()));
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.Format("{0}", null!));
    }

    // ── String.Template ─────────────────────────────────────────────────────

    private static Dictionary<string, object?> Level() => new Dictionary<string, object?> { ["level"] = "L2", ["count"] = 12 };

    [Fact]
    public void Template_ReplacesNamedPlaceholders()
    {
        Assert.Equal("Level L2: 12 clashes", StringExtraNodes.Template("Level {level}: {count} clashes", Level()));
    }

    [Fact]
    public void Template_ValuesAreInvariantText()
    {
        var dictionary = new Dictionary<string, object?> { ["x"] = 1.5, ["flag"] = true, ["nothing"] = null };
        WithCulture("de-DE", () => Assert.Equal("1.5 True null", StringExtraNodes.Template("{x} {flag} {nothing}", dictionary)));
    }

    [Fact]
    public void Template_DoubledBracesAreLiteral()
    {
        Assert.Equal("{level} = L2", StringExtraNodes.Template("{{level}} = {level}", Level()));
        Assert.Equal("a}b", StringExtraNodes.Template("a}}b", Level()));
    }

    [Fact]
    public void Template_NamesAreTrimmedAndCaseSensitive()
    {
        Assert.Equal("L2", StringExtraNodes.Template("{ level }", Level()));
        Assert.Equal("{Level}", StringExtraNodes.Template("{Level}", Level()));
    }

    [Fact]
    public void Template_OnMissing_KeepLeavesPlaceholder()
    {
        Assert.Equal("Hi {name}!", StringExtraNodes.Template("Hi {name}!", new Dictionary<string, object?>()));
        Assert.Equal("Hi {name}!", StringExtraNodes.Template("Hi {name}!", new Dictionary<string, object?>(), "keep"));
        Assert.Equal("Hi {name}!", StringExtraNodes.Template("Hi {name}!", new Dictionary<string, object?>(), null!));
    }

    [Fact]
    public void Template_OnMissing_EmptyDropsPlaceholder()
    {
        Assert.Equal("Hi !", StringExtraNodes.Template("Hi {name}!", new Dictionary<string, object?>(), "empty"));
    }

    [Fact]
    public void Template_OnMissing_ErrorNamesTheKeyAndTheAvailableOnes()
    {
        var ex = Assert.Throws<ArgumentException>(() => StringExtraNodes.Template("{level} {floor}", Level(), "ERROR"));
        Assert.Contains("'floor'", ex.Message);
        Assert.Contains("level", ex.Message);
        Assert.Contains("count", ex.Message);

        var empty = Assert.Throws<ArgumentException>(() => StringExtraNodes.Template("{a}", new Dictionary<string, object?>(), "error"));
        Assert.Contains("dictionary is empty", empty.Message);
    }

    [Fact]
    public void Template_UnknownOnMissing_ListsTheChoices()
    {
        var ex = Assert.Throws<ArgumentException>(() => StringExtraNodes.Template("{a}", Level(), "ignore"));
        Assert.Contains("keep", ex.Message);
        Assert.Contains("empty", ex.Message);
        Assert.Contains("error", ex.Message);
    }

    [Fact]
    public void Template_UnclosedBrace_ReportsPosition()
    {
        var ex = Assert.Throws<FormatException>(() => StringExtraNodes.Template("abc {level", Level()));
        Assert.Contains("character 5", ex.Message);
    }

    [Fact]
    public void Template_NonStringKeysAreMatchedByTheirText()
    {
        var dictionary = new Dictionary<int, string> { [7] = "seven" };
        Assert.Equal("seven", StringExtraNodes.Template("{7}", dictionary));
    }

    [Fact]
    public void Template_NullInputs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.Template(null!, Level()));
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.Template("x", null!));
    }

    // ── String.FromNumber ───────────────────────────────────────────────────────

    [Fact]
    public void FromNumber_DefaultsToTwoDecimals()
    {
        Assert.Equal("1234.50", StringExtraNodes.FromNumber(1234.5));
        Assert.Equal("3", StringExtraNodes.FromNumber(2.6, 0));
        Assert.Equal("0.500000000000000", StringExtraNodes.FromNumber(0.5, 15));
    }

    [Fact]
    public void FromNumber_ThousandsSeparator()
    {
        Assert.Equal("1,234,567.9", StringExtraNodes.FromNumber(1234567.891, 1, true));
        Assert.Equal("999.00", StringExtraNodes.FromNumber(999, 2, true));
    }

    [Fact]
    public void FromNumber_PrefixGoesBeforeTheSign()
    {
        Assert.Equal("EUR -1,234.50", StringExtraNodes.FromNumber(-1234.5, 2, true, "EUR "));
        Assert.Equal("12.3 m2", StringExtraNodes.FromNumber(12.345, 1, false, "", " m2"));
        Assert.Equal("(5.00)", StringExtraNodes.FromNumber(5, 2, false, "(", ")"));
        Assert.Equal("5.00", StringExtraNodes.FromNumber(5, 2, false, null!, null!));
    }

    [Fact]
    public void FromNumber_IsInvariantCulture()
    {
        WithCulture("de-DE", () => Assert.Equal("1,234.50", StringExtraNodes.FromNumber(1234.5, 2, true)));
        WithCulture("fr-FR", () => Assert.Equal("1234.50", StringExtraNodes.FromNumber(1234.5)));
    }

    [Fact]
    public void FromNumber_NegativeZeroShowsAsZero()
    {
        Assert.Equal("0.00", StringExtraNodes.FromNumber(-0.001));
        Assert.Equal("0", StringExtraNodes.FromNumber(-0.0, 0));
        Assert.Equal("-0.01", StringExtraNodes.FromNumber(-0.01));
    }

    [Fact]
    public void FromNumber_NotANumber()
    {
        Assert.Equal("NaN", StringExtraNodes.FromNumber(double.NaN));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void FromNumber_DecimalsOutOfRange_Throws(int decimals)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => StringExtraNodes.FromNumber(1, decimals));
        Assert.Contains("between 0 and 15", ex.Message);
    }

    // ── Regular expressions ─────────────────────────────────────────────────

    [Fact]
    public void RegexIsMatch_MatchesAnywhereUnlessAnchored()
    {
        Assert.True(StringExtraNodes.RegexIsMatch("Level 12", @"\d+"));
        Assert.False(StringExtraNodes.RegexIsMatch("Level", @"\d+"));
        Assert.False(StringExtraNodes.RegexIsMatch("Level 12", @"^\d+$"));
        Assert.True(StringExtraNodes.RegexIsMatch("12", @"^\d+$"));
    }

    [Fact]
    public void RegexIsMatch_IgnoreCase()
    {
        Assert.False(StringExtraNodes.RegexIsMatch("DUCT", "duct"));
        Assert.True(StringExtraNodes.RegexIsMatch("DUCT", "duct", true));
    }

    [Fact]
    public void RegexIsMatch_InvalidPattern_NamesPatternAndNode()
    {
        var ex = Assert.Throws<ArgumentException>(() => StringExtraNodes.RegexIsMatch("x", "("));
        Assert.StartsWith("String.RegexIsMatch: the pattern '(' is not valid: ", ex.Message);
    }

    [Fact]
    public void RegexIsMatch_NullInputs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.RegexIsMatch(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.RegexIsMatch("a", null!));
    }

    [Fact]
    public void RegexIsMatch_CatastrophicPattern_IsStoppedWithAClearMessage()
    {
        var text = new string('a', 60) + "!";
        var ex = Assert.Throws<InvalidOperationException>(() => StringExtraNodes.RegexIsMatch(text, @"^(a+)+$"));
        Assert.Contains("2 seconds", ex.Message);
        Assert.Contains("^(a+)+$", ex.Message);
    }

    [Fact]
    public void RegexMatch_ReturnsMatchAndGroupsInOrder()
    {
        var result = StringExtraNodes.RegexMatch("Ref ABC-123-x", @"([A-Z]+)-(\d+)");

        Assert.Equal(new[] { "found", "match", "groups" }, result.Keys);
        Assert.Equal(true, result["found"]);
        Assert.Equal("ABC-123", result["match"]);
        Assert.Equal(new[] { "ABC", "123" }, Assert.IsAssignableFrom<IList<string>>(result["groups"]));
    }

    [Fact]
    public void RegexMatch_NoMatch_IsFalseWithEmptyOutputs()
    {
        var result = StringExtraNodes.RegexMatch("none", @"(\d+)");

        Assert.Equal(false, result["found"]);
        Assert.Equal(string.Empty, result["match"]);
        Assert.Empty(Assert.IsAssignableFrom<IList<string>>(result["groups"]));
    }

    [Fact]
    public void RegexMatch_UnmatchedOptionalGroupIsEmpty()
    {
        var result = StringExtraNodes.RegexMatch("a", "(a)(b)?(c)?");
        Assert.Equal(new[] { "a", "", "" }, Assert.IsAssignableFrom<IList<string>>(result["groups"]));
    }

    [Fact]
    public void RegexMatch_PatternWithoutGroups_HasNoGroups()
    {
        var result = StringExtraNodes.RegexMatch("abc", "b");
        Assert.Equal(true, result["found"]);
        Assert.Empty(Assert.IsAssignableFrom<IList<string>>(result["groups"]));
    }

    [Fact]
    public void RegexMatch_ReturnsOnlyTheFirstMatch_AndHonoursIgnoreCase()
    {
        Assert.Equal("1", StringExtraNodes.RegexMatch("1 22 333", @"\d+")["match"]);
        Assert.Equal(false, StringExtraNodes.RegexMatch("ABC", "abc")["found"]);
        Assert.Equal(true, StringExtraNodes.RegexMatch("ABC", "abc", true)["found"]);
    }

    [Fact]
    public void RegexMatch_InvalidPattern_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => StringExtraNodes.RegexMatch("x", "[a"));
        Assert.StartsWith("String.RegexMatch: the pattern '[a' is not valid: ", ex.Message);
    }

    [Fact]
    public void RegexMatches_ReturnsEveryMatch()
    {
        Assert.Equal(new[] { "1", "22", "333" }, StringExtraNodes.RegexMatches("a1b22c333", @"\d+"));
        Assert.Empty(StringExtraNodes.RegexMatches("abc", @"\d+"));
        Assert.Equal(new[] { "A", "a" }, StringExtraNodes.RegexMatches("AbCa", "a", true));
    }

    [Fact]
    public void RegexMatches_InvalidPattern_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => StringExtraNodes.RegexMatches("x", "*"));
        Assert.Contains("String.RegexMatches: the pattern '*' is not valid", ex.Message);
    }

    [Fact]
    public void RegexReplace_SupportsGroupReferences()
    {
        Assert.Equal("ab-12", StringExtraNodes.RegexReplace("12-ab", @"(\d+)-(\w+)", "$2-$1"));
        Assert.Equal("a_b_c", StringExtraNodes.RegexReplace("a b  c", @"\s+", "_"));
    }

    [Fact]
    public void RegexReplace_NullReplacementRemovesMatches_AndIgnoreCaseWorks()
    {
        Assert.Equal("ac", StringExtraNodes.RegexReplace("abc", "b", null!));
        Assert.Equal("-x-", StringExtraNodes.RegexReplace("AxA", "a", "-", true));
        Assert.Equal("AxA", StringExtraNodes.RegexReplace("AxA", "a", "-"));
    }

    [Fact]
    public void RegexReplace_InvalidPattern_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => StringExtraNodes.RegexReplace("x", "(", "y"));
        Assert.StartsWith("String.RegexReplace: the pattern '(' is not valid", ex.Message);
    }

    [Fact]
    public void RegexSplit_SplitsOnTheMatches()
    {
        Assert.Equal(new[] { "a", "b", "c", "d" }, StringExtraNodes.RegexSplit("a, b;c ,d", @"\s*[,;]\s*"));
        Assert.Equal(new[] { "abc" }, StringExtraNodes.RegexSplit("abc", @"\d"));
    }

    [Fact]
    public void RegexSplit_KeepsCapturedSeparators_ButNotNonCapturingOnes()
    {
        Assert.Equal(new[] { "a", ",", "b" }, StringExtraNodes.RegexSplit("a,b", "(,)"));
        Assert.Equal(new[] { "a", "b" }, StringExtraNodes.RegexSplit("a,b", "(?:,)"));
    }

    [Fact]
    public void RegexSplit_InvalidPattern_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => StringExtraNodes.RegexSplit("x", "(?<"));
        Assert.StartsWith("String.RegexSplit: the pattern '(?<' is not valid", ex.Message);
    }

    // ── Padding ─────────────────────────────────────────────────────────────

    [Fact]
    public void PadLeft_PadsToWidth()
    {
        Assert.Equal("007", StringExtraNodes.PadLeft("7", 3, "0"));
        Assert.Equal("  ab", StringExtraNodes.PadLeft("ab", 4));
        Assert.Equal("abcdef", StringExtraNodes.PadLeft("abcdef", 3, "0"));
        Assert.Equal("ab", StringExtraNodes.PadLeft("ab", 0));
    }

    [Fact]
    public void PadRight_PadsToWidth()
    {
        Assert.Equal("ab..", StringExtraNodes.PadRight("ab", 4, "."));
        Assert.Equal("ab  ", StringExtraNodes.PadRight("ab", 4));
        Assert.Equal("abcdef", StringExtraNodes.PadRight("abcdef", 3, "."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData(null)]
    public void Pad_PadCharMustBeOneCharacter(string? padChar)
    {
        var left = Assert.Throws<ArgumentException>(() => StringExtraNodes.PadLeft("x", 3, padChar!));
        Assert.Contains("String.PadLeft: padChar must be exactly one character", left.Message);
        var right = Assert.Throws<ArgumentException>(() => StringExtraNodes.PadRight("x", 3, padChar!));
        Assert.Contains("String.PadRight: padChar must be exactly one character", right.Message);
    }

    [Fact]
    public void Pad_WidthIsChecked_AndNullTextRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StringExtraNodes.PadLeft("x", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => StringExtraNodes.PadRight("x", 1000001));
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.PadLeft(null!, 3));
    }

    // ── IndexOf / LastIndexOf ───────────────────────────────────────────────

    [Fact]
    public void IndexOf_FindsFirstOccurrence()
    {
        Assert.Equal(4, StringExtraNodes.IndexOf("Hello World", "o"));
        Assert.Equal(7, StringExtraNodes.IndexOf("Hello World", "o", false, 5));
        Assert.Equal(-1, StringExtraNodes.IndexOf("Hello", "z"));
        Assert.Equal(-1, StringExtraNodes.IndexOf("Hello", "H", false, 1));
    }

    [Fact]
    public void IndexOf_IgnoresCaseByDefault_AndIsOrdinalWhenSwitchedOff()
    {
        Assert.Equal(2, StringExtraNodes.IndexOf("HELLO", "l"));
        Assert.Equal(2, StringExtraNodes.IndexOf("HELLO", "l", true));
        Assert.Equal(-1, StringExtraNodes.IndexOf("HELLO", "l", false));
    }

    [Fact]
    public void IndexOf_EmptySearchIsFoundAtTheStartIndex()
    {
        Assert.Equal(0, StringExtraNodes.IndexOf("abc", ""));
        Assert.Equal(2, StringExtraNodes.IndexOf("abc", "", false, 2));
        Assert.Equal(3, StringExtraNodes.IndexOf("abc", "", false, 3));
        Assert.Equal(0, StringExtraNodes.IndexOf("", ""));
    }

    [Fact]
    public void IndexOf_StartIndexOutOfRange_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => StringExtraNodes.IndexOf("abc", "a", false, 4));
        Assert.Contains("start index 4", ex.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => StringExtraNodes.IndexOf("abc", "a", false, -1));
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.IndexOf(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.IndexOf("a", null!));
    }

    [Fact]
    public void LastIndexOf_FindsLastOccurrence()
    {
        Assert.Equal(3, StringExtraNodes.LastIndexOf("a.b.c", "."));
        Assert.Equal(-1, StringExtraNodes.LastIndexOf("abc", "."));
        Assert.Equal(4, StringExtraNodes.LastIndexOf("a.b.C", "c", true));
        Assert.Equal(4, StringExtraNodes.LastIndexOf("a.b.C", "c"));
        Assert.Equal(-1, StringExtraNodes.LastIndexOf("a.b.C", "c", false));
    }

    [Fact]
    public void LastIndexOf_EmptySearchIsFoundAtTheEnd_AndNullsThrow()
    {
        Assert.Equal(3, StringExtraNodes.LastIndexOf("abc", ""));
        Assert.Equal(0, StringExtraNodes.LastIndexOf("", ""));
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.LastIndexOf(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.LastIndexOf("a", null!));
    }

    // ── Lines ───────────────────────────────────────────────────────────────

    [Fact]
    public void Lines_SplitsOnAllLineBreakStyles()
    {
        Assert.Equal(new[] { "a", "b", "c", "d" }, StringExtraNodes.Lines("a\r\nb\nc\rd"));
    }

    [Fact]
    public void Lines_CrLfIsOneBreak_AndEmptyLinesAreKept()
    {
        Assert.Equal(new[] { "a", "", "b" }, StringExtraNodes.Lines("a\n\nb"));
        Assert.Equal(new[] { "a", "", "b" }, StringExtraNodes.Lines("a\r\n\r\nb"));
    }

    [Fact]
    public void Lines_RemoveEmptyDropsOnlyZeroLengthLines()
    {
        Assert.Equal(new[] { "a", "b" }, StringExtraNodes.Lines("a\n\nb\n", true));
        Assert.Equal(new[] { "a", "  ", "b" }, StringExtraNodes.Lines("a\n  \nb", true));
    }

    [Fact]
    public void Lines_FinalLineBreakAddsNoEmptyLine_AndEmptyTextHasNoLines()
    {
        Assert.Equal(new[] { "a", "b" }, StringExtraNodes.Lines("a\nb\n"));
        Assert.Empty(StringExtraNodes.Lines(""));
        Assert.Equal(new[] { "" }, StringExtraNodes.Lines("\n"));
        Assert.Empty(StringExtraNodes.Lines("\n", true));
        Assert.Equal(new[] { "single" }, StringExtraNodes.Lines("single"));
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.Lines(null!));
    }

    // ── Trim ────────────────────────────────────────────────────────────────

    [Fact]
    public void TrimStartAndEnd_DefaultToWhitespace()
    {
        Assert.Equal("ab  ", StringExtraNodes.TrimStart("  ab  "));
        Assert.Equal("  ab", StringExtraNodes.TrimEnd("  ab  "));
        Assert.Equal("ab  ", StringExtraNodes.TrimStart("\t ab  ", null!));
        Assert.Equal("", StringExtraNodes.TrimEnd("   "));
    }

    [Fact]
    public void TrimStartAndEnd_TakeACharacterSet()
    {
        Assert.Equal("ab-", StringExtraNodes.TrimStart("--xab-", "x-"));
        Assert.Equal("--xab", StringExtraNodes.TrimEnd("--xab-x", "x-"));
        Assert.Equal("  ab", StringExtraNodes.TrimStart("  ab", "z"));
    }

    [Fact]
    public void TrimStartAndEnd_NullTextThrows()
    {
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.TrimStart(null!));
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.TrimEnd(null!));
    }

    // ── Title case ──────────────────────────────────────────────────────────

    [Fact]
    public void ToTitleCase_CapitalisesEachWordAndLowercasesTheRest()
    {
        Assert.Equal("Bim Coordination", StringExtraNodes.ToTitleCase("bim COORDINATION"));
        Assert.Equal("Hello World", StringExtraNodes.ToTitleCase("hello world"));
        Assert.Equal("", StringExtraNodes.ToTitleCase(""));
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.ToTitleCase(null!));
    }

    [Fact]
    public void ToTitleCase_IgnoresTheComputersCulture()
    {
        // In Turkish, a naive upper/lower-casing turns "i" into a dotted capital.
        WithCulture("tr-TR", () => Assert.Equal("Istanbul Is Big", StringExtraNodes.ToTitleCase("ISTANBUL is BIG")));
    }

    // ── Repeat / Reverse ────────────────────────────────────────────────────

    [Fact]
    public void Repeat_RepeatsWithOptionalSeparator()
    {
        Assert.Equal("ababab", StringExtraNodes.Repeat("ab", 3));
        Assert.Equal("ab-ab-ab", StringExtraNodes.Repeat("ab", 3, "-"));
        Assert.Equal("ab", StringExtraNodes.Repeat("ab", 1, "-"));
        Assert.Equal("", StringExtraNodes.Repeat("ab", 0, "-"));
        Assert.Equal("a", StringExtraNodes.Repeat("a", 1, null!));
    }

    [Fact]
    public void Repeat_CountOutOfRange_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => StringExtraNodes.Repeat("a", -1));
        Assert.Contains("between 0 and 10000", ex.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => StringExtraNodes.Repeat("a", 10001));
        Assert.Equal(10000, StringExtraNodes.Repeat("a", 10000).Length);
    }

    [Fact]
    public void Repeat_RefusesAnAbsurdlyLongResult()
    {
        var ex = Assert.Throws<ArgumentException>(() => StringExtraNodes.Repeat(new string('x', 2000), 10000));
        Assert.Contains("20000000", ex.Message);
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.Repeat(null!, 2));
    }

    [Fact]
    public void Reverse_ReversesCharacters()
    {
        Assert.Equal("cba", StringExtraNodes.Reverse("abc"));
        Assert.Equal("", StringExtraNodes.Reverse(""));
        Assert.Equal("a", StringExtraNodes.Reverse("a"));
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.Reverse(null!));
    }

    [Fact]
    public void Reverse_KeepsSurrogatePairsAndCombiningMarksTogether()
    {
        Assert.Equal("b\U0001F600a", StringExtraNodes.Reverse("a\U0001F600b"));
        Assert.Equal("xé", StringExtraNodes.Reverse("éx"));
    }

    // ── IsBlank / Left / Right ──────────────────────────────────────────────

    [Fact]
    public void IsBlank_TestsNullEmptyAndWhitespace()
    {
        Assert.True(StringExtraNodes.IsBlank(null!));
        Assert.True(StringExtraNodes.IsBlank(""));
        Assert.True(StringExtraNodes.IsBlank(" \t\r\n"));
        Assert.False(StringExtraNodes.IsBlank(" a "));
        Assert.False(StringExtraNodes.IsBlank("0"));
    }

    [Fact]
    public void Left_TakesTheFirstCharacters()
    {
        Assert.Equal("He", StringExtraNodes.Left("Hello", 2));
        Assert.Equal("", StringExtraNodes.Left("Hello", 0));
        Assert.Equal("Hello", StringExtraNodes.Left("Hello", 5));
        Assert.Equal("Hello", StringExtraNodes.Left("Hello", 99));
    }

    [Fact]
    public void Right_TakesTheLastCharacters()
    {
        Assert.Equal("lo", StringExtraNodes.Right("Hello", 2));
        Assert.Equal("", StringExtraNodes.Right("Hello", 0));
        Assert.Equal("Hello", StringExtraNodes.Right("Hello", 5));
        Assert.Equal("Hello", StringExtraNodes.Right("Hello", 99));
    }

    [Fact]
    public void LeftAndRight_RejectNegativeCountsAndNullText()
    {
        var left = Assert.Throws<ArgumentOutOfRangeException>(() => StringExtraNodes.Left("abc", -1));
        Assert.Contains("String.Left: count must be 0 or more", left.Message);
        var right = Assert.Throws<ArgumentOutOfRangeException>(() => StringExtraNodes.Right("abc", -1));
        Assert.Contains("String.Right: count must be 0 or more", right.Message);
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.Left(null!, 1));
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.Right(null!, 1));
    }

    // ── RemoveDiacritics ────────────────────────────────────────────────────

    [Fact]
    public void RemoveDiacritics_StripsAccents()
    {
        Assert.Equal("Zurich cafe", StringExtraNodes.RemoveDiacritics("Zürich café"));
        Assert.Equal("E", StringExtraNodes.RemoveDiacritics("É"));
        Assert.Equal("aouAOU n", StringExtraNodes.RemoveDiacritics("äöüÄÖÜ ñ"));
    }

    [Fact]
    public void RemoveDiacritics_HandlesAlreadyDecomposedInput()
    {
        Assert.Equal("cafe", StringExtraNodes.RemoveDiacritics("café"));
    }

    [Fact]
    public void RemoveDiacritics_LeavesPlainAndNonDecomposingText()
    {
        Assert.Equal("Plain text 123", StringExtraNodes.RemoveDiacritics("Plain text 123"));
        Assert.Equal("", StringExtraNodes.RemoveDiacritics(""));
        Assert.Equal("øłß", StringExtraNodes.RemoveDiacritics("øłß"));
        Assert.Throws<ArgumentNullException>(() => StringExtraNodes.RemoveDiacritics(null!));
    }

    // ── Roles and the engine ────────────────────────────────────────────────

    private static NodeRegistry CreateRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    private static ZeroTouchNodeModel Create(NodeRegistry registry, string nodeName)
    {
        return new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == nodeName));
    }

    private static void Wire(GraphModel graph, NodeModel from, NodeModel to, string inPort)
    {
        var target = to.InPorts.First(p => p.Name == inPort);
        Assert.True(graph.Connect(from.OutPorts[0], target).Success, "could not wire into " + to.Name + "." + inPort);
    }

    [Fact]
    public void RegisterAll_ImportsEveryStringExtraNode()
    {
        var names = CreateRegistry().Definitions.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in new[]
        {
            "String.Format", "String.Template", "String.RegexIsMatch", "String.RegexMatch", "String.RegexMatches",
            "String.RegexReplace", "String.RegexSplit", "String.PadLeft", "String.PadRight", "String.IndexOf",
            "String.LastIndexOf", "String.Lines", "String.TrimStart", "String.TrimEnd", "String.ToTitleCase",
            "String.Repeat", "String.Reverse", "String.IsBlank", "String.Left", "String.Right",
            "String.RemoveDiacritics", "String.FromNumber",
        })
        {
            Assert.Contains(name, names);
        }
    }

    [Fact]
    public void Roles_TestsAreInfo_AndTransformsAreCreate()
    {
        var definitions = CreateRegistry().Definitions.ToDictionary(d => d.Name, StringComparer.Ordinal);

        foreach (var name in new[] { "String.RegexIsMatch", "String.IndexOf", "String.LastIndexOf", "String.IsBlank" })
        {
            Assert.Equal(NodeFunction.Info, definitions[name].Function);
        }

        foreach (var name in new[] { "String.Format", "String.Template", "String.RegexReplace", "String.PadLeft", "String.Lines", "String.FromNumber" })
        {
            Assert.Equal(NodeFunction.Create, definitions[name].Function);
        }
    }

    [Fact]
    public void Definitions_ExposeChoicesRangesAndMultiInput()
    {
        var definitions = CreateRegistry().Definitions.ToDictionary(d => d.Name, StringComparer.Ordinal);

        Assert.True(definitions["String.Format"].Inputs.Single(i => i.Name == "values").MultiInput);
        Assert.Equal(new[] { "keep", "empty", "error" }, definitions["String.Template"].Inputs.Single(i => i.Name == "onMissing").Choices);
        Assert.Equal(10000d, definitions["String.Repeat"].Inputs.Single(i => i.Name == "count").Range!.Max);
        Assert.Equal(15d, definitions["String.FromNumber"].Inputs.Single(i => i.Name == "decimals").Range!.Max);
        Assert.Equal(new[] { "found", "match", "groups" }, definitions["String.RegexMatch"].Outputs.Select(o => o.Name));
    }

    [Fact]
    public void Engine_RegexMatchSplitsIntoPorts()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var text = new StringInputNode { Value = "Ref ABC-123" };
        var pattern = new StringInputNode { Value = @"([A-Z]+)-(\d+)" };
        var match = Create(registry, "String.RegexMatch");
        foreach (var node in new NodeModel[] { text, pattern, match })
        {
            graph.AddNode(node);
        }

        Wire(graph, text, match, "text");
        Wire(graph, pattern, match, "pattern");

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(new[] { "found", "match", "groups" }, match.OutPorts.Select(p => p.Name));
        Assert.Equal(true, match.OutPorts[0].Value);
        Assert.Equal("ABC-123", match.OutPorts[1].Value);
        Assert.Equal(new object?[] { "ABC", "123" }, Assert.IsAssignableFrom<IEnumerable<string>>(match.OutPorts[2].Value).Cast<object?>().ToArray());
    }

    [Fact]
    public void Engine_FormatTakesSeveralWiresInOrder()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var format = new StringInputNode { Value = "{1} / {0:0.0}" };
        var first = new NumberInputNode { Value = 2.55 };
        var second = new StringInputNode { Value = "x" };
        var node = Create(registry, "String.Format");
        foreach (var n in new NodeModel[] { format, first, second, node })
        {
            graph.AddNode(n);
        }

        Wire(graph, format, node, "format");
        Wire(graph, first, node, "values");
        Wire(graph, second, node, "values");

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal("x / 2.6", node.OutPorts[0].Value);
    }

    [Fact]
    public void Engine_ReplicatesPadLeftOverAList()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var items = new ListCreateNode();
        var a = new StringInputNode { Value = "7" };
        var b = new StringInputNode { Value = "42" };
        var width = new NumberInputNode { Value = 4 };
        var zero = new StringInputNode { Value = "0" };
        var pad = Create(registry, "String.PadLeft");
        foreach (var n in new NodeModel[] { items, a, b, width, zero, pad })
        {
            graph.AddNode(n);
        }

        items.AddItemPort();
        Assert.True(graph.Connect(a.OutPorts[0], items.InPorts[0]).Success);
        Assert.True(graph.Connect(b.OutPorts[0], items.InPorts[1]).Success);
        Wire(graph, items, pad, "text");
        Wire(graph, width, pad, "width");
        Wire(graph, zero, pad, "padChar");

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(new object?[] { "0007", "0042" }, Assert.IsAssignableFrom<IEnumerable<object?>>(pad.OutPorts[0].Value).ToArray());
    }

    [Fact]
    public void Engine_BadPatternBecomesARedNodeWithTheMessage()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var text = new StringInputNode { Value = "abc" };
        var pattern = new StringInputNode { Value = "(" };
        var isMatch = Create(registry, "String.RegexIsMatch");
        foreach (var node in new NodeModel[] { text, pattern, isMatch })
        {
            graph.AddNode(node);
        }

        Wire(graph, text, isMatch, "text");
        Wire(graph, pattern, isMatch, "pattern");

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success); // the run completes; the failure is on the node
        Assert.Equal(NodeState.Error, isMatch.State);
        Assert.Contains("the pattern '(' is not valid", isMatch.StateMessage);
    }
}
