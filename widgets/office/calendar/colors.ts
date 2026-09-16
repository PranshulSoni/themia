// Per-account event colors. Extracted from Calendar.tsx so the palette + the
// override→fallback contract ([S-08]/[S-47]) can be unit-tested without mounting
// the widget.

/**
 * Fixed 6-color account palette [S-08]. Auto-assigned by the account's selection
 * index when the user hasn't overridden a source's color. These hex literals are
 * the widget's own event-chip palette (documented in SPEC), not the design
 * accent — so they intentionally live as literals here rather than as tokens.
 */
export const ACCT_COLORS = ['#60cdff', '#ff9660', '#a8e6a3', '#ffcc70', '#c8b3f7', '#ff8bab'] as const;

/** Default color for the account at the given selection index (wraps past 6). */
export function acctColor(idx: number): string {
  const n = ACCT_COLORS.length;
  return ACCT_COLORS[((idx % n) + n) % n];
}

const HEX_RE = /^#[0-9a-fA-F]{6}$/;

/** True only for a well-formed `#rrggbb` string. Guards the config trust
 *  boundary: a corrupt persisted override must never leak into a style. */
export function isHexColor(v: unknown): v is string {
  return typeof v === 'string' && HEX_RE.test(v);
}

/**
 * Resolve the event color for a source key [S-47]/[S-51]: a valid user override
 * for the key wins; otherwise fall back to the index-palette default [S-08].
 * The key is a calendar id when a specific calendar is selected, else the
 * account id (see `calendarColorKey`). Absent or corrupt overrides fall back
 * cleanly (persisted config is untrusted).
 */
export function resolveCalendarColor(
  key: string,
  idx: number,
  overrides: Record<string, string> | undefined,
): string {
  const o = overrides?.[key];
  if (isHexColor(o)) return o;
  return acctColor(idx);
}
