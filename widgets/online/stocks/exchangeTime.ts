/**
 * Rendering instants in the *exchange's* timezone.
 *
 * Session bounds and chart bars are absolute instants (epoch seconds), so all
 * the session logic in `marketSession.ts` is timezone-free. Turning one into a
 * wall-clock string is the only step that needs a zone, and the right zone is
 * the exchange's (`meta.exchangeTimezoneName`), not the user's: a Zürich user
 * watching AAPL wants to read "09:30–16:00" with an ET marker, not "15:30" in
 * their own zone, and a NYSE half-day must show its real 13:00 close.
 *
 * Every entry point falls back to the user's local zone when the payload
 * carries no usable zone, so a missing field degrades instead of throwing.
 */

/** Zones already proven (in)valid, so a bad symbol doesn't re-throw per render. */
const zoneCache = new Map<string, boolean>();

/**
 * Return `tz` when the runtime accepts it as an IANA zone, else undefined
 * (meaning: use the local zone).
 */
export function validZone(tz: string | undefined | null): string | undefined {
  if (!tz || typeof tz !== 'string') return undefined;
  const cached = zoneCache.get(tz);
  if (cached !== undefined) return cached ? tz : undefined;
  let ok: boolean;
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: tz });
    ok = true;
  } catch {
    // RangeError: unknown zone. Treated as "no zone", never fatal.
    ok = false;
  }
  zoneCache.set(tz, ok);
  return ok ? tz : undefined;
}

function fmt(epochSec: number, opts: Intl.DateTimeFormatOptions, locale?: string): string {
  const date = new Date(epochSec * 1000);
  try {
    return new Intl.DateTimeFormat(locale, opts).format(date);
  } catch {
    // A zone that passed validZone can still be rejected by an odd option mix;
    // drop the zone rather than lose the label.
    const { timeZone: _dropped, ...rest } = opts;
    return new Intl.DateTimeFormat(locale, rest).format(date);
  }
}

/** Clock time ("16:00" / "4:00 PM") at `epochSec` in `tz`, else locally. */
export function formatTimeInZone(epochSec: number, tz?: string, locale?: string): string {
  return fmt(epochSec, { hour: '2-digit', minute: '2-digit', timeZone: validZone(tz) }, locale);
}

/** Calendar date at `epochSec` in `tz`, else locally. */
export function formatDateInZone(epochSec: number, tz?: string, locale?: string, withYear = false): string {
  return fmt(epochSec, {
    month: 'short',
    day: 'numeric',
    year: withYear ? 'numeric' : undefined,
    timeZone: validZone(tz),
  }, locale);
}

/**
 * Short zone marker for `tz` at `epochSec` ("EDT", "GMT+2"), so a user outside
 * the exchange's zone can tell what the times refer to. DST-correct because it
 * is resolved at that instant, not at "now". Empty string when there is no
 * usable zone — the times are then the user's own and need no marker.
 */
export function zoneAbbrev(epochSec: number, tz?: string, locale = 'en-US'): string {
  const zone = validZone(tz);
  if (!zone) return '';
  try {
    const parts = new Intl.DateTimeFormat(locale, { timeZone: zone, timeZoneName: 'short' })
      .formatToParts(new Date(epochSec * 1000));
    return parts.find((p) => p.type === 'timeZoneName')?.value ?? '';
  } catch {
    return '';
  }
}

/**
 * Start/end labels for a sparkline, taken from the plotted bars themselves.
 *
 * The labels used to be guessed as `now - rangeDays … now` in the viewer's
 * zone, which drifted from the data on every intraday range (a 1D chart is one
 * ~6.5h session, not the last 24h) and was plain wrong for anyone outside the
 * exchange's zone. Reading the first and last bar keeps the axis honest for
 * regular and extended-hours charts alike.
 */
export function chartLabels(
  bars: { t: number }[],
  opts: { tz?: string; intraday: boolean; withYear?: boolean; locale?: string },
): [string, string] | null {
  if (bars.length < 2) return null;
  const first = bars[0].t;
  const last = bars[bars.length - 1].t;
  if (!Number.isFinite(first) || !Number.isFinite(last)) return null;
  const render = (sec: number) => opts.intraday
    ? formatTimeInZone(sec, opts.tz, opts.locale)
    : formatDateInZone(sec, opts.tz, opts.locale, opts.withYear);
  return [render(first), render(last)];
}
