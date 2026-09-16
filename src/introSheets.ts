// Sheet intros ([S-28]): the two styles that slide a view in from opposite
// screen edges as RIGID SHEETS — `sides` along the x axis (left/right),
// `topbottom` along the y axis (top/bottom). One algorithm, parametrized by
// axis, never two copies of it.
//
// A widget joins the NEAR sheet (left / top) when its CENTRE sits in the first
// half of that axis, otherwise the FAR one (right / bottom) — including a
// dead-centre widget. The two sheets fly in from their own edge and meet in the
// middle.
//
// Each sheet moves as ONE BLOCK: every widget in it shares a single offset,
// sized so the deepest-reaching member clears the screen, and every widget
// starts on the same frame (the [S-15] stagger is suppressed for these styles
// in WidgetGrid.tsx). Per-widget offsets would give each tile its own distance,
// hence its own speed, and the group would arrive smeared out behind itself
// instead of as one sheet.
//
// Pure — no DOM, no solid-js — so the rules are unit pinnable, exactly like
// hyperspaceFromVars in hyperspace.ts. The grid feeds the result in as an
// inline `--wi-dx`/`--wi-dy` CSS var; the `wi-sheet` keyframes (WidgetGrid.css)
// translate from those to identity. Distances are in the GRID CONTAINER's px
// space (viewport ÷ monitor scale, the space `rectStyle` lays out in), so the
// trip is scale-correct on every monitor.

import type { FracRect } from './gridGeometry.ts';
import type { WidgetStartupAnimation } from './design.ts';

/** Which axis a sheet style slides along: 'x' = left/right, 'y' = top/bottom. */
export type SheetAxis = 'x' | 'y';

/**
 * Extra travel past the screen edge, px. The sheet starts fully off-screen plus
 * this margin, so borders, shadows and any glow clear the edge before the first
 * painted frame instead of peeking in.
 */
export const SHEET_MARGIN_PX = 24;

/** The two sheets' shared start offsets, px (negative = off the near edge). */
export interface SheetOffsets {
  /** The left / top sheet. */
  near: number;
  /** The right / bottom sheet. */
  far: number;
}

/**
 * The axis `anim` slides along, or `null` when it is not a sheet style. The one
 * place that answers "is this a sheet intro?", so the grid's offset source and
 * its stagger suppression can never disagree about which styles qualify.
 */
export function sheetAxisFor(anim: WidgetStartupAnimation): SheetAxis | null {
  return anim === 'sides' ? 'x' : anim === 'topbottom' ? 'y' : null;
}

/** A rect's start coordinate and size along `axis`, as screen fractions. */
function span(rect: FracRect, axis: SheetAxis): { start: number; size: number } {
  return axis === 'x' ? { start: rect.x, size: rect.w } : { start: rect.y, size: rect.h };
}

/**
 * Which sheet a widget belongs to ([S-28]): `-1` = the near one (left / top),
 * `+1` = the far one (right / bottom). The test is on the widget's CENTRE along
 * the axis, i.e. "is the majority of the widget in the first half of the
 * screen"; a dead-centre widget goes far.
 */
export function sheetDirection(rect: FracRect, axis: SheetAxis): -1 | 1 {
  const { start, size } = span(rect, axis);
  return start + size / 2 < 0.5 ? -1 : 1;
}

/**
 * The single offset each sheet animates FROM, given every top-level widget on
 * the view and the container's size along the axis (`extentPx`: width for 'x',
 * height for 'y'). The near sheet is pushed out by its deepest member's far
 * edge, the far sheet by its shallowest member's near edge, so the last tile of
 * each group is off-screen at the start and the group's internal spacing never
 * changes. A sheet with no members gets 0 (nothing reads it).
 */
export function sheetOffsets(
  rects: readonly FracRect[],
  axis: SheetAxis,
  extentPx: number,
): SheetOffsets {
  const e = Math.max(0, extentPx);
  let nearExtent = -Infinity; // largest far edge among near-sheet widgets
  let farExtent = Infinity;   // smallest near edge among far-sheet widgets
  for (const r of rects) {
    const { start, size } = span(r, axis);
    if (sheetDirection(r, axis) < 0) nearExtent = Math.max(nearExtent, start + size);
    else farExtent = Math.min(farExtent, start);
  }
  return {
    near: nearExtent === -Infinity ? 0 : -Math.round(nearExtent * e + SHEET_MARGIN_PX),
    far: farExtent === Infinity ? 0 : Math.round((1 - farExtent) * e + SHEET_MARGIN_PX),
  };
}

/** The inline CSS var the `wi-sheet` keyframes read, for one widget ([S-28]). */
export function sheetFromVars(rect: FracRect, axis: SheetAxis, sheets: SheetOffsets): string {
  const dx = sheetDirection(rect, axis) < 0 ? sheets.near : sheets.far;
  return `${axis === 'x' ? '--wi-dx' : '--wi-dy'}: ${dx}px;`;
}
