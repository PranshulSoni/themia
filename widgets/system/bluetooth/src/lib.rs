use serde::Serialize;

#[derive(Serialize, Clone)]
pub struct BluetoothDevice {
    pub name: String,
    pub id: String,
    pub is_connected: bool,
    pub device_type: String,
    /// Device battery percentage (read via GATT Battery Service 0x180F).
    /// None for non-LE devices or when the device doesn't expose the service.
    pub battery_pct: Option<u8>,
    /// Received Signal Strength Indicator (dBm). None for paired classic devices.
    pub rssi: Option<i16>,
}

#[cfg(target_os = "windows")]
mod win {
    use super::*;
    use windows::core::Interface;
    use windows::Devices::Bluetooth::BluetoothDevice as WinBtDevice;
    use windows::Devices::Bluetooth::BluetoothLEDevice;
    use windows::Devices::Bluetooth::GenericAttributeProfile::{
        GattCharacteristicUuids, GattCommunicationStatus, GattServiceUuids,
    };
    use windows::Devices::Enumeration::DeviceInformation;
    use windows::Storage::Streams::DataReader;

    fn classify_device(name: &str) -> String {
        let lower = name.to_lowercase();
        if lower.contains("headphone") || lower.contains("headset")
            || lower.contains("speaker") || lower.contains("earbuds")
            || lower.contains("airpod") || lower.contains("buds")
            || lower.contains("audio") || lower.contains("soundbar")
        {
            return "audio".into();
        }
        if lower.contains("mouse") || lower.contains("trackpad") {
            return "mouse".into();
        }
        if lower.contains("keyboard") || lower.contains("keychron") {
            return "keyboard".into();
        }
        if lower.contains("phone") || lower.contains("iphone")
            || lower.contains("galaxy") || lower.contains("pixel")
        {
            return "phone".into();
        }
        if lower.contains("controller") || lower.contains("gamepad")
            || lower.contains("xbox") || lower.contains("dualsense")
        {
            return "gamepad".into();
        }
        "other".into()
    }

    fn read_is_connected(dev_info: &DeviceInformation) -> bool {
        dev_info
            .Properties()
            .ok()
            .and_then(|props| {
                props
                    .Lookup(&windows::core::HSTRING::from(
                        "System.Devices.Aep.IsConnected",
                    ))
                    .ok()
            })
            .and_then(|val: windows::core::IInspectable| {
                use windows::Foundation::IPropertyValue;
                val.cast::<IPropertyValue>().ok()?.GetBoolean().ok()
            })
            .unwrap_or(false)
    }

    /// Best-effort GATT Battery Service (0x180F) read. Returns None for any
    /// device that isn't connected, isn't LE, or doesn't expose the service.
    fn read_battery_pct(id: &str) -> Option<u8> {
        let hid = windows::core::HSTRING::from(id);
        let le_dev = BluetoothLEDevice::FromIdAsync(&hid).ok()?.get().ok()?;

        // The GATT service holds a BLE connection; both it and the device own
        // OS handles that must be Close()d on EVERY exit path (the widget polls
        // periodically, so a leak here grows the handle count / exhausts BLE
        // connections over time). Acquire the optional GattDeviceService first,
        // then run the read in a closure whose result we capture before closing
        // both handles, covering early-return / error paths uniformly.
        let svc = read_battery_service(&le_dev);
        let result = svc
            .as_ref()
            .and_then(read_battery_pct_from_service);

        if let Some(svc) = svc.as_ref() {
            let _ = svc.Close(); // ignore: best-effort handle release on poll path
        }
        let _ = le_dev.Close(); // ignore: best-effort handle release on poll path

        result
    }

    /// Resolve the GATT Battery Service (0x180F) for an LE device, if present.
    /// Returns the open service handle; the caller owns closing it.
    fn read_battery_service(
        le_dev: &BluetoothLEDevice,
    ) -> Option<windows::Devices::Bluetooth::GenericAttributeProfile::GattDeviceService> {
        let battery_uuid = GattServiceUuids::Battery().ok()?;
        let services_op = le_dev.GetGattServicesForUuidAsync(battery_uuid).ok()?;
        let services_res = services_op.get().ok()?;
        if services_res.Status().ok()? != GattCommunicationStatus::Success {
            return None;
        }
        let svcs = services_res.Services().ok()?;
        if svcs.Size().ok()? == 0 {
            return None;
        }
        svcs.GetAt(0).ok()
    }

    /// Read the Battery Level characteristic from an already-open service.
    fn read_battery_pct_from_service(
        svc: &windows::Devices::Bluetooth::GenericAttributeProfile::GattDeviceService,
    ) -> Option<u8> {
        let char_uuid = GattCharacteristicUuids::BatteryLevel().ok()?;
        let chars_op = svc.GetCharacteristicsForUuidAsync(char_uuid).ok()?;
        let chars_res = chars_op.get().ok()?;
        if chars_res.Status().ok()? != GattCommunicationStatus::Success {
            return None;
        }
        let chars = chars_res.Characteristics().ok()?;
        if chars.Size().ok()? == 0 {
            return None;
        }
        let ch = chars.GetAt(0).ok()?;

        let read_op = ch.ReadValueAsync().ok()?;
        let read_res = read_op.get().ok()?;
        if read_res.Status().ok()? != GattCommunicationStatus::Success {
            return None;
        }
        let buf = read_res.Value().ok()?;
        let reader = DataReader::FromBuffer(&buf).ok()?;
        reader.ReadByte().ok()
    }

    fn enumerate(selector: &windows::core::HSTRING) -> Vec<BluetoothDevice> {
        let devices = match DeviceInformation::FindAllAsyncAqsFilter(selector) {
            Ok(op) => match op.get() {
                Ok(d) => d,
                Err(_) => return vec![],
            },
            Err(_) => return vec![],
        };

        let mut out = Vec::new();
        if let Ok(count) = devices.Size() {
            for i in 0..count {
                let Ok(dev_info) = devices.GetAt(i) else { continue };
                let name = dev_info.Name().map(|n| n.to_string_lossy()).unwrap_or_default();
                let id = dev_info.Id().map(|n| n.to_string_lossy()).unwrap_or_default();
                let is_connected = read_is_connected(&dev_info);
                let device_type = classify_device(&name);

                // Only try GATT read for connected devices: disconnected reads
                // hang for the BLE radio scan timeout (~5–10 s each).
                let battery_pct = if is_connected {
                    read_battery_pct(&id)
                } else {
                    None
                };

                out.push(BluetoothDevice {
                    name,
                    id,
                    is_connected,
                    device_type,
                    battery_pct,
                    rssi: None,
                });
            }
        }
        out
    }

    pub fn get_bluetooth_devices_inner() -> Vec<BluetoothDevice> {
        let classic_sel = WinBtDevice::GetDeviceSelectorFromPairingState(true).ok();
        let le_sel = BluetoothLEDevice::GetDeviceSelectorFromPairingState(true).ok();

        let mut results: Vec<BluetoothDevice> = Vec::new();
        if let Some(sel) = classic_sel.as_ref() { results.extend(enumerate(sel)); }
        if let Some(sel) = le_sel.as_ref() {
            // Merge LE results: if a device with the same name exists in classic
            // list, prefer the LE entry (it carries GATT battery). Otherwise append.
            for le in enumerate(sel) {
                if let Some(existing) = results.iter_mut().find(|d| d.name == le.name && !d.name.is_empty()) {
                    if le.battery_pct.is_some() { existing.battery_pct = le.battery_pct; }
                    if le.rssi.is_some()        { existing.rssi = le.rssi; }
                } else {
                    results.push(le);
                }
            }
        }

        results.sort_by(|a, b| b.is_connected.cmp(&a.is_connected));
        results
    }
}

#[cfg(target_os = "windows")]
pub fn get_bluetooth_devices_inner() -> Vec<BluetoothDevice> {
    win::get_bluetooth_devices_inner()
}

#[cfg(not(target_os = "windows"))]
pub fn get_bluetooth_devices_inner() -> Vec<BluetoothDevice> {
    vec![]
}
