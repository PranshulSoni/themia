// [S-20] "Hide when idle" (themia-releases#97). Pins the three-way rule and the
// trust-boundary resolution of the persisted config value.

import { describe, it, expect } from 'vitest';
import {
  HIDE_WHEN_IDLE_MODES,
  resolveHideWhenIdle,
  shouldHideMusic,
  type HideWhenIdle,
} from './hideWhenIdle.ts';

const PLAYING = { is_playing: true };
const PAUSED = { is_playing: false };

describe('S-20_resolveHideWhenIdle — config trust boundary', () => {
  it('passes each known mode through unchanged', () => {
    for (const m of HIDE_WHEN_IDLE_MODES) expect(resolveHideWhenIdle(m)).toBe(m);
  });

  it('falls back to `never` on absent / corrupt / unknown values', () => {
    // `never` is the VISIBLE state: a bad value must never make the widget
    // vanish, because an invisible widget is the hardest one to fix.
    for (const bad of [undefined, null, '', 'always', 'NEVER', true, 0, 1, {}, []]) {
      expect(resolveHideWhenIdle(bad)).toBe('never');
    }
  });
});

describe('S-20_shouldHideMusic — never', () => {
  it('stays visible in every playback state', () => {
    expect(shouldHideMusic('never', null, false)).toBe(false);
    expect(shouldHideMusic('never', PAUSED, false)).toBe(false);
    expect(shouldHideMusic('never', PLAYING, false)).toBe(false);
  });
});

describe('S-20_shouldHideMusic — noSession', () => {
  it('hides only when there is no media session at all', () => {
    expect(shouldHideMusic('noSession', null, false)).toBe(true);
  });

  it('keeps a paused session on screen so it can be resumed', () => {
    expect(shouldHideMusic('noSession', PAUSED, false)).toBe(false);
  });

  it('keeps a playing session on screen', () => {
    expect(shouldHideMusic('noSession', PLAYING, false)).toBe(false);
  });
});

describe('S-20_shouldHideMusic — notPlaying', () => {
  it('hides when there is no media session', () => {
    expect(shouldHideMusic('notPlaying', null, false)).toBe(true);
  });

  it('also hides the lingering paused card', () => {
    expect(shouldHideMusic('notPlaying', PAUSED, false)).toBe(true);
  });

  it('shows again as soon as playback resumes', () => {
    expect(shouldHideMusic('notPlaying', PLAYING, false)).toBe(false);
  });
});

describe('S-20_shouldHideMusic — edit mode always wins', () => {
  it('never hides while the grid is editable, whatever the mode or state', () => {
    // The only route back to a hidden widget: it must be findable, movable and
    // configurable the whole time edit mode is on.
    const modes: HideWhenIdle[] = ['never', 'noSession', 'notPlaying'];
    for (const mode of modes) {
      for (const info of [null, PAUSED, PLAYING]) {
        expect(shouldHideMusic(mode, info, true)).toBe(false);
      }
    }
  });
});
