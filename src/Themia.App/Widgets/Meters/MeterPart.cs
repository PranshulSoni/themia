using Themia.App.Services;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Widgets.Meters;

/// <summary>What a meter part hands its readings to: the shared <see cref="MeterView"/>.</summary>
internal interface IMeterHost
{
    /// <summary>Put a reading on screen. May be called later than the tick that asked (a probe).</summary>
    void Present(MeterReading reading);

    /// <summary>Forget the history: the samples on the chart belong to something else now.</summary>
    void ClearHistory();

    /// <summary>[S-1167] Offer the card a title the kind knows (a drive's name, a host's label); null for the catalogue's.</summary>
    void SetTitle(string? title);

    /// <summary>[S-1167] Take a reading now rather than at the next tick (a Refresh command).</summary>
    void SampleNow();
}

/// <summary>
/// [S-1127] What the last layout pass had room for, so a part only does the work that will be seen:
/// a meter whose list is not on screen does not walk every process to fill it.
/// </summary>
/// <param name="ListRows">How many breakdown rows fit; 0 means the list is not shown.</param>
/// <param name="Details">Whether any detail line fits.</param>
/// <param name="Segments">Whether the composition bar fits.</param>
/// <param name="Cells">[S-1150] Whether the per-part strip fits.</param>
internal readonly record struct MeterDemand(int ListRows, bool Details, bool Segments, bool Cells = false)
{
    public static MeterDemand None { get; } = new(0, false, false);
}

/// <summary>
/// [S-1129] One meter kind's half of a meter: where the number comes from and what it adds to the
/// shared ladder. <see cref="MeterView"/> owns the other half — the reading, the chart, the bar,
/// min / avg / max, the scale line, the detail lines, the breakdown list, the composition bar and
/// where each goes at each size — so a kind never lays anything out itself, and each kind lives in
/// its own file (<c>Meters/&lt;Kind&gt;Meter.cs</c>) that one package can change without touching
/// another's.
/// <para>
/// A kind adds extras by putting them in the <see cref="MeterReading"/> it presents:
/// <see cref="MeterReading.Details"/> (lines, priority order), <see cref="MeterReading.List"/> (rows),
/// <see cref="MeterReading.Segments"/> (a composition bar), <see cref="MeterReading.Second"/> (a second
/// chart series). See <c>reports/meter-framework.md</c>.
/// </para>
/// </summary>
internal abstract class MeterPart
{
    protected const string Scope = "meter";

    public abstract WidgetKind Kind { get; }

    /// <summary>What the reading's own value is measured in, for the summary and the scale line.</summary>
    public virtual MeterUnit Unit => MeterUnit.Percent;

    /// <summary>
    /// The widest reading this kind can produce, in characters ("100%" is 4, German "100 %" 5). The
    /// kind's worst case, never the live text, so the number does not change size between two samples.
    /// </summary>
    public virtual double ReadingCharacters => Formatting.Percent(1).Length;

    /// <summary>How long the caption is for placing it; a caption that changes length every tick returns its worst case.</summary>
    public virtual double CaptionCharacters(string live) => live.Length;

    /// <summary>Whether the value moves enough for a min / avg / max to say anything.</summary>
    public virtual bool Moves => true;

    /// <summary>[S-1124] What the top of the chart stands for.</summary>
    public virtual ChartScale Scale => ChartScale.Share;

    /// <summary>Whether the measured thing can be missing from the machine (a battery), which shows an empty state.</summary>
    public virtual bool CanBeAbsent => false;

    /// <summary>[S-1127] The most breakdown rows this kind may show with its current config; 0 for no list.</summary>
    public virtual int ListWanted => 0;

    /// <summary>[S-1150] How many bars this kind's per-part strip has with its current config; 0 for no strip.</summary>
    public virtual int CellsWanted => 0;

    /// <summary>[S-1128] Whether this kind draws a usage bar beside its chart (and a composition bar where it fits).</summary>
    public virtual bool UsageBar => false;

    /// <summary>[S-1123] The colour a reading deserves. Never applied to the reading's own text.</summary>
    public virtual MeterPressure PressureOf(MeterReading reading) => MeterPressure.Normal;

    /// <summary>
    /// [S-1187] A glyph drawn before the reading, in the pressure colour (the battery's fill level),
    /// or null for none. Count its width in <see cref="ReadingCharacters"/>.
    /// </summary>
    public virtual string? Badge(MeterReading reading) => null;

    /// <summary>
    /// [S-1186] Whether this meter is a gallery preview, before the first config arrives. A part
    /// with a debug fixture reads it only in a preview, never on the desktop.
    /// </summary>
    public virtual void Bind(bool preview)
    {
    }

    /// <summary>
    /// Take a new config (it is already the right kind). True when the part needs a sample now —
    /// a ping host changed and waiting out the interval would read as a broken widget.
    /// </summary>
    public virtual bool ApplyConfig(WidgetConfig config, IMeterHost host) => false;

    /// <summary>
    /// Take one reading and hand it to <paramref name="host"/> — now, or later for a reading that
    /// needs a round trip. Runs on the UI thread's tick; a throw is caught and shown as unavailable.
    /// </summary>
    public abstract void Sample(MeterDemand demand, IMeterHost host);

    /// <summary>[S-1167] Commands for the card's right-click menu (Refresh, Open in Explorer); read each time it opens.</summary>
    public virtual IReadOnlyList<WidgetAction> ContextActions(IMeterHost host) => [];

    /// <summary>[S-1167] An element for the right of the card's header (a status dot), or null. Read once.</summary>
    public virtual Microsoft.UI.Xaml.FrameworkElement? HeaderTrailing => null;

    /// <summary>Let go of anything held between samples (a probe, a counter lease).</summary>
    public virtual void Detach()
    {
    }

    /// <summary>The part for a meter kind.</summary>
    public static MeterPart For(WidgetKind kind) => kind switch
    {
        WidgetKind.Cpu => new CpuMeter(),
        WidgetKind.Memory => new MemoryMeter(),
        WidgetKind.Disk => new DiskMeter(),
        WidgetKind.Battery => new BatteryMeter(),
        WidgetKind.Network => new NetworkMeter(),
        WidgetKind.Ping => new PingMeter(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a meter"),
    };
}
