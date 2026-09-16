import { describe, it, expect } from 'vitest';
import { dayLabel, isToday, localDateStr } from './dayLabel.ts';

// Regression for issue #55: day labels were computed at FETCH time (idx === 0
// => "Today") and persisted into the localStorage forecast cache ([S-26]). A
// cache restored on a LATER day then labelled a stale (past) first row "Today".
// Labels must be computed at RENDER time against the actual current date. [S-05]
describe('weather dayLabel [S-05]', () => {
  const now = new Date('2026-07-19T09:00:00'); // a Sunday

  it('55_ labels the actual current local day "Today" regardless of array position', () => {
    expect(dayLabel('2026-07-19', now)).toBe('Today');
  });

  it('55_ does NOT label a stale (past) first day "Today"', () => {
    // Fetched on the 17th (idx 0 => "Today" under the old code), restored on the 19th.
    expect(dayLabel('2026-07-17', now)).not.toBe('Today');
    expect(dayLabel('2026-07-17', now)).toBe('Fri');
  });

  it('55_ uses three-letter weekday names for non-today days', () => {
    expect(dayLabel('2026-07-20', now)).toBe('Mon');
    expect(dayLabel('2026-07-25', now)).toBe('Sat');
  });

  it('55_ isToday matches only the current local date', () => {
    expect(isToday('2026-07-19', now)).toBe(true);
    expect(isToday('2026-07-18', now)).toBe(false);
  });

  it('55_ localDateStr formats local Y-M-D zero-padded', () => {
    expect(localDateStr(new Date('2026-01-05T00:00:00'))).toBe('2026-01-05');
  });
});
