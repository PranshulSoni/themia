// ---------------------------------------------------------------------------
// Grid geometry: fraction-of-screen math for the percent layout model.
//
// Widget geometry is stored as fractions of the active window's screen
// (see src/SPEC.md [S-01]). This module is the single home for the conversions
// the renderer, drag manager, placement and cross-window transport all share:
// px <-> fraction, snap-to-grid, on-screen clamping, and the minimum-size floor.
//
// Intentionally free of solid-js / DOM-framework imports so it stays unit
// testable. `window` is read defensively (SSR / node test env → 1920x1080).
// ---------------------------------------------------------------------------

/** A widget rect in screen fractions: x,y top-left ∈[0,1]; w,h size ∈(0,1]. */
export interface FracRect {
  x: number;
  y: number;
  w: number;
  h: number;
}

// Snap-grid cell target (px). Fine-grained snap (~10px cells); this only sets
// how many snap lines exist, the rendered cell is screen/cols.
export const MIN_CELL_PX = 10;

// Smallest a widget may be resized to, in physical px on the current screen
// ([S-08]).
export const MIN_WIDGET_PX = 32;

/** Active window's screen size in CSS px. Falls back to 1920x1080 off-DOM. */
export function screenSize(): { w: number; h: number } {
  return {
    w: typeof window !== 'undefined' ? window.innerWidth : 1920,
    h: typeof window !== 'undefined' ? window.innerHeight : 1080,
  };
}

/** Snap-grid granularity for the current screen ([S-06]). */
export function gridDims(): { cols: number; rows: number } {
  const { w, h } = screenSize();
  return {
    cols: Math.max(1, Math.round(w / MIN_CELL_PX)),
    rows: Math.max(1, Math.round(h / MIN_CELL_PX)),
  };
}

export function clamp01(v: number): number {
  return v < 0 ? 0 : v > 1 ? 1 : v;
}

/** Round a fraction to the nearest of `n` grid lines (n cells → n+1 lines). */
export function snapFrac(value: number, n: number): number {
  if (n <= 0) return value;
  return Math.round(value * n) / n;
}

/** Minimum size as a fraction of each axis, derived from MIN_WIDGET_PX. */
export function minFrac(): { w: number; h: number } {
  const { w, h } = screenSize();
  return { w: MIN_WIDGET_PX / Math.max(1, w), h: MIN_WIDGET_PX / Math.max(1, h) };
}

/**
 * Clamp a rect fully on-screen ([S-09]). Size is capped to the viewport first,
 * floored to the minimum size, then the origin is pulled in so the far edge
 * stays ≤ 1.
 */
export function clampRect(r: FracRect): FracRect {
  const min = minFrac();
  const w = Math.min(1, Math.max(min.w, r.w));
  const h = Math.min(1, Math.max(min.h, r.h));
  const x = Math.min(1 - w, Math.max(0, r.x));
  const y = Math.min(1 - h, Math.max(0, r.y));
  return { x, y, w, h };
}

/**
 * Snap a rect's origin and size to the grid when `enabled`, then clamp it
 * on-screen. Size is snapped to a minimum of one cell so a snapped widget never
 * collapses below a cell. No-op (just clamp) when snapping is disabled.
 */
export function snapRect(r: FracRect, enabled: boolean): FracRect {
  if (!enabled) return clampRect(r);
  const { cols, rows } = gridDims();
  const w = Math.max(1 / cols, snapFrac(r.w, cols));
  const h = Math.max(1 / rows, snapFrac(r.h, rows));
  const x = snapFrac(r.x, cols);
  const y = snapFrac(r.y, rows);
  return clampRect({ x, y, w, h });
}

/**
 * Convert legacy cell geometry to an on-screen-clamped fractional rect ([S-07]).
 * `cols`/`rows` are the grid the cell coords were authored against. Pure: the
 * one place the cell→percent migration math lives, so it can be tested without
 * importing the layout store.
 */
export function cellToFrac(
  col: number, row: number, colSpan: number, rowSpan: number,
  cols: number, rows: number,
): FracRect {
  const c = Math.max(1, cols), r = Math.max(1, rows);
  return clampRect({
    x: (col - 1) / c,
    y: (row - 1) / r,
    w: colSpan / c,
    h: rowSpan / r,
  });
}

/** A registry default px size → fraction of the current screen ([S-10]). */
export function pxSizeToFrac(px: { width: number; height: number }): { w: number; h: number } {
  const { w, h } = screenSize();
  return { w: clamp01(px.width / Math.max(1, w)), h: clamp01(px.height / Math.max(1, h)) };
}

/** A fractional size → physical px on the current screen (transport, [S-12]). */
export function fracSizeToPx(size: { w: number; h: number }): { width: number; height: number } {
  const { w, h } = screenSize();
  return { width: size.w * w, height: size.h * h };
}

// ---------------------------------------------------------------------------
// Device-pixel snapping ([S-04a])
// ---------------------------------------------------------------------------

/**
 * Device (physical) pixels per app-layer CSS px.
 *
 * A widget's inline `left/top/width/height` are app-layer CSS px, and they are
 * scaled TWICE before reaching the framebuffer: `#app.app-layer` is
 * `transform: scale(monitorScale)`, and the display maps CSS px to physical px
 * by `devicePixelRatio`. So one inline px is `monitorScale · dpr` device px.
 */
export function devicePxPerCssPx(monitorScale: number): number {
  const dpr = typeof window !== 'undefined' ? (window.devicePixelRatio || 1) : 1;
  const q = (monitorScale || 1) * (dpr || 1);
  return q > 0 && Number.isFinite(q) ? q : 1;
}

/** Round `v` so that `v · q` is a whole device pixel. */
export function snapToDevicePx(v: number, q: number): number {
  return q > 0 && Number.isFinite(q) ? Math.round(v * q) / q : v;
}

/** The absolute px box a widget tile is positioned at, in app-layer CSS px. */
export interface PxBox { left: number; top: number; width: number; height: number }

/**
 * A fractional rect → the px box the renderer writes into the tile's inline
 * style, inset by `gap/2` on every side so neighbours show a gap without
 * changing their logical rect ([S-04]).
 *
 * TWO roundings, and they do different jobs:
 *
 *  1. **Logical edges to a shared integer boundary.** `round((x+w)·W)` for one
 *     widget is `round(x2·W)` for the next, so adjacent tiles agree on the
 *     boundary between them instead of leaving the sub-pixel hairline a raw
 *     `calc(%)` produces (which also differed per axis, since W ≠ H).
 *
 *  2. **Rendered edges onto whole DEVICE pixels** ([S-04a]). Integer CSS px is
 *     NOT integer device px: at 125% Windows scaling (`q` = 1.25) three out of
 *     four integer edges land mid-pixel, and the tile's 1px border is then
 *     rasterised across two device rows at partial alpha — which reads as a
 *     bottom/right border that is missing or "clipped by about a pixel". It
 *     tracks the tile's POSITION AND SIZE (the bottom edge is top + height), so
 *     it hits some tiles and not others and looks maddeningly arbitrary.
 *     Snapping both edges makes the border a crisp whole-pixel band by
 *     construction, at any scale, in any position.
 *
 * Snapping edges (not origin + size separately) keeps width/height on the
 * device grid too, since a difference of two snapped values is itself snapped.
 */
export function widgetBox(r: FracRect, gap: number, W: number, H: number, q: number): PxBox {
  const g = gap / 2;
  const x0 = Math.round(r.x * W);
  const y0 = Math.round(r.y * H);
  const x1 = Math.round((r.x + r.w) * W);
  const y1 = Math.round((r.y + r.h) * H);
  const left   = snapToDevicePx(x0 + g, q);
  const top    = snapToDevicePx(y0 + g, q);
  const right  = snapToDevicePx(x1 - g, q);
  const bottom = snapToDevicePx(y1 - g, q);
  return {
    left,
    top,
    width:  Math.max(0, right - left),
    height: Math.max(0, bottom - top),
  };
}
