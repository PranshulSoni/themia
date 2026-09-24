using System.Globalization;

namespace Themia.Core.Widgets;

/// <summary>
/// Number and size formatting for the readout widgets.
/// <para>
/// Explicit rather than culture-driven, for the same reason as the clock: the app builds with
/// <c>InvariantGlobalization</c>, so culture format strings do not behave the way a display string
/// needs to.
/// </para>
/// </summary>
public static class Formatting
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>
    /// A byte count as a short human string: <c>931 GB</c>, <c>1.4 GB</c>, <c>512 MB</c>.
    /// <para>
    /// Binary units (1024), because that is what Windows reports and what every other disk readout
    /// on the machine shows — a widget disagreeing with File Explorer about the size of the same
    /// drive reads as a bug.
    /// </para>
    /// </summary>
    public static string Bytes(double bytes)
    {
        if (!double.IsFinite(bytes) || bytes <= 0)
        {
            return "0 B";
        }

        var unit = 0;
        while (bytes >= 1024 && unit < ByteUnits.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        // One decimal only while it still carries information: "1.4 GB" is useful, "931.0 GB" is noise.
        var digits = unit == 0 || bytes >= 100 ? 0 : bytes >= 10 ? 0 : 1;
        return $"{bytes.ToString($"F{digits}", CultureInfo.InvariantCulture)} {ByteUnits[unit]}";
    }

    /// <summary>A per-second rate, e.g. <c>1.2 MB/s</c>.</summary>
    public static string Rate(double bytesPerSecond) => $"{Bytes(bytesPerSecond)}/s";

    /// <summary>
    /// A 0..1 fraction as a whole percentage, clamped. Never rounds a non-zero load to "0%" or an
    /// incomplete one to "100%", because both read as "nothing is happening" when something is.
    /// </summary>
    public static string Percent(double fraction)
    {
        if (!double.IsFinite(fraction))
        {
            return "--";
        }

        var clamped = Math.Clamp(fraction, 0, 1) * 100;
        var rounded = Math.Round(clamped);

        if (rounded <= 0 && clamped > 0)
        {
            rounded = 1;
        }

        if (rounded >= 100 && clamped < 100)
        {
            rounded = 99;
        }

        return $"{rounded.ToString("F0", CultureInfo.InvariantCulture)}%";
    }

    /// <summary>A duration as <c>4h 20m</c> / <c>35m</c>, for battery time remaining.</summary>
    public static string Duration(TimeSpan span)
    {
        if (span <= TimeSpan.Zero)
        {
            return "--";
        }

        var hours = (int)span.TotalHours;
        return hours > 0 ? $"{hours}h {span.Minutes}m" : $"{span.Minutes}m";
    }

    /// <summary>
    /// How long ago an instant was, in the terms a mail list uses: <c>now</c>, <c>4m</c>,
    /// <c>3h</c>, <c>Tue</c>, then a date.
    /// </summary>
    public static string Ago(DateTimeOffset when, DateTimeOffset now)
    {
        var delta = now - when;

        if (delta < TimeSpan.Zero)
        {
            return "now"; // clock skew between the provider and this machine
        }

        if (delta < TimeSpan.FromMinutes(1))
        {
            return "now";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"{(int)delta.TotalMinutes}m";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"{(int)delta.TotalHours}h";
        }

        if (delta < TimeSpan.FromDays(7))
        {
            return DayNames[(int)when.DayOfWeek];
        }

        return $"{when.Day} {MonthNames[when.Month - 1]}";
    }

    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    private static readonly string[] MonthNames =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
}
