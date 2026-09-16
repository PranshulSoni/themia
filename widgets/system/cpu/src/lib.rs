use std::collections::HashMap;
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant};
use sysinfo::{CpuRefreshKind, Disks, Networks, System};

// ── Public serialisable types ────────────────────────────────────────────────

#[derive(serde::Serialize, Clone)]
pub struct DiskStats {
    pub mount: String,
    pub used: u64,
    pub total: u64,
    pub read_per_sec: u64,
    pub write_per_sec: u64,
}

#[derive(serde::Serialize, Clone)]
pub struct NetIface {
    pub name: String,
    pub rx_per_sec: u64,
    pub tx_per_sec: u64,
    pub is_up: bool,
}

#[derive(serde::Serialize, Clone)]
pub struct ProcCpu {
    pub name: String,
    pub pid: u32,
    pub cpu_pct: f32,
    /// Version-info FileDescription of the process exe (the friendly name Task
    /// Manager shows, e.g. "Google Chrome" for chrome.exe); `None` when
    /// unresolvable (protected process, exe without a version resource), in
    /// which case the frontend falls back to the image name ([S-25]).
    pub desc: Option<String>,
}

#[derive(serde::Serialize, Clone)]
pub struct ProcMem {
    pub name: String,
    pub pid: u32,
    pub mem_bytes: u64,
}

#[derive(serde::Serialize, Clone)]
pub struct SystemStats {
    pub cpu_usage: f32,
    pub cpu_cores: Vec<f32>,
    pub cpu_freq_mhz: u64,
    pub cpu_temp_c: Option<f32>,
    /// CPU fan speed (RPM). Populated only when a sensor source exposes a CPU
    /// fan reading (currently the LibreHardwareMonitor WMI provider); `None`
    /// when no fan sensor is available. See [S-23].
    pub cpu_fan_rpm: Option<u32>,
    pub cpu_processes_total: u32,
    pub cpu_threads_total: u32,
    pub cpu_top: Vec<ProcCpu>,
    pub cpu_uptime_s: u64,
    pub ram_used: u64,
    pub ram_total: u64,
    pub ram_available: u64,
    pub swap_used: u64,
    pub swap_total: u64,
    pub ram_top: Vec<ProcMem>,
    pub disks: Vec<DiskStats>,
    pub net_rx_per_sec: u64,
    pub net_tx_per_sec: u64,
    pub net_interfaces: Vec<NetIface>,
    pub net_session_rx: u64,
    pub net_session_tx: u64,
    /// Public IP address (best-effort fetch, refreshed every 10 minutes).
    pub public_ip: Option<String>,
    /// System-wide bytes read from disk per second
    pub disk_read_per_sec: u64,
    /// System-wide bytes written to disk per second
    pub disk_write_per_sec: u64,
    /// System-wide read IOPS (ops/sec)
    pub disk_read_ops_per_sec: u64,
    /// System-wide write IOPS (ops/sec)
    pub disk_write_ops_per_sec: u64,
}

// ── Persistent sysinfo state ─────────────────────────────────────────────────

struct SysState {
    sys: System,
    networks: Networks,
    /// Cached volume list, re-enumerated at most every `DISK_LIST_REFRESH`;
    /// per tick only free space is refreshed ([disk S-21]).
    disks: Disks,
    disks_list_at: Instant,
    cached: Option<SystemStats>,
    /// Whether `cached` was built with process metrics ([S-21]).
    cached_has_procs: bool,
    last_refresh: Option<Instant>,
    /// Previous (IoReadTransferCount, IoWriteTransferCount, IoReadOperationCount, IoWriteOperationCount, timestamp)
    prev_disk_io: Option<(u64, u64, u64, u64, Instant)>,
    /// Previous cumulative net (total_received, total_transmitted, timestamp) for bytes/sec rates
    prev_net: Option<(u64, u64, Instant)>,
    /// Session start cumulative net rx/tx for baselining
    session_start_rx: Option<u64>,
    session_start_tx: Option<u64>,
    logical_disk_pdh: Option<LogicalDiskPdh>,
    public_ip_cache: Option<String>,
    public_ip_fetched_at: Option<Instant>,
    /// Previous per-process cumulative (kernel+user) CPU time in 100 ns units,
    /// keyed by PID, plus the sample instant, used to derive per-process CPU%
    /// from `NtQuerySystemInformation` deltas ([cpu S-17], issue #50). `None`
    /// until the first process sample.
    prev_proc_cpu: Option<(HashMap<u32, u64>, Instant)>,
    /// Version-info FileDescription per exe path for top-process rows
    /// ([S-25]); a cached `None` means the exe has no usable description.
    /// Bounded: cleared at DESC_CACHE_MAX entries.
    proc_desc_cache: HashMap<String, Option<String>>,
}

static STATE: OnceLock<Mutex<SysState>> = OnceLock::new();

fn sys_state() -> &'static Mutex<SysState> {
    STATE.get_or_init(|| {
        let mut sys = System::new();
        sys.refresh_cpu_specifics(CpuRefreshKind::new().with_cpu_usage().with_frequency());
        sys.refresh_memory();
        let networks = Networks::new_with_refreshed_list();
        let logical_disk_pdh = init_logical_disk_pdh();
        Mutex::new(SysState {
            sys,
            networks,
            disks: Disks::new_with_refreshed_list(),
            disks_list_at: Instant::now(),
            cached: None,
            cached_has_procs: false,
            last_refresh: None,
            prev_disk_io: None,
            prev_net: None,
            session_start_rx: None,
            session_start_tx: None,
            logical_disk_pdh,
            public_ip_cache: None,
            public_ip_fetched_at: None,
            prev_proc_cpu: None,
            proc_desc_cache: HashMap::new(),
        })
    })
}

// ── Windows: system-wide disk I/O via NtQuerySystemInformation ──────────────

#[cfg(target_os = "windows")]
#[link(name = "ntdll")]
extern "system" {
    fn NtQuerySystemInformation(
        SystemInformationClass: u32,
        SystemInformation: *mut std::ffi::c_void,
        SystemInformationLength: u32,
        ReturnLength: *mut u32,
    ) -> u32; // NTSTATUS; 0 = STATUS_SUCCESS
}

/// Pure parse of the SYSTEM_PERFORMANCE_INFORMATION u64 lanes into
/// (read_bytes, write_bytes, read_ops, write_ops).
///
/// IoReadTransferCount / IoWriteTransferCount are full 64-bit lanes (buf[1], buf[2]).
/// IoReadOperationCount and IoWriteOperationCount are two adjacent 32-bit fields that
/// share a single 64-bit lane (buf[4]): read_ops = low dword, write_ops = high dword.
#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
fn parse_disk_io_totals(buf: &[u64; 64]) -> (u64, u64, u64, u64) {
    let read_bytes = buf[1];
    let write_bytes = buf[2];
    let read_ops = buf[4] & 0xFFFF_FFFF;
    let write_ops = buf[4] >> 32;
    (read_bytes, write_bytes, read_ops, write_ops)
}

/// Returns (cumulative_bytes_read, cumulative_bytes_written, cumulative_read_ops, cumulative_write_ops).
fn query_disk_io_totals() -> Option<(u64, u64, u64, u64)> {
    #[cfg(target_os = "windows")]
    unsafe {
        let mut buf = [0u64; 64];
        let mut ret_len: u32 = 0;
        let status = NtQuerySystemInformation(
            2, // SystemPerformanceInformation
            buf.as_mut_ptr() as *mut _,
            (std::mem::size_of_val(&buf)) as u32,
            &mut ret_len,
        );
        if status == 0 {
            Some(parse_disk_io_totals(&buf))
        } else {
            None
        }
    }
    #[cfg(not(target_os = "windows"))]
    None
}

/// One sampled network interface: (name, total_rx, total_tx, delta_rx,
/// delta_tx): cumulative byte counters plus sysinfo's per-refresh deltas.
type IfaceSample = (String, u64, u64, u64, u64);

/// True for NDIS packet-capture filter mirror rows. Npcap (installed by
/// Wireshark and friends) registers a filter instance per physical NIC,
/// e.g. "Ethernet-Npcap Packet Driver (NPCAP)-0000", that re-reports the
/// underlying adapter's byte counters, so counting it doubles every rate
/// ([S-17], themia-releases#33). Matching "pcap packet driver" also covers
/// legacy WinPcap ("…-WinPcap Packet Driver (NPF)-0000") but not the
/// standalone "Npcap Loopback Adapter", which carries distinct traffic.
fn is_capture_mirror_iface(name: &str) -> bool {
    name.to_ascii_lowercase().contains("pcap packet driver")
}

/// Fold interface samples into the aggregate cumulative rx/tx totals plus
/// the per-interface delta list used for per-iface rate splitting.
/// Capture-driver mirror rows are skipped entirely ([S-17]).
fn aggregate_net_ifaces(samples: Vec<IfaceSample>) -> (u64, u64, Vec<(String, u64, u64)>) {
    let (mut cum_rx, mut cum_tx) = (0u64, 0u64);
    let mut iface_deltas: Vec<(String, u64, u64)> = Vec::new();
    for (name, total_rx, total_tx, delta_rx, delta_tx) in samples {
        if is_capture_mirror_iface(&name) {
            continue;
        }
        cum_rx = cum_rx.saturating_add(total_rx);
        cum_tx = cum_tx.saturating_add(total_tx);
        iface_deltas.push((name, delta_rx, delta_tx));
    }
    (cum_rx, cum_tx, iface_deltas)
}

/// Bytes/sec from a cumulative-counter delta over an elapsed window.
/// Guards a zero/negative window (first sample) by reporting 0.
fn bytes_per_sec(prev_total: u64, cur_total: u64, elapsed_secs: f64) -> u64 {
    if elapsed_secs > 0.01 {
        (cur_total.saturating_sub(prev_total) as f64 / elapsed_secs) as u64
    } else {
        0
    }
}

fn disk_io_rate(prev: &mut Option<(u64, u64, u64, u64, Instant)>) -> (u64, u64, u64, u64) {
    let Some((read_total, write_total, read_ops, write_ops)) = query_disk_io_totals() else {
        return (0, 0, 0, 0);
    };
    let now = Instant::now();
    let rate = if let Some((pr, pw, pro, pwo, pt)) = prev {
        let secs = now.duration_since(*pt).as_secs_f64();
        if secs > 0.01 {
            (
                ((read_total.saturating_sub(*pr)) as f64 / secs) as u64,
                ((write_total.saturating_sub(*pw)) as f64 / secs) as u64,
                ((read_ops.saturating_sub(*pro)) as f64 / secs) as u64,
                ((write_ops.saturating_sub(*pwo)) as f64 / secs) as u64,
            )
        } else {
            (0, 0, 0, 0)
        }
    } else {
        (0, 0, 0, 0)
    };
    *prev = Some((read_total, write_total, read_ops, write_ops, now));
    rate
}

// ── PDH per-mount disk I/O (\LogicalDisk(*)\Disk Read|Write Bytes/sec) ──────

#[cfg(target_os = "windows")]
#[link(name = "pdh")]
extern "system" {
    fn PdhOpenQueryW(szDataSource: *const u16, dwUserData: usize, phQuery: *mut isize) -> u32;
    fn PdhAddEnglishCounterW(
        hQuery: isize,
        szFullCounterPath: *const u16,
        dwUserData: usize,
        phCounter: *mut isize,
    ) -> u32;
    fn PdhCollectQueryData(hQuery: isize) -> u32;
    fn PdhGetFormattedCounterArrayW(
        hCounter: isize,
        dwFormat: u32,
        lpdwBufferSize: *mut u32,
        lpdwItemCount: *mut u32,
        pItemBuffer: *mut u8,
    ) -> u32;
    fn PdhCloseQuery(hQuery: isize) -> u32;
}

#[cfg(target_os = "windows")]
const PDH_FMT_DOUBLE: u32 = 0x0000_0200;

#[cfg(target_os = "windows")]
#[repr(C)]
struct PdhItemF64 {
    name: *const u16,
    status: u32,
    _pad: u32,
    value: f64,
}

struct LogicalDiskPdh {
    query: isize,
    read_counter: isize,
    write_counter: isize,
}

#[cfg(target_os = "windows")]
unsafe impl Send for LogicalDiskPdh {}
#[cfg(target_os = "windows")]
unsafe impl Sync for LogicalDiskPdh {}

#[cfg(not(target_os = "windows"))]
unsafe impl Send for LogicalDiskPdh {}
#[cfg(not(target_os = "windows"))]
unsafe impl Sync for LogicalDiskPdh {}

#[cfg(target_os = "windows")]
fn wstr(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0u16)).collect()
}

#[cfg(target_os = "windows")]
unsafe fn wchar_ptr_to_string(ptr: *const u16) -> String {
    if ptr.is_null() {
        return String::new();
    }
    let mut len = 0usize;
    while *ptr.add(len) != 0 {
        len += 1;
    }
    String::from_utf16_lossy(std::slice::from_raw_parts(ptr, len))
}

#[cfg(target_os = "windows")]
fn init_logical_disk_pdh() -> Option<LogicalDiskPdh> {
    unsafe {
        let mut query: isize = 0;
        if PdhOpenQueryW(std::ptr::null(), 0, &mut query) != 0 {
            return None;
        }
        let read_path = wstr("\\LogicalDisk(*)\\Disk Read Bytes/sec");
        let write_path = wstr("\\LogicalDisk(*)\\Disk Write Bytes/sec");
        let mut read_counter: isize = 0;
        let mut write_counter: isize = 0;
        if PdhAddEnglishCounterW(query, read_path.as_ptr(), 0, &mut read_counter) != 0 {
            PdhCloseQuery(query);
            return None;
        }
        if PdhAddEnglishCounterW(query, write_path.as_ptr(), 0, &mut write_counter) != 0 {
            PdhCloseQuery(query);
            return None;
        }
        PdhCollectQueryData(query);
        Some(LogicalDiskPdh {
            query,
            read_counter,
            write_counter,
        })
    }
}

#[cfg(not(target_os = "windows"))]
fn init_logical_disk_pdh() -> Option<LogicalDiskPdh> {
    None
}

#[cfg(target_os = "windows")]
unsafe fn pdh_collect_map(counter: isize) -> HashMap<String, u64> {
    let mut out: HashMap<String, u64> = HashMap::new();
    let mut buf_size: u32 = 0;
    let mut item_count: u32 = 0;
    PdhGetFormattedCounterArrayW(
        counter,
        PDH_FMT_DOUBLE,
        &mut buf_size,
        &mut item_count,
        std::ptr::null_mut(),
    );
    if item_count == 0 || buf_size == 0 {
        return out;
    }
    let mut buf = vec![0u8; buf_size as usize];
    let status = PdhGetFormattedCounterArrayW(
        counter,
        PDH_FMT_DOUBLE,
        &mut buf_size,
        &mut item_count,
        buf.as_mut_ptr(),
    );
    if status != 0 {
        return out;
    }
    let items = std::slice::from_raw_parts(buf.as_ptr() as *const PdhItemF64, item_count as usize);
    for it in items {
        if it.status != 0 || !it.value.is_finite() || it.value < 0.0 {
            continue;
        }
        let name = wchar_ptr_to_string(it.name);
        out.insert(name, it.value as u64);
    }
    out
}

#[cfg(target_os = "windows")]
fn sample_per_disk(pdh: &LogicalDiskPdh) -> HashMap<String, (u64, u64)> {
    unsafe {
        PdhCollectQueryData(pdh.query);
        let r = pdh_collect_map(pdh.read_counter);
        let w = pdh_collect_map(pdh.write_counter);
        let mut keys: std::collections::HashSet<String> = r.keys().cloned().collect();
        for k in w.keys() {
            keys.insert(k.clone());
        }
        keys.into_iter()
            .map(|k| {
                let rv = r.get(&k).copied().unwrap_or(0);
                let wv = w.get(&k).copied().unwrap_or(0);
                (k, (rv, wv))
            })
            .collect()
    }
}

#[cfg(not(target_os = "windows"))]
fn sample_per_disk(_pdh: &LogicalDiskPdh) -> HashMap<String, (u64, u64)> {
    HashMap::new()
}

/// Normalise a sysinfo mount string (e.g. "C:\\") to a PDH LogicalDisk instance name ("C:").
fn normalize_mount(mount: &str) -> String {
    mount.trim_end_matches(['\\', '/']).to_string()
}

// ── CPU temperature + fan via WMI ───────────────────────────────────────────
// Two WMI sources, queried on one dedicated worker thread ([S-20], [S-22],
// [S-23]):
//
//   1. LibreHardwareMonitor / OpenHardwareMonitor `Sensor` class
//      (root\LibreHardwareMonitor, falling back to root\OpenHardwareMonitor).
//      This is the ONLY no-extra-privilege source that covers modern consumer
//      AMD Ryzen boards: those expose the on-die Tctl/Tdie sensor only over the
//      SMU/SMN, which needs a ring-0 kernel driver; LHM ships its own signed
//      driver and surfaces the reading over WMI, so Themia reads it WITHOUT
//      bundling a kernel driver itself. It also carries CPU fan RPM ([S-23]).
//   2. ACPI thermal zone (`MSAcpi_ThermalZoneTemperature`, root\wmi). The
//      historical source; present on many Intel/OEM boards but commonly ABSENT
//      on consumer AMD boards (issue #36), hence source 1 is preferred.
//
// COM must not be initialised on the main thread: the WebView2 message loop is
// STA, so CoInitializeEx(COINIT_MULTITHREADED) fails with RPC_E_CHANGED_MODE
// and the metric was permanently dead. Sampling therefore runs on a dedicated
// worker thread with its own MTA COM library; it starts on the first request
// that needs the metric and publishes the latest reading.

/// Convert MSAcpi `CurrentTemperature` readings (tenths of a Kelvin, one per
/// thermal zone) to °C of the hottest zone. `None` = sensor absent or bogus.
#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
fn zones_to_celsius(kelvin_tenths: &[u32]) -> Option<f32> {
    let max = kelvin_tenths.iter().copied().max()? as f32;
    if max < 1000.0 {
        return None;
    }
    Some(max / 10.0 - 273.15)
}

/// A LibreHardwareMonitor/OpenHardwareMonitor `Sensor` row reduced to the three
/// fields we care about. `sensor_type` is e.g. "Temperature" / "Fan" / "Clock";
/// `name` is the human label (e.g. "CPU Package", "Core (Tctl/Tdie)", "CPU Fan").
#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
struct LhmSensor {
    name: String,
    sensor_type: String,
    value: f32,
}

/// Pick the CPU temperature (°C) from a LibreHardwareMonitor sensor list.
///
/// Prefer a package-level reading ("CPU Package" / "Tctl"/"Tdie") which is the
/// single number users expect; otherwise fall back to the hottest per-core
/// temperature. Only "Temperature" sensors whose name mentions the CPU are
/// considered, so GPU/board/drive temperatures can't leak in. Plausibility is
/// bounded to 0–150 °C; anything outside is treated as a bogus row and ignored.
/// `None` = LHM is running but exposes no CPU temperature.
#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
fn lhm_pick_cpu_temp(sensors: &[LhmSensor]) -> Option<f32> {
    let is_plausible = |v: f32| v.is_finite() && (0.0..=150.0).contains(&v);
    let cpu_temps: Vec<(&str, f32)> = sensors
        .iter()
        .filter(|s| s.sensor_type.eq_ignore_ascii_case("Temperature"))
        .filter(|s| {
            let n = s.name.to_ascii_lowercase();
            n.contains("cpu") || n.contains("tctl") || n.contains("tdie") || n.contains("core")
        })
        .filter(|s| is_plausible(s.value))
        .map(|s| (s.name.as_str(), s.value))
        .collect();
    if cpu_temps.is_empty() {
        return None;
    }
    // Prefer a package / Tctl / Tdie reading.
    let package = cpu_temps.iter().find(|(name, _)| {
        let n = name.to_ascii_lowercase();
        n.contains("package") || n.contains("tctl") || n.contains("tdie")
    });
    match package {
        Some((_, v)) => Some(*v),
        // No package sensor: report the hottest per-core temperature.
        None => cpu_temps
            .iter()
            .map(|(_, v)| *v)
            .fold(f32::MIN, f32::max)
            .into(),
    }
}

/// Pick the CPU fan speed (RPM) from a LibreHardwareMonitor sensor list ([S-23]).
/// Only "Fan" sensors whose name mentions the CPU are considered; the highest
/// plausible (1–60000 RPM) reading wins. A 0-RPM reading is a stopped/absent fan
/// header rather than a CPU fan, so it is ignored. `None` = no CPU fan exposed.
#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
fn lhm_pick_cpu_fan(sensors: &[LhmSensor]) -> Option<u32> {
    sensors
        .iter()
        .filter(|s| s.sensor_type.eq_ignore_ascii_case("Fan"))
        .filter(|s| s.name.to_ascii_lowercase().contains("cpu"))
        .filter(|s| s.value.is_finite() && s.value >= 1.0 && s.value <= 60_000.0)
        .map(|s| s.value as u32)
        .max()
}

/// Latest CPU temperature (°C) published by the sampler thread; `None` until
/// the sampler has run (or forever, when no source exposes a CPU temperature).
static CPU_TEMP_C: Mutex<Option<f32>> = Mutex::new(None);
/// Latest CPU fan speed (RPM); `None` when no fan sensor is exposed ([S-23]).
static CPU_FAN_RPM: Mutex<Option<u32>> = Mutex::new(None);
#[cfg(target_os = "windows")]
static TEMP_SAMPLER: OnceLock<()> = OnceLock::new();

fn cpu_temp_snapshot() -> Option<f32> {
    *CPU_TEMP_C.lock().unwrap_or_else(|e| e.into_inner())
}

fn cpu_fan_snapshot() -> Option<u32> {
    *CPU_FAN_RPM.lock().unwrap_or_else(|e| e.into_inner())
}

#[cfg(target_os = "windows")]
fn ensure_temp_sampler() {
    TEMP_SAMPLER.get_or_init(|| {
        // ignore: spawn failure leaves the metric None, which hides the row per [S-20]
        let _ = std::thread::Builder::new()
            .name("cpu-temp-wmi".into())
            .spawn(temp_sampler_loop);
    });
}

#[cfg(not(target_os = "windows"))]
fn ensure_temp_sampler() {}

/// Try the LibreHardwareMonitor (then OpenHardwareMonitor) WMI `Sensor` class.
/// Returns `Some((temp_c, fan_rpm))` when the provider is reachable; either
/// field may still be `None` if that specific sensor isn't exposed. Returns
/// `None` when neither namespace responds (LHM/OHM not installed/running), so
/// the caller can fall back to the ACPI thermal zone.
#[cfg(target_os = "windows")]
fn query_lhm_cpu_sensors(com: wmi::COMLibrary) -> Option<(Option<f32>, Option<u32>)> {
    use serde::Deserialize;
    use wmi::WMIConnection;

    #[derive(Deserialize)]
    #[serde(rename = "Sensor")]
    #[serde(rename_all = "PascalCase")]
    struct WmiSensor {
        name: String,
        sensor_type: String,
        value: f32,
    }

    // LibreHardwareMonitor uses root\LibreHardwareMonitor; the older
    // OpenHardwareMonitor uses root\OpenHardwareMonitor. Both expose the same
    // `Sensor` shape, so try the modern one first and fall back.
    for ns in ["root\\LibreHardwareMonitor", "root\\OpenHardwareMonitor"] {
        let Ok(wmi) = WMIConnection::with_namespace_path(ns, com) else {
            continue;
        };
        let Ok(rows) = wmi.query::<WmiSensor>() else {
            continue;
        };
        let sensors: Vec<LhmSensor> = rows
            .into_iter()
            .map(|r| LhmSensor {
                name: r.name,
                sensor_type: r.sensor_type,
                value: r.value,
            })
            .collect();
        return Some((lhm_pick_cpu_temp(&sensors), lhm_pick_cpu_fan(&sensors)));
    }
    None
}

/// Read the ACPI thermal zone (`MSAcpi_ThermalZoneTemperature`, root\wmi).
/// `Some(None)` = the namespace responded but reported no usable zone (common
/// on consumer AMD boards, issue #36); `None` = the query itself failed.
#[cfg(target_os = "windows")]
fn query_acpi_thermal_zone(com: wmi::COMLibrary) -> Option<Option<f32>> {
    use serde::Deserialize;
    use wmi::WMIConnection;

    #[derive(Deserialize)]
    #[serde(rename = "MSAcpi_ThermalZoneTemperature")]
    #[serde(rename_all = "PascalCase")]
    struct ThermalZone {
        current_temperature: u32,
    }

    let wmi = WMIConnection::with_namespace_path("root\\wmi", com).ok()?;
    let zones = wmi.query::<ThermalZone>().ok()?;
    let tenths: Vec<u32> = zones.iter().map(|z| z.current_temperature).collect();
    Some(zones_to_celsius(&tenths))
}

#[cfg(target_os = "windows")]
fn temp_sampler_loop() {
    use wmi::COMLibrary;

    const OK_PERIOD: Duration = Duration::from_secs(5);
    const RETRY_PERIOD: Duration = Duration::from_secs(60);

    let publish = |temp: Option<f32>, fan: Option<u32>| {
        *CPU_TEMP_C.lock().unwrap_or_else(|e| e.into_inner()) = temp;
        *CPU_FAN_RPM.lock().unwrap_or_else(|e| e.into_inner()) = fan;
    };

    loop {
        // Fresh MTA COM for this thread. CoInitializeSecurity is process-wide
        // and WebView2 may have called it already, so fall back to the
        // no-security init instead of giving up. COMLibrary is Copy and stays
        // valid for the lifetime of this thread (dropped when the thread ends).
        let Ok(com) = COMLibrary::new().or_else(|_| COMLibrary::without_security()) else {
            std::thread::sleep(RETRY_PERIOD);
            continue;
        };
        loop {
            // Source 1: LibreHardwareMonitor / OpenHardwareMonitor (covers AMD,
            // carries fan RPM). When reachable it is authoritative for both
            // metrics this tick; even a None temp from LHM beats a stale value.
            if let Some((lhm_temp, lhm_fan)) = query_lhm_cpu_sensors(com) {
                // Source 2 (temp only): if LHM is up but didn't expose a CPU
                // temperature, the ACPI zone may still have one.
                let temp = match lhm_temp {
                    Some(t) => Some(t),
                    None => query_acpi_thermal_zone(com).flatten(),
                };
                publish(temp, lhm_fan);
                let any = temp.is_some() || lhm_fan.is_some();
                std::thread::sleep(if any { OK_PERIOD } else { RETRY_PERIOD });
                continue;
            }

            // Source 2 only: LHM/OHM not running, so fall back to ACPI thermal
            // zone. No no-driver fan source exists without LHM, so fan stays
            // None ([S-23]).
            match query_acpi_thermal_zone(com) {
                Some(temp) => {
                    publish(temp, None);
                    // No sensor on this machine: poll slowly, the row stays hidden.
                    std::thread::sleep(if temp.is_some() {
                        OK_PERIOD
                    } else {
                        RETRY_PERIOD
                    });
                }
                None => {
                    // Both sources broke (service restart, access revoked):
                    // hide the rows rather than show a stale value, and rebuild
                    // the COM library on the next outer iteration.
                    publish(None, None);
                    std::thread::sleep(RETRY_PERIOD);
                    break;
                }
            }
        }
    }
}

// ── Public IP fetch ─────────────────────────────────────────────────────────
// Cached for 10 minutes; first call returns None and triggers a background
// fetch so the foreground request stays fast.

const PUBLIC_IP_TTL: Duration = Duration::from_secs(600);

fn fetch_public_ip_background() {
    std::thread::spawn(|| {
        #[cfg(target_os = "windows")]
        {
            // Via the Windows proxy when one applies (netproxy SPEC [PX-01]);
            // ureq has no proxy awareness of its own, so this path used to go
            // direct and simply fail on a proxied network.
            const IPIFY: &str = "https://api.ipify.org";
            let ip = url::Url::parse(IPIFY)
                .ok()
                .map(|u| widget_netproxy::ureq_agent(&u, Duration::from_secs(4)))
                .and_then(|agent| agent.get(IPIFY).call().ok())
                .and_then(|resp| resp.into_string().ok())
                .map(|s| s.trim().to_string())
                .filter(|s| !s.is_empty() && s.len() <= 45);
            if let Some(ip) = ip {
                let mut state = sys_state().lock().unwrap_or_else(|e| e.into_inner());
                state.public_ip_cache = Some(ip);
                state.public_ip_fetched_at = Some(Instant::now());
            }
        }
    });
}

// ── Public command entry point ───────────────────────────────────────────────

/// Volume re-enumeration cadence: free space refreshes every tick from the
/// cached list; the FindFirstVolumeW walk (expensive, opens every volume and
/// can spin up removable drives) runs at most this often, so new/removed
/// drives appear within 30 s ([disk S-21]).
const DISK_LIST_REFRESH: Duration = Duration::from_secs(30);

fn disk_list_stale(elapsed: Duration) -> bool {
    elapsed >= DISK_LIST_REFRESH
}

/// Debounce policy ([S-14], [S-21]): a fresh cache is served unless the caller
/// needs process metrics and the cached sample was built without them.
fn serve_cached(cache_fresh: bool, cached_has_procs: bool, want_procs: bool) -> bool {
    cache_fresh && (cached_has_procs || !want_procs)
}

// ── Windows process table via NtQuerySystemInformation ─────────────────────
// sysinfo derives per-process CPU% from `GetProcessTimes`, which needs a handle
// obtained via `OpenProcess`. For protected / elevated / system processes
// (Windows Update's TiWorker/TrustedInstaller, Defender's MsMpEng, Hyper-V's
// vmmem/vmwp, …) `OpenProcess` fails when Themia runs unelevated, so their CPU
// (and memory) read back as 0 and they never surface in the top lists
// (themia-releases#50). Task Manager avoids this by reading kernel+user times
// for every PID from `NtQuerySystemInformation(SystemProcessInformation)`, which
// requires no per-process handle. We do the same: enumerate once, diff the
// cumulative times against the previous sample for CPU%, and read WorkingSetSize
// for memory, covering every process regardless of privilege.
//
// The buffer parse and the CPU delta math are pure and platform-independent so
// they're unit-tested on any target; only the Windows `collect_proc_metrics`
// consumes them at runtime, hence the module is dead code elsewhere.
#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
mod proc_enum {
    use std::collections::HashMap;

    // x64 SYSTEM_PROCESS_INFORMATION field offsets. The layout is a stable,
    // long-documented kernel ABI; these must not drift (see the buffer-parse
    // characterization test).
    pub(super) const SPI_NEXT_OFFSET: usize = 0x00; // NextEntryOffset  (ULONG)
    pub(super) const SPI_THREAD_COUNT: usize = 0x04; // NumberOfThreads (ULONG)
    pub(super) const SPI_USER_TIME: usize = 0x28; // UserTime          (LARGE_INTEGER, 100 ns)
    pub(super) const SPI_KERNEL_TIME: usize = 0x30; // KernelTime       (LARGE_INTEGER, 100 ns)
    pub(super) const SPI_IMAGE_NAME_LEN: usize = 0x38; // ImageName.Length (USHORT, bytes)
    pub(super) const SPI_IMAGE_NAME_BUF: usize = 0x40; // ImageName.Buffer (PWSTR)
    pub(super) const SPI_UNIQUE_PID: usize = 0x50; // UniqueProcessId    (HANDLE)
    pub(super) const SPI_WORKING_SET: usize = 0x90; // WorkingSetSize    (SIZE_T, bytes)
    /// Bytes that must be readable for an entry's fixed part (through the
    /// WorkingSetSize u64 at 0x90).
    pub(super) const SPI_MIN_ENTRY: usize = SPI_WORKING_SET + 8;

    /// One SYSTEM_PROCESS_INFORMATION row, integer fields only. `name_ptr`/
    /// `name_len_bytes` locate the UNICODE_STRING image name inside the same
    /// returned buffer; name resolution (a raw-pointer read) is done separately
    /// so this parse stays pure and testable.
    #[derive(Debug, Clone, PartialEq, Eq)]
    pub(super) struct SysProcEntry {
        pub pid: u32,
        pub threads: u32,
        /// Cumulative kernel+user time in 100 ns units.
        pub cpu_time_100ns: u64,
        pub ws_bytes: u64,
        pub name_len_bytes: u16,
        pub name_ptr: usize,
    }

    fn rd_u16(buf: &[u8], off: usize) -> u16 {
        buf.get(off..off + 2)
            .map(|b| u16::from_le_bytes([b[0], b[1]]))
            .unwrap_or(0)
    }
    fn rd_u32(buf: &[u8], off: usize) -> u32 {
        buf.get(off..off + 4)
            .map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
            .unwrap_or(0)
    }
    fn rd_u64(buf: &[u8], off: usize) -> u64 {
        buf.get(off..off + 8)
            .map(|b| u64::from_le_bytes([b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7]]))
            .unwrap_or(0)
    }

    /// Walk the NextEntryOffset-linked SYSTEM_PROCESS_INFORMATION chain in a
    /// returned buffer into a flat entry list. Defensive against a truncated or
    /// malformed chain: an entry whose fixed part would read out of bounds ends
    /// the walk, and a zero/backwards NextEntryOffset stops it (never loops).
    pub(super) fn parse_system_process_info(buf: &[u8]) -> Vec<SysProcEntry> {
        // Upper bound on entries: far above any real process count, a backstop
        // in case the chain is corrupt.
        const MAX_ENTRIES: usize = 1 << 16;
        let mut out = Vec::new();
        let mut base = 0usize;
        for _ in 0..MAX_ENTRIES {
            match base.checked_add(SPI_MIN_ENTRY) {
                Some(end) if end <= buf.len() => {}
                _ => break,
            }
            let user = rd_u64(buf, base + SPI_USER_TIME);
            let kernel = rd_u64(buf, base + SPI_KERNEL_TIME);
            out.push(SysProcEntry {
                pid: rd_u64(buf, base + SPI_UNIQUE_PID) as u32,
                threads: rd_u32(buf, base + SPI_THREAD_COUNT),
                cpu_time_100ns: kernel.saturating_add(user),
                ws_bytes: rd_u64(buf, base + SPI_WORKING_SET),
                name_len_bytes: rd_u16(buf, base + SPI_IMAGE_NAME_LEN),
                name_ptr: rd_u64(buf, base + SPI_IMAGE_NAME_BUF) as usize,
            });
            let next = rd_u32(buf, base + SPI_NEXT_OFFSET) as usize;
            match base.checked_add(next) {
                Some(nb) if next != 0 && nb > base => base = nb,
                _ => break,
            }
        }
        out
    }

    /// Per-process CPU% (relative to a single core, matching sysinfo's scale so
    /// the shared `top_cpu_procs` normalisation applies unchanged) from the
    /// cumulative-time delta over `elapsed_100ns`. A PID absent from `prev` (a
    /// new process, or the first sample) contributes 0 this tick. The idle
    /// process (PID 0) is excluded: its "busy" time is the machine idling.
    pub(super) fn proc_cpu_rows(
        entries: &[(String, u32, u64)],
        prev: &HashMap<u32, u64>,
        elapsed_100ns: u64,
    ) -> Vec<(String, u32, f32)> {
        entries
            .iter()
            .filter(|(_, pid, _)| *pid != 0)
            .map(|(name, pid, t)| {
                let raw = match prev.get(pid) {
                    Some(prev_t) if elapsed_100ns > 0 => {
                        let delta = t.saturating_sub(*prev_t);
                        100.0 * (delta as f64 / elapsed_100ns as f64)
                    }
                    _ => 0.0,
                };
                (name.clone(), *pid, raw as f32)
            })
            .collect()
    }
}

#[cfg(target_os = "windows")]
const SYSTEM_PROCESS_INFORMATION_CLASS: u32 = 5;
#[cfg(target_os = "windows")]
const STATUS_INFO_LENGTH_MISMATCH: u32 = 0xC000_0004;

/// A process row after name resolution, ready to fold into the top lists.
#[cfg(target_os = "windows")]
struct RawProc {
    name: String,
    pid: u32,
    threads: u32,
    cpu_time_100ns: u64,
    ws_bytes: u64,
}

/// Call `NtQuerySystemInformation(SystemProcessInformation)`, growing the buffer
/// until it fits (the process list can change size between calls). Returns the
/// filled, right-sized buffer, or `None` on an unexpected NTSTATUS.
#[cfg(target_os = "windows")]
fn query_system_process_buf() -> Option<Vec<u8>> {
    unsafe {
        let mut size: usize = 512 * 1024;
        for _ in 0..8 {
            let mut buf = vec![0u8; size];
            let mut ret_len: u32 = 0;
            let status = NtQuerySystemInformation(
                SYSTEM_PROCESS_INFORMATION_CLASS,
                buf.as_mut_ptr() as *mut _,
                size as u32,
                &mut ret_len,
            );
            if status == 0 {
                buf.truncate((ret_len as usize).min(buf.len()));
                return Some(buf);
            }
            if status == STATUS_INFO_LENGTH_MISMATCH {
                // Grow past the hinted length (or double), plus slack for churn.
                size = (ret_len as usize)
                    .max(size.saturating_mul(2))
                    .saturating_add(64 * 1024);
                continue;
            }
            return None; // unexpected NTSTATUS, treat as no data this tick
        }
        None
    }
}

/// Resolve a SYSTEM_PROCESS_INFORMATION image name. The UNICODE_STRING buffer
/// points inside `buf`; bounds-check it against the allocation before reading so
/// a bogus pointer can never dereference out of bounds.
#[cfg(target_os = "windows")]
fn resolve_image_name(buf: &[u8], name_ptr: usize, name_len_bytes: u16) -> String {
    if name_ptr == 0 || name_len_bytes == 0 {
        return String::new();
    }
    let start = buf.as_ptr() as usize;
    let end = start.saturating_add(buf.len());
    let len = name_len_bytes as usize;
    if name_ptr < start || name_ptr.saturating_add(len) > end {
        return String::new();
    }
    // SAFETY: the range [name_ptr, name_ptr+len) lies within `buf` (checked
    // above); the kernel writes the name as UTF-16 there.
    let slice = unsafe { std::slice::from_raw_parts(name_ptr as *const u16, len / 2) };
    String::from_utf16_lossy(slice)
}

/// Enumerate all processes with cumulative CPU time, thread count and working
/// set via `NtQuerySystemInformation`, with no per-process handle needed, so protected and
/// elevated processes are included (issue #50).
#[cfg(target_os = "windows")]
fn query_system_processes() -> Vec<RawProc> {
    let Some(buf) = query_system_process_buf() else {
        return Vec::new();
    };
    proc_enum::parse_system_process_info(&buf)
        .into_iter()
        .map(|e| {
            let mut name = resolve_image_name(&buf, e.name_ptr, e.name_len_bytes);
            if name.is_empty() {
                // The idle (0) and System (4) processes carry no image name.
                name = if e.pid == 4 { "System".into() } else { name };
            }
            RawProc {
                name,
                pid: e.pid,
                threads: e.threads,
                cpu_time_100ns: e.cpu_time_100ns,
                ws_bytes: e.ws_bytes,
            }
        })
        .collect()
}

// ── [S-25] Process "real" name: version-info FileDescription ────────────────
// Task Manager's process list shows the exe's version-info FileDescription
// ("Google Chrome"), not the image name ("chrome.exe"). Resolve it for each
// top-CPU row: OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) →
// QueryFullProcessImageNameW → GetFileVersionInfoW/VerQueryValueW, cached per
// exe path so a binary's version resource is read from disk once, not every
// tick. Protected/system processes refuse even a limited-information handle
// when Themia runs unelevated; they keep `desc: None` and the frontend falls
// back to the image name.

/// Ordered StringFileInfo sub-block paths to try for FileDescription: each
/// translation-table language first, then the ubiquitous en-US/Unicode block
/// (many exes carry only that one even on localised systems).
#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
fn desc_subblocks(translations: &[(u16, u16)]) -> Vec<String> {
    let mut out: Vec<String> = translations
        .iter()
        .map(|(lang, cp)| format!("\\StringFileInfo\\{lang:04X}{cp:04X}\\FileDescription"))
        .collect();
    let fallback = "\\StringFileInfo\\040904B0\\FileDescription".to_string();
    if !out.contains(&fallback) {
        out.push(fallback);
    }
    out
}

/// Trim a raw FileDescription reading; blank counts as absent so the frontend
/// falls back to the image name instead of an empty row label ([S-25]).
#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
fn clean_desc(raw: String) -> Option<String> {
    let t = raw.trim();
    if t.is_empty() {
        None
    } else {
        Some(t.to_string())
    }
}

#[cfg(target_os = "windows")]
#[link(name = "kernel32")]
extern "system" {
    fn OpenProcess(dwDesiredAccess: u32, bInheritHandle: i32, dwProcessId: u32) -> isize;
    fn CloseHandle(hObject: isize) -> i32;
    fn QueryFullProcessImageNameW(
        hProcess: isize,
        dwFlags: u32,
        lpExeName: *mut u16,
        lpdwSize: *mut u32,
    ) -> i32;
}

#[cfg(target_os = "windows")]
#[link(name = "version")]
extern "system" {
    fn GetFileVersionInfoSizeW(lptstrFilename: *const u16, lpdwHandle: *mut u32) -> u32;
    fn GetFileVersionInfoW(
        lptstrFilename: *const u16,
        dwHandle: u32,
        dwLen: u32,
        lpData: *mut std::ffi::c_void,
    ) -> i32;
    fn VerQueryValueW(
        pBlock: *const std::ffi::c_void,
        lpSubBlock: *const u16,
        lplpBuffer: *mut *mut std::ffi::c_void,
        puLen: *mut u32,
    ) -> i32;
}

#[cfg(target_os = "windows")]
const PROCESS_QUERY_LIMITED_INFORMATION: u32 = 0x1000;

/// RAII guard for a Win32 process handle: closed on every exit path.
#[cfg(target_os = "windows")]
struct ProcessHandle(isize);
#[cfg(target_os = "windows")]
impl Drop for ProcessHandle {
    fn drop(&mut self) {
        if self.0 != 0 {
            // ignore: closing an already-invalid handle has no recovery
            unsafe { CloseHandle(self.0) };
        }
    }
}

/// Full image path of a running process. `None` for protected/elevated
/// processes (limited-information handle refused) or a PID that exited.
#[cfg(target_os = "windows")]
fn process_exe_path(pid: u32) -> Option<String> {
    unsafe {
        let raw = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
        if raw == 0 {
            return None;
        }
        let handle = ProcessHandle(raw);
        let mut buf = [0u16; 1024];
        let mut len = buf.len() as u32;
        if QueryFullProcessImageNameW(handle.0, 0, buf.as_mut_ptr(), &mut len) == 0 {
            return None;
        }
        Some(String::from_utf16_lossy(&buf[..len as usize]))
    }
}

/// Read the version-info FileDescription of a binary on disk ([S-25]).
#[cfg(target_os = "windows")]
fn file_description(path: &str) -> Option<String> {
    unsafe {
        let path_w = wstr(path);
        let mut ignored = 0u32;
        let size = GetFileVersionInfoSizeW(path_w.as_ptr(), &mut ignored);
        if size == 0 {
            return None; // no version resource at all
        }
        let mut data = vec![0u8; size as usize];
        if GetFileVersionInfoW(path_w.as_ptr(), 0, size, data.as_mut_ptr() as *mut _) == 0 {
            return None;
        }
        // (lang, codepage) pairs from the translation table; empty when the
        // resource is malformed, in which case only the en-US block is tried.
        let mut translations: Vec<(u16, u16)> = Vec::new();
        let trans_key = wstr("\\VarFileInfo\\Translation");
        let mut ptr: *mut std::ffi::c_void = std::ptr::null_mut();
        let mut len: u32 = 0;
        if VerQueryValueW(
            data.as_ptr() as *const _,
            trans_key.as_ptr(),
            &mut ptr,
            &mut len,
        ) != 0
            && !ptr.is_null()
        {
            let pairs = std::slice::from_raw_parts(ptr as *const u16, len as usize / 2);
            translations = pairs.chunks_exact(2).map(|c| (c[0], c[1])).collect();
        }
        for sub in desc_subblocks(&translations) {
            let key = wstr(&sub);
            let mut sptr: *mut std::ffi::c_void = std::ptr::null_mut();
            let mut slen: u32 = 0; // length in u16 chars, includes the NUL
            if VerQueryValueW(
                data.as_ptr() as *const _,
                key.as_ptr(),
                &mut sptr,
                &mut slen,
            ) == 0
                || sptr.is_null()
                || slen == 0
            {
                continue;
            }
            let wide = std::slice::from_raw_parts(sptr as *const u16, slen as usize);
            let s = String::from_utf16_lossy(wide)
                .trim_end_matches('\0')
                .to_string();
            if let Some(d) = clean_desc(s) {
                return Some(d);
            }
        }
        None
    }
}

/// Cache backstop: the cache is keyed by exe path so it can only grow as large
/// as the set of distinct binaries ever seen in the top list; clear rather
/// than grow unbounded under pathological churn.
#[cfg(target_os = "windows")]
const DESC_CACHE_MAX: usize = 512;

/// FileDescription for a pid via the per-exe-path cache ([S-25]). Resolvable
/// paths cache their result (including `None` for description-less exes); a
/// protected process yields no path and just retries next tick (one cheap
/// failed OpenProcess).
#[cfg(target_os = "windows")]
fn resolve_proc_desc(pid: u32, cache: &mut HashMap<String, Option<String>>) -> Option<String> {
    let path = process_exe_path(pid)?;
    if let Some(hit) = cache.get(&path) {
        return hit.clone();
    }
    let desc = file_description(&path);
    if cache.len() >= DESC_CACHE_MAX {
        cache.clear();
    }
    cache.insert(path, desc.clone());
    desc
}

/// How many top-CPU rows the backend serves. The widget renders its configured
/// `topProcsCount` (1..=10, default 3) by slicing the frontend list, so this
/// must stay at the config maximum for the slice to never starve ([S-26]).
const CPU_TOP_N: usize = 10;

/// Top-`n` CPU consumers from (name, pid, raw sysinfo cpu%) rows. sysinfo
/// reports per-process CPU relative to one core, so normalise by core count;
/// idle (0%) rows are dropped ([cpu S-17]). `desc` is resolved separately
/// after selection so version-info reads only happen for surfaced rows.
fn top_cpu_procs(rows: Vec<(String, u32, f32)>, cpu_count: f32, n: usize) -> Vec<ProcCpu> {
    let mut rows: Vec<(String, u32, f32)> = rows
        .into_iter()
        .map(|(name, pid, cpu)| (name, pid, cpu / cpu_count))
        .filter(|(_, _, c)| *c > 0.0)
        .collect();
    rows.sort_by(|a, b| b.2.partial_cmp(&a.2).unwrap_or(std::cmp::Ordering::Equal));
    rows.into_iter()
        .take(n)
        .map(|(name, pid, cpu_pct)| ProcCpu {
            name,
            pid,
            cpu_pct,
            desc: None,
        })
        .collect()
}

/// Top-5 memory consumers from (name, pid, bytes) rows ([ram S-15]).
fn top_mem_procs(rows: Vec<(String, u32, u64)>) -> Vec<ProcMem> {
    let mut rows: Vec<(String, u32, u64)> = rows.into_iter().filter(|(_, _, m)| *m > 0).collect();
    rows.sort_by(|a, b| b.2.cmp(&a.2));
    rows.into_iter()
        .take(5)
        .map(|(name, pid, mem_bytes)| ProcMem {
            name,
            pid,
            mem_bytes,
        })
        .collect()
}

/// Gather process-table metrics: total process count, total thread count, and
/// the top CPU / memory consumers. Returns `(processes_total, threads_total,
/// cpu_top, ram_top)`.
///
/// Windows enumerates every process via `NtQuerySystemInformation` so protected
/// and elevated processes are covered (issue #50) and the thread count is real;
/// other targets fall back to sysinfo's process table.
#[cfg(target_os = "windows")]
fn collect_proc_metrics(
    state: &mut SysState,
    cpu_count: f32,
) -> (u32, u32, Vec<ProcCpu>, Vec<ProcMem>) {
    let now = Instant::now();
    let raw = query_system_processes();

    // Counts exclude the idle process (PID 0), matching its exclusion from the
    // CPU rows.
    let real = || raw.iter().filter(|p| p.pid != 0);
    let total = real().count() as u32;
    let threads = real().map(|p| p.threads).sum();

    let cpu_time_rows: Vec<(String, u32, u64)> = raw
        .iter()
        .map(|p| (p.name.clone(), p.pid, p.cpu_time_100ns))
        .collect();
    let (prev_map, elapsed_100ns) = match &state.prev_proc_cpu {
        Some((m, t)) => (m.clone(), (now.duration_since(*t).as_nanos() / 100) as u64),
        None => (HashMap::new(), 0),
    };
    let cpu_rows = proc_enum::proc_cpu_rows(&cpu_time_rows, &prev_map, elapsed_100ns);
    let mut cpu_top = top_cpu_procs(cpu_rows, cpu_count, CPU_TOP_N);
    // [S-25] friendly names for the surfaced rows only (cached per exe path).
    for p in cpu_top.iter_mut() {
        p.desc = resolve_proc_desc(p.pid, &mut state.proc_desc_cache);
    }

    let mem_rows: Vec<(String, u32, u64)> = real()
        .map(|p| (p.name.clone(), p.pid, p.ws_bytes))
        .collect();
    let ram_top = top_mem_procs(mem_rows);

    // Persist this sample's cumulative times for the next delta.
    let next_map: HashMap<u32, u64> = cpu_time_rows
        .into_iter()
        .map(|(_, pid, t)| (pid, t))
        .collect();
    state.prev_proc_cpu = Some((next_map, now));

    (total, threads, cpu_top, ram_top)
}

#[cfg(not(target_os = "windows"))]
fn collect_proc_metrics(
    state: &mut SysState,
    cpu_count: f32,
) -> (u32, u32, Vec<ProcCpu>, Vec<ProcMem>) {
    use sysinfo::ProcessRefreshKind;
    state
        .sys
        .refresh_processes_specifics(ProcessRefreshKind::new().with_cpu().with_memory());
    let procs: Vec<_> = state.sys.processes().values().collect();
    let total = procs.len() as u32;
    let cpu_rows: Vec<(String, u32, f32)> = procs
        .iter()
        .map(|p| (p.name().to_string(), p.pid().as_u32(), p.cpu_usage()))
        .collect();
    let mem_rows: Vec<(String, u32, u64)> = procs
        .iter()
        .map(|p| (p.name().to_string(), p.pid().as_u32(), p.memory()))
        .collect();
    (
        total,
        total,
        top_cpu_procs(cpu_rows, cpu_count, CPU_TOP_N),
        top_mem_procs(mem_rows),
    )
}

pub fn get_system_stats_inner(include_procs: bool, include_temp: bool) -> SystemStats {
    if include_temp {
        ensure_temp_sampler();
    }

    let lock = sys_state();
    let mut state = lock.lock().unwrap_or_else(|e| e.into_inner());

    // 500 ms debounce: multiple widgets polled simultaneously share one refresh.
    // A procs-needing caller bypasses a cache built without procs ([S-21]).
    let cache_fresh = state
        .last_refresh
        .map(|last| last.elapsed().as_millis() < 500)
        .unwrap_or(false);
    if serve_cached(cache_fresh, state.cached_has_procs, include_procs) {
        if let Some(cached) = &state.cached {
            return cached.clone();
        }
    }

    // Refresh both usage AND frequency: `refresh_cpu_usage()` alone leaves
    // `Cpu::frequency()` at 0, so the frontend treats it as unavailable and
    // hides the GHz readout (issue #40). `with_frequency()` populates it.
    state
        .sys
        .refresh_cpu_specifics(CpuRefreshKind::new().with_cpu_usage().with_frequency());
    state.sys.refresh_memory();
    state.networks.refresh();

    let cpu_usage = state.sys.global_cpu_info().cpu_usage();
    let cpu_cores: Vec<f32> = state.sys.cpus().iter().map(|c| c.cpu_usage()).collect();
    let cpu_freq_mhz = state.sys.cpus().first().map(|c| c.frequency()).unwrap_or(0);
    let cpu_count = state.sys.cpus().len().max(1) as f32;

    let ram_used = state.sys.used_memory();
    let ram_total = state.sys.total_memory();
    let ram_available = state.sys.available_memory();
    let swap_used = state.sys.used_swap();
    let swap_total = state.sys.total_swap();
    let cpu_uptime_s = System::uptime();

    // Process metrics only when a widget asked for them: enumerating the
    // process table is the most expensive part of a tick ([S-21]).
    let (cpu_processes_total, cpu_threads_total, cpu_top, ram_top) = if include_procs {
        collect_proc_metrics(&mut state, cpu_count)
    } else {
        (0, 0, Vec::new(), Vec::new())
    };

    // Per-disk I/O via PDH LogicalDisk counters
    let per_disk_io: HashMap<String, (u64, u64)> = state
        .logical_disk_pdh
        .as_ref()
        .map(sample_per_disk)
        .unwrap_or_default();

    // Cached volume list: per tick only free space is refreshed; the full
    // re-enumeration runs at most every DISK_LIST_REFRESH ([disk S-21]).
    if disk_list_stale(state.disks_list_at.elapsed()) {
        state.disks.refresh_list();
        state.disks_list_at = Instant::now();
    } else {
        state.disks.refresh();
    }
    let mut disks: Vec<DiskStats> = Vec::new();
    for d in state.disks.iter().filter(|d| d.total_space() > 0) {
        let mount = d.mount_point().to_string_lossy().to_string();
        let key = normalize_mount(&mount);
        let (read_per_sec, write_per_sec) = per_disk_io.get(&key).copied().unwrap_or((0, 0));
        disks.push(DiskStats {
            mount,
            used: d.total_space().saturating_sub(d.available_space()),
            total: d.total_space(),
            read_per_sec,
            write_per_sec,
        });
    }

    // Net aggregate + per-interface.
    // sysinfo's received()/transmitted() are per-refresh deltas (depend on poll interval),
    // so derive a real bytes/sec rate from the cumulative totals over the elapsed wall time.
    let samples: Vec<IfaceSample> = state
        .networks
        .iter()
        .map(|(name, data)| {
            (
                name.clone(),
                data.total_received(),
                data.total_transmitted(),
                data.received(),
                data.transmitted(),
            )
        })
        .collect();
    let (cum_rx, cum_tx, iface_deltas) = aggregate_net_ifaces(samples);

    let net_now = Instant::now();
    // elapsed = 0.0 on the first sample (no prev_net) -> bytes_per_sec reports 0.
    let elapsed_secs = state
        .prev_net
        .map(|(_, _, t)| net_now.duration_since(t).as_secs_f64())
        .unwrap_or(0.0);
    let (net_rx_total, net_tx_total) = match state.prev_net {
        Some((prx, ptx, _)) => (
            bytes_per_sec(prx, cum_rx, elapsed_secs),
            bytes_per_sec(ptx, cum_tx, elapsed_secs),
        ),
        // First sample: no baseline yet, report 0 rather than a poll-interval-dependent value.
        None => (0, 0),
    };
    state.prev_net = Some((cum_rx, cum_tx, net_now));

    // Per-interface rate: divide each interface's per-refresh delta by the same elapsed window.
    let mut net_interfaces: Vec<NetIface> = iface_deltas
        .into_iter()
        .map(|(name, rx, tx)| NetIface {
            name,
            rx_per_sec: bytes_per_sec(0, rx, elapsed_secs),
            tx_per_sec: bytes_per_sec(0, tx, elapsed_secs),
            is_up: true,
        })
        .collect();
    net_interfaces
        .sort_by(|a, b| (b.rx_per_sec + b.tx_per_sec).cmp(&(a.rx_per_sec + a.tx_per_sec)));

    // Session totals = bytes since this session's first sample. Baseline the cumulative
    // counters on first sight, then report (cumulative - baseline).
    let session_baseline_rx = *state.session_start_rx.get_or_insert(cum_rx);
    let session_baseline_tx = *state.session_start_tx.get_or_insert(cum_tx);
    let session_rx = cum_rx.saturating_sub(session_baseline_rx);
    let session_tx = cum_tx.saturating_sub(session_baseline_tx);

    let (disk_read_per_sec, disk_write_per_sec, disk_read_ops_per_sec, disk_write_ops_per_sec) =
        disk_io_rate(&mut state.prev_disk_io);

    let cpu_temp_c: Option<f32> = cpu_temp_snapshot();
    let cpu_fan_rpm: Option<u32> = cpu_fan_snapshot();

    // Public IP: serve cached value, refresh in background if stale.
    let stale = state
        .public_ip_fetched_at
        .map(|t| t.elapsed() > PUBLIC_IP_TTL)
        .unwrap_or(true);
    let public_ip = state.public_ip_cache.clone();
    if stale {
        // Mark fetched_at now so we don't spawn multiple concurrent fetches.
        state.public_ip_fetched_at = Some(Instant::now());
        drop(state); // release lock before background spawn touches it
        fetch_public_ip_background();
        let state = lock.lock().unwrap_or_else(|e| e.into_inner());
        let stats = build_stats(
            cpu_usage,
            cpu_cores,
            cpu_freq_mhz,
            cpu_temp_c,
            cpu_fan_rpm,
            cpu_processes_total,
            cpu_threads_total,
            cpu_top,
            cpu_uptime_s,
            ram_used,
            ram_total,
            ram_available,
            swap_used,
            swap_total,
            ram_top,
            disks,
            net_rx_total,
            net_tx_total,
            net_interfaces,
            session_rx,
            session_tx,
            public_ip,
            disk_read_per_sec,
            disk_write_per_sec,
            disk_read_ops_per_sec,
            disk_write_ops_per_sec,
        );
        return finalize(stats, include_procs, state);
    }

    let stats = build_stats(
        cpu_usage,
        cpu_cores,
        cpu_freq_mhz,
        cpu_temp_c,
        cpu_fan_rpm,
        cpu_processes_total,
        cpu_threads_total,
        cpu_top,
        cpu_uptime_s,
        ram_used,
        ram_total,
        ram_available,
        swap_used,
        swap_total,
        ram_top,
        disks,
        net_rx_total,
        net_tx_total,
        net_interfaces,
        session_rx,
        session_tx,
        public_ip,
        disk_read_per_sec,
        disk_write_per_sec,
        disk_read_ops_per_sec,
        disk_write_ops_per_sec,
    );
    finalize(stats, include_procs, state)
}

// Assembles the flat CpuStats payload from individually-gathered metrics; the
// arguments are the metrics themselves, so packing them into a struct first
// would only move the same fields around.
#[allow(clippy::too_many_arguments)]
fn build_stats(
    cpu_usage: f32,
    cpu_cores: Vec<f32>,
    cpu_freq_mhz: u64,
    cpu_temp_c: Option<f32>,
    cpu_fan_rpm: Option<u32>,
    cpu_processes_total: u32,
    cpu_threads_total: u32,
    cpu_top: Vec<ProcCpu>,
    cpu_uptime_s: u64,
    ram_used: u64,
    ram_total: u64,
    ram_available: u64,
    swap_used: u64,
    swap_total: u64,
    ram_top: Vec<ProcMem>,
    disks: Vec<DiskStats>,
    net_rx_per_sec: u64,
    net_tx_per_sec: u64,
    net_interfaces: Vec<NetIface>,
    net_session_rx: u64,
    net_session_tx: u64,
    public_ip: Option<String>,
    disk_read_per_sec: u64,
    disk_write_per_sec: u64,
    disk_read_ops_per_sec: u64,
    disk_write_ops_per_sec: u64,
) -> SystemStats {
    SystemStats {
        cpu_usage,
        cpu_cores,
        cpu_freq_mhz,
        cpu_temp_c,
        cpu_fan_rpm,
        cpu_processes_total,
        cpu_threads_total,
        cpu_top,
        cpu_uptime_s,
        ram_used,
        ram_total,
        ram_available,
        swap_used,
        swap_total,
        ram_top,
        disks,
        net_rx_per_sec,
        net_tx_per_sec,
        net_interfaces,
        net_session_rx,
        net_session_tx,
        public_ip,
        disk_read_per_sec,
        disk_write_per_sec,
        disk_read_ops_per_sec,
        disk_write_ops_per_sec,
    }
}

fn finalize(
    stats: SystemStats,
    has_procs: bool,
    mut state: std::sync::MutexGuard<SysState>,
) -> SystemStats {
    state.cached = Some(stats.clone());
    state.cached_has_procs = has_procs;
    state.last_refresh = Some(Instant::now());
    stats
}

#[cfg(test)]
mod tests {
    use super::*;

    // Regression: write IOPS previously read buf[5] (a different perf lane) instead of the
    // high dword of buf[4]. IoReadOperationCount/IoWriteOperationCount are two adjacent u32
    // fields packed into the single u64 lane buf[4]: read_ops = low dword, write_ops = high.
    #[test]
    fn parse_disk_io_totals_field_mapping() {
        let mut buf = [0u64; 64];
        buf[1] = 1_111; // IoReadTransferCount  (bytes read)
        buf[2] = 2_222; // IoWriteTransferCount (bytes written)
                        // buf[4] packs write_ops in the high dword and read_ops in the low dword.
        let read_ops: u64 = 0x0000_0001_u64; //  1
        let write_ops: u64 = 0x0000_00AB_u64; // 171
        buf[4] = (write_ops << 32) | read_ops;
        buf[5] = 0xDEAD_BEEF; // unrelated lane: must NOT leak into write_ops

        let (rb, wb, ro, wo) = parse_disk_io_totals(&buf);
        assert_eq!(rb, 1_111);
        assert_eq!(wb, 2_222);
        assert_eq!(ro, 1);
        assert_eq!(wo, 171);
    }

    #[test]
    fn parse_disk_io_totals_full_dword_range() {
        let mut buf = [0u64; 64];
        let read_ops: u64 = 0xFFFF_FFFF; // max u32 in low dword
        let write_ops: u64 = 0x1234_5678; // arbitrary high dword
        buf[4] = (write_ops << 32) | read_ops;
        let (_, _, ro, wo) = parse_disk_io_totals(&buf);
        assert_eq!(ro, 0xFFFF_FFFF);
        assert_eq!(wo, 0x1234_5678);
    }

    // Regression: net rx/tx must be a true bytes/sec rate derived from cumulative-counter
    // deltas over the elapsed window, independent of the polling interval.
    #[test]
    fn bytes_per_sec_is_rate_not_per_refresh_delta() {
        // 1000 bytes over 2s -> 500 B/s, regardless of how often we polled.
        assert_eq!(bytes_per_sec(1_000, 2_000, 2.0), 500);
        // Same 1000-byte delta over 0.5s -> 2000 B/s.
        assert_eq!(bytes_per_sec(1_000, 2_000, 0.5), 2_000);
    }

    #[test]
    fn bytes_per_sec_first_sample_and_zero_window_report_zero() {
        // Elapsed ~0 (first sample / debounce) reports 0, never a divide-by-tiny blowup.
        assert_eq!(bytes_per_sec(0, 5_000, 0.0), 0);
        assert_eq!(bytes_per_sec(100, 5_000, 0.001), 0);
    }

    #[test]
    fn bytes_per_sec_counter_reset_saturates_to_zero() {
        // If the cumulative counter goes backwards, saturating_sub keeps the rate at 0.
        assert_eq!(bytes_per_sec(5_000, 1_000, 1.0), 0);
    }

    /// Characterization: regular interfaces all contribute to the aggregate
    /// cumulative totals and each appears in the per-interface delta list.
    #[test]
    fn aggregate_sums_all_regular_ifaces() {
        let (cum_rx, cum_tx, deltas) = aggregate_net_ifaces(vec![
            ("Ethernet".into(), 1_000, 500, 100, 50),
            ("Wi-Fi".into(), 200, 300, 20, 30),
        ]);
        assert_eq!(cum_rx, 1_200);
        assert_eq!(cum_tx, 800);
        assert_eq!(
            deltas,
            vec![
                ("Ethernet".into(), 100u64, 50u64),
                ("Wi-Fi".into(), 20u64, 30u64),
            ]
        );
    }

    /// [S-17] Capture-driver mirror rows are recognised by name: Npcap and
    /// legacy WinPcap filter instances follow "<iface>-<driver>-<instance>".
    #[test]
    fn s17_pcap_mirror_iface_names_detected() {
        assert!(is_capture_mirror_iface(
            "Ethernet-Npcap Packet Driver (NPCAP)-0000"
        ));
        assert!(is_capture_mirror_iface(
            "Wi-Fi-Npcap Packet Driver (NPCAP)-0001"
        ));
        assert!(is_capture_mirror_iface(
            "Ethernet-WinPcap Packet Driver (NPF)-0000"
        ));
        assert!(!is_capture_mirror_iface("Ethernet"));
        assert!(!is_capture_mirror_iface("Wi-Fi"));
        assert!(!is_capture_mirror_iface("vEthernet (WSL)"));
        // The standalone Npcap loopback adapter carries its own (loopback)
        // traffic, not a mirror of a physical NIC, so keep it.
        assert!(!is_capture_mirror_iface("Npcap Loopback Adapter"));
    }

    /// [S-17] Regression (themia-releases#33): with Npcap installed, the
    /// mirror adapter re-reports the physical NIC's counters. It must be
    /// excluded from the aggregate totals (else every rate + session total
    /// doubles) and from the per-interface list (else a duplicate row shows).
    #[test]
    fn s17_npcap_mirror_excluded_from_aggregate_and_iface_list() {
        let (cum_rx, cum_tx, deltas) = aggregate_net_ifaces(vec![
            ("Ethernet".into(), 1_000, 500, 100, 50),
            // Identical counters: the mirror of the row above.
            (
                "Ethernet-Npcap Packet Driver (NPCAP)-0000".into(),
                1_000,
                500,
                100,
                50,
            ),
        ]);
        assert_eq!(cum_rx, 1_000, "mirror rx must not double the aggregate");
        assert_eq!(cum_tx, 500, "mirror tx must not double the aggregate");
        assert_eq!(
            deltas,
            vec![("Ethernet".into(), 100u64, 50u64)],
            "mirror must not appear in the per-interface list"
        );
    }

    /// [S-21] Debounce-cache policy: a fresh cache built without process
    /// metrics must not be served to a caller that needs them; a cache built
    /// with them (superset) is served to everyone; stale is never served.
    #[test]
    fn s21_cache_policy_upgrades_when_procs_needed() {
        assert!(serve_cached(true, false, false));
        assert!(
            !serve_cached(true, false, true),
            "procs-needing caller must trigger a refresh, not get the lean cache"
        );
        assert!(serve_cached(true, true, false));
        assert!(serve_cached(true, true, true));
        assert!(!serve_cached(false, true, false));
        assert!(!serve_cached(false, false, true));
    }

    /// Characterization of the former inline top-CPU-list logic ([cpu S-17]):
    /// normalise by core count, drop 0% rows, sort desc, keep `n`.
    #[test]
    fn top_cpu_procs_normalises_filters_sorts_takes_n() {
        let rows = vec![
            ("idle".into(), 1, 0.0), // dropped: 0% after normalising
            ("a".into(), 2, 80.0),
            ("b".into(), 3, 40.0),
            ("c".into(), 4, 20.0),
            ("d".into(), 5, 10.0),
            ("e".into(), 6, 5.0),
            ("f".into(), 7, 2.5), // 6th by usage: trimmed by take(5)
        ];
        let top = top_cpu_procs(rows, 4.0, 5);
        assert_eq!(top.len(), 5);
        assert_eq!(top[0].name, "a");
        assert!(
            (top[0].cpu_pct - 20.0).abs() < f32::EPSILON,
            "80% / 4 cores = 20%"
        );
        assert_eq!(top[4].name, "e");
    }

    /// [S-26] The row cap is a parameter: the CPU widget's count config slices
    /// the frontend list, so the backend must return CPU_TOP_N (10) rows for
    /// the slice to draw from, and a smaller `n` still trims correctly.
    #[test]
    fn s26_top_cpu_procs_honours_requested_count() {
        let rows: Vec<(String, u32, f32)> = (0..12)
            .map(|i| (format!("p{i}.exe"), 100 + i, (12 - i) as f32 * 8.0))
            .collect();
        assert_eq!(top_cpu_procs(rows.clone(), 1.0, 2).len(), 2);
        let top = top_cpu_procs(rows, 1.0, CPU_TOP_N);
        assert_eq!(top.len(), 10, "backend serves up to 10 rows for [S-26]");
        assert_eq!(CPU_TOP_N, 10);
        assert!(
            top.iter().all(|p| p.desc.is_none()),
            "desc is resolved separately after selection ([S-25])"
        );
    }

    /// Characterization of the former inline top-memory-list logic ([ram S-15]):
    /// drop 0-byte rows, sort desc by bytes, keep 5.
    #[test]
    fn top_mem_procs_filters_sorts_takes5() {
        let rows = vec![
            ("zero".into(), 1, 0), // dropped
            ("a".into(), 2, 600),
            ("b".into(), 3, 500),
            ("c".into(), 4, 400),
            ("d".into(), 5, 300),
            ("e".into(), 6, 200),
            ("f".into(), 7, 100), // trimmed by take(5)
        ];
        let top = top_mem_procs(rows);
        assert_eq!(top.len(), 5);
        assert_eq!(top[0].name, "a");
        assert_eq!(top[0].mem_bytes, 600);
        assert_eq!(top[4].name, "e");
    }

    /// [S-20] Characterization of the former read_cpu_temp_c conversion:
    /// tenths-of-Kelvin → °C of the hottest zone; empty or bogus (<100 K)
    /// readings report None so the row hides.
    #[test]
    fn s20_zones_to_celsius_conversion() {
        assert_eq!(zones_to_celsius(&[]), None);
        assert_eq!(zones_to_celsius(&[500]), None);
        let t = zones_to_celsius(&[3032, 2982]).expect("valid zone");
        assert!(
            (t - 30.05).abs() < 0.01,
            "hottest zone 303.2 K = 30.05 °C, got {t}"
        );
    }

    fn lhm(name: &str, ty: &str, value: f32) -> LhmSensor {
        LhmSensor {
            name: name.into(),
            sensor_type: ty.into(),
            value,
        }
    }

    /// [S-20] issue #36: on AMD boards lacking an ACPI thermal zone, the
    /// LibreHardwareMonitor `Sensor` rows carry the on-die Tctl/Tdie/package
    /// temperature. A package/Tctl/Tdie reading is preferred over per-core, and
    /// non-CPU temperatures (GPU/board/drive) must never leak in.
    #[test]
    fn issue_36_lhm_prefers_cpu_package_temperature() {
        let sensors = vec![
            lhm("GPU Core", "Temperature", 71.0), // must be ignored (not CPU)
            lhm("Core #1", "Temperature", 48.0),
            lhm("Core #2", "Temperature", 52.0),
            lhm("CPU Package", "Temperature", 55.0), // preferred
            lhm("Temperature", "Temperature", 30.0), // drive temp w/o cpu name: ignored
        ];
        assert_eq!(lhm_pick_cpu_temp(&sensors), Some(55.0));
    }

    /// [S-20] issue #36: AMD parts often label the die sensor "CPU Core
    /// (Tctl/Tdie)" with no separate package row, and that token is still treated
    /// as the package-level reading.
    #[test]
    fn issue_36_lhm_tctl_tdie_token_is_package_level() {
        let sensors = vec![
            lhm("Core #1", "Temperature", 60.0),
            lhm("CPU Core (Tctl/Tdie)", "Temperature", 63.5),
        ];
        assert_eq!(lhm_pick_cpu_temp(&sensors), Some(63.5));
    }

    /// [S-20] issue #36: with no package/Tctl/Tdie sensor, fall back to the
    /// HOTTEST per-core CPU temperature.
    #[test]
    fn issue_36_lhm_falls_back_to_hottest_core() {
        let sensors = vec![
            lhm("CPU Core #1", "Temperature", 49.0),
            lhm("CPU Core #2", "Temperature", 57.0),
            lhm("CPU Core #3", "Temperature", 51.0),
        ];
        assert_eq!(lhm_pick_cpu_temp(&sensors), Some(57.0));
    }

    /// [S-20] issue #36: LHM reachable but exposing no CPU temperature (only a
    /// GPU/board temp, or an out-of-range bogus value) reports None so the row
    /// hides rather than showing a wrong number.
    #[test]
    fn issue_36_lhm_no_cpu_temp_reports_none() {
        assert_eq!(lhm_pick_cpu_temp(&[]), None);
        assert_eq!(
            lhm_pick_cpu_temp(&[lhm("GPU Hot Spot", "Temperature", 80.0)]),
            None
        );
        // CPU-named but physically impossible (e.g. uninitialised −40 or 999):
        assert_eq!(
            lhm_pick_cpu_temp(&[lhm("CPU Package", "Temperature", 999.0)]),
            None
        );
        assert_eq!(
            lhm_pick_cpu_temp(&[lhm("CPU Package", "Temperature", -40.0)]),
            None
        );
        // A Fan-type row with a CPU name is not a temperature.
        assert_eq!(lhm_pick_cpu_temp(&[lhm("CPU Fan", "Fan", 1200.0)]), None);
    }

    /// [S-23] issue #40: CPU fan RPM comes from an LHM "Fan" sensor whose name
    /// mentions the CPU; the highest plausible reading wins and 0-RPM (stopped/
    /// unpopulated header) is ignored. Non-CPU fans never leak in.
    #[test]
    fn issue_40_lhm_picks_cpu_fan_rpm() {
        let sensors = vec![
            lhm("GPU Fan", "Fan", 1800.0), // not CPU: ignored
            lhm("CPU Fan", "Fan", 1150.0),
            lhm("CPU Opt Fan", "Fan", 0.0), // unpopulated header: ignored
            lhm("CPU Package", "Temperature", 55.0), // not a Fan type: ignored
        ];
        assert_eq!(lhm_pick_cpu_fan(&sensors), Some(1150));
        // No CPU fan exposed -> None (row hides).
        assert_eq!(
            lhm_pick_cpu_fan(&[lhm("Chassis Fan #1", "Fan", 900.0)]),
            None
        );
        assert_eq!(lhm_pick_cpu_fan(&[]), None);
    }

    /// [disk S-21] Volume list is re-enumerated at a 30 s cadence; anything
    /// younger only refreshes free space.
    #[test]
    fn disk_s21_list_reenumeration_cadence() {
        assert!(!disk_list_stale(Duration::from_secs(29)));
        assert!(disk_list_stale(Duration::from_secs(30)));
        assert!(disk_list_stale(Duration::from_secs(31)));
    }

    /// [S-21] Regression: process metrics are gated on the request flag, and
    /// the shared debounce cache upgrades correctly. One test fn, since the shared
    /// static state makes ordering significant.
    #[test]
    fn s21_include_procs_gates_collection_and_cache() {
        // No caller wants procs: the expensive process refresh is skipped and
        // the proc fields come back empty/zero.
        let lean = get_system_stats_inner(false, false);
        assert!(lean.cpu_top.is_empty());
        assert!(lean.ram_top.is_empty());
        assert_eq!(lean.cpu_processes_total, 0);

        // Within the 500 ms debounce window, a procs-needing caller must
        // bypass the lean cache and get real process data.
        let full = get_system_stats_inner(true, false);
        assert!(
            full.cpu_processes_total > 0,
            "expected a real process count"
        );
        assert!(!full.ram_top.is_empty(), "expected top-memory processes");

        // The richer cache (superset) is shared with procs-less callers.
        let again = get_system_stats_inner(false, false);
        assert!(
            again.cpu_processes_total > 0,
            "fresh procs-ful cache should be served"
        );
    }

    /// issue #40 regression: `cpu_freq_mhz` must be populated. The old code
    /// called `refresh_cpu_usage()` only, which leaves `Cpu::frequency()` at 0;
    /// the frontend then treats 0 as "unavailable" and hides the GHz readout.
    /// The fix refreshes `with_frequency()`, so a real CPU reports a nonzero
    /// frequency. (Runs after the cache-policy test seeds shared state.)
    #[test]
    fn issue_40_cpu_frequency_is_populated() {
        let s = get_system_stats_inner(false, false);
        assert!(
            s.cpu_freq_mhz > 0,
            "cpu_freq_mhz must be nonzero (was 0 before the with_frequency() fix); got {}",
            s.cpu_freq_mhz
        );
    }

    // ── issue #50: NtQuerySystemInformation process enumeration ──────────────

    /// Encode one x64 SYSTEM_PROCESS_INFORMATION entry into a fixed 0x100-byte
    /// block (comfortably larger than the fields we read) for buffer-parse tests.
    fn spi_entry(
        next: u32,
        pid: u32,
        threads: u32,
        user_100ns: u64,
        kernel_100ns: u64,
        ws: u64,
    ) -> [u8; 0x100] {
        use super::proc_enum::*;
        let mut e = [0u8; 0x100];
        e[SPI_NEXT_OFFSET..SPI_NEXT_OFFSET + 4].copy_from_slice(&next.to_le_bytes());
        e[SPI_THREAD_COUNT..SPI_THREAD_COUNT + 4].copy_from_slice(&threads.to_le_bytes());
        e[SPI_USER_TIME..SPI_USER_TIME + 8].copy_from_slice(&user_100ns.to_le_bytes());
        e[SPI_KERNEL_TIME..SPI_KERNEL_TIME + 8].copy_from_slice(&kernel_100ns.to_le_bytes());
        e[SPI_UNIQUE_PID..SPI_UNIQUE_PID + 8].copy_from_slice(&(pid as u64).to_le_bytes());
        e[SPI_WORKING_SET..SPI_WORKING_SET + 8].copy_from_slice(&ws.to_le_bytes());
        e
    }

    /// [cpu S-17] issue #50: the SYSTEM_PROCESS_INFORMATION field offsets and
    /// NextEntryOffset chaining must stay exact: a drift silently zeroes CPU for
    /// every process. Two chained entries parse to pid / threads / (kernel+user)
    /// / working-set, and a zero NextEntryOffset ends the walk.
    #[test]
    fn issue_50_parses_system_process_info_buffer() {
        let mut buf = Vec::new();
        buf.extend_from_slice(&spi_entry(0x100, 1234, 12, 700, 300, 4096));
        buf.extend_from_slice(&spi_entry(0, 5678, 3, 10, 5, 2048));
        let entries = super::proc_enum::parse_system_process_info(&buf);
        assert_eq!(entries.len(), 2);
        assert_eq!(entries[0].pid, 1234);
        assert_eq!(entries[0].threads, 12);
        assert_eq!(entries[0].cpu_time_100ns, 1000, "kernel+user summed");
        assert_eq!(entries[0].ws_bytes, 4096);
        assert_eq!(entries[1].pid, 5678);
        assert_eq!(entries[1].threads, 3);
        assert_eq!(entries[1].cpu_time_100ns, 15);
        assert_eq!(entries[1].ws_bytes, 2048);
    }

    /// issue #50: the walk never reads out of bounds on a truncated / malformed
    /// buffer: a buffer too small for one entry yields nothing, and a
    /// NextEntryOffset pointing past the end stops after the valid entry.
    #[test]
    fn issue_50_parse_is_bounds_safe_on_truncation() {
        use super::proc_enum::{parse_system_process_info, SPI_MIN_ENTRY};
        assert!(parse_system_process_info(&[0u8; SPI_MIN_ENTRY - 1]).is_empty());
        let mut buf = spi_entry(0x100, 42, 1, 1, 1, 0).to_vec();
        buf.truncate(SPI_MIN_ENTRY + 4); // valid first entry, no room for a second
        let entries = parse_system_process_info(&buf);
        assert_eq!(entries.len(), 1);
        assert_eq!(entries[0].pid, 42);
    }

    /// [cpu S-17] issue #50: per-process CPU% is the cumulative-time delta over
    /// the elapsed window on sysinfo's single-core scale. A new PID (absent from
    /// `prev`) and the idle process (PID 0) contribute nothing.
    #[test]
    fn issue_50_proc_cpu_rows_deltas_and_exclusions() {
        use super::proc_enum::proc_cpu_rows;
        let elapsed = 10_000_000u64; // 1 s in 100 ns units
        let mut prev = HashMap::new();
        prev.insert(0u32, 9_999_999_999); // idle: huge, must be excluded
        prev.insert(10u32, 0); // used 0.5 core since prev
        prev.insert(11u32, 0); // used 0.1 core since prev
        let entries = vec![
            ("Idle".to_string(), 0u32, 9_999_999_999 + 5_000_000),
            ("half.exe".to_string(), 10u32, 5_000_000),
            ("tenth.exe".to_string(), 11u32, 1_000_000),
            ("fresh.exe".to_string(), 12u32, 8_000_000), // new PID → 0 this tick
        ];
        let rows = proc_cpu_rows(&entries, &prev, elapsed);
        assert!(
            rows.iter().all(|(_, pid, _)| *pid != 0),
            "idle PID 0 excluded"
        );
        let get = |pid: u32| rows.iter().find(|(_, p, _)| *p == pid).map(|(_, _, c)| *c);
        assert!(
            (get(10).unwrap() - 50.0).abs() < 1e-3,
            "0.5 core = 50% single-core"
        );
        assert!((get(11).unwrap() - 10.0).abs() < 1e-3);
        assert_eq!(
            get(12).unwrap(),
            0.0,
            "new PID contributes 0 on first sight"
        );
    }

    /// issue #50: a zero elapsed window (first sample / debounce) yields 0 for
    /// every process rather than dividing by ~0.
    #[test]
    fn issue_50_proc_cpu_rows_zero_window_is_zero() {
        use super::proc_enum::proc_cpu_rows;
        let mut prev = HashMap::new();
        prev.insert(1u32, 0);
        let entries = vec![("a".to_string(), 1u32, 5_000_000)];
        let rows = proc_cpu_rows(&entries, &prev, 0);
        assert_eq!(rows[0].2, 0.0);
    }

    /// issue #50 regression: a protected / elevated process that sysinfo reports
    /// as 0% CPU (its handle un-openable when Themia is unelevated) still
    /// surfaces in the top list, from its NtQuerySystemInformation time delta.
    /// "MsMpEng.exe" burns a full core while a foreground app uses a sliver; the
    /// protected process must rank first.
    #[test]
    fn issue_50_protected_process_surfaces_in_top() {
        use super::proc_enum::proc_cpu_rows;
        let elapsed = 10_000_000u64; // 1 s
        let mut prev = HashMap::new();
        prev.insert(100u32, 0); // MsMpEng
        prev.insert(200u32, 0); // notepad
        let entries = vec![
            ("MsMpEng.exe".to_string(), 100u32, 10_000_000), // 1 full core
            ("notepad.exe".to_string(), 200u32, 200_000),    // 0.02 core
        ];
        let rows = proc_cpu_rows(&entries, &prev, elapsed);
        // 8 logical cores: single-core % / 8 for the Task-Manager-style display.
        let top = top_cpu_procs(rows, 8.0, CPU_TOP_N);
        assert_eq!(
            top[0].name, "MsMpEng.exe",
            "protected process must rank first"
        );
        assert!(
            (top[0].cpu_pct - 12.5).abs() < 1e-2,
            "100% / 8 cores = 12.5%"
        );
        assert!(top.iter().any(|p| p.name == "notepad.exe"));
    }

    // ── [S-25]: version-info FileDescription ("real" process name) ────────────

    /// [S-25] The StringFileInfo sub-blocks are tried in translation-table
    /// order, with the en-US/Unicode block appended as a fallback (many exes
    /// carry only that block); the fallback is not duplicated when the table
    /// already lists it.
    #[test]
    fn s25_desc_subblocks_order_and_fallback() {
        assert_eq!(
            desc_subblocks(&[(0x0407, 0x04B0)]),
            vec![
                "\\StringFileInfo\\040704B0\\FileDescription".to_string(),
                "\\StringFileInfo\\040904B0\\FileDescription".to_string(),
            ]
        );
        // Empty translation table (malformed version resource): fallback only.
        assert_eq!(
            desc_subblocks(&[]),
            vec!["\\StringFileInfo\\040904B0\\FileDescription".to_string(),]
        );
        // en-US already listed: no duplicate.
        assert_eq!(desc_subblocks(&[(0x0409, 0x04B0)]).len(), 1);
    }

    /// [S-25] A read description is trimmed; blank/empty readings count as
    /// absent so the frontend falls back to the image name instead of showing
    /// an empty row label.
    #[test]
    fn s25_clean_desc_trims_and_rejects_blank() {
        assert_eq!(
            clean_desc("Google Chrome".into()),
            Some("Google Chrome".into())
        );
        assert_eq!(clean_desc("  Spotify  ".into()), Some("Spotify".into()));
        assert_eq!(clean_desc(String::new()), None);
        assert_eq!(clean_desc("   ".into()), None);
    }

    /// [S-25] Live: a well-known system DLL always carries a FileDescription;
    /// the resolver must return a non-empty string for it. (kernel32.dll rather
    /// than a process: version-info reading is path-based, and this file exists
    /// on every Windows install regardless of elevation.)
    #[cfg(target_os = "windows")]
    #[test]
    fn s25_file_description_reads_kernel32() {
        let root = std::env::var("SystemRoot").unwrap_or_else(|_| "C:\\Windows".into());
        let desc = file_description(&format!("{root}\\System32\\kernel32.dll"));
        let desc = desc.expect("kernel32.dll must expose a FileDescription");
        assert!(!desc.trim().is_empty());
    }
}
