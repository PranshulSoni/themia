/**
 * Always-on-top pin layer ([S-45]).
 *
 * Every widget on a monitor shares ONE overlay window, and z-order is a
 * property of the WINDOW, not of a widget inside it. So "keep this one widget
 * above other applications" cannot be a flag on the existing overlay: it needs
 * a SECOND window that is permanently `WS_EX_TOPMOST`.
 *
 * That window is `monitor-{i}-pin`. It has exactly the same work-area bounds
 * and the same view/grid as its base `monitor-{i}`, so a pinned widget keeps
 * its position, size and per-monitor scale — it only changes which window
 * paints it. Rust builds the pin overlays lazily (`set_pin_layer`), so a user
 * who pins nothing pays no extra renderer.
 *
 * This module is the single source of the routing rule and is pure, so the
 * rule is unit-pinned without a window.
 */

import type { Widget } from './layout.ts';

/** Label suffix Rust gives a monitor's pin overlay: `monitor-0` → `monitor-0-pin`. */
export const PIN_LAYER_SUFFIX = '-pin';

/** True for a pin overlay's window label. Base overlays and every detached
 *  window (config/connect/widget-window/…) are false. */
export function isPinLayerLabel(label: string): boolean {
  return label.startsWith('monitor-') && label.endsWith(PIN_LAYER_SUFFIX);
}

/**
 * Is this widget pinned above other applications?
 *
 * Strict `=== true`, like `transparentBg` ([S-32]): an absent or legacy value
 * is "not pinned", never "maybe pinned". Pinning is honoured on TOP-LEVEL
 * widgets only — a container's children (and an area group's members) follow
 * their ancestor, see `effectivePinned`, because splitting a group across two
 * windows would paint its members on a different layer than the panel they
 * stand on.
 */
export function isWidgetPinned(w: Pick<Widget, 'config'> | undefined): boolean {
  return w?.config?.alwaysOnTop === true;
}

/**
 * Pin state a widget actually renders with: its own flag if it is top-level,
 * otherwise its root ancestor's. Cycles and dangling `parentId`s (which
 * layout's parse already sanitizes, [S-20]) terminate on the visited set
 * rather than spinning.
 */
export function effectivePinned(w: Widget, byId: ReadonlyMap<string, Widget>): boolean {
  let cur: Widget | undefined = w;
  const seen = new Set<string>();
  while (cur && cur.parentId !== undefined && !seen.has(cur.id)) {
    seen.add(cur.id);
    cur = byId.get(cur.parentId);
  }
  return isWidgetPinned(cur);
}

/**
 * THE routing rule: does the window described by `pinLayer` render this widget?
 *
 * - The pin overlay renders pinned widgets and nothing else.
 * - The base overlay renders the rest — PLUS the pinned ones while edit mode
 *   is on, because the pin overlay stands down entirely during editing. It is
 *   a full-work-area topmost window, and edit mode clears the window region
 *   ([S-17]) so empty grid cells become drop targets; doing that on a topmost
 *   window would put a full-screen input trap over every other application.
 *   Handing the widgets back to the base layer keeps drag / resize / the grid
 *   overlay working on exactly one window, the way they always have.
 *
 * Every widget is rendered by exactly one of the two layers for any input, so
 * a pinned widget can never be painted twice or dropped by both.
 */
export function layerShowsWidget(pinned: boolean, pinLayer: boolean, editMode: boolean): boolean {
  return pinLayer ? pinned && !editMode : !pinned || editMode;
}

/** Does any widget in this list need a pin overlay to exist? Drives the lazy
 *  `set_pin_layer` build: nothing pinned → no second renderer per monitor. */
export function anyPinned(widgets: readonly Widget[]): boolean {
  return widgets.some(w => w.parentId === undefined && isWidgetPinned(w));
}
