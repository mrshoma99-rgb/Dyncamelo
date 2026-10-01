using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Dyncamelo.Nodes.Internal;

/// <summary>
/// A small, safe arithmetic expression language for <c>Math.Formula</c>: numbers, the variables a–f, the constants pi and tau,
/// the operators <c>+ - * / % ^</c>, comparisons (true = 1, false = 0), <c>&amp;&amp; || !</c> and a fixed set of functions.
/// Parsed once into closures and cached; nothing is ever compiled or executed beyond these closures.
/// </summary>
internal static class FormulaParser
{
    /// <summary>The variable names, in the order of the node's inputs.</summary>
    internal static readonly string[] Variables = { "a", "b", "c", "d", "e", "f" };

    private static readonly object CacheLock = new object();
    private static readonly Dictionary<string, Func<double[], double>> Cache = new Dictionary<string, Func<double[], double>>(StringComparer.Ordinal);

    private static readonly Dictionary<string, (int Min, int Max, Func<double[], double> Call)> Functions = BuildFunctions();

    /// <summary>The function names, for error messages and the node description.</summary>
    internal static IEnumerable<string> FunctionNames => Functions.Keys;

    /// <summary>Compiles (or fetches) the expression as a function of the six variable values.</summary>
    /// <param name="expression">The expression text.</param>
    internal static Func<double[], double> Compile(string expression) => Compile(expression, Variables);

    /// <summary>
    /// Compiles (or fetches) the expression with its own variable names (for example the columns of a table): the function takes
    /// one value per name, in order. A name containing spaces or symbols is written in square brackets, <c>[Fire Rating] * 2</c>.
    /// </summary>
    /// <param name="expression">The expression text.</param>
    /// <param name="names">The variable names, matched case-insensitively.</param>
    internal static Func<double[], double> Compile(string expression, IReadOnlyList<string> names)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ArgumentException("A formula is empty; write an expression such as \"a * b + 2\".", nameof(expression));
        }

        var key = ReferenceEquals(names, Variables) ? expression : expression + "\u0001" + string.Join("\u0001", names);
        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var compiled = new Parser(expression, names).ParseAll();
        lock (CacheLock)
        {
            if (Cache.Count >= 512)
            {
                Cache.Clear();
            }

            Cache[key] = compiled;
        }

        return compiled;
    }

    private static Dictionary<string, (int, int, Func<double[], double>)> BuildFunctions()
    {
        var map = new Dictionary<string, (int, int, Func<double[], double>)>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, int min, int max, Func<double[], double> call) => map[name] = (min, max, call);

        Add("abs", 1, 1, x => Math.Abs(x[0]));
        Add("sqrt", 1, 1, x => Math.Sqrt(x[0]));
        Add("sin", 1, 1, x => Math.Sin(x[0]));
        Add("cos", 1, 1, x => Math.Cos(x[0]));
        Add("tan", 1, 1, x => Math.Tan(x[0]));
        Add("asin", 1, 1, x => Math.Asin(x[0]));
        Add("acos", 1, 1, x => Math.Acos(x[0]));
        Add("atan", 1, 1, x => Math.Atan(x[0]));
        Add("atan2", 2, 2, x => Math.Atan2(x[0], x[1]));
        Add("ln", 1, 1, x => Math.Log(x[0]));
        Add("log", 1, 2, x => x.Length == 1 ? Math.Log10(x[0]) : Math.Log(x[0], x[1]));
        Add("exp", 1, 1, x => Math.Exp(x[0]));
        Add("pow", 2, 2, x => Math.Pow(x[0], x[1]));
        Add("min", 2, 16, x => { var m = x[0]; for (int i = 1; i < x.Length; i++) { m = Math.Min(m, x[i]); } return m; });
        Add("max", 2, 16, x => { var m = x[0]; for (int i = 1; i < x.Length; i++) { m = Math.Max(m, x[i]); } return m; });
        Add("round", 1, 2, x => Math.Round(x[0], x.Length == 1 ? 0 : (int)Math.Max(0, Math.Min(15, x[1])), MidpointRounding.AwayFromZero));
        Add("floor", 1, 1, x => Math.Floor(x[0]));
        Add("ceil", 1, 1, x => Math.Ceiling(x[0]));
        Add("trunc", 1, 1, x => Math.Truncate(x[0]));
        Add("sign", 1, 1, x => double.IsNaN(x[0]) ? 0 : Math.Sign(x[0]));
        Add("clamp", 3, 3, x => Math.Max(x[1], Math.Min(x[2], x[0])));
        Add("mod", 2, 2, x => x[0] % x[1]);
        Add("hypot", 2, 2, x => Math.Sqrt(x[0] * x[0] + x[1] * x[1]));
        Add("rad", 1, 1, x => x[0] * Math.PI / 180d);
        Add("deg", 1, 1, x => x[0] * 180d / Math.PI);
        Add("if", 3, 3, x => x[0] != 0d ? x[1] : x[2]);
        return map;
    }

    private enum Kind
    {
        Number,
        Name,
        Symbol,
        End,
    }

    private struct Token
    {
        public Kind Kind;
        public string Text;
        public double Number;
        public int Position;
        public bool Bracketed;
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly IReadOnlyList<string> _names;
        private readonly List<Token> _tokens = new List<Token>();
        private int _index;

        public Parser(string text, IReadOnlyList<string> names)
        {
            _text = text;
            _names = names;
            Tokenize();
        }

        public Func<double[], double> ParseAll()
        {
            var expression = ParseOr();
            if (Peek.Kind != Kind.End)
            {
                throw Error("unexpected '" + Peek.Text + "'", Peek.Position);
            }

            return expression;
        }

        private Token Peek => _tokens[_index];

        private Exception Error(string message, int position) =>
            new FormatException("Formula: " + message + " at position " + (position + 1).ToString(CultureInfo.InvariantCulture) +
                                " in \"" + _text + "\".");

        private void Tokenize()
        {
            var i = 0;
            while (i < _text.Length)
            {
                var c = _text[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (char.IsDigit(c) || (c == '.' && i + 1 < _text.Length && char.IsDigit(_text[i + 1])))
                {
                    var start = i;
                    while (i < _text.Length && (char.IsDigit(_text[i]) || _text[i] == '.'))
                    {
                        i++;
                    }

                    // An exponent only when digits follow ("2e3", "1.5E-2"); otherwise "2e" is 2 times the variable e.
                    if (i < _text.Length && (_text[i] == 'e' || _text[i] == 'E'))
                    {
                        var j = i + 1;
                        if (j < _text.Length && (_text[j] == '+' || _text[j] == '-'))
                        {
                            j++;
                        }

                        if (j < _text.Length && char.IsDigit(_text[j]))
                        {
                            while (j < _text.Length && char.IsDigit(_text[j]))
                            {
                                j++;
                            }

                            i = j;
                        }
                    }

                    var literal = _text.Substring(start, i - start);
                    if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        throw Error("'" + literal + "' is not a number", start);
                    }

                    _tokens.Add(new Token { Kind = Kind.Number, Text = literal, Number = number, Position = start });
                    continue;
                }

                if (c == '[')
                {
                    var close = _text.IndexOf(']', i + 1);
                    if (close < 0)
                    {
                        throw Error("a '[' is never closed", i);
                    }

                    _tokens.Add(new Token { Kind = Kind.Name, Text = _text.Substring(i + 1, close - i - 1).Trim(), Position = i, Bracketed = true });
                    i = close + 1;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < _text.Length && (char.IsLetterOrDigit(_text[i]) || _text[i] == '_'))
                    {
                        i++;
                    }

                    _tokens.Add(new Token { Kind = Kind.Name, Text = _text.Substring(start, i - start), Position = start });
                    continue;
                }

                var two = i + 1 < _text.Length ? _text.Substring(i, 2) : string.Empty;
                if (two == "<=" || two == ">=" || two == "==" || two == "!=" || two == "&&" || two == "||")
                {
                    _tokens.Add(new Token { Kind = Kind.Symbol, Text = two, Position = i });
                    i += 2;
                    continue;
                }

                if ("+-*/%^()<>,!=".IndexOf(c) >= 0)
                {
                    _tokens.Add(new Token { Kind = Kind.Symbol, Text = c == '=' ? "==" : c.ToString(), Position = i });
                    i++;
                    continue;
                }

                throw Error("unexpected character '" + c + "'", i);
            }

            _tokens.Add(new Token { Kind = Kind.End, Text = "end of the expression", Position = _text.Length });
        }

        private bool Accept(string symbol)
        {
            if (Peek.Kind == Kind.Symbol && Peek.Text == symbol)
            {
                _index++;
                return true;
            }

            return false;
        }

        private Func<double[], double> ParseOr()
        {
            var left = ParseAnd();
            while (Accept("||"))
            {
                var l = left;
                var r = ParseAnd();
                left = x => l(x) != 0d || r(x) != 0d ? 1d : 0d;
            }

            return left;
        }

        private Func<double[], double> ParseAnd()
        {
            var left = ParseEquality();
            while (Accept("&&"))
            {
                var l = left;
                var r = ParseEquality();
                left = x => l(x) != 0d && r(x) != 0d ? 1d : 0d;
            }

            return left;
        }

        private Func<double[], double> ParseEquality()
        {
            var left = ParseRelational();
            while (true)
            {
                if (Accept("=="))
                {
                    var l = left;
                    var r = ParseRelational();
                    left = x => l(x) == r(x) ? 1d : 0d;
                }
                else if (Accept("!="))
                {
                    var l = left;
                    var r = ParseRelational();
                    left = x => l(x) != r(x) ? 1d : 0d;
                }
                else
                {
                    return left;
                }
            }
        }

        private Func<double[], double> ParseRelational()
        {
            var left = ParseAdditive();
            while (true)
            {
                var l = left;
                if (Accept("<="))
                {
                    var r = ParseAdditive();
                    left = x => l(x) <= r(x) ? 1d : 0d;
                }
                else if (Accept(">="))
                {
                    var r = ParseAdditive();
                    left = x => l(x) >= r(x) ? 1d : 0d;
                }
                else if (Accept("<"))
                {
                    var r = ParseAdditive();
                    left = x => l(x) < r(x) ? 1d : 0d;
                }
                else if (Accept(">"))
                {
                    var r = ParseAdditive();
                    left = x => l(x) > r(x) ? 1d : 0d;
                }
                else
                {
                    return left;
                }
            }
        }

        private Func<double[], double> ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                var l = left;
                if (Accept("+"))
                {
                    var r = ParseMultiplicative();
                    left = x => l(x) + r(x);
                }
                else if (Accept("-"))
                {
                    var r = ParseMultiplicative();
                    left = x => l(x) - r(x);
                }
                else
                {
                    return left;
                }
            }
        }

        private Func<double[], double> ParseMultiplicative()
        {
            var left = ParseUnary();
            while (true)
            {
                var l = left;
                if (Accept("*"))
                {
                    var r = ParseUnary();
                    left = x => l(x) * r(x);
                }
                else if (Accept("/"))
                {
                    var r = ParseUnary();
                    left = x => l(x) / r(x);
                }
                else if (Accept("%"))
                {
                    var r = ParseUnary();
                    left = x => l(x) % r(x);
                }
                else
                {
                    return left;
                }
            }
        }

        private Func<double[], double> ParseUnary()
        {
            if (Accept("-"))
            {
                var operand = ParseUnary();
                return x => -operand(x);
            }

            if (Accept("+"))
            {
                return ParseUnary();
            }

            if (Accept("!"))
            {
                var operand = ParseUnary();
                return x => operand(x) == 0d ? 1d : 0d;
            }

            return ParsePower();
        }

        private Func<double[], double> ParsePower()
        {
            var baseValue = ParsePrimary();
            if (Accept("^"))
            {
                var exponent = ParseUnary();
                return x => Math.Pow(baseValue(x), exponent(x));
            }

            return baseValue;
        }

        private Func<double[], double> ParsePrimary()
        {
            var token = Peek;
            switch (token.Kind)
            {
                case Kind.Number:
                    _index++;
                    var value = token.Number;
                    return _ => value;

                case Kind.Name:
                    _index++;
                    return ParseName(token);

                case Kind.Symbol when token.Text == "(":
                    _index++;
                    var inner = ParseOr();
                    if (!Accept(")"))
                    {
                        throw Error("expected ')' but found " + DescribeToken(Peek), Peek.Position);
                    }

                    return inner;

                default:
                    throw Error("expected a number, a name or '(' but found " + DescribeToken(token), token.Position);
            }
        }

        private int IndexOf(string lowerName)
        {
            for (int i = 0; i < _names.Count; i++)
            {
                if (string.Equals(_names[i], lowerName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string DescribeToken(Token token) =>
            token.Kind == Kind.End ? token.Text : "'" + token.Text + "'";

        private Func<double[], double> ParseName(Token token)
        {
            if (!token.Bracketed && Peek.Kind == Kind.Symbol && Peek.Text == "(")
            {
                _index++;
                if (!Functions.TryGetValue(token.Text, out var function))
                {
                    throw Error("unknown function '" + token.Text + "' (known: " + string.Join(", ", Functions.Keys) + ")", token.Position);
                }

                var args = new List<Func<double[], double>>();
                if (!Accept(")"))
                {
                    do
                    {
                        args.Add(ParseOr());
                    }
                    while (Accept(","));

                    if (!Accept(")"))
                    {
                        throw Error("expected ')' or ',' but found " + DescribeToken(Peek), Peek.Position);
                    }
                }

                if (args.Count < function.Min || args.Count > function.Max)
                {
                    var expected = function.Min == function.Max
                        ? function.Min.ToString(CultureInfo.InvariantCulture)
                        : function.Min.ToString(CultureInfo.InvariantCulture) + "–" + function.Max.ToString(CultureInfo.InvariantCulture);
                    throw Error(token.Text + "() takes " + expected + " argument(s), not " + args.Count.ToString(CultureInfo.InvariantCulture), token.Position);
                }

                var call = function.Call;
                var captured = args.ToArray();
                return x =>
                {
                    var values = new double[captured.Length];
                    for (int i = 0; i < captured.Length; i++)
                    {
                        values[i] = captured[i](x);
                    }

                    return call(values);
                };
            }

            var name = token.Text.ToLowerInvariant();
            if (!token.Bracketed || IndexOf(name) < 0)
            {
                // A name that is also a column wins over the constants, so a column called "tau" still works.
                if (IndexOf(name) < 0)
                {
                    if (name == "pi")
                    {
                        return _ => Math.PI;
                    }

                    if (name == "tau")
                    {
                        return _ => 2d * Math.PI;
                    }

                    if (name == "true")
                    {
                        return _ => 1d;
                    }

                    if (name == "false")
                    {
                        return _ => 0d;
                    }
                }
            }

            var index = IndexOf(name);
            if (index < 0)
            {
                var known = string.Join(", ", _names.Count > 12 ? _names.Take(12).Concat(new[] { "…" }) : _names);
                throw Error("unknown name '" + token.Text + "' (use " + known + ", pi, tau, true or false)", token.Position);
            }

            return x => x[index];
        }
    }
}
