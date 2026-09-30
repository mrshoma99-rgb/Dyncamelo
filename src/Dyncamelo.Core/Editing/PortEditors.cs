using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Core.Editing;

/// <summary>The inline editor an unwired input port gets.</summary>
public enum PortEditorKind
{
    /// <summary>No editor: the value must come from a wire.</summary>
    None,
    /// <summary>Scrub/type number field.</summary>
    Number,
    /// <summary>On/off switch.</summary>
    Toggle,
    /// <summary>Fixed set of choices (drop-down, or segmented buttons when few and short).</summary>
    Choice,
    /// <summary>Single-line text.</summary>
    Text,
    /// <summary>Colour swatch opening the colour picker.</summary>
    Colour,
    /// <summary>Text plus a browse button for a file or folder.</summary>
    Path,
}

/// <summary>Everything a number field needs to know about its port.</summary>
public sealed class NumberEditSpec
{
    /// <summary>Hard minimum (clamps every edit).</summary>
    public double Min { get; set; } = double.MinValue;

    /// <summary>Hard maximum (clamps every edit).</summary>
    public double Max { get; set; } = double.MaxValue;

    /// <summary>Slider extent minimum (typing may go below).</summary>
    public double SoftMin { get; set; } = double.NaN;

    /// <summary>Slider extent maximum (typing may go above).</summary>
    public double SoftMax { get; set; } = double.NaN;

    /// <summary>Increment for arrows and scrubbing.</summary>
    public double Step { get; set; } = 1d;

    /// <summary>True for integer-typed ports: values round to whole numbers.</summary>
    public bool IsInteger { get; set; }

    /// <summary>Unit suffix ("mm", "%").</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>True when a finite slider extent exists, so a fill bar can be drawn.</summary>
    public bool HasSoftRange => !double.IsNaN(SoftMin) && !double.IsNaN(SoftMax) && SoftMax > SoftMin;

    /// <summary>Builds the spec from a port's range attribute, type and default.</summary>
    public static NumberEditSpec FromPort(PortModel port)
    {
        var type = Nullable.GetUnderlyingType(port.DeclaredType) ?? port.DeclaredType;
        var isInteger = type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
                        type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte);
        var spec = new NumberEditSpec { IsInteger = isInteger };

        double defaultValue = 0d;
        if (port.HasDefault && port.DefaultValue != null && TryToDouble(port.DefaultValue, out var d))
        {
            defaultValue = d;
        }

        var range = port.Range;
        if (range != null)
        {
            spec.Min = range.Min;
            spec.Max = range.Max;
            spec.SoftMin = range.SoftMin;
            spec.SoftMax = range.SoftMax;
            spec.Unit = range.Unit ?? string.Empty;
            spec.Step = !double.IsNaN(range.Step) && range.Step > 0d ? range.Step : NumberFormat.NiceStep(defaultValue, isInteger);
        }
        else
        {
            spec.Step = NumberFormat.NiceStep(defaultValue, isInteger);
        }

        if (type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(byte))
        {
            spec.Min = Math.Max(spec.Min, 0d);
        }

        if (isInteger)
        {
            spec.Step = Math.Max(1d, Math.Round(spec.Step));
        }

        return spec;
    }

    internal static bool TryToDouble(object value, out double result)
    {
        try
        {
            if (value is bool || value is string || value is char)
            {
                result = 0d;
                return false;
            }

            result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return !double.IsNaN(result) && !double.IsInfinity(result);
        }
        catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException)
        {
            result = 0d;
            return false;
        }
    }
}

/// <summary>Drag/step/typed-input arithmetic for the number field. Pure, so it is unit-tested.</summary>
public static class ScrubMath
{
    /// <summary>Pixels of drag that equal one step when the field has no finite range.</summary>
    public const double PixelsPerStep = 8d;

    /// <summary>The value after dragging <paramref name="deltaPixels"/> from <paramref name="start"/>.</summary>
    /// <param name="start">Value when the drag began.</param>
    /// <param name="deltaPixels">Horizontal distance dragged (positive = right).</param>
    /// <param name="spec">Range and step.</param>
    /// <param name="fieldWidth">Rendered width of the field in pixels.</param>
    /// <param name="fine">Shift held: a tenth of the sensitivity.</param>
    /// <param name="snap">Ctrl held: snap to multiples of the step.</param>
    public static double Scrub(double start, double deltaPixels, NumberEditSpec spec, double fieldWidth, bool fine, bool snap)
    {
        double perPixel;
        if (spec.HasSoftRange && fieldWidth > 20d)
        {
            perPixel = (spec.SoftMax - spec.SoftMin) / fieldWidth;
        }
        else
        {
            perPixel = spec.Step / PixelsPerStep;
        }

        if (fine)
        {
            perPixel *= 0.1d;
        }

        var value = start + deltaPixels * perPixel;
        if (snap)
        {
            value = NumberFormat.Snap(value, spec.Step);
        }

        return Normalize(value, spec, start);
    }

    /// <summary>The value one step up (<paramref name="direction"/> &gt; 0) or down, normalised.</summary>
    public static double StepBy(double value, int direction, NumberEditSpec spec, bool fine = false)
    {
        var step = fine ? spec.Step * 0.1d : spec.Step;
        var next = NumberFormat.Snap(value + Math.Sign(direction) * step, step);
        return Normalize(next, spec, value);
    }

    /// <summary>Parses typed text (numbers and arithmetic, optionally followed by the unit) into a normalised value.</summary>
    public static bool TryParse(string? text, NumberEditSpec spec, out double value)
    {
        value = 0d;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var input = text!.Trim();
        if (!string.IsNullOrEmpty(spec.Unit) && input.EndsWith(spec.Unit, StringComparison.OrdinalIgnoreCase))
        {
            input = input.Substring(0, input.Length - spec.Unit.Length).TrimEnd();
        }

        if (!NumberExpression.TryEvaluate(input, out var parsed))
        {
            return false;
        }

        value = Normalize(parsed, spec, parsed);
        return true;
    }

    /// <summary>Clamps to the hard range, rounds integers, and rejects non-finite input (falls back to <paramref name="fallback"/>).</summary>
    public static double Normalize(double value, NumberEditSpec spec, double fallback)
    {
        var clamped = NumberFormat.Clamp(value, spec.Min, spec.Max, fallback);
        if (spec.IsInteger)
        {
            clamped = Math.Round(clamped, MidpointRounding.AwayFromZero);
            clamped = NumberFormat.Clamp(clamped, spec.Min, spec.Max, fallback);
        }

        return clamped;
    }
}

/// <summary>Chooses the inline editor for a port and reads/writes its pinned value.</summary>
public static class PortEditors
{
    /// <summary>Choice lists this small and short are shown as segmented buttons instead of a drop-down.</summary>
    public const int MaxSegmentedChoices = 3;

    /// <summary>Character budget for segmented choices.</summary>
    public const int MaxSegmentedCharacters = 24;

    /// <summary>The editor an input port gets while unwired.</summary>
    public static PortEditorKind Resolve(PortModel port)
    {
        if (port == null)
        {
            throw new ArgumentNullException(nameof(port));
        }

        if (port.Direction != PortDirection.Input)
        {
            return PortEditorKind.None;
        }

        if (port.Choices != null && port.Choices.Count > 0)
        {
            return PortEditorKind.Choice;
        }

        var kind = PortKinds.FromPort(port);
        if (kind.Depth != PortDepth.Item)
        {
            return PortEditorKind.None;
        }

        switch (kind.Family)
        {
            case PortFamily.Boolean:
                return PortEditorKind.Toggle;
            case PortFamily.Number:
            case PortFamily.Integer:
                return PortEditorKind.Number;
            case PortFamily.Colour:
                return PortEditorKind.Colour;
            case PortFamily.File:
                return PortEditorKind.Path;
            case PortFamily.Text:
            case PortFamily.DateTime:
                return PortEditorKind.Text;
            default:
                return PortEditorKind.None;
        }
    }

    /// <summary>True when a port named like a folder expects a directory rather than a file.</summary>
    public static bool IsFolder(PortModel port)
    {
        var n = port.Name.ToLowerInvariant();
        return n.EndsWith("folder", StringComparison.Ordinal) || n.EndsWith("directory", StringComparison.Ordinal) ||
               n.EndsWith("dir", StringComparison.Ordinal);
    }

    /// <summary>True when the choices should render as segmented buttons.</summary>
    public static bool UseSegmentedChoices(IReadOnlyList<string>? choices)
    {
        return choices != null && choices.Count >= 2 && choices.Count <= MaxSegmentedChoices &&
               choices.Sum(c => c.Length) <= MaxSegmentedCharacters;
    }

    /// <summary>The value the port currently shows: the pinned value, else its default.</summary>
    public static object? Current(PortModel port) => port.HasUserValue ? port.UserValue : port.DefaultValue;

    /// <summary>True when the port has neither a pinned value nor a default (the field shows a placeholder).</summary>
    public static bool IsUnset(PortModel port) => !port.HasUserValue && (!port.HasDefault || port.DefaultValue == null);

    /// <summary>Current numeric value (0 when unset or not numeric). Infinite defaults (an unbounded "max") are returned as-is and shown as ∞.</summary>
    public static double GetNumber(PortModel port)
    {
        var current = Current(port);
        if (current != null && NumberEditSpec.TryToDouble(current, out var d))
        {
            return d;
        }

        if (current is double inf && double.IsInfinity(inf))
        {
            return inf;
        }

        return 0d;
    }

    /// <summary>Pins a number typed to the port's own primitive type; equal to the default clears the pin.</summary>
    public static void SetNumber(PortModel port, double value)
    {
        var spec = NumberEditSpec.FromPort(port);
        var normalised = ScrubMath.Normalize(value, spec, GetNumber(port));
        if (port.HasDefault && port.DefaultValue is double defaultDouble && defaultDouble == normalised)
        {
            port.ClearUserValue();
            return;
        }

        if (port.HasDefault && port.DefaultValue != null && NumberEditSpec.TryToDouble(port.DefaultValue, out var def) && def == normalised)
        {
            port.ClearUserValue();
            return;
        }

        port.SetUserValue(CoerceNumber(normalised, port.DeclaredType));
    }

    /// <summary>The number converted to the primitive type a port declares (double when unknown).</summary>
    public static object CoerceNumber(double value, Type declared)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        try
        {
            if (type == typeof(int)) { return checked((int)Math.Round(value, MidpointRounding.AwayFromZero)); }
            if (type == typeof(long)) { return checked((long)Math.Round(value, MidpointRounding.AwayFromZero)); }
            if (type == typeof(short)) { return checked((short)Math.Round(value, MidpointRounding.AwayFromZero)); }
            if (type == typeof(byte)) { return checked((byte)Math.Round(value, MidpointRounding.AwayFromZero)); }
            if (type == typeof(uint)) { return checked((uint)Math.Round(value, MidpointRounding.AwayFromZero)); }
            if (type == typeof(ulong)) { return checked((ulong)Math.Round(value, MidpointRounding.AwayFromZero)); }
            if (type == typeof(float)) { return (float)value; }
            if (type == typeof(decimal)) { return (decimal)value; }
        }
        catch (OverflowException)
        {
            // fall through to double
        }

        return value;
    }

    /// <summary>Current boolean value.</summary>
    public static bool GetBool(PortModel port)
    {
        var current = Current(port);
        return current is bool b && b;
    }

    /// <summary>Pins a boolean; equal to the default clears the pin.</summary>
    public static void SetBool(PortModel port, bool value)
    {
        if (port.HasDefault && port.DefaultValue is bool def && def == value)
        {
            port.ClearUserValue();
            return;
        }

        port.SetUserValue(value);
    }

    /// <summary>Current text value (empty when unset).</summary>
    public static string GetText(PortModel port)
    {
        var current = Current(port);
        if (current == null)
        {
            return string.Empty;
        }

        return current is DateTime dt ? dt.ToString("s", CultureInfo.InvariantCulture) : Convert.ToString(current, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>Pins text; equal to the default clears the pin. Empty text on a port without a default clears it too.</summary>
    public static void SetText(PortModel port, string? text)
    {
        var value = text ?? string.Empty;
        if (port.HasDefault && port.DefaultValue is string def && def == value)
        {
            port.ClearUserValue();
            return;
        }

        if (value.Length == 0 && !port.HasDefault)
        {
            port.ClearUserValue();
            return;
        }

        port.SetUserValue(value);
    }

    /// <summary>Parses a hex colour "#RRGGBB" / "#AARRGGBB" (the # optional).</summary>
    public static bool TryParseHex(string? hex, out byte a, out byte r, out byte g, out byte b)
    {
        a = 255; r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var digits = hex!.Trim().TrimStart('#');
        if ((digits.Length != 6 && digits.Length != 8) ||
            !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (digits.Length == 8)
        {
            a = (byte)(value >> 24);
        }

        r = (byte)(value >> 16);
        g = (byte)(value >> 8);
        b = (byte)value;
        return true;
    }

    /// <summary>Formats a colour as "#AARRGGBB".</summary>
    public static string ToHex(byte a, byte r, byte g, byte b) =>
        "#" + a.ToString("X2", CultureInfo.InvariantCulture) + r.ToString("X2", CultureInfo.InvariantCulture) +
        g.ToString("X2", CultureInfo.InvariantCulture) + b.ToString("X2", CultureInfo.InvariantCulture);

    /// <summary>Current colour as hex, or empty when unset / not a colour string.</summary>
    public static string GetColourHex(PortModel port)
    {
        var text = Current(port) as string;
        return TryParseHex(text, out var a, out var r, out var g, out var b) ? ToHex(a, r, g, b) : string.Empty;
    }

    /// <summary>Pins a colour as its hex string (a JSON-friendly primitive the engine converts).</summary>
    public static void SetColour(PortModel port, byte a, byte r, byte g, byte b)
    {
        port.SetUserValue(ToHex(a, r, g, b));
    }
}
