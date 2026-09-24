namespace Themia.Core.Widgets;

/// <summary>
/// One step of the Windows 11 type ramp: a size and the line height that goes with it.
/// </summary>
/// <param name="FontSize">Size in device-independent pixels.</param>
/// <param name="LineHeight">The line height Fluent pairs with that size.</param>
/// <param name="SemiBold">Whether the step is drawn semibold, as Fluent specifies.</param>
public readonly record struct TypeStep(double FontSize, double LineHeight, bool SemiBold);

/// <summary>
/// The Windows 11 type ramp, and how to pick a step that fits a box.
/// <para>
/// Widgets used to compute a font size as an arbitrary fraction of the tile — 0.26 of the width,
/// clamped between 16 and 88 — which is how the original web app did it and is why the result looked
/// like a web dashboard rather than like Windows. Fluent is a <b>ramp</b>: a fixed set of sizes, each
/// with its own line height and weight, and text that is not on one of them looks foreign next to
/// everything else on the desktop.
/// </para>
/// <para>
/// So widgets still scale to their tile — that is what a resizable widget has to do — but they scale
/// by <i>choosing a step</i> rather than by inventing a number. A tile that grows moves its readout
/// from Subtitle to Title to Display, and every size on screen is one Windows itself uses.
/// </para>
/// </summary>
public static class FluentType
{
    /// <summary>12/16. Labels, timestamps, secondary metadata.</summary>
    public static TypeStep Caption { get; } = new(12, 16, false);

    /// <summary>14/20. The default for body text.</summary>
    public static TypeStep Body { get; } = new(14, 20, false);

    /// <summary>14/20 semibold. A body line that needs emphasis, and the standard list-row title.</summary>
    public static TypeStep BodyStrong { get; } = new(14, 20, true);

    /// <summary>18/24. A larger body line.</summary>
    public static TypeStep BodyLarge { get; } = new(18, 24, false);

    /// <summary>20/28 semibold. Section headings.</summary>
    public static TypeStep Subtitle { get; } = new(20, 28, true);

    /// <summary>28/36 semibold. Page titles.</summary>
    public static TypeStep Title { get; } = new(28, 36, true);

    /// <summary>40/52 semibold.</summary>
    public static TypeStep TitleLarge { get; } = new(40, 52, true);

    /// <summary>68/92 semibold. The largest step Fluent defines.</summary>
    public static TypeStep Display { get; } = new(68, 92, true);

    /// <summary>The ramp in order, smallest first.</summary>
    public static IReadOnlyList<TypeStep> Ramp { get; } =
        [Caption, Body, BodyLarge, Subtitle, Title, TitleLarge, Display];

    /// <summary>
    /// The largest step whose line fits the box, never smaller than <paramref name="floor"/> and
    /// never larger than <paramref name="ceiling"/>.
    /// <para>
    /// The fit is measured against the line height rather than the font size, because a line of type
    /// occupies its line height and sizing to the glyph box is how text ends up touching the thing
    /// above it.
    /// </para>
    /// </summary>
    /// <param name="width">Available width in DIPs.</param>
    /// <param name="height">Height this line may occupy in DIPs.</param>
    /// <param name="characters">
    /// Roughly how many characters the line has to hold. Width matters as much as height: a readout
    /// of "100%" and one of "1.9 GB of 64 GB" want very different steps in the same box.
    /// </param>
    public static TypeStep Fit(double width, double height, double characters, TypeStep floor, TypeStep ceiling)
    {
        // An unmeasured or degenerate box gets the floor rather than a NaN that would reach a
        // FontSize and throw on the render path.
        if (!IsUsable(width) || !IsUsable(height))
        {
            return floor;
        }

        var wanted = Math.Max(1, characters);
        var chosen = floor;

        foreach (var step in Ramp)
        {
            if (step.FontSize < floor.FontSize || step.FontSize > ceiling.FontSize)
            {
                continue;
            }

            // 0.58em is a workable average advance for Segoe UI's digits and lower case. It only has
            // to be close: the result is a step, not a measurement, so being slightly out moves
            // nothing until it crosses a whole step.
            var needed = step.FontSize * 0.58 * wanted;

            if (step.LineHeight <= height && needed <= width)
            {
                chosen = step;
            }
        }

        return chosen;
    }

    /// <summary>
    /// A step scaled past the ramp, for the one case the ramp does not cover: a readout that is the
    /// entire point of a very large tile.
    /// <para>
    /// The ramp stops at 68px because Windows has nothing bigger to say. A clock filling a
    /// quarter-screen tile legitimately does, so it is allowed to scale <see cref="Display"/> up —
    /// keeping Fluent's own line-height ratio, so it is still the ramp's shape rather than a new one.
    /// </para>
    /// </summary>
    public static TypeStep Scaled(TypeStep step, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0)
        {
            return step;
        }

        var ratio = step.LineHeight / step.FontSize;
        var size = step.FontSize * factor;

        return new TypeStep(size, size * ratio, step.SemiBold);
    }

    /// <summary>
    /// The largest multiple of <see cref="Display"/> that still fits, for a hero readout. Never
    /// smaller than the ramp's own Display step.
    /// </summary>
    public static TypeStep Hero(double width, double height, double characters)
    {
        if (!IsUsable(width) || !IsUsable(height))
        {
            return Subtitle;
        }

        var fitted = Fit(width, height, characters, Caption, Display);

        if (fitted.FontSize < Display.FontSize)
        {
            return fitted;
        }

        var wanted = Math.Max(1, characters);
        var byWidth = width / (Display.FontSize * 0.58 * wanted);
        var byHeight = height / Display.LineHeight;
        var factor = Math.Min(byWidth, byHeight);

        return factor > 1 ? Scaled(Display, factor) : Display;
    }

    private static bool IsUsable(double value) => double.IsFinite(value) && value > 0;
}
