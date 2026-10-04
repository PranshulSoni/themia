namespace Themia.Core.Widgets;

/// <summary>
/// Which speaker icon the output level deserves. Windows draws four, and so does this: the glyph
/// is how you read the level at a glance on a tile too small for the number.
/// </summary>
public enum VolumeLevel
{
    /// <summary>Muted, whatever the level underneath says.</summary>
    Muted = 0,

    /// <summary>Not muted, but turned all the way down — audibly the same, deliberately not.</summary>
    Silent = 1,

    Low = 2,
    Medium = 3,
    High = 4,
}

/// <summary>
/// The arithmetic between the mixer's 0..1 scalar and what a person reads and drags.
/// <para>
/// Separate from the endpoint because it is the part that can be wrong in a way nobody notices: a
/// scalar that rounds to <c>0%</c> while sound is still coming out reads as a broken mute, and a
/// slider that reports back a percentage the mixer never agreed to is how a widget and the volume
/// flyout end up one step apart forever.
/// </para>
/// </summary>
public static class VolumeRules
{
    /// <summary>
    /// The scalar as the whole percentage Windows' own flyout shows.
    /// <para>
    /// Rounded away from zero at the halfway point, as every other volume readout on the machine
    /// does, and never all the way down to <c>0%</c> while the scalar is above zero: silence and
    /// "very quiet" are different facts and a tile that conflates them is lying about the one thing
    /// it is for.
    /// </para>
    /// </summary>
    public static int Percent(double scalar)
    {
        if (!double.IsFinite(scalar) || scalar <= 0)
        {
            return 0;
        }

        var percent = (int)Math.Round(Math.Clamp(scalar, 0, 1) * 100, MidpointRounding.AwayFromZero);
        return percent == 0 ? 1 : percent;
    }

    /// <summary>
    /// The scalar a percentage means. The inverse of <see cref="Percent"/> at every whole step, so
    /// a slider dropped on 34 sets exactly what a tile then reads back as 34%.
    /// </summary>
    public static double Scalar(double percent)
        => double.IsFinite(percent) ? Math.Clamp(percent, 0, 100) / 100.0 : 0;

    /// <summary>
    /// Which speaker glyph the state calls for. Mute wins over the level: a muted endpoint at 80%
    /// is silent, and showing three sound waves over it would be the widget disagreeing with the
    /// machine.
    /// </summary>
    public static VolumeLevel Level(double scalar, bool muted)
    {
        if (muted)
        {
            return VolumeLevel.Muted;
        }

        var percent = Percent(scalar);

        return percent switch
        {
            <= 0 => VolumeLevel.Silent,
            < 34 => VolumeLevel.Low,
            < 67 => VolumeLevel.Medium,
            _ => VolumeLevel.High,
        };
    }

    /// <summary>
    /// The line under the readout. Says <i>muted</i> in words as well as in the glyph, because a
    /// state carried only by an icon is a state some readers do not get at all.
    /// </summary>
    public static string Caption(double scalar, bool muted)
    {
        if (muted)
        {
            return "Muted";
        }

        return Percent(scalar) == 0 ? "Turned down" : "Output level";
    }

    /// <summary>The big number. Kept separate from the caption so the tile can drop one and not the other.</summary>
    public static string Readout(double scalar) => $"{Percent(scalar)}%";
}
