// @vitest-environment jsdom
//
// Safe-mode boot guard (1.19 B2). A normal boot must NEVER reach safe mode; only
// THRESHOLD consecutive failures of ONE window trips it. Keyed per window label so
// a normal multi-monitor startup (windows share localStorage) can't falsely trip.
import { describe, it, expect, beforeEach } from 'vitest';
import {
  beginBoot, markBootSuccess, clearBootAttempts, resetAllState, SAFE_MODE_THRESHOLD,
} from './safeMode.ts';

const W = 'main';

describe('safeMode: boot-attempt guard', () => {
  beforeEach(() => localStorage.clear());

  it('a normal boot loop (begin then mark-success) never reaches safe mode', () => {
    for (let i = 0; i < 10; i++) {
      expect(beginBoot(W)).toBe(false);
      markBootSuccess(W);
    }
  });

  it('enters safe mode on the THRESHOLD-th consecutive failure of one window', () => {
    for (let i = 1; i < SAFE_MODE_THRESHOLD; i++) expect(beginBoot(W)).toBe(false);
    expect(beginBoot(W)).toBe(true);
  });

  // Regression: localStorage is shared across windows; a single counter falsely
  // tripped on a normal multi-monitor boot (N windows -> N -> >= THRESHOLD).
  it('per-window keys: many windows each booting once never trip each other', () => {
    for (const w of ['monitor-1', 'monitor-2', 'monitor-3', 'monitor-4', 'monitor-5']) {
      expect(beginBoot(w)).toBe(false); // each is its own count of 1, not the shared 5
    }
  });

  it('markBootSuccess resets only that window counter', () => {
    beginBoot('a'); beginBoot('a'); beginBoot('b');
    markBootSuccess('a');
    expect(beginBoot('a')).toBe(false); // a reset to 1
    expect(beginBoot('b')).toBe(false); // b independent (now 2)
  });

  it('clearBootAttempts (Try again) resets that window counter', () => {
    beginBoot(W); beginBoot(W);
    clearBootAttempts(W);
    expect(beginBoot(W)).toBe(false);
  });

  it('resetAllState wipes persisted state + all counters', () => {
    localStorage.setItem('widget-layout', '{}');
    beginBoot(W); beginBoot(W);
    resetAllState();
    expect(localStorage.getItem('widget-layout')).toBe(null);
    expect(beginBoot(W)).toBe(false);
  });
});
