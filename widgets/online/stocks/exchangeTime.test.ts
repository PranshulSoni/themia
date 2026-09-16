// Exchange-timezone rendering for the Stocks widget (SPEC.md [S-36]).
//
// The point of these tests is that the OUTPUT DOES NOT DEPEND ON THE MACHINE'S
// ZONE: every case pins an absolute instant and an explicit exchange zone, so a
// run in Zürich, New York or UTC must produce the same strings.
import { describe, it, expect } from 'vitest';
import { chartLabels, formatDateInZone, formatTimeInZone, validZone, zoneAbbrev } from './exchangeTime.ts';

const NY = 'America/New_York';
const OPEN = 1786109400;   // 2026-08-07 09:30 EDT = 13:30 UTC = 15:30 CEST
const CLOSE = 1786132800;  // 2026-08-07 16:00 EDT
const WINTER = 1770474600; // 2026-02-07 09:30 EST (DST off)

describe('validZone', () => {
  it('[S-36] accepts IANA zones and rejects anything else', () => {
    expect(validZone(NY)).toBe(NY);
    expect(validZone('Europe/Berlin')).toBe('Europe/Berlin');
    expect(validZone('Mars/Olympus')).toBeUndefined();
    expect(validZone('EDT')).toBeUndefined();
    expect(validZone(undefined)).toBeUndefined();
    expect(validZone('')).toBeUndefined();
    expect(validZone(null)).toBeUndefined();
  });
});

describe('formatTimeInZone', () => {
  // [S-36] The exchange's clock, not the viewer's.
  it('[S-36] renders the exchange clock regardless of where it runs', () => {
    expect(formatTimeInZone(OPEN, NY, 'en-GB')).toBe('09:30');
    expect(formatTimeInZone(CLOSE, NY, 'en-GB')).toBe('16:00');
    // Same instant, other zones — proves the zone is what drives the output.
    expect(formatTimeInZone(OPEN, 'Europe/Zurich', 'en-GB')).toBe('15:30');
    expect(formatTimeInZone(OPEN, 'UTC', 'en-GB')).toBe('13:30');
    expect(formatTimeInZone(OPEN, 'Asia/Tokyo', 'en-GB')).toBe('22:30');
  });

  it('[S-36] an unusable zone falls back to local time instead of throwing', () => {
    const local = new Date(OPEN * 1000).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
    expect(formatTimeInZone(OPEN, 'Mars/Olympus', 'en-GB')).toBe(local);
    expect(formatTimeInZone(OPEN, undefined, 'en-GB')).toBe(local);
  });
});

describe('zoneAbbrev', () => {
  // Resolved at the instant, so the DST-correct marker is shown.
  it('[S-36] gives the DST-correct short marker for the instant', () => {
    expect(zoneAbbrev(OPEN, NY)).toBe('EDT');
    expect(zoneAbbrev(WINTER, NY)).toBe('EST');
  });

  it('[S-36] empty when there is no usable zone (times are already local)', () => {
    expect(zoneAbbrev(OPEN, undefined)).toBe('');
    expect(zoneAbbrev(OPEN, 'Mars/Olympus')).toBe('');
  });
});

describe('formatDateInZone', () => {
  // Late-evening ET is already the next calendar day in Europe: the date label
  // must follow the exchange, or a 5D chart is off by a day for half the world.
  it('[S-36] dates follow the exchange, not the viewer', () => {
    const lateNy = 1786147200; // 2026-08-07 20:00 EDT = 2026-08-08 02:00 CEST
    expect(formatDateInZone(lateNy, NY, 'en-US')).toBe('Aug 7');
    expect(formatDateInZone(lateNy, 'Europe/Zurich', 'en-US')).toBe('Aug 8');
  });

  it('[S-36] includes the year only when asked', () => {
    expect(formatDateInZone(OPEN, NY, 'en-US', true)).toBe('Aug 7, 2026');
  });
});

describe('chartLabels', () => {
  // [S-36] Labels come from the plotted bars, so they describe the data that is
  // actually on screen rather than a now-minus-range guess.
  it('[S-36] intraday labels are the first and last bar in exchange time', () => {
    const bars = [{ t: OPEN }, { t: OPEN + 3600 }, { t: CLOSE }];
    expect(chartLabels(bars, { tz: NY, intraday: true, locale: 'en-GB' })).toEqual(['09:30', '16:00']);
  });

  it('[S-36] non-intraday labels are dates', () => {
    const bars = [{ t: OPEN }, { t: OPEN + 86_400 * 3 }];
    expect(chartLabels(bars, { tz: NY, intraday: false, locale: 'en-US' })).toEqual(['Aug 7', 'Aug 10']);
  });

  it('[S-36] null when there is nothing to label, so the caller can fall back', () => {
    expect(chartLabels([], { tz: NY, intraday: true })).toBe(null);
    expect(chartLabels([{ t: OPEN }], { tz: NY, intraday: true })).toBe(null);
    expect(chartLabels([{ t: NaN }, { t: OPEN }], { tz: NY, intraday: true })).toBe(null);
  });
});
