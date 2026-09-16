import { describe, it, expect } from 'vitest';
import {
  cellToFrac,
  clampRect,
  snapRect,
  snapFrac,
  pxSizeToFrac,
  fracSizeToPx,
  gridDims,
  minFrac,
  MIN_WIDGET_PX,
  widgetBox,
  type FracRect,
} from './gridGeometry.ts';

// In the node test env `window` is undefined, so screenSize() falls back to
// 1920x1080; all screen-relative assertions below assume that fallback.
const SCREEN = { w: 1920, h: 1080 };

describe('cellToFrac: legacy cell → fraction migration [S-07]', () => {
  it('maps cell coords to proportional fractions', () => {
    // col/row are 1-based; a 3x2 widget at (1,1) on a 10x5 grid.
    const r = cellToFrac(1, 1, 3, 2, 10, 5);
    expect(r).toEqual({ x: 0, y: 0, w: 0.3, h: 0.4 });
  });

  it('preserves proportions for an off-origin widget', () => {
    const r = cellToFrac(5, 3, 2, 2, 10, 10);
    expect(r.x).toBeCloseTo(0.4, 10);
    expect(r.y).toBeCloseTo(0.2, 10);
    expect(r.w).toBeCloseTo(0.2, 10);
    expect(r.h).toBeCloseTo(0.2, 10);
  });

  it('keeps a far-edge widget fully on-screen [S-09]', () => {
    // Bottom-right cell of a 4x4 grid → x+w and y+h must stay ≤ 1.
    const r = cellToFrac(4, 4, 1, 1, 4, 4);
    expect(r.x + r.w).toBeLessThanOrEqual(1 + 1e-9);
    expect(r.y + r.h).toBeLessThanOrEqual(1 + 1e-9);
  });

  it('guards against a zero grid (never divides by 0)', () => {
    const r = cellToFrac(1, 1, 1, 1, 0, 0);
    expect(Number.isFinite(r.x)).toBe(true);
    expect(Number.isFinite(r.w)).toBe(true);
  });
});

describe('clampRect: on-screen + min-size [S-08][S-09]', () => {
  it('pulls an overflowing rect back on-screen', () => {
    const r = clampRect({ x: 0.95, y: 0.95, w: 0.2, h: 0.2 });
    expect(r.x + r.w).toBeLessThanOrEqual(1 + 1e-9);
    expect(r.y + r.h).toBeLessThanOrEqual(1 + 1e-9);
    expect(r.x).toBeGreaterThanOrEqual(0);
    expect(r.y).toBeGreaterThanOrEqual(0);
  });

  it('enforces the pixel min-size floor', () => {
    const r = clampRect({ x: 0.5, y: 0.5, w: 0, h: 0 });
    expect(r.w).toBeCloseTo(MIN_WIDGET_PX / SCREEN.w, 6);
    expect(r.h).toBeCloseTo(MIN_WIDGET_PX / SCREEN.h, 6);
  });

  it('clamps negative origin to 0', () => {
    const r = clampRect({ x: -0.3, y: -0.1, w: 0.2, h: 0.2 });
    expect(r.x).toBe(0);
    expect(r.y).toBe(0);
  });
});

describe('snapRect / snapFrac: grid snap aid [S-06]', () => {
  it('snaps to the nearest grid line when enabled', () => {
    const { cols, rows } = gridDims();
    const r = snapRect({ x: 0.123, y: 0.456, w: 0.2, h: 0.2 }, true);
    // x and y land on a 1/cols, 1/rows line.
    expect(Math.abs(r.x * cols - Math.round(r.x * cols))).toBeLessThan(1e-6);
    expect(Math.abs(r.y * rows - Math.round(r.y * rows))).toBeLessThan(1e-6);
  });

  it('does not snap when disabled (only clamps)', () => {
    const r = snapRect({ x: 0.123, y: 0.456, w: 0.2, h: 0.2 }, false);
    expect(r.x).toBeCloseTo(0.123, 10);
    expect(r.y).toBeCloseTo(0.456, 10);
  });

  it('snapFrac rounds to 1/n steps', () => {
    expect(snapFrac(0.26, 4)).toBeCloseTo(0.25, 10); // nearest quarter
    expect(snapFrac(0.4, 0)).toBe(0.4);              // n=0 → passthrough
  });
});

describe('px <-> fraction transport [S-12]', () => {
  it('round-trips a size through px and back', () => {
    const frac = { w: 0.25, h: 0.3 };
    const px = fracSizeToPx(frac);
    expect(px.width).toBeCloseTo(SCREEN.w * 0.25, 6);
    const back = pxSizeToFrac(px);
    expect(back.w).toBeCloseTo(frac.w, 10);
    expect(back.h).toBeCloseTo(frac.h, 10);
  });

  it('minFrac shrinks on a larger nominal screen', () => {
    const m = minFrac();
    expect(m.w).toBeCloseTo(MIN_WIDGET_PX / SCREEN.w, 6);
  });
});

// ---------------------------------------------------------------------------
// [S-04a] Device-pixel snapping
// ---------------------------------------------------------------------------

/** The pre-fix math: integer CSS px, gap applied without a device-pixel snap. */
function legacyBox(r: FracRect, gap: number, W: number, H: number) {
  const g = gap / 2;
  const x0 = Math.round(r.x * W), y0 = Math.round(r.y * H);
  const x1 = Math.round((r.x + r.w) * W), y1 = Math.round((r.y + r.h) * H);
  return {
    left: x0 + g, top: y0 + g,
    width: Math.max(0, x1 - x0 - gap), height: Math.max(0, y1 - y0 - gap),
  };
}

const onDeviceGrid = (v: number, q: number) => Math.abs(v * q - Math.round(v * q)) < 1e-6;

describe('widgetBox: rendered edges land on whole device pixels [S-04a]', () => {
  // The reported bug: a tile's bottom/right border read as clipped by ~1px,
  // depending on where the tile sat. Integer CSS px is not integer device px —
  // at 125% scaling 3 of 4 edges fall mid-pixel and the 1px border is split
  // across two device rows at partial alpha.
  it('S-04a_ no bottom edge is split, where the CSS-px-only math split most of them', () => {
    const q = 1.25; // 125% Windows scaling
    // Walk a tile down the screen one logical row at a time, exactly how the
    // user hit it: same widget, different position → border there or not.
    const rects: FracRect[] = [];
    for (let i = 0; i < 200; i++) rects.push({ x: 0.1, y: i / 1080, w: 0.2, h: 0.225 });

    const legacySplit = rects.filter(
      r => !onDeviceGrid(legacyBox(r, 12, 1920, 1080).top + legacyBox(r, 12, 1920, 1080).height, q),
    ).length;
    const snappedSplit = rects.filter(r => {
      const b = widgetBox(r, 12, 1920, 1080, q);
      return !onDeviceGrid(b.top + b.height, q);
    }).length;

    // The bug, quantified: most positions rendered a half-pixel bottom border.
    expect(legacySplit).toBeGreaterThan(rects.length / 2);
    expect(snappedSplit).toBe(0);
  });

  it('S-04a_ every edge is device-aligned across scales, gaps, positions and sizes', () => {
    for (const q of [1, 1.25, 1.5, 1.75, 2, 2.5, 3]) {
      for (const gap of [0, 1, 5, 12, 24]) {          // odd gaps too: g = gap/2 is .5
        for (let i = 0; i < 40; i++) {
          const r: FracRect = {
            x: i / 97, y: (i * 7) / 101,
            w: 0.05 + (i % 11) / 53, h: 0.05 + (i % 13) / 59,
          };
          const b = widgetBox(r, gap, 1920, 1080, q);
          expect(onDeviceGrid(b.left, q)).toBe(true);
          expect(onDeviceGrid(b.top, q)).toBe(true);
          expect(onDeviceGrid(b.left + b.width, q)).toBe(true);
          expect(onDeviceGrid(b.top + b.height, q)).toBe(true);
          // Size stays on the grid too, so the border band is a whole number
          // of device rows on every side.
          expect(onDeviceGrid(b.width, q)).toBe(true);
          expect(onDeviceGrid(b.height, q)).toBe(true);
        }
      }
    }
  });

  // [S-04] must survive the new snap: neighbours still meet on ONE edge.
  it('S-04a_ adjacent tiles still share an exact boundary at gap 0', () => {
    for (const q of [1, 1.25, 1.5, 2]) {
      const left:  FracRect = { x: 0.1,  y: 0.2, w: 0.237, h: 0.3 };
      const right: FracRect = { x: 0.337, y: 0.2, w: 0.2,  h: 0.3 };
      const a = widgetBox(left, 0, 1920, 1080, q);
      const b = widgetBox(right, 0, 1920, 1080, q);
      expect(a.left + a.width).toBeCloseTo(b.left, 9);
    }
  });

  it('S-04a_ still insets by the gap and never goes negative', () => {
    const r: FracRect = { x: 0.1, y: 0.2, w: 0.2, h: 0.3 };
    const gapped = widgetBox(r, 12, 1920, 1080, 1);
    const flush  = widgetBox(r, 0,  1920, 1080, 1);
    expect(flush.width - gapped.width).toBeCloseTo(12, 6);
    expect(gapped.left - flush.left).toBeCloseTo(6, 6);

    const tiny = widgetBox({ x: 0.5, y: 0.5, w: 0.001, h: 0.001 }, 200, 1920, 1080, 1.25);
    expect(tiny.width).toBe(0);
    expect(tiny.height).toBe(0);
  });

  it('S-04a_ degenerate scale factors fall back to a no-op snap', () => {
    for (const q of [0, -1, NaN, Infinity]) {
      const b = widgetBox({ x: 0.1, y: 0.2, w: 0.2, h: 0.3 }, 12, 1920, 1080, q);
      expect(Number.isFinite(b.left)).toBe(true);
      expect(Number.isFinite(b.width)).toBe(true);
    }
  });
});
