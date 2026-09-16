import { describe, it, expect } from 'vitest';
import { resolveCalendarTargets, calendarColorKey } from './targets.ts';

// [S-49] The widget fetches one target per selected calendar, or a single
// default-calendar target when an account has no explicit selection. Pure, so
// the default/selection contract is tested without mounting the widget.
describe('resolveCalendarTargets [S-49]', () => {
  it('S-49_ no selection → one default target per account (calendarId undefined)', () => {
    expect(resolveCalendarTargets(['a', 'b'], undefined)).toEqual([
      { accountId: 'a' },
      { accountId: 'b' },
    ]);
  });

  it('S-49_ an empty selection array falls back to the default target', () => {
    expect(resolveCalendarTargets(['a'], { a: [] })).toEqual([{ accountId: 'a' }]);
  });

  it('S-49_ explicit selection → one target per selected calendar id', () => {
    expect(resolveCalendarTargets(['a'], { a: ['cal-1', 'cal-2'] })).toEqual([
      { accountId: 'a', calendarId: 'cal-1' },
      { accountId: 'a', calendarId: 'cal-2' },
    ]);
  });

  it('S-49_ mixes selected and default accounts', () => {
    expect(resolveCalendarTargets(['a', 'b'], { a: ['cal-1'] })).toEqual([
      { accountId: 'a', calendarId: 'cal-1' },
      { accountId: 'b' },
    ]);
  });

  it('S-49_ drops blank/non-string ids and de-dupes (trust boundary)', () => {
    // A corrupt persisted list must not yield blank or duplicate targets.
    const sel = { a: ['cal-1', '', '  ', 'cal-1', 'cal-2'] as unknown as string[] };
    expect(resolveCalendarTargets(['a'], sel)).toEqual([
      { accountId: 'a', calendarId: 'cal-1' },
      { accountId: 'a', calendarId: 'cal-2' },
    ]);
  });

  it('S-49_ an all-blank selection collapses to the default target', () => {
    expect(resolveCalendarTargets(['a'], { a: ['', '   '] })).toEqual([{ accountId: 'a' }]);
  });
});

describe('calendarColorKey [S-51]', () => {
  it('S-51_ keys a selected calendar by its own id', () => {
    expect(calendarColorKey('acct-a', 'cal-1')).toBe('cal-1');
  });

  it('S-51_ keys the default calendar by the account id (legacy-compatible)', () => {
    expect(calendarColorKey('acct-a')).toBe('acct-a');
    expect(calendarColorKey('acct-a', '')).toBe('acct-a');
    expect(calendarColorKey('acct-a', '   ')).toBe('acct-a');
  });
});
