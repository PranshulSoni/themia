using System.Text.Json;
using System.Text.Json.Serialization;
using Themia.Core.Model;

namespace Themia.Core.Storage;

/// <summary>
/// Source-generated serialization metadata. Required for Native AOT: reflection-based
/// <see cref="JsonSerializer"/> overloads are trimmed away, so every persisted type is declared here.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppState))]
[JsonSerializable(typeof(ViewState))]
[JsonSerializable(typeof(WidgetInstance))]
[JsonSerializable(typeof(WidgetConfig))]
[JsonSerializable(typeof(ClockConfig))]
[JsonSerializable(typeof(CpuConfig))]
[JsonSerializable(typeof(MemoryConfig))]
[JsonSerializable(typeof(DiskConfig))]
[JsonSerializable(typeof(BatteryConfig))]
[JsonSerializable(typeof(EmailConfig))]
[JsonSerializable(typeof(DesignState))]
internal sealed partial class AppStateJsonContext : JsonSerializerContext;
