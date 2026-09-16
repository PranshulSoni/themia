// [S-28] Sheet intros (`sides` = x, `topbottom` = y): which sheet a widget
// belongs to, and the ONE offset each sheet slides in from. Pure math, no DOM —
// the CSS keyframes only consume the `--wi-dx`/`--wi-dy` vars this builds.
//
// Both axes run the SAME cases (the module is one algorithm, parametrized), so
// the suites are generated per axis rather than written twice.

import { describe, it, expect } from 'vitest';
import {
  sheetAxisFor,
  sheetDirection,
  sheetOffsets,
  sheetFromVars,
  SHEET_MARGIN_PX,
  type SheetAxis,
} from './introSheets.ts';

const EXTENT = 1920;

/** A rect spanning [start, start+size] along `axis`, full-bleed on the other. */
function at(axis: SheetAxis, start: number, size: number) {
  return axis === 'x'
    ? { x: start, y: 0, w: size, h: 1 }
    : { x: 0, y: start, w: 1, h: size };
}

describe('S-28 sheet styles: only the two sheet intros have an axis', () => {
  it('maps each style to the axis it slides along', () => {
    expect(sheetAxisFor('sides')).toBe('x');
    expect(sheetAxisFor('topbottom')).toBe('y');
  });

  it('is null for every non-sheet style (no stagger suppression, no vars)', () => {
    for (const a of ['none', 'fade', 'scale', 'slide', 'flip', 'blur', 'hyperspace', 'hacker'] as const) {
      expect(sheetAxisFor(a)).toBeNull();
    }
  });
});

for (const [axis, varName, near, far] of [
  ['x', '--wi-dx', 'left', 'right'],
  ['y', '--wi-dy', 'top', 'bottom'],
] as const) {
  const rect = (start: number, size: number) => at(axis, start, size);
  /** The px the var carries for `r`, given the whole view. */
  const dxOf = (r: ReturnType<typeof rect>, all: ReturnType<typeof rect>[], e = EXTENT) =>
    Number(/-?\d+(?=px)/.exec(sheetFromVars(r, axis, sheetOffsets(all, axis, e)))![0]);

  describe(`S-28 ${axis} sheets: direction follows which half the widget sits in`, () => {
    it(`a widget wholly in the ${near} half enters from the ${near}`, () => {
      expect(sheetDirection(rect(0.05, 0.2), axis)).toBe(-1);
    });

    it(`a widget wholly in the ${far} half enters from the ${far}`, () => {
      expect(sheetDirection(rect(0.7, 0.25), axis)).toBe(1);
    });

    it('a straddling widget follows its CENTRE, not its edges', () => {
      // Spans 0.30→0.60: crosses the midline, but the majority is near.
      expect(sheetDirection(rect(0.3, 0.3), axis)).toBe(-1);
      // Spans 0.45→0.95: crosses the midline, majority far.
      expect(sheetDirection(rect(0.45, 0.5), axis)).toBe(1);
    });

    it(`a dead-centre widget goes ${far} (the "otherwise" branch)`, () => {
      expect(sheetDirection(rect(0.25, 0.5), axis)).toBe(1);
      expect(sheetDirection(rect(0, 1), axis)).toBe(1);
    });

    it('judges each axis independently of the other', () => {
      // Near on x, far on y: the same rect belongs to a different sheet per style.
      const r = { x: 0.05, y: 0.8, w: 0.2, h: 0.15 };
      expect(sheetDirection(r, 'x')).toBe(-1);
      expect(sheetDirection(r, 'y')).toBe(1);
    });
  });

  describe(`S-28 ${axis} sheets: each side moves as one rigid sheet`, () => {
    // Two on the near side at different depths, two on the far side likewise.
    const view = [rect(0.02, 0.18), rect(0.24, 0.20), rect(0.55, 0.15), rect(0.78, 0.20)];

    it('every widget on a sheet shares ONE offset, so spacing never changes', () => {
      const [n1, n2, f1, f2] = view.map((r) => dxOf(r, view));
      expect(n1).toBe(n2);
      expect(f1).toBe(f2);
      // …and the two sheets travel opposite ways.
      expect(n1).toBeLessThan(0);
      expect(f1).toBeGreaterThan(0);
    });

    it('the sheet is sized by its DEEPEST member, so every tile starts off-screen', () => {
      const sheets = sheetOffsets(view, axis, EXTENT);
      for (const r of view) {
        const d = sheetDirection(r, axis) < 0 ? sheets.near : sheets.far;
        const start = (axis === 'x' ? r.x : r.y) * EXTENT + d;
        const end = start + (axis === 'x' ? r.w : r.h) * EXTENT;
        expect(end <= 0 || start >= EXTENT).toBe(true);
      }
      // Near sheet set by the inner tile's far edge (0.44), not the outer one.
      expect(sheets.near).toBe(-Math.round(0.44 * EXTENT + SHEET_MARGIN_PX));
      // Far sheet by the innermost near edge (0.55).
      expect(sheets.far).toBe(Math.round(0.45 * EXTENT + SHEET_MARGIN_PX));
    });

    it('a lone widget still clears its own edge', () => {
      expect(dxOf(rect(0.1, 0.2), [rect(0.1, 0.2)])).toBe(-(576 + SHEET_MARGIN_PX));
      expect(dxOf(rect(0.75, 0.2), [rect(0.75, 0.2)])).toBe(480 + SHEET_MARGIN_PX);
    });

    it('an empty sheet contributes nothing (no phantom offset)', () => {
      expect(sheetOffsets([rect(0.05, 0.2), rect(0.3, 0.1)], axis, EXTENT)).toEqual({
        near: -Math.round(0.4 * EXTENT + SHEET_MARGIN_PX),
        far: 0,
      });
      expect(sheetOffsets([], axis, EXTENT)).toEqual({ near: 0, far: 0 });
    });

    it('is scale-correct: a smaller container means a shorter trip', () => {
      const solo = [rect(0.8, 0.15)];
      expect(dxOf(solo[0], solo, 1000)).toBeLessThan(dxOf(solo[0], solo, EXTENT));
    });

    it(`emits whole-pixel ${varName} text the keyframes can read`, () => {
      const sheets = sheetOffsets(view, axis, EXTENT);
      expect(sheetFromVars(view[0], axis, sheets)).toBe(`${varName}: -869px;`);
      expect(sheetFromVars(view[3], axis, sheets)).toBe(`${varName}: 888px;`);
    });

    it('degrades to the bare margin off-DOM (extent 0), never NaN', () => {
      expect(sheetOffsets([rect(0.1, 0.2), rect(0.8, 0.2)], axis, 0)).toEqual({
        near: -SHEET_MARGIN_PX,
        far: SHEET_MARGIN_PX,
      });
    });
  });
}
