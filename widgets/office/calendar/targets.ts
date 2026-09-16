// Which (account, calendar) pairs the Calendar widget fetches, and how each
// maps to a color key. Pure so the selection/default contract ([S-49]/[S-50])
// is unit-tested without mounting the widget.

export interface CalendarTarget {
  accountId: string;
  /** Selected calendar id, or `undefined` for the account's default calendar. */
  calendarId?: string;
}

/**
 * Resolve the fetch targets for the active accounts [S-49].
 *
 * For each account: one target per explicitly-selected calendar id from
 * `calendars[accountId]`. When an account has no selection (absent or empty),
 * it resolves to a single default-calendar target (`calendarId` undefined) —
 * the pre-feature behavior, so an un-configured widget still shows the default
 * calendar and existing configs are unchanged.
 *
 * Persisted config is untrusted: non-string / blank ids are dropped, and
 * duplicate ids within one account collapse to one target.
 */
export function resolveCalendarTargets(
  accountIds: string[],
  calendars: Record<string, string[]> | undefined,
): CalendarTarget[] {
  const out: CalendarTarget[] = [];
  for (const accountId of accountIds) {
    const sel = calendars?.[accountId];
    const ids = Array.isArray(sel)
      ? [...new Set(sel.filter((id): id is string => typeof id === 'string' && id.trim() !== ''))]
      : [];
    if (ids.length) {
      for (const calendarId of ids) out.push({ accountId, calendarId });
    } else {
      out.push({ accountId });
    }
  }
  return out;
}

/**
 * Color key for a fetched calendar [S-51]: its own id when a specific calendar
 * was selected, else the account id. Keying the default calendar by account id
 * keeps a single-calendar user's color stable and lets the legacy per-account
 * override (`accountColors`) migrate forward unchanged.
 */
export function calendarColorKey(accountId: string, calendarId?: string): string {
  return calendarId && calendarId.trim() !== '' ? calendarId : accountId;
}
