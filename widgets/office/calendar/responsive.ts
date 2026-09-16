// Calendar responsive helpers: pure functions so the sizing math is unit-
// testable without a DOM. The widget feeds measured pixel sizes in; CSS
// container queries handle the rest (typography scale-up / detail hiding).

/** Approx. pixel budget the day-number row eats at the top of a month cell. */
const MONTH_DAY_NUM_PX = 16;
/** Approx. pixel height of one event chip including its row gap. */
const MONTH_CHIP_PX = 14;

/**
 * How many event/birthday chips fit in a single month-view day cell of the
 * given pixel height. Bigger cells (taller widget) show more chips ("more
 * verbose"); tiny cells fold down to a single chip + a "+N" overflow marker.
 * Always at least 1 and never more than 6 (past which a cell is just noise).
 */
export function monthChipCapacity(cellHeightPx: number): number {
  if (!Number.isFinite(cellHeightPx) || cellHeightPx <= 0) return 2;
  const avail = cellHeightPx - MONTH_DAY_NUM_PX;
  const n = Math.floor(avail / MONTH_CHIP_PX);
  return Math.max(1, Math.min(6, n));
}

/**
 * Color for a compact month-view event dot [S-43]. When the widget is too short
 * to fit chips, days render up to three of these dots instead of a bare number.
 * Birthdays get the warning hue; multi-account events keep their per-account
 * palette color [S-08]; single-account (and colorless) events use the accent.
 *
 * Returns a CSS color string (either a palette hex or a `var(--…)` token), used
 * directly as the dot's `background`.
 */
export function eventDotColor(
  kind: 'bd' | 'ev',
  accountColor: string | undefined,
  multiAccount: boolean,
): string {
  if (kind === 'bd') return 'var(--warning)';
  if (multiAccount && accountColor) return accountColor;
  return 'var(--primary)';
}
