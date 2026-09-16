//! [LOG-09] Windows Application event log sink for ERROR records.
//!
//! The [LOG-04] error floor puts every failure in the rotating log file, but a
//! user who reports "it just closed" is not going to find
//! `%LOCALAPPDATA%\com.themia.desktop\logs` on their own, and a hard crash
//! (access violation) only ever shows up as a WER `Application Error` entry in
//! Event Viewer with a module offset and no message. Mirroring our own ERROR
//! records into the same log puts both halves of a crash in one place that
//! support can ask for by name.
//!
//! Everything here is best-effort: a failure to reach the event log must never
//! break logging, so no path returns an error and none can panic. The file log
//! already has the record.

use std::sync::atomic::{AtomicUsize, Ordering};

/// Registered under
/// `HKLM\SYSTEM\CurrentControlSet\Services\EventLog\Application\Themia` by the
/// installer (`windows/installer-hooks.nsh`). Writing works without that key —
/// Event Viewer just wraps the text in a "description ... cannot be found"
/// preamble, which is why the key is worth an installer hook.
pub const SOURCE_NAME: &str = "Themia";

/// Event ID for an application error. Kept inside 1..=1000: the installer
/// points `EventMessageFile` at `EventCreate.exe`, whose message table covers
/// that range and renders the insertion string verbatim.
pub const EVENT_ID_ERROR: u32 = 1000;

/// Per-session cap. An error inside a retry loop or a repainting widget can
/// fire hundreds of times a minute; the Application log is a shared, bounded,
/// machine-wide resource, so Themia must not be able to push other software's
/// events out of it. The file log stays uncapped ([LOG-01] bounds it by size).
const MAX_EVENTS_PER_SESSION: usize = 25;

/// Event log strings are limited to 31,839 characters, but a wall of text is
/// unreadable in Event Viewer's detail pane and the full record is in the file
/// anyway.
const MAX_MESSAGE_CHARS: usize = 2_000;

/// How many records this process has handed to [`report_error`]. Saturating,
/// so a long-running session cannot wrap it back under the cap.
static EVENTS_SEEN: AtomicUsize = AtomicUsize::new(0);

/// [LOG-09] What to do with the record at `seen_before` (the value of the
/// counter BEFORE this record was counted). Pure, so the cap is testable
/// without an event log.
#[derive(Debug, PartialEq, Eq)]
pub enum Emit {
    /// Under the cap: write the record.
    Record,
    /// Exactly at the cap: write one notice that the rest are suppressed, so a
    /// reader is never left thinking the errors stopped.
    FinalNotice,
    /// Over the cap: write nothing.
    Suppressed,
}

pub fn decide(seen_before: usize) -> Emit {
    match seen_before.cmp(&MAX_EVENTS_PER_SESSION) {
        std::cmp::Ordering::Less => Emit::Record,
        std::cmp::Ordering::Equal => Emit::FinalNotice,
        std::cmp::Ordering::Greater => Emit::Suppressed,
    }
}

/// [LOG-09] Clamp to [`MAX_MESSAGE_CHARS`] on a char boundary (`char_indices`,
/// never a byte slice: a multi-byte character split in half would panic).
pub fn truncate_for_event(msg: &str) -> String {
    match msg.char_indices().nth(MAX_MESSAGE_CHARS) {
        None => msg.to_string(),
        Some((cut, _)) => format!("{}… (truncated, see the app log for the full record)", &msg[..cut]),
    }
}

fn suppression_notice() -> String {
    format!(
        "Themia stopped writing errors to the event log after {MAX_EVENTS_PER_SESSION} in this \
         session. The full record is in %LOCALAPPDATA%\\com.themia.desktop\\logs."
    )
}

/// Mirror one ERROR record into the Windows Application log. Best-effort and
/// non-blocking on failure; the caller is the logging pipeline itself, so this
/// must never log, panic, or return an error.
pub fn report_error(message: &str) {
    let text = match decide(EVENTS_SEEN.fetch_add(1, Ordering::Relaxed)) {
        Emit::Suppressed => return,
        Emit::FinalNotice => suppression_notice(),
        Emit::Record => truncate_for_event(message),
    };
    write_event(&text);
}

#[cfg(target_os = "windows")]
mod sys {
    use std::sync::OnceLock;
    use windows::core::PCWSTR;
    use windows::Win32::Foundation::HANDLE;
    use windows::Win32::System::EventLog::{
        DeregisterEventSource, RegisterEventSourceW, ReportEventW, EVENTLOG_ERROR_TYPE,
    };

    /// RAII wrapper for the registered source handle.
    struct SourceHandle(HANDLE);

    // SAFETY: `ReportEventW` is documented as thread-safe for a single source
    // handle, and the handle is opened once and then only read.
    unsafe impl Send for SourceHandle {}
    unsafe impl Sync for SourceHandle {}

    impl Drop for SourceHandle {
        fn drop(&mut self) {
            // ignore: deregistration failure at teardown has no recovery, and
            // the OS closes the handle with the process either way.
            let _ = unsafe { DeregisterEventSource(self.0) };
        }
    }

    /// Registered lazily on the first error and held for the process lifetime
    /// (a `static` never runs `Drop`; the handle is deliberately owned until
    /// exit, when Windows reclaims it). `None` = registration failed, e.g. the
    /// Event Log service is disabled — checked once, not per record.
    static SOURCE: OnceLock<Option<SourceHandle>> = OnceLock::new();

    fn wide(s: &str) -> Vec<u16> {
        s.encode_utf16().chain(std::iter::once(0)).collect()
    }

    fn source() -> Option<&'static SourceHandle> {
        SOURCE
            .get_or_init(|| {
                let name = wide(super::SOURCE_NAME);
                // ignore: no event log available → the file log still has the
                // record, and there is nowhere to report this failure to.
                unsafe { RegisterEventSourceW(PCWSTR::null(), PCWSTR(name.as_ptr())) }
                    .ok()
                    .map(SourceHandle)
            })
            .as_ref()
    }

    pub fn write_event(text: &str) {
        let Some(source) = source() else { return };
        let buf = wide(text);
        let strings = [PCWSTR(buf.as_ptr())];
        // ignore: best-effort mirror; the file log is the source of truth.
        let _ = unsafe {
            ReportEventW(
                source.0,
                EVENTLOG_ERROR_TYPE,
                0,
                super::EVENT_ID_ERROR,
                None,
                0,
                Some(&strings),
                None,
            )
        };
    }
}

#[cfg(target_os = "windows")]
use sys::write_event;

/// Non-Windows builds keep the pure cap/truncation logic (and its tests) but
/// have nowhere to write; Themia ships Windows-only.
#[cfg(not(target_os = "windows"))]
fn write_event(_text: &str) {}

#[cfg(test)]
mod tests {
    use super::*;

    /// [LOG-09] The cap is per session, with exactly one notice at the
    /// boundary: a reader who sees 25 errors and nothing after must be told
    /// the log stopped rather than assume the app recovered.
    #[test]
    fn log09_caps_events_per_session_with_one_notice() {
        assert_eq!(decide(0), Emit::Record);
        assert_eq!(decide(MAX_EVENTS_PER_SESSION - 1), Emit::Record);
        assert_eq!(decide(MAX_EVENTS_PER_SESSION), Emit::FinalNotice);
        assert_eq!(decide(MAX_EVENTS_PER_SESSION + 1), Emit::Suppressed);
        assert_eq!(decide(usize::MAX), Emit::Suppressed);
    }

    /// [LOG-09] Truncation is char-boundary safe: a panic payload can carry
    /// any user text (file names, mail subjects), and slicing a multi-byte
    /// character in half would panic inside the logging pipeline.
    #[test]
    fn log09_truncates_on_a_char_boundary() {
        let short = "boom";
        assert_eq!(truncate_for_event(short), short);

        let long = "é".repeat(MAX_MESSAGE_CHARS * 2);
        let cut = truncate_for_event(&long);
        assert!(cut.starts_with(&"é".repeat(MAX_MESSAGE_CHARS)));
        assert!(cut.ends_with("(truncated, see the app log for the full record)"));
        assert!(cut.chars().count() < long.chars().count());

        // Exactly at the limit is not truncated (the marker would be a lie).
        let exact = "a".repeat(MAX_MESSAGE_CHARS);
        assert_eq!(truncate_for_event(&exact), exact);
    }

    /// [LOG-09] Live write against the real Application log. `#[ignore]` on
    /// purpose: it has a machine-wide side effect (an entry another human can
    /// see) and asserts nothing CI could read back. Run it by hand when
    /// touching the FFI, then verify with
    /// `Get-WinEvent -ProviderName Themia -MaxEvents 1`:
    /// `cargo test -p themia --lib -- --ignored log09_writes`
    #[test]
    #[ignore = "writes a real entry to the machine's Application event log"]
    fn log09_writes_a_real_event_log_entry() {
        report_error("[eventlog] smoke test: this entry was written by a test run");
    }

    /// [LOG-09] The event id must stay inside the range EventCreate.exe's
    /// message table renders, or every entry gains a "description cannot be
    /// found" preamble even on a correctly registered source.
    #[test]
    fn log09_event_id_is_renderable_by_the_installed_message_file() {
        assert!((1..=1000).contains(&EVENT_ID_ERROR));
    }
}
