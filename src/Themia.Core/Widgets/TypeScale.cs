namespace Themia.Core.Widgets;

/// <summary>
/// How big text should be inside a widget of a given size.
/// <para>
/// A widget is resized freely, so its content has to grow and shrink with it or a large tile is
/// mostly empty and a small one is clipped. This is the equivalent of the original app's
/// <c>clamp(min, min(Ncqw, Mcqh), max)</c>: scale with the box, but never below readable and never
/// past absurd.
/// </para>
/// <para>
/// Real font sizes rather than a scale transform, because a <c>Viewbox</c> stretches an already
/// rendered glyph — text laid out at 48px and blown up four times is soft, while text laid out at
/// 192px is sharp.
/// </para>
/// </summary>
public static class TypeScale
{
    /// <summary>
    /// Scale to whichever of the two axes runs out first, then clamp.
    /// <para>
    /// Both axes matter: sizing on width alone makes a wide, short tile clip its text vertically,
    /// and sizing on height alone makes a tall, narrow one overflow sideways.
    /// </para>
    /// </summary>
    /// <param name="width">Available width in DIPs.</param>
    /// <param name="height">Available height in DIPs.</param>
    /// <param name="widthFactor">Fraction of the width one line should occupy.</param>
    /// <param name="heightFactor">Fraction of the height one line should occupy.</param>
    /// <param name="min">Never smaller than this, even in a tiny tile.</param>
    /// <param name="max">Never larger than this, however big the tile.</param>
    public static double Fit(
        double width,
        double height,
        double widthFactor,
        double heightFactor,
        double min,
        double max)
    {
        if (max < min)
        {
            (min, max) = (max, min);
        }

        // A box that has not been measured yet, or a degenerate one, gets the floor rather than a
        // NaN that would propagate into a FontSize and throw at render time.
        if (!IsUsable(width) || !IsUsable(height))
        {
            return min;
        }

        var candidate = Math.Min(width * widthFactor, height * heightFactor);
        return double.IsFinite(candidate) ? Math.Clamp(candidate, min, max) : min;
    }

    /// <summary>Scale against one axis only, for content that is constrained in just that direction.</summary>
    public static double FitWidth(double width, double factor, double min, double max)
        => Fit(width, double.MaxValue, factor, 1, min, max);

    private static bool IsUsable(double value) => double.IsFinite(value) && value > 0;
}
