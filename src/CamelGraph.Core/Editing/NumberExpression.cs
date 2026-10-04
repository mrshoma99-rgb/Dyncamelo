using System;
using System.Globalization;

namespace CamelGraph.Core.Editing;

/// <summary>
/// Tiny arithmetic evaluator for typed field input ("2*3+1", "(4+2)^2", "pi/4").
/// Recursive descent; invariant culture; supports + - * / % ^, unary minus,
/// parentheses, and the constants pi, e, tau. No reflection, no scripting.
/// </summary>
public static class NumberExpression
{
    /// <summary>Evaluates <paramref name="text"/>; false on any syntax error, overflow, NaN or division by zero.</summary>
    public static bool TryEvaluate(string? text, out double value)
    {
        value = 0d;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parser = new Parser(text!);
        try
        {
            var result = parser.ParseAll();
            if (double.IsNaN(result) || double.IsInfinity(result))
            {
                return false;
            }

            value = result;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) => _s = s;

        public double ParseAll()
        {
            var v = Expr();
            Skip();
            if (_i != _s.Length)
            {
                throw new FormatException();
            }

            return v;
        }

        private double Expr()
        {
            var v = Term();
            while (true)
            {
                Skip();
                if (Eat('+')) { v += Term(); }
                else if (Eat('-')) { v -= Term(); }
                else { return v; }
            }
        }

        private double Term()
        {
            var v = Power();
            while (true)
            {
                Skip();
                if (Eat('*')) { v *= Power(); }
                else if (Eat('/'))
                {
                    var d = Power();
                    if (d == 0d) { throw new FormatException(); }
                    v /= d;
                }
                else if (Eat('%'))
                {
                    var d = Power();
                    if (d == 0d) { throw new FormatException(); }
                    v %= d;
                }
                else { return v; }
            }
        }

        private double Power()
        {
            var b = Unary();
            Skip();
            if (Eat('^'))
            {
                var e = Power(); // right-associative
                return Math.Pow(b, e);
            }

            return b;
        }

        private double Unary()
        {
            Skip();
            if (Eat('-')) { return -Unary(); }
            if (Eat('+')) { return Unary(); }
            return Atom();
        }

        private double Atom()
        {
            Skip();
            if (Eat('('))
            {
                var v = Expr();
                Skip();
                if (!Eat(')')) { throw new FormatException(); }
                return v;
            }

            if (_i < _s.Length && char.IsLetter(_s[_i]))
            {
                var start = _i;
                while (_i < _s.Length && char.IsLetter(_s[_i])) { _i++; }
                switch (_s.Substring(start, _i - start).ToLowerInvariant())
                {
                    case "pi": return Math.PI;
                    case "e": return Math.E;
                    case "tau": return 2d * Math.PI;
                    default: throw new FormatException();
                }
            }

            var begin = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) { _i++; }
            if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E') && _i > begin)
            {
                var save = _i;
                _i++;
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) { _i++; }
                var digits = _i;
                while (_i < _s.Length && char.IsDigit(_s[_i])) { _i++; }
                if (digits == _i) { _i = save; }
            }

            if (begin == _i ||
                !double.TryParse(_s.Substring(begin, _i - begin), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                throw new FormatException();
            }

            return number;
        }

        private void Skip()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) { _i++; }
        }

        private bool Eat(char c)
        {
            if (_i < _s.Length && _s[_i] == c)
            {
                _i++;
                return true;
            }

            return false;
        }
    }
}
