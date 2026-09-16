// [S-16] Hyperspace startup overlay: warp profile, fly-in origin, screen
// shake. The shared mount/teardown LIFECYCLE (begin/end/replay/region) is
// style-agnostic and pinned in bootOverlay.test.ts; this file covers only the
// hyperspace style's visual math, which needs no DOM.

import { describe, it, expect } from 'vitest';
import {
  hyperspaceWarp,
  hyperspaceFromVars,
  hyperspaceShakeStyle,
  HYPERSPACE_PROFILES,
  HYPERSPACE_STATIC_MS,
  HYPERSPACE_ACCEL_MS,
  HYPERSPACE_CRUISE_MS,
  HYPERSPACE_JUMP_MS,
  HYPERSPACE_DECEL_MS,
  HYPERSPACE_SHAKE_MS,
} from './hyperspace.ts';

describe('S-16 warp profile: static, accelerate, cruise, decelerate', () => {
  const accelEnd = HYPERSPACE_STATIC_MS + HYPERSPACE_ACCEL_MS;

  it('widgets jump in after static + accel + cruise', () => {
    expect(HYPERSPACE_JUMP_MS).toBe(
      HYPERSPACE_STATIC_MS + HYPERSPACE_ACCEL_MS + HYPERSPACE_CRUISE_MS,
    );
  });

  it('stars hold static (warp 0) before the jump spools up', () => {
    expect(hyperspaceWarp(0)).toBe(0);
    expect(hyperspaceWarp(HYPERSPACE_STATIC_MS - 1)).toBe(0);
  });

  it('accelerates monotonically to full warp', () => {
    const a = hyperspaceWarp(HYPERSPACE_STATIC_MS + HYPERSPACE_ACCEL_MS * 0.3);
    const b = hyperspaceWarp(HYPERSPACE_STATIC_MS + HYPERSPACE_ACCEL_MS * 0.7);
    expect(a).toBeGreaterThan(0);
    expect(b).toBeGreaterThan(a);
    expect(b).toBeLessThan(1);
    expect(hyperspaceWarp(accelEnd)).toBe(1);
  });

  it('cruises at full warp until the widgets jump in', () => {
    expect(hyperspaceWarp(accelEnd + HYPERSPACE_CRUISE_MS / 2)).toBe(1);
    expect(hyperspaceWarp(HYPERSPACE_JUMP_MS)).toBe(1);
  });

  it('decelerates back to static after the jump and stays there', () => {
    const mid = hyperspaceWarp(HYPERSPACE_JUMP_MS + HYPERSPACE_DECEL_MS / 2);
    expect(mid).toBeGreaterThan(0);
    expect(mid).toBeLessThan(1);
    expect(hyperspaceWarp(HYPERSPACE_JUMP_MS + HYPERSPACE_DECEL_MS)).toBe(0);
    expect(hyperspaceWarp(HYPERSPACE_JUMP_MS + HYPERSPACE_DECEL_MS + 99999)).toBe(0);
  });
});

describe('S-16 fly-in origin: widgets come from the lowered warp core', () => {
  // The warp core sits toward the bottom (centerYFrac 0.68), so a horizontally
  // centred widget still needs a vertical offset DOWN to the core.
  it('a horizontally-centred widget offsets down to the lowered core', () => {
    // Widget centre (500, 400) on 1000×800 → dx 0; dy = 800·0.68 − 400 = 144.
    expect(hyperspaceFromVars({ x: 0.4, y: 0.4, w: 0.2, h: 0.2 }, 1000, 800))
      .toBe('--hs-dx: 0px; --hs-dy: 144px;');
  });

  it('an off-centre widget translates from the lowered core', () => {
    // Widget centre (250, 200) on 1000×800 → dx +250; dy = 544 − 200 = 344.
    expect(hyperspaceFromVars({ x: 0.2, y: 0.2, w: 0.1, h: 0.1 }, 1000, 800))
      .toBe('--hs-dx: 250px; --hs-dy: 344px;');
  });
});

describe('S-16 screen shake: one continuous subtle shake through the show', () => {
  it('a single shake spans accel + cruise + decel, delayed past the static phase', () => {
    expect(HYPERSPACE_SHAKE_MS).toBe(
      HYPERSPACE_ACCEL_MS + HYPERSPACE_CRUISE_MS + HYPERSPACE_DECEL_MS,
    );
    const s = hyperspaceShakeStyle();
    expect(s).toContain(`hs-shake ${HYPERSPACE_SHAKE_MS}ms linear ${HYPERSPACE_STATIC_MS}ms both`);
    // One animation, one path. The old two-phase rumble/impact pair is gone.
    expect(s).not.toContain('hs-rumble');
    expect(s).not.toContain('hs-impact');
  });
});

// [S-16] The (sole) hyperspace profile is the slow-burn-then-snap warp:
// pitch-black backdrop, projection centre toward the bottom, a LONG stationary
// hold, a tease where streaks only creep, then a sudden all-at-once snap to
// very fast, very long streaks, with the stars themselves rock-steady.
describe('S-16 hyperspace profile: dark, lowered core, tease then snap', () => {
  const p = HYPERSPACE_PROFILES['hyperspace'];

  it('mirrors the pinned S-16 constants (one source of truth)', () => {
    expect(p.staticMs).toBe(HYPERSPACE_STATIC_MS);
    expect(p.jumpMs).toBe(HYPERSPACE_JUMP_MS);
    expect(p.shakeMs).toBe(HYPERSPACE_SHAKE_MS);
    expect(p.shakeName).toBe('hs-shake');
  });

  it('derived timings stay internally consistent', () => {
    expect(p.jumpMs).toBe(p.staticMs + p.accelMs + p.cruiseMs);
    expect(p.shakeMs).toBe(p.accelMs + p.cruiseMs + p.decelMs);
    expect(p.snapMs).toBeLessThan(p.accelMs);
  });

  it('darkens the backdrop (veil) and paints NO core glow, pure black space', () => {
    expect(p.veil).toBe(true);
    expect(p.glow).toBe(false);
  });

  it('projection centre sits toward the BOTTOM (still well on screen)', () => {
    expect(p.centerYFrac).toBeGreaterThan(0.6);
    expect(p.centerYFrac).toBeLessThan(0.85);
  });

  it('widgets warp in from the lowered core (origin tracks centerYFrac)', () => {
    // Widget centre (500, 400) on 1000×800 → dx 0; dy = 800·centerYFrac − 400.
    const dy = Math.round(800 * p.centerYFrac - 400);
    expect(hyperspaceFromVars({ x: 0.4, y: 0.4, w: 0.2, h: 0.2 }, 1000, 800))
      .toBe(`--hs-dx: 0px; --hs-dy: ${dy}px;`);
  });

  it('stars fly fast with long streaks, and NEVER wiggle (steady stars)', () => {
    expect(p.speedFactor).toBeGreaterThan(200);
    expect(p.trailFactor).toBeGreaterThan(1000);
    expect(p.jitterPx).toBe(0); // the stars do not shake at all
  });

  it('never shakes the star CANVAS, only the widget grid shakes', () => {
    expect(p.canvasShake).toBe(false);
  });

  it('tease: warp only CREEPS up to a slight streak before the snap', () => {
    expect(p.teasePeak).toBeLessThanOrEqual(0.1); // "just slightly" longer strokes
    const teaseMs = p.accelMs - p.snapMs;
    const early = hyperspaceWarp(p.staticMs + teaseMs * 0.4);
    const late = hyperspaceWarp(p.staticMs + teaseMs * 0.95);
    expect(early).toBeGreaterThan(0);
    expect(late).toBeGreaterThan(early); // still creeping up…
    expect(late).toBeLessThanOrEqual(p.teasePeak); // …but never past the tease cap
  });

  it('snap: warp sweeps from the tease cap to FULL fast but SMOOTHLY (eased, no kink)', () => {
    expect(p.snapMs).toBeGreaterThan(0);
    expect(p.snapMs).toBeLessThanOrEqual(300); // still sudden…
    const snapStart = p.staticMs + p.accelMs - p.snapMs;
    expect(hyperspaceWarp(snapStart)).toBeCloseTo(p.teasePeak, 5);
    // …but eased: blends gently out of the tease (below the linear chord)…
    const early = hyperspaceWarp(snapStart + p.snapMs * 0.1);
    expect(early - p.teasePeak).toBeLessThan(0.1 * (1 - p.teasePeak));
    // …passes half warp mid-snap…
    const mid = hyperspaceWarp(snapStart + p.snapMs / 2);
    expect(mid).toBeGreaterThan(0.4);
    // …and eases INTO full warp (above the linear chord near the end).
    const late = hyperspaceWarp(snapStart + p.snapMs * 0.9);
    expect(late - p.teasePeak).toBeGreaterThan(0.9 * (1 - p.teasePeak));
    expect(hyperspaceWarp(p.staticMs + p.accelMs)).toBe(1);
  });

  it('uses the hs-shake keyframes with its timings (grid only)', () => {
    const s = hyperspaceShakeStyle();
    expect(s).toContain(`hs-shake ${p.shakeMs}ms linear ${p.staticMs}ms both`);
  });
});
