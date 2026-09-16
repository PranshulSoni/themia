// ---------------------------------------------------------------------------
// Tray widgets: the collection, and the push that keeps the shell in step
// (src/Tray.SPEC.md)
// ---------------------------------------------------------------------------
//
// A tray widget is a normal widget instance in the `tray` layout collection
// ([S-01]). It never appears on the desktop; it appears as one notification-area
// icon, and clicking that icon opens it in a flyout ([S-05]).
//
// The frontend owns the list and the icons; Rust owns the live tray. This module
// is the seam: it turns the collection into the `TraySpec[]` that `tray_sync`
// diffs against the shell ([S-02]).

import { createEffect, createRoot, type JSX } from 'solid-js';

import { getViewLayoutStore, TRAY_COLLECTION_ID, isContainerType, type Widget } from './layout.ts';
import {
  FLYOUT_MAX, FLYOUT_MIN, clampFlyoutSize, resolveFlyoutSize, resolveTrayIconKey,
  resolveTrayName, type FlyoutSize,
} from './trayLayout.ts';
import { VIEW_ICON_MAP, isKnownIcon } from './components/iconMap.tsx';
import { invoke, type TrayEnv, type TraySpec, type TrayMenuLabels } from './ipc.ts';
import { isTrayEnv, rasterizeTrayIcon, trayIconColor } from './trayIcon.ts';
import registry from './widgets/registry.tsx';
import { logger } from './debugLog.ts';
import { t } from './i18n.ts';

export { TRAY_COLLECTION_ID };

// ── The collection ──────────────────────────────────────────────────────────

/** The tray's layout store. Same store type as a view's, so widget config,
 *  labels and the config tree all work on tray widgets unchanged ([S-01]). */
export function trayStore() {
  return getViewLayoutStore(TRAY_COLLECTION_ID);
}

/** The tray's widgets, in icon order. Reactive. */
export function trayWidgets(): readonly Widget[] {
  return trayStore().layout.widgets;
}

/**
 * Whether a widget type may live in the tray ([S-01]). Containers are the one
 * exclusion: they exist to arrange sibling tiles on a grid, which a flyout
 * hosting exactly one widget has none of.
 */
function canLiveInTray(type: string): boolean {
  return !isContainerType(type) && type in registry;
}

/** Add a widget to the tray. Returns its id, or null when the free-tier ceiling
 *  refuses it (the caller routes the user to the licence page, as elsewhere). */
export function addTrayWidget(type: string, defaultConfig: Record<string, unknown>): string | null {
  if (!canLiveInTray(type)) {
    logger.warn('tray', `refused to add ${type} to the tray: not a trayable type`);
    return null;
  }
  return trayStore().addWidget(type, undefined, defaultConfig);
}

// ── Flyout size ─────────────────────────────────────────────────────────────

// The rules themselves live in a registry-free module so they stay unit-
// testable; re-exported here so callers reach everything tray-related through
// one import.
export { FLYOUT_MIN, FLYOUT_MAX, clampFlyoutSize, resolveFlyoutSize };
export type { FlyoutSize };

/** The flyout size for a live tray widget, resolved against its type default. */
function flyoutSizeOf(widget: Widget): FlyoutSize {
  const def = registry[widget.type];
  return resolveFlyoutSize(widget.config, def?.defaultSize ?? FLYOUT_MIN);
}

/** Persist a new flyout size on a tray widget. */
export function setFlyoutSize(id: string, size: Partial<FlyoutSize>): void {
  const store = trayStore();
  const widget = store.layout.widgets.find(w => w.id === id);
  if (!widget) return;
  const next = clampFlyoutSize({ ...flyoutSizeOf(widget), ...size });
  store.updateWidgetConfig(id, {
    ...(widget.config ?? {}),
    trayWidth: next.width,
    trayHeight: next.height,
  });
}

/**
 * A tray widget's tooltip / menu title ([S-14]): its own tray name, else its
 * header title, else its type's label. The chain matters — a tray widget can be
 * configured with no header at all, and then the title it falls back to is the
 * only thing naming it.
 */
export function trayWidgetTitle(widget: Widget): string {
  const fallback = widget.label?.trim() || registry[widget.type]?.label || widget.type;
  return resolveTrayName(widget.config, fallback);
}

/** A tray widget's icon override key ([S-14]), or `''` for its type's own. */
export function trayIconKeyOf(widget: Pick<Widget, 'config'>): string {
  return resolveTrayIconKey(widget.config, isKnownIcon);
}

// ── Sync ────────────────────────────────────────────────────────────────────

/** Rasterized icons, keyed by everything that determines the pixels. A sync that
 *  changes only a name must not redraw every icon. */
const iconCache = new Map<string, number[]>();

/** The icon a tray widget draws: its chosen catalogue override ([S-14]), else
 *  the widget type's own registry icon. */
function iconSource(type: string, iconKey: string): (() => JSX.Element) | null {
  if (iconKey) {
    const draw = VIEW_ICON_MAP[iconKey];
    if (draw) return () => draw(20);
  }
  return registry[type]?.icon ?? null;
}

async function iconFor(type: string, iconKey: string, env: TrayEnv): Promise<number[] | null> {
  const color = trayIconColor(env.lightTheme);
  const cacheKey = `${type}:${iconKey}:${env.iconSize}:${color}`;
  const hit = iconCache.get(cacheKey);
  if (hit) return hit;
  const source = iconSource(type, iconKey);
  if (!source) return null;
  const rgba = await rasterizeTrayIcon(source, env.iconSize, color);
  if (rgba) iconCache.set(cacheKey, rgba);
  return rgba;
}

/** What one sync needs from a widget, snapshotted so the async work below never
 *  reads the store again (and so the effect's dependencies are exact). */
interface TrayItem {
  id: string;
  type: string;
  iconKey: string;
  tooltip: string;
  size: FlyoutSize;
}

async function pushSync(items: readonly TrayItem[], labels: TrayMenuLabels): Promise<void> {
  let raw: unknown;
  try {
    raw = await invoke('tray_env');
  } catch (e) {
    // Non-Tauri (browser preview / unit env) or a backend that is not up yet.
    logger.debug('tray', `tray_env unavailable, skipping sync: ${String(e)}`);
    return;
  }
  if (!isTrayEnv(raw)) {
    logger.debug('tray', `tray_env returned an unusable shape, skipping sync: ${JSON.stringify(raw)}`);
    return;
  }
  const env: TrayEnv = raw;

  const specs: TraySpec[] = [];
  for (const item of items) {
    const icon = await iconFor(item.type, item.iconKey, env);
    if (!icon) {
      // Loud, and skipped rather than pushed blank: `tray_sync` would reject a
      // wrong-sized buffer anyway, and an invisible icon is worse than none.
      logger.error('tray', `could not rasterize the icon for ${item.type}; leaving it out of the tray`);
      continue;
    }
    specs.push({
      id: item.id,
      tooltip: item.tooltip,
      icon,
      iconSize: env.iconSize,
      width: item.size.width,
      height: item.size.height,
    });
  }

  try {
    await invoke('tray_sync', { specs, labels });
  } catch (e) {
    logger.error('tray', `tray_sync failed: ${String(e)}`);
  }
}

// Serializes pushes: the store can change again while a rasterization is in
// flight, and two overlapping syncs could otherwise land out of order and leave
// the shell showing the older set.
let pending: Promise<void> = Promise.resolve();
let disposeSync: (() => void) | null = null;

/**
 * Start mirroring this window's tray collection into the shell ([S-02]).
 *
 * Every window calls this. That is deliberate rather than wasteful: they all
 * read the same localStorage, so they all compute the same set, and `tray_sync`
 * is a diff — the second window's push is a handful of comparisons. Electing a
 * single owner would instead mean the tray silently stops updating whenever that
 * one window is the one that closed.
 *
 * Idempotent per window.
 */
export function initTraySync(): void {
  if (disposeSync) return;
  disposeSync = createRoot((dispose) => {
    createEffect(() => {
      // Read every reactive source synchronously; the push is async and must not
      // be what tracks them.
      const items: TrayItem[] = trayWidgets().map(w => ({
        id: w.id,
        type: w.type,
        iconKey: trayIconKeyOf(w),
        tooltip: trayWidgetTitle(w),
        size: flyoutSizeOf(w),
      }));
      const labels: TrayMenuLabels = {
        open: t('tray.menu.open'),
        settings: t('tray.menu.settings'),
        appSettings: t('tray.menu.appSettings'),
        quit: t('tray.menu.quit'),
      };
      // `.catch` on the CHAIN, not inside pushSync: a rejection left in
      // `pending` would poison every later sync (each `.then` would skip
      // straight to the rejected path) and the tray would silently stop
      // updating for the rest of the session.
      pending = pending
        .then(() => pushSync(items, labels))
        .catch(e => logger.error('tray', `tray sync failed: ${String(e)}`));
    });
    return dispose;
  });
}
