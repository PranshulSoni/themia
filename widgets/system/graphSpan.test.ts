// Unit tests for the shared graph-span logic used by every time-series system
// widget (battery, cpu, disk, gpu, net, ping, ram). These pin the two behaviours
// the widgets delegate here: deriving a bounded sample capacity from a span +
// poll interval, and placing samples on a fixed window (newest pinned right, no
// horizontal rescaling as the trace fills).

import { describe, it, expect } from 'vitest';
import {
  GRAPH_SPANS, GRAPH_SPAN_DEFAULTS, MAX_CAPACITY,
  graphSpanSec, windowCapacity, sampleX, formatSpan, spanOptions,
} from './graphSpan.ts';

describe('graphSpanSec — trust-boundary resolution', () => {
  it('passes a finite positive number through unchanged', () => {
    expect(graphSpanSec(120, 60)).toBe(120);
    expect(graphSpanSec(3600, 60)).toBe(3600);
  });

  it('falls back on absent / corrupt / non-positive input', () => {
    expect(graphSpanSec(undefined, 60)).toBe(60);
    expect(graphSpanSec(null, 60)).toBe(60);
    expect(graphSpanSec('300', 60)).toBe(60);
    expect(graphSpanSec(NaN, 60)).toBe(60);
    expect(graphSpanSec(0, 60)).toBe(60);
    expect(graphSpanSec(-5, 60)).toBe(60);
  });
});

describe('windowCapacity — samples per window', () => {
  it('is span / interval, rounded', () => {
    expect(windowCapacity(60, 2)).toBe(30);
    expect(windowCapacity(1200, 30)).toBe(40);
    expect(windowCapacity(300, 10)).toBe(30);
    expect(windowCapacity(180, 5)).toBe(36);
    expect(windowCapacity(50, 3)).toBe(17); // 16.67 -> 17
  });

  it('never drops below 2 (a line needs two points)', () => {
    expect(windowCapacity(60, 60)).toBe(2);
    expect(windowCapacity(30, 60)).toBe(2);
  });

  it('caps at MAX_CAPACITY so an extreme span cannot spin up a huge path', () => {
    expect(windowCapacity(3600, 1)).toBe(MAX_CAPACITY);
    expect(windowCapacity(3600, 2)).toBe(MAX_CAPACITY); // 1800 -> capped 600
  });

  it('falls back on non-finite / non-positive inputs (span 60s, interval 1s)', () => {
    expect(windowCapacity(NaN, 2)).toBe(30);   // span -> 60
    expect(windowCapacity(60, 0)).toBe(60);    // interval -> 1
    expect(windowCapacity(60, NaN)).toBe(60);  // interval -> 1
  });
});

describe('sampleX — fixed window, newest pinned right (no x rescaling)', () => {
  it('places the newest sample at the right edge for any fill level', () => {
    expect(sampleX(1, 2, 40)).toBe(100);   // 2 held
    expect(sampleX(19, 20, 40)).toBe(100); // 20 held
    expect(sampleX(39, 40, 40)).toBe(100); // full
  });

  it('does NOT stretch a partly-filled window: 2 of 40 hug the right edge', () => {
    // Old behaviour spread 2 samples 0..100. Now they occupy one slot at the far
    // right; the rest of the axis is the (still empty) older window.
    const step = 100 / 39;
    expect(sampleX(0, 2, 40)).toBe(+(100 - step).toFixed(2)); // 97.44
    expect(sampleX(1, 2, 40)).toBe(100);
  });

  it('fills the whole width once the window is full', () => {
    expect(sampleX(0, 40, 40)).toBe(0);
    expect(sampleX(39, 40, 40)).toBe(100);
  });

  it('steps left by a constant slot regardless of how many are held', () => {
    // The gap between the two newest samples is the same at 3 held and at 40.
    const gapAt3 = sampleX(2, 3, 40) - sampleX(1, 3, 40);
    const gapAt40 = sampleX(39, 40, 40) - sampleX(38, 40, 40);
    expect(gapAt3).toBeCloseTo(gapAt40, 6);
  });

  it('clamps a would-be negative x (transient over-long buffer) to 0', () => {
    expect(sampleX(0, 45, 40)).toBe(0);
  });

  it('degenerate capacity < 2 is treated as 2', () => {
    expect(sampleX(0, 2, 1)).toBe(0);
    expect(sampleX(1, 2, 1)).toBe(100);
  });
});

describe('formatSpan / spanOptions — dropdown presentation', () => {
  it('formats sub-minute, minute and hour spans', () => {
    expect(formatSpan(30)).toBe('30s');
    expect(formatSpan(60)).toBe('1m');
    expect(formatSpan(120)).toBe('2m');
    expect(formatSpan(1200)).toBe('20m');
    expect(formatSpan(1800)).toBe('30m');
    expect(formatSpan(3600)).toBe('1h');
  });

  it('builds one option per preset with string values', () => {
    const opts = spanOptions();
    expect(opts).toHaveLength(GRAPH_SPANS.length);
    expect(opts[0]).toEqual({ label: '30s', value: '30' });
    expect(opts.at(-1)).toEqual({ label: '1h', value: '3600' });
  });
});

describe('GRAPH_SPAN_DEFAULTS — every default is a real preset', () => {
  it('maps each widget to a value in GRAPH_SPANS', () => {
    for (const [widget, span] of Object.entries(GRAPH_SPAN_DEFAULTS)) {
      expect(GRAPH_SPANS, `${widget} default ${span} must be a preset`)
        .toContain(span as never);
    }
  });
});
