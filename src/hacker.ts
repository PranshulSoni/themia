// Hacker-mode startup animation: per-widget profile (src/SPEC.md [S-23]).
//
// `hacker` is a normal per-widget intro style (NOT a full-screen boot overlay):
// each widget tile renders its OWN canvas (components/HackerTile.tsx) that draws
// one movie-hacker animation (a scrolling console / hex-dump, a rotating
// protein, a CCTV feed, a radar/geometry scope, or an oscilloscope), picked by
// widget index. Because the canvas lives inside the widget it plays on EVERY
// widget mount (boot AND view switches) and needs no coordinate math. This
// module owns the deterministic VARIANT assignment + the TIMING; the drawing is
// cosmetic canvas work in the tile.
//
// Length follows the "Animation duration" slider ([S-23]): the hacker "churn"
// (the panel runs → the widget decodes in) scales linearly with the configured
// per-widget intro duration `widgetStartupAnimationMs`, so the slider tunes how
// long the hacker show lasts (every other style already respects it).

import { createSignal } from 'solid-js';

// Fraction of the per-widget duration the tile spends COVERING the widget
// (churning); the rest is the fade-out that reveals it. cover + fade = the
// configured duration exactly.
const HACKER_COVER_FRAC = 0.65;

export interface HackerTileTiming {
  /** ms from mount until the tile starts fading out (cover end, incl. stagger). */
  revealMs: number;
  /** fade-out duration (ms). */
  fadeMs: number;
  /** ms from mount until fully revealed (revealMs + fadeMs). */
  endMs: number;
}

/**
 * Per-widget tile timing for a configured intro duration `introMs` (the
 * mode-picked Animation-duration slider) and widget `index`. The tile's lifetime
 * **start-to-finish equals `introMs`** ([S-23]): it covers the widget for the
 * first `HACKER_COVER_FRAC`, then fades over the rest. Widgets cascade via the
 * standard [S-15] per-index stagger added to the reveal point, so the FIRST
 * widget finishes at exactly `introMs`.
 */
export function hackerTileTiming(introMs: number, index: number): HackerTileTiming {
  const d = Math.max(1, introMs);
  const stagger = Math.min(index * 45, 540);
  const coverMs = Math.round(d * HACKER_COVER_FRAC);
  const fadeMs = d - coverMs; // cover + fade === d exactly (per widget)
  const revealMs = coverMs + stagger;
  return { revealMs, fadeMs, endMs: revealMs + fadeMs };
}

// A panel kind drawn into a widget tile. `console` is a scrolling log/hex feed;
// `molecule` a rotating wireframe protein; `radar` a sweeping scope with
// rotating wireframe polygons; `camera` a CCTV feed with a tracking reticle;
// `wave` an oscilloscope. Closed union so the draw dispatch is exhaustive.
export type HackerPanelKind = 'console' | 'molecule' | 'radar' | 'camera' | 'wave';

export interface HackerVariant {
  kind: HackerPanelKind;
  // `console` only: an address/hex-dump feed instead of log lines.
  hex?: boolean;
  // Short label shown in the tile's corner (e.g. "NET", "SEQ", "CAM").
  tag: string;
}

// Every widget gets ONE animation; tiles cycle this list by widget index
// ([S-23]). Ordered so that NO two adjacent widgets (including the wrap) share a
// kind, so a row of tiles reads as a different feed each, not a repeat. There
// are two `console` variants (log + hex dump) deliberately placed apart.
export const HACKER_VARIANTS: HackerVariant[] = [
  { kind: 'console',  tag: 'NET' },
  { kind: 'molecule', tag: 'SEQ' },
  { kind: 'camera',   tag: 'CAM' },
  { kind: 'console',  tag: 'MEM', hex: true },
  { kind: 'radar',    tag: 'TRACE' },
  { kind: 'wave',     tag: 'SIG' },
];

/** The animation variant for the widget at index `i` (cycles HACKER_VARIANTS). */
export function hackerVariant(i: number): HackerVariant {
  const n = HACKER_VARIANTS.length;
  return HACKER_VARIANTS[((i % n) + n) % n];
}

// Replay token ([S-23]). Each HackerTile replays its canvas on every widget
// MOUNT (boot / view switch) on its own. The settings ▶ preview, however,
// restarts the intro WITHOUT remounting the widgets (same view), so it bumps
// this signal, which every mounted tile watches to restart its canvas.
const [playNonce, setPlayNonce] = createSignal(0);
/** Tiles watch this to restart their canvas on a settings-preview replay. */
export const hackerPlayNonce = playNonce;
/** Restart every mounted hacker tile's canvas (settings ▶ preview). */
export function bumpHackerPlay(): void {
  setPlayNonce(n => n + 1);
}
