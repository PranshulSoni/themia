import type { Component } from 'solid-js';

export interface MetricDef {
  key: string;
  label: () => string;
  icon: Component<{ size?: number; strokeWidth?: number }>;
  defaultOn: boolean;
}

export interface ResolvedMetrics {
  visible: string[];
  hidden: string[];
}

export function metricKeys(defs: MetricDef[]): string[] {
  return defs.map(d => d.key);
}

export function canonicalVisible(defs: MetricDef[]): string[] {
  return defs.filter(d => d.defaultOn).map(d => d.key);
}

export function getMetricDef(defs: MetricDef[], key: string): MetricDef | undefined {
  return defs.find(d => d.key === key);
}

export function resolveMetrics(raw: unknown, defs: MetricDef[]): ResolvedMetrics {
  const keys = metricKeys(defs);
  let visible: string[];
  if (Array.isArray(raw)) {
    visible = raw.filter((k): k is string => typeof k === 'string' && keys.includes(k));
  } else if (raw && typeof raw === 'object') {
    const rec = raw as Record<string, unknown>;
    visible = keys.filter(k => {
      const v = rec[k];
      if (v === undefined) return getMetricDef(defs, k)!.defaultOn;
      return !!v;
    });
  } else {
    visible = canonicalVisible(defs);
  }
  const hidden = keys.filter(k => !visible.includes(k));
  return { visible, hidden };
}

export function isMetricVisible(raw: unknown, defs: MetricDef[], key: string): boolean {
  return resolveMetrics(raw, defs).visible.includes(key);
}

/**
 * Resolve visible metrics while honouring a legacy boolean toggle. When the
 * modern `visibleMetrics` set is absent (undefined/null) and `legacy.enabled`
 * is explicitly `false`, `legacy.key` is dropped from the canonical defaults —
 * matching how a widget that still reads the legacy field renders. Once a
 * `visibleMetrics` set exists it wins outright and the legacy toggle is ignored.
 */
export function resolveVisibleWithLegacy(
  raw: unknown,
  defs: MetricDef[],
  legacy?: { key: string; enabled: boolean | undefined },
): string[] {
  if (raw === undefined || raw === null) {
    const out = canonicalVisible(defs);
    if (legacy && legacy.enabled === false) {
      return out.filter((k) => k !== legacy.key);
    }
    return out;
  }
  return resolveMetrics(raw, defs).visible;
}
