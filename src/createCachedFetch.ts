import { createSignal, createMemo, createEffect, untrack, type Accessor } from 'solid-js';
import { logger } from './debugLog';

const SCHEMA_VERSION = 1;
const DEFAULT_MAX_ENTRIES = 50;

interface PersistedEntry<T> {
  v: number;
  ts: number;
  data: T;
}

interface SharedEntry<T> {
  read: Accessor<T | null>;
  write: (v: T | null) => void;
  /**
   * When this entry last fetched successfully, as a SIGNAL rather than a plain
   * number: widgets need to render "last updated" and a staleness badge, which
   * they cannot do off a value that never notifies.
   */
  tsRead: Accessor<number>;
  tsWrite: (v: number) => void;
  inflight: Promise<void> | null;
}

const STORE: Map<string, SharedEntry<unknown>> = new Map();
// Per-namespace persistence kill-switch: a namespace whose writes can't fit even
// after evicting its own oldest entries stops persisting, but ONLY that
// namespace, so one bloated cache (e.g. rss) can't disable caching for all the
// others (github, weather, …).
const persistDisabledNs = new Set<string>();

function lsKey(ns: string, key: string): string {
  return `themia.cache.${ns}.${key}`;
}

function lsIndexKey(ns: string): string {
  return `themia.cache-index.${ns}`;
}

function readPersisted<T>(ns: string, key: string): { data: T; ts: number } | null {
  if (persistDisabledNs.has(ns)) return null;
  try {
    const raw = localStorage.getItem(lsKey(ns, key));
    if (!raw) return null;
    const parsed = JSON.parse(raw) as PersistedEntry<T>;
    if (parsed.v !== SCHEMA_VERSION) return null;
    if (parsed.data === undefined || typeof parsed.ts !== 'number') return null;
    return { data: parsed.data, ts: parsed.ts };
  } catch {
    return null;
  }
}

function readIndex(ns: string): string[] {
  try {
    const raw = localStorage.getItem(lsIndexKey(ns));
    return raw ? (JSON.parse(raw) as string[]) : [];
  } catch {
    return [];
  }
}

function writePersisted<T>(ns: string, key: string, data: T, ts: number, maxEntries: number): void {
  if (persistDisabledNs.has(ns)) return;
  let serialized: string;
  try {
    serialized = JSON.stringify({ v: SCHEMA_VERSION, ts, data } satisfies PersistedEntry<T>);
  } catch {
    return;
  }

  let index = readIndex(ns).filter(k => k !== key);
  index.push(key);

  for (let attempt = 0; attempt < 20; attempt++) {
    try {
      localStorage.setItem(lsKey(ns, key), serialized);
      while (index.length > maxEntries) {
        const oldest = index.shift()!;
        try { localStorage.removeItem(lsKey(ns, oldest)); } catch { /* noop */ }
      }
      localStorage.setItem(lsIndexKey(ns), JSON.stringify(index));
      return;
    } catch (e: unknown) {
      const name = e instanceof Error ? e.name : '';
      if (name !== 'QuotaExceededError' && name !== 'NS_ERROR_DOM_QUOTA_REACHED') {
        return;
      }
      if (index.length > 1) {
        const oldest = index.shift()!;
        if (oldest === key) {
          persistDisabledNs.add(ns);
          logger.error('cache', `localStorage quota exceeded for ${ns}; persistence disabled`);
          return;
        }
        try { localStorage.removeItem(lsKey(ns, oldest)); } catch { /* noop */ }
        continue;
      }
      persistDisabledNs.add(ns);
      logger.error('cache', `localStorage quota exceeded for ${ns}; persistence disabled`);
      return;
    }
  }
}

function getEntry<T>(ns: string, key: string): SharedEntry<T> {
  const id = `${ns}::${key}`;
  const existing = STORE.get(id) as SharedEntry<T> | undefined;
  if (existing) return existing;

  const persisted = readPersisted<T>(ns, key);
  const [read, write] = createSignal<T | null>(persisted ? persisted.data : null);
  const [tsRead, tsWrite] = createSignal<number>(persisted ? persisted.ts : 0);
  const entry: SharedEntry<T> = {
    read,
    write,
    tsRead,
    tsWrite,
    inflight: null,
  };
  STORE.set(id, entry as SharedEntry<unknown>);
  return entry;
}

export interface CachedFetch<T> {
  data: Accessor<T | null>;
  /** First load only: true while fetching with nothing cached to show yet. */
  loading: Accessor<boolean>;
  error: Accessor<string | null>;
  /**
   * True whenever THIS instance has a fetch in flight, including a forced
   * refresh over already-rendered data - which `loading` deliberately does not
   * cover, and which left the RSS widget's Refresh action with no observable
   * effect at all when it succeeded with unchanged items or failed behind
   * [S-25] (#81). A widget that offers a Refresh must render this.
   */
  refreshing: Accessor<boolean>;
  /** epoch ms of the last successful fetch for the current key; 0 if never. */
  lastUpdated: Accessor<number>;
  refresh: (force?: boolean) => Promise<void>;
}

export interface CachedFetchOptions<T> {
  namespace: string;
  key: () => string | null;
  fetcher: (key: string, signal: AbortSignal) => Promise<T>;
  staleMs?: () => number;
  maxEntries?: number;
}

export function createCachedFetch<T>(opts: CachedFetchOptions<T>): CachedFetch<T> {
  const max = opts.maxEntries ?? DEFAULT_MAX_ENTRIES;
  const [loading, setLoading] = createSignal(false);
  const [refreshing, setRefreshing] = createSignal(false);
  const [error, setError] = createSignal<string | null>(null);

  const currentKey = createMemo(() => opts.key());
  const data = createMemo<T | null>(() => {
    const k = currentKey();
    if (!k) return null;
    return getEntry<T>(opts.namespace, k).read();
  });
  const lastUpdated = createMemo<number>(() => {
    const k = currentKey();
    if (!k) return 0;
    return getEntry<T>(opts.namespace, k).tsRead();
  });

  // Monotonic request id. Only the LATEST request started by THIS instance may
  // write the SHARED error/loading signals. A superseded request (the key
  // changed mid-flight) early-returns without touching them, so a slow prior
  // fetch's late completion (e.g. an old-key 404 landing after the new key
  // already rendered) can't flash a stale error or spinner. The per-key data
  // signal is independently race-safe (each key has its own entry).
  let reqSeq = 0;
  let activeReqId = 0;
  // The controller of THIS instance's in-flight fetch, so a new key's fetch can
  // abort the prior one (and the HTTP request behind it). Scoped per instance,
  // never touches other instances/widgets that share the global cache store.
  let activeController: AbortController | null = null;

  function isAbortError(e: unknown): boolean {
    return e instanceof DOMException && e.name === 'AbortError';
  }

  async function refreshFor(k: string, force: boolean): Promise<void> {
    const entry = getEntry<T>(opts.namespace, k);
    const stale = opts.staleMs?.() ?? 0;
    const ts = untrack(() => entry.tsRead());
    const fresh = ts > 0 && Date.now() - ts < stale;
    if (!force && fresh) return;

    // Another instance sharing this key ([S-24]) is already fetching. Join it
    // rather than starting a second request - but still report as refreshing,
    // so a widget whose Refresh landed on a shared in-flight fetch shows the
    // same feedback as one that started its own.
    if (entry.inflight) {
      const joinId = ++reqSeq;
      activeReqId = joinId;
      setRefreshing(true);
      return entry.inflight.finally(() => {
        if (joinId === activeReqId) setRefreshing(false);
      });
    }

    // This fetch is now the latest for the instance; cancel the prior in-flight
    // one it supersedes so its HTTP request stops and its late result is a no-op.
    const reqId = ++reqSeq;
    activeReqId = reqId;
    if (activeController) activeController.abort();

    const controller = new AbortController();
    activeController = controller;

    if (entry.read() === null) setLoading(true);
    setRefreshing(true);

    const p = (async () => {
      try {
        const result = await opts.fetcher(k, controller.signal);
        const now = Date.now();
        entry.tsWrite(now);
        entry.write(result);
        writePersisted(opts.namespace, k, result, now, max);
        if (reqId === activeReqId) setError(null);
      } catch (e: unknown) {
        if (isAbortError(e)) return; // superseded/cancelled, not a real failure
        if (reqId !== activeReqId) return; // a stale fetch must not clobber state
        const msg = e instanceof Error ? e.message : String(e);
        setError(msg.replace(/^Error:\s*/, ''));
        logger.error(`cache:${opts.namespace}`, `fetch failed for ${k}: ${msg}`);
      } finally {
        // This entry serialises its own fetches (the `entry.inflight` early
        // return above), so the running fetch always owns `entry.inflight`.
        entry.inflight = null;
        if (activeController === controller) activeController = null;
        if (reqId === activeReqId) {
          setLoading(false);
          setRefreshing(false);
        }
      }
    })();
    entry.inflight = p;
    return p;
  }

  async function refresh(force = false): Promise<void> {
    const k = untrack(() => currentKey());
    if (!k) return;
    return refreshFor(k, force);
  }

  createEffect(() => {
    const k = currentKey();
    if (!k) return;
    untrack(() => { void refreshFor(k, false); });
  });

  return { data, loading, error, refreshing, lastUpdated, refresh };
}
