namespace Dyncamelo.Core.Editing;

/// <summary>Level of detail of the canvas, chosen from the zoom.</summary>
public enum LodLevel
{
    /// <summary>Everything is drawn.</summary>
    Full,
    /// <summary>Editors are simplified.</summary>
    Compact,
    /// <summary>Nodes shrink to their header; wires are straight hairlines.</summary>
    Overview,
}

/// <summary>Zoom thresholds with hysteresis, so a level never flickers at the boundary.</summary>
public static class Lod
{
    /// <summary>Zoom at or above which Full is entered.</summary>
    public const double FullEnter = 0.60;

    /// <summary>Zoom below which Full is left.</summary>
    public const double FullLeave = 0.55;

    /// <summary>Zoom below which Overview is entered.</summary>
    public const double OverviewEnter = 0.30;

    /// <summary>Zoom at or above which Overview is left.</summary>
    public const double OverviewLeave = 0.33;

    /// <summary>The level to use at <paramref name="zoom"/>, given the current one.</summary>
    public static LodLevel Next(LodLevel current, double zoom)
    {
        if (double.IsNaN(zoom) || double.IsInfinity(zoom) || zoom <= 0d)
        {
            return current;
        }

        switch (current)
        {
            case LodLevel.Full:
                if (zoom >= FullLeave)
                {
                    return LodLevel.Full;
                }

                return zoom < OverviewEnter ? LodLevel.Overview : LodLevel.Compact;
            case LodLevel.Compact:
                if (zoom >= FullEnter)
                {
                    return LodLevel.Full;
                }

                return zoom < OverviewEnter ? LodLevel.Overview : LodLevel.Compact;
            default:
                if (zoom < OverviewLeave)
                {
                    return LodLevel.Overview;
                }

                return zoom >= FullEnter ? LodLevel.Full : LodLevel.Compact;
        }
    }
}
