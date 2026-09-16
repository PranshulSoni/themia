// @vitest-environment jsdom
//
// Characterization of the per-monitor map loader: the single guard all 8
// per-monitor view-bar maps (view assignment, position, size, icon-only,
// hidden, visible-views, …) load through. A corrupt scalar/array or a
// wrong-typed entry must never reach Object.keys/entries downstream (it would
// set a bogus CSS class or a NaN position). Runs under jsdom for localStorage.
//
// Plus the #70 monitor-identity migration ([MI-01]/[MI-03]): the per-monitor
// maps used to be keyed by the GDI ordinal `\\.\DISPLAYn`, which Windows
// reassigns across reboots, so the saved bytes survived but designated a
// different physical panel. The `s70_*` tests below mirror the Rust-side names.

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import {
  loadObjectMap, createPerMonitorMap,
  isLegacyDeviceName, buildDeviceNameToId, migrateMonitorKeys,
  migrateMonitorKeyStorage, canInternMonitorKey, monitorLabel,
  viewReorderIndices, pruneVisibleViewsMap,
  initForMonitor, getCurrentMonitorId, monitorIdentityIsResolved,
  MONITOR_KEY_SCHEMA_KEY, PHANTOM_MONITOR_KEY,
} from './views.ts';
import { invoke } from './ipc.ts';
import type { MonitorDescriptor } from './ipc.ts';

// The migration runs once at module init off `get_active_monitors`. Under jsdom
// there is no Tauri bridge, so stub the transport: it resolves to an EMPTY
// topology, which per [MI-03] means "do not migrate, do not write" — leaving
// every test below in full control of storage.
vi.mock('./ipc.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./ipc.ts')>()),
  invoke: vi.fn(() => Promise.resolve([])),
}));

const isString = (v: unknown): v is string => typeof v === 'string';
const isBool = (v: unknown): v is boolean => typeof v === 'boolean';

// Real-world shapes: an ordinal (OS-assigned, unstable) vs a device interface
// path (carries the panel's hardware ids, survives reboots).
const ORD1 = '\\\\.\\DISPLAY1';
const ORD2 = '\\\\.\\DISPLAY2';
const ORD3 = '\\\\.\\DISPLAY3';
const ID_A = '\\\\?\\DISPLAY#DEL4098#5&1a2b3c&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}';
const ID_B = '\\\\?\\DISPLAY#GSM5B09#5&9f8e7d&0&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}';

function mon(id: string, deviceName: string, primary = false): MonitorDescriptor {
  return { id, deviceName, primary, x: 0, y: 0, width: 1920, height: 1032 };
}

beforeEach(() => localStorage.clear());

describe('views: loadObjectMap (per-monitor map guard)', () => {
  it('absent key → empty map', () => {
    expect(loadObjectMap('m', isString)).toEqual({});
  });

  it('corrupt JSON → empty map (never throws)', () => {
    localStorage.setItem('m', '{bad json');
    expect(loadObjectMap('m', isString)).toEqual({});
  });

  it('a non-object (array or scalar) → empty map', () => {
    localStorage.setItem('m', JSON.stringify([1, 2, 3]));
    expect(loadObjectMap('m', isString)).toEqual({});
    localStorage.setItem('m', JSON.stringify(42));
    expect(loadObjectMap('m', isString)).toEqual({});
    localStorage.setItem('m', JSON.stringify(null));
    expect(loadObjectMap('m', isString)).toEqual({});
  });

  it('drops entries whose value fails the guard, keeps the valid ones', () => {
    localStorage.setItem(
      'm',
      JSON.stringify({ mon1: 'view-a', mon2: 123, mon3: 'view-b', mon4: null, mon5: { x: 1 } }),
    );
    expect(loadObjectMap('m', isString)).toEqual({ mon1: 'view-a', mon3: 'view-b' });
  });

  it('applies the value guard per map type (boolean map)', () => {
    localStorage.setItem('m', JSON.stringify({ a: true, b: 'x', c: false, d: 0 }));
    expect(loadObjectMap('m', isBool)).toEqual({ a: true, c: false });
  });
});

describe('views: createPerMonitorMap (B8 factory)', () => {
  const KEY = 'pm-test-map';

  it('absent key → empty map; save round-trips to storage AND the signal', () => {
    const m = createPerMonitorMap(KEY, isString);
    expect(m.getMap()).toEqual({});
    m.save({ mon1: 'view-a' });
    expect(m.getMap()).toEqual({ mon1: 'view-a' });
    expect(JSON.parse(localStorage.getItem(KEY)!)).toEqual({ mon1: 'view-a' });
  });

  it('setForMonitor merges into the existing persisted map', () => {
    localStorage.setItem(KEY, JSON.stringify({ mon1: 'view-a' }));
    const m = createPerMonitorMap(KEY, isString);
    m.setForMonitor('mon2', 'view-b');
    expect(m.getMap()).toEqual({ mon1: 'view-a', mon2: 'view-b' });
    expect(JSON.parse(localStorage.getItem(KEY)!)).toEqual({ mon1: 'view-a', mon2: 'view-b' });
  });

  it('syncFromStorage refreshes the signal from an out-of-band write WITHOUT re-persisting', () => {
    const m = createPerMonitorMap(KEY, isString);
    localStorage.setItem(KEY, JSON.stringify({ mon3: 'view-c' })); // another window wrote
    const got = m.syncFromStorage();
    expect(got).toEqual({ mon3: 'view-c' });
    expect(m.getMap()).toEqual({ mon3: 'view-c' });
  });

  it('runs the onSave side effect after every save (e.g. mirror to Rust)', () => {
    const onSave = vi.fn();
    const m = createPerMonitorMap(KEY, isString, onSave);
    m.save({ x: 'y' });
    expect(onSave).toHaveBeenCalledTimes(1);
    expect(onSave).toHaveBeenCalledWith({ x: 'y' });
  });

  it('drops guard-failing entries on load (via loadObjectMap)', () => {
    localStorage.setItem(KEY, JSON.stringify({ ok: 'a', bad: 123 }));
    const m = createPerMonitorMap(KEY, isString);
    expect(m.getMap()).toEqual({ ok: 'a' });
  });
});

// ---------------------------------------------------------------------------
// #70: monitor identity migration
// ---------------------------------------------------------------------------

describe('views: legacy key shape [MI-03] (#70)', () => {
  it('s70_recognizes_the_ordinal_key_shape: only `\\\\.\\DISPLAYn` is legacy', () => {
    expect(isLegacyDeviceName(ORD1)).toBe(true);
    expect(isLegacyDeviceName('\\\\.\\display7')).toBe(true); // case-insensitive
    expect(isLegacyDeviceName(ID_A)).toBe(false);
    expect(isLegacyDeviceName(PHANTOM_MONITOR_KEY)).toBe(false);
    expect(isLegacyDeviceName('')).toBe(false);
  });

  it('builds the deviceName → id lookup off the live topology', () => {
    const lookup = buildDeviceNameToId([mon(ID_A, ORD1, true), mon(ID_B, ORD2)]);
    expect(lookup.get(ORD1)).toBe(ID_A);
    expect(lookup.get(ORD2)).toBe(ID_B);
    expect(lookup.get(ORD3)).toBeUndefined();
  });
});

describe('views: migrateMonitorKeys [MI-01][MI-03] (#70)', () => {
  const lookup = () => buildDeviceNameToId([mon(ID_A, ORD1, true), mon(ID_B, ORD2)]);

  it('s70_migrates_ordinal_keys_to_stable_ids: re-keys and keeps every value', () => {
    const got = migrateMonitorKeys({ [ORD1]: 'view-a', [ORD2]: 'view-b' }, lookup());
    expect(got).not.toBeNull();
    expect(got!.map).toEqual({ [ID_A]: 'view-a', [ID_B]: 'view-b' });
    expect(got!.dropped).toEqual([]);
    expect(got!.changed).toBe(true);
  });

  it('s70_stable_ids_pass_through_untouched: idempotent, no rewrite on a second run', () => {
    const first = migrateMonitorKeys({ [ORD1]: 'view-a' }, lookup())!;
    const second = migrateMonitorKeys(first.map, lookup())!;
    expect(second.map).toEqual(first.map);
    expect(second.changed).toBe(false);
    expect(second.dropped).toEqual([]);
  });

  it('s70_stable_id_of_an_unplugged_panel_survives: an id still names that exact panel', () => {
    // ID_B is not in the live topology (panel unplugged right now). Its id is
    // still meaningful, so its state must be kept.
    const only1 = buildDeviceNameToId([mon(ID_A, ORD1, true)]);
    const got = migrateMonitorKeys({ [ID_B]: 'view-b' }, only1)!;
    expect(got.map).toEqual({ [ID_B]: 'view-b' });
    expect(got.changed).toBe(false);
  });

  it('s70_drops_unresolvable_ordinals: a bare ordinal for a gone panel is discarded', () => {
    // Deliberate data loss, documented in [MI-03]: keeping it is not neutral, it
    // IS the bug — it would go on disabling whichever panel inherits ORD3.
    const got = migrateMonitorKeys({ [ORD1]: 'view-a', [ORD3]: 'view-c' }, lookup())!;
    expect(got.map).toEqual({ [ID_A]: 'view-a' });
    expect(got.dropped).toEqual([ORD3]);
    expect(got.changed).toBe(true);
  });

  it('s70_empty_monitor_list_does_not_migrate: returns null rather than wiping', () => {
    // Reachable, not theoretical: the autostart LogonTrigger has no delay
    // ([AS-01]), so the first enumeration can land before any panel is up.
    expect(migrateMonitorKeys({ [ORD1]: 'view-a' }, new Map())).toBeNull();
  });

  it('s70_mixed_legacy_and_stable: each key follows its own rule in one pass', () => {
    const got = migrateMonitorKeys(
      { [ORD1]: 'view-a', [ID_B]: 'view-b', [ORD3]: 'view-c' },
      lookup(),
    )!;
    expect(got.map).toEqual({ [ID_A]: 'view-a', [ID_B]: 'view-b' });
    expect(got.dropped).toEqual([ORD3]);
  });

  it('s70_collision_dedupe_prefers_the_stable_entry: never merges two keys silently', () => {
    // A legacy ordinal AND the stable id it resolves to both present: the stable
    // entry is authoritative (it was written by the migrated path).
    const got = migrateMonitorKeys({ [ID_A]: 'stable-wins', [ORD1]: 'legacy-loses' }, lookup())!;
    expect(got.map).toEqual({ [ID_A]: 'stable-wins' });
    expect(got.changed).toBe(true);
  });

  it('s70_ordinal_that_is_its_own_id_survives: the [MI-02] virtual-display fallback', () => {
    // An indirect/RDP adapter with no DeviceID falls back to the ordinal AS its
    // id, so that ordinal is a live key and must resolve to itself, never drop.
    const fallback = buildDeviceNameToId([mon(ORD2, ORD2)]);
    const got = migrateMonitorKeys({ [ORD2]: 'view-x' }, fallback)!;
    expect(got.map).toEqual({ [ORD2]: 'view-x' });
    expect(got.dropped).toEqual([]);
    expect(got.changed).toBe(false);
  });
});

describe('views: migrateMonitorKeyStorage — all 8 maps [MI-03] (#70)', () => {
  const MONITORS = [mon(ID_A, ORD1, true), mon(ID_B, ORD2)];

  // Every per-monitor map, with a legal value for its guard.
  const SEED: Record<string, unknown> = {
    'widget-monitor-screen-map':             { [ORD1]: 'view-a' },
    'widget-screen-selector-position-map':   { [ORD1]: 'top-right' },
    'widget-screen-selector-size-map':       { [ORD1]: 'large' },
    'widget-screen-selector-icon-only-map':  { [ORD1]: true },
    'widget-screen-selector-hidden-map':     { [ORD1]: true },
    'widget-screen-selector-visible-map':    { [ORD1]: ['view-a'] },
    'widget-monitor-disabled-map':           { [ORD1]: true },
    'widget-monitor-scale-map':              { [ORD1]: 1.25 },
  };
  const seedAll = () => {
    for (const [k, v] of Object.entries(SEED)) localStorage.setItem(k, JSON.stringify(v));
  };

  it('s70_migrates_every_per_monitor_map_in_storage: no map left on the ordinal', () => {
    seedAll();
    expect(migrateMonitorKeyStorage(MONITORS)).toBe(true);
    for (const [k, v] of Object.entries(SEED)) {
      const got = JSON.parse(localStorage.getItem(k)!) as Record<string, unknown>;
      expect(Object.keys(got), `${k} must be re-keyed`).toEqual([ID_A]);
      expect(got[ID_A]).toEqual(Object.values(v as Record<string, unknown>)[0]);
    }
  });

  it('s70_empty_monitor_list_does_not_migrate: not one byte written', () => {
    seedAll();
    const before = Object.fromEntries(
      Object.keys(SEED).map(k => [k, localStorage.getItem(k)]),
    );
    expect(migrateMonitorKeyStorage([])).toBe(false);
    for (const k of Object.keys(SEED)) expect(localStorage.getItem(k)).toBe(before[k]);
    expect(localStorage.getItem(MONITOR_KEY_SCHEMA_KEY)).toBeNull();
  });

  it('s70_migration_is_run_once: stamps a schema marker and is a no-op after', () => {
    seedAll();
    migrateMonitorKeyStorage(MONITORS);
    expect(localStorage.getItem(MONITOR_KEY_SCHEMA_KEY)).toBe('2');
    // A second pass over already-stable keys changes nothing.
    const after = localStorage.getItem('widget-monitor-screen-map');
    expect(migrateMonitorKeyStorage(MONITORS)).toBe(true);
    expect(localStorage.getItem('widget-monitor-screen-map')).toBe(after);
  });

  it('s70_leaves_untouched_maps_alone: an absent map is not created', () => {
    localStorage.setItem('widget-monitor-screen-map', JSON.stringify({ [ORD1]: 'view-a' }));
    migrateMonitorKeyStorage(MONITORS);
    expect(localStorage.getItem('widget-monitor-scale-map')).toBeNull();
  });
});

describe('views: monitorLabel [MI-01] (#70)', () => {
  const MONITORS = [mon(ID_A, ORD1, true), mon(ID_B, ORD3)];

  it('s70_label_derives_from_device_name_not_key: the key is no longer readable', () => {
    // The whole point: ID_B's key says nothing, but the live topology says it is
    // currently \\.\DISPLAY3.
    expect(monitorLabel(ID_B, MONITORS)).toBe('Display 3');
  });

  it('s70_label_never_shows_the_raw_id: an unknown id gets a generic label', () => {
    const label = monitorLabel(ID_B, []);
    expect(label).not.toContain('DISPLAY#');
    expect(label).toBe('Display');
  });

  it('s70_label_marks_the_primary_display: uses the MONITORINFOF_PRIMARY flag', () => {
    expect(monitorLabel(ID_A, MONITORS)).toBe('Display 1 (Primary)');
    expect(monitorLabel(ID_B, MONITORS)).not.toContain('Primary');
  });

  it('s70_label_falls_back_to_an_ordinal_id: the [MI-02] virtual-display case', () => {
    // id === deviceName for an adapter with no DeviceID; still label it sanely
    // even when it is not in the passed-in list.
    expect(monitorLabel(ORD2, [])).toBe('Display 2');
  });
});

describe('views: canInternMonitorKey [DW-07] (#70)', () => {
  const MONITORS = [mon(ID_A, ORD1, true)];

  it('s70_does_not_intern_the_fallback_phantom_key: no write-back for the DW-07 ghost', () => {
    expect(canInternMonitorKey(PHANTOM_MONITOR_KEY, MONITORS)).toBe(false);
    expect(canInternMonitorKey(PHANTOM_MONITOR_KEY, [])).toBe(false);
    expect(canInternMonitorKey('', MONITORS)).toBe(false);
  });

  it('s70_does_not_intern_a_dead_ordinal_when_the_topology_is_known', () => {
    expect(canInternMonitorKey(ORD3, MONITORS)).toBe(false);
    expect(canInternMonitorKey(ID_B, MONITORS)).toBe(false);
  });

  it('s70_interns_a_live_id', () => {
    expect(canInternMonitorKey(ID_A, MONITORS)).toBe(true);
  });

  it('s70_degrades_on_an_empty_topology: trusts the id rather than blocking boot', () => {
    // An empty enumeration must not make a real window forget its own monitor.
    expect(canInternMonitorKey(ID_A, [])).toBe(true);
  });
});

describe('views: viewReorderIndices (view-chip drag maps to full list)', () => {
  const list = [{ id: 'home' }, { id: 'b' }, { id: 'work' }, { id: 'd' }, { id: 'play' }];

  it('resolves full-list indices by id', () => {
    expect(viewReorderIndices(list, 'home', 'work')).toEqual({ from: 0, to: 2 });
    expect(viewReorderIndices(list, 'play', 'b')).toEqual({ from: 4, to: 1 });
  });

  // Regression: the view bar renders a per-display FILTERED subset. Dragging the
  // 2nd chip in a bar showing [home, work, play] must move 'work' (full index 2),
  // NOT viewStore.list[1] ('b', which isn't even in the bar).
  it('drag within a filtered bar moves the intended view, not the same-position full-list view', () => {
    const bar = [list[0], list[2], list[4]]; // home, work, play — indices 0,2,4 in full list
    const draggedFromBarIdx = 1;             // 'work' in the bar
    const targetFromBarIdx = 2;              // dropped onto 'play'
    const move = viewReorderIndices(list, bar[draggedFromBarIdx].id, bar[targetFromBarIdx].id);
    expect(move).toEqual({ from: 2, to: 4 }); // full-list indices for work→play
  });

  it('returns null for a no-op or unknown id', () => {
    expect(viewReorderIndices(list, 'home', 'home')).toBeNull();
    expect(viewReorderIndices(list, 'home', 'ghost')).toBeNull();
    expect(viewReorderIndices(list, 'ghost', 'work')).toBeNull();
  });
});

describe('views: pruneVisibleViewsMap (deleteView prunes per-display whitelist)', () => {
  it('removes the deleted id from a whitelist and keeps the rest', () => {
    const { map, changed } = pruneVisibleViewsMap({ mon1: ['home', 'work', 'play'] }, 'work');
    expect(changed).toBe(true);
    expect(map).toEqual({ mon1: ['home', 'play'] });
  });

  // Regression: a whitelist of only the deleted view must be dropped entirely,
  // not left as [] — an empty array renders a dead, empty bar with no way to
  // switch views. Dropping the entry falls the display back to all views.
  it('drops a monitor whose whitelist becomes empty', () => {
    const { map, changed } = pruneVisibleViewsMap({ mon1: ['work'], mon2: ['home'] }, 'work');
    expect(changed).toBe(true);
    expect(map).toEqual({ mon2: ['home'] });
    expect('mon1' in map).toBe(false);
  });

  it('is a no-op when no whitelist references the deleted id', () => {
    const input = { mon1: ['home', 'play'] };
    const { map, changed } = pruneVisibleViewsMap(input, 'work');
    expect(changed).toBe(false);
    expect(map).toEqual(input);
  });
});

// ---------------------------------------------------------------------------
// #93: the newest monitor adoption wins ([MI-07])
// ---------------------------------------------------------------------------
//
// `initForMonitor` suspends twice (the [MI-03] re-key, then
// `get_active_monitors`) before it writes anything. Display changes arrive in
// storms — a monitor waking fires several WM_DISPLAYCHANGE while DisplayPort
// link-trains — so two adoptions overlap routinely. Pre-fix the one that
// RESOLVED last won, which is not the one that STARTED last: the window could
// settle on a superseded monitor id and go on showing that screen's view, with
// widgets laid out for that screen's width, until the app was restarted.

describe('views: initForMonitor adoption race ([MI-07], #93)', () => {
  afterEach(() => {
    vi.mocked(invoke).mockImplementation((() => Promise.resolve([])) as typeof invoke);
  });

  it('mi07_newest_adoption_wins_even_when_an_older_one_resolves_last', async () => {
    // Hand out a deferred per `get_active_monitors` call so the test controls
    // the resolution ORDER independently of the call order.
    const pending: Array<(v: MonitorDescriptor[]) => void> = [];
    vi.mocked(invoke).mockImplementation(
      (() => new Promise<MonitorDescriptor[]>(resolve => { pending.push(resolve); })) as unknown as typeof invoke,
    );

    const stale = initForMonitor(ID_A);   // the topology we are moving away from
    const fresh = initForMonitor(ID_B);   // the one that supersedes it
    await new Promise(r => setTimeout(r, 0)); // let both reach their await
    expect(pending).toHaveLength(2);

    pending[1]([]);           // the NEWER adoption finishes first...
    await new Promise(r => setTimeout(r, 0));
    pending[0]([]);           // ...and the older one lands after it
    await Promise.all([stale, fresh]);

    expect(getCurrentMonitorId()).toBe(ID_B);
  });

  it('a single adoption still lands (the guard is not a blanket refusal)', async () => {
    await initForMonitor(ID_A);
    expect(getCurrentMonitorId()).toBe(ID_A);
  });
});

// ---------------------------------------------------------------------------
// [MI-08] An unresolved identity must never become a persistence key (#123)
// ---------------------------------------------------------------------------
//
// [MI-02] falls back to the `\\.\DISPLAYn` ordinal whenever EnumDisplayDevicesW
// fails or hands back an empty DeviceID. A panel waking from an overnight
// power-off answers that way for a pass or two while DisplayPort link-trains.
// Pre-fix, `initForMonitor` interned a fresh binding under that ordinal and
// switched the screen to the DEFAULT view: the user's widgets appeared to move
// to another screen's layout, and only a restart (which re-resolves the panel)
// put them back - the reported symptom AND workaround of #123.

/** A descriptor whose id is the ordinal because Rust could not resolve it. */
function monDegraded(deviceName: string): MonitorDescriptor {
  return { ...mon(deviceName, deviceName), idStable: false };
}

describe('views: monitorIdentityIsResolved [MI-08] (#123)', () => {
  it('mi08_a_flagged_fallback_id_is_not_resolved', () => {
    expect(monitorIdentityIsResolved(ORD2, [monDegraded(ORD2)])).toBe(false);
  });

  it('mi08_a_real_device_path_is_resolved', () => {
    expect(monitorIdentityIsResolved(ID_A, [mon(ID_A, ORD1)])).toBe(true);
  });

  it('mi08_an_unflagged_descriptor_keeps_its_old_meaning', () => {
    // Demo fixtures / e2e stubs / any pre-flag payload: absent is NOT degraded.
    expect(monitorIdentityIsResolved(ORD2, [mon(ORD2, ORD2)])).toBe(true);
  });

  it('mi08_an_unknown_topology_degrades_to_resolved', () => {
    expect(monitorIdentityIsResolved(ID_A, [])).toBe(true);
  });
});

describe('views: initForMonitor defers an unresolved identity [MI-08] (#123)', () => {
  afterEach(() => {
    vi.mocked(invoke).mockImplementation((() => Promise.resolve([])) as typeof invoke);
  });

  it('mi08_an_unresolved_id_with_no_binding_writes_nothing_and_is_not_adopted', async () => {
    await initForMonitor(ID_A);              // this window's real monitor
    expect(getCurrentMonitorId()).toBe(ID_A);
    const before = localStorage.getItem('widget-monitor-screen-map');

    vi.mocked(invoke).mockImplementation(
      (() => Promise.resolve([monDegraded(ORD2)])) as unknown as typeof invoke,
    );
    await initForMonitor(ORD2);              // the same panel, mid-link-training

    expect(getCurrentMonitorId()).toBe(ID_A);   // still on its own view
    expect(localStorage.getItem('widget-monitor-screen-map')).toBe(before);
  });

  it('mi08_an_unresolved_id_that_already_has_a_binding_is_still_adopted', async () => {
    // The permanently id-less adapter (RDP / virtual / indirect): its ordinal is
    // the only key it will ever have, and it must keep working.
    localStorage.setItem('widget-monitor-screen-map', JSON.stringify({ [ORD2]: 'default' }));
    vi.mocked(invoke).mockImplementation(
      (() => Promise.resolve([monDegraded(ORD2)])) as unknown as typeof invoke,
    );

    await initForMonitor(ORD2);

    expect(getCurrentMonitorId()).toBe(ORD2);
  });
});
