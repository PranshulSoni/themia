// Per-instance tray settings: what the icon looks like, what it is called, and
// how big its panel opens (Tray.SPEC [S-07] / [S-14]).
//
// Kept in a registry-free module — like `widgets/headerMode.ts` — so the rules
// stay node-importable for unit tests. `tray.ts` pulls in the widget registry
// (and through it every widget's JSX), which a plain `*.test.ts` cannot load.

/** Bounds for a flyout. The floor keeps a widget usable; the ceiling keeps the
 *  flyout a flyout rather than a second desktop. */
export const FLYOUT_MIN = { width: 160, height: 120 };
export const FLYOUT_MAX = { width: 720, height: 900 };

export interface FlyoutSize {
  width: number;
  height: number;
}

/** Clamp to [FLYOUT_MIN, FLYOUT_MAX] and round to whole pixels. A non-finite
 *  input falls back to the minimum rather than propagating NaN into a window
 *  size. Pure. */
export function clampFlyoutSize(size: FlyoutSize): FlyoutSize {
  const axis = (v: number, lo: number, hi: number) =>
    Number.isFinite(v) ? Math.round(Math.min(hi, Math.max(lo, v))) : lo;
  return {
    width: axis(size.width, FLYOUT_MIN.width, FLYOUT_MAX.width),
    height: axis(size.height, FLYOUT_MIN.height, FLYOUT_MAX.height),
  };
}

/**
 * A tray widget's flyout size in logical px ([S-07]).
 *
 * Stored on the instance config as `trayWidth`/`trayHeight`, the same place
 * `headerMode` and `transparentBg` live: they are app-level per-instance
 * settings, not widget options, and the config bag is where the app already
 * keeps those. Absent — or any non-numeric value, which is a corrupt config, not
 * a size of zero — falls back to the widget type's default, so a freshly added
 * tray widget opens at the size its author designed for. Pure.
 */
export function resolveFlyoutSize(
  config: Record<string, unknown> | undefined,
  fallback: FlyoutSize,
): FlyoutSize {
  // NaN and Infinity are corrupt config, not sizes: both fall back rather than
  // clamping to an edge, so a damaged value restores the author's default.
  const num = (v: unknown): number | null =>
    typeof v === 'number' && Number.isFinite(v) ? v : null;
  return clampFlyoutSize({
    width: num(config?.trayWidth) ?? fallback.width,
    height: num(config?.trayHeight) ?? fallback.height,
  });
}

/**
 * The icon catalogue key this tray widget overrides its icon with ([S-14]), or
 * `''` for "use the widget type's own icon".
 *
 * `isKnown` is injected rather than imported so this module stays free of the
 * catalogue (and its lucide JSX). An unrecognised stored key resolves to the
 * default: a catalogue entry can be retired, and when it is, the widget must
 * fall back to something that draws rather than to nothing at all. Pure.
 */
export function resolveTrayIconKey(
  config: Record<string, unknown> | undefined,
  isKnown: (key: string) => boolean,
): string {
  const raw = config?.trayIcon;
  return typeof raw === 'string' && isKnown(raw) ? raw : '';
}

/**
 * The tray icon's tooltip text ([S-14]): the widget's own tray name, else the
 * fallback the caller resolved (its title, else its type's label).
 *
 * A dedicated field rather than reusing the header title, because a tray widget
 * may well have no header at all — and then there would be nothing to name it
 * with. Whitespace-only is "unset", not a blank tooltip. Pure.
 */
export function resolveTrayName(
  config: Record<string, unknown> | undefined,
  fallback: string,
): string {
  const raw = config?.trayName;
  const trimmed = typeof raw === 'string' ? raw.trim() : '';
  return trimmed || fallback;
}
