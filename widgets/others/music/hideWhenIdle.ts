// Now Playing's "hide when idle" rule ([S-20]), kept as a pure module so the
// three-way decision is unit-testable without a Solid render or a live GSMTC
// session — the widget itself only wires this to its snapshot signal.

/** Closed set of hide modes. Persisted config is validated against it, never
 *  trusted raw (`resolveHideWhenIdle`). */
export type HideWhenIdle = 'never' | 'noSession' | 'notPlaying';

export const HIDE_WHEN_IDLE_MODES = ['never', 'noSession', 'notPlaying'] as const;

/** Resolve a persisted / imported / synced config value to a mode. Anything
 *  unrecognised — absent, corrupt, or a mode written by a newer version — falls
 *  back to `never`, i.e. the VISIBLE state: a widget the user can still see is
 *  one they can still fix. */
export function resolveHideWhenIdle(raw: unknown): HideWhenIdle {
  return (HIDE_WHEN_IDLE_MODES as readonly unknown[]).includes(raw)
    ? (raw as HideWhenIdle)
    : 'never';
}

/** The slice of `NowPlayingInfo` the decision reads. `null` means Windows
 *  reports no media session at all (nothing has claimed the transport controls). */
export interface HidePlaybackState {
  is_playing: boolean;
}

/**
 * Whether the widget should paint nothing at all.
 *
 * `editMode` ALWAYS wins: a tile the user cannot see is a tile they cannot
 * move, resize, configure or remove, so hiding is suspended for the whole time
 * the grid is editable — that's the one way back to a widget that has hidden
 * itself.
 */
export function shouldHideMusic(
  mode: HideWhenIdle,
  info: HidePlaybackState | null,
  editMode: boolean,
): boolean {
  if (editMode) return false;
  if (mode === 'never') return false;
  // No session at all reads as idle under BOTH hiding modes.
  if (!info) return true;
  return mode === 'notPlaying' && !info.is_playing;
}
