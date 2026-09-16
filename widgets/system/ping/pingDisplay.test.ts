// Characterization tests for the Ping widget's display helpers. Ping was the
// only system widget without a SPEC.md, so its behavior was unpinned: these
// tests fix what it does TODAY (SPEC.md [S-01], [S-03]..[S-11]) before anyone
// changes it. Where current behavior looks wrong ([S-10] loss), the test pins
// the wrong answer and says so — a characterization test records reality; the
// fix comes later as a deliberate red->green edit of both spec and test.

import { describe, it, expect } from 'vitest';
import {
  buildSpark, fmtMs, pingStats, pingTitle, statusColor, statusText,
} from './pingDisplay.ts';

describe('pingTitle — header fallback chain (ping [S-01])', () => {
  const host = { label: 'Google DNS', address: '8.8.8.8' };

  it('prefers the widget header override', () => {
    expect(pingTitle('My Latency', host)).toBe('My Latency');
  });

  it('falls back to the host label, then the address, then "Ping"', () => {
    expect(pingTitle('', host)).toBe('Google DNS');
    expect(pingTitle('', { label: '', address: '8.8.8.8' })).toBe('8.8.8.8');
    expect(pingTitle('', { label: '', address: '' })).toBe('Ping');
    expect(pingTitle(undefined, undefined)).toBe('Ping');
  });

  it('treats an empty override as absent, not as a blank title', () => {
    // `||`, not `??`: the grid always passes a string, '' when unset ([S-15]).
    expect(pingTitle('', host)).toBe('Google DNS');
  });
});

describe('statusText / statusColor — three states (ping [S-03], [S-04])', () => {
  it('reads Online / Offline / Checking…', () => {
    expect(statusText(true)).toBe('Online');
    expect(statusText(false)).toBe('Offline');
    expect(statusText(null)).toBe('Checking…');
  });

  it('colors from tokens, never a literal (CLAUDE.md: design tokens)', () => {
    expect(statusColor(true)).toBe('var(--success)');
    expect(statusColor(false)).toBe('var(--destructive)');
    expect(statusColor(null)).toBe('var(--subtle-foreground)');
  });
});

describe('fmtMs — latency formatting (ping [S-05])', () => {
  it('renders an em-dash placeholder when unmeasured', () => {
    expect(fmtMs(null)).toBe('—');
    expect(fmtMs(undefined)).toBe('—');
  });

  it('renders whole milliseconds below a second', () => {
    expect(fmtMs(0)).toBe('0ms');
    expect(fmtMs(23)).toBe('23ms');
    expect(fmtMs(999)).toBe('999ms');
  });

  it('switches to one-decimal seconds at 1000ms', () => {
    expect(fmtMs(1000)).toBe('1.0s');
    expect(fmtMs(1500)).toBe('1.5s');
    expect(fmtMs(2049)).toBe('2.0s');
  });
});

describe('buildSpark — latency sparkline (ping [S-07], [S-28])', () => {
  // A full window (capacity === sample count) spans the whole width 0..100.
  it('needs at least two samples', () => {
    expect(buildSpark([], 40)).toEqual({ line: '', area: '' });
    expect(buildSpark([20], 40)).toEqual({ line: '', area: '' });
  });

  it('needs at least one success: an all-failed history has no scale', () => {
    expect(buildSpark([null, null, null], 40)).toEqual({ line: '', area: '' });
  });

  it('spreads a full window across the width and scales to the max over a 94% band', () => {
    const { line } = buildSpark([50, 100], 2);
    // x: 0 -> 100. y: max sample sits at 100-94=6, half the max at 53.
    expect(line).toBe('M 0 53 L 100 6');
  });

  it('floors the scale at 1 so a history of zeroes does not divide by zero', () => {
    const { line } = buildSpark([0, 0], 2);
    expect(line).toBe('M 0 100 L 100 100');
    expect(line).not.toContain('NaN');
  });

  it('plots a failed sample on the baseline instead of breaking the line', () => {
    const { line } = buildSpark([100, null, 100], 3);
    expect(line).toBe('M 0 6 L 50 100 L 100 6');
  });

  it('closes the area path down to the baseline at both ends', () => {
    const { area } = buildSpark([50, 100], 2);
    expect(area).toBe('M 0 100 L 0 53 L 100 6 L 100 100 Z');
  });

  it('rounds coordinates to 2dp', () => {
    const { line } = buildSpark([10, 20, 30], 3);
    expect(line).toBe('M 0 68.67 L 50 37.33 L 100 6');
  });

  it('S-28_ a partly-filled window pins the newest to the right, not stretched', () => {
    // 2 samples of a 40-slot window: newest at x=100, previous one slot left
    // (100 - 100/39 = 97.44). It does NOT spread 0..100 as it used to.
    const { line } = buildSpark([50, 100], 40);
    expect(line).toBe('M 97.44 53 L 100 6');
  });
});

describe('pingStats — aggregates (ping [S-09]..[S-11])', () => {
  it('is all-null before the first success, so the aux rows stay hidden', () => {
    expect(pingStats([])).toEqual({ min: null, avg: null, max: null, jitter: null, loss: 0 });
    expect(pingStats([null, null])).toEqual({ min: null, avg: null, max: null, jitter: null, loss: 0 });
  });

  it('min/avg/max cover successful samples only (ping [S-11])', () => {
    const s = pingStats([10, null, 20, 60]);
    expect(s.min).toBe(10);
    expect(s.max).toBe(60);
    expect(s.avg).toBe(30); // (10+20+60)/3 — the failed sample is not a zero
  });

  it('jitter is the mean absolute step between successes (ping [S-09])', () => {
    // steps: |20-10| = 10, |40-20| = 20 -> mean 15
    expect(pingStats([10, 20, 40]).jitter).toBe(15);
  });

  it('jitter is 0 with exactly one success, not null', () => {
    expect(pingStats([null, 20]).jitter).toBe(0);
  });

  it('jitter bridges a dropped ping rather than treating it as a break', () => {
    // The null between 10 and 40 is dropped: jitter compares two samples that
    // were never adjacent in time, and reads the same as if it were not there.
    expect(pingStats([10, null, 40]).jitter).toBe(pingStats([10, 40]).jitter);
    expect(pingStats([10, null, 40]).jitter).toBe(30);
  });

  it('loss is the share of failed samples across the whole history (ping [S-10])', () => {
    expect(pingStats([10, null, 20, null]).loss).toBe(50);
    expect(pingStats([10, 20]).loss).toBe(0);
    expect(pingStats([10, null, null, null]).loss).toBe(75);
  });

  it('S-10_ KNOWN DEFECT: a host that never answered reports 0% loss, not 100%', () => {
    // The zero-success short-circuit returns loss 0 before the loss line runs,
    // so the widget's most important failure case reads as perfectly healthy.
    // Pinned as CURRENT behavior. Fixing it means 100 here + a spec edit.
    expect(pingStats([null, null, null]).loss).toBe(0);
  });
});

