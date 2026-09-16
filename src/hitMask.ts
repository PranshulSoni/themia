/**
 * Hit-mask rect math ([S-17]).
 *
 * Every tracked element (widget tiles + `[data-themia-interactive]` chrome) is
 * shipped to Rust as a device-pixel `[x, y, w, h, radius]` rect;
 * `update_widget_rects` OR's them into one HRGN and hands it to `SetWindowRgn`.
 * The region is a HARD CLIP: a pixel outside it is not part of the window, so
 * it is neither hit-tested NOR PAINTED. A mask rect that falls short of the
 * element's real device box therefore erases visible pixels, it doesn't just
 * make them click-through.
 *
 * That is why the conversion has to snap OUTWARD PER EDGE. Flooring the origin
 * and ceiling the SIZE independently is not equivalent and under-covers:
 *
 *     floor(a) + ceil(b)  <  ceil(a + b)      whenever frac(a) + frac(b) > 1
 *
 * e.g. a widget at top 103 CSS px, height 243, at dpr 1.25:
 *     floor(103·1.25) + ceil(243·1.25) = 128 + 304 = 432
 *     ceil((103 + 243)·1.25)                        = 433   ← one row short
 * and that missing row is the tile's bottom border, clipped away by the OS.
 * It reads as intermittent because it depends on the fractional parts: whether
 * a given tile is affected changes with its position and the monitor's dpr.
 * (It also LOOKS self-healing on hover over anything `data-focusable`, since
 * that makes the window interactive and interactive clears the region.)
 */

/** A device-pixel mask rect as shipped over IPC: `[x, y, w, h, radius]`. */
export type MaskRect = [number, number, number, number, number];

/**
 * Parse a tile's `data-mask-rect` band ([S-13a]): `left,top,width,height` in
 * app-layer CSS px — the same space the inline rect uses, because a tile that
 * paints only part of its box publishes the painted part rather than being
 * measured (measuring folds in the intro transform, see the note above).
 *
 * Returns null for absent OR malformed input, so the caller falls back to the
 * tile's own box. Never a partial band: three good numbers and one NaN would
 * clip a widget to garbage, which the region renders as missing pixels.
 * A zero-size band IS valid and passes through — the caller's `w <= 0` drop
 * then removes the tile from the mask entirely ([S-18]), which is what a tile
 * that paints nothing should do.
 */
export function maskBand(v: string | undefined | null): [number, number, number, number] | null {
  if (!v) return null;
  const parts = v.split(',');
  if (parts.length !== 4) return null;
  const n = parts.map(p => Number(p.trim()));
  if (n.some(x => !Number.isFinite(x))) return null;
  if (n[2] < 0 || n[3] < 0) return null;
  return [n[0], n[1], n[2], n[3]];
}

/**
 * Convert a viewport-CSS-px box to the device-px mask rect that covers every
 * pixel the element can paint into.
 *
 * Snapping is per EDGE (floor the left/top, ceil the right/bottom) so the
 * region is always a superset of the rendered box. Erring outward costs at
 * most one physical pixel of desktop click-through per edge; erring inward
 * deletes painted pixels.
 *
 * Expects a POSITIVE box: the caller drops `w <= 0 || h <= 0` before this, which
 * is what keeps [S-18]'s "degenerate rects count as empty" true.
 */
export function maskRect(
  left: number,
  top: number,
  width: number,
  height: number,
  radiusCssPx: number,
  dpr: number,
): MaskRect {
  const x0 = Math.floor(left * dpr);
  const y0 = Math.floor(top * dpr);
  const x1 = Math.ceil((left + width) * dpr);
  const y1 = Math.ceil((top + height) * dpr);
  return [x0, y0, x1 - x0, y1 - y0, Math.round(radiusCssPx * dpr)];
}
