// @vitest-environment jsdom
//
// [S-16] Boot-overlay lifecycle: the once-per-boot full-screen overlay. Only
// `hyperspace` is an overlay style; `hacker` is a per-widget intro ([S-23],
// HackerTile), so it never activates the overlay. The lifecycle MUST be strict:
// starts only for an overlay style, never under reduced motion, once per window
// boot (plus explicit replays from the settings preview button), and ends
// (region shrinks back to the widget rects) when the overlay reports its
// fade-out finished. A stuck `active` would leave the whole desktop
// click-blocked, and that's the bug class these tests pin.

import { describe, it, expect, beforeEach } from 'vitest';
import {
  bootOverlayActive,
  bootOverlayStyle,
  beginBootOverlay,
  endBootOverlay,
  replayStartupAnimation,
  resetBootOverlayForTest,
  introBaseDelayMs,
  introStaggerMs,
  overlayTotalMs,
  isOverlayStyle,
  INTRO_STAGGER_CAP_MS,
  introAnimMode,
  markViewTransition,
  armIntroPlay,
  consumeIntroPlay,
  introReplayNonce,
} from './bootOverlay.ts';
import { HYPERSPACE_JUMP_MS, HYPERSPACE_FADE_MS } from './hyperspace.ts';

beforeEach(() => {
  resetBootOverlayForTest();
  document.body.innerHTML = '';
});

describe('S-16 boot-overlay lifecycle', () => {
  it('begins only for an overlay style (hyperspace), never plain styles or hacker', () => {
    beginBootOverlay('scale', false);
    expect(bootOverlayActive()).toBe(false);
    resetBootOverlayForTest();
    beginBootOverlay('hyperspace', false);
    expect(bootOverlayActive()).toBe(true);
    expect(bootOverlayStyle()).toBe('hyperspace');
    resetBootOverlayForTest();
    // hacker is a per-widget intro ([S-23]), NOT a full-screen overlay.
    beginBootOverlay('hacker', false);
    expect(bootOverlayActive()).toBe(false);
    expect(bootOverlayStyle()).toBe(null);
  });

  it('never begins under prefers-reduced-motion', () => {
    beginBootOverlay('hyperspace', true);
    expect(bootOverlayActive()).toBe(false);
  });

  it('is one-shot per window boot: a second begin is a no-op', () => {
    beginBootOverlay('hyperspace', false);
    endBootOverlay();
    beginBootOverlay('hyperspace', false);
    expect(bootOverlayActive()).toBe(false);
  });

  it('end deactivates and clears the style (region can shrink back)', () => {
    beginBootOverlay('hyperspace', false);
    endBootOverlay();
    expect(bootOverlayActive()).toBe(false);
    expect(bootOverlayStyle()).toBe(null);
  });

  it('isOverlayStyle narrows the overlay style (hyperspace) from the rest', () => {
    expect(isOverlayStyle('hyperspace')).toBe(true);
    expect(isOverlayStyle('hacker')).toBe(false); // per-widget, not an overlay
    expect(isOverlayStyle('scale')).toBe(false);
    expect(isOverlayStyle('none')).toBe(false);
  });
});

describe('S-15/S-28 stagger: one formula for the render path and the replay', () => {
  it('cascades a normal style by 45ms per row, capped', () => {
    expect(introStaggerMs('scale', 0)).toBe(0);
    expect(introStaggerMs('scale', 3)).toBe(135);
    expect(introStaggerMs('scale', 99)).toBe(INTRO_STAGGER_CAP_MS);
  });

  it('[S-28] gives the sheet styles NO stagger at any index (rigid sheets)', () => {
    for (const style of ['sides', 'topbottom'] as const) {
      for (const i of [0, 1, 5, 99]) expect(introStaggerMs(style, i)).toBe(0);
    }
  });
});

describe('S-16 timing: widgets hold until the overlay reveal point', () => {
  it('introBaseDelayMs holds hyperspace to the jump; everything else is 0', () => {
    expect(introBaseDelayMs('hyperspace')).toBe(HYPERSPACE_JUMP_MS);
    expect(introBaseDelayMs('hacker')).toBe(0); // per-widget tile owns its timing
    expect(introBaseDelayMs('scale')).toBe(0);
    expect(introBaseDelayMs('none')).toBe(0);
  });

  it('overlayTotalMs covers reveal + widget cascade + the hyperspace fade', () => {
    expect(overlayTotalMs('hyperspace', 500)).toBe(
      HYPERSPACE_JUMP_MS + INTRO_STAGGER_CAP_MS + 500 + HYPERSPACE_FADE_MS,
    );
  });
});

describe('S-16/S-23 replay: settings preview button', () => {
  function mountWidgets(n: number, style: string): HTMLElement[] {
    const root = document.createElement('div');
    root.className = 'view-root active';
    const els: HTMLElement[] = [];
    for (let i = 0; i < n; i++) {
      const w = document.createElement('div');
      w.className = `widget intro intro-${style}`;
      root.appendChild(w);
      els.push(w);
    }
    document.body.appendChild(root);
    return els;
  }

  it('replays the overlay even after the boot shot was consumed', () => {
    beginBootOverlay('hyperspace', false);
    endBootOverlay();
    replayStartupAnimation('hyperspace', false);
    expect(bootOverlayActive()).toBe(true);
    expect(bootOverlayStyle()).toBe('hyperspace');
  });

  it('never replays under reduced motion or when the style is none', () => {
    replayStartupAnimation('hyperspace', true);
    expect(bootOverlayActive()).toBe(false);
    replayStartupAnimation('none', false);
    expect(bootOverlayActive()).toBe(false);
  });

  it('a hacker replay restarts its tiles without activating an overlay', () => {
    replayStartupAnimation('hacker', false);
    expect(bootOverlayActive()).toBe(false); // hacker is per-widget, not an overlay
  });

  it('re-arms each widget intro with the hyperspace jump base delay', () => {
    const els = mountWidgets(2, 'hyperspace');
    replayStartupAnimation('hyperspace', false);
    expect(els[0].style.animationDelay).toBe(`${HYPERSPACE_JUMP_MS}ms`);
    expect(els[1].style.animationDelay).toBe(`${HYPERSPACE_JUMP_MS + 45}ms`);
  });

  it('a plain style restarts widgets without the overlay (no base delay)', () => {
    const els = mountWidgets(2, 'scale');
    replayStartupAnimation('scale', false);
    expect(bootOverlayActive()).toBe(false);
    expect(els[0].style.animationDelay).toBe('0ms');
    expect(els[1].style.animationDelay).toBe('45ms');
  });

  // [S-28] A replay used to carry its OWN copy of the [S-15] cascade formula,
  // so it re-staggered the sheet styles the boot render lays out flat: tiles
  // that travel the same rigid distance left at different times and slid across
  // one another. Reported as "widgets still overlap when sliding, but not on
  // the first slide" — the first slide is the render path, every later one is
  // this poke. Both now go through introStaggerMs.
  for (const style of ['sides', 'topbottom'] as const) {
    it(`S-28 a ${style} replay leaves the whole sheet on one delay, never a cascade`, () => {
      const els = mountWidgets(4, style);
      replayStartupAnimation(style, false);
      expect(els.map((el) => el.style.animationDelay)).toEqual(['0ms', '0ms', '0ms', '0ms']);
    });
  }

  it('is ignored while an overlay is already playing (no mid-show restart)', () => {
    const els = mountWidgets(1, 'hyperspace');
    replayStartupAnimation('hyperspace', false);
    els[0].style.animationDelay = '123ms'; // sentinel: a second replay must not touch it
    replayStartupAnimation('hyperspace', false);
    expect(els[0].style.animationDelay).toBe('123ms');
    expect(bootOverlayActive()).toBe(true);
  });
});

// [S-24] The intro style is mode-picked: the boot mount (and settings replays)
// play the STARTUP style; every later view change plays the TRANSITION style.
// The mode signal here is what WidgetGrid reads to pick between the two
// globalDesign keys; these tests pin its transitions.
describe('S-24 intro mode: startup vs view-transition style', () => {
  it("boots in 'startup' mode", () => {
    expect(introAnimMode()).toBe('startup');
  });

  it("flips to 'transition' on a view change and stays there", () => {
    markViewTransition();
    expect(introAnimMode()).toBe('transition');
    markViewTransition();
    expect(introAnimMode()).toBe('transition');
  });

  it("a settings replay returns to 'startup' so the preview shows the startup style", () => {
    markViewTransition();
    replayStartupAnimation('scale', false);
    expect(introAnimMode()).toBe('startup');
  });

  // [S-16] the unlock replay (screen unlock / its settings preview) plays in its
  // OWN mode, so WidgetGrid picks the unlock style + duration keys.
  it("an unlock replay sets the mode to 'unlock'", () => {
    markViewTransition();
    replayStartupAnimation('scale', false, 'unlock');
    expect(introAnimMode()).toBe('unlock');
  });

  // [S-24] a view-transition PREVIEW plays in 'transition' mode and, like a real
  // view switch, must NOT mount the full-screen overlay even for hyperspace.
  it("a transition-mode replay sets 'transition' and never mounts the overlay", () => {
    replayStartupAnimation('hyperspace', false, 'transition');
    expect(introAnimMode()).toBe('transition');
    expect(bootOverlayActive()).toBe(false);
  });

  it('an ignored replay (reduced motion, style none, mid-show) leaves the mode alone', () => {
    markViewTransition();
    replayStartupAnimation('hyperspace', true); // reduced motion → ignored
    expect(introAnimMode()).toBe('transition');
    replayStartupAnimation('none', false); // style none → ignored
    expect(introAnimMode()).toBe('transition');
    replayStartupAnimation('hyperspace', false); // plays → startup (overlay active)
    markViewTransition();
    replayStartupAnimation('hyperspace', false); // mid-show → ignored
    expect(introAnimMode()).toBe('transition');
  });

  it('resetBootOverlayForTest restores startup mode', () => {
    markViewTransition();
    resetBootOverlayForTest();
    expect(introAnimMode()).toBe('startup');
  });
});

// [S-27] Intro plays are ARMED one-shots: only boot, a real view transition,
// the boot-time monitor-init switch, or a settings replay arm a play. A grid
// mount without an armed play renders WITHOUT intro animations. List edits,
// demo flips, game-mode resume etc. remount the grid but must not replay, and
// widgets placed into a live grid must never animate in.
describe('S-27 armed intro plays: grids mount silent unless a play is armed', () => {
  it('boot arms exactly one play, for ANY style (not just overlay styles)', () => {
    beginBootOverlay('scale', false);
    expect(consumeIntroPlay()).toBe(true);
    expect(consumeIntroPlay()).toBe(false); // one-shot; a remount stays silent
  });

  it('a second beginBootOverlay does not re-arm (one-shot per window boot)', () => {
    beginBootOverlay('scale', false);
    expect(consumeIntroPlay()).toBe(true);
    beginBootOverlay('scale', false);
    expect(consumeIntroPlay()).toBe(false);
  });

  it('boot arms even under reduced motion (CSS owns the suppression, [S-15])', () => {
    beginBootOverlay('hyperspace', true);
    expect(consumeIntroPlay()).toBe(true);
  });

  it('starts un-armed: a grid mount with no boot/transition stays silent', () => {
    expect(consumeIntroPlay()).toBe(false);
  });

  it('markViewTransition arms a play (the view-switch remount animates once)', () => {
    markViewTransition();
    expect(consumeIntroPlay()).toBe(true);
    expect(consumeIntroPlay()).toBe(false);
  });

  it('armIntroPlay arms without touching the mode (boot-time monitor init, [S-24])', () => {
    armIntroPlay();
    expect(introAnimMode()).toBe('startup');
    expect(consumeIntroPlay()).toBe(true);
  });

  it('resetBootOverlayForTest clears a pending play', () => {
    armIntroPlay();
    resetBootOverlayForTest();
    expect(consumeIntroPlay()).toBe(false);
  });
});

// [S-27] Replay nonce: a settings replay bumps a nonce so an already-mounted
// grid whose widgets rendered WITHOUT intro classes (un-armed mount) can
// re-acquire them and play the preview.
describe('S-27 replay nonce: preview works on silently-mounted grids', () => {
  it('bumps when a replay actually plays', () => {
    const before = introReplayNonce();
    replayStartupAnimation('scale', false);
    expect(introReplayNonce()).toBe(before + 1);
  });

  it('does not bump when the replay is ignored (reduced motion / none / mid-show)', () => {
    const before = introReplayNonce();
    replayStartupAnimation('scale', true); // reduced motion → ignored
    replayStartupAnimation('none', false); // style none → ignored
    expect(introReplayNonce()).toBe(before);
    replayStartupAnimation('hyperspace', false); // plays (overlay active)
    const after = introReplayNonce();
    replayStartupAnimation('hyperspace', false); // mid-show → ignored
    expect(introReplayNonce()).toBe(after);
  });
});
