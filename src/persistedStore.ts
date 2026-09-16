// ---------------------------------------------------------------------------
// Persisted-store core: schema'd localStorage (de)serialization
//
// The bug this kills: hand-rolled `JSON.parse(localStorage.getItem(k))` straight
// into a store. That path can't tell ABSENT from CORRUPT, silently swallows bad
// data, lets wrong-typed fields through (`uiScale: "oops"`), and has no version
// to migrate from. Every persisted store should load/save through here instead.
//
// Guarantees (per the engineering principles):
//  - Parse through a schema that carries a `version`. New writes are wrapped in
//    a `{ __sv, data }` envelope so the version travels with the value.
//  - A legacy bare blob (no envelope) loads as version 0 and is normalized +
//    re-saved enveloped on the next write, a transparent, lossless migration.
//  - ABSENT key  → defaults, silently.
//  - CORRUPT key → defaults, plus a LOUD log (injected `onCorrupt`), so a parse
//    failure is never mistaken for "no data".
//  - `parse` is contracted total: it returns a fully-formed value for ANY input
//    (filling missing / wrong-typed fields from defaults) and must not throw. A
//    throwing schema still fails safe to defaults.
//
// This module is intentionally free of `solid-js`, `./sync`, and `./debugLog`
// imports so it stays unit-testable under the plain node test env. The reactive
// + cross-window wrapper lives in `createPersistedStore.ts`.
// ---------------------------------------------------------------------------

export interface PersistedSchema<T> {
  /** Current schema version. Bump when the shape changes; pair with `migrate`. */
  version: number;
  /** A fresh default value. Returned for an absent or unrecoverable key. */
  defaults: () => T;
  /**
   * Total normalizer: validate + coerce an arbitrary parsed JSON payload into a
   * valid `T`, filling anything missing or wrong-typed from defaults. MUST NOT
   * throw (a throw is caught and treated as corrupt).
   */
  parse: (raw: unknown) => T;
  /**
   * Optional: upgrade an older-version payload to the current shape BEFORE
   * `parse`. Receives the stored payload and its version (0 = legacy bare blob).
   */
  migrate?: (raw: unknown, fromVersion: number) => unknown;
}

/** On-disk envelope. `__sv` (schema version) is deliberately unusual so a legacy
 *  bare payload can never be mistaken for an envelope. */
interface Envelope {
  __sv: number;
  data: unknown;
}

function isEnvelope(x: unknown): x is Envelope {
  return (
    !!x &&
    typeof x === 'object' &&
    typeof (x as { __sv?: unknown }).__sv === 'number' &&
    'data' in (x as object)
  );
}

export interface LoadOptions {
  /** Called with a human-readable reason whenever a value can't be read and the
   *  loader falls back to defaults for a reason OTHER than the key being absent.
   *  Wire this to a loud logger so corruption is visible. */
  onCorrupt?: (message: string) => void;
}

/** Minimal storage surface; `localStorage` satisfies it, and tests inject a fake. */
export type ReadableStorage = Pick<Storage, 'getItem'>;
export type WritableStorage = Pick<Storage, 'setItem'>;
/** Saves need read+write: they copy the prior value to its backup before
 *  overwriting (1.19 last-known-good). `localStorage` satisfies it. */
export type BackedStorage = Pick<Storage, 'getItem' | 'setItem'>;

/** Last-known-good backup key (1.19). A PREFIX, not a `.bak` suffix, so it can't
 *  collide with the `widget-layout-*` / `widget-design-*` cross-window match
 *  patterns (a suffix on `widget-layout-work` would still `startsWith` the
 *  pattern and fire spurious sync events). importExport clears `__bak__*` on
 *  import so backups never travel with an export. */
export const BAK_PREFIX = '__bak__';

/** Decode a raw stored string through the schema pipeline (JSON → envelope →
 *  migrate → parse). Returns a discriminated result instead of throwing or
 *  falling back, so the caller can try a backup before giving up. */
function decode<T>(
  raw: string,
  schema: PersistedSchema<T>,
): { ok: true; value: T } | { ok: false; reason: string } {
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return { ok: false, reason: 'corrupt JSON' };
  }

  let version: number;
  let payload: unknown;
  if (isEnvelope(parsed)) {
    version = parsed.__sv;
    payload = parsed.data;
  } else {
    // Legacy bare blob written before this module existed → version 0.
    version = 0;
    payload = parsed;
  }

  if (version < schema.version && schema.migrate) {
    try {
      payload = schema.migrate(payload, version);
    } catch (e) {
      return { ok: false, reason: `migration from v${version} failed (${String(e)})` };
    }
  }

  try {
    return { ok: true, value: schema.parse(payload) };
  } catch (e) {
    // `parse` is contracted total; a throw means a schema bug. Fail safe.
    return { ok: false, reason: `parse threw (${String(e)})` };
  }
}

/**
 * Load + validate a persisted value. Never throws; always returns a valid `T`.
 * On a corrupt primary, recovers from the last-known-good backup (`__bak__<key>`,
 * written by every successful save) before falling back to defaults (1.19), so a
 * single corrupting write no longer loses user state.
 */
export function loadPersisted<T>(
  key: string,
  schema: PersistedSchema<T>,
  storage: ReadableStorage,
  opts: LoadOptions = {},
): T {
  let raw: string | null;
  try {
    raw = storage.getItem(key);
  } catch (e) {
    // Storage itself unavailable (private mode, disabled). Not corruption.
    opts.onCorrupt?.(`${key}: storage unavailable (${String(e)}), using defaults`);
    return schema.defaults();
  }

  // ABSENT: the key was never written. Silent defaults.
  if (raw === null || raw === '') return schema.defaults();

  const primary = decode(raw, schema);
  if (primary.ok) return primary.value;

  // Primary is unusable: try the last-known-good backup before defaults.
  let bakRaw: string | null = null;
  try {
    bakRaw = storage.getItem(BAK_PREFIX + key);
  } catch {
    /* ignore: backup read failure leaves bakRaw null → falls through to defaults */
  }
  if (bakRaw != null && bakRaw !== '') {
    const bak = decode(bakRaw, schema);
    if (bak.ok) {
      opts.onCorrupt?.(`${key}: ${primary.reason}; recovered from last-known-good backup`);
      return bak.value;
    }
  }
  opts.onCorrupt?.(`${key}: ${primary.reason}; no usable backup, using defaults`);
  return schema.defaults();
}

export interface SaveOptions {
  /** Called when serialization or the write fails. Wire to a loud logger. */
  onError?: (message: string) => void;
}

/** Copy the current primary value to its last-known-good backup before an
 *  overwrite (1.19). Best-effort: a failure here must never block the real save. */
function backupPrior(key: string, storage: BackedStorage): void {
  try {
    const prior = storage.getItem(key);
    if (prior != null && prior !== '') storage.setItem(BAK_PREFIX + key, prior);
  } catch {
    // ignore: backup is best-effort; the primary write is what matters.
  }
}

/**
 * Persist a value wrapped in the current-version envelope. `localStorage.setItem`
 * is itself an atomic single-key write, so no temp/rename dance is needed (unlike
 * the file-backed Rust store).
 */
export function savePersisted<T>(
  key: string,
  schema: PersistedSchema<T>,
  value: T,
  storage: BackedStorage,
  opts: SaveOptions = {},
): void {
  let serialized: string;
  try {
    serialized = JSON.stringify({ __sv: schema.version, data: value } satisfies Envelope);
  } catch (e) {
    opts.onError?.(`${key}: serialize failed (${String(e)})`);
    return;
  }
  try {
    backupPrior(key, storage);
    storage.setItem(key, serialized);
  } catch (e) {
    opts.onError?.(`${key}: write failed (${String(e)})`);
  }
}

/**
 * Persist a value WITHOUT the version envelope; the on-disk shape is exactly
 * `JSON.stringify(value)`, the same bare format the pre-1.12 stores used.
 *
 * Use this (instead of `savePersisted`) for a key that OTHER modules read
 * directly as a plain blob (e.g. `widget-global-design`, read by importExport /
 * i18n / layout). `loadPersisted` still reads it back as a legacy version-0
 * payload and validates it through the schema, so you keep schema'd loading and
 * the absent/corrupt distinction without breaking those external bare readers.
 */
export function savePersistedBare<T>(
  key: string,
  value: T,
  storage: BackedStorage,
  opts: SaveOptions = {},
): void {
  let serialized: string;
  try {
    serialized = JSON.stringify(value);
  } catch (e) {
    opts.onError?.(`${key}: serialize failed (${String(e)})`);
    return;
  }
  try {
    backupPrior(key, storage);
    storage.setItem(key, serialized);
  } catch (e) {
    opts.onError?.(`${key}: write failed (${String(e)})`);
  }
}

// ---------------------------------------------------------------------------
// Schema builders + field guards
//
// Matches the codebase's existing guard-based validation style (typeof /
// Array.isArray checks, no schema library). `objectSchema` generalizes the
// ubiquitous `raw[k] ?? default` pattern but adds the missing piece: a per-field
// TYPE check, so a wrong-typed persisted field falls back to its default instead
// of poisoning the store.
// ---------------------------------------------------------------------------

export type Guard<V> = (v: unknown) => v is V;

export const isString: Guard<string> = (v): v is string => typeof v === 'string';
export const isBoolean: Guard<boolean> = (v): v is boolean => typeof v === 'boolean';
export const isNumber: Guard<number> = (v): v is number =>
  typeof v === 'number' && Number.isFinite(v);

/** Guard for a fixed set of string literals (closed sets → enums, not strings). */
export function isOneOf<const A extends readonly string[]>(
  ...values: A
): Guard<A[number]> {
  return (v): v is A[number] => typeof v === 'string' && (values as readonly string[]).includes(v);
}

/** Guard for an array whose every element passes `el`. */
export function isArrayOf<V>(el: Guard<V>): Guard<V[]> {
  return (v): v is V[] => Array.isArray(v) && v.every(el);
}

/**
 * Build a schema for a flat-ish object. Each field has a type guard; on load,
 * a field is taken from the payload only when present AND passing its guard,
 * otherwise the default is used. The result is always a complete object.
 */
export function objectSchema<T extends object>(opts: {
  version: number;
  defaults: () => T;
  fields: { [K in keyof T]: Guard<T[K]> };
  migrate?: (raw: unknown, fromVersion: number) => unknown;
}): PersistedSchema<T> {
  return {
    version: opts.version,
    defaults: opts.defaults,
    migrate: opts.migrate,
    parse: (raw: unknown): T => {
      const out = opts.defaults();
      if (!raw || typeof raw !== 'object') return out;
      const src = raw as Record<string, unknown>;
      for (const k in opts.fields) {
        const key = k as keyof T & string;
        const guard = opts.fields[key];
        if (Object.prototype.hasOwnProperty.call(src, key) && guard(src[key])) {
          out[key] = src[key] as T[typeof key];
        }
      }
      return out;
    },
  };
}
