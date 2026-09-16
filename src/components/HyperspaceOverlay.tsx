import { onCleanup, onMount } from 'solid-js';
import { bootOverlayStyle, endBootOverlay, overlayTotalMs, introAnimMode } from '../bootOverlay.ts';
import {
  hyperspaceWarp,
  hyperspaceShakeStyle,
  HYPERSPACE_PROFILES,
  type HyperspaceStyle,
} from '../hyperspace.ts';
import { introMsFor } from '../design.ts';
import './HyperspaceOverlay.css';

// Full-screen hyperspace boot overlay ([S-16], reads the HYPERSPACE_PROFILES
// entry). The root is tagged data-themia-interactive so the window's
// SetWindowRgn hit-mask covers the whole screen while it plays. That's what
// makes the animation visible edge-to-edge instead of being clipped to the
// widget rects. pointer-events is none so widget clicks land normally
// underneath during the show.
//
// Lifecycle is event-driven: the root runs a CSS fade-out whose delay is
// (total − fade); its animationend (or animationcancel, if the element is
// torn down early) calls endHyperspace(), which unmounts the overlay and
// lets App's rect push shrink the window back to the widget areas. No
// timers: a missed end event is the only way to stay full-screen, and the
// cancel handler covers that path. The veil is a CHILD element, so its own
// fade animations can never be mistaken for the root's hs-fade-out.

interface Star {
  angle: number;
  dist: number;
  speed: number;
  hue: number;
  phase: number; // twinkle offset so static stars don't pulse in lockstep
  color: string; // base hsl fixed at birth/recycle; only its alpha varies per frame
}

export default function HyperspaceOverlay() {
  let rootEl: HTMLDivElement | undefined;
  let canvasEl: HTMLCanvasElement | undefined;
  let raf: number | null = null;

  // Captured once at mount: the overlay only renders while a style is active,
  // and a mid-show style change can't happen (begin is one-shot, replays are
  // ignored while active).
  const style: HyperspaceStyle = bootOverlayStyle() ?? 'hyperspace';
  const profile = HYPERSPACE_PROFILES[style];

  onMount(() => {
    const canvas = canvasEl;
    const ctx = canvas?.getContext('2d');
    if (!canvas || !ctx) return; // no 2d context → silent veil only, fade still ends it

    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    const w = Math.max(1, Math.round(rect.width * dpr));
    const h = Math.max(1, Math.round(rect.height * dpr));
    canvas.width = w;
    canvas.height = h;

    // Warp core: horizontally centred, vertical from the profile (sits toward
    // the bottom). maxR reaches the FARTHEST corner so streaks always cover the
    // whole screen even with an off-centre core.
    const cx = w / 2;
    const cy = h * profile.centerYFrac;
    const maxR = Math.hypot(Math.max(cx, w - cx), Math.max(cy, h - cy));

    // Star colour (hue + a depth-based lightness) is fixed at birth and on each
    // recycle; only its alpha tracks warp/twinkle per frame (set via
    // ctx.globalAlpha, a cheap number). Precomputing the hsl string kills one
    // string alloc + one CSS-colour parse PER STAR PER FRAME, the main GC/CPU
    // churn behind the warp stutter.
    const starColor = (hue: number, dist: number) =>
      `hsl(${hue}, 80%, ${(78 + (dist / maxR) * 16).toFixed(1)}%)`;

    const stars: Star[] = [];
    for (let i = 0; i < profile.starCount; i++) {
      const dist = Math.pow(Math.random(), 0.6) * maxR * 0.9;
      const hue = 200 + Math.random() * 40;
      stars.push({
        angle: Math.random() * Math.PI * 2,
        dist,
        speed: 0.4 + Math.random() * 0.9,
        hue,
        phase: Math.random() * Math.PI * 2,
        color: starColor(hue, dist),
      });
    }

    // Core glow baked ONCE to an offscreen sprite (radial blue → transparent).
    // Each frame draws it scaled by the warp (swell) at a warp-scaled
    // globalAlpha (intensity), no per-frame createRadialGradient/addColorStop
    // allocation, which was a second per-frame string-parse cost. Profile-gated
    // (the warp paints no glow: pure black space, no background gradient).
    const GLOW_R = 128;
    const glowSprite = profile.glow ? document.createElement('canvas') : null;
    let glowOk = false;
    if (glowSprite) {
      glowSprite.width = glowSprite.height = GLOW_R * 2;
      const gctx = glowSprite.getContext('2d');
      if (gctx) {
        const g = gctx.createRadialGradient(GLOW_R, GLOW_R, 0, GLOW_R, GLOW_R, GLOW_R);
        g.addColorStop(0, 'rgba(190, 215, 255, 1)');
        g.addColorStop(1, 'rgba(190, 215, 255, 0)');
        gctx.fillStyle = g;
        gctx.fillRect(0, 0, GLOW_R * 2, GLOW_R * 2);
        glowOk = true;
      }
    }

    let start: number | null = null;
    let last: number | null = null;
    const frame = (now: number) => {
      if (start == null) start = now;
      if (last == null) last = now;
      const elapsed = now - start;
      const dt = Math.min(48, now - last) / 16.667; // frames of motion, capped
      last = now;

      // Warp profile ([S-16]): static field → accelerate into streaks →
      // cruise → decelerate back to static dots while the widgets land. The
      // root's CSS fade-out ends the show.
      const warp = hyperspaceWarp(elapsed, style);

      // Star-field shake: the projection centre jitters per frame, scaled by
      // the warp. The amplitude is the profile's jitterPx, 0 for this warp, so
      // the stars NEVER shake (only the widget grid does, [S-16]).
      const jx = (Math.random() * 2 - 1) * warp * profile.jitterPx * dpr;
      const jy = (Math.random() * 2 - 1) * warp * profile.jitterPx * dpr;

      ctx.clearRect(0, 0, w, h);
      ctx.lineCap = 'round';

      for (const s of stars) {
        const depth = s.dist / maxR;
        const v = s.speed * warp * profile.speedFactor * (0.15 + depth) * dpr * dt;
        // Streaks grow/shrink with the warp; the floor keeps a static star
        // rendering as a dot (Canvas skips true zero-length strokes).
        const trail = Math.max(0.7 * dpr, s.speed * warp * profile.trailFactor * (0.1 + depth) * dpr);
        const cos = Math.cos(s.angle);
        const sin = Math.sin(s.angle);
        const x1 = cx + jx + cos * s.dist;
        const y1 = cy + jy + sin * s.dist;
        const x0 = cx + jx + cos * Math.max(0, s.dist - trail);
        const y0 = cy + jy + sin * Math.max(0, s.dist - trail);

        // Twinkle reads strongest while static; warp speed washes it out.
        const twinkle = 0.75 + 0.25 * Math.sin(elapsed * 0.004 * s.speed + s.phase);
        const alpha = Math.min(1, (0.30 + depth * 0.55) * (0.5 + warp * 0.5) * twinkle);
        ctx.globalAlpha = alpha; // per-frame intensity, a number, no string parse
        ctx.strokeStyle = s.color; // fixed at birth, no per-frame string build/parse
        ctx.lineWidth = (0.5 + depth * 1.1) * (1 + warp * 0.6) * dpr;
        ctx.beginPath();
        ctx.moveTo(x0, y0);
        ctx.lineTo(x1, y1);
        ctx.stroke();

        s.dist += v;
        if (s.dist > maxR * 1.05) {
          s.angle = Math.random() * Math.PI * 2;
          s.dist = Math.random() * maxR * 0.12;
          s.speed = 0.4 + Math.random() * 0.9;
          s.color = starColor(s.hue, s.dist); // recolour for the new birth depth
        }
      }

      // Soft core glow that swells with the jump (only when the profile sets
      // glow; this warp keeps the background pure black, so it's skipped).
      // Baked sprite, scaled + alpha'd.
      ctx.globalAlpha = 1; // clear the per-star alpha before the glow / next frame
      if (glowOk && glowSprite) {
        const glowR = maxR * (0.08 + warp * 0.22);
        ctx.globalAlpha = 0.10 + warp * 0.16;
        ctx.drawImage(glowSprite, cx - glowR, cy - glowR, glowR * 2, glowR * 2);
        ctx.globalAlpha = 1;
      }

      raf = requestAnimationFrame(frame);
    };
    raf = requestAnimationFrame(frame);
  });

  onCleanup(() => {
    if (raf != null) cancelAnimationFrame(raf);
  });

  // The overlay can play in startup OR unlock mode ([S-16]); its fade tail
  // follows whichever duration the active mode picked.
  const totalMs = () => overlayTotalMs(style, introMsFor(introAnimMode()));

  const onEnd = (e: AnimationEvent) => {
    if (e.target === rootEl && e.animationName === 'hs-fade-out') endBootOverlay();
  };

  return (
    <div
      ref={rootEl}
      class="hyperspace-overlay"
      data-themia-interactive="true"
      style={`--hs-fade: ${profile.fadeMs}ms; --hs-fade-delay: ${totalMs() - profile.fadeMs}ms;`}
      onAnimationEnd={onEnd}
      onAnimationCancel={onEnd}
      aria-hidden="true"
    >
      {/* Darkened backdrop ([S-16]): snaps in with the show, holds black space
          through the warp, then lifts across the decel window (delay = jump,
          duration = decel) so the widgets crash into a brightening desktop.
          Only when the profile sets veil. */}
      {profile.veil && (
        <div
          class="hyperspace-veil"
          style={`--hs-veil-out-delay: ${profile.jumpMs}ms; --hs-veil-out: ${profile.decelMs}ms;`}
        />
      )}
      {/* Canvas shake only when the profile says so; this warp keeps the stars
          rock-steady, its shake runs on the widget grid alone ([S-16]). */}
      <canvas
        ref={canvasEl}
        class="hyperspace-canvas"
        style={profile.canvasShake ? hyperspaceShakeStyle(style) : undefined}
      />
    </div>
  );
}
