import { describe, it, expect } from 'vitest';
import {
  metricKeys, canonicalVisible, getMetricDef, resolveMetrics, isMetricVisible,
  resolveVisibleWithLegacy,
  type MetricDef,
} from './metricSet.ts';

// Icon value is irrelevant to the resolution logic; a no-op stand-in keeps the
// fixture free of lucide (and node-importable).
const icon = (() => null) as MetricDef['icon'];
const DEFS: MetricDef[] = [
  { key: 'a', label: () => 'A', icon, defaultOn: true  },
  { key: 'b', label: () => 'B', icon, defaultOn: true  },
  { key: 'c', label: () => 'C', icon, defaultOn: false },
];

describe('metricKeys / canonicalVisible / getMetricDef', () => {
  it('metricKeys preserves definition order', () => {
    expect(metricKeys(DEFS)).toEqual(['a', 'b', 'c']);
  });
  it('canonicalVisible is the defaultOn subset', () => {
    expect(canonicalVisible(DEFS)).toEqual(['a', 'b']);
  });
  it('getMetricDef finds by key, undefined when absent', () => {
    expect(getMetricDef(DEFS, 'b')?.key).toBe('b');
    expect(getMetricDef(DEFS, 'zzz')).toBeUndefined();
  });
});

describe('resolveMetrics', () => {
  it('null/undefined/garbage → canonical default', () => {
    for (const raw of [undefined, null, 'garbage', 42]) {
      expect(resolveMetrics(raw, DEFS).visible).toEqual(['a', 'b']);
    }
  });

  it('array form: keeps listed valid keys, drops unknown ones, order = array', () => {
    expect(resolveMetrics(['c', 'a'], DEFS).visible).toEqual(['c', 'a']);
    expect(resolveMetrics(['c', 'bogus'], DEFS).visible).toEqual(['c']);
  });

  it('object form: explicit booleans win; omitted keys fall back to defaultOn', () => {
    // a explicitly off, b omitted (defaultOn true), c explicitly on.
    const r = resolveMetrics({ a: false, c: true }, DEFS);
    expect(r.visible).toEqual(['b', 'c']); // key order, filtered
    expect(r.hidden).toEqual(['a']);
  });

  it('visible + hidden always partition the full key set', () => {
    const r = resolveMetrics(['a'], DEFS);
    expect([...r.visible, ...r.hidden].sort()).toEqual(['a', 'b', 'c']);
  });
});

describe('isMetricVisible', () => {
  it('reflects default visibility', () => {
    expect(isMetricVisible(undefined, DEFS, 'a')).toBe(true);
    expect(isMetricVisible(undefined, DEFS, 'c')).toBe(false);
  });
  it('reflects explicit overrides', () => {
    expect(isMetricVisible({ c: true }, DEFS, 'c')).toBe(true);
    expect(isMetricVisible(['b'], DEFS, 'a')).toBe(false);
  });
});

describe('resolveVisibleWithLegacy', () => {
  const legacyOff = { key: 'b', enabled: false };
  const legacyOn = { key: 'b', enabled: true };

  it('no visibleMetrics + legacy off drops the legacy key from canonical', () => {
    expect(resolveVisibleWithLegacy(undefined, DEFS, legacyOff)).toEqual(['a']);
    expect(resolveVisibleWithLegacy(null, DEFS, legacyOff)).toEqual(['a']);
  });

  it('no visibleMetrics + legacy on/absent keeps canonical defaults', () => {
    expect(resolveVisibleWithLegacy(undefined, DEFS, legacyOn)).toEqual(['a', 'b']);
    expect(resolveVisibleWithLegacy(undefined, DEFS, { key: 'b', enabled: undefined })).toEqual(['a', 'b']);
    expect(resolveVisibleWithLegacy(undefined, DEFS)).toEqual(['a', 'b']);
  });

  it('explicit visibleMetrics wins outright, legacy toggle ignored', () => {
    // 'b' kept because the modern set lists it, even though legacy says off.
    expect(resolveVisibleWithLegacy(['a', 'b'], DEFS, legacyOff)).toEqual(['a', 'b']);
    // 'b' absent because the modern set omits it, even though legacy says on.
    expect(resolveVisibleWithLegacy(['a'], DEFS, legacyOn)).toEqual(['a']);
  });
});
