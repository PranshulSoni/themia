using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>[S-607] Where a OneDrive widget starts: the files touched lately, or the top of the drive.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DriveStart>))]
public enum DriveStart
{
    Recent = 0,
    Root = 1,
}

/// <summary>[S-606] Files on a Microsoft account's OneDrive.</summary>
public sealed class OneDriveConfig : WidgetConfig
{
    public const int MinFiles = 5;

    public const int MaxFiles = 50;

    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.OneDrive;

    /// <summary>The Microsoft account, or empty for the first one signed in.</summary>
    public string AccountId { get; set; } = string.Empty;

    public DriveStart StartIn { get; set; } = DriveStart.Recent;

    /// <summary>How many items to load, 5 to 50.</summary>
    public int FileCount { get; set; } = 15;

    [JsonIgnore]
    public int EffectiveFileCount => Math.Clamp(FileCount, MinFiles, MaxFiles);

    /// <summary>[S-608] A drive changes slowly; every five minutes is plenty, and the widget is never the only way to see it.</summary>
    public static TimeSpan RefreshInterval => TimeSpan.FromMinutes(5);

    public override WidgetConfig Clone() => new OneDriveConfig
    {
        AccountId = AccountId,
        StartIn = StartIn,
        FileCount = FileCount,
    };
}
