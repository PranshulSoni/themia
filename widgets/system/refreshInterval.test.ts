// Regression: the system widgets' refresh-interval default disagreed with
// itself, and the config panel lied about how often the widget actually polls.
//
// The default lived in THREE places per widget — the registry `defaultConfig`,
// the widget's own `props.interval ?? N` fallback, and the config panel's
// `props.config.interval ?? N` — and they drifted apart:
//
//   battery    SPEC [S-10] 10s   registry 10   widget 10   config  2  <-- lies
//   bluetooth  SPEC [S-08] 10s   registry 10   widget 10   config  2  <-- lies
//   disk       SPEC [S-13]  5s   registry  5   widget  2   config  2  <-- both
//   ping       (no SPEC)         registry 30   -           config  2  <-- lies
//
// The visible bug: for any config persisted before `interval` existed, the
// stored blob has no `interval`, so the battery panel displayed "2 s" while
// BatteryWidget polled every 10 s. Disk was wrong by construction — a fresh
// disk widget stores 5 and polls 5, but a legacy one polls 2, against its spec.
//
// The fix is structural rather than four edited literals: ONE resolver both the
// registry and the two consumers call, so panel and widget cannot disagree.

import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { REFRESH_DEFAULTS, refreshInterval, type SystemWidgetId } from './refreshInterval.ts';

describe('refreshInterval defaults match each widget SPEC', () => {
  // Each entry cites the SPEC bullet that fixes the number.
  it.each([
    ['battery', 10],   // battery/SPEC.md [S-10]
    ['bluetooth', 10], // bluetooth/SPEC.md [S-08]
    ['cpu', 2],        // cpu/SPEC.md [S-11]
    ['disk', 5],       // disk/SPEC.md [S-13]
    ['gpu', 2],        // gpu/SPEC.md [S-14]
    ['mixer', 2],      // mixer/SPEC.md [S-07]
    ['net', 2],        // net/SPEC.md [S-09]
    ['ram', 2],        // ram/SPEC.md [S-09]
    ['ping', 30],      // ping/SPEC.md [S-16]
  ] as [SystemWidgetId, number][])('an absent interval resolves to the %s default (%i s)', (w, expected) => {
    expect(REFRESH_DEFAULTS[w]).toBe(expected);
    // The bug: battery/bluetooth/disk/ping each returned 2 here.
    expect(refreshInterval(undefined, w)).toBe(expected);
  });

  it('distinguishes absent from corrupt: junk falls back to the default, loudly typed', () => {
    // CLAUDE.md: "Distinguish absent from corrupt". Both resolve to the default,
    // but neither may produce NaN and poison max(1, interval) into a hot loop.
    for (const junk of [null, 'oops', NaN, Infinity, {}, [], '10']) {
      expect(refreshInterval(junk, 'cpu')).toBe(2);
    }
  });

  it('passes a stored value through unchanged, including out-of-range ones', () => {
    expect(refreshInterval(7, 'cpu')).toBe(7);
    expect(refreshInterval(1, 'battery')).toBe(1);
    // NOT clamped to the NumberStepper's 1..60: a user with a hand-edited or
    // imported 300s interval keeps polling every 300s. Clamping here would
    // silently speed their widget up 5x, which is functionality lost.
    expect(refreshInterval(300, 'net')).toBe(300);
  });
});

describe('refreshInterval is the single source of truth (anti-drift)', () => {
  const SYS = join(process.cwd(), 'widgets', 'system');

  function sources(): { file: string; text: string }[] {
    const out: { file: string; text: string }[] = [];
    for (const w of readdirSync(SYS)) {
      const dir = join(SYS, w);
      if (!statSync(dir).isDirectory()) continue;
      for (const f of readdirSync(dir)) {
        if (!/\.tsx?$/.test(f) || f.endsWith('.test.ts')) continue;
        out.push({ file: `widgets/system/${w}/${f}`, text: readFileSync(join(dir, f), 'utf8') });
      }
    }
    return out;
  }

  it('no widget resolves an interval default with a bare numeric literal', () => {
    // This is what allowed the three copies to drift silently. Matches
    // `interval ?? 2`, `props.config.interval ?? 10`, `interval: 5`, etc.
    const offenders: string[] = [];
    for (const { file, text } of sources()) {
      text.split('\n').forEach((line, i) => {
        if (/\binterval\s*\?\?\s*\d/.test(line) || /\binterval:\s*\d/.test(line)) {
          offenders.push(`${file}:${i + 1} ${line.trim()}`);
        }
      });
    }
    // Was: 8 registry literals + 8 widget/config fallbacks across the group.
    expect(offenders).toEqual([]);
  });

  it('every widget that has a refresh interval imports the shared resolver', () => {
    const missing: string[] = [];
    for (const w of Object.keys(REFRESH_DEFAULTS) as SystemWidgetId[]) {
      const files = sources().filter(s => s.file.startsWith(`widgets/system/${w}/`));
      const usesResolver = files.some(s => /refreshInterval|REFRESH_DEFAULTS/.test(s.text));
      if (!usesResolver) missing.push(w);
    }
    expect(missing).toEqual([]);
  });
});
