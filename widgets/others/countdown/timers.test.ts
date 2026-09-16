import { describe, it, expect } from 'vitest';
import { localInputToIso, isoToLocalInput } from './timers.ts';

// Regression for the audit finding "Clearing the datetime-local field silently
// retargets the timer to the current instant and refills the input" [C-55].
// Chrome fires `change` with value === '' when the field is cleared; previously
// localInputToIso('') returned `new Date().toISOString()`, rewriting the target
// to NOW and refilling the just-cleared input mid-edit.
describe('localInputToIso [C-55]', () => {
  it('returns null for an empty (cleared) field instead of retargeting to now', () => {
    expect(localInputToIso('')).toBeNull();
  });

  it('returns null for an unparseable value instead of retargeting to now', () => {
    expect(localInputToIso('not-a-date')).toBeNull();
  });

  it('[C-53] parses a valid local datetime-local value to ISO and round-trips', () => {
    const local = '2030-01-15T10:30';
    const iso = localInputToIso(local);
    expect(iso).not.toBeNull();
    expect(isNaN(new Date(iso as string).getTime())).toBe(false);
    expect(isoToLocalInput(iso as string)).toBe(local);
  });
});
