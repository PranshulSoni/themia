// @vitest-environment jsdom
//
// [S-23]/[S-38]/[S-39] Calendar birthday caching. The backend enumerates the
// FULL contact list (paged, up to 100 pages) on every `calendar_get_birthdays`
// call, so birthdays must be served from a daily-TTL cache: navigation, the
// 5-minute poll and remounts within the TTL must NOT hit the backend again.
// Permission failures (contacts_not_authorized) are persistent: they are
// cached and retried hourly instead of on every poll, and exposed as
// `deniedAccountIds` so the UI can surface them. Transient failures are never
// cached (old data stays, retry next poll).
//
// Runs under jsdom for localStorage. Mounted via solid-js/web `render` so
// createCachedFetch's auto-refresh effect runs (same approach as
// createCachedFetch.test.ts). Date.now is stubbed to step through TTLs.

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render } from 'solid-js/web';

vi.mock('../../../src/debugLog', () => ({
  logger: { error: vi.fn(), warn: vi.fn(), info: vi.fn(), debug: vi.fn() },
  debugLog: vi.fn(),
}));
vi.mock('../../../src/demo/invoke.ts', () => ({
  invoke: vi.fn(),
  isDemo: () => false,
}));

import {
  birthdayCacheKey,
  fetchBirthdaysForAccounts,
  createBirthdayCache,
  BIRTHDAY_TTL_MS,
  DENIED_RETRY_MS,
  type Birthday,
  type BirthdayCacheData,
} from './birthdays.ts';
import type { CachedFetch } from '../../../src/createCachedFetch.ts';

const tick = () => new Promise<void>((r) => setTimeout(r, 0));

const BD: Birthday = { display_name: 'Ada Lovelace', month: 12, day: 10, year: 1815 };

let now = 1_750_000_000_000;
let dispose: (() => void) | null = null;
let host: HTMLDivElement | null = null;

beforeEach(() => {
  localStorage.clear();
  now = 1_750_000_000_000;
  vi.spyOn(Date, 'now').mockImplementation(() => now);
});

afterEach(() => {
  dispose?.();
  dispose = null;
  host?.remove();
  host = null;
  vi.restoreAllMocks();
});

function mountCache(
  key: string,
  invokeBirthdays: (accountId: string) => Promise<Birthday[]>,
): CachedFetch<BirthdayCacheData> {
  host = document.createElement('div');
  document.body.appendChild(host);
  let cache!: CachedFetch<BirthdayCacheData>;
  dispose = render(() => {
    cache = createBirthdayCache(() => key, invokeBirthdays);
    return null;
  }, host);
  return cache;
}

describe('birthdayCacheKey', () => {
  it('S-23_ null when birthdays disabled or no accounts', () => {
    expect(birthdayCacheKey(false, ['a'])).toBeNull();
    expect(birthdayCacheKey(true, [])).toBeNull();
    expect(birthdayCacheKey(undefined, [])).toBeNull();
  });

  it('S-23_ defaults to enabled and is account-order-insensitive', () => {
    const k1 = birthdayCacheKey(undefined, ['b', 'a']);
    const k2 = birthdayCacheKey(true, ['a', 'b']);
    expect(k1).not.toBeNull();
    expect(k1).toBe(k2);
  });
});

describe('fetchBirthdaysForAccounts', () => {
  it('S-23_ merges per-account results', async () => {
    const other: Birthday = { display_name: 'Linus T', month: 12, day: 28, year: null };
    const res = await fetchBirthdaysForAccounts(['a1', 'a2'], (id) =>
      Promise.resolve(id === 'a1' ? [BD] : [other]),
    );
    expect(res.birthdays).toEqual([BD, other]);
    expect(res.deniedAccountIds).toEqual([]);
  });

  it('S-39_ permission failure lands in deniedAccountIds without throwing', async () => {
    const res = await fetchBirthdaysForAccounts(['good', 'no-perm'], (id) =>
      id === 'good' ? Promise.resolve([BD]) : Promise.reject('contacts_not_authorized'),
    );
    expect(res.birthdays).toEqual([BD]);
    expect(res.deniedAccountIds).toEqual(['no-perm']);
  });

  it('S-38_ transient failure throws so it is never cached as empty', async () => {
    await expect(
      fetchBirthdaysForAccounts(['good', 'flaky'], (id) =>
        id === 'good' ? Promise.resolve([BD]) : Promise.reject(new Error('Graph API error 500')),
      ),
    ).rejects.toThrow('Graph API error 500');
  });
});

describe('createBirthdayCache daily TTL', () => {
  it('S-38_ nav/poll refreshes within the TTL never re-hit the backend', async () => {
    let calls = 0;
    const cache = mountCache('acct-ttl', () => {
      calls++;
      return Promise.resolve([BD]);
    });

    // Mount: the auto-refresh effect performs the single initial fetch.
    await tick();
    expect(cache.data()?.birthdays).toEqual([BD]);
    expect(calls).toBe(1);

    // Nav clicks / arrow keys / poll ticks all funnel into refresh(false).
    await cache.refresh(false);
    await cache.refresh(false);
    await cache.refresh(false);
    expect(calls).toBe(1);

    // Just inside the TTL: still cached.
    now += BIRTHDAY_TTL_MS - 1;
    await cache.refresh(false);
    expect(calls).toBe(1);

    // Past the TTL: one real refetch.
    now += 2;
    await cache.refresh(false);
    await tick();
    expect(calls).toBe(2);
  });

  it('S-39_ denied result is cached and retried hourly, not every poll', async () => {
    let calls = 0;
    const cache = mountCache('acct-denied', () => {
      calls++;
      return Promise.reject('contacts_not_authorized');
    });

    await tick();
    expect(cache.data()).toEqual({ birthdays: [], deniedAccountIds: ['acct-denied'] });
    expect(calls).toBe(1);

    // 5-minute poll tick: no retry, denial is persistent, not transient.
    now += 5 * 60_000;
    await cache.refresh(false);
    expect(calls).toBe(1);

    // After the denied-retry interval the user may have re-granted: retry.
    now += DENIED_RETRY_MS;
    await cache.refresh(false);
    await tick();
    expect(calls).toBe(2);
  });
});
