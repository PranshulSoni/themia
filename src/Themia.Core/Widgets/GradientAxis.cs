namespace Themia.Core.Widgets;

/// <summary>
/// [S-1503] Where a linear gradient starts and ends inside a box, in the box's own coordinates
/// (y down), for a CSS-style angle: 0° points up, 90° right, clockwise.
/// <para>
/// The original drew its gradient with CSS <c>linear-gradient(&lt;angle&gt;deg, …)</c>, whose line runs
/// through the centre and is exactly long enough that the two corners furthest along it sit on the
/// 0 % and 100 % stops — so the colours reach every corner whatever the box's shape. XAML's
/// <c>LinearGradientBrush</c> in relative mapping instead stretches a unit square onto the box, which
/// skews every angle that is not a multiple of 90° on a box that is not square. The axis is
/// therefore computed in absolute coordinates for the actual size, here, where the corner rule is
/// tested.
/// </para>
/// </summary>
/// <param name="StartX">Where the 0 % stop sits.</param>
/// <param name="StartY">Where the 0 % stop sits.</param>
/// <param name="EndX">Where the 100 % stop sits.</param>
/// <param name="EndY">Where the 100 % stop sits.</param>
public readonly record struct GradientAxis(double StartX, double StartY, double EndX, double EndY)
{
    /// <summary>The original's default angle: top-left to bottom-right.</summary>
    public const double DefaultAngle = 135;

    /// <summary>
    /// The axis for <paramref name="angleDegrees"/> across a <paramref name="width"/> ×
    /// <paramref name="height"/> box. A box that is not measured yet (zero, negative, NaN) is treated
    /// as a unit square, so the brush is valid before the first layout and right after it.
    /// </summary>
    public static GradientAxis For(double angleDegrees, double width, double height)
    {
        var w = double.IsFinite(width) && width > 0 ? width : 1;
        var h = double.IsFinite(height) && height > 0 ? height : 1;

        var radians = NormaliseAngle(angleDegrees) * Math.PI / 180;
        var dx = Math.Sin(radians);
        var dy = -Math.Cos(radians);

        // CSS's gradient-line length: the corners furthest along the direction land on the ends.
        var half = ((Math.Abs(w * dx) + Math.Abs(h * dy)) / 2);

        var cx = w / 2;
        var cy = h / 2;

        return new GradientAxis(cx - (dx * half), cy - (dy * half), cx + (dx * half), cy + (dy * half));
    }

    /// <summary>
    /// An angle in [0, 360). A value a hand edit made meaningless (NaN, infinite) is the default
    /// rather than an exception: the widget still draws.
    /// </summary>
    public static double NormaliseAngle(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            return DefaultAngle;
        }

        var turned = degrees % 360;
        return turned < 0 ? turned + 360 : turned;
    }
}

/// <summary>A colour as a swatch paints it: the colour and how opaque it is (0..1).</summary>
public readonly record struct FillColor(RgbColor Color, double Alpha)
{
    /// <summary>The alpha byte: 0..1 clamped and rounded, NaN reading as opaque.</summary>
    public byte A => (byte)Math.Round(SwatchFill.ClampAlpha(Alpha) * 255);
}

/// <summary>[S-1502] Reading the swatch's stored colours, which come off disk and are not trusted.</summary>
public static class SwatchFill
{
    /// <summary>The original's gradient end colour (its "green").</summary>
    public const string DefaultEndColor = "#34D399";

    /// <summary>0..1; anything else a hand edit wrote reads as the nearest end, NaN as opaque.</summary>
    public static double ClampAlpha(double alpha) => double.IsNaN(alpha) ? 1 : Math.Clamp(alpha, 0, 1);

    /// <summary>
    /// The colour to paint for a stored <c>#RRGGBB</c>: an empty or unreadable value is
    /// <paramref name="fallback"/> (the theme's accent, as the original's default was the app's).
    /// </summary>
    public static FillColor Resolve(string? hex, double alpha, RgbColor fallback)
        => HexColor.TryParse(hex, out var r, out var g, out var b)
            ? new FillColor(new RgbColor(r, g, b), ClampAlpha(alpha))
            : new FillColor(fallback, ClampAlpha(alpha));
}
