#[derive(serde::Serialize, Clone)]
pub struct BatteryStats {
    pub percent: u8,
    pub is_charging: bool,
    pub is_on_ac: bool,
    pub seconds_remaining: Option<i32>,
    pub has_battery: bool,
    /// Battery design capacity in mWh (None if unavailable).
    pub design_capacity_mwh: Option<u32>,
    /// Last-known full charge capacity (mWh).
    pub full_charge_capacity_mwh: Option<u32>,
    /// Health = full / design * 100. None if either capacity is unknown.
    pub health_pct: Option<f32>,
    /// Cycle count (None if firmware doesn't expose it).
    pub cycle_count: Option<u32>,
    /// Battery voltage (mV).
    pub voltage_mv: Option<u32>,
    /// Battery temperature (°C).
    pub temp_c: Option<f32>,
}

#[cfg(target_os = "windows")]
mod win_wmi {
    use serde::Deserialize;
    use wmi::{COMLibrary, WMIConnection};

    #[derive(Deserialize, Debug)]
    #[serde(rename = "BatteryStaticData")]
    #[serde(rename_all = "PascalCase")]
    struct BatteryStaticData {
        instance_name: Option<String>,
        designed_capacity: Option<u32>,
    }

    #[derive(Deserialize, Debug)]
    #[serde(rename = "BatteryFullChargedCapacity")]
    #[serde(rename_all = "PascalCase")]
    struct BatteryFullChargedCapacity {
        instance_name: Option<String>,
        full_charged_capacity: Option<u32>,
    }

    #[derive(Deserialize, Debug)]
    #[serde(rename = "BatteryCycleCount")]
    #[serde(rename_all = "PascalCase")]
    struct BatteryCycleCount {
        cycle_count: Option<u32>,
    }

    #[derive(Deserialize, Debug)]
    #[serde(rename = "BatteryStatus")]
    #[serde(rename_all = "PascalCase")]
    struct BatteryStatus {
        voltage: Option<u32>,
    }

    #[derive(Deserialize, Debug)]
    #[serde(rename = "BatteryTemperature")]
    #[serde(rename_all = "PascalCase")]
    struct BatteryTemperature {
        // Reported in tenths of a Kelvin.
        temperature: Option<u32>,
    }

    pub struct ExtraBattery {
        pub design_capacity_mwh: Option<u32>,
        pub full_charge_capacity_mwh: Option<u32>,
        pub cycle_count: Option<u32>,
        pub voltage_mv: Option<u32>,
        pub temp_c: Option<f32>,
    }

    /// Correlate design + full-charge capacities to a SINGLE battery instance.
    ///
    /// On a multi-battery machine each WMI class returns one row per battery,
    /// and the rows are not guaranteed to be in the same order across classes.
    /// Pairing `design.first()` with `full.first()` can therefore mix the design
    /// capacity of battery A with the full-charge capacity of battery B and
    /// produce a nonsense health figure. We pair the two capacities on a shared
    /// `InstanceName` and return the design/full of one matched instance.
    ///
    /// Inputs are `(instance_name, capacity)` pairs (the same data
    /// `BatteryStaticData` / `BatteryFullChargedCapacity` carry) so this is a
    /// pure function and unit-testable. Returns `(design, full)`, each `Some`
    /// only when it belongs to the chosen instance.
    pub fn correlate_capacities(
        design: &[(Option<String>, Option<u32>)],
        full: &[(Option<String>, Option<u32>)],
    ) -> (Option<u32>, Option<u32>) {
        // Prefer an instance present in BOTH classes with both capacities > 0,
        // so design/full describe the same physical battery.
        for (d_name, d_cap) in design {
            let d_cap = match d_cap.filter(|&v| v > 0) {
                Some(v) => v,
                None => continue,
            };
            let name: &str = match d_name {
                Some(n) => n.as_str(),
                None => continue,
            };
            if let Some((_, f_cap)) = full
                .iter()
                .find(|(f_name, f_cap)| f_name.as_deref() == Some(name) && f_cap.unwrap_or(0) > 0)
            {
                // f_cap is guaranteed Some(>0) by the find predicate.
                return (Some(d_cap), *f_cap);
            }
        }

        // No correlated pair (single-battery firmware without InstanceName, or
        // one class missing). Fall back to the first valid value of each class
        // independently: the common single-battery case stays correct, and a
        // multi-battery machine that doesn't expose InstanceName is no worse
        // than before.
        let design_fallback = design.iter().find_map(|(_, c)| c.filter(|&v| v > 0));
        let full_fallback = full.iter().find_map(|(_, c)| c.filter(|&v| v > 0));
        (design_fallback, full_fallback)
    }

    pub fn fetch() -> ExtraBattery {
        let blank = ExtraBattery {
            design_capacity_mwh: None,
            full_charge_capacity_mwh: None,
            cycle_count: None,
            voltage_mv: None,
            temp_c: None,
        };

        let com = match COMLibrary::new() {
            Ok(c) => c,
            Err(_) => return blank,
        };
        let wmi = match WMIConnection::with_namespace_path("root\\wmi", com) {
            Ok(w) => w,
            Err(_) => return blank,
        };

        // ignore: each WMI class is best-effort extra telemetry; a query Err
        // (class missing on this firmware, WMI hiccup) is degraded to "no rows"
        // so the metric surfaces as None. No corrupt/absent distinction exists
        // here (WMI returns rows or an Err), and the `log` crate is not a
        // dependency of this crate, so we cannot surface it via a logger.
        let design: Vec<BatteryStaticData> = wmi.query().unwrap_or_else(|_| Vec::new());
        let full:   Vec<BatteryFullChargedCapacity> = wmi.query().unwrap_or_else(|_| Vec::new());
        let cycles: Vec<BatteryCycleCount> = wmi.query().unwrap_or_else(|_| Vec::new());
        let status: Vec<BatteryStatus> = wmi.query().unwrap_or_else(|_| Vec::new());
        let temp:   Vec<BatteryTemperature> = wmi.query().unwrap_or_else(|_| Vec::new());

        let design_pairs: Vec<(Option<String>, Option<u32>)> = design
            .into_iter()
            .map(|d| (d.instance_name, d.designed_capacity))
            .collect();
        let full_pairs: Vec<(Option<String>, Option<u32>)> = full
            .into_iter()
            .map(|f| (f.instance_name, f.full_charged_capacity))
            .collect();
        let (design_capacity_mwh, full_charge_capacity_mwh) =
            correlate_capacities(&design_pairs, &full_pairs);

        let cycle_count = cycles.first().and_then(|c| c.cycle_count).filter(|&v| v > 0);
        let voltage_mv  = status.first().and_then(|s| s.voltage).filter(|&v| v > 0);
        let temp_c = temp
            .first()
            .and_then(|t| t.temperature)
            .filter(|&v| v > 1000) // sanity: must be > 100 K
            .map(|kelvin_tenths| kelvin_tenths as f32 / 10.0 - 273.15);

        ExtraBattery {
            design_capacity_mwh,
            full_charge_capacity_mwh,
            cycle_count,
            voltage_mv,
            temp_c,
        }
    }
}

/// Health = full / design * 100, only when both belong to the SAME battery
/// instance and design is non-zero. Pure so it is unit-testable.
pub fn compute_health_pct(design_mwh: Option<u32>, full_mwh: Option<u32>) -> Option<f32> {
    match (design_mwh, full_mwh) {
        (Some(d), Some(f)) if d > 0 => Some((f as f32 / d as f32) * 100.0),
        _ => None,
    }
}

pub fn get_battery_stats_inner() -> BatteryStats {
    #[cfg(target_os = "windows")]
    {
        use winapi::um::winbase::{GetSystemPowerStatus, SYSTEM_POWER_STATUS};
        use std::mem::zeroed;

        let mut status: SYSTEM_POWER_STATUS = unsafe { zeroed() };
        let ok = unsafe { GetSystemPowerStatus(&mut status) };

        if ok == 0 {
            return BatteryStats {
                percent: 0,
                is_charging: false,
                is_on_ac: false,
                seconds_remaining: None,
                has_battery: false,
                design_capacity_mwh: None,
                full_charge_capacity_mwh: None,
                health_pct: None,
                cycle_count: None,
                voltage_mv: None,
                temp_c: None,
            };
        }

        let has_battery = status.BatteryFlag != 128; // 128 = no system battery
        let percent = if status.BatteryLifePercent == 255 { 0 } else { status.BatteryLifePercent };
        let is_charging = status.BatteryFlag & 8 != 0; // 8 = charging
        let is_on_ac = status.ACLineStatus == 1;
        let seconds_remaining = if status.BatteryLifeTime == u32::MAX {
            None
        } else {
            Some(status.BatteryLifeTime as i32)
        };

        let extra = if has_battery { win_wmi::fetch() } else {
            win_wmi::ExtraBattery {
                design_capacity_mwh: None,
                full_charge_capacity_mwh: None,
                cycle_count: None,
                voltage_mv: None,
                temp_c: None,
            }
        };

        let health_pct =
            compute_health_pct(extra.design_capacity_mwh, extra.full_charge_capacity_mwh);

        BatteryStats {
            percent,
            is_charging,
            is_on_ac,
            seconds_remaining,
            has_battery,
            design_capacity_mwh: extra.design_capacity_mwh,
            full_charge_capacity_mwh: extra.full_charge_capacity_mwh,
            health_pct,
            cycle_count: extra.cycle_count,
            voltage_mv: extra.voltage_mv,
            temp_c: extra.temp_c,
        }
    }

    #[cfg(not(target_os = "windows"))]
    BatteryStats {
        percent: 0,
        is_charging: false,
        is_on_ac: true,
        seconds_remaining: None,
        has_battery: false,
        design_capacity_mwh: None,
        full_charge_capacity_mwh: None,
        health_pct: None,
        cycle_count: None,
        voltage_mv: None,
        temp_c: None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn health_math_on_matched_instance() {
        // full/design * 100 when both present and design > 0.
        let h = compute_health_pct(Some(50_000), Some(45_000)).unwrap();
        assert!((h - 90.0).abs() < 1e-3, "expected ~90%, got {h}");
    }

    #[test]
    fn health_none_when_design_zero_or_missing() {
        assert_eq!(compute_health_pct(Some(0), Some(45_000)), None);
        assert_eq!(compute_health_pct(None, Some(45_000)), None);
        assert_eq!(compute_health_pct(Some(50_000), None), None);
    }

    #[cfg(target_os = "windows")]
    #[test]
    fn correlate_picks_same_instance_not_cross_battery() {
        use super::win_wmi::correlate_capacities;
        // Two batteries, classes returned in DIFFERENT order. The naive
        // first()+first() pairing would mix A's design (50000) with B's full
        // (20000) -> 40% nonsense. Correlation must keep A with A.
        let design = vec![
            (Some("BAT_A".to_string()), Some(50_000u32)),
            (Some("BAT_B".to_string()), Some(20_000u32)),
        ];
        let full = vec![
            (Some("BAT_B".to_string()), Some(18_000u32)),
            (Some("BAT_A".to_string()), Some(45_000u32)),
        ];
        let (d, f) = correlate_capacities(&design, &full);
        assert_eq!(d, Some(50_000));
        assert_eq!(f, Some(45_000));
        let h = compute_health_pct(d, f).unwrap();
        assert!((h - 90.0).abs() < 1e-3, "expected ~90%, got {h}");
    }

    #[cfg(target_os = "windows")]
    #[test]
    fn correlate_single_battery_without_instance_name_falls_back() {
        use super::win_wmi::correlate_capacities;
        let design = vec![(None, Some(50_000u32))];
        let full = vec![(None, Some(45_000u32))];
        let (d, f) = correlate_capacities(&design, &full);
        assert_eq!(d, Some(50_000));
        assert_eq!(f, Some(45_000));
    }

    #[cfg(target_os = "windows")]
    #[test]
    fn correlate_skips_zero_capacities() {
        use super::win_wmi::correlate_capacities;
        // Matched instance but its full-charge is 0 -> not a usable pair; fall
        // back to first valid value of each class independently.
        let design = vec![(Some("BAT_A".to_string()), Some(50_000u32))];
        let full = vec![(Some("BAT_A".to_string()), Some(0u32))];
        let (d, f) = correlate_capacities(&design, &full);
        assert_eq!(d, Some(50_000));
        assert_eq!(f, None);
    }
}
