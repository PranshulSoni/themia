// Birthday data layer for the calendar widget.
//
// The backend enumerates the FULL contact list (paged, up to 100 pages,
// sequential) on every `calendar_get_birthdays` call, while birthdays change
// ~never. So birthdays are served through createCachedFetch with a daily TTL
// [S-38]: mount, navigation and the 5-minute poll all reuse the cached set and
// the contact enumeration runs at most once a day per account set.
//
// Failure semantics [S-38]/[S-39]:
// - Permission failures (`contacts_not_authorized` / `calendar_not_authorized`)
//   are persistent: they are cached as `deniedAccountIds` (so the UI can
//   surface them) and retried hourly instead of on every poll.
// - Transient failures (network, 5xx) throw, so they are never cached as "no
//   birthdays": previously cached data stays visible, createCachedFetch logs
//   the error, and the next poll tick retries.

import { createCachedFetch, type CachedFetch } from '../../../src/createCachedFetch';
import { invoke } from '../../../src/demo/invoke.ts';

export interface Birthday {
  display_name: string;
  month: number;
  day: number;
  year?: number | null;
}

export interface BirthdayCacheData {
  birthdays: Birthday[];
  /** Accounts whose contacts permission is missing (persistent failures). */
  deniedAccountIds: string[];
}

export const BIRTHDAY_TTL_MS = 24 * 60 * 60_000;
export const DENIED_RETRY_MS = 60 * 60_000;

const PERMISSION_ERRORS = new Set(['contacts_not_authorized', 'calendar_not_authorized']);

// Account ids never contain newlines, so '\n' round-trips the id list through
// the cache key (one shared entry per account set, order-insensitive).
const KEY_SEP = '\n';

export function birthdayCacheKey(
  showBirthdays: boolean | undefined,
  accountIds: string[],
): string | null {
  if (showBirthdays === false || !accountIds.length) return null;
  return [...accountIds].sort().join(KEY_SEP);
}

export type BirthdayInvoke = (accountId: string) => Promise<Birthday[]>;

const invokeBirthdaysReal: BirthdayInvoke = (accountId) =>
  invoke('calendar_get_birthdays', { accountId });

export async function fetchBirthdaysForAccounts(
  accountIds: string[],
  invokeBirthdays: BirthdayInvoke = invokeBirthdaysReal,
): Promise<BirthdayCacheData> {
  const results = await Promise.allSettled(accountIds.map((id) => invokeBirthdays(id)));
  const birthdays: Birthday[] = [];
  const deniedAccountIds: string[] = [];
  const transientErrors: string[] = [];
  results.forEach((r, i) => {
    if (r.status === 'fulfilled') {
      birthdays.push(...r.value);
    } else {
      const msg = r.reason instanceof Error ? r.reason.message : String(r.reason);
      if (PERMISSION_ERRORS.has(msg)) deniedAccountIds.push(accountIds[i]);
      else transientErrors.push(`${accountIds[i]}: ${msg}`);
    }
  });
  if (transientErrors.length) throw new Error(transientErrors.join('; '));
  return { birthdays, deniedAccountIds };
}

export function createBirthdayCache(
  key: () => string | null,
  invokeBirthdays?: BirthdayInvoke,
): CachedFetch<BirthdayCacheData> {
  let cache: CachedFetch<BirthdayCacheData> | undefined;
  const created = createCachedFetch<BirthdayCacheData>({
    namespace: 'calendar-birthdays',
    key,
    // The auto-refresh effect fires synchronously inside createCachedFetch,
    // before `cache` is assigned, so that first call falls back to the daily
    // TTL (a persisted denial then tightens to hourly from the next poll on).
    staleMs: () =>
      cache?.data()?.deniedAccountIds.length ? DENIED_RETRY_MS : BIRTHDAY_TTL_MS,
    fetcher: (k) => fetchBirthdaysForAccounts(k.split(KEY_SEP), invokeBirthdays),
  });
  cache = created;
  return created;
}
