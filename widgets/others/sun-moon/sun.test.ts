import { describe, it, expect } from 'vitest';
import {
  sunTimes, moonInfo, nextSunEvent, sublunarPoint, subsolarPoint,
  skyPosition, skyPathSamples, semiDiurnalArc, altitudeAtHourAngle, hourAngleAt,
} from './sun.ts';

/** Calendar date at a fixed UTC offset, so these assertions do not depend on
 *  the machine's own timezone. */
const localDate = (d: Date, tzHours: number) =>
  new Date(d.getTime() + tzHours * 3_600_000).toISOString().slice(0, 10);

/** The instant of `hour:30` local time on 2026-08-10 at a place `tzHours` off UTC. */
const localInstant = (hour: number, tzHours: number) =>
  new Date(Date.UTC(2026, 7, 10, hour - tzHours, 30));

// 2000-01-06 18:14 UTC reference new moon (the constant the model is anchored to).
const KNOWN_NEW_MS = (2451550.1 - 2440587.5) * 86_400_000;

describe('sun: moonInfo', () => {
  it('reports a new moon at the anchor date', () => {
    const m = moonInfo(new Date(KNOWN_NEW_MS));
    expect(m.phase).toBeLessThan(0.02);
    expect(m.illumination).toBeLessThan(0.02);
    expect(m.name).toBe('New Moon');
  });

  it('reports a full moon ~14.77 days later', () => {
    const m = moonInfo(new Date(KNOWN_NEW_MS + 14.765 * 86_400_000));
    expect(m.phase).toBeGreaterThan(0.48);
    expect(m.phase).toBeLessThan(0.52);
    expect(m.illumination).toBeGreaterThan(0.98);
    expect(m.name).toBe('Full Moon');
  });

  it('keeps illumination within [0, 1]', () => {
    for (let d = 0; d < 30; d++) {
      const m = moonInfo(new Date(KNOWN_NEW_MS + d * 86_400_000));
      expect(m.illumination).toBeGreaterThanOrEqual(0);
      expect(m.illumination).toBeLessThanOrEqual(1);
    }
  });
});

describe('sun: sunTimes', () => {
  it('returns sunrise before sunset at a temperate latitude', () => {
    const s = sunTimes(new Date(Date.UTC(2024, 5, 21, 12)), 47.37, 8.54); // Zurich, midsummer
    expect(s.polar).toBeNull();
    expect(s.sunrise).not.toBeNull();
    expect(s.sunset).not.toBeNull();
    expect(s.sunrise!.getTime()).toBeLessThan(s.sunset!.getTime());
  });

  it('flags polar day inside the Arctic Circle in summer', () => {
    const s = sunTimes(new Date(Date.UTC(2024, 5, 21, 12)), 78, 15); // Svalbard
    expect(s.polar).toBe('day');
    expect(s.sunrise).toBeNull();
    expect(s.sunset).toBeNull();
  });

  it('returns sunrise before sunset at a southern temperate latitude', () => {
    const s = sunTimes(new Date(Date.UTC(2026, 7, 9, 23)), -33.87, 151.21); // Sydney
    expect(s.polar).toBeNull();
    expect(s.sunrise!.getTime()).toBeLessThan(s.sunset!.getTime());
  });

  it('[S-03] flags the southern polar seasons the opposite way round', () => {
    const midwinter = sunTimes(new Date(Date.UTC(2024, 5, 21, 12)), -75, 0); // Antarctica, June
    expect(midwinter.polar).toBe('night');
    const midsummer = sunTimes(new Date(Date.UTC(2024, 11, 21, 12)), -75, 0);
    expect(midsummer.polar).toBe('day');
  });
});

// Regression: issue #105 "doesn't seem to work for southern hemisphere". The
// astronomy was fine in both hemispheres (<3 min off NOAA); the day index was
// the UTC day, so at far-east longitudes -- Australia and NZ, i.e. where the
// southern-hemisphere users are -- the whole local morning reported the
// PREVIOUS day and the countdown ran negative.
describe('[S-06] sunTimes keys off the solar day, not the UTC day', () => {
  const places = [
    { name: 'Sydney', lat: -33.87, lon: 151.21, tz: 10 },
    { name: 'Auckland', lat: -36.85, lon: 174.76, tz: 12 },
    { name: 'Tokyo', lat: 35.68, lon: 139.65, tz: 9 },
    { name: 'Honolulu', lat: 21.31, lon: -157.86, tz: -10 },
    { name: 'Buenos Aires', lat: -34.6, lon: -58.38, tz: -3 },
  ];

  for (const p of places) {
    it(`[S-06] ${p.name}: sunrise and sunset land on the local day, 01:30-23:30`, () => {
      for (let hour = 1; hour < 24; hour++) {
        const now = localInstant(hour, p.tz);
        const s = sunTimes(now, p.lat, p.lon);
        expect(localDate(s.sunrise!, p.tz), `${p.name} sunrise at ${hour}:30`).toBe('2026-08-10');
        expect(localDate(s.sunset!, p.tz), `${p.name} sunset at ${hour}:30`).toBe('2026-08-10');
      }
    });
  }
});

describe('[S-07] nextSunEvent never returns an event in the past', () => {
  const places = [
    { name: 'Sydney', lat: -33.87, lon: 151.21, tz: 10 },
    { name: 'Auckland', lat: -36.85, lon: 174.76, tz: 12 },
    { name: 'Tokyo', lat: 35.68, lon: 139.65, tz: 9 },
    { name: 'Honolulu', lat: 21.31, lon: -157.86, tz: -10 },
    { name: 'Zurich', lat: 47.37, lon: 8.54, tz: 2 },
  ];

  for (const p of places) {
    it(`[S-07] ${p.name}: the next event is strictly ahead at every hour`, () => {
      for (let hour = 0; hour < 24; hour++) {
        const now = localInstant(hour, p.tz);
        const next = nextSunEvent(now, p.lat, p.lon)!;
        expect(next, `${p.name} at ${hour}:30`).not.toBeNull();
        expect(next.at.getTime(), `${p.name} at ${hour}:30`).toBeGreaterThan(now.getTime());
        // ...and within a day, so it is the *next* one and not a later day's.
        expect(next.at.getTime() - now.getTime()).toBeLessThan(86_400_000);
      }
    });
  }

  it('[S-07] Sydney at 09:30 local counts down to sunset, not a stale sunrise', () => {
    const now = localInstant(9, 10);
    const next = nextSunEvent(now, -33.87, 151.21)!;
    expect(next.kind).toBe('sunset');
    const minutes = (next.at.getTime() - now.getTime()) / 60_000;
    expect(minutes).toBeGreaterThan(7 * 60); // ~7h50m to a ~17:20 sunset
    expect(minutes).toBeLessThan(9 * 60);
  });

  it('[S-07] returns null when the sun neither rises nor sets in the window', () => {
    expect(nextSunEvent(new Date(Date.UTC(2024, 11, 21, 12)), -85, 0)).toBeNull();
  });
});

describe('[S-08] sublunarPoint', () => {
  it('stays within the lunar declination limit (obliquity + 5.1° inclination)', () => {
    const t0 = Date.UTC(2026, 0, 1);
    for (let h = 0; h < 24 * 400; h += 6) {
      const p = sublunarPoint(new Date(t0 + h * 3_600_000));
      expect(Math.abs(p.lat)).toBeLessThanOrEqual(28.7);
      expect(Math.abs(p.lon)).toBeLessThanOrEqual(180);
    }
  });

  it('returns to roughly the same meridian one lunar day later', () => {
    const t0 = new Date(Date.UTC(2026, 7, 10));
    const t1 = new Date(t0.getTime() + (24 * 60 + 50) * 60_000);
    const drift = Math.abs(sublunarPoint(t1).lon - sublunarPoint(t0).lon);
    expect(drift).toBeLessThan(8); // the lunar day itself swings by ~±15 min
  });

});

// The whole point of [S-11]: the drawn path is the sun's real one, not a
// semicircle. These pin the properties that make it real.
describe('[S-11] the sun path is the real one', () => {
  it('peaks at the true noon altitude, 90 - |lat - dec|', () => {
    for (const [lat, dec] of [[47.37, 15.6], [47.37, -23.4], [-33.87, -20], [0, 0], [64.15, 23.4]]) {
      expect(altitudeAtHourAngle(0, dec, lat)).toBeCloseTo(90 - Math.abs(lat - dec), 6);
    }
  });

  it('crosses the horizon exactly at the ends of the arc', () => {
    for (const [lat, dec] of [[47.37, 15.6], [-33.87, -20], [10, 5], [-54.8, 12]]) {
      const w = semiDiurnalArc(dec, lat);
      expect(altitudeAtHourAngle(w, dec, lat)).toBeCloseTo(0, 6);
      expect(altitudeAtHourAngle(-w, dec, lat)).toBeCloseTo(0, 6);
    }
  });

  it('gives a 12-hour day at the equinox, everywhere', () => {
    for (const lat of [-54.8, -33.87, 0, 47.37, 64.15]) {
      expect(semiDiurnalArc(0, lat)).toBeCloseTo(90, 6); // 90° of hour angle = 6 h
    }
  });

  it('reports circumpolar sun and polar night as the degenerate arcs', () => {
    expect(semiDiurnalArc(23.4, 78)).toBe(180); // Svalbard midsummer: never sets
    expect(semiDiurnalArc(-23.4, 78)).toBe(0);  // ...and never rises in midwinter
  });

  it('sits lower in winter than in summer at the same place', () => {
    const summer = altitudeAtHourAngle(0, 23.4, 47.37);
    const winter = altitudeAtHourAngle(0, -23.4, 47.37);
    expect(summer).toBeCloseTo(66.03, 1);
    expect(winter).toBeCloseTo(19.23, 1);
    expect(summer - winter).toBeCloseTo(46.8, 1); // twice the declination swing
  });

  // A semicircle would have altitude proportional to sin(pi * progress). The
  // real curve does not: it is fuller in the middle of the day, because the sun
  // climbs fast near the horizon and loiters near its peak.
  it('is not a semicircle', () => {
    const dec = 15.6, lat = 47.37;
    const peak = altitudeAtHourAngle(0, dec, lat);
    let maxGap = 0;
    for (const s of skyPathSamples(dec, lat, false, 40)) {
      const circle = Math.sin(Math.PI * s.progress);
      maxGap = Math.max(maxGap, Math.abs(s.altitude / peak - circle));
    }
    expect(maxGap).toBeGreaterThan(0.05);
  });

  it('draws a curve that starts and ends on the horizon and peaks at midday', () => {
    const dec = 15.6, lat = 47.37;
    const day = skyPathSamples(dec, lat, false, 48);
    expect(day[0].altitude).toBeCloseTo(0, 6);
    expect(day[day.length - 1].altitude).toBeCloseTo(0, 6);
    const peak = day.reduce((a, b) => (b.altitude > a.altitude ? b : a));
    expect(peak.progress).toBeCloseTo(0.5, 2);
    expect(peak.altitude).toBeCloseTo(altitudeAtHourAngle(0, dec, lat), 6);
    // ...and rises then falls, with no wobble in between.
    for (let i = 1; i < day.length; i++) {
      if (day[i].progress <= 0.5) expect(day[i].altitude).toBeGreaterThanOrEqual(day[i - 1].altitude - 1e-9);
      else expect(day[i].altitude).toBeLessThanOrEqual(day[i - 1].altitude + 1e-9);
    }
  });

  it('runs the night curve the other way, deepest at the nadir', () => {
    const dec = 15.6, lat = 47.37;
    const night = skyPathSamples(dec, lat, true, 48);
    expect(night[0].altitude).toBeCloseTo(0, 6);              // just set
    expect(night[night.length - 1].altitude).toBeCloseTo(0, 6); // about to rise
    const nadir = night.reduce((a, b) => (b.altitude < a.altitude ? b : a));
    expect(nadir.progress).toBeCloseTo(0.5, 2);
    expect(nadir.altitude).toBeCloseTo(altitudeAtHourAngle(180, dec, lat), 6);
  });

  it('maps progress back to the hour angles the arc is built from', () => {
    const w = semiDiurnalArc(15.6, 47.37);
    expect(hourAngleAt(0, w, false)).toBeCloseTo(-w, 9);
    expect(hourAngleAt(0.5, w, false)).toBeCloseTo(0, 9);
    expect(hourAngleAt(1, w, false)).toBeCloseTo(w, 9);
    expect(hourAngleAt(0, w, true)).toBeCloseTo(w, 9);
    expect(hourAngleAt(0.5, w, true)).toBeCloseTo(180, 9);
    expect(hourAngleAt(1, w, true)).toBeCloseTo(-w, 9);
  });
});

describe('[S-10] skyPosition', () => {
  it('puts a body straight overhead at its peak, mid-arc', () => {
    const p = skyPosition({ lat: 47.37, lon: 8.54 }, 47.37, 8.54);
    expect(p.up).toBe(true);
    expect(p.progress).toBeCloseTo(0.5, 6);
    expect(p.altitude).toBeCloseTo(90, 6);
  });

  it('puts the antipodal body at the nadir', () => {
    const p = skyPosition({ lat: -47.37, lon: 8.54 - 180 }, 47.37, 8.54);
    expect(p.up).toBe(false);
    expect(p.progress).toBeCloseTo(0.5, 6);
    expect(p.altitude).toBeCloseTo(-90, 6);
  });

  it('reads east as the rising half and west as the setting half', () => {
    expect(skyPosition({ lat: 0, lon: 45 }, 0, 0).progress).toBeLessThan(0.5);
    expect(skyPosition({ lat: 0, lon: -45 }, 0, 0).progress).toBeGreaterThan(0.5);
  });

  it('keeps progress in range everywhere on the sphere', () => {
    for (let dec = -28; dec <= 28; dec += 4) {
      for (let lon = -180; lon < 180; lon += 5) {
        for (const lat of [-85, -70, -33, 0, 47, 70, 85]) {
          const p = skyPosition({ lat: dec, lon }, lat, 8.54);
          expect(p.progress).toBeGreaterThanOrEqual(0);
          expect(p.progress).toBeLessThanOrEqual(1);
          expect(Number.isFinite(p.altitude)).toBe(true);
        }
      }
    }
  });

  // The path and the clock have to tell the same story: if the widget draws the
  // sun above the horizon, sunTimes must agree the sun is up.
  it('[S-06] agrees with sunTimes at every hour, in both hemispheres', () => {
    for (const [lat, lon] of [[47.37, 8.54], [-33.87, 151.21], [-36.85, 174.76], [21.31, -157.86]]) {
      for (let hour = 0; hour < 24; hour++) {
        const now = new Date(Date.UTC(2026, 7, 10, hour, 30));
        const s = sunTimes(now, lat, lon);
        if (s.polar) continue;
        const inDay = now >= s.sunrise! && now <= s.sunset!;
        expect(skyPosition(subsolarPoint(now), lat, lon).up, `lat=${lat} hour=${hour}`).toBe(inDay);
      }
    }
  });

  it('sweeps the sun from one end of the arc to the other across a day', () => {
    const seen: number[] = [];
    for (let m = 0; m < 24 * 60; m += 10) {
      const p = skyPosition(subsolarPoint(new Date(Date.UTC(2026, 7, 10) + m * 60_000)), 47.37, 8.54);
      if (p.up) seen.push(p.progress);
    }
    expect(Math.min(...seen)).toBeLessThan(0.05);
    expect(Math.max(...seen)).toBeGreaterThan(0.95);
  });
});
