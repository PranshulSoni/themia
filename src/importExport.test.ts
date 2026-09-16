// @vitest-environment jsdom
//
// Characterization of applyImport: the data-loss-critical settings-import write
// path (extracted from importSettings). Pins: which keys are CLEARED (stale
// layout/design + persistedStore __bak__ backups), which are WRITTEN, the
// visible-screens / default-screen present-vs-absent branches, and the legacy
// bgImage/bgImageFit stripping. A regression here silently corrupts a user's
// whole config on import.

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { applyImport } from './importExport.ts';
import { MONITOR_KEY_SCHEMA_KEY } from './views.ts';
import type { ThemiaExport } from './importValidate.ts';

// views.ts kicks off the #70 key migration off `get_active_monitors` at module
// init. No Tauri bridge under jsdom, so stub the transport: an empty topology
// means "do not migrate, do not write" ([MI-03]), keeping storage under this
// test's control.
vi.mock('./ipc.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./ipc.ts')>()),
  invoke: vi.fn(() => Promise.resolve([])),
}));

function sample(over: Partial<ThemiaExport> = {}): ThemiaExport {
  return {
    version: 3,
    appVersion: '0.0.0-test',
    exportedAt: '2020-01-01T00:00:00Z',
    screens: [{ id: 's1', name: 'Main' }],
    monitorScreenMap: { MON1: 's1' },
    defaultScreenForNewDisplays: null,
    globalDesign: { theme: 'dark' },
    layouts: { 'widget-layout-s1': { widgets: [] } },
    editMode: false,
    autoCheckUpdates: true,
    ...over,
  };
}

beforeEach(() => localStorage.clear());

describe('applyImport: data-loss-critical import write path', () => {
  it('clears stale layout / design / __bak__ keys, preserves unrelated keys', () => {
    localStorage.setItem('widget-layout-stale', '{}');
    localStorage.setItem('widget-design-x', '{}');
    localStorage.setItem('__bak__widget-global-design', '{}');
    localStorage.setItem('keep-me', 'yes'); // unrelated → must survive
    applyImport(sample(), localStorage);
    expect(localStorage.getItem('widget-layout-stale')).toBeNull();
    expect(localStorage.getItem('widget-design-x')).toBeNull();
    expect(localStorage.getItem('__bak__widget-global-design')).toBeNull();
    expect(localStorage.getItem('keep-me')).toBe('yes');
  });

  it('s70_import_re_arms_the_monitor_key_migration: an old export may carry ordinals', () => {
    // A file exported by a pre-#70 build has `\\.\DISPLAYn` keys. If the target
    // machine had already migrated, its stamp would make the boot-time [MI-03]
    // pass skip — and the imported ordinals would key the maps forever, which is
    // #70 walking straight back in through the import path.
    localStorage.setItem(MONITOR_KEY_SCHEMA_KEY, '2');
    applyImport(sample({ monitorScreenMap: { '\\\\.\\DISPLAY1': 's1' } }), localStorage);
    expect(localStorage.getItem(MONITOR_KEY_SCHEMA_KEY)).toBeNull();
  });

  it('s70_monitor_screen_map_round_trips_stable_ids: keys are opaque, never parsed', () => {
    const id = '\\\\?\\DISPLAY#DEL4098#5&1a2b3c&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}';
    applyImport(sample({ monitorScreenMap: { [id]: 's1' } }), localStorage);
    expect(JSON.parse(localStorage.getItem('widget-monitor-screen-map')!)).toEqual({ [id]: 's1' });
  });

  it('writes screens, monitor map, design, flags and migration markers', () => {
    applyImport(sample(), localStorage);
    expect(JSON.parse(localStorage.getItem('widget-screens')!)).toEqual([{ id: 's1', name: 'Main' }]);
    expect(JSON.parse(localStorage.getItem('widget-monitor-screen-map')!)).toEqual({ MON1: 's1' });
    expect(JSON.parse(localStorage.getItem('widget-global-design')!)).toEqual({ theme: 'dark' });
    expect(localStorage.getItem('themia-edit-mode')).toBe('false');
    expect(localStorage.getItem('themia-auto-check-updates')).toBe('true');
    expect(localStorage.getItem('widget-design-migration-v2')).toBe('1');
    expect(localStorage.getItem('widget-design-migration-v3')).toBe('1');
  });

  it('writes each per-view layout and strips legacy bgImage/bgImageFit', () => {
    applyImport(
      sample({ layouts: { 'widget-layout-s1': { widgets: [], bgImage: 'x.png', bgImageFit: 'cover' } } }),
      localStorage,
    );
    const lay = JSON.parse(localStorage.getItem('widget-layout-s1')!);
    expect(lay.widgets).toEqual([]);
    expect('bgImage' in lay).toBe(false);
    expect('bgImageFit' in lay).toBe(false);
  });

  it('visible-screens map: written when non-empty, removed when empty', () => {
    localStorage.setItem('widget-screen-selector-visible-map', 'stale');
    applyImport(sample({ monitorVisibleScreensMap: {} }), localStorage);
    expect(localStorage.getItem('widget-screen-selector-visible-map')).toBeNull();

    applyImport(sample({ monitorVisibleScreensMap: { MON1: ['s1'] } }), localStorage);
    expect(JSON.parse(localStorage.getItem('widget-screen-selector-visible-map')!)).toEqual({ MON1: ['s1'] });
  });

  it('default-screen: written when set, removed when null', () => {
    localStorage.setItem('widget-default-screen-for-new-displays', 'stale');
    applyImport(sample({ defaultScreenForNewDisplays: null }), localStorage);
    expect(localStorage.getItem('widget-default-screen-for-new-displays')).toBeNull();

    applyImport(sample({ defaultScreenForNewDisplays: 's1' }), localStorage);
    expect(localStorage.getItem('widget-default-screen-for-new-displays')).toBe('s1');
  });
});
