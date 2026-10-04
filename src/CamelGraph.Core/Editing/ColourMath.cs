using System;

namespace CamelGraph.Core.Editing;

/// <summary>Colour-space maths behind the colour picker (kept free of UI types so it is unit-tested).</summary>
public static class ColourMath
{
    /// <summary>Converts hue (degrees), saturation and value (0–1) to 0–255 RGB channels.</summary>
    public static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
    {
        h = ((h % 360d) + 360d) % 360d;
        s = Clamp01(s);
        v = Clamp01(v);
        var c = v * s;
        var x = c * (1d - Math.Abs(((h / 60d) % 2d) - 1d));
        var m = v - c;
        double rp, gp, bp;
        if (h < 60d) { rp = c; gp = x; bp = 0d; }
        else if (h < 120d) { rp = x; gp = c; bp = 0d; }
        else if (h < 180d) { rp = 0d; gp = c; bp = x; }
        else if (h < 240d) { rp = 0d; gp = x; bp = c; }
        else if (h < 300d) { rp = x; gp = 0d; bp = c; }
        else { rp = c; gp = 0d; bp = x; }

        r = ToByte(rp + m);
        g = ToByte(gp + m);
        b = ToByte(bp + m);
    }

    /// <summary>Converts 0–255 RGB channels to hue (degrees), saturation and value (0–1).</summary>
    public static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
    {
        double rd = r / 255d, gd = g / 255d, bd = b / 255d;
        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var delta = max - min;
        if (delta < 1e-9)
        {
            h = 0d;
        }
        else if (max == rd)
        {
            h = 60d * ((((gd - bd) / delta) % 6d + 6d) % 6d);
        }
        else if (max == gd)
        {
            h = 60d * (((bd - rd) / delta) + 2d);
        }
        else
        {
            h = 60d * (((rd - gd) / delta) + 4d);
        }

        s = max < 1e-9 ? 0d : delta / max;
        v = max;
    }

    private static double Clamp01(double value) => value < 0d ? 0d : value > 1d ? 1d : value;

    private static byte ToByte(double unit) => (byte)Math.Max(0d, Math.Min(255d, Math.Round(unit * 255d)));
}
