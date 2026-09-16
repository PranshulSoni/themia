// Pure day-label logic for the Weather Forecast widget. [S-05]
//
// Extracted so the label is computed at RENDER time from the current date
// instead of being baked into the cached forecast at fetch time (issue #55):
// persisting the label meant a cache restored on a later day still labelled a
// stale row "Today".

/**
 * Local YYYY-MM-DD for `now`. open-meteo returns daily dates in the location's
 * local time (timezone=auto), so we compare against the local calendar day.
 */
export function localDateStr(now: Date = new Date()): string {
  const m = String(now.getMonth() + 1).padStart(2, '0');
  const d = String(now.getDate()).padStart(2, '0');
  return `${now.getFullYear()}-${m}-${d}`;
}

/** True when the forecast date string is the actual current local day. */
export function isToday(dateStr: string, now: Date = new Date()): boolean {
  return dateStr === localDateStr(now);
}

/**
 * Label for a forecast day, computed against the CURRENT day (not the fetch
 * day): the current local date is `todayLabel`, every other day is its short
 * weekday name localised via `locale`. Pure — the caller injects `locale` and
 * `todayLabel` (from i18n) so this stays importable in the node unit env. [S-05]
 */
export function dayLabel(
  dateStr: string,
  now: Date = new Date(),
  locale: string = 'en',
  todayLabel: string = 'Today',
): string {
  if (isToday(dateStr, now)) return todayLabel;
  const d = new Date(dateStr + 'T00:00:00');
  return d.toLocaleDateString(locale, { weekday: 'short' });
}
