// Geometry for the day/night cycle graphic. The curve is the real one: a body's
// altitude plotted against how far it is through its passage across the sky.
// That is not a semicircle -- it flattens in winter, towers in summer, and
// barely lifts off the horizon inside the polar circles. `sun.ts` supplies the
// altitudes; this file only turns them into pixels.
//
// `moonPhasePath` lives here too because the moon glyph in the rows is the same
// drawing problem: a disc with a lit fraction.

/** How much of the day/night graphic to draw. A closed set, not a loose
 *  string: the config panel, the context menu and the widget all read it. */
export type SunPathMode = 'hidden' | 'plain' | 'numbers';

const SUN_PATH_MODES: readonly SunPathMode[] = ['hidden', 'plain', 'numbers'];

export function isSunPathMode(v: unknown): v is SunPathMode {
  return typeof v === 'string' && (SUN_PATH_MODES as readonly string[]).includes(v);
}

export interface Point {
  x: number;
  y: number;
}

/** One altitude reading along a path. */
export interface Sample {
  /** 0..1 along the current stretch. */
  progress: number;
  /** Degrees above the horizon; negative below it. */
  altitude: number;
}

/** Where the plot sits in the drawing, and what its vertical axis means. */
export interface PathGeometry {
  /** Left and right edge: rise and set. */
  x0: number;
  x1: number;
  /** y of the horizon line, i.e. altitude zero. */
  horizonY: number;
  /** Pixels the axis gives to `axisMax` degrees of altitude. */
  aboveH: number;
  /** Top of the altitude axis, degrees. Set per location to the highest the sun
   *  gets there all year, so the curve uses the height available while the
   *  seasonal difference between a June arc and a December one stays visible. */
  axisMax: number;
  /** Pixels the night dip may use. The underside is compressed against the
   *  daylight scale on purpose: nobody reads a number off it, and at a winter
   *  latitude a true-to-scale midnight would be deeper than the whole widget. */
  belowH: number;
  /** Depth of the nadir, degrees, used to normalise that compression. */
  nightDepth: number;
}

const clamp = (v: number, lo: number, hi: number): number => (v < lo ? lo : v > hi ? hi : v);

/** Place one reading. */
export function pointAt(s: Sample, g: PathGeometry): Point {
  const x = g.x0 + (g.x1 - g.x0) * clamp(s.progress, 0, 1);
  if (s.altitude >= 0) {
    return { x, y: g.horizonY - clamp(s.altitude / g.axisMax, 0, 1) * g.aboveH };
  }
  const depth = g.nightDepth > 0 ? g.nightDepth : 1;
  return { x, y: g.horizonY + clamp(-s.altitude / depth, 0, 1) * g.belowH };
}

const fmt = (n: number): string => (Math.abs(n) < 1e-3 ? '0' : n.toFixed(2));

function polyline(pts: Point[]): string {
  return pts.map((p, i) => `${i ? 'L' : 'M'}${fmt(p.x)},${fmt(p.y)}`).join('');
}

/** The path through a run of readings. */
export function curvePath(samples: Sample[], g: PathGeometry): string {
  if (!samples.length) return '';
  return polyline(samples.map((s) => pointAt(s, g)));
}

/** The same run, closed down to the horizon, so elapsed daylight can be
 *  filled in behind the sun. */
export function areaPath(samples: Sample[], g: PathGeometry): string {
  if (samples.length < 2) return '';
  const pts = samples.map((s) => pointAt(s, g));
  const first = pts[0];
  const last = pts[pts.length - 1];
  return `${polyline(pts)}L${fmt(last.x)},${fmt(g.horizonY)}L${fmt(first.x)},${fmt(g.horizonY)}Z`;
}

/** The lit part of a moon disc, as a closed polyline.
 *
 *  Two arcs joined: the outer limb on the lit side, and the terminator, an
 *  ellipse whose half-width is `r·(1 − 2k)` -- positive for a crescent (bows
 *  toward the lit edge), zero at quarter (a straight line), negative for a
 *  gibbous (bows past the centre).
 *
 *  `phase` runs 0..1 from new to new; 0.5 is full. `southern` mirrors the disc,
 *  because a waxing moon is lit on the right from Zurich and on the left from
 *  Sydney. */
export function moonPhasePath(
  cx: number,
  cy: number,
  r: number,
  phase: number,
  southern = false,
  samples = 48,
): string {
  const p = ((phase % 1) + 1) % 1;
  const k = (1 - Math.cos(2 * Math.PI * p)) / 2; // illuminated fraction
  const side = (p < 0.5 ? 1 : -1) * (southern ? -1 : 1);
  const half = r * (1 - 2 * k);

  const pts: Point[] = [];
  for (let i = 0; i <= samples; i++) {
    const th = (Math.PI * i) / samples; // 0 = top, pi = bottom
    pts.push({ x: cx + side * r * Math.sin(th), y: cy - r * Math.cos(th) });
  }
  for (let i = samples; i >= 0; i--) {
    const th = (Math.PI * i) / samples;
    pts.push({ x: cx + side * half * Math.sin(th), y: cy - r * Math.cos(th) });
  }
  return `${polyline(pts)}Z`;
}
