using System.Globalization;
using System.Text.Json;

namespace Themia.Core.Widgets;

/// <summary>What a hardware monitor said about the processor: either figure may be missing.</summary>
public readonly record struct SensorReadout(double? Celsius, double? FanRpm);

/// <summary>
/// [S-1146] [S-1147] Reads LibreHardwareMonitor's (or OpenHardwareMonitor's) sensor tree, as its
/// local web server serves it at <c>/data.json</c>, for the processor temperature and fan.
/// <para>
/// The original read the same sensors over WMI, which Native AOT cannot use. The web server is the
/// other door the monitor offers onto the same tree: opt-in (the user runs the monitor and turns
/// its server on), local only, and the monitor's own signed driver does the hardware access, so
/// Themia ships none. It is the only source of a fan speed on Windows, and the only source of the
/// on-die temperature on the AMD boards whose ACPI thermal zone is missing.
/// </para>
/// <para>
/// The tree is nodes of <c>{ Text, Value, ImageURL, Children }</c>. Sensors are leaves whose value
/// carries its unit ("45.0 °C", "1180 RPM"), formatted in the monitor machine's culture, so a decimal
/// comma is accepted. A processor is a hardware node whose icon is the CPU icon.
/// </para>
/// </summary>
public static class HardwareMonitor
{
    /// <summary>Deeper than any real tree; a document nested past this is not one.</summary>
    private const int MaxDepth = 64;

    /// <summary>
    /// The processor temperature and fan in <paramref name="json"/>; both null when the document is
    /// not a sensor tree. Never throws for a malformed document: it is data from another program.
    /// </summary>
    public static SensorReadout Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
            var temperatures = new List<(string Name, double Celsius, bool InCpu)>();
            var fans = new List<(string Name, double Rpm)>();

            Walk(document.RootElement, inCpu: false, temperatures, fans);
            return new SensorReadout(PickTemperature(temperatures), PickFan(fans));
        }
        catch (JsonException)
        {
            return default; // ignore: not a sensor tree; the caller shows N/A
        }
    }

    private static void Walk(
        JsonElement node,
        bool inCpu,
        List<(string Name, double Celsius, bool InCpu)> temperatures,
        List<(string Name, double Rpm)> fans)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var text = String(node, "Text") ?? string.Empty;
        var image = String(node, "ImageURL") ?? string.Empty;
        var cpu = inCpu || image.Contains("cpu", StringComparison.OrdinalIgnoreCase);

        if (String(node, "Value") is { } value)
        {
            if (Number(value, "°C") is { } celsius)
            {
                temperatures.Add((text, celsius, cpu));
            }
            else if (Number(value, "RPM") is { } rpm)
            {
                fans.Add((text, rpm));
            }
        }

        if (node.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                Walk(child, cpu, temperatures, fans);
            }
        }
    }

    /// <summary>
    /// The processor's package / Tctl / Tdie reading when there is one, else its hottest sensor.
    /// Only when no processor node reports does a sensor elsewhere count, and then only one named
    /// for the processor (a board's "CPU" probe) — a graphics card's "GPU Core" is not one.
    /// Implausible readings (a disconnected probe reading 0 or 255) are dropped first.
    /// </summary>
    private static double? PickTemperature(List<(string Name, double Celsius, bool InCpu)> readings)
    {
        var inside = readings.Where(r => r.InCpu && CpuRules.Plausible(r.Celsius)).ToList();

        if (inside.Count > 0)
        {
            var package = inside.Where(r => IsPackage(r.Name)).ToList();
            return (package.Count > 0 ? package : inside).Max(r => r.Celsius);
        }

        var named = readings.Where(r => !r.InCpu && CpuRules.Plausible(r.Celsius) && NamesCpu(r.Name)).ToList();
        return named.Count > 0 ? named.Max(r => r.Celsius) : null;
    }

    /// <summary>The fastest plausible fan named for the processor.</summary>
    private static double? PickFan(List<(string Name, double Rpm)> fans)
    {
        double? fastest = null;

        foreach (var (name, rpm) in fans)
        {
            if (NamesCpu(name) && rpm >= CpuRules.MinRpm && rpm <= CpuRules.MaxRpm && (fastest is null || rpm > fastest))
            {
                fastest = rpm;
            }
        }

        return fastest;
    }

    private static bool NamesCpu(string name)
        => name.Contains("cpu", StringComparison.OrdinalIgnoreCase)
        || name.Contains("tctl", StringComparison.OrdinalIgnoreCase)
        || name.Contains("tdie", StringComparison.OrdinalIgnoreCase);

    private static bool IsPackage(string name)
        => name.Contains("package", StringComparison.OrdinalIgnoreCase)
        || name.Contains("tctl", StringComparison.OrdinalIgnoreCase)
        || name.Contains("tdie", StringComparison.OrdinalIgnoreCase);

    private static string? String(JsonElement node, string name)
        => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>"45,5 °C" → 45.5 when the value ends in <paramref name="unit"/>; null otherwise.</summary>
    public static double? Number(string value, string unit)
    {
        var trimmed = value.Trim();

        if (!trimmed.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var digits = trimmed[..^unit.Length].Trim().Replace(',', '.');

        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? number
            : null;
    }
}
