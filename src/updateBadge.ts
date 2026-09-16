// ---------------------------------------------------------------------------
// Desktop "update available" badge ([S-35]): what the corner pill remembers.
//
// The badge is the only update affordance visible OUTSIDE edit mode, so it must
// not become permanent nagging: dismissing it silences exactly the version that
// was offered. The next release offers a different version string and the badge
// comes back on its own.
// ---------------------------------------------------------------------------

import { createSignal } from 'solid-js';
import { writeShared, onStorageKey } from './sync.ts';

/** localStorage key holding the last version whose badge the user dismissed. */
export const UPDATE_DISMISSED_KEY = 'themia-update-dismissed';

/**
 * Should the corner badge show?
 *
 * Deliberately an EXACT version match, not a semver comparison: the only thing
 * persisted is "the user has already seen this offer". Any other offered
 * version — newer, or an older one after a downgrade — is a fresh offer.
 */
export function shouldShowUpdateBadge(
  offered: string | null | undefined,
  dismissed: string | null | undefined,
): boolean {
  if (!offered) return false;
  return offered !== dismissed;
}

/** Version whose badge was dismissed, or `null` when none was (or it's blank). */
export function readDismissedVersion(): string | null {
  const v = localStorage.getItem(UPDATE_DISMISSED_KEY);
  return v && v.trim() !== '' ? v : null;
}

/**
 * The dismissed version as LIVE state, so every window's badge reacts to a
 * dismissal — including the one the user clicked in.
 *
 * A window never hears its own storage write: `writeShared` mirrors the change
 * to the OTHER windows and `onStorageKey` picks it up there, but the writer has
 * to update itself. Without that half, dismissing the badge silenced it on every
 * monitor EXCEPT the display the click landed on, where it sat until a reload.
 * Same split as the per-monitor view-bar state in `views.ts`.
 */
const [dismissedVersion, setDismissedVersion] = createSignal<string | null>(readDismissedVersion());
export const getDismissedVersion = dismissedVersion;
onStorageKey(UPDATE_DISMISSED_KEY, () => setDismissedVersion(readDismissedVersion()));

/**
 * Remember that `version`'s badge was dismissed. Written through `writeShared`
 * so every other monitor's window drops its badge in the same gesture, rather
 * than each display needing its own dismissal.
 */
export function dismissUpdateVersion(version: string): void {
  writeShared(UPDATE_DISMISSED_KEY, version);
  setDismissedVersion(version);
}
