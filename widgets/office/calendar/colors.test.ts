import { describe, it, expect } from 'vitest';
import { ACCT_COLORS, acctColor, isHexColor, resolveCalendarColor } from './colors.ts';

// [S-08]/[S-47] Per-account event colors. The fixed 6-color index palette is the
// default (auto-assigned by the account's selection index [S-08]); the user may
// override any source's color, persisted as `accountColors[accountId]` [S-47].
// Resolution is a pure function so the override→fallback contract is unit-tested
// without mounting the widget (issue #57b).
describe('acctColor index palette [S-08]', () => {
  it('S-08_ maps index 0..5 onto the fixed palette', () => {
    expect(acctColor(0)).toBe(ACCT_COLORS[0]);
    expect(acctColor(1)).toBe(ACCT_COLORS[1]);
    expect(acctColor(5)).toBe(ACCT_COLORS[5]);
  });

  it('S-08_ wraps past the palette length', () => {
    expect(acctColor(6)).toBe(ACCT_COLORS[0]);
    expect(acctColor(7)).toBe(ACCT_COLORS[1]);
  });

  it('S-08_ tolerates a negative index without going out of range', () => {
    expect(ACCT_COLORS).toContain(acctColor(-1));
  });
});

describe('isHexColor [S-47]', () => {
  it('S-47_ accepts a 6-digit #rrggbb string', () => {
    expect(isHexColor('#60cdff')).toBe(true);
    expect(isHexColor('#ABCDEF')).toBe(true);
  });

  it('S-47_ rejects malformed / non-string values', () => {
    expect(isHexColor('#abc')).toBe(false); // 3-digit shorthand not accepted
    expect(isHexColor('red')).toBe(false);
    expect(isHexColor('#gggggg')).toBe(false);
    expect(isHexColor('60cdff')).toBe(false); // missing '#'
    expect(isHexColor('')).toBe(false);
    expect(isHexColor(undefined)).toBe(false);
    expect(isHexColor(null)).toBe(false);
    expect(isHexColor(123)).toBe(false);
  });
});

describe('resolveCalendarColor [S-47]/[S-51]', () => {
  it('S-51_ a valid user override for the key wins over the index palette', () => {
    expect(resolveCalendarColor('cal-1', 0, { 'cal-1': '#123456' })).toBe('#123456');
  });

  it('S-51_ falls back to the index palette when no override is set', () => {
    expect(resolveCalendarColor('cal-1', 1, {})).toBe(acctColor(1));
    expect(resolveCalendarColor('cal-1', 3, undefined)).toBe(acctColor(3));
  });

  it('S-51_ falls back to the palette when the override is corrupt (trust boundary)', () => {
    // Persisted config is untrusted: a garbage override must not leak through.
    expect(resolveCalendarColor('cal-1', 2, { 'cal-1': 'not-a-color' })).toBe(acctColor(2));
    expect(resolveCalendarColor('cal-1', 0, { 'cal-1': '' })).toBe(acctColor(0));
  });

  it('S-51_ an override for a different key does not affect this one', () => {
    expect(resolveCalendarColor('cal-1', 4, { 'cal-2': '#000000' })).toBe(acctColor(4));
  });
});
