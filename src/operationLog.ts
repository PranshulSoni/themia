// ---------------------------------------------------------------------------
// App-wide operation log for undoable file operations.
//
// Any widget that performs reversible file-system actions (rename, delete,
// move, copy, create) records an entry here. Ctrl+Z pops the newest entry
// and runs the reverse action. The log is persisted in localStorage and
// synchronised across widget windows via storage events.
//
// Pending framework
// -----------------
// File ops run on the Rust side and can take a while (network shares, big
// copies, Recycle Bin enumeration for delete). If an op is still in flight
// when the user hits Ctrl+Z, naive "record on success" logic causes the
// undo to pop a *different, older* entry, and its reverse command then
// fires against stale state, with the typical symptom "file not found".
//
// To fix this, every mutating op goes through `beginOp`: a placeholder is
// pushed to the log immediately (occupying the top slot), and `undoLast`
// awaits its Promise before running any reverse command. Success flips
// `pending:false`; failure removes the entry.
//
// Undo-only (no redo). Capacity 100.
// ---------------------------------------------------------------------------

import { createStore, reconcile } from 'solid-js/store';
import { invoke } from './ipc.ts';
import { emit } from '@tauri-apps/api/event';
import { onStorageKey, sharedStorage } from './sync.ts';
import { debugLog, logger } from './debugLog.ts';
import { updateWidgetConfig } from './layout.ts';
import { loadPersisted, savePersistedBare, type PersistedSchema } from './persistedStore.ts';

declare global {
  interface Window {
    __opLog?: {
      setEntries: (entries: OpEntry[]) => void;
      claimKey: (entryId: string) => string;
      claimToken: () => string;
      peek: () => OpEntry | null;
      undo: () => Promise<UndoResult>;
      entries: () => readonly OpEntry[];
      setInvoke: (fn: typeof invoke | null) => void;
    };
  }
}

const DBG = 'op-log';
const STORAGE_KEY = 'op-log';
const MAX_ENTRIES = 100;

export interface TrashedFile {
  original_path: string;
  name: string;
  deleted_at_ms: number;
  // Populated by the Rust side after the bin enumeration so restore can skip
  // re-enumerating. Optional here for backward compat with tokens persisted
  // before this was added.
  id?: string;
  original_parent?: string;
  time_deleted?: number;
}

// Discriminated union of op-kind payloads. Kept separate from the stored
// entry so `beginOp` can accept the payload without the engine-maintained
// `id`/`ts`/`pending` fields.
export type OpData =
  | {
      kind: 'rename';
      dir: string;                // parent directory, emitted as folder-changed
      fromPath: string;           // new path (current location)
      originalPath: string;       // old path (before rename); restore target
    }
  | {
      kind: 'delete';
      dir: string;                // directory the deletions happened in
      paths?: string[];           // original paths, set at begin, used by describe() while pending
      tokens: TrashedFile[];      // to pass back to fs_restore_from_trash
    }
  | {
      kind: 'paste-cut';          // move: srcPaths → destDir
      srcDir: string;
      destDir: string;
      movedPaths: string[];       // final paths at dest (destDir/<basename>)
      originalPaths: string[];    // where files were before the move
    }
  | {
      kind: 'paste-copy';         // copy: srcPaths → destDir (originals stay)
      destDir: string;
      createdPaths: string[];     // new copies at dest (to delete on undo)
    }
  | {
      kind: 'create';             // new folder or new file
      dir: string;
      createdPath: string;        // path of the new file/folder (to trash on undo)
    }
  | {
      kind: 'widget-config';      // generic widget-config mutation (add/remove/reorder/rename/etc.)
      widgetId: string;           // target widget; undo writes previousConfig back via updateWidgetConfig
      label: string;              // short human label shown in the toast (e.g. 'Add shortcut')
      previousConfig: Record<string, unknown>; // full config snapshot captured before the mutation
    };

export type OpEntry = OpData & {
  id: string;
  ts: number;
  pending?: boolean;              // true while the underlying Rust call is in flight
};

interface LogState {
  entries: OpEntry[];
}

const OP_KINDS: readonly OpEntry['kind'][] = [
  'rename', 'delete', 'paste-cut', 'paste-copy', 'create', 'widget-config',
];

// Schema'd persistence (1.12). The undo path dereferences kind-specific fields,
// so a structurally-broken entry must never reach it. parse() requires `entries`
// to be an array and keeps only entries that are objects with a string `id`, a
// numeric `ts`, and a known `kind`. It also drops orphaned pending entries:
// their resolving Promise lived only in the window that started the op, so after
// a reload they can never complete (preserves the prior load() behavior).
// Exported for characterization tests: this parse() is the guard that keeps a
// structurally-broken persisted entry from ever reaching the undo path.
export const opLogSchema: PersistedSchema<LogState> = {
  version: 1,
  defaults: () => ({ entries: [] }),
  parse: (raw: unknown): LogState => {
    if (!raw || typeof raw !== 'object') return { entries: [] };
    const r = raw as { entries?: unknown };
    if (!Array.isArray(r.entries)) return { entries: [] };
    const entries = r.entries.filter((e): e is OpEntry => {
      if (!e || typeof e !== 'object') return false;
      const o = e as Record<string, unknown>;
      if (typeof o.id !== 'string' || typeof o.ts !== 'number') return false;
      if (typeof o.kind !== 'string' || !(OP_KINDS as readonly string[]).includes(o.kind)) return false;
      if (o.pending) return false;
      return true;
    });
    return { entries };
  },
};

function load(): LogState {
  return loadPersisted(STORAGE_KEY, opLogSchema, localStorage, {
    onCorrupt: (m) => logger.error(DBG, m),
  });
}

const [log, setLog] = createStore<LogState>(load());
export { log };

function persist(): void {
  // Bare format ({entries:[...]}, no version envelope): the op-log key is read
  // directly as a plain blob by cross-window undo-claim logic and the e2e
  // harness. loadPersisted still validates it through SCHEMA on the way in.
  savePersistedBare(STORAGE_KEY, { entries: log.entries }, sharedStorage, {
    onError: (m) => logger.error(DBG, m),
  });
}

onStorageKey(STORAGE_KEY, () => {
  setLog(reconcile(load()));
});

function newId(): string {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

function pushEntry(entry: OpEntry): void {
  const next = [...log.entries, entry];
  if (next.length > MAX_ENTRIES) next.splice(0, next.length - MAX_ENTRIES);
  setLog('entries', next);
  persist();
}

/**
 * Append an operation to the log synchronously. Only safe for ops that have
 * *already completed*. For anything that awaits a Tauri invoke, use `beginOp`
 * instead, otherwise a quick Ctrl+Z lands on the wrong entry.
 */
export function recordOp(data: OpData): void {
  const full = { ...data, id: newId(), ts: Date.now() } as OpEntry;
  pushEntry(full);
  debugLog(DBG, `record ${full.kind} (total=${log.entries.length})`);
}

// ---------------------------------------------------------------------------
// Pending-op framework
//
// Call `beginOp(data)` *before* invoking the Rust command. This pushes a
// pending entry and hands back a handle. On command success, call
// `handle.resolve(patch?)`, optionally passing a partial to update fields
// that were unknown at begin-time (e.g. delete's `tokens`). On failure,
// call `handle.reject(err)`, which removes the entry.
//
// `undoLast` awaits the handle's Promise before running any reverse command,
// so Ctrl+Z during an in-flight op blocks until the state is consistent.
// ---------------------------------------------------------------------------

export interface OpHandle {
  id: string;
  promise: Promise<void>;
  /** Flip the entry to non-pending. Pass a patch to update fields filled in by the op. */
  resolve(patch?: Partial<OpData>): void;
  /** Drop the entry from the log and reject the promise. */
  reject(err: unknown): void;
}

// Promise map is per-window. Other windows seeing a pending entry via the
// storage-event sync get "still in progress, try again" on Ctrl+Z.
const pendingPromises = new Map<string, Promise<void>>();

export function beginOp(data: OpData): OpHandle {
  const id = newId();
  let resolveFn!: () => void;
  let rejectFn!: (e: unknown) => void;
  const promise = new Promise<void>((res, rej) => {
    resolveFn = res;
    rejectFn = rej;
  });
  // Prevent unhandled-rejection warnings: undoLast swallows reject errors,
  // and callers don't always await the promise directly.
  promise.catch(() => {});
  pendingPromises.set(id, promise);

  const entry = { ...data, id, ts: Date.now(), pending: true } as OpEntry;
  pushEntry(entry);
  debugLog(DBG, `begin ${data.kind}`);

  return {
    id,
    promise,
    resolve(patch) {
      pendingPromises.delete(id);
      const idx = log.entries.findIndex(e => e.id === id);
      if (idx >= 0) {
        const cur = log.entries[idx];
        // Merge the patch (if any) on top of the existing entry, clear pending.
        // Cast through `any` because TS can't prove the patch matches the union
        // branch, but callers are responsible for patching their own kind.
        const merged = { ...cur, ...(patch ?? {}), pending: false } as OpEntry;
        setLog('entries', idx, merged);
        persist();
      }
      resolveFn();
      debugLog(DBG, `resolve ${data.kind}`);
    },
    reject(err) {
      pendingPromises.delete(id);
      removeEntry(id);
      rejectFn(err);
      debugLog(DBG, `reject ${data.kind}: ${err}`);
    },
  };
}

/** Peek at the most-recent entry without removing it. */
export function peekLast(): OpEntry | null {
  const n = log.entries.length;
  return n ? log.entries[n - 1] : null;
}

function popLast(): OpEntry | null {
  const n = log.entries.length;
  if (!n) return null;
  const last = log.entries[n - 1];
  setLog('entries', log.entries.slice(0, n - 1));
  persist();
  return last;
}

/** Clear the entire log. */
export function clearLog(): void {
  setLog('entries', []);
  persist();
}

/** Remove a specific entry by id (e.g. when revert already done). */
export function removeEntry(id: string): void {
  const next = log.entries.filter(e => e.id !== id);
  if (next.length === log.entries.length) return;
  setLog('entries', next);
  persist();
}

// ---------------------------------------------------------------------------
// Undo
// ---------------------------------------------------------------------------

export interface UndoResult {
  ok: boolean;
  label: string;      // short description shown in the toast
  error?: string;
}

function short(path: string): string {
  const m = /[\\/]([^\\/]+)$/.exec(path);
  return m ? m[1] : path;
}

function describe(entry: OpEntry): string {
  switch (entry.kind) {
    case 'rename':     return `rename of ${short(entry.originalPath)}`;
    case 'delete': {
      // Prefer tokens (authoritative) but fall back to the paths captured
      // at begin-time, so undo toasts read correctly pre-resolve.
      const list = entry.tokens.length
        ? entry.tokens.map(t => t.original_path)
        : (entry.paths ?? []);
      return list.length === 1
        ? `delete of ${short(list[0])}`
        : `delete of ${list.length} items`;
    }
    case 'paste-cut':  return entry.movedPaths.length === 1
      ? `move of ${short(entry.movedPaths[0])}`
      : `move of ${entry.movedPaths.length} items`;
    case 'paste-copy': return entry.createdPaths.length === 1
      ? `copy of ${short(entry.createdPaths[0])}`
      : `copy of ${entry.createdPaths.length} items`;
    case 'create':     return `creation of ${short(entry.createdPath)}`;
    case 'widget-config': return entry.label;
  }
}

const MAX_PENDING_WAIT_MS = 30_000;

function pendingLabel(kind: OpData['kind']): string {
  switch (kind) {
    case 'delete':     return 'Delete still in progress, try again';
    case 'rename':     return 'Rename still in progress, try again';
    case 'paste-cut':  return 'Move still in progress, try again';
    case 'paste-copy': return 'Copy still in progress, try again';
    case 'create':     return 'Creation still in progress, try again';
    case 'widget-config': return 'Widget change still in progress, try again';
  }
}

// ---------------------------------------------------------------------------
// Cross-window claim.
//
// Every widget window runs this module, so a single Ctrl+Z must not cause
// every window to run the reverse command in parallel (the first wins, the
// rest hit stale state and surface "file not found"). Rust scopes the
// app-key emit to one window, but the JS keydown path and future keyboard
// sources can still fire in more than one. Defense-in-depth: whichever
// window observes a given entry-id first writes a claim to localStorage;
// others see the claim via storage events and bail out silently.
// ---------------------------------------------------------------------------

const CLAIM_KEY_PREFIX = 'op-log-undo-claim:';
const CLAIM_TTL_MS = 60_000;

// Unique token per window instance. Stays stable across Ctrl+Z in the
// same window; differs across windows even if labels collide somehow.
const claimToken = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;

// In-memory dedup for the same window: two undoLast() calls racing in
// one process (e.g. JS keydown + Tauri app-key both firing before the
// first popLast()) would both pass the localStorage claim, since they
// share `claimToken`. This set makes the second bail out immediately.
const inFlightUndos = new Set<string>();

function tryClaimUndo(entryId: string): boolean {
  // Same-window guard first, cheaper than a localStorage round-trip and
  // also catches racing calls before either has written the claim.
  if (inFlightUndos.has(entryId)) return false;

  const key = CLAIM_KEY_PREFIX + entryId;
  const now = Date.now();
  try {
    const raw = localStorage.getItem(key);
    if (raw) {
      const prev = JSON.parse(raw) as { token: string; ts: number };
      if (prev && now - prev.ts < CLAIM_TTL_MS && prev.token !== claimToken) {
        return false; // another window already owns this undo
      }
    }
    localStorage.setItem(key, JSON.stringify({ token: claimToken, ts: now }));
    inFlightUndos.add(entryId);
    return true;
  } catch {
    // localStorage unavailable; fall back to in-memory guard only. In
    // that degraded mode cross-window duplicates are possible but not
    // worse than before; same-window races are still deduped.
    inFlightUndos.add(entryId);
    return true;
  }
}

function releaseClaim(entryId: string): void {
  inFlightUndos.delete(entryId);
  try {
    localStorage.removeItem(CLAIM_KEY_PREFIX + entryId);
  } catch {
    // ignore: releasing the cross-window claim is best-effort; a stale key
    // self-expires via CLAIM_TTL_MS so a failed remove can't deadlock undo.
  }
}

/** Undo the most-recent operation. Returns a result for toast display. */
export async function undoLast(): Promise<UndoResult> {
  const entry = peekLast();
  if (!entry) return { ok: false, label: 'Nothing to undo' };

  // Pending: wait for the in-flight op to settle, then retry. Works for
  // every kind. If we don't own the promise (another window started it),
  // surface a "try again" toast rather than blocking indefinitely or undoing
  // the wrong entry. Same for a stale promise that never resolves.
  if (entry.pending) {
    const p = pendingPromises.get(entry.id);
    if (!p) {
      return { ok: false, label: pendingLabel(entry.kind) };
    }
    try {
      await Promise.race([
        p,
        new Promise<void>((_, rej) =>
          setTimeout(() => rej(new Error('pending timeout')), MAX_PENDING_WAIT_MS),
        ),
      ]);
    } catch (err) {
      const isTimeout = err instanceof Error && err.message === 'pending timeout';
      if (isTimeout) {
        return { ok: false, label: pendingLabel(entry.kind) };
      }
      // Op failed: reject() removed the entry; recurse to the one beneath.
    }
    return undoLast();
  }

  // Claim this entry before running the reverse command. If another window
  // already claimed it (same Ctrl+Z arriving on multiple windows), silently
  // no-op, since the owning window shows the toast.
  if (!tryClaimUndo(entry.id)) {
    debugLog(DBG, `undo skipped, claimed by another window: ${entry.id}`);
    return { ok: true, label: '' };
  }

  const label = describe(entry);
  try {
    await performUndo(entry);
    popLast();
    releaseClaim(entry.id);
    debugLog(DBG, `undo ok: ${entry.kind}`);
    return { ok: true, label: `Undid ${label}` };
  } catch (err) {
    releaseClaim(entry.id);
    const msg = err instanceof Error ? err.message : String(err);
    logger.error(DBG, `undo failed: ${msg}`);
    // Keep the entry so the user can retry after fixing the underlying
    // problem (e.g. file in use). Show the real backend error in the toast
    // so they know what went wrong.
    return { ok: false, label: `Could not undo ${label}: ${msg}`, error: msg };
  }
}

// Invoke wrapper, test-overridable. Tauri 2 defines
// `__TAURI_INTERNALS__.invoke` as non-writable/non-configurable, so e2e specs
// can't stub the IPC at the window level. This indirection lets tests replace
// the invoke used inside performUndo without touching Tauri internals.
let invokeFn: typeof invoke = invoke;

// Test-only hook: e2e specs (running under webdriver) need a reliable way
// to seed log entries and intercept the fs_* invokes fired by undo.
if (typeof window !== 'undefined' && navigator.webdriver) {
  window.__opLog = {
    setEntries: (entries: OpEntry[]) => {
      setLog('entries', entries);
      persist();
    },
    // Expose the cross-window claim so tests can simulate a "foreign window"
    // already holding the undo lock. This verifies that a given window bails
    // out silently instead of racing the reverse command.
    claimKey: (entryId: string) => CLAIM_KEY_PREFIX + entryId,
    claimToken: () => claimToken,
    peek: peekLast,
    undo: undoLast,
    entries: () => log.entries,
    setInvoke: (fn: typeof invoke | null) => { invokeFn = fn ?? invoke; },
  };
}

async function performUndo(entry: OpEntry): Promise<void> {
  switch (entry.kind) {
    case 'rename': {
      // Current name on disk is the one we renamed TO. Rename it back.
      const lastSlash = Math.max(entry.originalPath.lastIndexOf('\\'), entry.originalPath.lastIndexOf('/'));
      const origName = entry.originalPath.slice(lastSlash + 1);
      await invokeFn('fs_rename', { path: entry.fromPath, newName: origName });
      emit('folder-changed', entry.dir);
      return;
    }

    case 'delete': {
      await invokeFn('fs_restore_from_trash', { items: entry.tokens });
      emit('folder-changed', entry.dir);
      return;
    }

    case 'paste-cut': {
      // Move moved files back to their source directory.
      await invokeFn('fs_paste', {
        paths: entry.movedPaths,
        destDir: entry.srcDir,
        cut: true,
      });
      emit('folder-changed', entry.destDir);
      emit('folder-changed', entry.srcDir);
      return;
    }

    case 'paste-copy': {
      // Permanent delete: originals still exist, so no need to trash the copies.
      await invokeFn('fs_delete_permanent', { paths: entry.createdPaths });
      emit('folder-changed', entry.destDir);
      return;
    }

    case 'create': {
      // Trash the newly-created item so it's recoverable if the user Ctrl+Z'd by accident.
      await invokeFn('fs_delete', { paths: [entry.createdPath] });
      emit('folder-changed', entry.dir);
      return;
    }

    case 'widget-config': {
      updateWidgetConfig(entry.widgetId, entry.previousConfig);
      return;
    }
  }
}
