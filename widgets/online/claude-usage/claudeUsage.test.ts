import { describe, it, expect } from 'vitest';
import {
  utilizationPct,
  usageSeverity,
  resetLabel,
  clampRefreshMinutes,
  usageView,
  buildBars,
  usageDensity,
  labelSize,
  barLabel,
  DEFAULT_REFRESH_MINUTES,
  MIN_REFRESH_MINUTES,
  MAX_REFRESH_MINUTES,
  type ClaudeUsage,
  type UsageWindow,
} from './claudeUsage.ts';

describe('utilizationPct', () => {
  it('passes the endpoint percentage through unscaled', () => {
    expect(utilizationPct(42.5)).toBe(42.5);
    expect(utilizationPct(8)).toBe(8);
  });

  // [U-01] Regression: sub-1% readings used to be read as a 0..1 fraction and
  // multiplied by 100, so an almost-unused account showed a full red bar.
  it('does not scale sub-1% readings up to a full bar', () => {
    expect(utilizationPct(1)).toBe(1);
    expect(utilizationPct(0.62)).toBeCloseTo(0.62);
    expect(utilizationPct(0.425)).toBeCloseTo(0.425);
  });

  it('clamps to [0, 100] and floors junk to 0', () => {
    expect(utilizationPct(150)).toBe(100);
    expect(utilizationPct(-5)).toBe(0);
    expect(utilizationPct(0)).toBe(0);
    expect(utilizationPct(NaN)).toBe(0);
  });
});

describe('usageSeverity', () => {
  it('buckets by threshold', () => {
    expect(usageSeverity(0)).toBe('ok');
    expect(usageSeverity(69.9)).toBe('ok');
    expect(usageSeverity(70)).toBe('warn');
    expect(usageSeverity(89.9)).toBe('warn');
    expect(usageSeverity(90)).toBe('crit');
    expect(usageSeverity(100)).toBe('crit');
  });
});

describe('resetLabel', () => {
  const now = Date.parse('2026-06-14T12:00:00Z');

  it('returns empty for null, past, or unparseable timestamps', () => {
    expect(resetLabel(null, now)).toBe('');
    expect(resetLabel('2026-06-14T11:00:00Z', now)).toBe('');
    expect(resetLabel('not-a-date', now)).toBe('');
  });

  it('formats minutes, hours, and days', () => {
    expect(resetLabel('2026-06-14T12:45:00Z', now)).toBe('resets in 45m');
    expect(resetLabel('2026-06-14T15:20:00Z', now)).toBe('resets in 3h 20m');
    expect(resetLabel('2026-06-16T14:00:00Z', now)).toBe('resets in 2d 2h');
  });

  // [S-18] A narrow widget keeps the duration and drops the words.
  it('drops the prefix when short', () => {
    expect(resetLabel('2026-06-14T12:45:00Z', now, true)).toBe('45m');
    expect(resetLabel('2026-06-14T15:20:00Z', now, true)).toBe('3h 20m');
    expect(resetLabel('2026-06-16T14:00:00Z', now, true)).toBe('2d 2h');
    expect(resetLabel(null, now, true)).toBe('');
  });
});

describe('clampRefreshMinutes', () => {
  it('clamps to range and defaults junk', () => {
    expect(clampRefreshMinutes(1)).toBe(MIN_REFRESH_MINUTES);
    expect(clampRefreshMinutes(999)).toBe(MAX_REFRESH_MINUTES);
    expect(clampRefreshMinutes(15)).toBe(15);
    expect(clampRefreshMinutes(undefined)).toBe(DEFAULT_REFRESH_MINUTES);
  });
});

describe('usageView', () => {
  it('empty only when no account (and not demo)', () => {
    expect(usageView({ hasAccount: false, hasData: false, hasError: false })).toBe('empty');
  });

  it('loaded when data is present, over error/loading', () => {
    expect(usageView({ hasAccount: true, hasData: true, hasError: true })).toBe('loaded');
  });

  it('error when connected with an error and no data', () => {
    expect(usageView({ hasAccount: true, hasData: false, hasError: true })).toBe('error');
  });

  it('loading (not empty) when connected with no data and no error', () => {
    expect(usageView({ hasAccount: true, hasData: false, hasError: false })).toBe('loading');
  });
});

describe('buildBars [S-04]', () => {
  const win = (u: number): UsageWindow => ({ utilization: u, resets_at: null });
  const base: ClaudeUsage = {
    five_hour: win(10),
    seven_day: win(20),
    seven_day_opus: null,
    seven_day_sonnet: null,
  };

  it('always includes the two base windows, no per-model rows when toggles off', () => {
    expect(buildBars(base, false, false).map(b => b.key)).toEqual(['five_hour', 'seven_day']);
  });

  it('adds Opus/Sonnet rows when toggled on, showing a 0% bar when the window is null', () => {
    const bars = buildBars(base, true, true);
    expect(bars.map(b => b.key)).toEqual(['five_hour', 'seven_day', 'opus', 'sonnet']);
    // null window → shown as a 0% bar (never null), not hidden.
    expect(bars.find(b => b.key === 'opus')!.win).toEqual({ utilization: 0, resets_at: null });
    expect(bars.find(b => b.key === 'sonnet')!.win).toEqual({ utilization: 0, resets_at: null });
  });

  it('carries the per-model window when the endpoint provides it', () => {
    const bars = buildBars({ ...base, seven_day_opus: win(55) }, true, false);
    expect(bars.map(b => b.key)).toEqual(['five_hour', 'seven_day', 'opus']);
    expect(bars.find(b => b.key === 'opus')!.win).toEqual(win(55));
  });

  // [S-18] Every row carries all three label widths, and the short forms stay
  // distinct from each other — an abbreviation that collides tells the user
  // nothing about which limit they are looking at.
  it('gives every bar a distinct label at each width', () => {
    const bars = buildBars(base, true, true);
    for (const size of ['full', 'short', 'mini'] as const) {
      const labels = bars.map(b => barLabel(b, size));
      expect(labels.every(l => l.length > 0)).toBe(true);
      expect(new Set(labels).size).toBe(bars.length);
    }
    expect(bars.map(b => barLabel(b, 'mini'))).toEqual(['5h', '7d', 'Opus', 'Son.']);
  });
});

describe('usageDensity [S-17]', () => {
  // The heights the widget actually renders at; a box of exactly this height
  // must still hold the density (>=, not >).
  const FITS = { full: 44, compact: 27, tight: 17 };
  const need = (d: keyof typeof FITS, rows: number, gap: number) =>
    rows * FITS[d] + (rows - 1) * gap;

  it('picks the loosest layout the box can hold', () => {
    expect(usageDensity(need('full', 2, 12), 2)).toBe('full');
    expect(usageDensity(need('full', 4, 12), 4)).toBe('full');
    expect(usageDensity(500, 4)).toBe('full');
  });

  it('drops the reset line, then stacking, then the row heights as it shrinks', () => {
    // Same box, more bars: each extra row costs another layer of detail.
    const box = 135;
    expect(usageDensity(box, 2)).toBe('full');
    expect(usageDensity(box, 4)).toBe('compact');
    expect(usageDensity(box, 6)).toBe('tight');
    expect(usageDensity(box, 7)).toBe('micro');
  });

  it('steps down exactly at the budget, never one row short', () => {
    expect(usageDensity(need('full', 3, 12), 3)).toBe('full');
    expect(usageDensity(need('full', 3, 12) - 1, 3)).toBe('compact');
    expect(usageDensity(need('compact', 3, 8), 3)).toBe('compact');
    expect(usageDensity(need('compact', 3, 8) - 1, 3)).toBe('tight');
    expect(usageDensity(need('tight', 3, 6), 3)).toBe('tight');
    expect(usageDensity(need('tight', 3, 6) - 1, 3)).toBe('micro');
  });

  it('counts the global font scale into the row budget', () => {
    const box = need('full', 3, 12);
    expect(usageDensity(box, 3, 1)).toBe('full');
    // Same box, bigger text: the rows no longer fit as `full`.
    expect(usageDensity(box, 3, 1.4)).toBe('compact');
    // A junk or zero scale must not collapse the budget to the fixed part.
    expect(usageDensity(box, 3, 0)).toBe('full');
    expect(usageDensity(box, 3, NaN)).toBe('full');
  });

  it('defaults to the loosest layout before the first measurement', () => {
    expect(usageDensity(NaN, 2)).toBe('full');
    // No bars yet (loading/empty state): nothing to fit.
    expect(usageDensity(40, 0)).toBe('full');
  });

  // A widget whose header and padding already used the whole box measures 0 —
  // which must NOT read as "not measured yet" and snap back to the tallest
  // layout, the one guaranteed to overflow.
  it('treats a measured empty box as micro, not as unmeasured', () => {
    expect(usageDensity(0, 4)).toBe('micro');
    expect(usageDensity(-5, 4)).toBe('micro');
  });
});

describe('labelSize [S-18]', () => {
  it('shortens the label as the widget narrows', () => {
    expect(labelSize(240, 'full')).toBe('full');
    expect(labelSize(140, 'full')).toBe('short');
    expect(labelSize(70, 'full')).toBe('mini');
  });

  it('needs more width on a one-line row, which also carries bar + percentage', () => {
    // Same widget width: stacked still spells the label out, inline can't.
    expect(labelSize(150, 'compact')).toBe('full');
    expect(labelSize(150, 'tight')).toBe('short');
    expect(labelSize(100, 'micro')).toBe('mini');
  });

  it('scales the thresholds with the global font scale', () => {
    expect(labelSize(200, 'full', 1)).toBe('full');
    expect(labelSize(200, 'full', 1.5)).toBe('short');
  });

  it('defaults to the full label before the first measurement', () => {
    expect(labelSize(NaN, 'full')).toBe('full');
    expect(labelSize(NaN, 'tight')).toBe('full');
    // …but a measured empty box is a real, very small widget.
    expect(labelSize(0, 'full')).toBe('mini');
  });
});
