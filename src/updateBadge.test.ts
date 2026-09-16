// @vitest-environment jsdom
import { describe, it, expect, beforeEach } from 'vitest';
import {
  UPDATE_DISMISSED_KEY,
  shouldShowUpdateBadge,
  readDismissedVersion,
  dismissUpdateVersion,
  getDismissedVersion,
} from './updateBadge.ts';

describe('[S-35] update badge visibility', () => {
  beforeEach(() => localStorage.clear());

  it('shows when an update is offered and nothing was dismissed', () => {
    expect(shouldShowUpdateBadge('1.2.3', null)).toBe(true);
  });

  it('stays hidden when no update is offered', () => {
    expect(shouldShowUpdateBadge(null, null)).toBe(false);
    expect(shouldShowUpdateBadge(undefined, '1.2.3')).toBe(false);
  });

  it('hides the exact version the user dismissed', () => {
    expect(shouldShowUpdateBadge('1.2.3', '1.2.3')).toBe(false);
  });

  it('comes back for the NEXT release after a dismissal', () => {
    expect(shouldShowUpdateBadge('1.3.0', '1.2.3')).toBe(true);
  });

  it('dismissal round-trips through localStorage', () => {
    expect(readDismissedVersion()).toBe(null);
    dismissUpdateVersion('1.2.3');
    expect(localStorage.getItem(UPDATE_DISMISSED_KEY)).toBe('1.2.3');
    expect(readDismissedVersion()).toBe('1.2.3');
    expect(shouldShowUpdateBadge('1.2.3', readDismissedVersion())).toBe(false);
  });

  // Regression [S-35]: a window never hears its OWN storage write, so a
  // dismissal that only went through `writeShared` left the badge on screen in
  // the very display the user clicked in — it dropped everywhere else.
  it('dismissal updates the live state in the window that made it', () => {
    // A version no other case uses: the signal is module state, so this must
    // prove the transition, not inherit it.
    dismissUpdateVersion('9.9.9');
    expect(getDismissedVersion()).toBe('9.9.9');
    expect(shouldShowUpdateBadge('9.9.9', getDismissedVersion())).toBe(false);
    expect(shouldShowUpdateBadge('9.10.0', getDismissedVersion())).toBe(true);
  });

  it('treats a blank stored value as "nothing dismissed"', () => {
    localStorage.setItem(UPDATE_DISMISSED_KEY, '   ');
    expect(readDismissedVersion()).toBe(null);
    expect(shouldShowUpdateBadge('1.2.3', readDismissedVersion())).toBe(true);
  });
});
