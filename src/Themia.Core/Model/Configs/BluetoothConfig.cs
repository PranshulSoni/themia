using System.Text.Json.Serialization;

namespace Themia.Core.Model;

/// <summary>Placeholder: the real Bluetooth settings replace this file wholesale.</summary>
public sealed class BluetoothConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Bluetooth;

    public override WidgetConfig Clone() => new BluetoothConfig();
}
