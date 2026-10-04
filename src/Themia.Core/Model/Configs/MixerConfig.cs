using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>
/// [S-440] Per-application volume for the default output device.
/// <para>
/// No refresh interval, unlike the original: the list follows Core Audio's own notifications, so
/// there is no poll for a setting to tune.
/// </para>
/// </summary>
public sealed class MixerConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Mixer;

    /// <summary>[S-442] One row per application rather than one per audio session.</summary>
    public bool GroupByApp { get; set; } = true;

    /// <summary>[S-444] Pin the device's master level as the first row.</summary>
    public bool ShowMaster { get; set; } = true;

    /// <summary>[S-445] Include the Windows system-sounds row.</summary>
    public bool ShowSystemSounds { get; set; }

    /// <summary>[S-445] Only the apps playing right now.</summary>
    public bool OnlyActive { get; set; }

    /// <summary>[S-446] Each app's own icon rather than a generic glyph.</summary>
    public bool ShowIcons { get; set; } = true;

    public override WidgetConfig Clone() => new MixerConfig
    {
        GroupByApp = GroupByApp,
        ShowMaster = ShowMaster,
        ShowSystemSounds = ShowSystemSounds,
        OnlyActive = OnlyActive,
        ShowIcons = ShowIcons,
    };
}
