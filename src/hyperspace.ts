// Hyperspace startup overlay: visual profile (src/SPEC.md [S-16]).
//
// The shared mount/teardown lifecycle (region expansion, replay, one-shot boot
// guard) lives in bootOverlay.ts, alongside the hacker style. This module owns
// only the hyperspace warp TIMING and geometry helpers, so they stay unit
// pinnable without a DOM. The overlay, grid and keyframe call sites read
// everything from HYPERSPACE_PROFILES, so there is one code path, never a
// parallel implementation.

/** The hyperspace overlay style ([S-16]). */
export type HyperspaceStyle = 'hyperspace';

export interface HyperspaceProfile {
  /** Phase lengths ([S-16]): static dots → accel into streaks → cruise → decel. */
  staticMs: number;
  accelMs: number;
  cruiseMs: number;
  decelMs: number;
  /** static + accel + cruise: the widgets warp in here. */
  jumpMs: number;
  /** accel + cruise + decel: the continuous screen shake, after static. */
  shakeMs: number;
  /** Overlay fade-out tail after the last widget has landed. */
  fadeMs: number;
  /** Warp ramp exponent (lower = warp builds sooner = more violent spool-up). */
  accelPow: number;
  /**
   * Warp ceiling for the TEASE segment of accel: the ramp climbs to this cap,
   * so streaks only creep, then the snap leaps to full warp.
   */
  teasePeak: number;
  /**
   * Length of the SNAP: the fast (but smoothstep-eased, no kink) sweep from
   * teasePeak to full warp at the very end of accel.
   */
  snapMs: number;
  /** Star velocity multiplier at full warp (px/frame basis). */
  speedFactor: number;
  /** Streak length multiplier at full warp. */
  trailFactor: number;
  /** Canvas projection-centre jitter at full warp, CSS px (0 = steady stars). */
  jitterPx: number;
  /**
   * Whether the screen-shake keyframes ALSO run on the overlay's star canvas.
   * False keeps the stars rock-steady: the shake runs on the widget grid only.
   */
  canvasShake: boolean;
  starCount: number;
  /**
   * Vertical position of the warp core (projection centre AND the widgets'
   * warp-in origin) as a fraction of screen height (0.5 = centred). The warp
   * core sits toward the bottom.
   */
  centerYFrac: number;
  /** Dark backdrop veil behind the starfield. */
  veil: boolean;
  /**
   * Soft radial core glow that swells with the jump. Off paints pure black
   * space (no background gradient).
   */
  glow: boolean;
  /** Grid/canvas shake keyframes name (WidgetGrid.css). */
  shakeName: 'hs-shake';
}

// Hyperspace phases ([S-16]): slow-burn-then-snap choreography: a LONG
// stationary hold, then accel splits into a tease (warp creeps to
// HYPERSPACE_TEASE_PEAK, where streaks barely lengthen) and a fast
// HYPERSPACE_SNAP_MS smoothstep sweep to full warp, so the whole field tears off
// together, but eased at both ends so the slow→fast hand-off has no kink.
// Cruise at full warp (the widgets warp in at the jump point), then decel
// covers the staggered crash-landing window (jump + 540ms stagger cap + the
// impact point of the last widget).
export const HYPERSPACE_STATIC_MS = 900;
export const HYPERSPACE_ACCEL_MS = 1500;
export const HYPERSPACE_SNAP_MS = 280;
export const HYPERSPACE_TEASE_PEAK = 0.06;
export const HYPERSPACE_CRUISE_MS = 900;
export const HYPERSPACE_DECEL_MS = 900;
export const HYPERSPACE_JUMP_MS =
  HYPERSPACE_STATIC_MS + HYPERSPACE_ACCEL_MS + HYPERSPACE_CRUISE_MS;
export const HYPERSPACE_SHAKE_MS =
  HYPERSPACE_ACCEL_MS + HYPERSPACE_CRUISE_MS + HYPERSPACE_DECEL_MS;
// Overlay fade-out tail after the last widget has landed.
export const HYPERSPACE_FADE_MS = 300;

export const HYPERSPACE_PROFILES: Record<HyperspaceStyle, HyperspaceProfile> = {
  // [S-16] the warp: pitch-black backdrop, warp core toward the bottom, a
  // long stationary hold, a tease-then-snap spool-up into very fast / very long
  // streaks, big-amplitude grid shake. The stars themselves NEVER shake (zero
  // jitter, no canvas shake); the spectacle is the snap, not wiggle.
  hyperspace: {
    staticMs: HYPERSPACE_STATIC_MS,
    accelMs: HYPERSPACE_ACCEL_MS,
    cruiseMs: HYPERSPACE_CRUISE_MS,
    decelMs: HYPERSPACE_DECEL_MS,
    jumpMs: HYPERSPACE_JUMP_MS,
    shakeMs: HYPERSPACE_SHAKE_MS,
    fadeMs: HYPERSPACE_FADE_MS,
    accelPow: 1.6,
    teasePeak: HYPERSPACE_TEASE_PEAK,
    snapMs: HYPERSPACE_SNAP_MS,
    speedFactor: 260,
    trailFactor: 1500,
    jitterPx: 0,
    canvasShake: false,
    starCount: 100,
    centerYFrac: 0.68,
    veil: true,
    glow: false,
    shakeName: 'hs-shake',
  },
};

/**
 * Star speed/streak factor ∈ [0, 1] at `elapsedMs` into the show:
 * 0 = static dots, 1 = full warp streaks. Static → ease-in acceleration →
 * cruise → ease-out deceleration back to 0 (and 0 ever after). The phase
 * lengths and the ramp exponent come from the profile. Accel is two segments:
 * a tease ramp capped at teasePeak (streaks only creep), then a fast smoothstep
 * sweep to full warp over the final snapMs, so the whole field tears off
 * together, eased at both ends so the slow→fast hand-off has no kink ([S-16]).
 */
export function hyperspaceWarp(elapsedMs: number, style: HyperspaceStyle = 'hyperspace'): number {
  const p = HYPERSPACE_PROFILES[style];
  if (elapsedMs <= p.staticMs) return 0;
  const accelEnd = p.staticMs + p.accelMs;
  if (elapsedMs < accelEnd) {
    const t = elapsedMs - p.staticMs;
    const teaseMs = p.accelMs - p.snapMs;
    if (t < teaseMs) return Math.pow(t / teaseMs, p.accelPow) * p.teasePeak;
    const f = (t - teaseMs) / p.snapMs;
    const s = f * f * (3 - 2 * f); // smoothstep: gentle out of the tease, eases into full
    return p.teasePeak + (1 - p.teasePeak) * s;
  }
  if (elapsedMs <= p.jumpMs) return 1;
  const d = (elapsedMs - p.jumpMs) / p.decelMs;
  if (d >= 1) return 0;
  return Math.pow(1 - d, 3); // sharp drop out of warp, gentle settle to static
}

/**
 * Per-widget CSS vars for the warp-in origin: the offset from the widget's
 * centre to the WARP CORE (horizontal centre, vertical from the profile's
 * centerYFrac, toward the bottom), in the grid container's px space. The
 * `wi-hyperspace` keyframes translate from this point at near-zero scale, so
 * widgets fly in from the background core, not from the front ([S-16]).
 */
export function hyperspaceFromVars(
  rect: { x: number; y: number; w: number; h: number },
  W: number,
  H: number,
  style: HyperspaceStyle = 'hyperspace',
): string {
  const p = HYPERSPACE_PROFILES[style];
  const cx = (rect.x + rect.w / 2) * W;
  const cy = (rect.y + rect.h / 2) * H;
  return `--hs-dx: ${Math.round(W / 2 - cx)}px; --hs-dy: ${Math.round(H * p.centerYFrac - cy)}px;`;
}

/**
 * Screen-shake animation shorthand ([S-16]): ONE continuous shake that runs
 * the whole show: near-still through the tease, a hard bang at the snap,
 * judders through cruise, spikes at the jump point and decays to nothing
 * through the staggered widget-crash window (the `hs-shake` keyframes hammer
 * the grid with an impact spike per landing widget). Applied inline to the
 * grid; the overlay's star canvas stays rock-steady (`canvasShake: false`) so
 * only the widgets shake. Ends on an identity transform.
 */
export function hyperspaceShakeStyle(style: HyperspaceStyle = 'hyperspace'): string {
  const p = HYPERSPACE_PROFILES[style];
  return `animation: ${p.shakeName} ${p.shakeMs}ms linear ${p.staticMs}ms both;`;
}
