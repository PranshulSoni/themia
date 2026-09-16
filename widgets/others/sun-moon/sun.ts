// Local astronomy, no network. Sunrise/sunset via the standard "sunrise
// equation" (Wikipedia), moon phase via the synodic month from a known new moon.
// Accuracy is ~1-2 minutes for sun times, plenty for a desktop widget.

const DEG = Math.PI / 180;

function toJulian(date: Date): number {
  return date.getTime() / 86_400_000 + 2440587.5;
}

function fromJulian(j: number): Date {
  return new Date((j - 2440587.5) * 86_400_000);
}

export interface SunTimes {
  /** null when the sun never rises/sets on this day (polar day/night). */
  sunrise: Date | null;
  sunset: Date | null;
  polar: 'day' | 'night' | null;
}

export function sunTimes(date: Date, lat: number, lon: number): SunTimes {
  const jd = toJulian(date);
  // Index the *solar* day at `lon`, not the UTC day. Solar noon at +151°
  // (Sydney) falls ~10 h before noon UTC, so keying off the UTC day handed back
  // the previous day's times right through the local morning. [S-06]
  const n = Math.round(jd - 2451545.0 + 0.0008 + lon / 360);
  const jStar = n - lon / 360;
  const M = (357.5291 + 0.98560028 * jStar) % 360;
  const Mr = M * DEG;
  const C = 1.9148 * Math.sin(Mr) + 0.02 * Math.sin(2 * Mr) + 0.0003 * Math.sin(3 * Mr);
  const lambda = (M + C + 180 + 102.9372) % 360;
  const lr = lambda * DEG;
  const jTransit = 2451545.0 + jStar + 0.0053 * Math.sin(Mr) - 0.0069 * Math.sin(2 * lr);
  const sinDec = Math.sin(lr) * Math.sin(23.44 * DEG);
  const cosDec = Math.cos(Math.asin(sinDec));
  const latR = lat * DEG;
  const cosOmega = (Math.sin(-0.833 * DEG) - Math.sin(latR) * sinDec) / (Math.cos(latR) * cosDec);

  if (cosOmega > 1) return { sunrise: null, sunset: null, polar: 'night' };
  if (cosOmega < -1) return { sunrise: null, sunset: null, polar: 'day' };

  const omega = Math.acos(cosOmega) / DEG;
  return {
    sunrise: fromJulian(jTransit - omega / 360),
    sunset: fromJulian(jTransit + omega / 360),
    polar: null,
  };
}

export interface NextEvent {
  kind: 'sunrise' | 'sunset';
  at: Date;
}

/** The next sunrise/sunset strictly after `now`. Collects every event from the
 *  solar day before, of, and after `now` and takes the earliest still ahead, so
 *  the answer can never be a past event however the solar day lines up with the
 *  local clock. Returns null at the poles (no event in the window). [S-07] */
export function nextSunEvent(now: Date, lat: number, lon: number): NextEvent | null {
  const events: NextEvent[] = [];
  for (let day = -1; day <= 1; day++) {
    const s = sunTimes(new Date(now.getTime() + day * 86_400_000), lat, lon);
    if (s.sunrise) events.push({ kind: 'sunrise', at: s.sunrise });
    if (s.sunset) events.push({ kind: 'sunset', at: s.sunset });
  }
  events.sort((a, b) => a.at.getTime() - b.at.getTime());
  return events.find((e) => e.at.getTime() > now.getTime()) ?? null;
}

// ── Ground tracks ────────────────────────────────────────────────────────────
// Where a body is directly overhead. The globe draws the sun and the moon at
// these points, so they need the *instantaneous* position -- the rounded day
// index the sunrise equation uses above would pin them to a single moment of
// the day and they would never move.

const OBLIQUITY = 23.4397 * DEG;

/** A point on the Earth's surface: degrees north, degrees east. */
export interface GroundPoint {
  /** -90..90, north positive. */
  lat: number;
  /** -180..180, east positive. */
  lon: number;
}

const norm360 = (deg: number): number => ((deg % 360) + 360) % 360;
const norm180 = (deg: number): number => {
  const r = norm360(deg);
  return r > 180 ? r - 360 : r;
};

/** Greenwich mean sidereal time, degrees (IAU low-precision expression). */
function gmst(daysSinceJ2000: number): number {
  return norm360(280.46061837 + 360.98564736629 * daysSinceJ2000);
}

/** Equatorial coordinates -> the sub-body point. The declination *is* the
 *  latitude; the longitude is the right ascension measured back from the
 *  Greenwich meridian's current sidereal position. */
function groundPoint(raDeg: number, decDeg: number, daysSinceJ2000: number): GroundPoint {
  return { lat: decDeg, lon: norm180(raDeg - gmst(daysSinceJ2000)) };
}

/** The point with the moon straight overhead. Truncated lunar series (Meeus,
 *  low precision): good to ~0.3°, which is finer than the marker is wide. */
export function sublunarPoint(date: Date): GroundPoint {
  const d = toJulian(date) - 2451545.0;
  const L = (218.316 + 13.176396 * d) * DEG; // mean longitude
  const M = (134.963 + 13.064993 * d) * DEG; // mean anomaly
  const F = (93.272 + 13.229350 * d) * DEG;  // argument of latitude
  const lambda = L + 6.289 * DEG * Math.sin(M);
  const beta = 5.128 * DEG * Math.sin(F);
  const ra = Math.atan2(
    Math.sin(lambda) * Math.cos(OBLIQUITY) - Math.tan(beta) * Math.sin(OBLIQUITY),
    Math.cos(lambda),
  ) / DEG;
  const dec = Math.asin(
    Math.sin(beta) * Math.cos(OBLIQUITY) + Math.cos(beta) * Math.sin(OBLIQUITY) * Math.sin(lambda),
  ) / DEG;
  return groundPoint(ra, dec, d);
}

/** The point with the sun straight overhead. Same solar series as `sunTimes`,
 *  carried through to right ascension instead of stopping at declination. */
export function subsolarPoint(date: Date): GroundPoint {
  const d = toJulian(date) - 2451545.0;
  const M = norm360(357.5291 + 0.98560028 * d) * DEG;
  const C = (1.9148 * Math.sin(M) + 0.02 * Math.sin(2 * M) + 0.0003 * Math.sin(3 * M)) * DEG;
  const lambda = M + C + (180 + 102.9372) * DEG;
  const ra = Math.atan2(Math.cos(OBLIQUITY) * Math.sin(lambda), Math.cos(lambda)) / DEG;
  const dec = Math.asin(Math.sin(OBLIQUITY) * Math.sin(lambda)) / DEG;
  return groundPoint(ra, dec, d);
}

// ── Sky path ─────────────────────────────────────────────────────────────────
// The shape the widget draws is the real one: a body's altitude against its
// hour angle. It is not a semicircle. How high it climbs depends on the
// observer's latitude and the body's declination, so the curve flattens in
// winter, towers in summer, and is barely a bump inside the polar circles.

const clamp01 = (v: number): number => (v < 0 ? 0 : v > 1 ? 1 : v);

/** Altitude above the horizon, degrees, at hour angle `H`. Negative below. */
export function altitudeAtHourAngle(H: number, dec: number, lat: number): number {
  const latR = lat * DEG;
  const decR = dec * DEG;
  return Math.asin(
    Math.sin(latR) * Math.sin(decR) + Math.cos(latR) * Math.cos(decR) * Math.cos(H * DEG),
  ) / DEG;
}

/** Half the time a body spends above the horizon, as an hour angle in degrees.
 *  0 when it never rises, 180 when it never sets. This is what makes the ends
 *  of the drawn arc land exactly on rise and set. */
export function semiDiurnalArc(dec: number, lat: number): number {
  const c = -Math.tan(lat * DEG) * Math.tan(dec * DEG);
  if (c <= -1) return 180; // circumpolar: never sets
  if (c >= 1) return 0;    // never rises
  return Math.acos(c) / DEG;
}

/** Hour angle at a point of progress along one of the two stretches.
 *
 *  Above the horizon, progress 0..1 runs rise -> meridian -> set, so `H` runs
 *  -omega0 .. +omega0. Below, it carries on the same way round the sky: set ->
 *  nadir -> rise, i.e. +omega0 .. 180 / -180 .. -omega0. */
export function hourAngleAt(progress: number, omega0: number, below: boolean): number {
  const p = clamp01(progress);
  if (!below) return (2 * p - 1) * omega0;
  const half = 180 - omega0;
  return p <= 0.5 ? omega0 + 2 * p * half : -180 + (p - 0.5) * 2 * half;
}

export interface SkyPosition {
  /** Above the horizon? */
  up: boolean;
  /** 0 at rise, 0.5 at the meridian, 1 at set; then 0 at set, 0.5 at the
   *  nadir, 1 back at rise. */
  progress: number;
  /** Degrees above the horizon; negative below it. */
  altitude: number;
}

/** Where a body actually is in the observer's sky right now. */
export function skyPosition(body: GroundPoint, lat: number, lon: number): SkyPosition {
  // Hour angle: how far west of the observer's meridian the body is. Negative
  // means it is still east, i.e. on its way up.
  const H = norm180(lon - body.lon);
  const altitude = altitudeAtHourAngle(H, body.lat, lat);
  const omega0 = semiDiurnalArc(body.lat, lat);
  const up = altitude >= 0;
  if (up) {
    // omega0 is 0 only when the body never rises, which contradicts `up`.
    return { up, progress: omega0 > 0 ? clamp01(0.5 + H / (2 * omega0)) : 0.5, altitude };
  }
  const half = 180 - omega0;
  if (half <= 0) return { up, progress: 0.5, altitude };
  return {
    up,
    progress: clamp01(H >= 0 ? (H - omega0) / (2 * half) : 0.5 + (H + 180) / (2 * half)),
    altitude,
  };
}

/** The path itself: altitude sampled evenly along one stretch, for drawing.
 *  `from`/`to` narrow it to a part of the stretch -- the elapsed-daylight fill
 *  is the same curve stopped at the sun. */
export function skyPathSamples(
  dec: number,
  lat: number,
  below: boolean,
  steps = 48,
  from = 0,
  to = 1,
): { progress: number; altitude: number }[] {
  const omega0 = semiDiurnalArc(dec, lat);
  const out: { progress: number; altitude: number }[] = [];
  for (let i = 0; i <= steps; i++) {
    const progress = from + ((to - from) * i) / steps;
    out.push({ progress, altitude: altitudeAtHourAngle(hourAngleAt(progress, omega0, below), dec, lat) });
  }
  return out;
}

export interface MoonInfo {
  /** 0..1 through the lunar cycle (0 = new, 0.5 = full). */
  phase: number;
  /** 0..1 fraction of the disc lit. */
  illumination: number;
  name: string;
}

const PHASE_NAMES = [
  'New Moon', 'Waxing Crescent', 'First Quarter', 'Waxing Gibbous',
  'Full Moon', 'Waning Gibbous', 'Last Quarter', 'Waning Crescent',
];

export function moonInfo(date: Date): MoonInfo {
  const jd = toJulian(date);
  const SYNODIC = 29.53058867;
  const KNOWN_NEW = 2451550.1; // 2000-01-06 18:14 UTC new moon
  let phase = ((jd - KNOWN_NEW) / SYNODIC) % 1;
  if (phase < 0) phase += 1;
  const illumination = (1 - Math.cos(2 * Math.PI * phase)) / 2;
  const idx = Math.round(phase * 8) % 8;
  return { phase, illumination, name: PHASE_NAMES[idx] };
}
