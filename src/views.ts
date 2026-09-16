import { createSignal } from 'solid-js';
import { produce } from 'solid-js/store';
import { invoke, type MonitorDescriptor } from './ipc.ts';
import { isPro } from './license.ts';
import { onStorageKey, writeShared } from './sync.ts';
import { logger } from './debugLog.ts';
import {
  viewStore,
  setViewStore,
  layoutKey,
  isDemoView,
  VIEWS_KEY,
  loadViewList,
  type View,
} from './viewState.ts';
import { destroyViewLayoutStore } from './layout.ts';
import { applyGlobalDesign } from './design.ts';
import { armIntroPlay, markViewTransition } from './bootOverlay.ts';
import { t } from './i18n.ts';
import { isString, isNumber, isBoolean, isOneOf, isArrayOf, type Guard } from './persistedStore.ts';

// Re-export for callers that imported from views.ts historically
export { viewStore, setViewStore, layoutKey, isDemoView, type View };

const MONITOR_MAP_KEY    = 'widget-monitor-screen-map';
const DEFAULT_NEW_KEY    = 'widget-default-screen-for-new-displays';

// The view list is owned + validated by viewState; reuse that one loader so the
// validation (and the icon migration) lives in exactly one place.
const loadList = loadViewList;

// Shared loader for the per-monitor maps (1.12). Each is a `Record<monitorId,
// V>` blob. Guarantees the result is a plain object (a corrupt scalar/array no
// longer flows downstream into Object.keys/entries) and drops any entry whose
// value fails its guard, so e.g. an invalid persisted position can't set a
// bogus CSS class.
// Exported for characterization tests: this guard is the single chokepoint all
// 8 per-monitor maps load through.
export function loadObjectMap<V>(key: string, valueGuard: Guard<V>): Record<string, V> {
  let raw: unknown;
  try { raw = JSON.parse(localStorage.getItem(key) ?? '{}'); }
  catch { return {}; }
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return {};
  const src = raw as Record<string, unknown>;
  const out: Record<string, V> = {};
  for (const k of Object.keys(src)) if (valueGuard(src[k])) out[k] = src[k] as V;
  return out;
}

/** The per-monitor map primitive (B8). Every `Record<monitorId, V>` blob shares
 *  the same shape: a guarded load, a reactive signal mirror, a save (persist +
 *  bump the signal), and a per-monitor setter. Factored here so the eight maps
 *  (view / position / size / icon-only / hidden / visible-views / disabled /
 *  scale) don't each hand-roll it. `onSave` lets a map run a side effect after
 *  every persist (e.g. mirror the disabled set to Rust). */
export interface PerMonitorMap<V> {
  load: () => Record<string, V>;
  getMap: () => Record<string, V>;
  save: (map: Record<string, V>) => void;
  setForMonitor: (monitorId: string, value: V) => void;
  /** Reload from storage into the reactive mirror WITHOUT re-persisting, for the
   *  cross-window `onStorageKey` handlers reacting to another window's write.
   *  Returns the freshly-loaded map so the handler can apply per-monitor effects. */
  syncFromStorage: () => Record<string, V>;
  /** [MI-03] Re-key this map from GDI ordinals onto stable ids (#70). Persists
   *  only when something actually changed, so a steady-state boot is silent.
   *  Returns the ordinals that resolved to nothing and were dropped. */
  migrateKeys: (deviceNameToId: ReadonlyMap<string, string>) => string[];
}

export function createPerMonitorMap<V>(
  key: string,
  guard: Guard<V>,
  onSave?: (map: Record<string, V>) => void,
): PerMonitorMap<V> {
  const load = (): Record<string, V> => loadObjectMap(key, guard);
  const [getMap, setSig] = createSignal<Record<string, V>>(load());
  const save = (map: Record<string, V>): void => {
    // writeShared: reliably mirror this per-monitor map to other windows (the
    // Displays overview + each monitor's widget window): the native storage
    // event is unreliable between WebView2 windows (1.24).
    writeShared(key, JSON.stringify(map));
    setSig({ ...map });
    onSave?.(map);
  };
  const setForMonitor = (monitorId: string, value: V): void => {
    const map = load();
    map[monitorId] = value;
    save(map);
  };
  const syncFromStorage = (): Record<string, V> => {
    const map = load();
    setSig(map);
    return map;
  };
  const migrateKeys = (deviceNameToId: ReadonlyMap<string, string>): string[] => {
    const result = migrateMonitorKeys(load(), deviceNameToId);
    // null = empty enumeration. Nothing resolves, so a rewrite would destroy the
    // user's config: leave every byte alone ([MI-03]).
    if (!result) return [];
    if (result.changed) save(result.map);
    return result.dropped;
  };
  return { load, getMap, save, setForMonitor, syncFromStorage, migrateKeys };
}

// ---------------------------------------------------------------------------
// Per-monitor → view mapping
// ---------------------------------------------------------------------------

// Reactive mirror of the per-monitor → view map, for the Displays overview.
const monitorView = createPerMonitorMap(MONITOR_MAP_KEY, isString);
const loadMonitorMap = monitorView.load;
const saveMonitorMap = monitorView.save;
const monitorViewMapSig = monitorView.getMap;
/** Per-monitor → viewId map (reactive), for the Displays overview. */
export const getMonitorViewMap = monitorView.getMap;

let currentMonitorId: string | null = null;
const [currentMonitorIdSig, setCurrentMonitorIdSig] = createSignal<string | null>(null);
/** This window's monitor id (reactive). */
export const getCurrentMonitorId = currentMonitorIdSig;

// ---------------------------------------------------------------------------
// Monitor identity: keys, migration, labels  (#70, [MI-01]/[MI-03])
// ---------------------------------------------------------------------------

/** The phantom device name a pre-[DW-07] build invented when `EnumDisplayMonitors`
 *  came back empty (an unconditional 1920x1080 window at the primary's origin).
 *  Rust no longer creates it; this constant exists so the key can never be
 *  interned again, and so an old localStorage carrying it stays recognizable. */
export const PHANTOM_MONITOR_KEY = 'FALLBACK';

/** Marker making the [MI-03] key migration run-once. The per-monitor maps are
 *  bare `Record`s with nowhere to stamp a version, so the stamp lives beside
 *  them. Mirrors `schemaVersion` in Rust's `disabled_monitors.json`. */
export const MONITOR_KEY_SCHEMA_KEY = 'widget-monitor-key-schema';
const MONITOR_KEY_SCHEMA_VERSION = 2;

/** [MI-03] True for the pre-#70 key shape: a GDI ordinal `\\.\DISPLAY<n>`.
 *  Purely syntactic on purpose, and deliberately identical to Rust's
 *  `is_legacy_device_name` (a prefix test, not a `\d+$` match) so both halves
 *  classify every string the same way. Anything else in a saved map is either a
 *  stable id ([MI-01]) or a string we never wrote, and both must survive
 *  migration untouched. */
export function isLegacyDeviceName(s: string): boolean {
  return s.toUpperCase().startsWith('\\\\.\\DISPLAY');
}

/** The live `deviceName → id` lookup the [MI-03] migration resolves through. */
export function buildDeviceNameToId(
  monitors: readonly MonitorDescriptor[],
): ReadonlyMap<string, string> {
  return new Map(monitors.map(m => [m.deviceName, m.id]));
}

export interface MonitorKeyMigration<V> {
  /** The re-keyed map to persist. */
  map: Record<string, V>;
  /** Ordinals that resolved to nothing and were discarded. Logged loudly. */
  dropped: string[];
  /** False when every key already was a stable id: persist nothing. */
  changed: boolean;
}

/** [MI-03] Re-key one saved per-monitor map from GDI ordinals onto stable ids.
 *
 * `null` means "do not migrate, do not write": the enumeration was EMPTY, so
 * nothing can be resolved and a rewrite would destroy the user's config. That
 * state is reachable in practice, not theoretical: the autostart task uses a
 * LogonTrigger with no delay ([AS-01]), so the first enumeration can land before
 * any panel has finished coming up.
 *
 * Per key:
 *   * resolves via the live device_name→id map → replaced by the stable id;
 *   * not ordinal-shaped (already a stable id) → passed through untouched, even
 *     when its panel is unplugged right now: an id still names that exact panel,
 *     so its state is worth keeping while it is away;
 *   * an ordinal that resolves to nothing → DROPPED.
 *
 * The drop is the one deliberate exception to "never delete user data on a
 * recoverable error", and it is not recoverable: a bare ordinal cannot be
 * resolved back to a panel once that panel is gone, and keeping it is not
 * neutral — it IS the #70 bug, the entry would go on configuring whichever
 * unrelated panel inherits that ordinal next boot. The mild cost is that a
 * monitor disconnected at migration time forgets its settings once, which the
 * user re-sets in one click. Mirrors Rust's `migrate_disabled_ids`.
 */
export function migrateMonitorKeys<V>(
  saved: Record<string, V>,
  deviceNameToId: ReadonlyMap<string, string>,
): MonitorKeyMigration<V> | null {
  if (deviceNameToId.size === 0) return null;

  const out: Record<string, V> = {};
  const dropped: string[] = [];
  const resolved: [string, string][] = [];

  // Pass 1: keys that are already stable ids. They are authoritative, so they
  // claim their slot before any re-keyed ordinal can collide with it.
  for (const key of Object.keys(saved)) {
    const id = deviceNameToId.get(key);
    if (id !== undefined) resolved.push([key, id]);
    else if (isLegacyDeviceName(key)) dropped.push(key);
    else out[key] = saved[key];
  }

  // Pass 2: ordinals resolved through the live topology. An id already taken by
  // pass 1 wins: two keys must never silently merge into one entry.
  for (const [key, id] of resolved) {
    if (id in out) {
      logger.warn('displays', `[MI-03] monitor key collision: "${key}" resolves to an id ` +
        `already present; keeping the stable entry and discarding the legacy one`);
      continue;
    }
    out[id] = saved[key];
  }

  dropped.sort();
  const savedKeys = Object.keys(saved);
  const changed = savedKeys.length !== Object.keys(out).length
    || savedKeys.some(k => !(k in out));
  return { map: out, dropped, changed };
}

/** Display number parsed from a Windows device name ("\\.\DISPLAY1" → 1). */
export function monitorNumber(deviceName: string): number | null {
  const m = deviceName.match(/DISPLAY(\d+)/i);
  return m ? parseInt(m[1], 10) : null;
}

/** [MI-01] The human label for a monitor key.
 *
 *  The key is a device interface path now — an opaque hardware string that must
 *  never reach the user — so the label is re-derived from the LIVE topology's
 *  `deviceName` on every render. That indirection is the whole point: the
 *  ordinal is a fine *label* precisely because it is re-read from the current
 *  topology, and a fatal *key* for exactly the same reason.
 *
 *  Unknown id (stale target, or a panel unplugged since): fall back to the id
 *  itself only when it is ordinal-shaped (the [MI-02] virtual-display case,
 *  where id === deviceName), otherwise a generic label. Never the raw id. */
export function monitorLabel(
  monitorId: string,
  monitors: readonly MonitorDescriptor[],
): string {
  const live = monitors.find(m => m.id === monitorId);
  const deviceName = live?.deviceName
    ?? (isLegacyDeviceName(monitorId) ? monitorId : null);
  const n = deviceName !== null ? monitorNumber(deviceName) : null;
  // issue #91: the label is user-facing chrome (config tree, display settings
  // header), so both halves are translated. "(Primary)" is a wrapper key, not a
  // suffix concatenated onto `base` — a language that marks the primary display
  // with a prefix has nowhere to put a hardcoded trailing parenthetical.
  const base = n !== null ? t('app.monitorLabel', { n }) : t('app.monitorLabelUnknown');
  return live?.primary ? t('app.monitorLabelPrimary', { label: base }) : base;
}

/** [DW-07] Whether a monitor key may be written back into the per-monitor maps.
 *
 *  `initForMonitor` interns whatever key it is handed (it writes a default view
 *  for an unknown one), so a bad key becomes permanent. Two keys are refused:
 *  the empty string, and the [DW-07] `"FALLBACK"` phantom a pre-fix build could
 *  hand us. Beyond that, an id absent from a KNOWN topology is refused too.
 *
 *  An EMPTY topology degrades to "trust it": the id came from `get_monitor_id`,
 *  which resolves it from Rust's own live enumeration, and blocking on a
 *  transiently empty enumeration would make a real window forget its monitor.
 *  Note an ordinal-shaped id is NOT refused on its own: [MI-02] falls back to
 *  the ordinal as the id for indirect/virtual/RDP adapters, and that is a
 *  legitimate (if unstable) live key. */
export function canInternMonitorKey(
  monitorId: string,
  monitors: readonly MonitorDescriptor[],
): boolean {
  if (!monitorId || monitorId === PHANTOM_MONITOR_KEY) return false;
  if (monitors.length === 0) return true;
  return monitors.some(m => m.id === monitorId);
}

/** [MI-08] Did Rust RESOLVE this monitor's identity, or is the key a
 *  placeholder?
 *
 *  [MI-02] falls back to the `\\.\DISPLAYn` ordinal whenever
 *  `EnumDisplayDevicesW` fails or returns an empty `DeviceID`. For an indirect
 *  / virtual / RDP adapter that is permanent and legitimate. For a real panel
 *  it is TRANSIENT - one waking from power-off answers that way for a pass or
 *  two while DisplayPort link-trains - and the two are indistinguishable from
 *  the string alone, which is why the flag travels on the descriptor.
 *
 *  An unknown topology (empty list) degrades to "resolved", matching
 *  `canInternMonitorKey`: blocking on a transiently empty enumeration would
 *  make a real window forget its monitor. */
export function monitorIdentityIsResolved(
  monitorId: string,
  monitors: readonly MonitorDescriptor[],
): boolean {
  const live = monitors.find(m => m.id === monitorId);
  return live ? live.idStable !== false : true;
}

// ---------------------------------------------------------------------------
// Per-monitor view-bar position / size / icon-only
// ---------------------------------------------------------------------------

export type ViewBarPosition =
  | 'top-left' | 'top-center' | 'top-right'
  | 'left-center' | 'right-center'
  | 'bottom-left' | 'bottom-center' | 'bottom-right';

const POSITION_LAST_KEY  = 'widget-screen-selector-position-last';
const SIZE_LAST_KEY      = 'widget-screen-selector-size-last';
const ICON_ONLY_LAST_KEY = 'widget-screen-selector-icon-only-last';
const POSITION_MAP_KEY = 'widget-screen-selector-position-map';

const isViewBarPosition: Guard<ViewBarPosition> = isOneOf(
  'top-left', 'top-center', 'top-right',
  'left-center', 'right-center',
  'bottom-left', 'bottom-center', 'bottom-right',
);

const positionMap = createPerMonitorMap(POSITION_MAP_KEY, isViewBarPosition);
const loadPositionMap = positionMap.load;
const savePositionMap = positionMap.save;
/** Per-monitor view-bar position map (reactive), for the Displays overview. */
export const getViewBarPositionMap = positionMap.getMap;

/** Set the view-bar position for a specific monitor (used by the Displays
 *  overview). The monitor's own widget window picks this up via the
 *  POSITION_MAP_KEY sync. */
export function setViewBarPositionForMonitor(monitorId: string, pos: ViewBarPosition): void {
  const map = loadPositionMap();
  map[monitorId] = pos;
  savePositionMap(map);
}

function loadLastPosition(): ViewBarPosition {
  const v = localStorage.getItem(POSITION_LAST_KEY);
  const valid: ViewBarPosition[] = [
    'top-left','top-center','top-right',
    'bottom-left','bottom-center','bottom-right',
  ];
  return (valid as string[]).includes(v ?? '') ? (v as ViewBarPosition) : 'bottom-left';
}

const [viewBarPosition, _setViewBarPosition] = createSignal<ViewBarPosition>(loadLastPosition());
export const getViewBarPosition = viewBarPosition;

let pendingPosition: ViewBarPosition | null = null;
let pendingSize:     ViewBarSize | null     = null;
let pendingIconOnly: boolean | null         = null;
let pendingHidden:   boolean | null         = null;

export function setViewBarPosition(pos: ViewBarPosition): void {
  _setViewBarPosition(pos);
  localStorage.setItem(POSITION_LAST_KEY, pos);
  if (currentMonitorId) {
    const map = loadPositionMap();
    map[currentMonitorId] = pos;
    savePositionMap(map);
  } else {
    pendingPosition = pos;
  }
}

export type ViewBarSize = 'medium' | 'large' | 'xl';
const SIZE_MAP_KEY = 'widget-screen-selector-size-map';

const isViewBarSize: Guard<ViewBarSize> = isOneOf('medium', 'large', 'xl');

const sizeMap = createPerMonitorMap(SIZE_MAP_KEY, isViewBarSize);
const loadSizeMap = sizeMap.load;
const saveSizeMap = sizeMap.save;
/** Per-monitor view-bar size map (reactive), for the Displays overview. */
export const getViewBarSizeMap = sizeMap.getMap;

/** Set the view-bar size for a specific monitor (used by the Displays
 *  overview). The monitor's own widget window picks this up via the
 *  SIZE_MAP_KEY sync. */
export function setViewBarSizeForMonitor(monitorId: string, size: ViewBarSize): void {
  const map = loadSizeMap();
  map[monitorId] = size;
  saveSizeMap(map);
}

function loadLastSize(): ViewBarSize {
  const v = localStorage.getItem(SIZE_LAST_KEY);
  return v === 'medium' || v === 'large' || v === 'xl' ? v : 'medium';
}

const [viewBarSize, _setViewBarSize] = createSignal<ViewBarSize>(loadLastSize());
export const getViewBarSize = viewBarSize;

export function setViewBarSize(size: ViewBarSize): void {
  _setViewBarSize(size);
  localStorage.setItem(SIZE_LAST_KEY, size);
  if (currentMonitorId) {
    const map = loadSizeMap();
    map[currentMonitorId] = size;
    saveSizeMap(map);
  } else {
    pendingSize = size;
  }
}

const ICON_ONLY_MAP_KEY = 'widget-screen-selector-icon-only-map';

const iconOnlyMap = createPerMonitorMap(ICON_ONLY_MAP_KEY, isBoolean);
const loadIconOnlyMap = iconOnlyMap.load;
const saveIconOnlyMap = iconOnlyMap.save;
/** Per-monitor view-bar icon-only map (reactive), for the Displays overview. */
export const getViewBarIconOnlyMap = iconOnlyMap.getMap;

/** Set view-bar icon-only mode for a specific monitor (used by the Displays
 *  overview). The monitor's own widget window picks this up via the
 *  ICON_ONLY_MAP_KEY sync. */
export function setViewBarIconOnlyForMonitor(monitorId: string, v: boolean): void {
  const map = loadIconOnlyMap();
  map[monitorId] = v;
  saveIconOnlyMap(map);
}

function loadLastIconOnly(): boolean {
  return localStorage.getItem(ICON_ONLY_LAST_KEY) === 'true';
}

const [viewBarIconOnly, _setViewBarIconOnly] = createSignal(loadLastIconOnly());
export const getViewBarIconOnly = viewBarIconOnly;

export function setViewBarIconOnly(v: boolean): void {
  _setViewBarIconOnly(v);
  localStorage.setItem(ICON_ONLY_LAST_KEY, v ? 'true' : 'false');
  if (currentMonitorId) {
    const map = loadIconOnlyMap();
    map[currentMonitorId] = v;
    saveIconOnlyMap(map);
  } else {
    pendingIconOnly = v;
  }
}

const HIDDEN_LAST_KEY = 'widget-screen-selector-hidden-last';
const HIDDEN_MAP_KEY  = 'widget-screen-selector-hidden-map';

const hiddenMapStore = createPerMonitorMap(HIDDEN_MAP_KEY, isBoolean);
const loadHiddenMap = hiddenMapStore.load;
const saveHiddenMap = hiddenMapStore.save;
/** Per-monitor view-bar hidden map (reactive), for the Displays overview. */
export const getViewBarHiddenMap = hiddenMapStore.getMap;

/** Set view-bar hidden state for a specific monitor (used by the Displays
 *  overview). The monitor's own widget window picks this up via the
 *  HIDDEN_MAP_KEY sync. */
export function setViewBarHiddenForMonitor(monitorId: string, v: boolean): void {
  const map = loadHiddenMap();
  map[monitorId] = v;
  saveHiddenMap(map);
}

function loadLastHidden(): boolean {
  return localStorage.getItem(HIDDEN_LAST_KEY) === 'true';
}

const [viewBarHidden, _setViewBarHidden] = createSignal(loadLastHidden());
export const getViewBarHidden = viewBarHidden;

export function setViewBarHidden(v: boolean): void {
  _setViewBarHidden(v);
  localStorage.setItem(HIDDEN_LAST_KEY, v ? 'true' : 'false');
  if (currentMonitorId) {
    const map = loadHiddenMap();
    map[currentMonitorId] = v;
    saveHiddenMap(map);
  } else {
    pendingHidden = v;
  }
}

// ---------------------------------------------------------------------------
// Per-monitor visible-views (whitelist of view ids shown in the view bar)
// ---------------------------------------------------------------------------

const VISIBLE_VIEWS_MAP_KEY = 'widget-screen-selector-visible-map';

const visibleViewsStore = createPerMonitorMap(VISIBLE_VIEWS_MAP_KEY, isArrayOf(isString));
const loadVisibleViewsMap = visibleViewsStore.load;
const saveVisibleViewsMap = visibleViewsStore.save;
/** Per-monitor visible-views whitelist map (reactive). Empty/missing entry = show all. */
export const getViewBarVisibleViewsMap = visibleViewsStore.getMap;

/** Set the visible-views whitelist for a monitor. Empty array drops the entry
 *  so "all visible" is the default state and round-trips cleanly. */
export function setViewBarVisibleViewsForMonitor(monitorId: string, ids: string[]): void {
  const map = loadVisibleViewsMap();
  if (ids.length === 0) {
    if (!(monitorId in map)) return;
    delete map[monitorId];
  } else {
    map[monitorId] = [...ids];
  }
  saveVisibleViewsMap(map);
}

// ---------------------------------------------------------------------------
// Per-monitor "themia disabled" flag
// ---------------------------------------------------------------------------

const MONITOR_DISABLED_KEY = 'widget-monitor-disabled-map';

// `onSave` mirrors the disabled set to Rust after every persist: it keeps a JSON
// file (so the pre-frontend startup pass can skip overlay creation) and
// reconciles live windows (closes overlays for newly-disabled monitors, builds
// for re-enabled ones). Without it an overlay stays alive on a disabled monitor
// and swallows desktop right-clicks. (`pushDisabledMonitorsToBackend` is a
// hoisted function declaration, so it's available at this module-init call.)
const disabledStore = createPerMonitorMap(MONITOR_DISABLED_KEY, isBoolean, pushDisabledMonitorsToBackend);
const loadMonitorDisabledMap = disabledStore.load;
const saveMonitorDisabledMap = disabledStore.save;
const monitorDisabledMapSig = disabledStore.getMap;
/** Per-monitor "themia disabled" map (reactive), for the Displays overview. */
export const getMonitorDisabledMap = disabledStore.getMap;

/** True for monitors whose flag is set in the disabled map. */
function disabledList(map: Record<string, boolean>): string[] {
  return Object.entries(map).filter(([, v]) => v).map(([k]) => k);
}

/** Mirror the disabled set to Rust ([MI-04]: stable ids, never ordinals).
 *
 *  ORDERING (#70): gated on the [MI-03] re-key. This fires on load (App pushes
 *  once at startup) and on every save, both of which can happen before the async
 *  migration lands — an un-gated push would send the dead ordinals and make Rust
 *  destroy overlays on the wrong physical panels, which is the very bug. The map
 *  is read AFTER the await, never captured at call time, so a push queued
 *  mid-migration sends the migrated keys. */
function pushDisabledMonitorsToBackend(): void {
  void monitorKeysReady.then(() => {
    invoke('sync_disabled_monitors', { disabled: disabledList(monitorDisabledMapSig()) })
      .catch(e => logger.error('tauri', `sync_disabled_monitors failed: ${e}`));
  });
}

/** Push the localStorage state once at app startup so the Rust file mirror
 *  catches up with whatever the user set in a previous run (or with a
 *  pre-existing localStorage from before this code shipped). Safe to call
 *  from every window: Rust dedupes via a HashSet and reconcile is
 *  idempotent. Deferred until the [MI-03] re-key lands (see the push). */
export function syncMonitorDisabledToBackend(): void {
  pushDisabledMonitorsToBackend();
}

/** Whether themia is disabled on THIS window's monitor (reactive). */
export function getMonitorDisabled(): boolean {
  const id = currentMonitorIdSig();
  return id ? !!monitorDisabledMapSig()[id] : false;
}

/** Enable/disable themia on a monitor. Refuses to disable the last remaining
 *  enabled monitor, since blanking every display would lock the user out of the
 *  settings (the config window is only reachable from a live widget window). */
export function setMonitorDisabled(monitorId: string, disabled: boolean): void {
  const map = loadMonitorDisabledMap();
  if (disabled) {
    const stillEnabled = Object.keys(monitorViewMapSig())
      .filter(id => id !== monitorId && !map[id]);
    if (stillEnabled.length === 0) return;
    map[monitorId] = true;
  } else {
    delete map[monitorId];
  }
  saveMonitorDisabledMap(map);
}

// ---------------------------------------------------------------------------
// Per-monitor widget scale
// ---------------------------------------------------------------------------

const MONITOR_SCALE_KEY = 'widget-monitor-scale-map';

const scaleStore = createPerMonitorMap(MONITOR_SCALE_KEY, isNumber);
const loadMonitorScaleMap = scaleStore.load;
const saveMonitorScaleMap = scaleStore.save;
const monitorScaleMapSig = scaleStore.getMap;
/** Per-monitor widget-scale map (reactive), for the Displays overview. */
export const getMonitorScaleMap = scaleStore.getMap;

/** Widget-scale factor for THIS window's monitor (reactive). Defaults to 1.
 *  Scales the widget window's content (widgets + chrome); independent of the
 *  global uiScale, which only affects popups + the config window. */
export function getMonitorScale(): number {
  const id = currentMonitorIdSig();
  const v = id ? monitorScaleMapSig()[id] : undefined;
  return typeof v === 'number' && v > 0 ? v : 1;
}

/** Set the widget-scale factor for a monitor (used by the Displays overview).
 *  The monitor's own widget window picks this up via the MONITOR_SCALE_KEY
 *  sync. A scale of 1 is the default, so drop the key rather than store it. */
export function setMonitorScale(monitorId: string, scale: number): void {
  const map = loadMonitorScaleMap();
  if (scale === 1) delete map[monitorId];
  else map[monitorId] = scale;
  saveMonitorScaleMap(map);
}

// ---------------------------------------------------------------------------
// #70: the one-time key re-key, and the gate everything else waits on
// ---------------------------------------------------------------------------

/** Every per-monitor map, i.e. everything the [MI-03] re-key must cover. Adding
 *  a ninth map without adding it here would silently leave it on the ordinals. */
const PER_MONITOR_MAPS: {
  key: string;
  migrateKeys: (lookup: ReadonlyMap<string, string>) => string[];
}[] = [
  { key: MONITOR_MAP_KEY,          migrateKeys: monitorView.migrateKeys },
  { key: POSITION_MAP_KEY,         migrateKeys: positionMap.migrateKeys },
  { key: SIZE_MAP_KEY,             migrateKeys: sizeMap.migrateKeys },
  { key: ICON_ONLY_MAP_KEY,        migrateKeys: iconOnlyMap.migrateKeys },
  { key: HIDDEN_MAP_KEY,           migrateKeys: hiddenMapStore.migrateKeys },
  { key: VISIBLE_VIEWS_MAP_KEY,    migrateKeys: visibleViewsStore.migrateKeys },
  { key: MONITOR_DISABLED_KEY,     migrateKeys: disabledStore.migrateKeys },
  { key: MONITOR_SCALE_KEY,        migrateKeys: scaleStore.migrateKeys },
];

function isMonitorDescriptor(v: unknown): v is MonitorDescriptor {
  if (!v || typeof v !== 'object') return false;
  const m = v as Record<string, unknown>;
  return typeof m.id === 'string' && m.id !== ''
    && typeof m.deviceName === 'string' && m.deviceName !== ''
    && typeof m.primary === 'boolean'
    && typeof m.x === 'number' && typeof m.y === 'number'
    && typeof m.width === 'number' && typeof m.height === 'number';
}

/** Trust boundary for the live topology. A descriptor without an id or a
 *  deviceName can key nor label nothing, and a half-built lookup is worse than
 *  no lookup: it would make the [MI-03] migration drop ordinals it should have
 *  re-keyed. Drop the bad entries loudly; an all-bad list degrades to "unknown
 *  topology", which is the safe no-migration state. */
export function parseMonitorDescriptors(raw: unknown): MonitorDescriptor[] {
  if (!Array.isArray(raw)) {
    logger.error('displays', `get_active_monitors returned ${typeof raw}, expected an array; treating the topology as unknown`);
    return [];
  }
  const out = raw.filter(isMonitorDescriptor);
  if (out.length !== raw.length) {
    logger.error('displays', `get_active_monitors: dropped ${raw.length - out.length} malformed monitor descriptor(s)`);
  }
  return out;
}

/** The live topology, validated. The single place the frontend reads it. */
export async function fetchActiveMonitors(): Promise<MonitorDescriptor[]> {
  return parseMonitorDescriptors(await invoke('get_active_monitors'));
}

function monitorKeySchema(): number {
  const raw = localStorage.getItem(MONITOR_KEY_SCHEMA_KEY);
  if (raw === null) return 0; // absent: pre-#70 install or first run (silent)
  const n = Number(raw);
  if (!Number.isInteger(n) || n < 0) {
    // Corrupt ≠ absent: re-running the re-key is harmless (it is a no-op on
    // stable keys), but the bad stamp is worth a log.
    logger.warn('displays', `[MI-03] ${MONITOR_KEY_SCHEMA_KEY} is corrupt ("${raw}"); re-running the monitor key migration`);
    return 0;
  }
  return n;
}

/** [MI-03] Re-key every per-monitor map from GDI ordinals onto stable ids (#70)
 *  and stamp the schema. Returns false when it did nothing.
 *
 *  An EMPTY topology is NOT an error to shrug off: nothing resolves, so every
 *  key would look unresolvable and the whole config would be wiped. Skip, leave
 *  the stamp absent, and let the next launch (or the next
 *  `themia:display-changed`) do it. */
export function migrateMonitorKeyStorage(monitors: readonly MonitorDescriptor[]): boolean {
  if (monitors.length === 0) {
    logger.warn('displays', '[MI-03] monitor key migration skipped: the enumeration returned no monitors; stored keys left untouched');
    return false;
  }
  const lookup = buildDeviceNameToId(monitors);
  for (const m of PER_MONITOR_MAPS) {
    const dropped = m.migrateKeys(lookup);
    if (dropped.length > 0) {
      logger.warn('displays',
        `[MI-03] ${m.key}: dropped ${dropped.length} unresolvable ordinal key(s) ${JSON.stringify(dropped)}. ` +
        'A bare \\\\.\\DISPLAYn cannot be resolved back to a panel once that panel is gone, and keeping it ' +
        'would go on configuring whichever panel inherits that ordinal next boot (#70). ' +
        'If that display is currently disconnected, its settings are re-set in one click when it returns.');
    }
  }
  localStorage.setItem(MONITOR_KEY_SCHEMA_KEY, String(MONITOR_KEY_SCHEMA_VERSION));
  return true;
}

async function runMonitorKeyMigration(): Promise<void> {
  if (monitorKeySchema() >= MONITOR_KEY_SCHEMA_VERSION) return;
  try {
    migrateMonitorKeyStorage(await fetchActiveMonitors());
  } catch (e) {
    // Never fatal: the maps stay on their current keys and the next launch
    // retries. Loud, because until it succeeds #70 is still live.
    logger.error('displays', `[MI-03] monitor key migration failed: ${e}; per-monitor settings stay on their current keys until the next launch`);
  }
}

/** THE ORDERING GATE (#70). The per-monitor maps load synchronously from
 *  localStorage at module init, while the re-key that gives them their real keys
 *  needs an async `get_active_monitors`. Anything that pushes or adopts a
 *  per-monitor key must therefore chain on this promise, or it acts on the dead
 *  ordinals and leaves the UI and the backend disagreeing about which physical
 *  panels are configured.
 *
 *  Deterministic by construction: the promise is created here at module init, so
 *  it exists before any consumer can run, and a consumer that chains on an
 *  already-settled promise just runs on the next microtask. No timer, no "poll
 *  until it is probably right" (forbidden in correctness paths). */
const monitorKeysReady: Promise<void> = runMonitorKeyMigration();

/** Await the [MI-03] re-key. Exported for callers that must not read a
 *  per-monitor map before it lands. */
export function whenMonitorKeysReady(): Promise<void> {
  return monitorKeysReady;
}

// ---------------------------------------------------------------------------
// "Default view for new displays"
// ---------------------------------------------------------------------------

function resolveInitialDefaultView(): string {
  const stored = localStorage.getItem(DEFAULT_NEW_KEY);
  if (stored && viewStore.list.find(v => v.id === stored)) return stored;
  const fallback = viewStore.list[0].id;
  localStorage.setItem(DEFAULT_NEW_KEY, fallback);
  return fallback;
}

const [defaultViewForNew, _setDefaultViewForNew] = createSignal<string>(
  resolveInitialDefaultView(),
);

export const defaultNewDisplayView = defaultViewForNew;

export function getDefaultViewForNewDisplays(): string {
  return defaultViewForNew();
}

export function setDefaultViewForNewDisplays(id: string): void {
  if (!id || !viewStore.list.find(v => v.id === id)) return;
  writeShared(DEFAULT_NEW_KEY, id);
  _setDefaultViewForNew(id);
}

// ---------------------------------------------------------------------------
// Storage helpers
// ---------------------------------------------------------------------------

function saveList(): void {
  writeShared(VIEWS_KEY, JSON.stringify(viewStore.list));
}

// ---------------------------------------------------------------------------
// Cross-window sync
// ---------------------------------------------------------------------------

onStorageKey(VIEWS_KEY, () => {
  setViewStore({ list: loadList() });
  if (!viewStore.list.find(v => v.id === viewStore.activeId)) {
    switchView(viewStore.list[0].id);
  }
});

onStorageKey(MONITOR_MAP_KEY, () => {
  const map = monitorView.syncFromStorage();
  if (currentMonitorId) {
    const newId = map[currentMonitorId];
    if (newId && newId !== viewStore.activeId &&
        viewStore.list.find(v => v.id === newId)) {
      switchViewLocal(newId);
    }
  }
});

onStorageKey(MONITOR_DISABLED_KEY, () => {
  disabledStore.syncFromStorage();
});

onStorageKey(MONITOR_SCALE_KEY, () => {
  scaleStore.syncFromStorage();
});

onStorageKey(DEFAULT_NEW_KEY, (newValue) => {
  if (newValue && viewStore.list.find(v => v.id === newValue)) {
    _setDefaultViewForNew(newValue);
  } else {
    const fallback = viewStore.list[0].id;
    localStorage.setItem(DEFAULT_NEW_KEY, fallback);
    _setDefaultViewForNew(fallback);
  }
});

onStorageKey(POSITION_MAP_KEY, () => {
  const map = positionMap.syncFromStorage();
  if (currentMonitorId) {
    const pos = map[currentMonitorId];
    if (pos) _setViewBarPosition(pos);
  }
});

onStorageKey(SIZE_MAP_KEY, () => {
  const map = sizeMap.syncFromStorage();
  if (currentMonitorId) {
    if (map[currentMonitorId]) _setViewBarSize(map[currentMonitorId]);
  }
});

onStorageKey(ICON_ONLY_MAP_KEY, () => {
  const map = iconOnlyMap.syncFromStorage();
  if (currentMonitorId) {
    if (map[currentMonitorId] !== undefined) _setViewBarIconOnly(map[currentMonitorId]);
  }
});

onStorageKey(HIDDEN_MAP_KEY, () => {
  const map = hiddenMapStore.syncFromStorage();
  if (currentMonitorId) {
    if (map[currentMonitorId] !== undefined) _setViewBarHidden(map[currentMonitorId]);
  }
});

onStorageKey(VISIBLE_VIEWS_MAP_KEY, () => {
  visibleViewsStore.syncFromStorage();
});

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/** [MI-07] Monotonic adoption generation: the NEWEST `initForMonitor` wins.
 *
 *  Adoption suspends twice (the [MI-03] re-key, then `get_active_monitors`)
 *  before it writes anything, and display changes arrive in storms — a monitor
 *  waking up fires several `WM_DISPLAYCHANGE` in a row while DisplayPort
 *  link-trains. Without this counter the adoption that RESOLVES last wins,
 *  which is not necessarily the one that STARTED last, so a window could settle
 *  on a monitor id the topology had already moved past — and then keep another
 *  screen's view, with widgets laid out for another screen's width, until the
 *  app was restarted. That is the surviving half of #93. */
let adoptSeq = 0;

/** Adopt a monitor: this window's per-monitor settings come from `monitorId`'s
 *  slot in every map, and an unknown monitor gets its defaults interned.
 *
 *  ORDERING (#70): awaits the [MI-03] re-key before touching a single map. The
 *  id handed in is a STABLE id ([MI-01]), so looking it up in ordinal-keyed maps
 *  would miss on every one of them and then intern a SECOND entry for a monitor
 *  that already had settings — the user's pinned view would silently reset. */
export async function initForMonitor(monitorId: string): Promise<void> {
  const seq = ++adoptSeq;
  await monitorKeysReady;

  // [DW-07] Never intern a key that no live panel answers to: `initForMonitor`
  // writes back defaults for an unknown key, so a phantom becomes permanent.
  const monitors = await fetchActiveMonitors().catch((e: unknown) => {
    logger.error('displays', `get_active_monitors failed while adopting "${monitorId}": ${e}; proceeding on Rust's id`);
    return [] as MonitorDescriptor[];
  });
  // [MI-07] Last await crossed; everything below WRITES. A newer adoption has
  // started in the meantime, so this one is stale and must not land.
  if (seq !== adoptSeq) {
    logger.warn('displays', `abandoning stale adoption of "${monitorId}": a newer display change superseded it`);
    return;
  }
  if (!canInternMonitorKey(monitorId, monitors)) {
    logger.error('displays', `refusing to adopt monitor key "${monitorId}": it is not in the live topology (${monitors.length} monitor(s)); no per-monitor state written`);
    return;
  }

  // [MI-08] An identity we could not resolve is not a name, so it must not
  // become one. Adopting a transient ordinal interns a fresh binding below and
  // switches this screen to the DEFAULT view: the user's widgets "move" to
  // another screen's layout, and only a restart - which re-resolves the panel
  // properly - puts them back (#123). Bail BEFORE any write and let the next
  // display event, once the panel has finished link-training, adopt the real
  // id. A key that ALREADY has a binding is adopted either way: that is the
  // permanently id-less adapter (RDP / virtual / indirect) whose ordinal is the
  // only key it will ever have, and it must keep working.
  if (!monitorIdentityIsResolved(monitorId, monitors) && !loadMonitorMap()[monitorId]) {
    logger.warn('displays', `deferring adoption of "${monitorId}": its identity is unresolved (ordinal fallback) and it has no stored view; keeping the current view until it resolves`);
    return;
  }

  currentMonitorId = monitorId;
  setCurrentMonitorIdSig(monitorId);

  const posMap = loadPositionMap();
  if (pendingPosition !== null) {
    posMap[monitorId] = pendingPosition;
    savePositionMap(posMap);
    _setViewBarPosition(pendingPosition);
    pendingPosition = null;
  } else if (posMap[monitorId]) {
    _setViewBarPosition(posMap[monitorId]);
  }

  const sizeMap = loadSizeMap();
  if (pendingSize !== null) {
    sizeMap[monitorId] = pendingSize;
    saveSizeMap(sizeMap);
    _setViewBarSize(pendingSize);
    pendingSize = null;
  } else if (sizeMap[monitorId]) {
    _setViewBarSize(sizeMap[monitorId]);
  }

  const iconOnlyMap = loadIconOnlyMap();
  if (pendingIconOnly !== null) {
    iconOnlyMap[monitorId] = pendingIconOnly;
    saveIconOnlyMap(iconOnlyMap);
    _setViewBarIconOnly(pendingIconOnly);
    pendingIconOnly = null;
  } else if (iconOnlyMap[monitorId] !== undefined) {
    _setViewBarIconOnly(iconOnlyMap[monitorId]);
  }

  const hiddenMap = loadHiddenMap();
  if (pendingHidden !== null) {
    hiddenMap[monitorId] = pendingHidden;
    saveHiddenMap(hiddenMap);
    _setViewBarHidden(pendingHidden);
    pendingHidden = null;
  } else if (hiddenMap[monitorId] !== undefined) {
    _setViewBarHidden(hiddenMap[monitorId]);
  }

  const map = loadMonitorMap();
  let viewId = map[monitorId];

  if (!viewId || !viewStore.list.find(v => v.id === viewId)) {
    const preferred = getDefaultViewForNewDisplays();
    viewId = viewStore.list.find(v => v.id === preferred)
      ? preferred
      : viewStore.list[0].id;
    map[monitorId] = viewId;
    saveMonitorMap(map);
  }

  if (viewId !== viewStore.activeId) {
    // boot: this is the window finding ITS monitor's view (startup, display
    // connect): the remount must keep playing the STARTUP style ([S-24]).
    switchViewLocal(viewId, { boot: true });
  }
  // Re-apply global palette in case theme/uiScale loaded post-init.
  applyGlobalDesign();
}

/** Switch local active view WITHOUT writing to the monitor map (used by sync).
 *  Every real view change funnels through here, so this is where the intro
 *  flips to the TRANSITION style ([S-24]), except the boot-time monitor-init
 *  switch, which is part of startup, not a transition. */
function switchViewLocal(id: string, opts?: { boot?: boolean }): void {
  if (id === viewStore.activeId) return;
  // Both branches arm a one-shot intro play ([S-27]); a view-change remount
  // is allowed to animate; the boot branch keeps the STARTUP mode ([S-24]).
  if (!opts?.boot) markViewTransition();
  else armIntroPlay();
  setViewStore({ activeId: id });
}

export function switchView(id: string): void {
  if (id === viewStore.activeId) return;
  switchViewLocal(id);

  if (currentMonitorId) {
    const map = loadMonitorMap();
    map[currentMonitorId] = id;
    saveMonitorMap(map);
  }
}

/** Assign a view to a specific monitor (used by the Displays overview). The
 *  monitor's own widget window picks this up via the MONITOR_MAP_KEY sync. */
export function setMonitorView(monitorId: string, viewId: string): void {
  const map = loadMonitorMap();
  if (map[monitorId] === viewId) return;
  map[monitorId] = viewId;
  saveMonitorMap(map);
}

/** Drop all saved per-monitor state for a display, used to forget a
 *  disconnected display from the Displays overview. It reappears with defaults
 *  via initForMonitor() if the monitor is ever reconnected. */
export function forgetMonitor(monitorId: string): void {
  const viewMap = loadMonitorMap();
  if (monitorId in viewMap) { delete viewMap[monitorId]; saveMonitorMap(viewMap); }

  const disabledMap = loadMonitorDisabledMap();
  if (monitorId in disabledMap) { delete disabledMap[monitorId]; saveMonitorDisabledMap(disabledMap); }

  const posMap = loadPositionMap();
  if (monitorId in posMap) { delete posMap[monitorId]; savePositionMap(posMap); }

  const sizeMap = loadSizeMap();
  if (monitorId in sizeMap) { delete sizeMap[monitorId]; saveSizeMap(sizeMap); }

  const iconOnlyMap = loadIconOnlyMap();
  if (monitorId in iconOnlyMap) { delete iconOnlyMap[monitorId]; saveIconOnlyMap(iconOnlyMap); }

  const hiddenMap = loadHiddenMap();
  if (monitorId in hiddenMap) { delete hiddenMap[monitorId]; saveHiddenMap(hiddenMap); }

  const scaleMap = loadMonitorScaleMap();
  if (monitorId in scaleMap) { delete scaleMap[monitorId]; saveMonitorScaleMap(scaleMap); }

  const visibleMap = loadVisibleViewsMap();
  if (monitorId in visibleMap) { delete visibleMap[monitorId]; saveVisibleViewsMap(visibleMap); }
}

/**
 * True when the free tier has run out of views (2 max). `addView` /
 * `duplicateView` return `null` in that state; UI that offers "add view" checks
 * this first so it can route to the license page instead of failing silently.
 */
export function atFreeViewLimit(): boolean {
  return !isPro() && viewStore.list.length >= 2;
}

/** Auto-name for a newly created view ("View 3"), skipping names already taken. */
export function nextViewName(): string {
  const taken = new Set(viewStore.list.map((v: View) => v.name));
  let n = viewStore.list.length + 1;
  while (taken.has(t('app.viewN', { n }))) n += 1;
  return t('app.viewN', { n });
}

export function addView(name: string): string | null {
  if (atFreeViewLimit()) return null;
  const id = `view-${Date.now()}`;

  setViewStore(produce((state) => { state.list.push({ id, name: name.trim() || 'View', icon: 'home' }); }));
  saveList();
  return id;
}

export function duplicateView(sourceId: string): string | null {
  if (atFreeViewLimit()) return null;
  const source = viewStore.list.find((v: View) => v.id === sourceId);
  if (!source) return null;

  const newId = `view-${Date.now()}`;

  const rawLayout = localStorage.getItem(layoutKey(sourceId));
  if (rawLayout) {
    try {
      const data = JSON.parse(rawLayout) as { widgets?: unknown };
      if (Array.isArray(data.widgets)) {
        const now = Date.now();
        data.widgets = data.widgets.map((w: Record<string, unknown>, i: number) => ({ ...w, id: `${String(w.type)}-${now}-${i}` }));
      }
      writeShared(layoutKey(newId), JSON.stringify(data));
    } catch {
      // ignore: duplicating a view's layout is best-effort; a corrupt source
      // layout just yields a view with no copied widgets rather than aborting.
    }
  }

  setViewStore(produce((state) => {
    state.list.push({ id: newId, name: `${source.name} (Copy)`, icon: source.icon });
  }));
  saveList();
  return newId;
}

/** Remove a deleted view id from every per-display visible-views whitelist. A
 *  whitelist that becomes empty is dropped entirely so the bar falls back to
 *  showing all views — an empty whitelist array would otherwise render a dead,
 *  empty bar with no way to switch views. */
export function pruneVisibleViewsMap(
  map: Record<string, string[]>,
  deletedId: string,
): { map: Record<string, string[]>; changed: boolean } {
  let changed = false;
  const out: Record<string, string[]> = {};
  for (const [mon, arr] of Object.entries(map)) {
    if (!Array.isArray(arr) || !arr.includes(deletedId)) { out[mon] = arr; continue; }
    changed = true;
    const pruned = arr.filter(v => v !== deletedId);
    if (pruned.length > 0) out[mon] = pruned; // else omit → falls back to all views
  }
  return { map: out, changed };
}

export function deleteView(id: string): void {
  if (viewStore.list.length <= 1) return;
  if (isDemoView(id)) return;

  // Tear down the per-view store before removing localStorage
  destroyViewLayoutStore(id);

  localStorage.removeItem(layoutKey(id));
  setViewStore(produce((state) => {
    const idx = state.list.findIndex((v: View) => v.id === id);
    if (idx !== -1) state.list.splice(idx, 1);
  }));
  saveList();

  const map = loadMonitorMap();
  const fallback = viewStore.list[0].id;
  let mapChanged = false;
  for (const mon of Object.keys(map)) {
    if (map[mon] === id) { map[mon] = fallback; mapChanged = true; }
  }
  if (mapChanged) saveMonitorMap(map);

  // Drop the deleted id from every per-display visible-views whitelist so a
  // display whose whitelist referenced it doesn't end up with an empty bar.
  const vis = pruneVisibleViewsMap(loadVisibleViewsMap(), id);
  if (vis.changed) saveVisibleViewsMap(vis.map);

  if (getDefaultViewForNewDisplays() === id) {
    setDefaultViewForNewDisplays(viewStore.list[0].id);
  }

  if (viewStore.activeId === id) switchView(viewStore.list[0].id);
}

export function renameView(id: string, name: string): void {
  if (!name.trim()) return;
  if (isDemoView(id)) return;
  setViewStore('list', (v: View) => v.id === id, { name: name.trim() });
  saveList();
}

export function setViewIcon(id: string, icon: string): void {
  if (isDemoView(id)) return;
  if (!icon) return;
  setViewStore('list', (v: View) => v.id === id, { icon });
  saveList();
}

/** Full-list indices for moving the view `draggedId` to `targetId`'s slot.
 *  The view bar can render a FILTERED subset (per-display whitelist), so its DOM
 *  indices don't line up with `viewStore.list`; the reorder must be resolved by
 *  id against the full list, never by the bar's own index. Returns null when
 *  either id is missing or they're the same (nothing to move). */
export function viewReorderIndices(
  list: readonly { id: string }[],
  draggedId: string,
  targetId: string,
): { from: number; to: number } | null {
  const from = list.findIndex(v => v.id === draggedId);
  const to = list.findIndex(v => v.id === targetId);
  if (from < 0 || to < 0 || from === to) return null;
  return { from, to };
}

export function moveView(fromIndex: number, toIndex: number): void {
  if (fromIndex === toIndex) return;
  if (fromIndex < 0 || fromIndex >= viewStore.list.length) return;
  if (toIndex   < 0 || toIndex   >= viewStore.list.length) return;
  setViewStore(produce((state) => {
    const [item] = state.list.splice(fromIndex, 1);
    state.list.splice(toIndex, 0, item);
  }));
  saveList();
}
