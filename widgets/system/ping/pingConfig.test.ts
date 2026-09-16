// ping [S-13]: a config panel renders from `props.config` and keeps no local
// mirror of it.
//
// PingConfig used to copy `props.config.host` into two signals at mount:
//
//   const [hostLabel, setHostLabel] = createSignal(props.config.host?.label ?? '');
//   const [hostAddress, setHostAddress] = createSignal(props.config.host?.address ?? '');
//
// and `update()` rewrote `host` from those signals on EVERY change:
//
//   props.onChange({ ...props.config, host: { label: hostLabel().trim(), ... }, [key]: value });
//
// So editing the interval wrote a mount-time snapshot of `host` back over
// whatever `host` had since become — a lost update, and the panel's inputs went
// on showing the stale host. Ping was the only offender; every sibling (e.g.
// GitHubConfig's `repo`) derives its inputs from props directly, and
// ConfigWindow's `keyed` remount (ConfigWindow.tsx:390) was left carrying the
// weight of covering for it.
//
// This is a source scan rather than a mounted-component test, following
// refreshInterval.test.ts: vitest here has no JSX transform, so no config panel
// can be rendered in a unit test today. The scan is coarse but it catches the
// exact habit, across every panel, which is what stops it coming back.

import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';

const WIDGETS = join(process.cwd(), 'widgets');

/** The habit: seeding local reactive state from `props.config`. Catches the
 *  generic-typed form (`createSignal<T>(props.config…)`) and the common
 *  `const config = () => props.config` accessor alias (`createSignal(config()…)`),
 *  both of which slipped the original matcher and let the habit creep back in. */
const MIRRORS_CONFIG = /create(?:Signal|Store)\s*(?:<[^>]*>)?\s*\(\s*[^;]*?(?:props\.config\b|\bconfig\(\))/;

function configPanels(): { file: string; text: string }[] {
  const out: { file: string; text: string }[] = [];
  for (const category of readdirSync(WIDGETS)) {
    const catDir = join(WIDGETS, category);
    if (!statSync(catDir).isDirectory()) continue;
    for (const w of readdirSync(catDir)) {
      const dir = join(catDir, w);
      if (!statSync(dir).isDirectory()) continue;
      for (const f of readdirSync(dir)) {
        if (!/Config\.tsx$/.test(f)) continue;
        out.push({ file: `widgets/${category}/${w}/${f}`, text: readFileSync(join(dir, f), 'utf8') });
      }
    }
  }
  return out;
}

describe('config panels keep no mirror of props.config (ping [S-13])', () => {
  it('finds the config panels to scan (guard against an empty sweep)', () => {
    const panels = configPanels();
    expect(panels.length).toBeGreaterThan(20);
    expect(panels.map(p => p.file)).toContain('widgets/system/ping/PingConfig.tsx');
  });

  it('S-13_ no config panel seeds local state from props.config', () => {
    const offenders = configPanels()
      .filter(p => MIRRORS_CONFIG.test(p.text))
      .map(p => p.file);
    // Was: ['widgets/system/ping/PingConfig.tsx'] — the only one in the repo.
    expect(offenders).toEqual([]);
  });

  it('the detector still recognises the shape it was written for', () => {
    // Pin the matcher against the historical snippet, so the guard above can't
    // quietly degrade into a test that passes because it matches nothing.
    expect(MIRRORS_CONFIG.test(
      "const [hostLabel, setHostLabel] = createSignal(props.config.host?.label ?? '');",
    )).toBe(true);
    expect(MIRRORS_CONFIG.test(
      "const [s, setS] = createStore(props.config.items ?? []);",
    )).toBe(true);
    // Generic-typed signals and the `config()` accessor alias are mirrors too —
    // both forms previously slipped the matcher and reintroduced the habit.
    expect(MIRRORS_CONFIG.test(
      "const [defaultView, setDefaultView] = createSignal<string>(props.config.defaultView ?? 'month');",
    )).toBe(true);
    expect(MIRRORS_CONFIG.test(
      "const [viewMode, setViewMode] = createSignal<ViewMode>(config().viewMode ?? 'list');",
    )).toBe(true);
    // Reading props.config in a derivation is the CORRECT pattern, not a mirror.
    expect(MIRRORS_CONFIG.test(
      "const host = () => props.config.host ?? { label: '', address: '' };",
    )).toBe(false);
    expect(MIRRORS_CONFIG.test(
      "const [open, setOpen] = createSignal(false);",
    )).toBe(false);
    // A transient edit buffer seeded from null (not config) is not a mirror,
    // e.g. OneDriveConfig's debounce `pending` value.
    expect(MIRRORS_CONFIG.test(
      "const [pending, setPending] = createSignal<number | null>(null);",
    )).toBe(false);
  });
});
