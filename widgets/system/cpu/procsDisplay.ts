// Top-process row display helpers, extracted so they are unit-testable.
//
// [S-25] The backend resolves each top process's version-info FileDescription
// (the "real" name Task Manager shows, e.g. "Google Chrome" for chrome.exe)
// where possible; protected processes and exes without version info come back
// with no desc, so the row falls back to the image name.
// [S-26] `topProcsCount` config picks how many rows render (1..TOP_PROCS_MAX,
// default 3); the backend always returns up to TOP_PROCS_MAX so the slice
// never starves.

export const TOP_PROCS_DEFAULT = 3;
export const TOP_PROCS_MAX = 10;

/** Friendly description when resolvable, else the exe image name ([S-25]). */
export function displayProcName(p: { name: string; desc?: string | null }): string {
  const d = p.desc?.trim();
  return d ? d : p.name;
}

/**
 * Normalise the persisted `topProcsCount` config value ([S-26]). Anything
 * non-numeric (old configs, corrupt values) yields the default rather than a
 * half-working row count.
 */
export function clampProcCount(raw: unknown): number {
  if (typeof raw !== 'number' || !Number.isFinite(raw)) return TOP_PROCS_DEFAULT;
  return Math.min(TOP_PROCS_MAX, Math.max(1, Math.round(raw)));
}
