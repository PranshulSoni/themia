import { describe, it, expect } from 'vitest';
import { maskBand, maskRect } from './hitMask.ts';

/** The pre-fix math: floor the origin, ceil the SIZE, independently. */
function legacyMaskRect(
  left: number, top: number, w: number, h: number, r: number, dpr: number,
) {
  return [
    Math.floor(left * dpr), Math.floor(top * dpr),
    Math.ceil(w * dpr), Math.ceil(h * dpr), Math.round(r * dpr),
  ] as const;
}

const bottomOf = (m: readonly number[]) => m[1] + m[3];
const rightOf  = (m: readonly number[]) => m[0] + m[2];

describe('[S-17] hit-mask rect covers every device pixel the tile paints', () => {
  // The reported bug: at 125% Windows scaling a tile's bottom border row fell
  // outside SetWindowRgn, so the OS clipped it away — visibly, not just for
  // hit-testing. Fails with the old origin-floor + size-ceil math.
  it('S-17_ covers the bottom edge where origin-floor + size-ceil fell one row short', () => {
    const [left, top, w, h, dpr] = [20, 103, 260, 243, 1.25];

    // Characterize the old math so the regression is explicit, not implied.
    expect(bottomOf(legacyMaskRect(left, top, w, h, 8, dpr)))
      .toBeLessThan(Math.ceil((top + h) * dpr));

    const m = maskRect(left, top, w, h, 8, dpr);
    expect(bottomOf(m)).toBe(Math.ceil((top + h) * dpr));
    expect(bottomOf(m)).toBeGreaterThanOrEqual((top + h) * dpr);
  });

  it('S-17_ never under-covers any edge across dprs, offsets and sizes', () => {
    const dprs = [1, 1.25, 1.5, 1.75, 2, 2.5];
    for (const dpr of dprs) {
      for (let top = 0; top < 24; top++) {
        for (let h = 1; h < 24; h++) {
          const m = maskRect(7, top, 13, h, 8, dpr);
          // Superset of the real box on both axes.
          expect(m[1]).toBeLessThanOrEqual(top * dpr);
          expect(bottomOf(m)).toBeGreaterThanOrEqual((top + h) * dpr);
          expect(m[0]).toBeLessThanOrEqual(7 * dpr);
          expect(rightOf(m)).toBeGreaterThanOrEqual((7 + 13) * dpr);
        }
      }
    }
  });

  it('S-17_ overshoots by at most one device pixel per edge', () => {
    for (const dpr of [1, 1.25, 1.5, 2]) {
      for (let top = 0; top < 16; top++) {
        for (let h = 1; h < 16; h++) {
          const m = maskRect(7, top, 13, h, 8, dpr);
          expect(top * dpr - m[1]).toBeLessThan(1);
          expect(bottomOf(m) - (top + h) * dpr).toBeLessThan(1);
        }
      }
    }
  });

  // Precondition is a POSITIVE box (App.tsx drops `w <= 0 || h <= 0` before
  // calling, which is what keeps [S-18]'s "degenerate counts as empty" true).
  // Within that precondition a sub-pixel box must still round up to real area
  // rather than collapsing to nothing.
  it('S-17_ never collapses a positive box to zero area', () => {
    for (const dpr of [1, 1.25, 1.5, 2]) {
      const m = maskRect(10.4, 10.4, 0.2, 0.2, 0, dpr);
      expect(m[2]).toBeGreaterThan(0);
      expect(m[3]).toBeGreaterThan(0);
    }
  });

  it('S-17_ is an exact integer box at dpr 1', () => {
    expect(maskRect(20, 103, 260, 243, 8, 1)).toEqual([20, 103, 260, 243, 8]);
  });
});

// A tile that paints only part of its box publishes that part ([S-13a]): the
// group with a see-through area contributes its title bar, so the desktop under
// the area keeps its clicks instead of hitting an invisible window.
describe('[S-13a] published mask band', () => {
  it('S-13a_ parses the four app-layer px numbers, whitespace and all', () => {
    expect(maskBand('12,34,560,32')).toEqual([12, 34, 560, 32]);
    expect(maskBand(' 12 , 34 , 560 , 32 ')).toEqual([12, 34, 560, 32]);
    expect(maskBand('12.5,34.25,560,32')).toEqual([12.5, 34.25, 560, 32]);
  });

  it('S-13a_ absent or malformed falls back to the tile box, never to a partial band', () => {
    // Three good numbers and one NaN would clip the widget to garbage, which
    // the region renders as missing pixels — worse than masking the whole tile.
    for (const v of [undefined, null, '', '12,34,560', '12,34,560,32,9', '12,34,560,px',
                     'a,b,c,d', '12,34,-5,32']) {
      expect(maskBand(v), String(v)).toBeNull();
    }
  });

  it('S-13a_ a zero-size band is valid and passes through', () => {
    // The caller's `w <= 0` drop then removes the tile from the mask ([S-18]),
    // which is what a tile painting nothing should do.
    expect(maskBand('12,34,0,0')).toEqual([12, 34, 0, 0]);
  });
});
