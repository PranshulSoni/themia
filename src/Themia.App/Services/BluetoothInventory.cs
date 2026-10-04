using Themia.Core.Diagnostics;
using Themia.Core.Widgets;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace Themia.App.Services;

/// <summary>What one enumeration found, and why it found nothing when it found nothing.</summary>
/// <param name="Status">The state to draw. Only <see cref="BluetoothStatus.Ok"/> has rows.</param>
/// <param name="Devices">
/// What the two enumerations returned, as they returned it. Unmerged and unordered on purpose:
/// folding the dual-mode duplicates and choosing the order are pure decisions, so they live in
/// <see cref="BluetoothRules.Arrange"/> where they can be tested without a radio.
/// </param>
public readonly record struct BluetoothSnapshot(BluetoothStatus Status, IReadOnlyList<BluetoothDeviceEntry> Devices)
{
    public static BluetoothSnapshot Of(BluetoothStatus status) => new(status, []);
}

/// <summary>
/// The paired Bluetooth devices, from the WinRT device enumeration.
/// <para>
/// Every call here is slow in the way a widget cannot afford on the UI thread: enumerating the
/// radios is an RPC, and a GATT battery read opens a BLE connection and waits on the device to
/// answer. So this is <c>async</c> end to end and is called from a background load with a
/// cancellation token, exactly as the folder listing is.
/// </para>
/// <para>
/// The distinction it exists to preserve is between <i>nothing found</i> and <i>nowhere to look</i>.
/// A desktop with no radio, a laptop with the radio switched off, and a machine with a radio and no
/// pairings are three different facts about the PC, and a widget that renders all three as an empty
/// list has thrown away the only useful thing it knew.
/// </para>
/// </summary>
internal static class BluetoothInventory
{
    private const string Scope = "bluetooth";

    /// <summary>The Aep property carrying whether a paired device is connected right now.</summary>
    private const string ConnectedProperty = "System.Devices.Aep.IsConnected";

    private static readonly string[] RequestedProperties = [ConnectedProperty];

    /// <summary>
    /// How long the battery pass may take in total.
    /// <para>
    /// A GATT read against a device that is connected but asleep sits until the stack gives up. One
    /// such device would otherwise hold the whole refresh, so the pass stops when the budget is
    /// gone and the devices it did not reach simply show no battery column — which is the same
    /// thing they show when they have no Battery Service, and is honest either way.
    /// </para>
    /// </summary>
    private static readonly TimeSpan BatteryBudget = TimeSpan.FromSeconds(8);

    /// <summary>Read the paired devices. Returns a named state rather than throwing.</summary>
    public static async Task<BluetoothSnapshot> ReadAsync(bool withBattery, CancellationToken token)
    {
        var radio = await RadioStateAsync(token).ConfigureAwait(false);

        if (radio != BluetoothStatus.Ok)
        {
            return BluetoothSnapshot.Of(radio);
        }

        try
        {
            var found = new List<BluetoothDeviceEntry>();

            found.AddRange(await EnumerateAsync(
                BluetoothDevice.GetDeviceSelectorFromPairingState(true), token).ConfigureAwait(false));

            found.AddRange(await EnumerateAsync(
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), token).ConfigureAwait(false));

            if (withBattery)
            {
                await AddBatteryLevelsAsync(found, token).ConfigureAwait(false);
            }

            return new BluetoothSnapshot(BluetoothStatus.Ok, found);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, "could not enumerate paired devices", ex);
            return BluetoothSnapshot.Of(BluetoothStatus.Failed);
        }
    }

    /// <summary>
    /// Whether there is a radio to ask at all, and whether it is on.
    /// <para>
    /// Asked before the enumeration rather than inferred from it: <c>FindAllAsync</c> returns an
    /// empty list for a machine with no radio, a machine with the radio off and a machine with
    /// nothing paired, and those need three different sentences.
    /// </para>
    /// </summary>
    private static async Task<BluetoothStatus> RadioStateAsync(CancellationToken token)
    {
        try
        {
            var access = await Radio.RequestAccessAsync().AsTask(token).ConfigureAwait(false);

            if (access is RadioAccessStatus.DeniedByUser or RadioAccessStatus.DeniedBySystem)
            {
                return BluetoothStatus.PermissionDenied;
            }

            var radios = await Radio.GetRadiosAsync().AsTask(token).ConfigureAwait(false);

            var bluetooth = radios.Where(radio => radio.Kind == RadioKind.Bluetooth).ToList();

            if (bluetooth.Count == 0)
            {
                return BluetoothStatus.NoRadio;
            }

            // Any radio being on is enough: a machine with two adapters can pair on either.
            return bluetooth.Any(radio => radio.State == RadioState.On)
                ? BluetoothStatus.Ok
                : BluetoothStatus.RadioOff;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The radio API is the part most likely to be unavailable on an older or locked-down
            // build. Falling through to the enumeration is better than refusing to show devices
            // that are plainly there; the enumeration's own result then names the state.
            Log.Warn(Scope, "could not read the radio state; enumerating anyway", ex);
            return BluetoothStatus.Ok;
        }
    }

    /// <summary>One selector's worth of devices, with no battery levels yet.</summary>
    private static async Task<List<BluetoothDeviceEntry>> EnumerateAsync(string selector, CancellationToken token)
    {
        var devices = new List<BluetoothDeviceEntry>();

        var found = await DeviceInformation
            .FindAllAsync(selector, RequestedProperties)
            .AsTask(token)
            .ConfigureAwait(false);

        foreach (var info in found)
        {
            token.ThrowIfCancellationRequested();

            var connected = info.Properties.TryGetValue(ConnectedProperty, out var value)
                && value is bool flag
                && flag;

            // The name is the device's own advertisement, so it is normalised by the display rule
            // and then only ever drawn.
            devices.Add(new BluetoothDeviceEntry(
                BluetoothRules.DisplayName(info.Name),
                info.Id,
                connected,
                BluetoothRules.Classify(info.Name),
                null));
        }

        return devices;
    }

    /// <summary>
    /// Fill in the battery level for the connected devices, within a budget.
    /// <para>
    /// Connected only. A disconnected device's GATT read does not fail — it waits for the radio to
    /// find the device, which takes the better part of ten seconds per device and would turn a
    /// refresh into a minute of blocked background work for a list nobody can act on anyway.
    /// </para>
    /// </summary>
    private static async Task AddBatteryLevelsAsync(List<BluetoothDeviceEntry> devices, CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow + BatteryBudget;

        for (var i = 0; i < devices.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            if (!devices[i].Connected)
            {
                continue;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                Log.Debug(Scope, "battery pass ran out of budget; the rest of the list shows no battery");
                return;
            }

            var percent = await BatteryPercentAsync(devices[i].Id, token).ConfigureAwait(false);

            if (percent is not null)
            {
                devices[i] = devices[i] with { BatteryPercent = percent };
            }
        }
    }

    /// <summary>
    /// The GATT Battery Service's level characteristic, if the device has one.
    /// <para>
    /// Both the device and the service hold OS handles and a live BLE connection, and this runs on
    /// a poll, so each is disposed on every path — an early return or a device that answers with a
    /// failure status leaks exactly as fast as one that succeeds.
    /// </para>
    /// </summary>
    private static async Task<int?> BatteryPercentAsync(string id, CancellationToken token)
    {
        BluetoothLEDevice? device = null;
        GattDeviceService? service = null;

        try
        {
            device = await BluetoothLEDevice.FromIdAsync(id).AsTask(token).ConfigureAwait(false);

            if (device is null)
            {
                // Not an LE device. Classic devices report battery over HFP, which Windows does not
                // expose, so there is genuinely nothing to read.
                return null;
            }

            var services = await device
                .GetGattServicesForUuidAsync(GattServiceUuids.Battery)
                .AsTask(token)
                .ConfigureAwait(false);

            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
            {
                return null;
            }

            service = services.Services[0];

            var characteristics = await service
                .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel)
                .AsTask(token)
                .ConfigureAwait(false);

            if (characteristics.Status != GattCommunicationStatus.Success
                || characteristics.Characteristics.Count == 0)
            {
                return null;
            }

            var read = await characteristics.Characteristics[0]
                .ReadValueAsync()
                .AsTask(token)
                .ConfigureAwait(false);

            if (read.Status != GattCommunicationStatus.Success || read.Value is null || read.Value.Length == 0)
            {
                return null;
            }

            using var reader = DataReader.FromBuffer(read.Value);
            return reader.ReadByte();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One device that will not answer must not cost the rest of the list its battery
            // column. Debug rather than warn: a sleeping peripheral is routine.
            Log.Debug(Scope, $"no battery from a paired device: {ex.GetType().Name}");
            return null;
        }
        finally
        {
            // Dispose is Close() through the projection: the BLE connection and the OS handle both
            // go back, on the success path and on every failure path.
            service?.Dispose();
            device?.Dispose();
        }
    }
}
