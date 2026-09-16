// Top-process row-count helper for the RAM widget, extracted so it is
// unit-testable ([S-17]).
//
// Mirrors CPU's `clampProcCount` ([S-26]) but caps at 5 rather than 10: the
// shared system-stats backend returns up to 5 memory processes
// (`top_mem_procs` uses `.take(5)`), so requesting more would only starve the
// slice. Anything non-numeric (old configs without the field, corrupt values)
// yields the default rather than a half-working row count — a RAM-local helper
// because CPU's constant `TOP_PROCS_MAX` (10) is the wrong cap here.

export const RAM_TOP_PROCS_DEFAULT = 3;
export const RAM_TOP_PROCS_MAX = 5;

/**
 * Normalise the persisted `topProcsCount` config value for the RAM widget
 * ([S-17]). Non-numeric junk falls back to the default; valid numbers are
 * rounded and clamped to 1..RAM_TOP_PROCS_MAX.
 */
export function clampRamProcCount(raw: unknown): number {
  if (typeof raw !== 'number' || !Number.isFinite(raw)) return RAM_TOP_PROCS_DEFAULT;
  return Math.min(RAM_TOP_PROCS_MAX, Math.max(1, Math.round(raw)));
}
