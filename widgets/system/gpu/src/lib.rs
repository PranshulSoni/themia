use std::collections::HashMap;
use std::sync::{Mutex, OnceLock};
use std::time::Instant;

// ── Public serialisable types ────────────────────────────────────────────────

#[derive(serde::Serialize, Clone, Default)]
pub struct GpuStats {
    pub name: String,
    /// 0–100 (3D engine utilisation %)
    pub utilization: f32,
    /// Dedicated VRAM currently in use (MB)
    pub vram_used_mb: u64,
    /// Dedicated VRAM total capacity (MB, 0 if unknown)
    pub vram_total_mb: u64,
    /// Shared system memory currently used by GPU (MB)
    pub shared_used_mb: u64,
    /// Shared system memory budget (MB, ~half of system RAM on Windows)
    pub shared_total_mb: u64,
    /// GPU core temperature (°C). NVIDIA only for now.
    pub temp_c: Option<f32>,
    /// Instantaneous power draw (W). NVIDIA only.
    pub power_w: Option<f32>,
    /// Fan speed percent (0-100). NVIDIA only.
    pub fan_pct: Option<u32>,
    /// Video encoder utilisation %. NVIDIA only.
    pub encoder_pct: Option<f32>,
    /// Video decoder utilisation %. NVIDIA only.
    pub decoder_pct: Option<f32>,
    /// Core/graphics clock (MHz). NVIDIA only.
    pub core_clock_mhz: Option<u32>,
    /// Memory clock (MHz). NVIDIA only.
    pub mem_clock_mhz: Option<u32>,
}

#[derive(serde::Serialize, Clone)]
pub struct GpuInfo {
    pub index: u32,
    pub name:  String,
}

// ── GPU: Windows registry helpers ────────────────────────────────────────────

#[cfg(target_os = "windows")]
#[link(name = "advapi32")]
extern "system" {
    fn RegOpenKeyExW(
        hKey: isize,
        lpSubKey: *const u16,
        ulOptions: u32,
        samDesired: u32,
        phkResult: *mut isize,
    ) -> i32;
    fn RegQueryValueExW(
        hKey: isize,
        lpValueName: *const u16,
        lpReserved: *const u32,
        lpType: *mut u32,
        lpData: *mut u8,
        lpcbData: *mut u32,
    ) -> i32;
    fn RegCloseKey(hKey: isize) -> i32;
}

#[cfg(target_os = "windows")]
const HKLM: isize = 0x80000002u32 as i32 as isize;
#[cfg(target_os = "windows")]
const KEY_READ: u32 = 0x20019;
#[cfg(target_os = "windows")]
const REG_SZ: u32 = 1;
#[cfg(target_os = "windows")]
const REG_BINARY: u32 = 3;
#[cfg(target_os = "windows")]
const REG_DWORD: u32 = 4;
#[cfg(target_os = "windows")]
const REG_QWORD: u32 = 11;

#[cfg(target_os = "windows")]
fn wstr(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0u16)).collect()
}

/// Read name, VRAM total (bytes), and LUID prefix string for a GPU at the given registry index.
#[cfg(target_os = "windows")]
fn read_gpu_registry(index: u32) -> (String, u64, Option<String>) {
    unsafe {
        let subkey = wstr(&format!(
            "SYSTEM\\CurrentControlSet\\Control\\Class\\\
             {{4d36e968-e325-11ce-bfc1-08002be10318}}\\{:04}",
            index
        ));
        let mut hkey: isize = 0;
        if RegOpenKeyExW(HKLM, subkey.as_ptr(), 0, KEY_READ, &mut hkey) != 0 {
            return (String::new(), 0, None);
        }

        // ── GPU name ──
        let val_name = wstr("DriverDesc");
        let mut kind: u32 = 0;
        let mut sz: u32 = 0;
        RegQueryValueExW(hkey, val_name.as_ptr(), std::ptr::null(), &mut kind, std::ptr::null_mut(), &mut sz);
        let name = if kind == REG_SZ && sz > 0 {
            let mut buf = vec![0u8; sz as usize];
            RegQueryValueExW(hkey, val_name.as_ptr(), std::ptr::null(), &mut kind, buf.as_mut_ptr(), &mut sz);
            let wchars: Vec<u16> = buf.chunks_exact(2)
                .map(|c| u16::from_le_bytes([c[0], c[1]]))
                .take_while(|&c| c != 0)
                .collect();
            String::from_utf16_lossy(&wchars)
        } else {
            String::new()
        };

        if name.is_empty() {
            RegCloseKey(hkey);
            return (String::new(), 0, None);
        }

        // ── VRAM total ──
        let vram_bytes: u64 = {
            let val_qw = wstr("HardwareInformation.qwMemorySize");
            let mut kind: u32 = 0;
            let mut sz: u32 = 8;
            let mut buf8 = [0u8; 8];
            let ret = RegQueryValueExW(hkey, val_qw.as_ptr(), std::ptr::null(), &mut kind, buf8.as_mut_ptr(), &mut sz);
            if ret == 0 && sz == 8 && (kind == REG_QWORD || kind == REG_BINARY) {
                u64::from_le_bytes(buf8)
            } else {
                let val_dw = wstr("HardwareInformation.MemorySize");
                let mut kind2: u32 = 0;
                let mut sz2: u32 = 8;
                let mut buf4 = [0u8; 8];
                RegQueryValueExW(hkey, val_dw.as_ptr(), std::ptr::null(), &mut kind2, buf4.as_mut_ptr(), &mut sz2);
                match (kind2, sz2) {
                    (REG_QWORD, 8) | (REG_BINARY, 8) => u64::from_le_bytes(buf4),
                    (REG_DWORD, _) | (_, 4)          => u32::from_le_bytes([buf4[0], buf4[1], buf4[2], buf4[3]]) as u64,
                    _                                 => 0,
                }
            }
        };

        // ── Adapter LUID ──
        let val_luid = wstr("AdapterLuid");
        let mut kind3: u32 = 0;
        let mut sz3: u32 = 8;
        let mut luid_bytes = [0u8; 8];
        RegQueryValueExW(hkey, val_luid.as_ptr(), std::ptr::null(), &mut kind3, luid_bytes.as_mut_ptr(), &mut sz3);
        let luid_prefix = if sz3 == 8 {
            let lo = u32::from_le_bytes([luid_bytes[0], luid_bytes[1], luid_bytes[2], luid_bytes[3]]);
            let hi = u32::from_le_bytes([luid_bytes[4], luid_bytes[5], luid_bytes[6], luid_bytes[7]]);
            if lo != 0 || hi != 0 {
                Some(format!("luid_0x{:08x}_0x{:08x}", hi, lo))
            } else {
                None
            }
        } else {
            None
        };

        RegCloseKey(hkey);
        (name, vram_bytes, luid_prefix)
    }
}

#[cfg(not(target_os = "windows"))]
fn read_gpu_registry(_index: u32) -> (String, u64, Option<String>) { (String::new(), 0, None) }

/// Enumerate all GPU adapters found in the registry (up to 16).
pub fn list_gpus_inner() -> Vec<GpuInfo> {
    let mut gpus = Vec::new();
    for i in 0u32..16 {
        let (name, _, _) = read_gpu_registry(i);
        if name.is_empty() { break; }
        gpus.push(GpuInfo { index: i, name });
    }
    gpus
}

// ── GPU: PDH utilisation + VRAM used ─────────────────────────────────────────

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
const PDH_FMT_LARGE: u32 = 0x0000_0400;

#[cfg(target_os = "windows")]
#[repr(C)]
struct PdhItemF64 {
    _name:   *const u16,
    _status: u32,
    _pad:    u32,
    value:   f64,
}

#[cfg(target_os = "windows")]
#[repr(C)]
struct PdhItemI64 {
    _name:   *const u16,
    _status: u32,
    _pad:    u32,
    value:   i64,
}

struct GpuPdhState {
    query:          isize,
    util_counter:   isize,
    vram_counter:   isize,
    shared_counter: isize,
}

#[cfg(target_os = "windows")]
unsafe impl Send for GpuPdhState {}
#[cfg(target_os = "windows")]
unsafe impl Sync for GpuPdhState {}

#[cfg(not(target_os = "windows"))]
unsafe impl Send for GpuPdhState {}
#[cfg(not(target_os = "windows"))]
unsafe impl Sync for GpuPdhState {}

struct GpuPerIndex {
    name:         String,
    vram_total:   u64,
    luid_prefix:  Option<String>,
    cached:       Option<GpuStats>,
    last_refresh: Option<Instant>,
}

struct GlobalGpuState {
    pdh:       Option<GpuPdhState>,
    per_index: HashMap<u32, GpuPerIndex>,
    nvml:      Option<nvml_wrapper::Nvml>,
}

static GPU_STATE: OnceLock<Mutex<GlobalGpuState>> = OnceLock::new();

fn gpu_state() -> &'static Mutex<GlobalGpuState> {
    GPU_STATE.get_or_init(|| {
        let pdh = init_gpu_pdh();
        let nvml = nvml_wrapper::Nvml::init().ok();
        Mutex::new(GlobalGpuState {
            pdh,
            per_index: HashMap::new(),
            nvml,
        })
    })
}

/// Best-effort NVIDIA-specific sensor sampling. Returns None for any field whose
/// driver call fails or whose hardware doesn't expose it.
struct NvSample {
    /// True GPU-core utilisation % (matches nvidia-smi). Source of truth on NVIDIA
    /// hardware; unaffected by the foreground/present-mode quirks that make the PDH
    /// `engtype_3D` counter under-report for borderless-fullscreen games.
    gpu_util: Option<f32>,
    temp_c: Option<f32>,
    power_w: Option<f32>,
    fan_pct: Option<u32>,
    encoder_pct: Option<f32>,
    decoder_pct: Option<f32>,
    core_clock_mhz: Option<u32>,
    mem_clock_mhz: Option<u32>,
}

impl NvSample {
    /// All-`None` sample: NVML missing or the device handle couldn't be opened.
    fn empty() -> Self {
        NvSample {
            gpu_util: None,
            temp_c: None, power_w: None, fan_pct: None,
            encoder_pct: None, decoder_pct: None,
            core_clock_mhz: None, mem_clock_mhz: None,
        }
    }
}

/// Pick the utilisation Themia reports: prefer the accurate NVML reading on NVIDIA
/// cards, fall back to the PDH `engtype_3D` sum on non-NVIDIA / NVML-missing hardware.
/// Clamped to 0–100 so neither source can emit an out-of-range bar.
fn choose_utilization(nvml_util: Option<f32>, pdh_util: f32) -> f32 {
    nvml_util.unwrap_or(pdh_util).clamp(0.0, 100.0)
}

fn sample_nvml(nvml: &nvml_wrapper::Nvml, name: &str, index: u32) -> NvSample {
    use nvml_wrapper::enum_wrappers::device::{Clock, TemperatureSensor};

    // Match by name first (most reliable when there's >1 NVIDIA card); fall back to the
    // registry index, then index 0, only when no device name matches.
    let by_name = if name.is_empty() {
        None
    } else {
        let count = nvml.device_count().unwrap_or(0);
        (0..count).find_map(|i| {
            let d = nvml.device_by_index(i).ok()?;
            if d.name().map(|n| n == name).unwrap_or(false) {
                Some(d)
            } else {
                None
            }
        })
    };
    let device = match by_name {
        Some(d) => Ok(d),
        None => nvml.device_by_index(index).or_else(|_| nvml.device_by_index(0)),
    };

    let Ok(device) = device else {
        return NvSample::empty();
    };

    NvSample {
        gpu_util: device.utilization_rates().ok().map(|u| u.gpu as f32),
        temp_c: device.temperature(TemperatureSensor::Gpu).ok().map(|t| t as f32),
        power_w: device.power_usage().ok().map(|mw| mw as f32 / 1000.0),
        fan_pct: device.fan_speed(0).ok(),
        encoder_pct: device.encoder_utilization().ok().map(|u| u.utilization as f32),
        decoder_pct: device.decoder_utilization().ok().map(|u| u.utilization as f32),
        core_clock_mhz: device.clock_info(Clock::Graphics).ok(),
        mem_clock_mhz: device.clock_info(Clock::Memory).ok(),
    }
}

#[cfg(target_os = "windows")]
fn init_gpu_pdh() -> Option<GpuPdhState> {
    unsafe {
        let mut query: isize = 0;
        if PdhOpenQueryW(std::ptr::null(), 0, &mut query) != 0 {
            return None;
        }

        let util_path = wstr("\\GPU Engine(*engtype_3D)\\Utilization Percentage");
        let mut util_counter: isize = 0;
        if PdhAddEnglishCounterW(query, util_path.as_ptr(), 0, &mut util_counter) != 0 {
            PdhCloseQuery(query);
            return None;
        }

        let vram_path = wstr("\\GPU Adapter Memory(*)\\Dedicated Usage");
        let mut vram_counter: isize = 0;
        if PdhAddEnglishCounterW(query, vram_path.as_ptr(), 0, &mut vram_counter) != 0 {
            vram_counter = 0;
        }

        let shared_path = wstr("\\GPU Adapter Memory(*)\\Shared Usage");
        let mut shared_counter: isize = 0;
        if PdhAddEnglishCounterW(query, shared_path.as_ptr(), 0, &mut shared_counter) != 0 {
            shared_counter = 0;
        }

        PdhCollectQueryData(query);

        Some(GpuPdhState { query, util_counter, vram_counter, shared_counter })
    }
}

#[cfg(not(target_os = "windows"))]
fn init_gpu_pdh() -> Option<GpuPdhState> { None }

#[cfg(target_os = "windows")]
#[link(name = "kernel32")]
extern "system" {
    fn GlobalMemoryStatusEx(lpBuffer: *mut MemoryStatusEx) -> i32;
}

#[cfg(target_os = "windows")]
#[repr(C)]
struct MemoryStatusEx {
    dw_length:                 u32,
    dw_memory_load:            u32,
    ull_total_phys:            u64,
    ull_avail_phys:            u64,
    ull_total_page_file:       u64,
    ull_avail_page_file:       u64,
    ull_total_virtual:         u64,
    ull_avail_virtual:         u64,
    ull_avail_extended_virtual:u64,
}

#[cfg(target_os = "windows")]
fn system_ram_mb() -> u64 {
    unsafe {
        let mut m: MemoryStatusEx = std::mem::zeroed();
        m.dw_length = std::mem::size_of::<MemoryStatusEx>() as u32;
        if GlobalMemoryStatusEx(&mut m) != 0 {
            m.ull_total_phys / 1_048_576
        } else { 0 }
    }
}

#[cfg(not(target_os = "windows"))]
fn system_ram_mb() -> u64 { 0 }

#[cfg(target_os = "windows")]
unsafe fn wchar_ptr_to_string(ptr: *const u16) -> String {
    if ptr.is_null() { return String::new(); }
    let mut len = 0usize;
    while *ptr.add(len) != 0 { len += 1; }
    String::from_utf16_lossy(std::slice::from_raw_parts(ptr, len)).to_lowercase()
}

#[cfg(target_os = "windows")]
fn luid_prefix_from_instance(name: &str) -> &str {
    if let Some(pos) = name.find("_phys_") { &name[..pos] } else { name }
}

/// Extract the `luid_<hi>_<lo>` token from a GPU Engine counter instance name such as
/// `pid_<pid>_luid_<hi>_<lo>_phys_<n>_eng_<n>_engtype_3D`. Unlike the VRAM "GPU Adapter
/// Memory" instances (which already begin with `luid_`), engine instances are prefixed
/// with `pid_<pid>_`, so we locate the `luid_` token before truncating at `_phys_`.
#[cfg(target_os = "windows")]
fn luid_engine_prefix_from_instance(name: &str) -> &str {
    let from_luid = match name.find("luid_") {
        Some(pos) => &name[pos..],
        None => name,
    };
    luid_prefix_from_instance(from_luid)
}

#[cfg(target_os = "windows")]
unsafe fn pdh_sum_i64(counter: isize, luid_prefix: Option<&str>) -> u64 {
    if counter == 0 { return 0; }
    let mut buf_size: u32 = 0;
    let mut item_count: u32 = 0;
    PdhGetFormattedCounterArrayW(
        counter, PDH_FMT_LARGE,
        &mut buf_size, &mut item_count, std::ptr::null_mut(),
    );
    if item_count == 0 || buf_size == 0 { return 0; }
    let mut buf = vec![0u8; buf_size as usize];
    let status = PdhGetFormattedCounterArrayW(
        counter, PDH_FMT_LARGE,
        &mut buf_size, &mut item_count, buf.as_mut_ptr(),
    );
    if status != 0 { return 0; }
    let items = std::slice::from_raw_parts(
        buf.as_ptr() as *const PdhItemI64,
        item_count as usize,
    );
    let total: i64 = items.iter()
        .filter(|i| {
            if i._status != 0 || i.value < 0 { return false; }
            match luid_prefix {
                Some(prefix) => {
                    let inst = wchar_ptr_to_string(i._name);
                    luid_prefix_from_instance(&inst) == prefix
                }
                None => true,
            }
        })
        .map(|i| i.value)
        .sum();
    (total as u64) / 1_048_576
}

#[cfg(target_os = "windows")]
fn sample_gpu_pdh(pdh: &GpuPdhState, luid_prefix: Option<&str>) -> (f32, u64, u64) {
    unsafe {
        PdhCollectQueryData(pdh.query);

        let utilization = {
            let mut buf_size: u32 = 0;
            let mut item_count: u32 = 0;
            PdhGetFormattedCounterArrayW(
                pdh.util_counter, PDH_FMT_DOUBLE,
                &mut buf_size, &mut item_count, std::ptr::null_mut(),
            );
            if item_count > 0 && buf_size > 0 {
                let mut buf = vec![0u8; buf_size as usize];
                let status = PdhGetFormattedCounterArrayW(
                    pdh.util_counter, PDH_FMT_DOUBLE,
                    &mut buf_size, &mut item_count, buf.as_mut_ptr(),
                );
                if status == 0 {
                    let items = std::slice::from_raw_parts(
                        buf.as_ptr() as *const PdhItemF64,
                        item_count as usize,
                    );
                    let sum: f64 = items.iter()
                        .filter(|i| {
                            if i._status != 0 || !i.value.is_finite() || i.value < 0.0 { return false; }
                            match luid_prefix {
                                Some(prefix) => {
                                    // Engine instance names are
                                    // `pid_<pid>_luid_<hi>_<lo>_phys_<n>_eng_<n>_engtype_3D`,
                                    // so they never start with `luid_...`. Match the same way
                                    // the VRAM path does: isolate the `luid_..._phys_` token.
                                    let inst = wchar_ptr_to_string(i._name);
                                    luid_engine_prefix_from_instance(&inst) == prefix
                                }
                                None => true,
                            }
                        })
                        .map(|i| i.value)
                        .sum();
                    (sum as f32).min(100.0)
                } else { 0.0 }
            } else { 0.0 }
        };

        let vram_used_mb   = pdh_sum_i64(pdh.vram_counter,   luid_prefix);
        let shared_used_mb = pdh_sum_i64(pdh.shared_counter, luid_prefix);

        (utilization, vram_used_mb, shared_used_mb)
    }
}

#[cfg(not(target_os = "windows"))]
fn sample_gpu_pdh(_pdh: &GpuPdhState, _luid_prefix: Option<&str>) -> (f32, u64, u64) { (0.0, 0, 0) }

pub fn get_gpu_stats_inner(index: u32) -> GpuStats {
    let lock = gpu_state();
    let mut state = lock.lock().unwrap_or_else(|e| e.into_inner());

    state.per_index.entry(index).or_insert_with(|| {
        let (name, vram_bytes, luid_prefix) = read_gpu_registry(index);
        GpuPerIndex {
            name,
            vram_total:  vram_bytes / 1_048_576,
            luid_prefix,
            cached:      None,
            last_refresh: None,
        }
    });

    // 500 ms debounce per index
    {
        let gi = &state.per_index[&index];
        if let (Some(cached), Some(last)) = (&gi.cached, gi.last_refresh) {
            if last.elapsed().as_millis() < 500 {
                return cached.clone();
            }
        }
    }

    let luid_prefix = state.per_index[&index].luid_prefix.clone();
    let (pdh_utilization, vram_used_mb, shared_used_mb) = match &state.pdh {
        Some(pdh) => sample_gpu_pdh(pdh, luid_prefix.as_deref()),
        None      => (0.0, 0, 0),
    };

    let name       = state.per_index[&index].name.clone();
    let vram_total = state.per_index[&index].vram_total;
    // Windows defaults to ~50% of system RAM for the shared GPU memory budget.
    let shared_total_mb = system_ram_mb() / 2;

    let nv = state.nvml.as_ref()
        .map(|nvml| sample_nvml(nvml, &name, index))
        .unwrap_or_else(NvSample::empty);

    // NVIDIA: trust NVML's GPU-Util (matches nvidia-smi, immune to the borderless-
    // fullscreen present-mode quirk that makes the PDH 3D-engine counter read low while
    // a game holds foreground). Everything else falls back to the PDH 3D-engine sum.
    let utilization = choose_utilization(nv.gpu_util, pdh_utilization);

    let stats = GpuStats {
        name,
        utilization,
        vram_used_mb,
        vram_total_mb: vram_total,
        shared_used_mb,
        shared_total_mb,
        temp_c: nv.temp_c,
        power_w: nv.power_w,
        fan_pct: nv.fan_pct,
        encoder_pct: nv.encoder_pct,
        decoder_pct: nv.decoder_pct,
        core_clock_mhz: nv.core_clock_mhz,
        mem_clock_mhz: nv.mem_clock_mhz,
    };

    if let Some(gi) = state.per_index.get_mut(&index) {
        gi.cached       = Some(stats.clone());
        gi.last_refresh = Some(Instant::now());
    }
    stats
}

#[cfg(all(test, target_os = "windows"))]
mod tests {
    use super::{choose_utilization, luid_engine_prefix_from_instance, luid_prefix_from_instance};

    // Regression for themia-releases#32: a foreground borderless-fullscreen game makes the
    // PDH `engtype_3D` counter under-report (e.g. 4%) while the GPU is actually pegged. On
    // NVIDIA hardware NVML reports the true utilisation, and it must win over the stale PDH read.
    #[test]
    fn nvml_utilization_wins_over_pdh_when_present() {
        // NVML says 87% busy, PDH 3D-engine counter collapsed to 4% (the bug symptom).
        assert_eq!(choose_utilization(Some(87.0), 4.0), 87.0);
    }

    #[test]
    fn pdh_utilization_used_when_nvml_absent() {
        // Non-NVIDIA / NVML-missing: fall back to the PDH 3D-engine sum unchanged.
        assert_eq!(choose_utilization(None, 23.5), 23.5);
    }

    #[test]
    fn chosen_utilization_is_clamped_to_0_100() {
        assert_eq!(choose_utilization(Some(140.0), 0.0), 100.0);
        assert_eq!(choose_utilization(Some(-5.0), 0.0), 0.0);
        assert_eq!(choose_utilization(None, 250.0), 100.0);
    }

    // Regression for [HIGH] util-collapses-to-0: GPU Engine counter instance names are
    // `pid_<pid>_luid_<hi>_<lo>_phys_<n>_eng_<n>_engtype_3D`. The old filter used
    // `starts_with("luid_...")`, which is always false for these `pid_`-prefixed names,
    // so utilisation collapsed to 0 whenever a LUID prefix was present.
    #[test]
    fn engine_instance_matches_registry_luid_prefix() {
        let inst = "pid_1234_luid_0x00000000_0x0000abcd_phys_0_eng_0_engtype_3d";
        let prefix = "luid_0x00000000_0x0000abcd";
        assert_eq!(luid_engine_prefix_from_instance(inst), prefix);
        // The buggy predicate would have failed:
        assert!(!inst.starts_with(prefix));
    }

    #[test]
    fn engine_instance_with_different_luid_does_not_match() {
        let inst = "pid_1234_luid_0x00000000_0x0000ffff_phys_0_eng_0_engtype_3d";
        let prefix = "luid_0x00000000_0x0000abcd";
        assert_ne!(luid_engine_prefix_from_instance(inst), prefix);
    }

    #[test]
    fn engine_prefix_handles_missing_phys_and_luid_tokens() {
        // No `_phys_`: keep everything from `luid_` onward.
        assert_eq!(
            luid_engine_prefix_from_instance("pid_1_luid_0x0_0x1"),
            "luid_0x0_0x1"
        );
        // No `luid_` token at all: returned unchanged (matches nothing real).
        assert_eq!(luid_engine_prefix_from_instance("engtype_3d"), "engtype_3d");
    }

    // The VRAM "GPU Adapter Memory" instances already begin with `luid_`; the engine helper
    // must agree with the VRAM helper on those so both memory and util target one adapter.
    #[test]
    fn engine_helper_agrees_with_vram_helper_on_adapter_instances() {
        let vram_inst = "luid_0x00000000_0x0000abcd_phys_0";
        assert_eq!(
            luid_engine_prefix_from_instance(vram_inst),
            luid_prefix_from_instance(vram_inst)
        );
        assert_eq!(
            luid_engine_prefix_from_instance(vram_inst),
            "luid_0x00000000_0x0000abcd"
        );
    }
}
