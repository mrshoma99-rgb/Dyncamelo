using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Types;
using CamelGraph.Core.Loader;

namespace CamelGraph.Core.Editing;

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
    /// <summary>Model element picker: use the host's current selection, click to select them again, clear.</summary>
    Model,
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
    /// <summary>
    /// Where the pointer should reappear when a drag reaches the edge of the screen area, so a number can be dragged further
    /// than the screen is wide.
    /// </summary>
    /// <param name="x">Pointer X in screen pixels.</param>
    /// <param name="left">Left edge of the screen area.</param>
    /// <param name="width">Width of the screen area.</param>
    /// <param name="margin">How close to an edge counts as reaching it.</param>
    /// <param name="newX">The X to move the pointer to (the opposite edge, just inside it) when this returns true.</param>
    /// <returns>True when the pointer is at an edge and should be moved.</returns>
    public static bool TryWrap(double x, double left, double width, double margin, out double newX)
    {
        newX = x;
        if (width <= margin * 6d)
        {
            return false;
        }

        var right = left + width;
        if (x <= left + margin)
        {
            newX = right - (margin * 2d);
            return true;
        }

        if (x >= right - margin)
        {
            newX = left + (margin * 2d);
            return true;
        }

        return false;
    }

    /// <summary>Pixels of drag that equal one step when the field has no finite range.</summary>
    public const double PixelsPerStep = 8d;

    /// <summary>The value after dragging <paramref name="deltaPixels"/> from <paramref name="start"/>.</summary>
    /// <param name="start">Value when the drag began.</param>
    /// <param name="deltaPixels">Horizontal distance dragged (positive = right).</param>
    /// <param name="spec">Range and step.</param>
    /// <param name="fieldWidth">Rendered width of the field in pixels.</param>
    /// <param name="fine">Shift held: a tenth of the sensitivity.</param>
    /// <param name="snap">Ctrl held: snap to multiples of the step.</param>
    /// <param name="pixelsPerStep">Drag speed: pixels of travel per step (the default suits most; smaller is faster).</param>
    public static double Scrub(double start, double deltaPixels, NumberEditSpec spec, double fieldWidth, bool fine, bool snap, double pixelsPerStep = PixelsPerStep)
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

        // The speed preference scales both the stepped and the range-based feel equally.
        if (pixelsPerStep > 0d && !double.IsNaN(pixelsPerStep) && !double.IsInfinity(pixelsPerStep))
        {
            perPixel *= PixelsPerStep / pixelsPerStep;
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

        if (IsModelItemPort(port))
        {
            return PortEditorKind.Model;
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

    /// <summary>True when the port takes host model elements (a model item, a list of them, or a model-item collection).</summary>
    public static bool IsModelItemPort(PortModel port)
    {
        var type = port.DeclaredType;
        if (type == null)
        {
            return false;
        }

        if (type.Name == "ModelItemCollection")
        {
            return true;
        }

        var element = TypeCoercion.GetListElementType(type) ?? type;
        return element.Name == "ModelItem";
    }

    /// <summary>True when a model-element port takes a single item rather than a list.</summary>
    public static bool IsSingleModelItem(PortModel port)
    {
        var type = port.DeclaredType;
        return type != null && type.Name == "ModelItem";
    }

    /// <summary>
    /// True when a path port expects a directory rather than a file: it is marked <c>[NodePath(NodePathMode.Folder)]</c>, else
    /// named like a folder (see <see cref="PathPicker.Resolve"/>).
    /// </summary>
    public static bool IsFolder(PortModel port) => PathPicker.Resolve(port).IsFolder;

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

    /// <summary>
    /// Splits pasted text into numbers: "1, 2, 3", "(1; 2; 3)", one per line or tab (spreadsheet cells), or plain numbers
    /// separated by spaces. Each part may be an expression ("1+2"). Returns nothing unless every part is a number.
    /// </summary>
    /// <param name="text">The clipboard text.</param>
    public static IReadOnlyList<double> SplitNumbers(string? text)
    {
        var numbers = new List<double>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return numbers;
        }

        var trimmed = text!.Trim().Trim('(', ')', '[', ']', '{', '}').Trim();
        var parts = trimmed.Split(new[] { ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            // "1 2 3": spaces only separate plain numbers (a space inside "1 + 2" is part of the expression).
            parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(part => !double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                return numbers;
            }
        }

        foreach (var part in parts)
        {
            if (!NumberExpression.TryEvaluate(part.Trim(), out var value) || double.IsNaN(value) || double.IsInfinity(value))
            {
                return new List<double>();
            }

            numbers.Add(value);
        }

        return numbers;
    }

    /// <summary>
    /// Pastes several numbers into consecutive number fields starting at <paramref name="start"/> (a coordinate "1, 2, 3" into
    /// x, y, z). Stops at the first port that is not an unwired number field.
    /// </summary>
    /// <param name="inputs">The node's input ports.</param>
    /// <param name="start">Index of the port the paste started on.</param>
    /// <param name="text">The clipboard text.</param>
    /// <param name="isConnected">Whether a port has a wire.</param>
    /// <returns>How many fields were filled; 0 when the text is not two or more numbers or the first field cannot take one.</returns>
    public static int PasteNumbers(IReadOnlyList<PortModel> inputs, int start, string? text, Func<PortModel, bool> isConnected)
    {
        var numbers = SplitNumbers(text);
        if (numbers.Count < 2 || start < 0)
        {
            return 0;
        }

        var filled = 0;
        for (var i = start; i < inputs.Count && filled < numbers.Count; i++)
        {
            var port = inputs[i];
            if (Resolve(port) != PortEditorKind.Number || isConnected(port))
            {
                break;
            }

            SetNumber(port, numbers[filled]);
            filled++;
        }

        return filled;
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
