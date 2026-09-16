// Trading-session logic for the Stocks widget (SPEC.md [S-33]..[S-35], #100).
import { describe, it, expect } from 'vitest';
import {
  barSegments, barsOf, extendedQuoteIn, extendedQuotesOf, isExtendedPhase, marketPhase, sessionsOf,
  type Bar, type TradingSessions,
} from './marketSession.ts';

// Epoch seconds for a real NYSE session: 2026-08-07, America/New_York (EDT).
const PRE_OPEN = 1786089600;   // 04:00
const OPEN = 1786109400;       // 09:30
const CLOSE = 1786132800;      // 16:00
const POST_END = 1786147200;   // 20:00
const ms = (sec: number) => sec * 1000;

const PERIOD = {
  pre: { start: PRE_OPEN, end: OPEN },
  regular: { start: OPEN, end: CLOSE },
  post: { start: CLOSE, end: POST_END },
};

describe('sessionsOf', () => {
  // [S-33] Real Yahoo v8 chart payloads carry the session bounds here.
  it('[S-33] extracts pre/regular/post from currentTradingPeriod', () => {
    expect(sessionsOf(PERIOD)).toEqual({
      pre: { start: PRE_OPEN, end: OPEN },
      regular: { start: OPEN, end: CLOSE },
      post: { start: CLOSE, end: POST_END },
    });
  });

  // [S-33] Absent or malformed bounds must not be smuggled through as zeros.
  it('[S-33] drops missing, non-numeric or inverted windows', () => {
    expect(sessionsOf(undefined)).toEqual({});
    expect(sessionsOf({})).toEqual({});
    expect(sessionsOf({ regular: {} })).toEqual({});
    expect(sessionsOf({ regular: { start: OPEN } })).toEqual({});
    expect(sessionsOf({ regular: { start: OPEN, end: null } })).toEqual({});
    expect(sessionsOf({ regular: { start: NaN, end: CLOSE } })).toEqual({});
    expect(sessionsOf({ regular: { start: CLOSE, end: OPEN } })).toEqual({});
  });

  // A broken extended window must not cost us the regular one.
  it('[S-33] validates each window independently', () => {
    expect(sessionsOf({ regular: PERIOD.regular, post: { start: CLOSE } }))
      .toEqual({ regular: { start: OPEN, end: CLOSE } });
  });
});

describe('marketPhase', () => {
  const sessions = sessionsOf(PERIOD);

  it('[S-33] open inside the regular window, at both edges', () => {
    expect(marketPhase(sessions, ms(OPEN))).toBe('open');
    expect(marketPhase(sessions, ms(OPEN + 3600))).toBe('open');
    expect(marketPhase(sessions, ms(CLOSE - 1))).toBe('open');
  });

  // [S-34] The extended windows are named, not lumped in with "closed".
  it('[S-34] pre before the opening bell, post after the closing bell', () => {
    expect(marketPhase(sessions, ms(PRE_OPEN))).toBe('pre');
    expect(marketPhase(sessions, ms(OPEN - 1))).toBe('pre');
    expect(marketPhase(sessions, ms(CLOSE))).toBe('post');
    expect(marketPhase(sessions, ms(POST_END - 1))).toBe('post');
  });

  it('[S-33] closed outside every window', () => {
    expect(marketPhase(sessions, ms(PRE_OPEN - 1))).toBe('closed');
    expect(marketPhase(sessions, ms(POST_END))).toBe('closed');
    expect(marketPhase(sessions, ms(CLOSE + 86_400 * 2))).toBe('closed');
  });

  // A venue with no extended trading: outside regular hours it is simply shut.
  it('[S-33] regular-only schedule is closed outside the session', () => {
    const regularOnly = sessionsOf({ regular: PERIOD.regular });
    expect(marketPhase(regularOnly, ms(OPEN + 60))).toBe('open');
    expect(marketPhase(regularOnly, ms(CLOSE + 60))).toBe('closed');
  });

  // #100: the widget compared `meta.marketState === 'REGULAR'`, a field the v8
  // chart API never returns, so an open US market rendered "Market closed".
  // Unknown must stay unknown — the badge only shows on a proven phase.
  it('[S-33] unknown (never closed) when the schedule is missing or junk', () => {
    expect(marketPhase(undefined, ms(OPEN + 60))).toBe('unknown');
    expect(marketPhase({}, ms(OPEN + 60))).toBe('unknown');
    expect(marketPhase({ regular: { start: NaN, end: CLOSE } } as never, ms(OPEN + 60))).toBe('unknown');
    // Shape restored from the persisted ticker cache, written by an older build.
    expect(marketPhase({ pre: undefined } as TradingSessions, ms(OPEN + 60))).toBe('unknown');
  });

  // Bounds are exchange-resolved absolute instants, so the phase is the same
  // for every viewer on earth — no local-time arithmetic anywhere.
  it('[S-36] a non-US exchange resolves from the same absolute instants', () => {
    const xetra = sessionsOf({ regular: { start: 1786086000, end: 1786116600 } }); // 09:00–17:30 CEST
    expect(marketPhase(xetra, ms(1786100000))).toBe('open');
    expect(marketPhase(xetra, ms(1786120000))).toBe('closed');
    // Same instant, one venue open and the other not: 15:00 CEST = 09:00 EDT.
    expect(marketPhase(sessions, ms(1786100000))).toBe('pre');
  });

  it('[S-34] isExtendedPhase names exactly the two extended windows', () => {
    expect(isExtendedPhase('pre')).toBe(true);
    expect(isExtendedPhase('post')).toBe(true);
    expect(isExtendedPhase('open')).toBe(false);
    expect(isExtendedPhase('closed')).toBe(false);
    expect(isExtendedPhase('unknown')).toBe(false);
  });
});

describe('barsOf', () => {
  // Yahoo pads illiquid extended-hours minutes with nulls; dropping a close
  // without its timestamp would shear the two arrays out of alignment.
  it('[S-34] zips timestamps with closes and drops holes as pairs', () => {
    expect(barsOf([1, 2, 3, 4], [10, null, 30, undefined])).toEqual([
      { t: 1, c: 10 }, { t: 3, c: 30 },
    ]);
  });

  it('[S-34] tolerates missing or non-array inputs', () => {
    expect(barsOf(undefined, [1, 2])).toEqual([]);
    expect(barsOf([1, 2], undefined)).toEqual([]);
    expect(barsOf('nope', 'nope')).toEqual([]);
    expect(barsOf([1, 2], [NaN, 5])).toEqual([{ t: 2, c: 5 }]);
  });
});

describe('extendedQuoteIn', () => {
  const bars: Bar[] = [
    { t: PRE_OPEN + 300, c: 98 },
    { t: OPEN + 300, c: 100 },
    { t: CLOSE - 300, c: 110 },
    { t: CLOSE + 300, c: 111 },
    { t: CLOSE + 900, c: 112.2 },
  ];

  // [S-34] The last print in the window, measured off the regular close.
  it('[S-34] takes the last print in the window vs the regular close', () => {
    const q = extendedQuoteIn(bars, { start: CLOSE, end: POST_END }, 110);
    expect(q?.price).toBe(112.2);
    expect(q?.at).toBe(CLOSE + 900);
    expect(q?.changePct).toBeCloseTo(2, 10);
  });

  it('[S-34] pre-market measures off the same (previous) regular close', () => {
    const q = extendedQuoteIn(bars, { start: PRE_OPEN, end: OPEN }, 100);
    expect(q?.price).toBe(98);
    expect(q?.changePct).toBeCloseTo(-2, 10);
  });

  it('[S-34] undefined when the window is empty, absent, or has no baseline', () => {
    expect(extendedQuoteIn([], { start: CLOSE, end: POST_END }, 110)).toBeUndefined();
    expect(extendedQuoteIn(bars, undefined, 110)).toBeUndefined();
    expect(extendedQuoteIn(bars, { start: CLOSE, end: POST_END }, undefined)).toBeUndefined();
    expect(extendedQuoteIn(bars, { start: CLOSE, end: POST_END }, 0)).toBeUndefined();
    // Regular-session bars must not leak into an extended window.
    expect(extendedQuoteIn(bars.slice(1, 3), { start: CLOSE, end: POST_END }, 110)).toBeUndefined();
  });

  it('[S-34] extendedQuotesOf reports both windows from one pass', () => {
    const q = extendedQuotesOf(bars, sessionsOf(PERIOD), 110);
    expect(q.pre?.price).toBe(98);
    expect(q.post?.price).toBe(112.2);
  });
});

describe('barSegments', () => {
  const sessions = sessionsOf(PERIOD);

  // [S-35] The chart draws extended runs dashed, so they have to be found.
  it('[S-35] splits a full extended-hours day into pre / regular / post', () => {
    const bars: Bar[] = [
      { t: PRE_OPEN + 60, c: 1 },
      { t: OPEN + 60, c: 2 },
      { t: CLOSE - 60, c: 3 },
      { t: CLOSE + 60, c: 4 },
    ];
    expect(barSegments(bars, sessions)).toEqual([
      { from: 0, to: 1, extended: true },
      { from: 1, to: 3, extended: false },
      { from: 3, to: 4, extended: true },
    ]);
  });

  it('[S-35] a regular-only series is one non-extended run', () => {
    const bars: Bar[] = [{ t: OPEN + 60, c: 1 }, { t: CLOSE - 60, c: 2 }];
    expect(barSegments(bars, sessions)).toEqual([{ from: 0, to: 2, extended: false }]);
  });

  // Without a schedule we cannot claim any bar is extended.
  it('[S-35] no regular window → everything is one regular run', () => {
    const bars: Bar[] = [{ t: 1, c: 1 }, { t: 2, c: 2 }];
    expect(barSegments(bars, {})).toEqual([{ from: 0, to: 2, extended: false }]);
    expect(barSegments([], sessions)).toEqual([]);
  });
});
