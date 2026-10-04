using System.Globalization;
using System.Text;

namespace Themia.Core.Motion;

/// <summary>What a hacker tile draws. A closed set, so the tile's dispatch is exhaustive.</summary>
public enum HackerPanel
{
    /// <summary>A scrolling exploit / packet log, or a memory hex dump.</summary>
    Console,

    /// <summary>A rotating wireframe protein.</summary>
    Molecule,

    /// <summary>A CCTV feed with a tracking reticle and REC.</summary>
    Camera,

    /// <summary>A sweeping scope with rotating wireframe polygons.</summary>
    Radar,

    /// <summary>An oscilloscope.</summary>
    Wave,
}

/// <summary>One tile's panel and its corner tag.</summary>
public readonly record struct HackerVariant(HackerPanel Panel, string Tag, bool Hex = false);

/// <summary>When a tile stops covering its widget and when it is gone, in ms from the start of the intro.</summary>
public readonly record struct HackerTiming(double RevealMs, double FadeMs)
{
    public double EndMs => RevealMs + FadeMs;
}

/// <summary>
/// [S-1007] The hacker intro: each widget is hacked into existence in its own tile — an opaque,
/// green-phosphor panel that covers the live widget, churns, then fades to reveal it. Per widget,
/// not an overlay, so it plays on every armed moment (startup, view change, unlock) and needs no
/// screen geometry.
/// </summary>
public static class HackerIntro
{
    /// <summary>The share of the intro the tile spends covering the widget; the rest is its fade.</summary>
    public const double CoverFraction = 0.65;

    /// <summary>
    /// Ordered so no two neighbours — including the wrap — share a panel, so a row of tiles reads as
    /// different feeds rather than a repeat. The two consoles sit apart.
    /// </summary>
    public static IReadOnlyList<HackerVariant> Variants { get; } =
    [
        new(HackerPanel.Console, "NET"),
        new(HackerPanel.Molecule, "SEQ"),
        new(HackerPanel.Camera, "CAM"),
        new(HackerPanel.Console, "MEM", Hex: true),
        new(HackerPanel.Radar, "TRACE"),
        new(HackerPanel.Wave, "SIG"),
    ];

    /// <summary>The panel for the widget at <paramref name="index"/>, cycling <see cref="Variants"/>.</summary>
    public static HackerVariant VariantFor(int index)
    {
        var n = Variants.Count;
        return Variants[((index % n) + n) % n];
    }

    /// <summary>
    /// [S-1007] A tile's timing for an intro of <paramref name="introMs"/>: it covers for 65% and fades
    /// over the rest, so cover + fade is the configured length exactly, and the cascade is added to the
    /// reveal — the first widget is done at exactly <paramref name="introMs"/>.
    /// </summary>
    public static HackerTiming TimingFor(double introMs, int index)
    {
        var d = double.IsFinite(introMs) ? Math.Max(1, introMs) : IntroMotion.DefaultMs;
        var cover = Math.Round(d * CoverFraction);
        var stagger = Math.Min(Math.Max(0, index) * IntroMotion.StaggerStepMs, IntroMotion.StaggerCapMs);
        return new HackerTiming(cover + stagger, d - cover);
    }

    private static readonly string[] Verbs =
    [
        "SYN", "ACK", "probe", "inject", "handshake", "decrypt", "bypass", "escalate", "sniff", "route",
        "spoof", "tunnel", "fuzz", "patch", "dump", "crack",
    ];

    private static readonly string[] Targets =
    [
        "kernel32", "gateway", "auth.svc", "mainframe", "firewall", "proxy:8080", "rootfs", "socket",
        "daemon", "vault", "relay", "sat-uplink",
    ];

    /// <summary>
    /// The console's lines, deterministic per seed so a render is repeatable: a fake log of
    /// "exploit" steps, or a memory dump of address + 8 hex bytes + ASCII.
    /// </summary>
    public static IReadOnlyList<string> ConsoleLines(int seed, int count, bool hex)
    {
        var random = new Random(seed);
        var lines = new List<string>(Math.Max(0, count));
        var address = 0x7FF6_A000 + (random.Next(0, 0x400) * 0x10);

        for (var i = 0; i < count; i++)
        {
            if (hex)
            {
                var bytes = new byte[8];
                random.NextBytes(bytes);
                var text = new StringBuilder();
                text.Append(CultureInfo.InvariantCulture, $"{address + (i * 8):X8}  ");
                foreach (var b in bytes)
                {
                    text.Append(CultureInfo.InvariantCulture, $"{b:X2} ");
                }

                text.Append(' ');
                foreach (var b in bytes)
                {
                    text.Append(b is >= 0x21 and < 0x7F ? (char)b : '.');
                }

                lines.Add(text.ToString());
                continue;
            }

            var verb = Verbs[random.Next(Verbs.Length)];
            var target = Targets[random.Next(Targets.Length)];
            var ip = string.Create(CultureInfo.InvariantCulture, $"10.{random.Next(256)}.{random.Next(256)}.{random.Next(256)}");
            var status = random.Next(5) == 0 ? "[!!]" : "[ok]";
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{status} {verb} {target} {ip} 0x{random.Next(0x10000):x4}"));
        }

        return lines;
    }
}
