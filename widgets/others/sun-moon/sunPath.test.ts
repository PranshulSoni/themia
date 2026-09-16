import { describe, it, expect } from 'vitest';
import { pointAt, curvePath, areaPath, moonPhasePath, type PathGeometry } from './sunPath.ts';

const G: PathGeometry = {
  x0: 22, x1: 178, horizonY: 116, aboveH: 96, axisMax: 66, belowH: 24, nightDepth: 27,
};

/** Every number in a path, as (x, y) pairs. The builders only emit M/L, so the
 *  coordinates are positional. */
function coords(d: string): { x: number; y: number }[] {
  const nums = (d.match(/-?\d+(\.\d+)?/g) ?? []).map(Number);
  const out: { x: number; y: number }[] = [];
  for (let i = 0; i + 1 < nums.length; i += 2) out.push({ x: nums[i], y: nums[i + 1] });
  return out;
}

describe('[S-11] pointAt maps altitude and progress onto the plot', () => {
  it('puts the horizon on the horizon, at either end', () => {
    expect(pointAt({ progress: 0, altitude: 0 }, G)).toEqual({ x: G.x0, y: G.horizonY });
    expect(pointAt({ progress: 1, altitude: 0 }, G)).toEqual({ x: G.x1, y: G.horizonY });
  });

  it('spaces progress evenly across the width', () => {
    expect(pointAt({ progress: 0.5, altitude: 0 }, G).x).toBeCloseTo((G.x0 + G.x1) / 2, 9);
    expect(pointAt({ progress: 0.25, altitude: 0 }, G).x)
      .toBeCloseTo(G.x0 + (G.x1 - G.x0) * 0.25, 9);
  });

  it('scales altitude linearly up to the axis top', () => {
    expect(pointAt({ progress: 0.5, altitude: G.axisMax }, G).y).toBeCloseTo(G.horizonY - G.aboveH, 9);
    expect(pointAt({ progress: 0.5, altitude: G.axisMax / 2 }, G).y)
      .toBeCloseTo(G.horizonY - G.aboveH / 2, 9);
  });

  it('clamps rather than drawing off the top when a body beats the axis', () => {
    expect(pointAt({ progress: 0.5, altitude: 89 }, G).y).toBeCloseTo(G.horizonY - G.aboveH, 9);
  });

  it('sends negative altitude into the night band, normalised to the nadir', () => {
    expect(pointAt({ progress: 0.5, altitude: -G.nightDepth }, G).y)
      .toBeCloseTo(G.horizonY + G.belowH, 9);
    expect(pointAt({ progress: 0.5, altitude: -G.nightDepth / 2 }, G).y)
      .toBeCloseTo(G.horizonY + G.belowH / 2, 9);
  });

  it('never leaves the plot box, whatever it is handed', () => {
    for (const altitude of [-90, -40, -1, 0, 1, 40, 90, 200]) {
      for (const progress of [-2, 0, 0.37, 1, 3]) {
        const p = pointAt({ progress, altitude }, G);
        expect(p.x).toBeGreaterThanOrEqual(G.x0);
        expect(p.x).toBeLessThanOrEqual(G.x1);
        expect(p.y).toBeGreaterThanOrEqual(G.horizonY - G.aboveH);
        expect(p.y).toBeLessThanOrEqual(G.horizonY + G.belowH);
      }
    }
  });
});

describe('[S-11] path builders', () => {
  const samples = [
    { progress: 0, altitude: 0 },
    { progress: 0.5, altitude: 40 },
    { progress: 1, altitude: 0 },
  ];

  it('draws through every reading in order', () => {
    const pts = coords(curvePath(samples, G));
    expect(pts).toHaveLength(3);
    expect(pts[1].y).toBeLessThan(pts[0].y); // higher altitude, smaller y
    expect(pts[0].x).toBeLessThan(pts[2].x);
  });

  it('closes the elapsed-daylight area back along the horizon', () => {
    const d = areaPath(samples, G);
    expect(d.endsWith('Z')).toBe(true);
    const pts = coords(d);
    expect(pts[pts.length - 1].y).toBeCloseTo(G.horizonY, 6);
    expect(pts[pts.length - 2].y).toBeCloseTo(G.horizonY, 6);
  });

  it('emits nothing rather than a broken path when there is nothing to draw', () => {
    expect(curvePath([], G)).toBe('');
    expect(areaPath([], G)).toBe('');
    expect(areaPath([{ progress: 0, altitude: 0 }], G)).toBe('');
  });
});

describe('[S-13] moon phase disc', () => {
  const span = (d: string) => {
    const xs = coords(d).map((c) => c.x);
    return { min: Math.min(...xs), max: Math.max(...xs) };
  };
  const atWidest = (d: string) => coords(d).filter((c) => Math.abs(c.y) < 1e-9).map((c) => c.x);

  it('is empty at new moon and a full disc at full moon', () => {
    expect(span(moonPhasePath(0, 0, 10, 0)).max).toBeCloseTo(10, 2);
    expect(span(moonPhasePath(0, 0, 10, 0)).min).toBeCloseTo(0, 2);
    const full = span(moonPhasePath(0, 0, 10, 0.5));
    expect(full.min).toBeCloseTo(-10, 2);
    expect(full.max).toBeCloseTo(10, 2);
  });

  it('lights the right half at first quarter and the left at last', () => {
    const first = span(moonPhasePath(0, 0, 10, 0.25));
    expect(first.min).toBeCloseTo(0, 2);
    expect(first.max).toBeCloseTo(10, 2);
    const last = span(moonPhasePath(0, 0, 10, 0.75));
    expect(last.min).toBeCloseTo(-10, 2);
    expect(last.max).toBeCloseTo(0, 2);
  });

  // The southern sky sees the same phase mirrored -- issue #105 came from a
  // southern-hemisphere user, so this is not a detail to get wrong.
  it('mirrors below the equator', () => {
    const north = span(moonPhasePath(0, 0, 10, 0.25, false));
    const south = span(moonPhasePath(0, 0, 10, 0.25, true));
    expect(south.min).toBeCloseTo(-north.max, 4);
    expect(south.max).toBeCloseTo(-north.min, 4);
  });

  it('bows toward the lit edge for a crescent and past centre for a gibbous', () => {
    const [limb, term] = atWidest(moonPhasePath(0, 0, 10, 0.1));
    expect(limb).toBeCloseTo(10, 2);
    expect(term).toBeGreaterThan(0);
    const [gLimb, gTerm] = atWidest(moonPhasePath(0, 0, 10, 0.4));
    expect(gLimb).toBeCloseTo(10, 2);
    expect(gTerm).toBeLessThan(0);
  });

  it('stays inside the disc at every phase', () => {
    for (let p = 0; p < 1; p += 0.02) {
      for (const c of coords(moonPhasePath(0, 0, 10, p))) {
        expect(Math.hypot(c.x, c.y)).toBeLessThanOrEqual(10 + 1e-2);
      }
    }
  });
});
