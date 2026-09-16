// ---------------------------------------------------------------------------
// Local notes store
//
// Notes used to live as a string inside one widget's config bag, in one view's
// localStorage. That made a note a property OF a widget: deleting the widget
// deleted the note, two widgets could never show the same note, and the text
// was only ever as durable as a browser storage key.
//
// Here a note is a document the app owns:
//
//   <app_data_dir>/notes/index.json   { schema_version, notes: [ {id,title,updated_at} ] }
//   <app_data_dir>/notes/<id>.md      the markdown source, byte for byte
//
// The split is deliberate. Content is one plain `.md` per note - greppable,
// backup-able, and recoverable with a text editor if everything else burns -
// while the mutable metadata that a picker needs (title, ordering key) sits in
// one small index we can rewrite atomically. A corrupt index therefore costs
// titles, never text: `rebuild_index_from_disk` re-adopts every `<id>.md` it
// finds, so ids (and so every widget's link to its note) survive intact.
//
// Every write is temp-file + rename, and every read-modify-write of the index
// runs under `store_lock()`, so two windows editing two notes cannot lose each
// other's update.
// ---------------------------------------------------------------------------

use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::sync::Mutex;

/// Bumped when the on-disk index shape changes. A file carrying an unknown
/// version is treated as unreadable (rebuilt from disk), never as a
/// half-understood object.
const SCHEMA_VERSION: u32 = 1;

/// Title used for a note the index lost track of, and for a note created with
/// no title. Deliberately not localized: this is a disk-level fallback, and the
/// frontend relabels an empty title through i18n before display.
const UNTITLED: &str = "Untitled";

/// Hard cap on a title, so a pathological paste can't bloat the index.
const MAX_TITLE: usize = 200;

// -- Public types -----------------------------------------------------------

/// A note as the picker sees it. `updated_at` is unix seconds, used for
/// "recently changed" ordering in the UI; the canonical list order is by title.
#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
pub struct NoteMeta {
    pub id: String,
    pub title: String,
    pub updated_at: u64,
}

// -- On-disk index ----------------------------------------------------------

#[derive(Serialize, Deserialize, Default)]
struct NoteIndex {
    #[serde(default)]
    schema_version: u32,
    #[serde(default)]
    notes: Vec<NoteMeta>,
}

fn notes_dir(app: &tauri::AppHandle) -> PathBuf {
    use tauri::Manager;
    app.path()
        .app_data_dir()
        .unwrap_or_else(|_| PathBuf::from("."))
        .join("notes")
}

fn index_path(dir: &Path) -> PathBuf {
    dir.join("index.json")
}

fn note_path(dir: &Path, id: &str) -> PathBuf {
    dir.join(format!("{id}.md"))
}

/// Guard for any read-modify-write of the index. The index is a single file
/// holding *all* notes, so two writers that each do load, mutate-one, save
/// would lose each other's update. Held across the whole load/mutate/save; there
/// is no `.await` in the guarded region, so a std Mutex is correct. Recovers a
/// poisoned lock rather than panicking.
fn store_lock() -> std::sync::MutexGuard<'static, ()> {
    static STORE_LOCK: once_cell::sync::Lazy<Mutex<()>> =
        once_cell::sync::Lazy::new(|| Mutex::new(()));
    STORE_LOCK.lock().unwrap_or_else(|e| e.into_inner())
}

fn now_secs() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs())
        // ignore: a clock before 1970 only costs ordering, never data.
        .unwrap_or(0)
}

// -- Trust boundary: ids ----------------------------------------------------

/// A note id is a v4 UUID we minted. Validating the SHAPE (not just rejecting
/// `..`) is what keeps `note_path` from ever escaping the notes directory: an id
/// arriving over IPC is untrusted input that we are about to interpolate into a
/// file name.
fn is_valid_id(id: &str) -> bool {
    id.len() == 36
        && id
            .bytes()
            .all(|b| b.is_ascii_hexdigit() || b == b'-')
        && id.as_bytes()[8] == b'-'
        && id.as_bytes()[13] == b'-'
        && id.as_bytes()[18] == b'-'
        && id.as_bytes()[23] == b'-'
}

fn check_id(id: &str) -> Result<(), String> {
    if is_valid_id(id) {
        Ok(())
    } else {
        Err(format!("Invalid note id: {id}"))
    }
}

/// Collapse a user-supplied title to something the index can hold: trimmed,
/// newline-free (a pasted paragraph must not become a multi-line list row), and
/// length-capped. An empty result becomes `UNTITLED` rather than a blank row.
fn clean_title(title: &str) -> String {
    let flat: String = title
        .chars()
        .map(|c| if c.is_control() { ' ' } else { c })
        .collect();
    let trimmed = flat.trim();
    if trimmed.is_empty() {
        return UNTITLED.to_string();
    }
    trimmed.chars().take(MAX_TITLE).collect::<String>().trim().to_string()
}

// -- Atomic file IO ---------------------------------------------------------

/// Write `data` to `path` atomically: serialize to a unique sibling temp file,
/// then rename it over the target. A crash mid-write leaves either the old file
/// or the new one, never a truncated blob. The temp name carries pid + sequence
/// so two processes never clash on it.
fn write_atomic(path: &Path, data: &str) -> std::io::Result<()> {
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)?;
    }
    use std::sync::atomic::{AtomicU64, Ordering};
    static TMP_SEQ: AtomicU64 = AtomicU64::new(0);
    let seq = TMP_SEQ.fetch_add(1, Ordering::Relaxed);
    let tmp = path.with_extension(format!("tmp.{}.{}", std::process::id(), seq));
    std::fs::write(&tmp, data)?;
    // rename is atomic on the same volume (Windows MoveFileEx replaces existing).
    match std::fs::rename(&tmp, path) {
        Ok(()) => Ok(()),
        Err(e) => {
            let _ = std::fs::remove_file(&tmp); // don't leak the temp on failure
            Err(e)
        }
    }
}

// -- Index load / save ------------------------------------------------------

/// Read the index, distinguishing the three cases that matter:
///
///  - **absent** (no file yet, or an empty notes dir): empty list, silent.
///    This is a first run, not a fault.
///  - **corrupt / unknown schema**: rebuild from the `.md` files on disk and
///    log loudly. Titles are lost, ids and text are not.
///  - **valid**: used as-is, minus entries whose `.md` has gone missing.
fn load_index(dir: &Path) -> NoteIndex {
    let path = index_path(dir);
    let raw = match std::fs::read_to_string(&path) {
        Ok(s) => s,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => {
            // Absent: either a first run, or an index that was deleted while
            // notes remain. Rebuilding covers both and is a no-op for the first.
            return rebuild_index_from_disk(dir, false);
        }
        Err(e) => {
            log::error!("notes: index unreadable ({e}); rebuilding from disk");
            return rebuild_index_from_disk(dir, true);
        }
    };
    match serde_json::from_str::<NoteIndex>(&raw) {
        Ok(idx) if idx.schema_version == SCHEMA_VERSION => prune_missing(dir, idx),
        Ok(idx) => {
            log::error!(
                "notes: index schema_version {} is not {SCHEMA_VERSION}; rebuilding from disk",
                idx.schema_version
            );
            rebuild_index_from_disk(dir, true)
        }
        Err(e) => {
            log::error!("notes: index is corrupt ({e}); rebuilding from disk");
            rebuild_index_from_disk(dir, true)
        }
    }
}

/// Drop index entries whose content file has gone (deleted outside the app).
/// The index is metadata about files; a row with no file is a dangling pointer
/// that would render as an empty note the user cannot fix.
fn prune_missing(dir: &Path, mut idx: NoteIndex) -> NoteIndex {
    idx.notes.retain(|n| {
        let keep = is_valid_id(&n.id) && note_path(dir, &n.id).is_file();
        if !keep {
            log::warn!("notes: dropping index entry with no content file: {}", n.id);
        }
        keep
    });
    idx
}

/// Adopt every `<id>.md` in the directory. Used when the index is missing or
/// unusable: the text is the irreplaceable part, so it is always recovered, and
/// only the title falls back to `UNTITLED`. `loud` distinguishes a genuine
/// recovery from the silent first-run case.
fn rebuild_index_from_disk(dir: &Path, loud: bool) -> NoteIndex {
    let entries = match std::fs::read_dir(dir) {
        Ok(e) => e,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => {
            return NoteIndex { schema_version: SCHEMA_VERSION, notes: Vec::new() };
        }
        Err(e) => {
            log::error!("notes: cannot read {}: {e}", dir.display());
            return NoteIndex { schema_version: SCHEMA_VERSION, notes: Vec::new() };
        }
    };
    let mut notes = Vec::new();
    for entry in entries.flatten() {
        let path = entry.path();
        if path.extension().and_then(|e| e.to_str()) != Some("md") {
            continue;
        }
        let Some(stem) = path.file_stem().and_then(|s| s.to_str()) else { continue };
        if !is_valid_id(stem) {
            continue;
        }
        let updated_at = entry
            .metadata()
            .ok()
            .and_then(|m| m.modified().ok())
            .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
            .map(|d| d.as_secs())
            .unwrap_or_else(now_secs);
        notes.push(NoteMeta { id: stem.to_string(), title: UNTITLED.to_string(), updated_at });
    }
    if loud && !notes.is_empty() {
        log::warn!("notes: recovered {} note(s) from disk; titles reset", notes.len());
    }
    NoteIndex { schema_version: SCHEMA_VERSION, notes }
}

fn save_index(dir: &Path, idx: &NoteIndex) -> Result<(), String> {
    let json = serde_json::to_string(idx).map_err(|e| format!("notes: serialize index: {e}"))?;
    write_atomic(&index_path(dir), &json).map_err(|e| format!("notes: write index: {e}"))
}

/// Canonical list order: title (case-insensitive), tie-broken by id so the order
/// is total and stable. A picker that reordered itself under the user's cursor
/// while they typed would be unusable, which rules out ordering by `updated_at`.
fn sort_notes(notes: &mut [NoteMeta]) {
    notes.sort_by(|a, b| {
        a.title
            .to_lowercase()
            .cmp(&b.title.to_lowercase())
            .then_with(|| a.id.cmp(&b.id))
    });
}

// -- Commands ---------------------------------------------------------------

/// Every note, ordered for display. Never fails: an unreadable store yields an
/// empty list plus a log line, because a picker that errors is a dead end while
/// an empty picker still offers "New note".
pub fn list_notes(app: &tauri::AppHandle) -> Vec<NoteMeta> {
    let dir = notes_dir(app);
    let _w = store_lock();
    let mut idx = load_index(&dir);
    sort_notes(&mut idx.notes);
    idx.notes
}

/// The markdown source of one note. A missing note is an error rather than an
/// empty string: the caller must be able to tell "this note is empty" from
/// "this note is gone", or it would happily persist emptiness over the latter.
pub fn read_note(app: &tauri::AppHandle, id: &str) -> Result<String, String> {
    check_id(id)?;
    let dir = notes_dir(app);
    match std::fs::read_to_string(note_path(&dir, id)) {
        Ok(s) => Ok(s),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Err(format!("Note not found: {id}")),
        Err(e) => Err(format!("notes: read {id}: {e}")),
    }
}

/// Replace a note's content and stamp `updated_at`. Returns the new stamp so the
/// caller can echo-suppress its own write.
///
/// Refuses to write a note that is not in the index: an id that survived in a
/// widget's config after the note was deleted would otherwise silently
/// resurrect it as an untitled orphan.
pub fn write_note(app: &tauri::AppHandle, id: &str, content: &str) -> Result<u64, String> {
    check_id(id)?;
    let dir = notes_dir(app);
    let _w = store_lock();
    let mut idx = load_index(&dir);
    let Some(meta) = idx.notes.iter_mut().find(|n| n.id == id) else {
        return Err(format!("Note not found: {id}"));
    };
    // Content first: if the index write fails we have the user's text on disk
    // and a stale timestamp, which the next load recovers from. The reverse
    // order would advertise a save that never landed.
    write_atomic(&note_path(&dir, id), content).map_err(|e| format!("notes: write {id}: {e}"))?;
    let stamp = now_secs();
    meta.updated_at = stamp;
    save_index(&dir, &idx)?;
    Ok(stamp)
}

/// Create a note and return its metadata. `content` seeds the body, which is how
/// a widget's legacy inline text is migrated into the store without a round trip.
pub fn create_note(
    app: &tauri::AppHandle,
    title: &str,
    content: &str,
) -> Result<NoteMeta, String> {
    let dir = notes_dir(app);
    let id = uuid::Uuid::new_v4().to_string();
    let meta = NoteMeta { id: id.clone(), title: clean_title(title), updated_at: now_secs() };
    let _w = store_lock();
    let mut idx = load_index(&dir);
    write_atomic(&note_path(&dir, &id), content).map_err(|e| format!("notes: create {id}: {e}"))?;
    idx.notes.push(meta.clone());
    save_index(&dir, &idx)?;
    Ok(meta)
}

pub fn rename_note(app: &tauri::AppHandle, id: &str, title: &str) -> Result<(), String> {
    check_id(id)?;
    let dir = notes_dir(app);
    let _w = store_lock();
    let mut idx = load_index(&dir);
    let Some(meta) = idx.notes.iter_mut().find(|n| n.id == id) else {
        return Err(format!("Note not found: {id}"));
    };
    meta.title = clean_title(title);
    save_index(&dir, &idx)
}

/// Delete a note's index entry and its file. The index entry goes first: a
/// leftover `.md` with no index row is invisible and harmless (and would be
/// re-adopted only by an explicit recovery), whereas a leftover index row with
/// no file is a dangling pointer the picker would show.
pub fn delete_note(app: &tauri::AppHandle, id: &str) -> Result<(), String> {
    check_id(id)?;
    let dir = notes_dir(app);
    let _w = store_lock();
    let mut idx = load_index(&dir);
    let before = idx.notes.len();
    idx.notes.retain(|n| n.id != id);
    if idx.notes.len() == before {
        return Err(format!("Note not found: {id}"));
    }
    save_index(&dir, &idx)?;
    match std::fs::remove_file(note_path(&dir, id)) {
        Ok(()) => Ok(()),
        // Already gone: the outcome the caller asked for.
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(e) => Err(format!("notes: delete {id}: {e}")),
    }
}

// -- Tests ------------------------------------------------------------------
//
// The pure, path-taking half is what carries the rules worth pinning (id
// validation, title cleaning, index recovery, atomicity). Anything needing a
// real `AppHandle` is left to the e2e pass.

#[cfg(test)]
mod tests {
    use super::*;

    fn tmpdir(tag: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("themia-notes-test-{tag}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("test temp dir");
        dir
    }

    #[test]
    fn s31_rejects_ids_that_could_escape_the_notes_dir() {
        assert!(!is_valid_id("../../secrets"));
        assert!(!is_valid_id(".."));
        assert!(!is_valid_id(""));
        assert!(!is_valid_id("a/b"));
        // Right length, wrong alphabet.
        assert!(!is_valid_id("zzzzzzzz-zzzz-zzzz-zzzz-zzzzzzzzzzzz"));
        assert!(is_valid_id(&uuid::Uuid::new_v4().to_string()));
    }

    #[test]
    fn s35_clean_title_flattens_and_caps() {
        assert_eq!(clean_title("  hello  "), "hello");
        assert_eq!(clean_title(""), UNTITLED);
        assert_eq!(clean_title("   "), UNTITLED);
        assert_eq!(clean_title("one\ntwo"), "one two");
        assert_eq!(clean_title(&"x".repeat(500)).chars().count(), MAX_TITLE);
    }

    #[test]
    fn s33_missing_index_is_a_silent_empty_list_not_an_error() {
        let dir = tmpdir("absent");
        let idx = load_index(&dir);
        assert!(idx.notes.is_empty());
        assert_eq!(idx.schema_version, SCHEMA_VERSION);
    }

    #[test]
    fn s33_corrupt_index_recovers_every_note_body_and_keeps_ids() {
        let dir = tmpdir("corrupt");
        let id = uuid::Uuid::new_v4().to_string();
        std::fs::write(note_path(&dir, &id), "# kept").expect("seed note");
        std::fs::write(index_path(&dir), "{ not json at all").expect("seed index");

        let idx = load_index(&dir);
        assert_eq!(idx.notes.len(), 1, "the body must be recovered, not dropped");
        assert_eq!(idx.notes[0].id, id, "ids survive so widget links survive");
        assert_eq!(idx.notes[0].title, UNTITLED);
        assert_eq!(
            std::fs::read_to_string(note_path(&dir, &id)).expect("body"),
            "# kept",
            "recovery must never rewrite the text"
        );
    }

    #[test]
    fn s33_unknown_schema_version_rebuilds_rather_than_half_reading() {
        let dir = tmpdir("schema");
        let id = uuid::Uuid::new_v4().to_string();
        std::fs::write(note_path(&dir, &id), "body").expect("seed note");
        std::fs::write(
            index_path(&dir),
            format!(r#"{{"schema_version":99,"notes":[{{"id":"{id}","title":"Real","updated_at":5}}]}}"#),
        )
        .expect("seed index");

        let idx = load_index(&dir);
        assert_eq!(idx.notes.len(), 1);
        assert_eq!(idx.notes[0].title, UNTITLED, "a future shape is not trusted for titles");
    }

    #[test]
    fn s33_index_rows_without_a_body_file_are_pruned() {
        let dir = tmpdir("dangling");
        let alive = uuid::Uuid::new_v4().to_string();
        let dead = uuid::Uuid::new_v4().to_string();
        std::fs::write(note_path(&dir, &alive), "here").expect("seed note");
        let idx = NoteIndex {
            schema_version: SCHEMA_VERSION,
            notes: vec![
                NoteMeta { id: alive.clone(), title: "Alive".into(), updated_at: 1 },
                NoteMeta { id: dead, title: "Dead".into(), updated_at: 2 },
            ],
        };
        save_index(&dir, &idx).expect("save");

        let loaded = load_index(&dir);
        assert_eq!(loaded.notes.len(), 1);
        assert_eq!(loaded.notes[0].id, alive);
    }

    #[test]
    fn s33_a_valid_index_round_trips_untouched() {
        let dir = tmpdir("roundtrip");
        let id = uuid::Uuid::new_v4().to_string();
        std::fs::write(note_path(&dir, &id), "body").expect("seed note");
        save_index(
            &dir,
            &NoteIndex {
                schema_version: SCHEMA_VERSION,
                notes: vec![NoteMeta { id: id.clone(), title: "Shopping".into(), updated_at: 42 }],
            },
        )
        .expect("save");

        let loaded = load_index(&dir);
        assert_eq!(loaded.notes, vec![NoteMeta { id, title: "Shopping".into(), updated_at: 42 }]);
    }

    #[test]
    fn s30_write_atomic_leaves_no_temp_files_behind() {
        let dir = tmpdir("atomic");
        let target = dir.join("x.md");
        write_atomic(&target, "one").expect("first write");
        write_atomic(&target, "two").expect("overwrite");
        assert_eq!(std::fs::read_to_string(&target).expect("read"), "two");
        let strays: Vec<_> = std::fs::read_dir(&dir)
            .expect("read dir")
            .flatten()
            .map(|e| e.file_name().to_string_lossy().to_string())
            .filter(|n| n.contains("tmp."))
            .collect();
        assert!(strays.is_empty(), "temp files leaked: {strays:?}");
    }

    #[test]
    fn s35_sort_is_case_insensitive_and_total() {
        let mut notes = vec![
            NoteMeta { id: "b".into(), title: "beta".into(), updated_at: 0 },
            NoteMeta { id: "a".into(), title: "Alpha".into(), updated_at: 0 },
            NoteMeta { id: "c".into(), title: "Alpha".into(), updated_at: 0 },
        ];
        sort_notes(&mut notes);
        assert_eq!(
            notes.iter().map(|n| n.id.as_str()).collect::<Vec<_>>(),
            vec!["a", "c", "b"]
        );
    }
}
