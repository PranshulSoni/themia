// Shared on-disk store: versioned JSON in app_data_dir, atomic single-writer
// saves, and an explicit absent/unreadable/corrupt/loaded split at the trust
// boundary.
//
// Extracted from widgets/system/screentime (the only store that had all three
// properties). analytics.rs and the local widget stores (todo/contacts/calendar)
// build on this instead of hand-rolling a fourth and fifth copy.
//
// The on-disk shape is the caller's own struct with a `schema_version` field —
// NOT a wrapper envelope. Existing stores are already written that way and
// re-shaping them would orphan every user's file.

use serde::de::DeserializeOwned;
use serde::Serialize;
use std::path::Path;

/// A struct that is persisted as versioned JSON. `VERSION` is the version this
/// build writes; `version()` reports what was actually read off disk, so a
/// mismatch is detectable before the value is trusted.
pub trait VersionedStore: Serialize + DeserializeOwned {
    const VERSION: u32;
    fn version(&self) -> u32;
}

/// Outcome of a load. The four cases are deliberately distinct: "absent" is a
/// first run and is silent, "unreadable" is a transient I/O fault that must NOT
/// destroy the file, and "corrupt" is unrecoverable content that gets preserved
/// aside. Collapsing any pair of these loses user data.
#[derive(Debug)]
pub enum Load<T> {
    /// File does not exist. First run — callers use their typed default.
    Absent,
    /// File exists but could not be read (permissions, sharing violation, …).
    /// Logged. The file is left untouched so a later retry can still read it.
    Unreadable,
    /// File was read but is not valid JSON, or its `schema_version` does not
    /// match `T::VERSION`. Logged, and the file has been renamed aside to
    /// `<name>.corrupt` (best effort) so the next save cannot destroy it.
    Corrupt,
    Loaded(T),
}

/// Read `path` as versioned JSON. Never panics; never silently returns a
/// half-built value. `target` is the log target (e.g. "screentime").
pub fn load_versioned<T: VersionedStore>(path: &Path, target: &str) -> Load<T> {
    let raw = match std::fs::read_to_string(path) {
        Ok(raw) => raw,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Load::Absent,
        Err(e) => {
            log::warn!(target: target, "read {path:?} failed: {e}; starting empty");
            return Load::Unreadable;
        }
    };
    // Turbofish, not a `let` annotation: the closure below is type-checked before
    // the binding's type flows back, so without it inference fails (E0282).
    let parsed = serde_json::from_str::<T>(&raw).ok().filter(|v| v.version() == T::VERSION);
    match parsed {
        Some(v) => Load::Loaded(v),
        None => {
            log::warn!(target: target, "corrupt or version-mismatched {path:?}; starting empty");
            preserve_corrupt(path, target);
            Load::Corrupt
        }
    }
}

/// Move an unparseable file aside so a subsequent save can't overwrite history
/// that a downgrade (or a fixed parser) could still recover. Best effort: if the
/// rename fails there is nothing further to try, so log and carry on.
fn preserve_corrupt(path: &Path, target: &str) {
    let backup = path.with_extension("json.corrupt");
    if let Err(e) = std::fs::rename(path, &backup) {
        log::warn!(target: target, "could not preserve corrupt store: {e}");
    }
}

/// Atomic save: serialize, write to a process-unique temp file, rename over the
/// target. Rename is atomic on the same volume (Windows MoveFileEx replaces an
/// existing file), so a crash mid-write can never leave a truncated store.
///
/// Single-writer is the CALLER's responsibility — hold your store lock across
/// this call. Two concurrent writers would each rename a full (but differently
/// stale) file, and last-writer-wins would silently drop the other's edit.
pub fn save_atomic<T: Serialize>(path: &Path, value: &T) -> std::io::Result<()> {
    use std::sync::atomic::{AtomicU64, Ordering};
    static TMP_SEQ: AtomicU64 = AtomicU64::new(0);

    let bytes =
        serde_json::to_vec(value).map_err(|e| std::io::Error::new(std::io::ErrorKind::InvalidData, e))?;
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
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde::Deserialize;

    #[derive(Serialize, Deserialize, PartialEq, Debug)]
    struct Demo {
        schema_version: u32,
        value: String,
    }
    impl VersionedStore for Demo {
        const VERSION: u32 = 1;
        fn version(&self) -> u32 {
            self.schema_version
        }
    }

    fn tmpdir(tag: &str) -> std::path::PathBuf {
        let d = std::env::temp_dir().join(format!("themia-store-{}-{}", tag, std::process::id()));
        let _ = std::fs::remove_dir_all(&d);
        d
    }

    #[test]
    fn absent_is_silent_and_distinct_from_corrupt() {
        let p = tmpdir("absent").join("s.json");
        assert!(matches!(load_versioned::<Demo>(&p, "test"), Load::Absent));
    }

    #[test]
    fn round_trip_preserves_value() {
        let dir = tmpdir("rt");
        let p = dir.join("s.json");
        let v = Demo { schema_version: 1, value: "hi".into() };
        save_atomic(&p, &v).expect("save");
        match load_versioned::<Demo>(&p, "test") {
            Load::Loaded(got) => assert_eq!(got, v),
            other => panic!("expected Loaded, got {other:?}"),
        }
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn save_leaves_no_temp_files() {
        let dir = tmpdir("notmp");
        let p = dir.join("s.json");
        save_atomic(&p, &Demo { schema_version: 1, value: "x".into() }).expect("save");
        let entries: Vec<_> = std::fs::read_dir(&dir)
            .expect("dir")
            .map(|e| e.expect("dirent").file_name().to_string_lossy().to_string())
            .collect();
        assert_eq!(entries, vec!["s.json"]);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn corrupt_is_preserved_aside_not_overwritten() {
        let dir = tmpdir("corrupt");
        std::fs::create_dir_all(&dir).expect("mkdir");
        let p = dir.join("s.json");
        std::fs::write(&p, "{not json").expect("write");
        assert!(matches!(load_versioned::<Demo>(&p, "test"), Load::Corrupt));
        assert!(!p.exists(), "corrupt file moved aside");
        assert!(dir.join("s.json.corrupt").exists());
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn version_mismatch_is_corrupt_not_loaded() {
        let dir = tmpdir("ver");
        std::fs::create_dir_all(&dir).expect("mkdir");
        let p = dir.join("s.json");
        std::fs::write(&p, "{\"schema_version\":999,\"value\":\"x\"}").expect("write");
        assert!(matches!(load_versioned::<Demo>(&p, "test"), Load::Corrupt));
        let _ = std::fs::remove_dir_all(&dir);
    }

    /// A future version must never be silently downgraded into the current
    /// default and then saved over — that would destroy a newer build's data.
    #[test]
    fn missing_required_field_is_corrupt() {
        let dir = tmpdir("missing");
        std::fs::create_dir_all(&dir).expect("mkdir");
        let p = dir.join("s.json");
        std::fs::write(&p, "{\"schema_version\":1}").expect("write");
        assert!(matches!(load_versioned::<Demo>(&p, "test"), Load::Corrupt));
        let _ = std::fs::remove_dir_all(&dir);
    }
}
