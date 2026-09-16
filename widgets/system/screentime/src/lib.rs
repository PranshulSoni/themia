// Screen-time tracking backend (widgets/system/screentime/SPEC.md, [S-20]..[S-31]).
//
// The pure core (`Tracker` + `LocalStamp`) is OS-free and unit-tested; Win32
// glue (pid → exe resolution, idle query, local time) lives behind
// cfg(target_os = "windows") at the bottom. src-tauri feeds events in from the
// existing EVENT_SYSTEM_FOREGROUND hook and the display-watcher session
// messages; a sampler thread owned by this crate ticks accrual and flushes.

use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;
use std::path::{Path, PathBuf};
use std::sync::{Mutex, MutexGuard, OnceLock};

pub const SCHEMA_VERSION: u32 = 1;
/// [S-28] only the most recent N dates survive a save.
const RETENTION_DAYS: usize = 90;
/// [S-24] input idle beyond this pauses accrual.
const IDLE_THRESHOLD_SECS: u32 = 300;
/// [S-22] sampler cadence; open intervals are at most this long.
const TICK_SECS: u64 = 30;
/// [S-29] minimum spacing between dirty-flush writes.
const FLUSH_SECS: u64 = 60;
/// [S-32] longest anchor-to-now gap that still counts as usage. The sampler
/// ticks every TICK_SECS while the machine runs; anything longer means
/// suspend/hibernate, a forward clock jump, or a stalled process.
const MAX_CREDIT_SECS: u64 = 2 * TICK_SECS;
const SECS_PER_DAY: u32 = 86_400;

// ---------------------------------------------------------------------------
// Pure core
// ---------------------------------------------------------------------------

/// A point in local time: epoch seconds plus the local date and the seconds
/// since local midnight, so midnight splits ([S-25]) need no timezone math.
#[derive(Clone, Debug, PartialEq)]
pub struct LocalStamp {
    pub epoch: u64,
    pub date: String,
    pub secs_into_day: u32,
}

#[derive(Clone, Debug)]
struct OpenInterval {
    exe: String,
    anchor: LocalStamp,
}

#[derive(Debug, Default, PartialEq, Clone, Copy)]
enum Pause {
    #[default]
    None,
    Locked,
    Idle,
}

type DayMap = BTreeMap<String, BTreeMap<String, u64>>;

#[derive(Debug, Default)]
pub struct Tracker {
    days: DayMap,
    names: BTreeMap<String, String>,
    // [S-38] last-known full exe path per bucket key, so the frontend can
    // extract app icons ([S-37]) for historical days too. Additive to the v1
    // store (see UsageFile); never required for accrual correctness.
    paths: BTreeMap<String, String>,
    // [S-55] how many times the foreground app CHANGED, per date. Additive to
    // the v1 store like `paths`; a plain counter, with no timing detail.
    switches: BTreeMap<String, u64>,
    current: Option<OpenInterval>,
    paused: Pause,
    dirty: bool,
}

#[derive(Serialize, Clone, Debug)]
pub struct AppStat {
    pub exe: String,
    pub name: String,
    pub secs: u64,
    /// [S-38] full path to the app's exe, when known; the frontend feeds it to
    /// `get_file_icon` ([S-37]). `None` for buckets seen only in an old store
    /// written before path tracking, or when path resolution failed.
    pub path: Option<String>,
}

#[derive(Serialize, Clone, Debug)]
pub struct DayStats {
    pub date: String,
    pub total_secs: u64,
    pub apps: Vec<AppStat>,
    /// [S-55] foreground switches recorded for the date.
    pub switch_count: u64,
}

fn add_secs(days: &mut DayMap, date: &str, exe: &str, secs: u64) {
    if secs == 0 {
        return;
    }
    *days
        .entry(date.to_string())
        .or_default()
        .entry(exe.to_string())
        .or_insert(0) += secs;
}

/// Seconds of `anchor..now` that belong to `date`, splitting once at the
/// anchor's local midnight ([S-25]). Multi-day gaps collapse into `now.date`;
/// the sampler keeps gaps to one tick so that case stays theoretical.
fn split_secs(anchor: &LocalStamp, now: &LocalStamp, date: &str) -> u64 {
    if now.epoch <= anchor.epoch {
        return 0;
    }
    let total = now.epoch - anchor.epoch;
    if anchor.date == now.date {
        return if date == anchor.date { total } else { 0 };
    }
    let until_midnight = u64::from(SECS_PER_DAY - anchor.secs_into_day.min(SECS_PER_DAY)).min(total);
    if date == anchor.date {
        until_midnight
    } else if date == now.date {
        total - until_midnight
    } else {
        0
    }
}

impl Tracker {
    /// Accrue the open interval up to `upto` and move its anchor there.
    fn credit(&mut self, upto: &LocalStamp) {
        let Some(open) = self.current.clone() else {
            return;
        };
        // [S-32] a gap longer than two ticks is suspend/clock-jump time, not
        // usage: re-anchor without crediting (loses at most one real tick).
        if upto.epoch.saturating_sub(open.anchor.epoch) > MAX_CREDIT_SECS {
            if let Some(open) = &mut self.current {
                open.anchor = upto.clone();
            }
            return;
        }
        let first = split_secs(&open.anchor, upto, &open.anchor.date);
        add_secs(&mut self.days, &open.anchor.date, &open.exe, first);
        let mut credited = first;
        if upto.date != open.anchor.date {
            let rest = split_secs(&open.anchor, upto, &upto.date);
            add_secs(&mut self.days, &upto.date, &open.exe, rest);
            credited += rest;
        }
        if credited > 0 {
            self.dirty = true;
        }
        if let Some(open) = &mut self.current {
            open.anchor = upto.clone();
        }
    }

    /// [S-38] remember the full exe path for `exe`'s bucket so the frontend can
    /// extract its icon. Empty inputs are ignored; a changed path marks dirty so
    /// it survives to the next flush.
    pub fn note_exe_path(&mut self, exe: &str, path: &str) {
        if exe.is_empty() || path.is_empty() {
            return;
        }
        if self.paths.get(exe).map(String::as_str) != Some(path) {
            self.paths.insert(exe.to_string(), path.to_string());
            self.dirty = true;
        }
    }

    /// [S-20] foreground switched to `exe`. [S-21] remember its display name.
    /// While paused, the remembered app updates but nothing accrues ([S-23]).
    pub fn on_foreground(&mut self, stamp: LocalStamp, exe: String, name: Option<String>) {
        if let Some(n) = name {
            if !n.is_empty() && self.names.get(&exe) != Some(&n) {
                self.names.insert(exe.clone(), n);
                self.dirty = true;
            }
        }
        if self.paused == Pause::None {
            self.credit(&stamp);
            // [S-55] count a real CHANGE of app, and only while accruing: a
            // switch behind a lock or an idle screen isn't the user switching.
            // The first app of a session is a start, not a switch.
            if self.current.as_ref().is_some_and(|c| c.exe != exe) {
                *self.switches.entry(stamp.date.clone()).or_insert(0) += 1;
                self.dirty = true;
            }
        }
        self.current = Some(OpenInterval { exe, anchor: stamp });
    }

    /// [S-22] sampler tick: accrue, and manage the idle pause ([S-24]).
    pub fn on_tick(&mut self, stamp: LocalStamp, idle_secs: u32) {
        match self.paused {
            Pause::Locked => {} // only unlock resumes ([S-23])
            Pause::Idle => {
                if idle_secs < IDLE_THRESHOLD_SECS {
                    // User is back; the idle gap itself is never credited.
                    if let Some(open) = &mut self.current {
                        open.anchor = stamp;
                    }
                    self.paused = Pause::None;
                }
            }
            Pause::None => {
                self.credit(&stamp);
                if idle_secs >= IDLE_THRESHOLD_SECS {
                    self.paused = Pause::Idle;
                }
            }
        }
    }

    /// [S-33] Themia's own window is foreground (hook can't see own-process
    /// transitions): accrue up to now, then close the interval so the
    /// previously focused app stops accruing.
    pub fn on_own_foreground(&mut self, stamp: LocalStamp) {
        if self.paused == Pause::None {
            self.credit(&stamp);
        }
        self.current = None;
    }

    /// [S-23] session locked: accrue up to the lock, then stop.
    pub fn on_lock(&mut self, stamp: LocalStamp) {
        if self.paused == Pause::None {
            self.credit(&stamp);
        }
        self.paused = Pause::Locked;
    }

    /// [S-23] session unlocked: resume from now (idle re-pauses on next tick).
    pub fn on_unlock(&mut self, stamp: LocalStamp) {
        if self.paused == Pause::Locked {
            if let Some(open) = &mut self.current {
                open.anchor = stamp;
            }
            self.paused = Pause::None;
        }
    }

    /// [S-30] snapshot for one date, including the live open interval.
    pub fn day_stats(&self, date: &str, now: Option<&LocalStamp>) -> DayStats {
        let mut per: BTreeMap<String, u64> = self.days.get(date).cloned().unwrap_or_default();
        if self.paused == Pause::None {
            if let (Some(open), Some(now)) = (&self.current, now) {
                // [S-32] guard the live interval too: between wake and the
                // first post-wake tick the anchor can be hours old.
                if now.epoch.saturating_sub(open.anchor.epoch) <= MAX_CREDIT_SECS {
                    let live = split_secs(&open.anchor, now, date);
                    if live > 0 {
                        *per.entry(open.exe.clone()).or_insert(0) += live;
                    }
                }
            }
        }
        let total_secs: u64 = per.values().sum();
        let mut apps: Vec<AppStat> = per
            .into_iter()
            .map(|(exe, secs)| AppStat {
                name: self
                    .names
                    .get(&exe)
                    .cloned()
                    .unwrap_or_else(|| prettify_exe(&exe)),
                path: self.paths.get(&exe).cloned(), // [S-38]
                exe,
                secs,
            })
            .collect();
        apps.sort_by(|a, b| b.secs.cmp(&a.secs).then_with(|| a.exe.cmp(&b.exe)));
        DayStats {
            date: date.to_string(),
            total_secs,
            apps,
            switch_count: self.switches.get(date).copied().unwrap_or(0), // [S-55]
        }
    }

    /// [S-38] exe keys that appear anywhere in history but have no recorded
    /// path yet, so their icon can't be extracted. Deduped, for a one-time
    /// backfill from currently-running processes.
    fn stems_missing_paths(&self) -> Vec<String> {
        self.history_stems(|exe| !self.paths.contains_key(exe))
    }

    /// [S-21] exe keys in history with no friendly NAME yet (they display as the
    /// prettified stem). Re-resolvable from a running process during the backfill
    /// — e.g. "githubdesktop" → "GitHub Desktop".
    fn stems_missing_names(&self) -> Vec<String> {
        self.history_stems(|exe| !self.names.contains_key(exe))
    }

    /// Deduped exe keys across all days matching `pred`, in first-seen order.
    fn history_stems(&self, pred: impl Fn(&str) -> bool) -> Vec<String> {
        let mut out = Vec::new();
        let mut seen = std::collections::BTreeSet::new();
        for per in self.days.values() {
            for exe in per.keys() {
                if pred(exe) && seen.insert(exe.clone()) {
                    out.push(exe.clone());
                }
            }
        }
        out
    }

    /// [S-28] drop the oldest dates beyond retention. YYYY-MM-DD keys sort
    /// lexicographically = chronologically in the BTreeMap.
    fn prune(&mut self) {
        while self.days.len() > RETENTION_DAYS {
            let Some(oldest) = self.days.keys().next().cloned() else {
                break;
            };
            self.days.remove(&oldest);
        }
        // [S-55] switch counts follow the days they belong to, so retention
        // can't leave a counter behind for a date with no usage left.
        self.switches.retain(|date, _| self.days.contains_key(date));
    }
}

/// [S-21] a display name must be a single clean line. Truncate at the first
/// control unit and trim; empty → `None` (the bucket then falls back to
/// `prettify_exe` or re-resolves). Heals older stores that captured
/// version-info garbage before the `file_description` NUL-termination fix
/// (e.g. "Spotify\0\u{8}\u{16}…FileV" → "Spotify").
fn sanitize_name(s: &str) -> Option<String> {
    let end = s.find(|c: char| (c as u32) < 0x20).unwrap_or(s.len());
    let t = s[..end].trim();
    if t.is_empty() {
        None
    } else {
        Some(strip_entrypoint_suffix(t))
    }
}

/// [S-47] some apps ship an internal entry-point identifier as their
/// FileDescription ("WhatsApp.Root"). Drop a trailing `.<junk>` segment from
/// such an identifier-shaped name — no whitespace and a dotted tail — so it
/// reads as the app ("WhatsApp"). Only the known meaningless tails are dropped,
/// so real dotted names ("Node.js JavaScript Runtime", "Microsoft.Todos") are
/// untouched. Applied by `sanitize_name`, hence also on store LOAD, so a store
/// that already cached "WhatsApp.Root" heals itself.
fn strip_entrypoint_suffix(s: &str) -> String {
    const JUNK_TAILS: [&str; 6] = ["root", "app", "exe", "main", "entrypoint", "program"];
    if s.contains(char::is_whitespace) {
        return s.to_string();
    }
    let Some((head, tail)) = s.rsplit_once('.') else {
        return s.to_string();
    };
    if head.is_empty() || !JUNK_TAILS.contains(&tail.to_ascii_lowercase().as_str()) {
        return s.to_string();
    }
    head.to_string()
}

/// [S-47] a name that still looks like an identifier after
/// `strip_entrypoint_suffix` (dotted, no whitespace) is an internal name, not a
/// product name — the caller then prefers the exe's ProductName.
fn is_identifier_like(s: &str) -> bool {
    !s.contains(char::is_whitespace) && s.contains('.')
}

/// [S-21]/[S-47] the display name for an exe: its FileDescription, or its
/// ProductName when the description is an internal identifier. `None` when
/// neither is usable, and the bucket falls back to `prettify_exe`.
fn display_name_for(path: &str) -> Option<String> {
    let desc = version_string(path, "FileDescription").and_then(|s| sanitize_name(&s));
    match &desc {
        Some(d) if !is_identifier_like(d) => return desc,
        _ => {}
    }
    let product = version_string(path, "ProductName").and_then(|s| sanitize_name(&s));
    match product {
        Some(p) if !is_identifier_like(&p) => Some(p),
        _ => desc,
    }
}

/// [S-21] fallback display name: capitalized exe stem.
pub fn prettify_exe(exe: &str) -> String {
    let mut chars = exe.chars();
    match chars.next() {
        Some(c) => c.to_uppercase().collect::<String>() + chars.as_str(),
        None => String::from("Unknown"),
    }
}

// ---------------------------------------------------------------------------
// Persistence ([S-26]..[S-28])
// ---------------------------------------------------------------------------

#[derive(Serialize, Deserialize)]
struct UsageFile {
    schema_version: u32,
    days: DayMap,
    names: BTreeMap<String, String>,
    // [S-38] additive to schema_version 1: `default` lets a store written
    // before path tracking load with an empty map, and `skip_serializing_if`
    // keeps the on-disk bytes byte-identical to the old format when no paths
    // are known (so the golden format test [S-26] stays valid and an older
    // Themia downgrade still reads the file — serde ignores the field either
    // way). A NEW field never needs a schema_version bump; a REMOVED or
    // RENAMED one does.
    #[serde(default, skip_serializing_if = "BTreeMap::is_empty")]
    paths: BTreeMap<String, String>,
    // [S-55] additive on the same terms as `paths`: absent in an older store,
    // omitted from the bytes when empty, so no schema_version bump.
    #[serde(default, skip_serializing_if = "BTreeMap::is_empty")]
    switches: BTreeMap<String, u64>,
}

/// [S-27] a day key must be `YYYY-MM-DD`; anything else would sort outside
/// the chronological order that retention pruning relies on.
fn is_date_key(k: &str) -> bool {
    k.len() == 10
        && k.bytes()
            .enumerate()
            .all(|(i, b)| if i == 4 || i == 7 { b == b'-' } else { b.is_ascii_digit() })
}

/// What a store yields once parsed and sanitized: `(days, names, paths,
/// switches)` — the same four fields `Tracker` keeps, in that order.
type ParsedStore = (
    DayMap,
    BTreeMap<String, String>,
    BTreeMap<String, String>,
    BTreeMap<String, u64>,
);

/// [S-27] `None` = corrupt or version mismatch (caller logs loudly); the
/// distinction from ABSENT is made by the caller checking file existence.
/// Junk day keys and empty exe keys are dropped so they can never become
/// immortal and evict real days from retention.
fn parse_store(raw: &str) -> Option<ParsedStore> {
    let f: UsageFile = serde_json::from_str(raw).ok()?;
    if f.schema_version != SCHEMA_VERSION {
        return None;
    }
    let days: DayMap = f
        .days
        .into_iter()
        .filter(|(date, _)| is_date_key(date))
        .map(|(date, per)| (date, per.into_iter().filter(|(exe, _)| !exe.is_empty()).collect()))
        .collect();
    // [S-21] sanitize names on load so a store written before the
    // file_description fix heals itself (garbage-suffixed names are truncated;
    // empty ones drop and re-resolve). Empty exe keys are dropped too.
    let names: BTreeMap<String, String> = f
        .names
        .into_iter()
        .filter(|(exe, _)| !exe.is_empty())
        .filter_map(|(exe, name)| sanitize_name(&name).map(|n| (exe, n)))
        .collect();
    // [S-38] drop junk path entries (empty key or value) so they can't survive
    // reloads, mirroring the day/exe key hygiene above.
    let paths: BTreeMap<String, String> = f
        .paths
        .into_iter()
        .filter(|(exe, path)| !exe.is_empty() && !path.is_empty())
        .collect();
    // [S-55] switch counts are keyed by date, so the same key hygiene applies.
    let switches: BTreeMap<String, u64> = f
        .switches
        .into_iter()
        .filter(|(date, n)| is_date_key(date) && *n > 0)
        .collect();
    Some((days, names, paths, switches))
}

fn load_tracker_at(path: &Path) -> Tracker {
    let mut t = Tracker::default();
    let raw = match std::fs::read_to_string(path) {
        Ok(raw) => raw,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return t, // [S-27] absent = silent default
        Err(e) => {
            log::warn!(target: "screentime", "read {path:?} failed: {e}; starting empty");
            return t;
        }
    };
    match parse_store(&raw) {
        Some((days, names, paths, switches)) => {
            t.days = days;
            t.names = names;
            t.paths = paths;
            t.switches = switches;
        }
        None => {
            // [S-27] corrupt/mismatched: loud log, typed default. Move the
            // bad file aside (best effort) so the next flush can't silently
            // destroy recoverable history (e.g. after a version downgrade).
            log::warn!(target: "screentime", "corrupt or version-mismatched {path:?}; starting empty");
            let backup = path.with_extension("json.corrupt");
            if let Err(e) = std::fs::rename(path, &backup) {
                log::warn!(target: "screentime", "could not preserve corrupt store: {e}");
            }
        }
    }
    t
}

/// [S-26] atomic single-writer save: unique temp name (pid + seq), write,
/// rename. Rename is atomic on the same volume (Windows MoveFileEx replaces
/// existing). Callers hold the tracker lock, so writes serialize.
fn save_tracker_at(path: &Path, t: &mut Tracker) -> std::io::Result<()> {
    use std::sync::atomic::{AtomicU64, Ordering};
    static TMP_SEQ: AtomicU64 = AtomicU64::new(0);

    t.prune();
    let file = UsageFile {
        schema_version: SCHEMA_VERSION,
        days: t.days.clone(),
        names: t.names.clone(),
        paths: t.paths.clone(), // [S-38]
        switches: t.switches.clone(), // [S-55]
    };
    let bytes = serde_json::to_vec(&file)
        .map_err(|e| std::io::Error::new(std::io::ErrorKind::InvalidData, e))?;
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)?;
    }
    let seq = TMP_SEQ.fetch_add(1, Ordering::Relaxed);
    let tmp = path.with_extension(format!("tmp.{}.{}", std::process::id(), seq));
    std::fs::write(&tmp, &bytes)?;
    if let Err(e) = std::fs::rename(&tmp, path) {
        let _ = std::fs::remove_file(&tmp); // ignore: best-effort cleanup of our own temp
        return Err(e);
    }
    t.dirty = false;
    Ok(())
}

// ---------------------------------------------------------------------------
// Runtime state + entry points called from src-tauri
// ---------------------------------------------------------------------------

static TRACKER: OnceLock<Mutex<Tracker>> = OnceLock::new();
static STORE_PATH: OnceLock<PathBuf> = OnceLock::new();

fn tracker() -> &'static Mutex<Tracker> {
    TRACKER.get_or_init(|| Mutex::new(Tracker::default()))
}

fn lock_tracker() -> MutexGuard<'static, Tracker> {
    tracker().lock().unwrap_or_else(|e| e.into_inner())
}

fn save_now(t: &mut Tracker) {
    let Some(path) = STORE_PATH.get() else {
        return;
    };
    if let Err(e) = save_tracker_at(path, t) {
        log::warn!(target: "screentime", "save {path:?} failed: {e}");
    }
}

/// [S-29] lock transition: pause accrual and flush if dirty. Extracted from
/// `note_lock` so the flush-on-lock contract is unit-testable.
fn lock_and_flush_at(t: &mut Tracker, path: &Path, stamp: LocalStamp) {
    t.on_lock(stamp);
    if t.dirty {
        if let Err(e) = save_tracker_at(path, t) {
            log::warn!(target: "screentime", "flush on lock failed: {e}");
        }
    }
}

/// [S-20] the foreground hook can fire before `init` finishes loading the
/// store, so merge the loaded history INTO the live tracker instead of
/// clobbering it (live seconds add on top; live names win).
fn merge_loaded(live: &mut Tracker, loaded: Tracker) {
    for (date, per) in loaded.days {
        for (exe, secs) in per {
            add_secs(&mut live.days, &date, &exe, secs);
        }
    }
    for (exe, name) in loaded.names {
        live.names.entry(exe).or_insert(name);
    }
    // [S-38] live paths (recorded this session from a real resolve) win; the
    // loaded ones only fill buckets not yet seen since startup.
    for (exe, path) in loaded.paths {
        live.paths.entry(exe).or_insert(path);
    }
    // [S-55] switch counts ADD, like the seconds: both sides counted real
    // switches, so the day's total is their sum.
    for (date, n) in loaded.switches {
        *live.switches.entry(date).or_insert(0) += n;
    }
}

/// [S-38]/[S-21] one-time catch-up for history buckets missing a path (no icon)
/// or a name (prettified stem): resolve both from the matching process running
/// now. The tracker lock is released across the process scan AND the
/// version-info reads (both are IO), then re-taken only to write the maps.
fn backfill_running_paths() {
    let wanted: std::collections::HashSet<String> = {
        let t = lock_tracker();
        t.stems_missing_paths()
            .into_iter()
            .chain(t.stems_missing_names())
            .collect()
    };
    if wanted.is_empty() {
        return;
    }
    let found = running_exe_paths(&wanted);
    if found.is_empty() {
        return;
    }
    // Which of the found stems still lack a name? Snapshot under the lock, then
    // read FileDescription off-lock (file IO must not block accrual).
    let need_name: std::collections::HashSet<String> = {
        let t = lock_tracker();
        found
            .iter()
            .filter(|(stem, _)| !t.names.contains_key(stem))
            .map(|(stem, _)| stem.clone())
            .collect()
    };
    let resolved_names: Vec<(String, String)> = found
        .iter()
        .filter(|(stem, _)| need_name.contains(stem))
        .filter_map(|(stem, path)| display_name_for(path).map(|name| (stem.clone(), name)))
        .collect();

    let mut t = lock_tracker();
    for (stem, path) in found {
        t.note_exe_path(&stem, &path);
    }
    for (stem, name) in resolved_names {
        // Don't clobber a name that landed meanwhile (e.g. a foreground event).
        if !name.is_empty() && !t.names.contains_key(&stem) {
            t.names.insert(stem, name);
            t.dirty = true;
        }
    }
}

/// Load the store and start the sampler thread. Call once at startup.
pub fn init(app: &tauri::AppHandle) {
    use tauri::Manager;
    let path = match app.path().app_data_dir() {
        Ok(dir) => dir.join("usage_stats.json"),
        Err(e) => {
            // Degraded fallback: cwd-relative store. Loud, because history
            // will not follow the user profile.
            log::warn!(target: "screentime", "app_data_dir unavailable ({e}); storing usage_stats.json in cwd");
            PathBuf::from(".").join("usage_stats.json")
        }
    };
    let loaded = load_tracker_at(&path);
    if STORE_PATH.set(path).is_err() {
        return; // ignore: double init (dev reload); first sampler keeps running
    }
    merge_loaded(&mut lock_tracker(), loaded);

    // [S-20] seed the app that is already foreground so accrual starts now,
    // not at the first app switch.
    seed_initial_foreground();

    // [S-22]/[S-29] sampler: accrue each tick, flush when dirty and due.
    // Cadence-based sampling like game_mode's watcher, not a correctness
    // ordering hack; event hooks stay the source of app switches.
    std::thread::Builder::new()
        .name("screentime".into())
        .spawn(|| {
            // [S-38] one-time catch-up: fill exe paths for path-less history
            // buckets whose app is running now, so their icons ([S-37]) resolve
            // without waiting for each to be re-foregrounded. Off the main
            // thread; the tracker lock is never held across the process scan.
            backfill_running_paths();
            let mut last_flush = std::time::Instant::now();
            loop {
                std::thread::sleep(std::time::Duration::from_secs(TICK_SECS));
                let stamp = local_stamp_now();
                let idle = idle_secs();
                let own_foreground = foreground_is_self();
                let mut t = lock_tracker();
                t.on_tick(stamp.clone(), idle);
                if own_foreground {
                    // [S-33] the hook skips own-process transitions; close the
                    // interval so Themia time never accrues to the last app.
                    t.on_own_foreground(stamp);
                }
                if t.dirty && last_flush.elapsed().as_secs() >= FLUSH_SECS {
                    save_now(&mut t);
                    last_flush = std::time::Instant::now();
                }
            }
        })
        .map(|_| ())
        .unwrap_or_else(|e| log::warn!(target: "screentime", "sampler spawn failed: {e}"));
}

/// [S-20] called from the EVENT_SYSTEM_FOREGROUND hook with the new
/// foreground pid. Must never panic (hook thread); everything inside is
/// panic-free by construction. The stamp is taken immediately before the
/// tracker call so a slow name lookup can never rewind the anchor.
pub fn note_foreground(pid: u32) {
    if pid == 0 || pid == std::process::id() {
        return;
    }
    let Some((exe, path)) = resolve_exe(pid) else {
        return;
    };
    // [S-38] record the exe path every switch (even for a cached name): an app
    // update can move the exe to a new versioned folder, and the icon extract
    // needs the current path.
    if let Some(p) = &path {
        lock_tracker().note_exe_path(&exe, p);
    }
    let cached = lock_tracker().names.contains_key(&exe);
    if !cached {
        if let Some(path) = path {
            // [S-21] version-info read is file IO: run it off the hook
            // thread; the name lands in the store when it resolves.
            let exe_key = exe.clone();
            std::thread::Builder::new()
                .name("screentime-name".into())
                .spawn(move || {
                    if let Some(name) = display_name_for(&path) {
                        let mut t = lock_tracker();
                        if t.names.get(&exe_key) != Some(&name) {
                            t.names.insert(exe_key, name);
                            t.dirty = true;
                        }
                    }
                })
                .map(|_| ())
                .unwrap_or_else(|e| log::warn!(target: "screentime", "name thread spawn failed: {e}"));
        }
    }
    let stamp = local_stamp_now();
    lock_tracker().on_foreground(stamp, exe, None);
}

/// [S-23] session locked: accrue, pause, and flush ([S-29]).
pub fn note_lock() {
    let stamp = local_stamp_now();
    let mut t = lock_tracker();
    match STORE_PATH.get() {
        Some(path) => lock_and_flush_at(&mut t, path, stamp),
        None => t.on_lock(stamp), // pre-init lock: pause only, nothing to flush
    }
}

/// [S-23] session unlocked: resume accrual.
pub fn note_unlock() {
    let stamp = local_stamp_now();
    lock_tracker().on_unlock(stamp);
}

/// [S-29] final flush on app exit.
pub fn flush() {
    let stamp = local_stamp_now();
    let mut t = lock_tracker();
    if t.paused == Pause::None {
        t.credit(&stamp);
    }
    if t.dirty {
        save_now(&mut t);
    }
}

/// [S-30] command backend: stats for `date` (default today), live-inclusive.
pub fn get_day(date: Option<String>) -> DayStats {
    let now = local_stamp_now();
    let date = date.unwrap_or_else(|| now.date.clone());
    lock_tracker().day_stats(&date, Some(&now))
}

/// [S-41] command backend: one `DayStats` per local date of the rolling window
/// that ENDS TODAY (`days` entries, oldest first). The window is anchored on the
/// BACKEND clock, like `get_day`'s default ([S-12]), so a frontend/backend
/// midnight disagreement can never shift it. Live-inclusive: `split_secs` only
/// credits the open interval to its own date, so only today's entry can carry
/// it. `days` is clamped to 1..=retention — a caller can never ask for more
/// history than the store keeps.
pub fn get_range(days: u32) -> Vec<DayStats> {
    let now = local_stamp_now();
    let t = lock_tracker();
    range_stats(&t, &now, days)
}

/// [S-41] core of `get_range`, split out so tests can drive it with a
/// constructed tracker and stamp instead of the process-wide one.
fn range_stats(t: &Tracker, now: &LocalStamp, days: u32) -> Vec<DayStats> {
    let n = i64::from(days.clamp(1, RETENTION_DAYS as u32));
    (0..n)
        .rev() // oldest first
        .map(|back| t.day_stats(&shift_date(&now.date, -back), Some(now)))
        .collect()
}

/// [S-41] the `YYYY-MM-DD` key `delta` days from `date` (negative = back),
/// calendar-aware. A key that is not a well-formed date is returned unchanged
/// rather than becoming a half-built one.
fn shift_date(date: &str, delta: i64) -> String {
    if !is_date_key(date) {
        return date.to_string();
    }
    let (Ok(y), Ok(m), Ok(d)) = (
        date[0..4].parse::<i64>(),
        date[5..7].parse::<u32>(),
        date[8..10].parse::<u32>(),
    ) else {
        return date.to_string();
    };
    if !(1..=12).contains(&m) || !(1..=31).contains(&d) {
        return date.to_string();
    }
    let (y, m, d) = civil_from_days(days_from_civil(y, m, d) + delta);
    format!("{y:04}-{m:02}-{d:02}")
}

// ---------------------------------------------------------------------------
// OS glue
// ---------------------------------------------------------------------------

/// Civil (y, m, d) → days-since-epoch. Howard Hinnant's algorithm, the exact
/// inverse of `civil_from_days`; [S-41] steps a date key by whole days with it.
fn days_from_civil(y: i64, m: u32, d: u32) -> i64 {
    let y = if m <= 2 { y - 1 } else { y };
    let era = if y >= 0 { y } else { y - 399 } / 400;
    let yoe = (y - era * 400) as u64; // [0, 399]
    let mp = if m > 2 { m - 3 } else { m + 9 }; // March-based month
    let doy = (153 * u64::from(mp) + 2) / 5 + u64::from(d) - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    era * 146_097 + doe as i64 - 719_468
}

/// Days-since-epoch → civil (y, m, d). Howard Hinnant's algorithm; used by the
/// non-Windows fallback, by [S-41]'s date stepping, and pinned by tests.
fn civil_from_days(z: i64) -> (i64, u32, u32) {
    let z = z + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = (z - era * 146_097) as u64;
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe as i64 + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = (doy - (153 * mp + 2) / 5 + 1) as u32;
    let m = if mp < 10 { mp + 3 } else { mp - 9 } as u32;
    (if m <= 2 { y + 1 } else { y }, m, d)
}

#[cfg(target_os = "windows")]
fn local_stamp_now() -> LocalStamp {
    use winapi::um::sysinfoapi::GetLocalTime;
    let epoch = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    let mut st = unsafe { std::mem::zeroed::<winapi::um::minwinbase::SYSTEMTIME>() };
    unsafe { GetLocalTime(&mut st) };
    LocalStamp {
        epoch,
        date: format!("{:04}-{:02}-{:02}", st.wYear, st.wMonth, st.wDay),
        secs_into_day: u32::from(st.wHour) * 3600 + u32::from(st.wMinute) * 60 + u32::from(st.wSecond),
    }
}

#[cfg(not(target_os = "windows"))]
fn local_stamp_now() -> LocalStamp {
    // Fallback (tests / non-Windows builds): UTC calendar.
    let epoch = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    let (y, m, d) = civil_from_days((epoch / 86_400) as i64);
    LocalStamp {
        epoch,
        date: format!("{y:04}-{m:02}-{d:02}"),
        secs_into_day: (epoch % 86_400) as u32,
    }
}

/// [S-24] seconds since the last user input, from GetLastInputInfo.
/// GetTickCount wraps at ~49.7 days; wrapping_sub keeps the delta correct.
#[cfg(target_os = "windows")]
fn idle_secs() -> u32 {
    use winapi::um::sysinfoapi::GetTickCount;
    use winapi::um::winuser::{GetLastInputInfo, LASTINPUTINFO};
    let mut lii = LASTINPUTINFO {
        cbSize: std::mem::size_of::<LASTINPUTINFO>() as u32,
        dwTime: 0,
    };
    let ok = unsafe { GetLastInputInfo(&mut lii) };
    if ok == 0 {
        return 0; // treat query failure as "active": never pause on bad data
    }
    let now = unsafe { GetTickCount() };
    now.wrapping_sub(lii.dwTime) / 1000
}

#[cfg(not(target_os = "windows"))]
fn idle_secs() -> u32 {
    0
}

/// [S-33] is Themia's own window currently foreground? Sampled per tick.
#[cfg(target_os = "windows")]
fn foreground_is_self() -> bool {
    use winapi::um::winuser::{GetForegroundWindow, GetWindowThreadProcessId};
    unsafe {
        let hwnd = GetForegroundWindow();
        if hwnd.is_null() {
            return false;
        }
        let mut pid: u32 = 0;
        GetWindowThreadProcessId(hwnd, &mut pid);
        pid == std::process::id()
    }
}

#[cfg(not(target_os = "windows"))]
fn foreground_is_self() -> bool {
    false
}

/// [S-20] record the app that is foreground right now (startup seed).
#[cfg(target_os = "windows")]
fn seed_initial_foreground() {
    use winapi::um::winuser::{GetForegroundWindow, GetWindowThreadProcessId};
    let pid = unsafe {
        let hwnd = GetForegroundWindow();
        if hwnd.is_null() {
            return;
        }
        let mut pid: u32 = 0;
        GetWindowThreadProcessId(hwnd, &mut pid);
        pid
    };
    note_foreground(pid);
}

#[cfg(not(target_os = "windows"))]
fn seed_initial_foreground() {}

/// [S-20] pid → (lowercase exe stem, full path). RAII handle guard frees the
/// process handle on every path.
#[cfg(target_os = "windows")]
fn resolve_exe(pid: u32) -> Option<(String, Option<String>)> {
    use winapi::um::handleapi::CloseHandle;
    use winapi::um::processthreadsapi::OpenProcess;
    use winapi::um::winbase::QueryFullProcessImageNameW;
    use winapi::um::winnt::PROCESS_QUERY_LIMITED_INFORMATION;

    struct Handle(winapi::shared::ntdef::HANDLE);
    impl Drop for Handle {
        fn drop(&mut self) {
            if !self.0.is_null() {
                unsafe { CloseHandle(self.0) };
            }
        }
    }

    let handle = Handle(unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid) });
    if handle.0.is_null() {
        return None; // protected/exited process: skip, previous bucket keeps accruing
    }
    let mut buf = [0u16; 1024];
    let mut len = buf.len() as u32;
    let ok = unsafe { QueryFullProcessImageNameW(handle.0, 0, buf.as_mut_ptr(), &mut len) };
    if ok == 0 || len == 0 {
        return None;
    }
    let path = String::from_utf16_lossy(&buf[..len as usize]);
    let stem = Path::new(&path)
        .file_stem()
        .map(|s| s.to_string_lossy().to_lowercase())?;
    if stem.is_empty() {
        return None;
    }
    Some((stem, Some(path)))
}

#[cfg(not(target_os = "windows"))]
fn resolve_exe(_pid: u32) -> Option<(String, Option<String>)> {
    None
}

/// [S-38] resolve full exe paths for the wanted stems from processes running
/// right now (one Toolhelp snapshot). Best effort: inaccessible/protected
/// processes and stems not currently running are simply skipped. Panic-free
/// (called from the sampler thread); the snapshot handle is freed on every path.
#[cfg(target_os = "windows")]
fn running_exe_paths(wanted: &std::collections::HashSet<String>) -> Vec<(String, String)> {
    use winapi::um::handleapi::{CloseHandle, INVALID_HANDLE_VALUE};
    use winapi::um::tlhelp32::{
        CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W,
        TH32CS_SNAPPROCESS,
    };

    let mut out = Vec::new();
    if wanted.is_empty() {
        return out;
    }

    struct Snap(winapi::shared::ntdef::HANDLE);
    impl Drop for Snap {
        fn drop(&mut self) {
            unsafe { CloseHandle(self.0) };
        }
    }

    unsafe {
        let snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if snap == INVALID_HANDLE_VALUE {
            return out;
        }
        let _guard = Snap(snap);
        let mut entry: PROCESSENTRY32W = std::mem::zeroed();
        entry.dwSize = std::mem::size_of::<PROCESSENTRY32W>() as u32;
        if Process32FirstW(snap, &mut entry) == 0 {
            return out;
        }
        let mut done: std::collections::HashSet<String> = std::collections::HashSet::new();
        loop {
            // szExeFile is the bare file name (e.g. "spotify.exe"); take its
            // lowercase stem to match the bucket key before an OpenProcess.
            let n = entry
                .szExeFile
                .iter()
                .position(|&c| c == 0)
                .unwrap_or(entry.szExeFile.len());
            let name = String::from_utf16_lossy(&entry.szExeFile[..n]);
            if let Some(stem) = Path::new(&name)
                .file_stem()
                .map(|s| s.to_string_lossy().to_lowercase())
            {
                if wanted.contains(&stem) && !done.contains(&stem) {
                    if let Some((resolved, Some(path))) = resolve_exe(entry.th32ProcessID) {
                        done.insert(resolved.clone());
                        out.push((resolved, path));
                    }
                }
            }
            if Process32NextW(snap, &mut entry) == 0 {
                break;
            }
        }
    }
    out
}

#[cfg(not(target_os = "windows"))]
fn running_exe_paths(_wanted: &std::collections::HashSet<String>) -> Vec<(String, String)> {
    Vec::new()
}

/// [S-21] a version-info StringFileInfo field for an exe (e.g.
/// "FileDescription", "ProductName"), from the first translation entry.
#[cfg(target_os = "windows")]
fn version_string(path: &str, field: &str) -> Option<String> {
    use winapi::um::winver::{GetFileVersionInfoSizeW, GetFileVersionInfoW, VerQueryValueW};

    let wide: Vec<u16> = path.encode_utf16().chain(std::iter::once(0)).collect();
    let mut dummy = 0u32;
    let size = unsafe { GetFileVersionInfoSizeW(wide.as_ptr(), &mut dummy) };
    if size == 0 {
        return None;
    }
    let mut data = vec![0u8; size as usize];
    let ok = unsafe {
        GetFileVersionInfoW(wide.as_ptr(), 0, size, data.as_mut_ptr() as *mut _)
    };
    if ok == 0 {
        return None;
    }

    // First language+codepage pair from \VarFileInfo\Translation.
    let mut val: *mut winapi::ctypes::c_void = std::ptr::null_mut();
    let mut val_len = 0u32;
    let trans_key: Vec<u16> = "\\VarFileInfo\\Translation\0".encode_utf16().collect();
    let ok = unsafe {
        VerQueryValueW(
            data.as_ptr() as *const _,
            trans_key.as_ptr(),
            &mut val,
            &mut val_len,
        )
    };
    if ok == 0 || val.is_null() || (val_len as usize) < 4 {
        return None;
    }
    let (lang, cp) = unsafe {
        let p = val as *const u16;
        (*p, *p.add(1))
    };

    let desc_key: Vec<u16> = format!("\\StringFileInfo\\{lang:04x}{cp:04x}\\{field}\0")
        .encode_utf16()
        .collect();
    let mut desc: *mut winapi::ctypes::c_void = std::ptr::null_mut();
    let mut desc_len = 0u32;
    let ok = unsafe {
        VerQueryValueW(
            data.as_ptr() as *const _,
            desc_key.as_ptr(),
            &mut desc,
            &mut desc_len,
        )
    };
    if ok == 0 || desc.is_null() || desc_len == 0 {
        return None;
    }
    let slice = unsafe { std::slice::from_raw_parts(desc as *const u16, desc_len as usize) };
    // [S-21] VerQueryValueW's returned length can OVERSHOOT the actual string
    // (padding, or a resource that miscounts), so stop at the first NUL/control
    // unit instead of trusting `desc_len`. Trusting it leaked adjacent
    // version-info bytes into the name (e.g. "Spotify\0\u{8}\u{16}…FileV").
    let end = slice.iter().position(|&c| c < 0x20).unwrap_or(slice.len());
    let s = String::from_utf16_lossy(&slice[..end]).trim().to_string();
    if s.is_empty() {
        None
    } else {
        Some(s)
    }
}

#[cfg(not(target_os = "windows"))]
fn version_string(_path: &str, _field: &str) -> Option<String> {
    None
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

#[cfg(test)]
mod tests {
    use super::*;

    fn stamp(date: &str, secs_into_day: u32, epoch: u64) -> LocalStamp {
        LocalStamp {
            epoch,
            date: date.to_string(),
            secs_into_day,
        }
    }

    // Convenience: stamps within one day where epoch and secs_into_day move
    // together (midnight of "day one" = epoch 1_000_000).
    const DAY1_MIDNIGHT: u64 = 1_000_000;
    fn d1(secs: u32) -> LocalStamp {
        stamp("2026-07-01", secs, DAY1_MIDNIGHT + u64::from(secs))
    }
    fn d2(secs: u32) -> LocalStamp {
        stamp("2026-07-02", secs, DAY1_MIDNIGHT + 86_400 + u64::from(secs))
    }

    fn secs_of(t: &Tracker, date: &str, exe: &str) -> u64 {
        t.days
            .get(date)
            .and_then(|d| d.get(exe))
            .copied()
            .unwrap_or(0)
    }

    #[test]
    fn s20_foreground_change_switches_bucket() {
        let mut t = Tracker::default();
        t.on_foreground(d1(100), "chrome".into(), None);
        t.on_foreground(d1(160), "code".into(), None);
        t.on_tick(d1(190), 0);
        assert_eq!(secs_of(&t, "2026-07-01", "chrome"), 60);
        assert_eq!(secs_of(&t, "2026-07-01", "code"), 30);
    }

    #[test]
    fn s21_display_name_cached_and_prettify_fallback() {
        let mut t = Tracker::default();
        t.on_foreground(d1(0), "chrome".into(), Some("Google Chrome".into()));
        t.on_foreground(d1(10), "code".into(), None);
        t.on_tick(d1(20), 0);
        let stats = t.day_stats("2026-07-01", None);
        let by_exe = |e: &str| stats.apps.iter().find(|a| a.exe == e).unwrap().name.clone();
        assert_eq!(by_exe("chrome"), "Google Chrome");
        assert_eq!(by_exe("code"), "Code"); // fallback [S-21]
        assert_eq!(prettify_exe(""), "Unknown");
    }

    #[test]
    fn s22_ticks_accumulate_elapsed_time() {
        let mut t = Tracker::default();
        t.on_foreground(d1(0), "chrome".into(), None);
        t.on_tick(d1(30), 0);
        t.on_tick(d1(60), 0);
        assert_eq!(secs_of(&t, "2026-07-01", "chrome"), 60);
        assert!(t.dirty);
    }

    #[test]
    fn s23_lock_pauses_and_unlock_resumes() {
        let mut t = Tracker::default();
        t.on_foreground(d1(0), "chrome".into(), None);
        t.on_lock(d1(50));
        t.on_tick(d1(80), 0); // locked: nothing accrues
        t.on_unlock(d1(100));
        t.on_tick(d1(130), 0);
        assert_eq!(secs_of(&t, "2026-07-01", "chrome"), 50 + 30);
    }

    #[test]
    fn s23_foreground_while_locked_updates_app_but_accrues_nothing() {
        let mut t = Tracker::default();
        t.on_foreground(d1(0), "chrome".into(), None);
        t.on_lock(d1(50));
        t.on_foreground(d1(60), "code".into(), None);
        t.on_unlock(d1(100));
        t.on_tick(d1(130), 0);
        assert_eq!(secs_of(&t, "2026-07-01", "chrome"), 50);
        assert_eq!(secs_of(&t, "2026-07-01", "code"), 30); // from unlock, not lock-time switch
    }

    #[test]
    fn s24_idle_pauses_and_input_resumes() {
        let mut t = Tracker::default();
        t.on_foreground(d1(0), "chrome".into(), None);
        // Realistic 30s cadence; last input at t=30, idle grows each tick.
        t.on_tick(d1(30), 0);
        for i in 1..=10u32 {
            t.on_tick(d1(30 + i * 30), i * 30); // t=330 reaches idle 300 → pause
        }
        assert_eq!(secs_of(&t, "2026-07-01", "chrome"), 330);
        t.on_tick(d1(360), 330); // still idle: nothing
        t.on_tick(d1(400), 5); // user back: resume from 400, gap not credited
        t.on_tick(d1(430), 10);
        assert_eq!(secs_of(&t, "2026-07-01", "chrome"), 330 + 30);
    }

    #[test]
    fn s25_midnight_split_credits_both_days() {
        let mut t = Tracker::default();
        t.on_foreground(d1(86_360), "chrome".into(), None); // 40s before midnight
        t.on_tick(d2(20), 0); // 20s after midnight
        assert_eq!(secs_of(&t, "2026-07-01", "chrome"), 40);
        assert_eq!(secs_of(&t, "2026-07-02", "chrome"), 20);
    }

    #[test]
    fn s25_split_secs_cases() {
        let a = d1(86_380);
        let b = d2(30);
        assert_eq!(split_secs(&a, &b, "2026-07-01"), 20);
        assert_eq!(split_secs(&a, &b, "2026-07-02"), 30);
        assert_eq!(split_secs(&a, &b, "2026-06-30"), 0);
        // Same-day, and the degenerate now <= anchor case.
        assert_eq!(split_secs(&d1(10), &d1(25), "2026-07-01"), 15);
        assert_eq!(split_secs(&d1(25), &d1(10), "2026-07-01"), 0);
    }

    #[test]
    fn s26_save_is_atomic_and_leaves_no_temp() {
        let dir = std::env::temp_dir().join(format!("sct-test-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        let path = dir.join("usage_stats.json");
        let mut t = Tracker::default();
        t.on_foreground(d1(0), "chrome".into(), Some("Google Chrome".into()));
        t.on_tick(d1(30), 0);
        save_tracker_at(&path, &mut t).expect("save failed");
        assert!(!t.dirty);
        let entries: Vec<_> = std::fs::read_dir(&dir)
            .expect("dir missing")
            .map(|e| e.expect("dirent").file_name().to_string_lossy().to_string())
            .collect();
        assert_eq!(entries, vec!["usage_stats.json"], "no temp files left");
        let loaded = load_tracker_at(&path);
        assert_eq!(secs_of(&loaded, "2026-07-01", "chrome"), 30);
        assert_eq!(loaded.names.get("chrome").map(String::as_str), Some("Google Chrome"));
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn s27_corrupt_or_mismatched_version_returns_default() {
        assert!(parse_store("{not json").is_none());
        assert!(parse_store("{\"schema_version\":999,\"days\":{},\"names\":{}}").is_none());
        assert!(parse_store("{\"days\":{}}").is_none()); // missing fields = corrupt
        let ok = parse_store(
            "{\"schema_version\":1,\"days\":{\"2026-07-01\":{\"chrome\":12}},\"names\":{}}",
        );
        assert_eq!(
            ok.expect("valid store").0["2026-07-01"]["chrome"],
            12
        );
    }

    #[test]
    fn s27_load_absent_is_silent_default() {
        let t = load_tracker_at(Path::new("Z:/definitely/not/here/usage_stats.json"));
        assert!(t.days.is_empty());
        assert!(!t.dirty);
    }

    #[test]
    fn s28_retention_prunes_oldest_dates() {
        let mut t = Tracker::default();
        for i in 0..(RETENTION_DAYS + 5) {
            add_secs(&mut t.days, &format!("2026-01-{:03}", i), "chrome", 1);
        }
        t.prune();
        assert_eq!(t.days.len(), RETENTION_DAYS);
        assert!(!t.days.contains_key("2026-01-000")); // oldest gone
        assert!(t.days.contains_key(&format!("2026-01-{:03}", RETENTION_DAYS + 4)));
    }

    #[test]
    fn s30_day_stats_sorted_desc_and_includes_live_interval() {
        let mut t = Tracker::default();
        t.on_foreground(d1(0), "code".into(), None);
        t.on_tick(d1(30), 0);
        t.on_tick(d1(60), 0);
        t.on_tick(d1(100), 0);
        t.on_foreground(d1(100), "chrome".into(), None);
        // Open interval: chrome since 100, now = 160 → 60 live secs.
        let stats = t.day_stats("2026-07-01", Some(&d1(160)));
        assert_eq!(stats.total_secs, 160);
        assert_eq!(stats.apps[0].exe, "code");
        assert_eq!(stats.apps[0].secs, 100);
        assert_eq!(stats.apps[1].exe, "chrome");
        assert_eq!(stats.apps[1].secs, 60);
        // Paused (idle/locked) → live interval NOT counted.
        t.on_lock(d1(160));
        let stats = t.day_stats("2026-07-01", Some(&d1(300)));
        assert_eq!(stats.total_secs, 160);
        // day_stats is read-only: state unchanged by snapshotting.
        assert_eq!(secs_of(&t, "2026-07-01", "chrome"), 60);
    }

    #[test]
    fn s32_suspend_gap_is_never_credited() {
        let mut t = Tracker::default();
        t.on_foreground(d1(0), "chrome".into(), None);
        t.on_tick(d1(30), 0);
        // Machine suspended: next tick arrives 4 hours later. The gap must
        // not be credited (would otherwise be 4h of phantom chrome usage).
        t.on_tick(d1(30 + 4 * 3600), 5);
        assert_eq!(secs_of(&t, "2026-07-01", "chrome"), 30);
        // Accrual resumes normally from the re-anchored stamp.
        t.on_tick(d1(30 + 4 * 3600 + 30), 10);
        assert_eq!(secs_of(&t, "2026-07-01", "chrome"), 60);
        // A multi-day gap (Friday → Monday) credits nothing to either day.
        t.on_foreground(d1(86_000), "code".into(), None);
        t.on_tick(d2(80_000), 5);
        assert_eq!(secs_of(&t, "2026-07-01", "code"), 0);
        assert_eq!(secs_of(&t, "2026-07-02", "code"), 0);
    }

    #[test]
    fn s33_own_foreground_closes_interval_without_credit() {
        let mut t = Tracker::default();
        t.on_foreground(d1(0), "chrome".into(), None);
        t.on_tick(d1(30), 0);
        // Tick sees Themia's own window foreground: close the interval.
        t.on_own_foreground(d1(30));
        t.on_tick(d1(60), 0); // no open interval → nothing accrues
        t.on_tick(d1(90), 0);
        assert_eq!(secs_of(&t, "2026-07-01", "chrome"), 30);
        // Switching back to a real app resumes accrual from that moment.
        t.on_foreground(d1(100), "chrome".into(), None);
        t.on_tick(d1(130), 0);
        assert_eq!(secs_of(&t, "2026-07-01", "chrome"), 60);
    }

    #[test]
    fn s27_corrupt_file_is_preserved_not_overwritten() {
        let dir = std::env::temp_dir().join(format!("sct-corrupt-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("mkdir");
        let path = dir.join("usage_stats.json");
        std::fs::write(&path, "{not json").expect("write corrupt");
        let t = load_tracker_at(&path);
        assert!(t.days.is_empty());
        // [S-27] the unreadable file must survive as a .corrupt sibling so a
        // later flush can't destroy recoverable history.
        assert!(!path.exists(), "corrupt file should be moved aside");
        assert!(dir.join("usage_stats.json.corrupt").exists());
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn s27_junk_day_and_exe_keys_dropped_on_load() {
        let raw = "{\"schema_version\":1,\"days\":{\"2026-07-01\":{\"chrome\":10,\"\":5},\"zzz-junk\":{\"chrome\":7},\"2026-7-1\":{\"x\":1}},\"names\":{}}";
        let (days, _, _, _) = parse_store(raw).expect("valid envelope");
        assert!(days.contains_key("2026-07-01"));
        assert!(!days.contains_key("zzz-junk"));
        assert!(!days.contains_key("2026-7-1"));
        assert!(!days["2026-07-01"].contains_key(""));
        assert_eq!(days["2026-07-01"]["chrome"], 10);
    }

    /// Characterization: pins the exact on-disk bytes, not just the round-trip.
    /// Every other persistence test reads back through the same writer, so a
    /// change to the serialized shape (field order, envelope, key naming) would
    /// pass them all while silently orphaning existing user stores. This test is
    /// the one that fails. Update the literal ONLY together with a schema_version
    /// bump + a migration.
    #[test]
    fn s26_on_disk_format_is_stable() {
        let dir = std::env::temp_dir().join(format!("sct-golden-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        let path = dir.join("usage_stats.json");
        let mut t = Tracker::default();
        t.on_foreground(d1(0), "chrome".into(), Some("Google Chrome".into()));
        t.on_tick(d1(30), 0);
        save_tracker_at(&path, &mut t).expect("save failed");
        let raw = std::fs::read_to_string(&path).expect("read back");
        assert_eq!(
            raw,
            "{\"schema_version\":1,\"days\":{\"2026-07-01\":{\"chrome\":30}},\"names\":{\"chrome\":\"Google Chrome\"}}"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn s29_lock_flushes_dirty_state_to_disk() {
        let dir = std::env::temp_dir().join(format!("sct-flush-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        let path = dir.join("usage_stats.json");
        let mut t = Tracker::default();
        t.on_foreground(d1(0), "chrome".into(), None);
        lock_and_flush_at(&mut t, &path, d1(45));
        assert_eq!(t.paused, Pause::Locked);
        assert!(!t.dirty, "lock must flush the dirty store [S-29]");
        let loaded = load_tracker_at(&path);
        assert_eq!(secs_of(&loaded, "2026-07-01", "chrome"), 45);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn s20_init_merge_keeps_pre_init_accrual() {
        // Foreground events can arrive before init() finishes loading the
        // store; merging (not clobbering) keeps both sides.
        let mut live = Tracker::default();
        live.on_foreground(d1(0), "chrome".into(), None);
        live.on_tick(d1(30), 0);
        let mut loaded = Tracker::default();
        add_secs(&mut loaded.days, "2026-06-30", "code", 500);
        loaded.names.insert("code".into(), "Visual Studio Code".into());
        merge_loaded(&mut live, loaded);
        assert_eq!(secs_of(&live, "2026-07-01", "chrome"), 30);
        assert_eq!(secs_of(&live, "2026-06-30", "code"), 500);
        assert_eq!(live.names.get("code").map(String::as_str), Some("Visual Studio Code"));
        assert!(live.current.is_some(), "open interval must survive the merge");
    }

    #[test]
    fn s38_exe_path_recorded_and_returned_in_day_stats() {
        let mut t = Tracker::default();
        t.on_foreground(d1(0), "chrome".into(), Some("Google Chrome".into()));
        t.note_exe_path("chrome", "C:/apps/chrome.exe");
        t.on_tick(d1(30), 0);
        let stats = t.day_stats("2026-07-01", None);
        let chrome = stats.apps.iter().find(|a| a.exe == "chrome").expect("chrome");
        assert_eq!(chrome.path.as_deref(), Some("C:/apps/chrome.exe"));
        // A bucket with no recorded path returns None, not a fabricated value.
        t.on_foreground(d1(40), "code".into(), None);
        t.on_tick(d1(70), 0);
        let stats = t.day_stats("2026-07-01", None);
        let code = stats.apps.iter().find(|a| a.exe == "code").expect("code");
        assert_eq!(code.path, None);
    }

    #[test]
    fn s38_note_exe_path_ignores_empty_and_dedups() {
        let mut t = Tracker::default();
        t.note_exe_path("", "C:/x.exe"); // empty key ignored
        t.note_exe_path("chrome", ""); // empty value ignored
        assert!(t.paths.is_empty());
        assert!(!t.dirty);
        t.note_exe_path("chrome", "C:/apps/chrome.exe");
        assert!(t.dirty);
        t.dirty = false;
        t.note_exe_path("chrome", "C:/apps/chrome.exe"); // unchanged: no dirty
        assert!(!t.dirty);
        t.note_exe_path("chrome", "C:/apps2/chrome.exe"); // moved: updates + dirty
        assert!(t.dirty);
        assert_eq!(t.paths["chrome"], "C:/apps2/chrome.exe");
    }

    #[test]
    fn s38_paths_persist_round_trip() {
        let dir = std::env::temp_dir().join(format!("sct-paths-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        let path = dir.join("usage_stats.json");
        let mut t = Tracker::default();
        t.on_foreground(d1(0), "chrome".into(), Some("Google Chrome".into()));
        t.note_exe_path("chrome", "C:/apps/chrome.exe");
        t.on_tick(d1(30), 0);
        save_tracker_at(&path, &mut t).expect("save failed");
        let loaded = load_tracker_at(&path);
        assert_eq!(loaded.paths.get("chrome").map(String::as_str), Some("C:/apps/chrome.exe"));
        // And it surfaces through the read model the widget consumes.
        let stats = loaded.day_stats("2026-07-01", None);
        assert_eq!(stats.apps[0].path.as_deref(), Some("C:/apps/chrome.exe"));
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn s38_v1_store_without_paths_field_still_loads() {
        // Forward compatibility: a store written before path tracking has no
        // `paths` field. It must load as a valid v1 store (empty paths), NOT be
        // treated as corrupt — that would move real history aside ([S-27]).
        let raw = "{\"schema_version\":1,\"days\":{\"2026-07-01\":{\"chrome\":30}},\"names\":{\"chrome\":\"Google Chrome\"}}";
        let (days, names, paths, _) = parse_store(raw).expect("v1 store without paths must parse");
        assert_eq!(days["2026-07-01"]["chrome"], 30);
        assert_eq!(names.get("chrome").map(String::as_str), Some("Google Chrome"));
        assert!(paths.is_empty());
    }

    #[test]
    fn s38_junk_path_entries_dropped_on_load() {
        let raw = "{\"schema_version\":1,\"days\":{},\"names\":{},\"paths\":{\"chrome\":\"C:/c.exe\",\"\":\"C:/x.exe\",\"code\":\"\"}}";
        let (_, _, paths, _) = parse_store(raw).expect("valid envelope");
        assert_eq!(paths.get("chrome").map(String::as_str), Some("C:/c.exe"));
        assert!(!paths.contains_key("")); // empty key dropped
        assert!(!paths.contains_key("code")); // empty value dropped
    }

    /// Characterization for the additive `paths` field. When at least one path
    /// is known the serialized envelope gains a trailing `"paths"` object; when
    /// none are (see [S-26] `s26_on_disk_format_is_stable`) the field is omitted
    /// and the bytes are identical to the pre-[S-38] format. Update this literal
    /// ONLY together with a schema_version bump + migration.
    #[test]
    fn s38_on_disk_format_with_paths_is_stable() {
        let dir = std::env::temp_dir().join(format!("sct-golden-paths-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        let path = dir.join("usage_stats.json");
        let mut t = Tracker::default();
        t.on_foreground(d1(0), "chrome".into(), Some("Google Chrome".into()));
        t.note_exe_path("chrome", "C:/apps/chrome.exe");
        t.on_tick(d1(30), 0);
        save_tracker_at(&path, &mut t).expect("save failed");
        let raw = std::fs::read_to_string(&path).expect("read back");
        assert_eq!(
            raw,
            "{\"schema_version\":1,\"days\":{\"2026-07-01\":{\"chrome\":30}},\"names\":{\"chrome\":\"Google Chrome\"},\"paths\":{\"chrome\":\"C:/apps/chrome.exe\"}}"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn s21_sanitize_name_truncates_control_garbage() {
        // The real-world Spotify corruption: version-info bytes leaked past the
        // string's NUL.
        assert_eq!(
            sanitize_name("Spotify\u{0}8\u{16}\u{1}FileV").as_deref(),
            Some("Spotify")
        );
        assert_eq!(sanitize_name("Google Chrome").as_deref(), Some("Google Chrome"));
        assert_eq!(sanitize_name("  Padded  ").as_deref(), Some("Padded"));
        assert_eq!(sanitize_name("\u{0}garbage"), None); // leading control → empty
        assert_eq!(sanitize_name(""), None);
    }

    #[test]
    fn s55_counts_real_foreground_changes_only() {
        let mut t = Tracker::default();
        // The first app of a session is a start, not a switch.
        t.on_foreground(d1(0), "chrome".into(), None);
        assert_eq!(t.day_stats("2026-07-01", None).switch_count, 0);

        t.on_foreground(d1(10), "code".into(), None);
        t.on_foreground(d1(20), "chrome".into(), None);
        assert_eq!(t.day_stats("2026-07-01", None).switch_count, 2);

        // Re-reporting the SAME app is not a switch (the hook can repeat).
        t.on_foreground(d1(30), "chrome".into(), None);
        assert_eq!(t.day_stats("2026-07-01", None).switch_count, 2);

        // [S-23] switches behind a lock aren't the user switching.
        t.on_lock(d1(40));
        t.on_foreground(d1(50), "code".into(), None);
        assert_eq!(t.day_stats("2026-07-01", None).switch_count, 2);
        t.on_unlock(d1(60));
        t.on_foreground(d1(70), "chrome".into(), None);
        assert_eq!(t.day_stats("2026-07-01", None).switch_count, 3);

        // Counted per DATE, so a new day starts from zero.
        assert_eq!(t.day_stats("2026-07-02", None).switch_count, 0);
        t.on_foreground(d2(10), "code".into(), None);
        assert_eq!(t.day_stats("2026-07-02", None).switch_count, 1);
        assert_eq!(t.day_stats("2026-07-01", None).switch_count, 3);
    }

    #[test]
    fn s55_switch_counts_round_trip_and_follow_retention() {
        // Additive field: a store without it still loads as a valid v1 store.
        let raw = "{\"schema_version\":1,\"days\":{\"2026-07-01\":{\"chrome\":30}},\"names\":{}}";
        let (_, _, _, switches) = parse_store(raw).expect("v1 store without switches must parse");
        assert!(switches.is_empty());

        // Junk keys and zero counts are dropped, like the day/exe hygiene.
        let raw = "{\"schema_version\":1,\"days\":{},\"names\":{},\"switches\":{\"2026-07-01\":4,\"zzz\":9,\"2026-07-02\":0}}";
        let (_, _, _, switches) = parse_store(raw).expect("valid envelope");
        assert_eq!(switches.get("2026-07-01").copied(), Some(4));
        assert!(!switches.contains_key("zzz"));
        assert!(!switches.contains_key("2026-07-02"));

        // [S-28] a counter never outlives the day it belongs to.
        let mut t = Tracker::default();
        for i in 0..(RETENTION_DAYS + 3) {
            let date = format!("2026-01-{i:03}");
            add_secs(&mut t.days, &date, "chrome", 10);
            t.switches.insert(date, 1);
        }
        t.prune();
        assert_eq!(t.days.len(), RETENTION_DAYS);
        assert_eq!(t.switches.len(), RETENTION_DAYS);
        assert!(t.switches.keys().all(|d| t.days.contains_key(d)));
    }

    #[test]
    fn s47_entrypoint_identifier_names_read_as_the_app() {
        // The real-world case: WhatsApp's FileDescription is "WhatsApp.Root".
        assert_eq!(sanitize_name("WhatsApp.Root").as_deref(), Some("WhatsApp"));
        assert_eq!(strip_entrypoint_suffix("Teams.exe"), "Teams");
        assert_eq!(strip_entrypoint_suffix("Foo.APP"), "Foo"); // tail is case-insensitive
        // Real names keep their dots: a meaningful tail, or any whitespace.
        assert_eq!(strip_entrypoint_suffix("Node.js JavaScript Runtime"), "Node.js JavaScript Runtime");
        assert_eq!(strip_entrypoint_suffix("Microsoft.Todos"), "Microsoft.Todos");
        assert_eq!(strip_entrypoint_suffix("Google Chrome"), "Google Chrome");
        assert_eq!(strip_entrypoint_suffix(".Root"), ".Root"); // no head to keep
        // Only a still-identifier-shaped name asks for the ProductName fallback.
        assert!(is_identifier_like("Microsoft.Todos"));
        assert!(!is_identifier_like("WhatsApp"));
        assert!(!is_identifier_like("Node.js JavaScript Runtime"));
    }

    #[test]
    fn s47_entrypoint_names_healed_on_store_load() {
        // A store cached before the fix must self-heal, like the [S-21] garbage.
        let raw = "{\"schema_version\":1,\"days\":{},\"names\":{\"whatsapp\":\"WhatsApp.Root\"},\"paths\":{}}";
        let (_, names, _, _) = parse_store(raw).expect("valid envelope");
        assert_eq!(names.get("whatsapp").map(String::as_str), Some("WhatsApp"));
    }

    #[test]
    fn s21_corrupt_names_healed_on_store_load() {
        // A store written before the file_description NUL fix. On load the bad
        // name is truncated at the first control unit; clean names pass through.
        let raw = "{\"schema_version\":1,\"days\":{},\"names\":{\"spotify\":\"Spotify\\u00008\\u0016\\u0001FileV\",\"chrome\":\"Google Chrome\"},\"paths\":{}}";
        let (_, names, _, _) = parse_store(raw).expect("valid envelope");
        assert_eq!(names.get("spotify").map(String::as_str), Some("Spotify"));
        assert_eq!(names.get("chrome").map(String::as_str), Some("Google Chrome"));
    }

    #[test]
    fn s38_stems_missing_paths_and_names_list_incomplete_history_buckets() {
        let mut t = Tracker::default();
        add_secs(&mut t.days, "2026-07-01", "chrome", 10);
        add_secs(&mut t.days, "2026-07-01", "spotify", 20);
        add_secs(&mut t.days, "2026-07-02", "chrome", 5); // same exe across days
        t.note_exe_path("chrome", "C:/apps/chrome.exe");
        t.names.insert("chrome".into(), "Google Chrome".into());
        // chrome is complete; spotify lacks both; the cross-day dup is deduped.
        assert_eq!(t.stems_missing_paths(), vec!["spotify".to_string()]);
        assert_eq!(t.stems_missing_names(), vec!["spotify".to_string()]);
        t.note_exe_path("spotify", "C:/apps/spotify.exe");
        assert!(t.stems_missing_paths().is_empty());
        assert_eq!(t.stems_missing_names(), vec!["spotify".to_string()]); // still no name
    }

    #[test]
    fn civil_from_days_pins_calendar() {
        assert_eq!(civil_from_days(0), (1970, 1, 1));
        assert_eq!(civil_from_days(20_270), (2025, 7, 1));
        assert_eq!(civil_from_days(11_016), (2000, 2, 29)); // leap-year aware
    }

    #[test]
    fn s41_days_from_civil_inverts_civil_from_days() {
        for z in [0_i64, 11_016, 20_270, -1, -719_162, 30_000] {
            let (y, m, d) = civil_from_days(z);
            assert_eq!(days_from_civil(y, m, d), z, "roundtrip failed for {z}");
        }
    }

    #[test]
    fn s41_shift_date_steps_whole_days_across_boundaries() {
        assert_eq!(shift_date("2026-07-10", -1), "2026-07-09");
        assert_eq!(shift_date("2026-07-01", -1), "2026-06-30"); // month boundary
        assert_eq!(shift_date("2026-01-01", -1), "2025-12-31"); // year boundary
        assert_eq!(shift_date("2024-03-01", -1), "2024-02-29"); // leap day
        assert_eq!(shift_date("2026-07-10", -29), "2026-06-11"); // 30-day window
        assert_eq!(shift_date("2026-07-10", 0), "2026-07-10");
        // A malformed key is returned unchanged, never a half-built date.
        assert_eq!(shift_date("not-a-date", -1), "not-a-date");
        assert_eq!(shift_date("2026-13-01", -1), "2026-13-01");
    }

    #[test]
    fn s41_range_stats_returns_one_entry_per_day_oldest_first() {
        let mut t = Tracker::default();
        // Two days of history, plus a gap day with no usage at all. Seeded
        // directly: this pins the WINDOWING, not the accrual rules ([S-22]/[S-32]
        // cover those), so the days need not be reachable by one tick.
        t.days.insert(
            "2026-07-01".into(),
            BTreeMap::from([("chrome".to_string(), 600_u64)]),
        );
        t.days.insert(
            "2026-07-02".into(),
            BTreeMap::from([("code".to_string(), 1_200_u64)]),
        );

        // "Now" is 2026-07-03, so the 3-day window is 07-01..07-03.
        let now = stamp("2026-07-03", 0, DAY1_MIDNIGHT + 2 * 86_400);
        let days = range_stats(&t, &now, 3);
        assert_eq!(
            days.iter().map(|d| d.date.as_str()).collect::<Vec<_>>(),
            vec!["2026-07-01", "2026-07-02", "2026-07-03"],
        );
        assert_eq!(days[0].total_secs, 600);
        assert_eq!(days[1].total_secs, 1_200);
        // A day with no usage is still present, as an explicit zero — the chart
        // must show a gap, not skip the day.
        assert_eq!(days[2].total_secs, 0);
        assert!(days[2].apps.is_empty());
    }

    #[test]
    fn s41_range_stats_clamps_the_window_to_retention() {
        let t = Tracker::default();
        let now = stamp("2026-07-03", 0, DAY1_MIDNIGHT);
        assert_eq!(range_stats(&t, &now, 0).len(), 1); // never empty
        assert_eq!(range_stats(&t, &now, 7).len(), 7);
        assert_eq!(range_stats(&t, &now, 30).len(), 30);
        assert_eq!(range_stats(&t, &now, 10_000).len(), RETENTION_DAYS);
    }
}
