// @vitest-environment jsdom
//
// [Q3_/E3] Aborting superseded fetches. When the cache key changes mid-flight
// (repo/feed/city/account switch), the slow PRIOR fetch must not clobber the
// SHARED per-instance error/loading signals when it finally lands, and it must
// be handed an AbortSignal that is flipped to `.aborted === true` so the actual
// HTTP request can be cancelled. The per-KEY data signal is already race-safe;
// the race regression is on the shared error/loading state.
//
// Runs under jsdom for localStorage. debugLog is mocked so importing
// createCachedFetch doesn't transitively pull the @tauri-apps/* plugins.
//
// The cache is mounted with solid-js/web `render` so the full reactive scheduler
// runs: the key-change auto-refresh effect fires and the key memo recomputes
// (bare createRoot under vitest never advances the scheduler). The key is driven
// by a signal so changing it supersedes the in-flight fetch through the real
// reactive path.

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { createSignal, createRoot } from 'solid-js';
import { render } from 'solid-js/web';

vi.mock('./debugLog', () => ({
  logger: { error: () => {}, warn: () => {}, info: () => {}, debug: () => {} },
}));

import { createCachedFetch, type CachedFetch } from './createCachedFetch.ts';

beforeEach(() => localStorage.clear());

let dispose: (() => void) | null = null;
let host: HTMLDivElement | null = null;
afterEach(() => {
  dispose?.();
  dispose = null;
  host?.remove();
  host = null;
});

// A fetcher whose per-key resolution is driven manually by the test, and which
// records the AbortSignal it was handed for each key so we can assert it later.
function deferredFetcher() {
  const resolvers = new Map<string, (v: unknown) => void>();
  const rejecters = new Map<string, (e: unknown) => void>();
  const signals = new Map<string, AbortSignal | undefined>();
  const fetcher = (key: string, signal?: AbortSignal): Promise<unknown> => {
    signals.set(key, signal);
    return new Promise((resolve, reject) => {
      resolvers.set(key, resolve);
      rejecters.set(key, reject);
    });
  };
  return {
    fetcher,
    resolve: (k: string, v: unknown) => resolvers.get(k)!(v),
    reject: (k: string, e: unknown) => rejecters.get(k)!(e),
    signalFor: (k: string) => signals.get(k),
  };
}

const tick = () => new Promise<void>((r) => setTimeout(r, 0));

// Mount a cache via render so its createEffect / key memo run under the real
// reactive scheduler. Returns the cache plus the key setter.
function mountCache(opts: {
  namespace: string;
  initialKey: string | null;
  fetcher: (key: string, signal?: AbortSignal) => Promise<unknown>;
  staleMs?: () => number;
}): { cache: CachedFetch<unknown>; setKey: (k: string | null) => void } {
  host = document.createElement('div');
  document.body.appendChild(host);
  const [key, setKey] = createSignal<string | null>(opts.initialKey);
  let cache!: CachedFetch<unknown>;
  dispose = render(() => {
    cache = createCachedFetch<unknown>({
      namespace: opts.namespace,
      key,
      fetcher: opts.fetcher,
      staleMs: opts.staleMs,
    });
    return null;
  }, host);
  return { cache, setKey };
}

describe('createCachedFetch: superseded-fetch race', () => {
  it('Q3_ superseded fetch is aborted and never clobbers shared error/loading', async () => {
    const def = deferredFetcher();
    const { cache, setKey } = mountCache({
      namespace: 'q3-race',
      initialKey: 'A',
      fetcher: def.fetcher,
    });

    // First fetch for key A is in flight (its promise stays pending until driven).
    await tick();
    expect(cache.loading()).toBe(true);

    // Key switches to B before A completes, so A is now superseded. The auto-effect
    // kicks off a fresh fetch for B.
    setKey('B');
    await tick();

    // B completes first and renders.
    def.resolve('B', { val: 'b-data' });
    await tick();
    expect(cache.data()).toEqual({ val: 'b-data' });
    expect(cache.error()).toBe(null);
    expect(cache.loading()).toBe(false);

    // Now the superseded A fetch finally fails (e.g. a late 404). Its late
    // completion must NOT write the shared error/loading signals.
    def.reject('A', new Error('Repository not found'));
    await tick();

    // (a) superseded result did not surface a stale error, and did not flip
    //     loading back on after B already settled it.
    expect(cache.error()).toBe(null);
    expect(cache.loading()).toBe(false);

    // (b) the superseded fetch was handed an AbortSignal that is now aborted.
    const aSignal = def.signalFor('A');
    expect(aSignal).toBeInstanceOf(AbortSignal);
    expect(aSignal!.aborted).toBe(true);

    // The current fetch (B) must NOT have been aborted.
    const bSignal = def.signalFor('B');
    expect(bSignal).toBeInstanceOf(AbortSignal);
    expect(bSignal!.aborted).toBe(false);
  });

  it('AbortError from a fetcher is treated as a non-error', async () => {
    const def = deferredFetcher();
    const { cache } = mountCache({
      namespace: 'q3-aborterr',
      initialKey: 'only',
      fetcher: def.fetcher,
    });

    await tick();
    expect(cache.loading()).toBe(true);

    def.reject('only', new DOMException('aborted', 'AbortError'));
    await tick();

    // An AbortError is not a user-facing failure.
    expect(cache.error()).toBe(null);
    expect(cache.loading()).toBe(false);
  });
});

// Issue #81: "The refresh button never respond." It was responding. `loading`
// is set only when there is nothing cached to show, and a widget with cached
// items renders those items whether the refresh succeeded, changed nothing, or
// failed - so a forced refresh over existing data produced no observable state
// change of any kind. `refreshing` is the signal a widget can actually render.
describe('issue_81 a forced refresh is observable', () => {
  it('S-33_ refreshing() is true during a forced refresh over already-cached data', async () => {
    const def = deferredFetcher();
    const { cache } = mountCache({
      namespace: 'issue81-refreshing',
      initialKey: 'K',
      fetcher: def.fetcher,
    });

    // First load populates the cache.
    await tick();
    def.resolve('K', { val: 1 });
    await tick();
    expect(cache.data()).toEqual({ val: 1 });
    expect(cache.refreshing()).toBe(false);

    // The context-menu Refresh path: force past the stale window with data present.
    const p = cache.refresh(true);
    await tick();

    // `loading` stays false BY DESIGN (items are on screen, not a spinner
    // screen) - which is exactly why it could not carry this feedback.
    expect(cache.loading()).toBe(false);
    expect(cache.refreshing()).toBe(true);

    def.resolve('K', { val: 2 });
    await p;
    await tick();
    expect(cache.refreshing()).toBe(false);
    expect(cache.data()).toEqual({ val: 2 });
  });

  it('S-33_ refreshing() clears when the forced refresh FAILS, and the reason surfaces', async () => {
    const def = deferredFetcher();
    const { cache } = mountCache({
      namespace: 'issue81-refresh-fail',
      initialKey: 'K',
      fetcher: def.fetcher,
    });

    await tick();
    def.resolve('K', { val: 1 });
    await tick();

    const p = cache.refresh(true);
    await tick();
    expect(cache.refreshing()).toBe(true);

    def.reject('K', new Error('HTTP 403 Forbidden'));
    await p;
    await tick();

    expect(cache.refreshing()).toBe(false);
    // Items survive ([S-25]) AND the real reason is available for the widget to
    // show. Previously the items survived and the reason went nowhere.
    expect(cache.data()).toEqual({ val: 1 });
    expect(cache.error()).toBe('HTTP 403 Forbidden');
  });

  it('S-34_ lastUpdated() tracks the last SUCCESSFUL fetch, so staleness is renderable', async () => {
    const def = deferredFetcher();
    const { cache } = mountCache({
      namespace: 'issue81-lastupdated',
      initialKey: 'K',
      fetcher: def.fetcher,
    });

    expect(cache.lastUpdated()).toBe(0);

    await tick();
    const before = Date.now();
    def.resolve('K', { val: 1 });
    await tick();
    const stamped = cache.lastUpdated();
    expect(stamped).toBeGreaterThanOrEqual(before);

    // A failed refresh must NOT advance the stamp: "showing items from 2 days
    // ago" is only true if a failure leaves the old time in place.
    const p = cache.refresh(true);
    await tick();
    def.reject('K', new Error('HTTP 500 Internal Server Error'));
    await p;
    await tick();
    expect(cache.lastUpdated()).toBe(stamped);
  });

  it('S-33_ a refresh that JOINS another widget\'s in-flight fetch still reports as refreshing', async () => {
    // [S-24]: widgets with the same key share one entry and one request. The
    // second widget's Refresh must not look dead just because it did not start
    // the request it is waiting on.
    const def = deferredFetcher();
    const a = mountCache({ namespace: 'issue81-shared', initialKey: 'K', fetcher: def.fetcher });
    await tick();

    let second!: CachedFetch<unknown>;
    const disposeSecond = createRoot((d) => {
      second = createCachedFetch<unknown>({
        namespace: 'issue81-shared',
        key: () => 'K',
        fetcher: def.fetcher,
      });
      return d;
    });
    try {
      const p = second.refresh(true);
      await tick();
      expect(second.refreshing()).toBe(true);

      def.resolve('K', { val: 'shared' });
      await p;
      await tick();
      expect(second.refreshing()).toBe(false);
      expect(a.cache.data()).toEqual({ val: 'shared' });
    } finally {
      disposeSecond();
    }
  });
});
