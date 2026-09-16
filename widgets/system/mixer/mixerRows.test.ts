import { describe, it, expect } from 'vitest';
import {
  buildRows, displayPct, clampLevel, iconPaths, masterRow, MASTER_KEY,
  flag, reconcileOverrides, applyOverrides,
  type MixerSession,
} from './mixerRows.ts';

function session(over: Partial<MixerSession> = {}): MixerSession {
  return {
    key: 'c:\\chrome.exe',
    name: 'Google Chrome',
    path: 'C:\\Chrome\\chrome.exe',
    level: 0.5,
    muted: false,
    active: true,
    is_system: false,
    sessions: 1,
    ...over,
  };
}

const ALL = { showMaster: true, showSystemSounds: true, onlyActive: false };

describe('buildRows', () => {
  it('[S-08] pins the master row first when enabled', () => {
    const rows = buildRows([session()], { level: 0.8, muted: false }, ALL);
    expect(rows.map(r => r.key)).toEqual([MASTER_KEY, 'c:\\chrome.exe']);
    expect(rows[0].level).toBe(0.8);
  });

  it('[S-08] omits the master row when disabled, and when it never loaded', () => {
    expect(buildRows([session()], { level: 0.8, muted: false }, { ...ALL, showMaster: false }))
      .toHaveLength(1);
    // No endpoint / call still in flight: no placeholder row, no crash.
    expect(buildRows([session()], null, ALL)).toHaveLength(1);
  });

  it('[S-09] drops the system-sounds row unless it is switched on', () => {
    const rows = [session(), session({ key: 'system-sounds', name: '', is_system: true, path: null })];
    expect(buildRows(rows, null, { ...ALL, showSystemSounds: false }).map(r => r.key))
      .toEqual(['c:\\chrome.exe']);
    expect(buildRows(rows, null, ALL)).toHaveLength(2);
  });

  it('[S-10] onlyActive hides apps that hold a session but are not playing', () => {
    const rows = [
      session({ key: 'a', active: true }),
      session({ key: 'b', active: false }),
    ];
    expect(buildRows(rows, null, { ...ALL, onlyActive: true }).map(r => r.key)).toEqual(['a']);
  });

  it('[S-10] onlyActive never hides system sounds, which is idle by nature', () => {
    // Its session sits Inactive between notification chimes; filtering it on
    // activity would make the row blink in and out.
    const rows = [session({ key: 'system-sounds', is_system: true, active: false })];
    expect(buildRows(rows, null, { ...ALL, onlyActive: true })).toHaveLength(1);
  });

  it('[S-10] the master row survives onlyActive', () => {
    const rows = buildRows([], { level: 0.3, muted: true }, { ...ALL, onlyActive: true });
    expect(rows.map(r => r.key)).toEqual([MASTER_KEY]);
  });

  it('keeps the backend order of the app rows untouched ([S-06] owns it)', () => {
    const rows = [session({ key: 'z', name: 'Zoom' }), session({ key: 'a', name: 'Arc' })];
    expect(buildRows(rows, null, ALL).map(r => r.key)).toEqual(['z', 'a']);
  });
});

describe('displayPct / clampLevel', () => {
  it('[S-12] a muted row reads 0% but keeps its stored level', () => {
    const row = session({ level: 0.62, muted: true });
    expect(displayPct(row)).toBe(0);
    expect(row.level).toBe(0.62);
  });

  it('rounds to whole percent', () => {
    expect(displayPct(session({ level: 0.615 }))).toBe(62);
    expect(displayPct(session({ level: 1 }))).toBe(100);
  });

  it('survives a junk level from the IPC boundary instead of emitting NaN%', () => {
    for (const junk of [NaN, Infinity, -Infinity, -0.5, 3]) {
      const pct = displayPct(session({ level: junk }));
      expect(Number.isFinite(pct)).toBe(true);
      expect(pct).toBeGreaterThanOrEqual(0);
      expect(pct).toBeLessThanOrEqual(100);
    }
    expect(clampLevel(NaN)).toBe(0);
    expect(clampLevel(2)).toBe(1);
  });
});

describe('flag', () => {
  it('returns a real boolean unchanged', () => {
    expect(flag(true, false)).toBe(true);
    expect(flag(false, true)).toBe(false);
  });

  it('falls back for absent AND corrupt, without coercing', () => {
    // `!!'false'` is true — the bug this guards against.
    for (const junk of [undefined, null, 'false', 'true', 0, 1, {}, []]) {
      expect(flag(junk, true)).toBe(true);
      expect(flag(junk, false)).toBe(false);
    }
  });
});

describe('reconcileOverrides / applyOverrides [S-11]', () => {
  it('keeps an override the backend has not caught up with yet', () => {
    const rows = [session({ key: 'a', level: 0.2 })];
    expect(reconcileOverrides({ a: 0.8 }, rows)).toEqual({ a: 0.8 });
    expect(applyOverrides(rows, { a: 0.8 })[0].level).toBe(0.8);
  });

  it('drops an override once the poll confirms it', () => {
    const rows = [session({ key: 'a', level: 0.8 })];
    expect(reconcileOverrides({ a: 0.8 }, rows)).toEqual({});
  });

  it('drops an override whose app has exited, instead of leaking it forever', () => {
    expect(reconcileOverrides({ gone: 0.8 }, [session({ key: 'a' })])).toEqual({});
  });

  it('dragging a muted row above zero shows it unmuted, matching the write sent', () => {
    const rows = [session({ key: 'a', level: 0, muted: true })];
    const shown = applyOverrides(rows, { a: 0.4 });
    expect(shown[0].muted).toBe(false);
    expect(displayPct(shown[0])).toBe(40);
    // Dragging back to zero leaves the mute alone.
    expect(applyOverrides(rows, { a: 0 })[0].muted).toBe(true);
  });

  it('leaves rows without an override untouched', () => {
    const rows = [session({ key: 'a', level: 0.3 }), session({ key: 'b', level: 0.6 })];
    const shown = applyOverrides(rows, { a: 0.9 });
    expect(shown.map(r => r.level)).toEqual([0.9, 0.6]);
  });
});

describe('iconPaths', () => {
  it('deduplicates and skips rows without an executable', () => {
    const rows = [
      session({ path: 'C:\\a.exe' }),
      session({ path: 'C:\\a.exe' }),
      session({ path: null }),
      masterRow({ level: 1, muted: false }),
    ];
    expect(iconPaths(rows)).toEqual(['C:\\a.exe']);
  });
});
