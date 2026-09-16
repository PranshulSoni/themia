// [S-49] How many lines a task title wraps to before it is clamped.
//
// This is a config value, so it arrives as an untrusted `unknown` (hand-edited
// config, an older/newer schema, an import) and is narrowed here rather than at
// the point of use — a bad value must degrade to the default, never to a
// zero-height row or an unbounded one.

export const TITLE_LINES_MIN = 1;
export const TITLE_LINES_MAX = 6;

/** Two lines: enough that an ordinary task reads in full without a to-do list
 *  turning into paragraphs. */
export const TITLE_LINES_DEFAULT = 2;

/**
 * Narrow an untrusted `titleLines` config value to a usable line count.
 * Absent / non-numeric / NaN / infinite → the default; a number outside the
 * range is clamped rather than rejected, and a fraction is floored, so a value
 * the config panel could never produce still yields a sane list.
 */
export function clampTitleLines(raw: unknown): number {
  if (typeof raw !== 'number' || !Number.isFinite(raw)) return TITLE_LINES_DEFAULT;
  const n = Math.floor(raw);
  if (n < TITLE_LINES_MIN) return TITLE_LINES_MIN;
  if (n > TITLE_LINES_MAX) return TITLE_LINES_MAX;
  return n;
}
