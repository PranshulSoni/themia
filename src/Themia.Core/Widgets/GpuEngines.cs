using System.Globalization;
using Themia.Core.Localization;

namespace Themia.Core.Widgets;

/// <summary>
/// [S-1203] The kinds of engine an adapter has, as the GPU counters name them in
/// <c>..._engtype_3D</c>. A closed set: engine types outside it (security, overlay) are not drawn.
/// </summary>
public enum GpuEngineKind
{
    Graphics = 0,
    Compute = 1,
    Copy = 2,
    VideoDecode = 3,
    VideoEncode = 4,
    VideoProcessing = 5,
}

/// <summary>One engine kind's load on one adapter, 0..100: its busiest engine of that kind.</summary>
public readonly record struct GpuEngineLoad(GpuEngineKind Kind, double Percent);

/// <summary>One process's load on one adapter, 0..100: the busiest engine it uses.</summary>
/// <param name="Name">What to call it; resolved by the sampler, never empty.</param>
public readonly record struct GpuProcessLoad(int Pid, string Name, double Percent);

/// <summary>
/// [S-1203]..[S-1206] The breakdowns behind the GPU tile's larger sizes, worked out from the same
/// <c>\GPU Engine(*)\Utilization Percentage</c> instances the load figure comes from: one figure per
/// engine kind (3D, copy, video decode…) and one per process. Nothing here needs a GPU to test.
/// </summary>
public static class GpuEngines
{
    /// <summary>[S-1205] Processes the tile lists at most: the largest tile has room for about this many.</summary>
    public const int MaxProcesses = 8;

    /// <summary>
    /// [S-1203] The engine kind an instance measures, from its <c>_engtype_</c> tail, or null when it
    /// has none or names a kind the tile does not draw. Vendors spell the same engine differently:
    /// "3D", "Graphics_1" and "High Priority 3D" are all graphics; "Compute_0" and "Cuda" are compute.
    /// </summary>
    public static GpuEngineKind? KindOf(string instance)
    {
        if (string.IsNullOrEmpty(instance))
        {
            return null;
        }

        var at = instance.IndexOf("_engtype_", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return null;
        }

        var type = instance[(at + "_engtype_".Length)..];

        // Most specific first: "VideoProcessing" must not be read as anything else, and "High
        // Priority Compute" is compute although it is not the plain "Compute_N".
        if (Has(type, "VideoDecode") || Has(type, "Video Decode"))
        {
            return GpuEngineKind.VideoDecode;
        }

        if (Has(type, "VideoEncode") || Has(type, "Video Encode"))
        {
            return GpuEngineKind.VideoEncode;
        }

        if (Has(type, "VideoProcessing") || Has(type, "Video Processing"))
        {
            return GpuEngineKind.VideoProcessing;
        }

        if (Has(type, "Copy"))
        {
            return GpuEngineKind.Copy;
        }

        if (Has(type, "Compute") || Has(type, "Cuda"))
        {
            return GpuEngineKind.Compute;
        }

        if (Has(type, "3D") || Has(type, "Graphics"))
        {
            return GpuEngineKind.Graphics;
        }

        return null;

        static bool Has(string type, string token) => type.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>[S-1205] The process an engine instance belongs to (<c>pid_1234_...</c>), or null when it carries none.</summary>
    public static int? PidOf(string instance)
    {
        if (string.IsNullOrEmpty(instance) || !instance.StartsWith("pid_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var end = instance.IndexOf('_', 4);
        var digits = end < 0 ? instance[4..] : instance[4..end];

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ? pid : null;
    }

    /// <summary>
    /// [S-1203] One figure per engine kind the adapter has, in <see cref="GpuEngineKind"/> order.
    /// <para>
    /// Each engine's load is the sum across the processes using it (the instances are per process
    /// <i>and</i> per engine); a kind's load is its busiest engine, the way the adapter's load is
    /// its busiest engine overall. A kind is listed as soon as any instance of it exists, even at
    /// 0%, so the list does not grow and shrink as engines go idle. Junk readings count as nothing.
    /// </para>
    /// </summary>
    public static List<GpuEngineLoad> Loads(IEnumerable<(string Instance, double Percent)> engines, string luid)
    {
        ArgumentNullException.ThrowIfNull(engines);

        var perEngine = new Dictionary<string, (GpuEngineKind Kind, double Sum)>(StringComparer.Ordinal);

        foreach (var (instance, percent) in engines)
        {
            if (!string.Equals(GpuRules.LuidOf(instance), luid, StringComparison.OrdinalIgnoreCase)
                || KindOf(instance) is not { } kind)
            {
                continue;
            }

            var engine = GpuRules.EngineOf(instance) ?? kind.ToString();
            var add = double.IsFinite(percent) && percent > 0 ? percent : 0;
            var sum = perEngine.TryGetValue(engine, out var known) ? known.Sum : 0;
            perEngine[engine] = (kind, sum + add);
        }

        var perKind = new SortedDictionary<GpuEngineKind, double>();
        foreach (var (kind, sum) in perEngine.Values)
        {
            perKind[kind] = Math.Max(perKind.GetValueOrDefault(kind), Math.Clamp(sum, 0, 100));
        }

        return [.. perKind.Select(pair => new GpuEngineLoad(pair.Key, pair.Value))];
    }

    /// <summary>
    /// [S-1204] The video engines as the NVIDIA driver reports them, over the counters' figures: the
    /// driver's encoder and decoder load is the one NVIDIA's own tools show. A kind the counters did
    /// not list is added; a driver figure that is missing leaves the counter's figure alone.
    /// </summary>
    public static List<GpuEngineLoad> WithDriverVideo(IReadOnlyList<GpuEngineLoad> loads, double? encoder, double? decoder)
    {
        ArgumentNullException.ThrowIfNull(loads);

        var perKind = new SortedDictionary<GpuEngineKind, double>();
        foreach (var load in loads)
        {
            perKind[load.Kind] = load.Percent;
        }

        if (encoder is { } enc && double.IsFinite(enc))
        {
            perKind[GpuEngineKind.VideoEncode] = Math.Clamp(enc, 0, 100);
        }

        if (decoder is { } dec && double.IsFinite(dec))
        {
            perKind[GpuEngineKind.VideoDecode] = Math.Clamp(dec, 0, 100);
        }

        return [.. perKind.Select(pair => new GpuEngineLoad(pair.Key, pair.Value))];
    }

    /// <summary>
    /// [S-1205] The busiest processes on an adapter: each one's load is the busiest engine it uses
    /// (Task Manager's "GPU" column), busiest first, ties by pid, at most <paramref name="count"/>.
    /// Idle processes and the idle pseudo-process (pid 0) are left out.
    /// </summary>
    public static List<(int Pid, double Percent)> TopProcesses(IEnumerable<(string Instance, double Percent)> engines, string luid, int count)
    {
        ArgumentNullException.ThrowIfNull(engines);

        if (count <= 0)
        {
            return [];
        }

        var perProcess = new Dictionary<int, Dictionary<string, double>>();

        foreach (var (instance, percent) in engines)
        {
            if (!double.IsFinite(percent) || percent <= 0
                || PidOf(instance) is not { } pid || pid == 0
                || !string.Equals(GpuRules.LuidOf(instance), luid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!perProcess.TryGetValue(pid, out var perEngine))
            {
                perEngine = new Dictionary<string, double>(StringComparer.Ordinal);
                perProcess[pid] = perEngine;
            }

            var engine = GpuRules.EngineOf(instance) ?? string.Empty;
            perEngine[engine] = perEngine.GetValueOrDefault(engine) + percent;
        }

        return [.. perProcess
            .Select(pair => (Pid: pair.Key, Percent: Math.Clamp(pair.Value.Values.Max(), 0, 100)))
            .OrderByDescending(item => item.Percent)
            .ThenBy(item => item.Pid)
            .Take(count)];
    }

    /// <summary>[S-1203] An engine kind in words, as a row names it.</summary>
    public static string Label(GpuEngineKind kind) => Label(Strings.Current, kind);

    /// <summary><see cref="Label(GpuEngineKind)"/> in <paramref name="locale"/>.</summary>
    public static string Label(Locale locale, GpuEngineKind kind) => kind switch
    {
        GpuEngineKind.Graphics => Strings.Get(locale, Str.WidgetsB_Gpu_Engine3d),
        GpuEngineKind.Compute => Strings.Get(locale, Str.WidgetsB_Gpu_EngineCompute),
        GpuEngineKind.Copy => Strings.Get(locale, Str.WidgetsB_Gpu_EngineCopy),
        GpuEngineKind.VideoDecode => Strings.Get(locale, Str.WidgetsB_Gpu_EngineVideoDecode),
        GpuEngineKind.VideoEncode => Strings.Get(locale, Str.WidgetsB_Gpu_EngineVideoEncode),
        GpuEngineKind.VideoProcessing => Strings.Get(locale, Str.WidgetsB_Gpu_EngineVideoProcessing),
        _ => kind.ToString(),
    };

    /// <summary>
    /// [S-1206] The words beside the load: which engine kind is busiest ("3D"), and on a large tile
    /// that engine's own figure to one decimal ("3D 12.4%"). The engine's figure, not the load's: on an
    /// NVIDIA card the load is the driver's, which can differ from any one counter, and a caption that
    /// put the driver's number next to a counter's engine would claim the engine is that busy. Empty
    /// when no engine is doing anything — naming the busiest of five idle engines would be noise.
    /// </summary>
    public static string Caption(IReadOnlyList<GpuEngineLoad> loads, bool precise) => Caption(Strings.Current, loads, precise);

    /// <summary><see cref="Caption(IReadOnlyList{GpuEngineLoad}, bool)"/> in <paramref name="locale"/>.</summary>
    public static string Caption(Locale locale, IReadOnlyList<GpuEngineLoad> loads, bool precise)
    {
        ArgumentNullException.ThrowIfNull(loads);

        GpuEngineLoad? busiest = null;
        foreach (var load in loads)
        {
            if (load.Percent > 0 && (busiest is null || load.Percent > busiest.Value.Percent))
            {
                busiest = load;
            }
        }

        if (busiest is not { } b)
        {
            return string.Empty;
        }

        return precise
            ? Strings.Format(locale, Str.WidgetsB_Gpu_EngineLoad, ("engine", Label(locale, b.Kind)), ("percent", LocaleFormat.Percent(locale, Math.Clamp(b.Percent, 0, 100), 1)))
            : Label(locale, b.Kind);
    }

    /// <summary>The widest caption, in characters, so the layout does not hop as the busiest engine changes.</summary>
    public static int CaptionCharacters(bool precise) => CaptionCharacters(Strings.Current, precise);

    /// <summary><see cref="CaptionCharacters(bool)"/> in <paramref name="locale"/>: the longest engine name, with its widest figure when precise.</summary>
    public static int CaptionCharacters(Locale locale, bool precise)
    {
        var widest = 0;
        foreach (var kind in Enum.GetValues<GpuEngineKind>())
        {
            var label = Label(locale, kind);
            var text = precise
                ? Strings.Format(locale, Str.WidgetsB_Gpu_EngineLoad, ("engine", label), ("percent", LocaleFormat.Percent(locale, 100, 1)))
                : label;
            widest = Math.Max(widest, text.Length);
        }

        return widest;
    }
}
