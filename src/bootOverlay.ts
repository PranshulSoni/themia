// Full-screen startup boot overlay: shared lifecycle for every startup style
// that paints edge-to-edge (src/SPEC.md [S-16] hyperspace, [S-23] hacker).
//
// While a boot overlay is mounted, its root is data-themia-interactive, so the
// window's SetWindowRgn hit-mask covers the whole screen and the animation is
// visible edge-to-edge. The moment it ends the overlay unmounts and App's rect
// push shrinks the window back to the widget areas, so the lifecycle is
// deliberately strict and lives in ONE place rather than once per style:
//
//   - starts only when globalDesign.widgetStartupAnimation is an OVERLAY style
//   - never under prefers-reduced-motion (matches the [S-15] suppression)
//   - once per window boot, plus explicit replays from the settings preview
//     button (view switches replay only the per-widget intro, never the overlay)
//   - ends event-driven via the overlay's fade-out `animationend`, no timers
//     in the correctness path; a stuck `active` would leave the whole desktop
//     click-blocked.
//
// Each style keeps its OWN visual profile (hyperspace.ts / hacker.ts); this
// module only knows the two timings it needs to coordinate the shared parts:
// the reveal point (when widgets are released to animate in) and the fade tail.

import { createSignal } from 'solid-js';
import { type WidgetStartupAnimation } from './design.ts';
import { HYPERSPACE_PROFILES, type HyperspaceStyle } from './hyperspace.ts';
import { sheetAxisFor } from './introSheets.ts';
import { bumpHackerPlay } from './hacker.ts';

// Startup styles that play a one-shot full-screen overlay at boot. Only
// `hyperspace` qualifies ([S-16]); `hacker` is a normal per-widget intro (its
// canvas renders INSIDE each tile, see [S-23]/HackerTile), so it never expands
// the window region. A closed union so `bootOverlayStyle()` is exhaustive at
// every call site (App picks the overlay component).
export type OverlayStyle = HyperspaceStyle;
const OVERLAY_STYLES = new Set<WidgetStartupAnimation>(['hyperspace']);
export function isOverlayStyle(a: WidgetStartupAnimation): a is OverlayStyle {
  return OVERLAY_STYLES.has(a);
}

// Per-widget stagger cap: the `min(index · 45ms, 540ms)` cascade of [S-15], also
// what makes the overlay outlive the slowest widget's intro.
export const INTRO_STAGGER_CAP_MS = 540;
const INTRO_STAGGER_STEP_MS = 45;

/**
 * The [S-15] cascade delay for the row at `index`, in ms — the ONE place the
 * formula lives. Both the render path (WidgetGrid's inline `animation-delay`)
 * and the replay poke below call it, so they can never disagree about a style's
 * timing.
 *
 * The sheet styles ([S-28]) take NO stagger: each side flies in as a rigid
 * block, so every widget must start on the same frame. Cascading them makes
 * tiles that travel the same distance leave at different times and slide across
 * one another — which is exactly what a second copy of this formula in the
 * replay path used to do, while the boot render got it right.
 */
export function introStaggerMs(anim: WidgetStartupAnimation, index: number): number {
  if (sheetAxisFor(anim)) return 0;
  return Math.min(index * INTRO_STAGGER_STEP_MS, INTRO_STAGGER_CAP_MS);
}

/**
 * Base delay before a widget's CSS intro starts, so it holds until the boot
 * overlay's reveal point (the hyperspace jump, [S-16]). 0 for
 * everything else, including hacker, which has no widget CSS intro (its tile
 * canvas owns the timing, [S-23]).
 */
export function introBaseDelayMs(anim: WidgetStartupAnimation): number {
  if (isOverlayStyle(anim)) return HYPERSPACE_PROFILES[anim].jumpMs;
  return 0;
}

/** Total overlay lifetime for a style + per-widget intro duration ([S-15] ms). */
export function overlayTotalMs(style: OverlayStyle, widgetIntroMs: number): number {
  const p = HYPERSPACE_PROFILES[style];
  return p.jumpMs + INTRO_STAGGER_CAP_MS + widgetIntroMs + p.fadeMs;
}

const [style, setStyle] = createSignal<OverlayStyle | null>(null);
let bootConsumed = false;

// ── Intro mode ([S-24]/[S-16]) ──
// Which globalDesign key styles the per-widget intro: the boot mount (and the
// startup settings replay) play widgetStartupAnimation; a view change plays
// widgetTransitionAnimation; a screen unlock plays widgetUnlockAnimation.
// WidgetGrid reads this signal to pick the matching style/duration keys (see
// introAnimFor/introMsFor in design.ts); views.ts flips it to 'transition' on
// every real view switch (NOT the boot-time monitor-init switch, so a
// multi-monitor boot still plays the startup style).
export type IntroAnimMode = 'startup' | 'transition' | 'unlock';
const [introMode, setIntroMode] = createSignal<IntroAnimMode>('startup');

/** Which design key currently styles the per-widget intro ([S-24]). */
export const introAnimMode = introMode;

// ── Armed intro plays ([S-27]) ──
// An intro play is a ONE-SHOT, armed only by the events that are allowed to
// animate: window boot (beginBootOverlay), a real view transition
// (markViewTransition), the boot-time monitor-init switch (armIntroPlay via
// views.ts), and a settings replay. The next WidgetGrid mount consumes it; a
// grid that mounts with nothing armed renders WITHOUT intro animations. View
// list edits, demo flips, game-mode resume etc. remount the grid but must not
// replay, and widgets placed into a live grid never animate in.
let pendingIntroPlay = false;

/** Arm a one-shot intro play for the next grid mount ([S-27]). */
export function armIntroPlay(): void {
  pendingIntroPlay = true;
}

/** Consume the armed play (called once per WidgetGrid mount, [S-27]). */
export function consumeIntroPlay(): boolean {
  const v = pendingIntroPlay;
  pendingIntroPlay = false;
  return v;
}

// Settings replays bump this nonce so a grid that mounted SILENT (no armed
// play → widgets carry no intro classes) can re-acquire them and play the
// preview; rows compare against the nonce they captured at creation ([S-27]).
const [replayNonce, setReplayNonce] = createSignal(0);
export const introReplayNonce = replayNonce;

/** A real view change happened; later mounts play the TRANSITION style. */
export function markViewTransition(): void {
  setIntroMode('transition');
  armIntroPlay();
}

/** Which full-screen boot overlay is mounted, or null when none is. */
export const bootOverlayStyle = style;
/** True while a boot overlay is mounted (window region = full screen). */
export const bootOverlayActive = (): boolean => style() !== null;

/**
 * Arm the overlay at window boot. Call once from App's component body (before
 * the grid renders, so widgets see the active state and add the reveal delay).
 */
export function beginBootOverlay(anim: WidgetStartupAnimation, reducedMotion: boolean): void {
  if (bootConsumed) return;
  bootConsumed = true;
  // Arm the boot's intro play for EVERY style ([S-27]); 'none' and reduced
  // motion are suppressed downstream (class gate / CSS), not by staying silent,
  // so there is one arming path, not a per-style fork.
  armIntroPlay();
  if (!isOverlayStyle(anim) || reducedMotion) return;
  setStyle(anim);
}

/** Overlay fade-out finished (animationend): unmount + shrink the region. */
export function endBootOverlay(): void {
  setStyle(null);
}

/**
 * Replay an intro animation on demand for `mode` (settings preview buttons,
 * delivered cross-window via the 'startup-anim-replay' transient, and the real
 * screen-unlock event for the 'unlock' mode). Restarts every mounted widget's
 * intro by bouncing its animation-name through 'none' (one reflow flushes the
 * override) and re-arming the [S-15] stagger delays, with the active style's
 * reveal base when the overlay plays. Ignored mid-show: the overlay's own
 * animationend still owns the teardown.
 */
export function replayStartupAnimation(
  anim: WidgetStartupAnimation,
  reducedMotion: boolean,
  mode: IntroAnimMode = 'startup',
): void {
  if (reducedMotion || anim === 'none' || bootOverlayActive()) return;
  // The preview must show the mode's style even after a view switch flipped the
  // grid to the transition style ([S-24]). Set the mode FIRST: the grid's
  // class/style react synchronously, so the manual delay writes below land on
  // top of (not under) that re-render.
  setIntroMode(mode);
  // The full-screen overlay plays for the boot-style moments (startup / unlock),
  // never for a view-transition preview; a view switch only ever plays the
  // per-widget intro ([S-24]).
  if (mode !== 'transition' && isOverlayStyle(anim)) setStyle(anim);
  // Re-acquire intro classes on a silently-mounted grid ([S-27]) BEFORE the
  // element pokes below; the rows' class bindings react synchronously, so the
  // querySelectorAll sees every widget, including ones that mounted without an
  // armed play (and would otherwise carry no .intro class to restart).
  setReplayNonce(n => n + 1);
  // hacker renders per-widget canvases, not an overlay, so restart them too (the
  // widgets don't remount on a same-view preview replay, so the tiles need a
  // poke). [S-23]
  if (anim === 'hacker') bumpHackerPlay();
  // Hold widgets to the overlay reveal point only when the overlay actually
  // mounted (startup/unlock overlay styles, set just above). A transition-mode
  // hyperspace preview has no overlay, so its widgets warp in with no base delay.
  const base = bootOverlayActive() ? introBaseDelayMs(anim) : 0;
  const els = document.querySelectorAll<HTMLElement>('.view-root.active .widget.intro');
  els.forEach(el => { el.style.animationName = 'none'; });
  void document.body.offsetWidth; // flush the 'none' so re-enabling restarts
  els.forEach((el, i) => {
    el.style.animationDelay = `${base + introStaggerMs(anim, i)}ms`;
    el.style.animationName = '';
  });
}

/** Test seam: clear the one-shot guard between cases. */
export function resetBootOverlayForTest(): void {
  bootConsumed = false;
  pendingIntroPlay = false;
  setStyle(null);
  setIntroMode('startup');
}
