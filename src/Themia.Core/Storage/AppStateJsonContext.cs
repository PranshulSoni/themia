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
[JsonSerializable(typeof(ProcessesConfig))]
[JsonSerializable(typeof(NetworkConfig))]
[JsonSerializable(typeof(PingConfig))]
[JsonSerializable(typeof(ContactsConfig))]
[JsonSerializable(typeof(CalendarConfig))]
[JsonSerializable(typeof(TasksConfig))]
[JsonSerializable(typeof(NotesConfig))]
[JsonSerializable(typeof(CountdownConfig))]
[JsonSerializable(typeof(PomodoroConfig))]
[JsonSerializable(typeof(CalculatorConfig))]
[JsonSerializable(typeof(SunMoonConfig))]
[JsonSerializable(typeof(DesignState))]
internal sealed partial class AppStateJsonContext : JsonSerializerContext;
