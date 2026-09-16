// ---------------------------------------------------------------------------
// Anonymous usage analytics, TelemetryDeck.
//
// Split of responsibility (see docs/ANALYTICS.md for the full spec):
//   - THIS module owns CONSENT, CADENCE and every signal's CONTENT. It decides
//     what may be sent and when, builds each signal, and is the only thing that
//     touches analytics.json.
//   - The FRONTEND (`src/analytics.ts`) owns TRANSPORT. It runs TelemetryDeck's
//     official `@telemetrydeck/sdk`, reports the handful of facts only a webview
//     can see (screen, locale, time zone), and puts signals on the wire.
//
// TelemetryDeck ships no Rust SDK (Swift/Kotlin/Flutter/JS/React/Vue/Obj-C
// only), so the official client can only run in the webview. Consent stays
// here anyway: the frontend cannot send anything it was not handed by
// `analytics_plan` / `analytics_track`, so a fault in the webview can never
// re-enable a user who opted out.
//
// What we hand out: the SHA-256 of a random per-install UUID (`clientUser`).
// The raw UUID never leaves this module, so it never reaches the webview.
//
// The signals and parameter names are TelemetryDeck's own, so their built-in
// dashboards (Acquisition, Activation, Retention, Errors) work without any
// custom insight configuration:
//   - TelemetryDeck.Acquisition.newInstallDetected: once ever per install
//   - TelemetryDeck.Session.started:                once per app launch
//   - TelemetryDeck.Error.occurred:                 per reported error
//   - TelemetryDeck.Navigation.pathChanged:         per view/window navigation
//   - TelemetryDeck.Purchase.completed:             on license activation
//   - app_heartbeat:                                at most once per day, so a
//     machine that is never restarted still counts as active
//   - app_opt_out:                                  once, when a user opts out
//
// Consent is a THREE-state decision, not a checkbox ([S-03]): until the user
// answers the first-run prompt nothing is sent, no install id is minted, and
// analytics.json holds nothing but the (absent) decision itself.
//
// Replaced Google Analytics 4 in v0.15.0. GA4 has no per-install identifier,
// so installs/MAU had to be reconstructed from its user model.
// ---------------------------------------------------------------------------

use std::collections::{BTreeMap, BTreeSet};
use std::fmt::Write as _;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use tauri::{AppHandle, Manager};

/// Bump when the on-disk shape changes. A file with any other version is
/// treated as unreadable → typed default (never a half-built object).
///
/// Deliberately still 1 across the GA4 → TelemetryDeck switch AND across the
/// retention fields added in v0.16.0: bumping it would discard every existing
/// file and reset `optOut` to false, silently re-enabling analytics for people
/// who had turned it off. New keys are added with `#[serde(default)]` instead,
/// so an older file simply starts its retention history at zero ([S-11]).
const SCHEMA_VERSION: u32 = 1;

/// TelemetryDeck app ID for Themia (dashboard → App → Settings). Committed
/// intentionally: it is a client-side write key that ships inside the binary
/// and is trivially extractable, so treating it as a secret buys nothing.
///
/// Empty means "this build has no analytics configured": every plan comes back
/// empty, so the frontend sends nothing ([S-04]).
const TD_APP_ID: &str = "67AD8F18-8230-41A1-ADF2-13296ADB8AB6";

/// Ingest API v2, addressed to the `com.nathaniel-walser` namespace. Handed to
/// the SDK as its `target` rather than left to the SDK's default, so a change
/// to that default cannot silently reroute Themia's data. Both forms are
/// accepted by the server (probed: each returns 200).
const INGEST_URL: &str = "https://nom.telemetrydeck.com/v2/namespace/com.nathaniel-walser/";

/// How much retention history is kept on disk. Only counts are ever sent, and
/// nothing older than a month is used, so this is a bound on file growth rather
/// than a data-retention policy.
const MAX_DISTINCT_DAYS: usize = 400;

/// Serializes every read-modify-write of analytics.json so concurrent callers
/// (several windows' plan calls + a Settings toggle) can't interleave.
static FILE_LOCK: Mutex<()> = Mutex::new(());

/// Lifecycle events already claimed by some window in THIS process.
///
/// Themia opens one webview per monitor and each runs the same startup code, so
/// without this every monitor would send its own copy of each signal. The first
/// caller claims; everyone else gets an empty plan. In-memory on purpose: a
/// claim that is never confirmed leaves the on-disk marker unset, so the event
/// is retried on the next launch rather than lost ([S-14]).
static CLAIMED: Mutex<BTreeSet<AnalyticsEvent>> = Mutex::new(BTreeSet::new());

/// Whether some window in THIS process has already taken the job of showing
/// the first-run consent prompt. Same reasoning as `CLAIMED`: one webview per
/// monitor, and the question must be asked once, not once per screen ([S-27]).
static PROMPT_CLAIMED: Mutex<bool> = Mutex::new(false);

/// The last environment the webview reported. Signals raised from Rust-side
/// state (or from a window that never sent one) still carry the full default
/// parameter set this way ([S-18]).
static ENV: Mutex<Option<WebviewEnv>> = Mutex::new(None);

// ── On-disk config ─────────────────────────────────────────────────────────

/// Whether the user has agreed to anonymous analytics ([S-03]).
///
/// Three states, not a boolean: "hasn't answered yet" is a distinct thing from
/// "said no", and only the first one may raise the prompt. Storing a UUID on
/// someone's machine for analytics needs consent first (ePrivacy Art. 5(3)),
/// so `Unset` behaves exactly like `Denied` on the wire — it differs only in
/// that we may still ask.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum Consent {
    #[default]
    Unset,
    Granted,
    Denied,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct AnalyticsConfig {
    #[serde(default)]
    schema_version: u32,
    /// Random, anonymous, stable per install. Empty == not yet generated.
    /// Never transmitted and never handed to the frontend: only its SHA-256
    /// goes out, as `clientUser`.
    #[serde(default)]
    install_id: String,
    /// The user's answer to the first-run prompt. Default `Unset`: a fresh
    /// install sends nothing until it is answered ([S-03]).
    #[serde(default)]
    consent: Consent,
    /// Pre-consent key (v0.16.x and earlier), when analytics were on by
    /// default and this flag was the only way out. Read once by
    /// `migrate_consent` and never written again — `skip_serializing` drops it
    /// from the file on the next save ([S-25]).
    #[serde(default, skip_serializing)]
    opt_out: Option<bool>,
    /// Whether the one-time new-install signal has reached TelemetryDeck.
    ///
    /// Deliberately NOT the older `installReported` / `firstRunReported` keys.
    /// `installReported` marked GA4's `app_install` delivered, but GA silently
    /// dropped all of those (reserved name, [S-10]); `firstRunReported` marked
    /// delivery to GA4, which TelemetryDeck never saw ([S-11]). Both are
    /// ignored, so a fresh key defaulting to false makes every existing install
    /// send exactly one catch-up signal on the next launch after upgrading.
    #[serde(default)]
    first_run_sent_td: bool,
    /// Epoch-day (UTC days since 1970) of the last delivered heartbeat. Gates
    /// heartbeats to at most one per calendar day across launches, windows and
    /// the frontend's periodic re-poll.
    #[serde(default)]
    last_heartbeat_ymd: u64,

    // ── Acquisition / retention state ([S-19]) ──────────────────────────────
    /// Epoch-day of the first session ever. 0 == not yet recorded.
    #[serde(default)]
    first_session_ymd: u64,
    /// Epoch-days on which the app was launched, ascending, capped at
    /// `MAX_DISTINCT_DAYS`. Only counts derived from it are ever sent.
    #[serde(default)]
    distinct_days_used: Vec<u64>,
    /// Sessions started, ever.
    #[serde(default)]
    total_sessions_count: u64,
    /// Sessions that ended cleanly, and their summed length. The average is
    /// derived from these two rather than stored, so it can never drift.
    #[serde(default)]
    ended_sessions_count: u64,
    #[serde(default)]
    total_session_seconds: u64,
    /// Length of the previous session, in seconds.
    #[serde(default)]
    previous_session_seconds: u64,
}

impl Default for AnalyticsConfig {
    fn default() -> Self {
        AnalyticsConfig {
            schema_version: SCHEMA_VERSION,
            install_id: String::new(),
            consent: Consent::Unset,
            opt_out: None,
            first_run_sent_td: false,
            last_heartbeat_ymd: 0,
            first_session_ymd: 0,
            distinct_days_used: Vec::new(),
            total_sessions_count: 0,
            ended_sessions_count: 0,
            total_session_seconds: 0,
            previous_session_seconds: 0,
        }
    }
}

/// Parse the on-disk JSON. A wrong/missing schema version or invalid JSON
/// yields the typed default: we never build a half-populated config from a
/// corrupt file. (The caller logs which case occurred.)
fn parse_config(s: &str) -> AnalyticsConfig {
    match serde_json::from_str::<AnalyticsConfig>(s) {
        Ok(mut c) if c.schema_version == SCHEMA_VERSION => {
            migrate_consent(&mut c);
            c
        }
        _ => AnalyticsConfig::default(),
    }
}

/// Fold the pre-consent `optOut` flag into the tri-state decision ([S-25]).
///
/// `optOut: true` was an explicit "no" and is honoured forever as `Denied`.
/// `optOut: false` was never a decision — it was the old default — so it maps
/// to `Unset` and the user is asked once, which is the whole point of the
/// change. A file that already carries a `consent` value always wins: a
/// migration must never overwrite an answer.
fn migrate_consent(cfg: &mut AnalyticsConfig) {
    let legacy = cfg.opt_out.take();
    if cfg.consent == Consent::Unset && legacy == Some(true) {
        cfg.consent = Consent::Denied;
    }
}

fn analytics_path(app: &AppHandle) -> PathBuf {
    app.path()
        .app_data_dir()
        .unwrap_or_else(|_| PathBuf::from("."))
        .join("analytics.json")
}

/// Atomic persist: write a sibling temp file then rename over the target, so a
/// crash mid-write can never leave a truncated config behind.
fn write_atomic(path: &Path, bytes: &[u8]) -> std::io::Result<()> {
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)?;
    }
    let tmp = path.with_extension("json.tmp");
    std::fs::write(&tmp, bytes)?;
    std::fs::rename(&tmp, path)
}

/// Read config from disk, distinguishing "absent" (normal first run) from
/// "corrupt" (present but unreadable, logged loudly). Ensures a stable
/// `install_id` exists, persisting it on first run / after repair. Single-flight
/// via `FILE_LOCK`.
fn load_or_init(app: &AppHandle) -> AnalyticsConfig {
    // Recover a poisoned lock instead of panicking: a telemetry write must
    // never take down a Tauri command.
    let _guard = FILE_LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let path = analytics_path(app);

    let mut cfg = read_config(&path);

    // The install id is analytics state, so it is minted on consent and not
    // before ([S-26]). Until then this function only reads.
    if cfg.consent == Consent::Granted && cfg.install_id.is_empty() {
        cfg.install_id = uuid::Uuid::new_v4().to_string();
        cfg.schema_version = SCHEMA_VERSION;
        persist(&path, &cfg);
    }
    cfg
}

/// Read without initialising. Callers already holding `FILE_LOCK` use this.
fn read_config(path: &Path) -> AnalyticsConfig {
    match std::fs::read_to_string(path) {
        Ok(s) => parse_config(&s),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => AnalyticsConfig::default(),
        Err(e) => {
            log::warn!(target: "analytics", "read failed for {}: {e}, using defaults", path.display());
            AnalyticsConfig::default()
        }
    }
}

fn persist(path: &Path, cfg: &AnalyticsConfig) {
    match serde_json::to_vec_pretty(cfg) {
        Ok(bytes) => {
            if let Err(e) = write_atomic(path, &bytes) {
                log::warn!(target: "analytics", "write failed for {}: {e}", path.display());
            }
        }
        // ignore: a serialize failure on our own owned struct is effectively
        // impossible; log rather than unwrap so we never panic.
        Err(e) => log::warn!(target: "analytics", "serialize failed: {e}"),
    }
}

/// Read-modify-write under the file lock. Everything that mutates persisted
/// state goes through here, so no caller can stomp a concurrent opt-out write.
fn update_config(app: &AppHandle, f: impl FnOnce(&mut AnalyticsConfig)) -> AnalyticsConfig {
    let _guard = FILE_LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let path = analytics_path(app);
    let mut cfg = read_config(&path);
    cfg.schema_version = SCHEMA_VERSION;
    f(&mut cfg);
    // After `f`, so the call that grants consent mints the id in the same
    // write; before consent there is nothing to identify ([S-26]).
    if cfg.consent == Consent::Granted && cfg.install_id.is_empty() {
        cfg.install_id = uuid::Uuid::new_v4().to_string();
    }
    persist(&path, &cfg);
    cfg
}

/// True iff this answer REVOKES a consent that was previously given — the
/// single moment we emit the one-time opt-out signal ([S-07]).
///
/// Answering "no" to the first-run prompt is not a revocation: nothing was ever
/// sent for that install, so there is no departure to announce (and no consent
/// under which to announce it).
fn is_revocation(prev: Consent, next: Consent) -> bool {
    prev == Consent::Granted && next == Consent::Denied
}

/// Record the start of a session: one more session, and today counted once as
/// a distinct day used ([S-19]). Pure so the bookkeeping is unit-tested.
fn record_session_start(cfg: &mut AnalyticsConfig, today: u64) {
    if cfg.first_session_ymd == 0 {
        cfg.first_session_ymd = today;
    }
    cfg.total_sessions_count = cfg.total_sessions_count.saturating_add(1);
    if cfg.distinct_days_used.last() != Some(&today) {
        cfg.distinct_days_used.push(today);
        // Keep the tail: the only question asked of this list is "how many of
        // the last 30 days", so old entries are dead weight.
        if cfg.distinct_days_used.len() > MAX_DISTINCT_DAYS {
            let excess = cfg.distinct_days_used.len() - MAX_DISTINCT_DAYS;
            cfg.distinct_days_used.drain(..excess);
        }
    }
}

/// Record a finished session. A wildly long value (a machine suspended for a
/// week) would poison the average, so it is clamped to 24h.
fn record_session_end(cfg: &mut AnalyticsConfig, seconds: u64) {
    let seconds = seconds.min(24 * 60 * 60);
    cfg.previous_session_seconds = seconds;
    cfg.ended_sessions_count = cfg.ended_sessions_count.saturating_add(1);
    cfg.total_session_seconds = cfg.total_session_seconds.saturating_add(seconds);
}

// ── Events ──────────────────────────────────────────────────────────────────

/// Lifecycle events: sent at most once per install / launch / day, claimed
/// across windows, and (for the first two) confirmed back so a failed send is
/// retried rather than lost.
///
/// A discriminated union across the IPC boundary, never a bare string: an
/// unknown value fails to deserialize instead of being forwarded to
/// TelemetryDeck. The serde names ARE the wire event names.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize)]
pub enum AnalyticsEvent {
    /// TelemetryDeck's own new-install signal — the one its Acquisition
    /// dashboard counts. Our previous custom `app_first_run` name was invisible
    /// there ([S-20]).
    #[serde(rename = "TelemetryDeck.Acquisition.newInstallDetected")]
    NewInstall,
    /// Once per app launch; drives the Activation dashboard's session metrics.
    #[serde(rename = "TelemetryDeck.Session.started")]
    SessionStarted,
    /// Custom: covers the machine that is never restarted, which would
    /// otherwise stop counting as active after its single session signal.
    #[serde(rename = "app_heartbeat")]
    Heartbeat,
    /// Emitted the moment a user turns analytics OFF, so opt-outs are
    /// observable. Sent before the subsystem goes quiet.
    #[serde(rename = "app_opt_out")]
    OptOut,
}

impl AnalyticsEvent {
    /// The wire event type. TelemetryDeck has no reserved-name list, so unlike
    /// GA4 there is no risk of a name being silently dropped.
    fn name(self) -> &'static str {
        match self {
            AnalyticsEvent::NewInstall => "TelemetryDeck.Acquisition.newInstallDetected",
            AnalyticsEvent::SessionStarted => "TelemetryDeck.Session.started",
            AnalyticsEvent::Heartbeat => "app_heartbeat",
            AnalyticsEvent::OptOut => "app_opt_out",
        }
    }

    /// Whether a confirmed delivery changes persisted state. Session starts and
    /// opt-outs are launch-scoped facts already recorded when the signal was
    /// built, so re-sending them later would be wrong, not helpful.
    fn is_persisted(self) -> bool {
        matches!(self, AnalyticsEvent::NewInstall | AnalyticsEvent::Heartbeat)
    }
}

/// TelemetryDeck's error categories, spelled exactly as its Errors preset
/// expects them.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "kebab-case")]
pub enum ErrorCategory {
    ThrownException,
    UserInput,
    AppState,
}

impl ErrorCategory {
    fn name(self) -> &'static str {
        match self {
            ErrorCategory::ThrownException => "thrown-exception",
            ErrorCategory::UserInput => "user-input",
            ErrorCategory::AppState => "app-state",
        }
    }
}

/// Purchase shapes TelemetryDeck knows about.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "kebab-case")]
pub enum PurchaseKind {
    Subscription,
    OneTimePurchase,
}

impl PurchaseKind {
    fn name(self) -> &'static str {
        match self {
            PurchaseKind::Subscription => "subscription",
            PurchaseKind::OneTimePurchase => "one-time-purchase",
        }
    }
}

/// Events raised by the app as they happen, with their own parameters. A closed
/// set with typed fields, so the webview cannot invent an event type or smuggle
/// arbitrary parameters onto the wire.
#[derive(Debug, Clone, Deserialize)]
#[serde(tag = "event", rename_all = "camelCase", deny_unknown_fields)]
pub enum TrackedEvent {
    /// Something went wrong. `id` groups similar errors; it must be a stable
    /// slug, never an interpolated message.
    Error {
        id: String,
        category: ErrorCategory,
        /// Optional static description. Sanitised before sending: anything that
        /// looks like a path, URL or address is dropped ([S-22]).
        #[serde(default)]
        message: Option<String>,
    },
    /// The user moved from one screen to another.
    Navigation { from: String, to: String },
    /// A license was activated.
    Purchase {
        product_id: String,
        kind: PurchaseKind,
    },
}

// ── What the webview contributes ────────────────────────────────────────────

/// Facts only the webview can see. Every field optional: a missing one drops
/// its parameter rather than inventing a value. `deny_unknown_fields` keeps
/// this a closed shape at the IPC boundary.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct WebviewEnv {
    /// Full BCP-47 locale, e.g. `de-CH`.
    pub locale: Option<String>,
    /// The user's preferred language, e.g. `de`.
    pub preferred_language: Option<String>,
    /// Region subtag, e.g. `CH`.
    pub region: Option<String>,
    /// Themia's own UI language, which can differ from the system's.
    pub app_language: Option<String>,
    /// IANA zone, e.g. `Europe/Zurich`.
    pub time_zone: Option<String>,
    /// `Dark` or `Light`.
    pub color_scheme: Option<String>,
    /// `leftToRight` or `rightToLeft`.
    pub layout_direction: Option<String>,
    pub screen_width: Option<f64>,
    pub screen_height: Option<f64>,
    pub screen_scale: Option<f64>,
}

/// Trim, cap and reject junk from a string that came across the IPC boundary.
/// Control characters and absurd lengths never reach the wire.
fn clean(value: &str, max: usize) -> Option<String> {
    let v: String = value
        .trim()
        .chars()
        .filter(|c| !c.is_control())
        .take(max)
        .collect();
    let v = v.trim().to_string();
    if v.is_empty() { None } else { Some(v) }
}

/// A message may only ever be a short, static description. Anything carrying a
/// path, URL, address or Windows drive letter is dropped and logged: those are
/// the shapes user data arrives in, and TelemetryDeck explicitly warns against
/// sending them ([S-22]).
fn clean_message(value: &str) -> Option<String> {
    let v = clean(value, 100)?;
    let leaky = v.contains('@')
        || v.contains('\\')
        || v.contains("://")
        || v.contains('/')
        || v.as_bytes().windows(2).any(|w| w[1] == b':' && w[0].is_ascii_alphabetic());
    if leaky {
        log::warn!(target: "analytics", "dropped an error message that looks like user data");
        return None;
    }
    Some(v)
}

// ── The signals handed to the frontend ──────────────────────────────────────

/// One fully-built signal. The frontend adds nothing: it posts `payload` under
/// `kind` and, if `marker` is set, confirms delivery back.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Signal {
    /// The wire event type.
    #[serde(rename = "type")]
    pub kind: String,
    pub payload: BTreeMap<String, String>,
    /// Confirm this back via `analytics_mark_sent` once delivered. `None` means
    /// there is nothing to persist.
    pub marker: Option<AnalyticsEvent>,
}

/// Everything the frontend needs to configure the SDK and send what is due.
/// An empty `signals` means "send nothing": opted out, unconfigured, already
/// sent today, or another window already claimed the work.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AnalyticsPlan {
    app_id: &'static str,
    /// SHA-256 of the install UUID. The SDK hashes it again with its own salt;
    /// double hashing is harmless and keeps the raw UUID inside Rust ([S-09]).
    client_user: String,
    target: &'static str,
    /// Debug builds mark every signal test-mode so developer runs stay out of
    /// production queries ([S-06]).
    test_mode: bool,
    signals: Vec<Signal>,
}

impl AnalyticsPlan {
    fn new(cfg: &AnalyticsConfig, signals: Vec<Signal>) -> Self {
        AnalyticsPlan {
            app_id: TD_APP_ID,
            client_user: client_user(&cfg.install_id),
            target: INGEST_URL,
            test_mode: cfg!(debug_assertions),
            signals,
        }
    }
}

/// The default parameters attached to EVERY signal, using TelemetryDeck's own
/// names so its dashboards group them without custom configuration ([S-16]).
///
/// The SDK's own body is only {clientUser, sessionID, appID, type,
/// telemetryClientVersion} — it knows nothing about the app it is embedded in,
/// so everything here would otherwise be lost.
fn default_payload(cfg: &AnalyticsConfig, env: &WebviewEnv, today: u64) -> BTreeMap<String, String> {
    let mut p: BTreeMap<String, String> = BTreeMap::new();
    let mut put = |k: &str, v: String| {
        p.insert(k.to_string(), v);
    };

    put("TelemetryDeck.AppInfo.version", env!("CARGO_PKG_VERSION").to_string());

    // Device. `platform` and `operatingSystem` are both sent and both spelled
    // the way TelemetryDeck's own SDKs spell them ("Windows", not Rust's
    // lowercase target name): the dashboard groups verbatim, so `windows` and
    // `Windows` would be two different platforms ([S-17]).
    put("TelemetryDeck.Device.platform", platform_name().to_string());
    put("TelemetryDeck.Device.operatingSystem", platform_name().to_string());
    put("TelemetryDeck.Device.architecture", std::env::consts::ARCH.to_string());
    for (k, v) in os_version_params() {
        put(k, v);
    }
    if let Some(v) = device_model() {
        put("TelemetryDeck.Device.modelName", v);
    }
    if let Some(v) = device_brand() {
        put("TelemetryDeck.Device.brand", v);
    }
    if let Some(v) = env.time_zone.as_deref().and_then(|s| clean(s, 64)) {
        put("TelemetryDeck.Device.timeZone", v);
    }
    if let Some(v) = env.screen_width.filter(|v| *v > 0.0) {
        put("TelemetryDeck.Device.screenResolutionWidth", format!("{v}"));
    }
    if let Some(v) = env.screen_height.filter(|v| *v > 0.0) {
        put("TelemetryDeck.Device.screenResolutionHeight", format!("{v}"));
    }
    if let Some(v) = env.screen_scale.filter(|v| *v > 0.0) {
        put("TelemetryDeck.Device.screenScaleFactor", format!("{v}"));
    }

    // Run context. Themia is a plain native app: never a simulator, never an
    // app store or TestFlight build.
    put("TelemetryDeck.RunContext.isDebug", cfg!(debug_assertions).to_string());
    put("TelemetryDeck.RunContext.isSimulator", "false".to_string());
    put("TelemetryDeck.RunContext.isTestFlight", "false".to_string());
    put("TelemetryDeck.RunContext.isAppStore", "false".to_string());
    put("TelemetryDeck.RunContext.targetEnvironment", "native".to_string());
    if let Some(v) = env.app_language.as_deref().and_then(|s| clean(s, 32)) {
        put("TelemetryDeck.RunContext.language", v);
    }
    if let Some(v) = env.locale.as_deref().and_then(|s| clean(s, 32)) {
        put("TelemetryDeck.RunContext.locale", v);
    }

    // User preferences.
    if let Some(v) = env.preferred_language.as_deref().and_then(|s| clean(s, 32)) {
        put("TelemetryDeck.UserPreference.language", v);
    }
    if let Some(v) = env.region.as_deref().and_then(|s| clean(s, 16)) {
        put("TelemetryDeck.UserPreference.region", v);
    }
    if let Some(v) = env.color_scheme.as_deref().and_then(|s| clean(s, 16)) {
        put("TelemetryDeck.UserPreference.colorScheme", v);
    }
    if let Some(v) = env.layout_direction.as_deref().and_then(|s| clean(s, 16)) {
        put("TelemetryDeck.UserPreference.layoutDirection", v);
    }

    // Acquisition + retention, from our own on-disk history ([S-19]).
    if cfg.first_session_ymd > 0 {
        put("TelemetryDeck.Acquisition.firstSessionDate", iso_date(cfg.first_session_ymd));
    }
    put("TelemetryDeck.Retention.totalSessionsCount", cfg.total_sessions_count.to_string());
    put("TelemetryDeck.Retention.distinctDaysUsed", cfg.distinct_days_used.len().to_string());
    put(
        "TelemetryDeck.Retention.distinctDaysUsedLastMonth",
        distinct_days_last_month(&cfg.distinct_days_used, today).to_string(),
    );
    put(
        "TelemetryDeck.Retention.averageSessionSeconds",
        average_session_seconds(cfg.total_session_seconds, cfg.ended_sessions_count).to_string(),
    );
    if cfg.previous_session_seconds > 0 {
        put(
            "TelemetryDeck.Retention.previousSessionSeconds",
            cfg.previous_session_seconds.to_string(),
        );
    }

    p
}

/// Days used within the last 30, inclusive of today.
fn distinct_days_last_month(days: &[u64], today: u64) -> usize {
    let cutoff = today.saturating_sub(30);
    days.iter().filter(|d| **d >= cutoff).count()
}

/// Mean session length, rounded down. Zero when nothing has ended yet, which is
/// what TelemetryDeck's own SDKs report for a first session.
fn average_session_seconds(total: u64, ended: u64) -> u64 {
    if ended == 0 { 0 } else { total / ended }
}

/// The OS family as TelemetryDeck's own SDKs report it ("macOS", "iOS", …), not
/// Rust's lowercase target name.
fn platform_name() -> &'static str {
    match std::env::consts::OS {
        "windows" => "Windows",
        "macos" => "macOS",
        "linux" => "Linux",
        other => other,
    }
}

/// The `TelemetryDeck.Device.system*Version` parameters, or empty when the OS
/// version can't be determined. Left out entirely rather than faked: an absent
/// key reads as "unset" in the dashboard, an invented version reads as a fact.
#[cfg(target_os = "windows")]
fn os_version_params() -> Vec<(&'static str, String)> {
    use windows::Wdk::System::SystemServices::RtlGetVersion;
    use windows::Win32::System::SystemInformation::OSVERSIONINFOW;

    let mut info = OSVERSIONINFOW {
        dwOSVersionInfoSize: std::mem::size_of::<OSVERSIONINFOW>() as u32,
        ..Default::default()
    };
    // SAFETY: `info` is a live, correctly sized OSVERSIONINFOW that outlives the
    // call; RtlGetVersion only writes into it. Unlike GetVersionExW it reads the
    // real version instead of whatever the app manifest claims compatibility
    // with, which is what makes the build number trustworthy.
    let status = unsafe { RtlGetVersion(&mut info) };
    if status.is_err() {
        log::warn!(target: "analytics", "RtlGetVersion failed: {status:?}");
        return Vec::new();
    }
    version_params(info.dwMajorVersion, info.dwMinorVersion, info.dwBuildNumber)
}

#[cfg(not(target_os = "windows"))]
fn os_version_params() -> Vec<(&'static str, String)> {
    Vec::new()
}

/// Pure, unit-tested: the three version parameters for a Windows kernel version
/// triple ([S-17]).
///
/// All three are sent explicitly, as the official SDKs do, because the dashboard
/// offers full / major.minor / major as separate Top-N dimensions and does not
/// derive the coarser ones from `systemVersion`.
///
/// Windows 11 still reports kernel version 10.0 — only the build number
/// separates it from Windows 10 — so the major number is corrected to 11 from
/// build 22000 (the first Windows 11 build) upwards. Reporting the kernel
/// version verbatim would file every Windows 11 machine under "10", which is the
/// one split worth having.
#[cfg(target_os = "windows")]
fn version_params(major: u32, minor: u32, build: u32) -> Vec<(&'static str, String)> {
    let major = if major == 10 && build >= 22_000 { 11 } else { major };
    vec![
        (
            "TelemetryDeck.Device.systemVersion",
            format!("{major}.{minor}.{build}"),
        ),
        (
            "TelemetryDeck.Device.systemMajorMinorVersion",
            format!("{major}.{minor}"),
        ),
        ("TelemetryDeck.Device.systemMajorVersion", major.to_string()),
    ]
}

/// Machine model / manufacturer, as the BIOS reports them ("XPS 15 9520",
/// "Dell Inc."). Device class, not device identity: no serial, asset tag or
/// machine name is read.
#[cfg(target_os = "windows")]
fn bios_string(value: &str) -> Option<String> {
    use winapi::um::winreg::{RegGetValueW, HKEY_LOCAL_MACHINE, RRF_RT_REG_SZ};

    let key: Vec<u16> = r"HARDWARE\DESCRIPTION\System\BIOS"
        .encode_utf16()
        .chain(std::iter::once(0))
        .collect();
    let name: Vec<u16> = value.encode_utf16().chain(std::iter::once(0)).collect();
    let mut buf = [0u16; 128];
    let mut size = std::mem::size_of_val(&buf) as u32;
    // SAFETY: both strings are NUL-terminated and outlive the call; `buf`/`size`
    // describe the same buffer, and RegGetValueW writes at most `size` bytes
    // into it. It opens and closes the predefined key itself, so there is no
    // handle to leak.
    let rc = unsafe {
        RegGetValueW(
            HKEY_LOCAL_MACHINE,
            key.as_ptr(),
            name.as_ptr(),
            RRF_RT_REG_SZ,
            std::ptr::null_mut(),
            buf.as_mut_ptr().cast(),
            &mut size,
        )
    };
    if rc != 0 {
        return None;
    }
    // `size` is bytes including the terminating NUL.
    let chars = ((size as usize) / 2).saturating_sub(1).min(buf.len());
    clean(&String::from_utf16_lossy(&buf[..chars]), 64)
}

#[cfg(target_os = "windows")]
fn device_model() -> Option<String> {
    bios_string("SystemProductName")
}

#[cfg(target_os = "windows")]
fn device_brand() -> Option<String> {
    bios_string("SystemManufacturer")
}

#[cfg(not(target_os = "windows"))]
fn device_model() -> Option<String> {
    None
}

#[cfg(not(target_os = "windows"))]
fn device_brand() -> Option<String> {
    None
}

/// Lowercase-hex SHA-256 of the install UUID ([S-09]). Deterministic and
/// unsalted so it is stable across launches.
fn client_user(install_id: &str) -> String {
    let mut out = String::with_capacity(64);
    for b in Sha256::digest(install_id.as_bytes()) {
        // ignore: writing to a String is infallible.
        let _ = write!(out, "{b:02x}");
    }
    out
}

/// Current UTC day number (days since the epoch). Used to throttle heartbeats
/// to one per calendar day and to date the retention history.
fn epoch_day() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs() / 86_400)
        .unwrap_or(0)
}

/// ISO `yyyy-MM-dd` for a Unix epoch day (Howard Hinnant's `civil_from_days`).
/// Pure and unit-tested; avoids pulling a date crate in for one format.
fn iso_date(epoch_day: u64) -> String {
    let z = epoch_day as i64 + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097);
    let yoe = (doe - doe / 1_460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let y = if m <= 2 { y + 1 } else { y };
    format!("{y:04}-{m:02}-{d:02}")
}

/// Pure gate, unit-tested: a heartbeat is due when none has been sent today.
fn heartbeat_due(last_ymd: u64, today: u64) -> bool {
    last_ymd != today
}

/// Pure gate, unit-tested: send only when an app id is configured AND the user
/// has actively granted consent ([S-04]). `Unset` is as silent as `Denied`.
fn should_send(app_id: &str, consent: Consent) -> bool {
    !app_id.is_empty() && consent == Consent::Granted
}

/// Pure gate, unit-tested: what is owed given the persisted state, before
/// per-process claiming. A session start is owed on every launch — the claim
/// set, not this, is what makes it once per launch.
fn due_events(app_id: &str, consent: Consent, first_run_sent: bool, heartbeat: bool) -> Vec<AnalyticsEvent> {
    if !should_send(app_id, consent) {
        return Vec::new();
    }
    let mut out = Vec::new();
    if !first_run_sent {
        out.push(AnalyticsEvent::NewInstall);
    }
    out.push(AnalyticsEvent::SessionStarted);
    if heartbeat {
        out.push(AnalyticsEvent::Heartbeat);
    }
    out
}

/// Take the events not already claimed by another window in this process, and
/// record the ones taken ([S-14]).
fn claim(claimed: &mut BTreeSet<AnalyticsEvent>, due: Vec<AnalyticsEvent>) -> Vec<AnalyticsEvent> {
    due.into_iter().filter(|e| claimed.insert(*e)).collect()
}

/// Remember the webview's environment for signals raised later ([S-18]).
fn store_env(env: Option<WebviewEnv>) -> WebviewEnv {
    let mut slot = ENV.lock().unwrap_or_else(|e| e.into_inner());
    if let Some(env) = env {
        *slot = Some(env);
    }
    slot.clone().unwrap_or_default()
}

// ── Tauri commands ──────────────────────────────────────────────────────────

/// Ask what may be sent right now, and hand over the environment only the
/// webview can see. The frontend cannot send anything it was not handed here,
/// which is what keeps consent enforcement in the backend even though the
/// official SDK runs in the webview.
///
/// Called on startup by every window and periodically thereafter; the claim set
/// plus the on-disk day marker make the extra calls no-ops.
#[tauri::command]
pub fn analytics_plan(app: AppHandle, env: Option<WebviewEnv>) -> AnalyticsPlan {
    let env = store_env(env);
    let cfg = load_or_init(&app);
    let today = epoch_day();
    let due = due_events(
        TD_APP_ID,
        cfg.consent,
        cfg.first_run_sent_td,
        heartbeat_due(cfg.last_heartbeat_ymd, today),
    );

    let events = {
        let mut claimed = CLAIMED.lock().unwrap_or_else(|e| e.into_inner());
        claim(&mut claimed, due)
    };

    // Starting a session is a fact about this launch, not about delivery, so it
    // is recorded the moment the signal is handed out. Doing it here also means
    // the session itself is counted in the parameters it ships with.
    let cfg = if events.contains(&AnalyticsEvent::SessionStarted) {
        update_config(&app, |c| record_session_start(c, today))
    } else {
        cfg
    };

    if !events.is_empty() {
        log::debug!(target: "analytics", "plan: {:?}", events.iter().map(|e| e.name()).collect::<Vec<_>>());
    }

    let payload = default_payload(&cfg, &env, today);
    let signals = events
        .into_iter()
        .map(|e| {
            let mut p = payload.clone();
            if e == AnalyticsEvent::NewInstall {
                // The Acquisition dashboard reads the install date off this
                // signal's own parameters.
                p.insert(
                    "TelemetryDeck.Acquisition.firstSessionDate".to_string(),
                    iso_date(if cfg.first_session_ymd > 0 { cfg.first_session_ymd } else { today }),
                );
            }
            Signal {
                kind: e.name().to_string(),
                payload: p,
                marker: e.is_persisted().then_some(e),
            }
        })
        .collect();

    AnalyticsPlan::new(&cfg, signals)
}

/// Build the signal for something that just happened. Returns a plan with one
/// signal, or an empty one when the user opted out — the consent gate is here,
/// not in the webview ([S-21]).
#[tauri::command]
pub fn analytics_track(app: AppHandle, event: TrackedEvent) -> AnalyticsPlan {
    let cfg = load_or_init(&app);
    if !should_send(TD_APP_ID, cfg.consent) {
        return AnalyticsPlan::new(&cfg, Vec::new());
    }
    let today = epoch_day();
    let env = store_env(None);
    let mut payload = default_payload(&cfg, &env, today);

    let kind = match &event {
        TrackedEvent::Error { id, category, message } => {
            let Some(id) = clean(id, 64) else {
                log::warn!(target: "analytics", "error signal without an id, dropped");
                return AnalyticsPlan::new(&cfg, Vec::new());
            };
            payload.insert("TelemetryDeck.Error.id".to_string(), id);
            payload.insert(
                "TelemetryDeck.Error.category".to_string(),
                category.name().to_string(),
            );
            if let Some(m) = message.as_deref().and_then(clean_message) {
                payload.insert("TelemetryDeck.Error.message".to_string(), m);
            }
            "TelemetryDeck.Error.occurred"
        }
        TrackedEvent::Navigation { from, to } => {
            let from = clean(from, 64).unwrap_or_else(|| "unknown".to_string());
            let to = clean(to, 64).unwrap_or_else(|| "unknown".to_string());
            payload.insert("TelemetryDeck.Navigation.schemaVersion".to_string(), "1".to_string());
            payload.insert(
                "TelemetryDeck.Navigation.identifier".to_string(),
                format!("{from} -> {to}"),
            );
            payload.insert("TelemetryDeck.Navigation.sourcePath".to_string(), from);
            payload.insert("TelemetryDeck.Navigation.destinationPath".to_string(), to);
            "TelemetryDeck.Navigation.pathChanged"
        }
        TrackedEvent::Purchase { product_id, kind } => {
            let Some(product) = clean(product_id, 64) else {
                return AnalyticsPlan::new(&cfg, Vec::new());
            };
            payload.insert("TelemetryDeck.Purchase.productID".to_string(), product);
            payload.insert("TelemetryDeck.Purchase.type".to_string(), kind.name().to_string());
            "TelemetryDeck.Purchase.completed"
        }
    };

    log::debug!(target: "analytics", "track {kind}");
    AnalyticsPlan::new(
        &cfg,
        vec![Signal {
            kind: kind.to_string(),
            payload,
            marker: None,
        }],
    )
}

/// Record that a session ended and how long it ran, so the next launch's
/// signals carry an accurate average ([S-19]). Persisted only; sends nothing.
#[tauri::command]
pub fn analytics_session_end(app: AppHandle, seconds: u64) {
    // Retention bookkeeping is analytics state: without consent there is
    // nothing to keep it for, so nothing is written ([S-26]).
    if load_or_init(&app).consent != Consent::Granted {
        return;
    }
    update_config(&app, |c| record_session_end(c, seconds));
}

/// Record that the frontend got a success response from TelemetryDeck for one
/// event. Markers are persisted ONLY here, so a failed send is retried on the
/// next launch rather than silently dropped.
#[tauri::command]
pub fn analytics_mark_sent(app: AppHandle, event: AnalyticsEvent) {
    log::debug!(target: "analytics", "delivered {}", event.name());
    if !event.is_persisted() {
        return;
    }
    update_config(&app, |cfg| match event {
        AnalyticsEvent::NewInstall => cfg.first_run_sent_td = true,
        AnalyticsEvent::Heartbeat => cfg.last_heartbeat_ymd = epoch_day(),
        AnalyticsEvent::SessionStarted | AnalyticsEvent::OptOut => {}
    });
}

/// The user's current analytics decision. Settings renders `Granted` as the
/// "Share anonymous usage data" toggle being on; `Unset` reads as off, because
/// nothing is being shared until it is answered.
#[tauri::command]
pub fn analytics_consent(app: AppHandle) -> Consent {
    load_or_init(&app).consent
}

/// Ask whether THIS window should show the first-run consent prompt ([S-27]).
///
/// True for at most one window per process, and only while the decision is
/// still `Unset`. The claim is in-memory: if the window that took it is closed
/// without answering, the next launch asks again — an unanswered prompt must
/// never be mistaken for a "no".
#[tauri::command]
pub fn analytics_claim_consent_prompt(app: AppHandle) -> bool {
    if TD_APP_ID.is_empty() || load_or_init(&app).consent != Consent::Unset {
        return false;
    }
    let mut claimed = PROMPT_CLAIMED.lock().unwrap_or_else(|e| e.into_inner());
    if *claimed {
        return false;
    }
    *claimed = true;
    true
}

/// Record the user's answer — from the first-run prompt or the Settings
/// toggle, which are the same decision and so the same command.
///
/// Revoking a consent that was previously given returns a plan holding a single
/// `app_opt_out` signal: that event IS the user telling us they're leaving, so
/// it is deliberately not gated by the answer it announces ([S-07]). Every
/// other answer — including declining the first-run prompt — returns an empty
/// plan, so the webview has nothing to send.
#[tauri::command]
pub fn analytics_set_consent(app: AppHandle, granted: bool) -> AnalyticsPlan {
    let next = if granted { Consent::Granted } else { Consent::Denied };
    let mut transition = false;
    let cfg = update_config(&app, |cfg| {
        transition = is_revocation(cfg.consent, next);
        cfg.consent = next;
    });

    if !transition || TD_APP_ID.is_empty() {
        return AnalyticsPlan::new(&cfg, Vec::new());
    }
    let env = store_env(None);
    AnalyticsPlan::new(
        &cfg,
        vec![Signal {
            kind: AnalyticsEvent::OptOut.name().to_string(),
            payload: default_payload(&cfg, &env, epoch_day()),
            marker: None,
        }],
    )
}

/// Diagnostic: report what the backend would hand out right now, without
/// claiming anything, so wiring can be inspected from any build.
#[tauri::command]
pub fn diag_analytics_test(app: AppHandle) -> Result<String, String> {
    if TD_APP_ID.is_empty() {
        return Err("no TelemetryDeck app ID compiled in".to_string());
    }
    let cfg = load_or_init(&app);
    let today = epoch_day();
    let due = due_events(
        TD_APP_ID,
        cfg.consent,
        cfg.first_run_sent_td,
        heartbeat_due(cfg.last_heartbeat_ymd, today),
    );
    let claimed = CLAIMED.lock().unwrap_or_else(|e| e.into_inner());
    let env = store_env(None);
    let payload = default_payload(&cfg, &env, today);
    Ok(format!(
        "target={INGEST_URL} appID={TD_APP_ID} testMode={} consent={:?} due={:?} claimed={:?} payload={:#?}",
        cfg!(debug_assertions),
        cfg.consent,
        due.iter().map(|e| e.name()).collect::<Vec<_>>(),
        claimed.iter().map(|e| e.name()).collect::<Vec<_>>(),
        payload,
    ))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn cfg_with(f: impl FnOnce(&mut AnalyticsConfig)) -> AnalyticsConfig {
        let mut c = AnalyticsConfig::default();
        f(&mut c);
        c
    }

    // [S-01] absent/corrupt config → typed default, never a half-built object.
    #[test]
    fn s01_parse_corrupt_returns_default() {
        let d = parse_config("not json at all");
        assert_eq!(d.install_id, "");
        assert_eq!(d.consent, Consent::Unset);
        assert!(!d.first_run_sent_td);
        assert_eq!(d.schema_version, SCHEMA_VERSION);
    }

    // [S-01] a future/old schema version is treated as unreadable.
    #[test]
    fn s01_parse_wrong_schema_version_returns_default() {
        let json = r#"{"schemaVersion":999,"installId":"keep-me","consent":"granted"}"#;
        let d = parse_config(json);
        assert_eq!(d.install_id, ""); // discarded, not partially trusted
        assert_eq!(d.consent, Consent::Unset); // and never a granted default
    }

    // [S-02] a valid v1 config round-trips every field.
    #[test]
    fn s02_parse_valid_roundtrips() {
        let json = r#"{"schemaVersion":1,"installId":"abc-123","consent":"granted","firstRunSentTd":true,"lastHeartbeatYmd":20000}"#;
        let c = parse_config(json);
        assert_eq!(c.install_id, "abc-123");
        assert_eq!(c.consent, Consent::Granted);
        assert!(c.first_run_sent_td);
        assert_eq!(c.last_heartbeat_ymd, 20_000);
    }

    // [S-19] a pre-retention file still loads: the new keys default to empty
    // rather than discarding the install identity and the opt-out choice.
    #[test]
    fn s19_pre_retention_config_loads_with_empty_history() {
        let json = r#"{"schemaVersion":1,"installId":"abc-123","consent":"denied","firstRunSentTd":true}"#;
        let c = parse_config(json);
        assert_eq!(c.install_id, "abc-123");
        assert_eq!(c.consent, Consent::Denied);
        assert_eq!(c.first_session_ymd, 0);
        assert_eq!(c.total_sessions_count, 0);
        assert!(c.distinct_days_used.is_empty());
    }

    // [S-10] a GA-era config with the legacy `installReported: true` key does
    // NOT suppress the first-run signal. Historic reason: that key marked GA4's
    // `app_install` delivered, but GA silently dropped every one of those as a
    // reserved name, so the once-ever signal was still owed.
    #[test]
    fn s10_legacy_install_reported_does_not_suppress_first_run() {
        let json = r#"{"schemaVersion":1,"installId":"abc-123","optOut":false,"installReported":true}"#;
        let c = parse_config(json);
        assert!(!c.first_run_sent_td); // still owed → will send the install signal
        assert_eq!(c.install_id, "abc-123"); // identity preserved
        // [S-25] legacy `optOut: false` was the old default, never an answer.
        assert_eq!(c.consent, Consent::Unset);
    }

    // [S-11] provider migration: a GA-era config with `firstRunReported: true`
    // does NOT suppress the TelemetryDeck first-run signal. Those events went
    // to GA4, so TelemetryDeck is still owed one. Critically, the install id
    // and the opt-out CHOICE survive: re-keying delivery must never re-enable
    // analytics for someone who turned it off.
    #[test]
    fn s11_ga_era_first_run_reported_does_not_suppress_td_first_run() {
        let json = r#"{"schemaVersion":1,"installId":"abc-123","optOut":true,"firstRunReported":true}"#;
        let c = parse_config(json);
        assert!(!c.first_run_sent_td); // TelemetryDeck still owed the signal
        assert_eq!(c.install_id, "abc-123"); // identity preserved across providers
        assert_eq!(c.consent, Consent::Denied); // consent preserved: still a refusal
    }

    // [S-03] a fresh install has made no decision, and an undecided install
    // sends nothing. Analytics are OFF until the prompt is answered.
    #[test]
    fn s03_default_consent_is_unset_and_silent() {
        assert_eq!(AnalyticsConfig::default().consent, Consent::Unset);
        assert!(!should_send("A-1234", Consent::Unset));
        assert!(due_events("A-1234", Consent::Unset, false, true).is_empty());
    }

    // [S-25] migration from the pre-consent file. An explicit `optOut: true`
    // is an answer and survives as Denied; `optOut: false` was the old default
    // rather than a choice, so it becomes Unset and the user is asked once.
    #[test]
    fn s25_legacy_opt_out_migrates_to_consent() {
        let denied = parse_config(r#"{"schemaVersion":1,"installId":"a","optOut":true}"#);
        assert_eq!(denied.consent, Consent::Denied);

        let unset = parse_config(r#"{"schemaVersion":1,"installId":"a","optOut":false}"#);
        assert_eq!(unset.consent, Consent::Unset);
        assert_eq!(unset.install_id, "a"); // identity untouched by the migration
    }

    // [S-25] an existing answer always wins over the legacy flag, and the
    // legacy key is dropped from the file on the next save rather than kept
    // alive as a second source of truth.
    #[test]
    fn s25_consent_beats_legacy_flag_and_legacy_key_is_not_rewritten() {
        let c = parse_config(r#"{"schemaVersion":1,"consent":"granted","optOut":true}"#);
        assert_eq!(c.consent, Consent::Granted);
        assert_eq!(c.opt_out, None);

        let json = serde_json::to_string(&c).expect("serialize");
        assert!(!json.contains("optOut"), "legacy key must not be written back: {json}");
        assert!(json.contains(r#""consent":"granted""#));
    }

    // [S-26] nothing identifying exists before consent: an unanswered config
    // carries no install id, so declining leaves no identifier behind at all.
    #[test]
    fn s26_no_install_id_without_consent() {
        let undecided = parse_config(r#"{"schemaVersion":1}"#);
        assert_eq!(undecided.install_id, "");
        assert_eq!(undecided.consent, Consent::Unset);
    }

    // [S-04] send gate: an app id must be configured AND the user must not
    // have opted out. An unconfigured build is a no-op, not a send to nowhere.
    #[test]
    fn s04_should_send_gate() {
        assert!(should_send("A-1234", Consent::Granted)); // configured + consent
        assert!(!should_send("A-1234", Consent::Denied)); // said no → silent
        assert!(!should_send("A-1234", Consent::Unset)); // not asked yet → silent
        assert!(!should_send("", Consent::Granted)); // no app id → silent
    }

    // [S-04] guard against shipping a build with analytics silently disabled.
    #[test]
    fn s04_app_id_is_compiled_in() {
        assert!(!TD_APP_ID.is_empty(), "TelemetryDeck app ID not configured");
    }

    // [S-04] an opted-out user is handed an EMPTY plan: the frontend is
    // physically unable to send, which is what keeps consent in the backend
    // even though the SDK runs in the webview.
    #[test]
    fn s04_opted_out_yields_no_events() {
        assert!(due_events("A-1234", Consent::Denied, false, true).is_empty());
        assert!(due_events("A-1234", Consent::Unset, false, true).is_empty());
        assert!(due_events("", Consent::Granted, false, true).is_empty());
    }

    // [S-08] heartbeat is due at most once per calendar day.
    #[test]
    fn s08_heartbeat_throttled_per_day() {
        assert!(heartbeat_due(0, 20_000)); // never sent → due
        assert!(heartbeat_due(19_999, 20_000)); // new day → due
        assert!(!heartbeat_due(20_000, 20_000)); // already sent today → not due
    }

    // [S-05] what is owed, given persisted state. A session start is owed on
    // every launch; only the per-process claim makes it once per launch.
    #[test]
    fn s05_due_events() {
        // fresh install, heartbeat due → install + session + heartbeat
        assert_eq!(
            due_events("A", Consent::Granted, false, true),
            vec![
                AnalyticsEvent::NewInstall,
                AnalyticsEvent::SessionStarted,
                AnalyticsEvent::Heartbeat
            ]
        );
        // install already delivered, heartbeat due → session + heartbeat
        assert_eq!(
            due_events("A", Consent::Granted, true, true),
            vec![AnalyticsEvent::SessionStarted, AnalyticsEvent::Heartbeat]
        );
        // everything else done today → still a session for this launch
        assert_eq!(
            due_events("A", Consent::Granted, true, false),
            vec![AnalyticsEvent::SessionStarted]
        );
    }

    // [S-14] Themia opens one webview per monitor and each runs the same
    // startup code. The first window to ask claims the work; every other window
    // gets an empty list, so a 3-monitor setup sends one session signal, not
    // three.
    #[test]
    fn s14_claim_dedupes_across_windows() {
        let mut claimed = BTreeSet::new();
        let due = || {
            vec![
                AnalyticsEvent::NewInstall,
                AnalyticsEvent::SessionStarted,
                AnalyticsEvent::Heartbeat,
            ]
        };

        assert_eq!(claim(&mut claimed, due()).len(), 3);
        // monitors 2 and 3 ask for the same work
        assert!(claim(&mut claimed, due()).is_empty());
        assert!(claim(&mut claimed, due()).is_empty());
    }

    // [S-14] a claim is per-event: a later heartbeat is still handed out even
    // though the install event was claimed earlier in the same run.
    #[test]
    fn s14_claim_is_per_event() {
        let mut claimed = BTreeSet::new();
        assert_eq!(
            claim(&mut claimed, vec![AnalyticsEvent::NewInstall]),
            vec![AnalyticsEvent::NewInstall]
        );
        assert_eq!(
            claim(&mut claimed, vec![AnalyticsEvent::NewInstall, AnalyticsEvent::Heartbeat]),
            vec![AnalyticsEvent::Heartbeat]
        );
    }

    // [S-20] the lifecycle events cross IPC as TelemetryDeck's own signal
    // names, so its built-in Acquisition and Activation dashboards count them
    // without any custom insight. The old custom `app_first_run` name was
    // invisible to those dashboards.
    #[test]
    fn s20_event_wire_names() {
        let json = serde_json::to_string(&vec![
            AnalyticsEvent::NewInstall,
            AnalyticsEvent::SessionStarted,
            AnalyticsEvent::Heartbeat,
            AnalyticsEvent::OptOut,
        ])
        .unwrap_or_default();
        assert_eq!(
            json,
            r#"["TelemetryDeck.Acquisition.newInstallDetected","TelemetryDeck.Session.started","app_heartbeat","app_opt_out"]"#
        );

        assert_eq!(
            AnalyticsEvent::NewInstall.name(),
            "TelemetryDeck.Acquisition.newInstallDetected"
        );
        assert_eq!(AnalyticsEvent::SessionStarted.name(), "TelemetryDeck.Session.started");
        assert_eq!(AnalyticsEvent::Heartbeat.name(), "app_heartbeat");
        assert_eq!(AnalyticsEvent::OptOut.name(), "app_opt_out");
    }

    // [S-05] an unknown event name from the webview is REJECTED rather than
    // forwarded: the IPC boundary is a closed set, not a string passthrough.
    #[test]
    fn s05_unknown_event_is_rejected() {
        assert!(serde_json::from_str::<AnalyticsEvent>(r#""app_heartbeat""#).is_ok());
        assert!(serde_json::from_str::<AnalyticsEvent>(r#""something_else""#).is_err());
        assert!(serde_json::from_str::<AnalyticsEvent>(r#""app_first_run""#).is_err());
    }

    // [S-21] tracked events are a closed set with typed fields too: an unknown
    // kind, an unknown category or a smuggled extra parameter is rejected at
    // the boundary instead of reaching TelemetryDeck.
    #[test]
    fn s21_tracked_events_are_a_closed_set() {
        assert!(serde_json::from_str::<TrackedEvent>(
            r#"{"event":"error","id":"email.authFailed","category":"thrown-exception"}"#
        )
        .is_ok());
        assert!(serde_json::from_str::<TrackedEvent>(
            r#"{"event":"navigation","from":"grid","to":"settings"}"#
        )
        .is_ok());
        assert!(serde_json::from_str::<TrackedEvent>(
            r#"{"event":"purchase","productId":"pro","kind2":"x"}"#
        )
        .is_err());
        // unknown category
        assert!(serde_json::from_str::<TrackedEvent>(
            r#"{"event":"error","id":"x","category":"whatever"}"#
        )
        .is_err());
        // unknown event kind
        assert!(serde_json::from_str::<TrackedEvent>(r#"{"event":"crash","id":"x"}"#).is_err());
        // extra parameter smuggled alongside a known event
        assert!(serde_json::from_str::<TrackedEvent>(
            r#"{"event":"navigation","from":"a","to":"b","email":"me@example.com"}"#
        )
        .is_err());
    }

    // [S-22] an error message may only be a short static description. Anything
    // shaped like user data — a path, a URL, an address — is dropped, because
    // that is exactly what TelemetryDeck warns against sending.
    #[test]
    fn s22_error_message_rejects_user_data_shapes() {
        assert_eq!(clean_message("IMAP login rejected"), Some("IMAP login rejected".to_string()));
        assert_eq!(clean_message("  spaced  "), Some("spaced".to_string()));
        assert_eq!(clean_message(""), None);
        assert_eq!(clean_message("failed for me@example.com"), None);
        assert_eq!(clean_message(r"cannot read C:\Users\Someone\notes.txt"), None);
        assert_eq!(clean_message("GET https://mail.example.com/x failed"), None);
        assert_eq!(clean_message("read /home/user/f"), None);
        // absurd length is capped, not rejected
        let long = "e".repeat(500);
        assert_eq!(clean_message(&long).map(|m| m.len()), Some(100));
    }

    // [S-18] every string from the webview is trimmed, stripped of control
    // characters and capped before it can reach the wire.
    #[test]
    fn s18_env_strings_are_cleaned() {
        assert_eq!(clean(" de-CH ", 32), Some("de-CH".to_string()));
        assert_eq!(clean("a\u{0}b", 32), Some("ab".to_string()));
        assert_eq!(clean("   ", 32), None);
        assert_eq!(clean(&"x".repeat(99), 16).map(|s| s.len()), Some(16));
    }

    // [S-18] the webview env is a closed shape: an unexpected field is a hard
    // deserialize error, not a silently forwarded parameter.
    #[test]
    fn s18_env_rejects_unknown_fields() {
        assert!(serde_json::from_str::<WebviewEnv>(r#"{"locale":"de-CH"}"#).is_ok());
        assert!(serde_json::from_str::<WebviewEnv>(r#"{}"#).is_ok());
        assert!(serde_json::from_str::<WebviewEnv>(r#"{"userEmail":"me@example.com"}"#).is_err());
    }

    // [S-09] clientUser is the SHA-256 of the install UUID: deterministic for a
    // given install, and distinct across installs.
    #[test]
    fn s09_client_user_is_sha256_of_install_id() {
        // NIST vector pins both the algorithm and the lowercase-hex encoding.
        assert_eq!(
            client_user("abc"),
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        );

        let id = "3f2a1e5c-0000-4000-8000-abcdefabcdef";
        let hashed = client_user(id);
        assert_ne!(hashed, id, "raw install UUID must never be sent");
        assert_eq!(hashed.len(), 64);
        assert_eq!(hashed, client_user(id), "must be stable across launches");
        assert_ne!(hashed, client_user("some-other-install"));
    }

    // [S-05]/[S-06]/[S-09] the plan hands over the SDK config and never the raw
    // install UUID.
    #[test]
    fn s05_plan_shape() {
        let raw = "3f2a1e5c-0000-4000-8000-abcdefabcdef";
        let cfg = cfg_with(|c| c.install_id = raw.to_string());
        let plan = AnalyticsPlan::new(
            &cfg,
            vec![Signal {
                kind: AnalyticsEvent::Heartbeat.name().to_string(),
                payload: default_payload(&cfg, &WebviewEnv::default(), 20_000),
                marker: Some(AnalyticsEvent::Heartbeat),
            }],
        );
        let v = serde_json::to_value(&plan).unwrap_or(serde_json::Value::Null);

        assert_eq!(v["appId"], TD_APP_ID);
        assert_eq!(v["target"], INGEST_URL);
        assert_eq!(v["signals"][0]["type"], "app_heartbeat");
        assert_eq!(v["signals"][0]["marker"], "app_heartbeat");
        assert_eq!(v["testMode"], true); // tests compile with debug_assertions

        // [S-09] the raw UUID must never reach the webview
        let raw_json = v.to_string();
        assert!(!raw_json.contains(raw));
        assert_eq!(v["clientUser"].as_str().map(str::len), Some(64));
    }

    // [S-07] the opt-out signal fires once, and only when a consent that was
    // actually given is taken away. Declining the first-run prompt is not a
    // departure: nothing was ever sent for that install, and there is no
    // consent under which to announce one.
    #[test]
    fn s07_opt_out_fires_only_on_revocation() {
        assert!(is_revocation(Consent::Granted, Consent::Denied)); // revoked
        assert!(!is_revocation(Consent::Unset, Consent::Denied)); // declined prompt
        assert!(!is_revocation(Consent::Denied, Consent::Denied)); // already off
        assert!(!is_revocation(Consent::Denied, Consent::Granted)); // turning ON
        assert!(!is_revocation(Consent::Granted, Consent::Granted)); // unchanged
        assert!(!is_revocation(Consent::Unset, Consent::Granted)); // accepted
    }

    // [S-16] every signal carries the full TelemetryDeck default parameter set,
    // so the dashboards break installs and sessions down by app version,
    // platform, OS version and locale without custom configuration.
    #[test]
    fn s16_payload_carries_the_default_parameters() {
        let cfg = cfg_with(|c| {
            c.first_session_ymd = 19_000;
            c.total_sessions_count = 7;
            c.distinct_days_used = vec![19_000, 19_990, 20_000];
            c.ended_sessions_count = 2;
            c.total_session_seconds = 500;
            c.previous_session_seconds = 300;
        });
        let env = WebviewEnv {
            locale: Some("de-CH".into()),
            preferred_language: Some("de".into()),
            region: Some("CH".into()),
            app_language: Some("en".into()),
            time_zone: Some("Europe/Zurich".into()),
            color_scheme: Some("Dark".into()),
            layout_direction: Some("leftToRight".into()),
            screen_width: Some(2560.0),
            screen_height: Some(1440.0),
            screen_scale: Some(1.5),
        };
        let p = default_payload(&cfg, &env, 20_000);
        let get = |k: &str| p.get(k).map(String::as_str);

        assert_eq!(get("TelemetryDeck.AppInfo.version"), Some(env!("CARGO_PKG_VERSION")));
        assert_eq!(get("TelemetryDeck.Device.platform"), Some(platform_name()));
        assert_eq!(get("TelemetryDeck.Device.operatingSystem"), Some(platform_name()));
        assert_eq!(get("TelemetryDeck.Device.architecture"), Some(std::env::consts::ARCH));
        assert_eq!(get("TelemetryDeck.Device.timeZone"), Some("Europe/Zurich"));
        assert_eq!(get("TelemetryDeck.Device.screenResolutionWidth"), Some("2560"));
        assert_eq!(get("TelemetryDeck.Device.screenScaleFactor"), Some("1.5"));
        assert_eq!(get("TelemetryDeck.RunContext.locale"), Some("de-CH"));
        assert_eq!(get("TelemetryDeck.RunContext.language"), Some("en"));
        assert_eq!(get("TelemetryDeck.RunContext.targetEnvironment"), Some("native"));
        assert_eq!(get("TelemetryDeck.UserPreference.language"), Some("de"));
        assert_eq!(get("TelemetryDeck.UserPreference.region"), Some("CH"));
        assert_eq!(get("TelemetryDeck.UserPreference.colorScheme"), Some("Dark"));
        assert_eq!(get("TelemetryDeck.Acquisition.firstSessionDate"), Some("2022-01-08"));
        assert_eq!(get("TelemetryDeck.Retention.totalSessionsCount"), Some("7"));
        assert_eq!(get("TelemetryDeck.Retention.distinctDaysUsed"), Some("3"));
        assert_eq!(get("TelemetryDeck.Retention.distinctDaysUsedLastMonth"), Some("2"));
        assert_eq!(get("TelemetryDeck.Retention.averageSessionSeconds"), Some("250"));
        assert_eq!(get("TelemetryDeck.Retention.previousSessionSeconds"), Some("300"));

        // [S-17] the OS version is what the dashboard's System Version insight
        // groups by; on Windows all three granularities are sent.
        #[cfg(target_os = "windows")]
        {
            let sv = get("TelemetryDeck.Device.systemVersion").unwrap_or_default();
            assert!(sv.starts_with("1"), "unexpected Windows version {sv}");
            assert_eq!(sv.split('.').count(), 3);
            assert!(get("TelemetryDeck.Device.systemMajorVersion").is_some());
            assert!(get("TelemetryDeck.Device.systemMajorMinorVersion").is_some());
        }
    }

    // [S-16] a missing environment value drops its parameter instead of
    // sending an empty or invented one.
    #[test]
    fn s16_absent_env_values_are_omitted() {
        let p = default_payload(&AnalyticsConfig::default(), &WebviewEnv::default(), 20_000);
        assert!(!p.contains_key("TelemetryDeck.Device.timeZone"));
        assert!(!p.contains_key("TelemetryDeck.RunContext.locale"));
        assert!(!p.contains_key("TelemetryDeck.UserPreference.region"));
        // never sent on a fresh install: there is no first session yet
        assert!(!p.contains_key("TelemetryDeck.Acquisition.firstSessionDate"));
        // but the app version and platform are always there
        assert!(p.contains_key("TelemetryDeck.AppInfo.version"));
        assert!(p.contains_key("TelemetryDeck.Device.platform"));
    }

    // [S-17] Windows 11 reports kernel version 10.0; only the build number
    // separates it from Windows 10, so the major number is corrected.
    #[cfg(target_os = "windows")]
    #[test]
    fn s17_windows_11_is_not_filed_under_10() {
        let v = |m, mi, b| {
            version_params(m, mi, b)
                .into_iter()
                .collect::<BTreeMap<_, _>>()
        };

        let win11 = v(10, 0, 26_200);
        assert_eq!(win11["TelemetryDeck.Device.systemVersion"], "11.0.26200");
        assert_eq!(win11["TelemetryDeck.Device.systemMajorMinorVersion"], "11.0");
        assert_eq!(win11["TelemetryDeck.Device.systemMajorVersion"], "11");

        // 22000 is the first Windows 11 build; one below it is still Windows 10
        assert_eq!(v(10, 0, 22_000)["TelemetryDeck.Device.systemMajorVersion"], "11");
        assert_eq!(v(10, 0, 21_999)["TelemetryDeck.Device.systemMajorVersion"], "10");
        assert_eq!(v(10, 0, 19_045)["TelemetryDeck.Device.systemVersion"], "10.0.19045");
    }

    // [S-19] session bookkeeping: one session per launch, one distinct day per
    // calendar day, and the first session date pinned once.
    #[test]
    fn s19_session_start_bookkeeping() {
        let mut c = AnalyticsConfig::default();
        record_session_start(&mut c, 20_000);
        record_session_start(&mut c, 20_000); // relaunch, same day
        assert_eq!(c.total_sessions_count, 2);
        assert_eq!(c.distinct_days_used, vec![20_000]);
        assert_eq!(c.first_session_ymd, 20_000);

        record_session_start(&mut c, 20_001);
        assert_eq!(c.total_sessions_count, 3);
        assert_eq!(c.distinct_days_used, vec![20_000, 20_001]);
        assert_eq!(c.first_session_ymd, 20_000, "first session date never moves");
    }

    // [S-19] the day history is bounded, so a long-lived install cannot grow
    // analytics.json without limit.
    #[test]
    fn s19_distinct_days_are_capped() {
        let mut c = AnalyticsConfig::default();
        for day in 0..(MAX_DISTINCT_DAYS as u64 + 50) {
            record_session_start(&mut c, day);
        }
        assert_eq!(c.distinct_days_used.len(), MAX_DISTINCT_DAYS);
        // the tail is kept: the newest day survives, the oldest is dropped
        assert_eq!(
            c.distinct_days_used.last(),
            Some(&(MAX_DISTINCT_DAYS as u64 + 49))
        );
        assert_eq!(c.distinct_days_used.first(), Some(&50));
    }

    // [S-19] a suspended machine must not poison the average session length.
    #[test]
    fn s19_session_end_clamps_absurd_lengths() {
        let mut c = AnalyticsConfig::default();
        record_session_end(&mut c, 60);
        record_session_end(&mut c, 7 * 24 * 60 * 60); // a week "in session"
        assert_eq!(c.previous_session_seconds, 24 * 60 * 60);
        assert_eq!(c.ended_sessions_count, 2);
        assert_eq!(average_session_seconds(c.total_session_seconds, c.ended_sessions_count), 43_230);
    }

    // [S-19] averages and the 30-day window are pure and unit-tested.
    #[test]
    fn s19_derived_retention_numbers() {
        assert_eq!(average_session_seconds(0, 0), 0); // first ever session
        assert_eq!(average_session_seconds(300, 2), 150);

        // The window is the 30 days up to and including today, so 19_970 counts
        // and the day before it does not.
        let days = vec![19_000, 19_969, 19_970, 20_000];
        assert_eq!(distinct_days_last_month(&days, 20_000), 2);
        assert_eq!(distinct_days_last_month(&[], 20_000), 0);
    }

    // [S-19] the ISO date sent as firstSessionDate is a real calendar date.
    #[test]
    fn s19_iso_date_from_epoch_day() {
        assert_eq!(iso_date(0), "1970-01-01");
        assert_eq!(iso_date(19_000), "2022-01-08");
        assert_eq!(iso_date(20_000), "2024-10-04");
        assert_eq!(iso_date(19_722), "2023-12-31"); // year boundary
        assert_eq!(iso_date(19_723), "2024-01-01");
        assert_eq!(iso_date(18_321), "2020-02-29"); // leap day
    }

    // [S-05] ingest endpoint is TelemetryDeck's namespaced v2 API.
    #[test]
    fn s05_ingest_url() {
        assert_eq!(
            INGEST_URL,
            "https://nom.telemetrydeck.com/v2/namespace/com.nathaniel-walser/"
        );
        assert!(INGEST_URL.ends_with("/"), "trailing slash is required");
        assert!(!INGEST_URL.contains("google"));
    }
}
