// Display-row assembly for the volume mixer (themia-releases#112).
//
// Kept out of Mixer.tsx so the ordering/filtering rules — the part that decides
// what a user actually sees — are unit-testable without a DOM, WASAPI or COM.

/** One backend row (`widget_mixer::MixerApp`), verbatim over IPC. */
export interface MixerSession {
  key: string;
  name: string;
  path: string | null;
  level: number;
  muted: boolean;
  active: boolean;
  is_system: boolean;
  sessions: number;
}

/** Key of the synthetic master row. Disjoint from every backend key space (exe
 *  path / session instance id / `system-sounds`), so the setters can dispatch
 *  on it without a parallel "is this the master" flag ([S-08]). */
export const MASTER_KEY = '__master__';

export interface MixerRowOptions {
  /** Pin the endpoint's master volume as the first row ([S-08]). */
  showMaster: boolean;
  /** Include the Windows "System sounds" session ([S-09]). */
  showSystemSounds: boolean;
  /** Only apps rendering audio right now, rather than every app that holds a
   *  session ([S-10]). */
  onlyActive: boolean;
}

/** The default-output-device master level, as the volume widget reads it. */
export interface MasterState {
  level: number;
  muted: boolean;
}

export function masterRow(master: MasterState): MixerSession {
  return {
    key: MASTER_KEY,
    name: '',
    path: null,
    level: master.level,
    muted: master.muted,
    // The endpoint is never "idle" the way a session is; marking it active
    // keeps it out of the [S-10] filter, which is only about apps.
    active: true,
    is_system: false,
    sessions: 0,
  };
}

/**
 * Final display list: the optional master row, then the app rows the current
 * options admit. The backend has already grouped and ordered the app rows
 * ([S-03]/[S-06]); this only decides which of them are shown, so filtering can
 * react to a config change without waiting for the next poll.
 */
export function buildRows(
  sessions: MixerSession[],
  master: MasterState | null,
  o: MixerRowOptions,
): MixerSession[] {
  const rows: MixerSession[] = [];
  if (o.showMaster && master) rows.push(masterRow(master));
  for (const s of sessions) {
    if (s.is_system && !o.showSystemSounds) continue;
    if (o.onlyActive && !s.active && !s.is_system) continue;
    rows.push(s);
  }
  return rows;
}

/** Whole-percent level for display; a mute reads as 0 without destroying the
 *  stored level, so unmuting restores where the slider was ([S-12]). */
export function displayPct(row: MixerSession): number {
  if (row.muted) return 0;
  return Math.round(clampLevel(row.level) * 100);
}

/** 0..1, NaN-proof: the level crosses an IPC boundary and feeds a CSS gradient
 *  stop, where a NaN silently blanks the whole slider fill. */
export function clampLevel(level: number): number {
  if (!Number.isFinite(level)) return 0;
  return Math.min(1, Math.max(0, level));
}

/**
 * Resolve a persisted boolean setting.
 *
 * Absent and corrupt both fall back to the widget default, and neither is
 * COERCED: a hand-edited or imported config holding the string `"false"` is
 * truthy, so `!!raw` would silently flip the setting on (CLAUDE.md: validate at
 * every trust boundary; distinguish absent from corrupt).
 */
export function flag(raw: unknown, fallback: boolean): boolean {
  return typeof raw === 'boolean' ? raw : fallback;
}

/**
 * Levels the user has dragged to but the backend has not confirmed yet ([S-11]).
 *
 * An override is kept only while it still says something: it is dropped once a
 * poll reports the same level (Windows has applied the write) and once its row
 * disappears (the app exited). Purely value-driven — nothing here waits on a
 * timer for the write to "probably" have landed.
 */
export function reconcileOverrides(
  overrides: Record<string, number>,
  rows: MixerSession[],
  epsilon = 0.005,
): Record<string, number> {
  const byKey = new Map(rows.map((r) => [r.key, r]));
  const out: Record<string, number> = {};
  for (const [key, level] of Object.entries(overrides)) {
    const row = byKey.get(key);
    if (!row) continue;
    if (Math.abs(clampLevel(row.level) - level) < epsilon) continue;
    out[key] = level;
  }
  return out;
}

/** Paint the rows with any unconfirmed level the user dragged to ([S-11]).
 *  Moving a muted row above zero also unmutes it, matching the write the
 *  slider sends ([S-12]). */
export function applyOverrides(
  rows: MixerSession[],
  overrides: Record<string, number>,
): MixerSession[] {
  return rows.map((r) => {
    const level = overrides[r.key];
    if (level === undefined) return r;
    return { ...r, level, muted: r.muted && level <= 0 };
  });
}

/** Distinct exe paths of the rows on screen, for one batched icon fetch. */
export function iconPaths(rows: MixerSession[]): string[] {
  const seen = new Set<string>();
  for (const r of rows) {
    if (r.path) seen.add(r.path);
  }
  return [...seen];
}
