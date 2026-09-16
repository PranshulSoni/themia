// @vitest-environment jsdom
//
// The area group's expand/collapse animation state ([S-43] / group [S-16]).
// Three surfaces read this at once — the grid (tile transition + members kept
// for the fade), the group widget (area kept mounted while it closes) and the
// hit-mask push (must not shrink the region mid-animation) — so what matters is
// that a phase starts on a real toggle only, and always ends.

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
  GROUP_COLLAPSE_MS, anyGroupCollapsing, collapseTransitions, groupCollapsePhase,
  resetGroupCollapse, syncGroupCollapse,
} from './groupCollapse.ts';

describe('collapseTransitions (pure)', () => {
  it('S-43_a group seen for the first time never animates', () => {
    // A view that mounts with a collapsed group renders it collapsed; playing a
    // close on mount would collapse an area the user is already looking at.
    expect(collapseTransitions(new Map(), [{ id: 'g', collapsed: true }])).to.deep.equal([]);
    expect(collapseTransitions(new Map(), [{ id: 'g', collapsed: false }])).to.deep.equal([]);
  });

  it('S-43_only a real flip starts a phase, and the direction follows the flag', () => {
    const seen = new Map([['g', false], ['h', true]]);
    expect(collapseTransitions(seen, [
      { id: 'g', collapsed: true },   // expanded → collapsed
      { id: 'h', collapsed: true },   // unchanged
    ])).to.deep.equal([{ id: 'g', phase: 'out' }]);
    expect(collapseTransitions(seen, [{ id: 'h', collapsed: false }]))
      .to.deep.equal([{ id: 'h', phase: 'in' }]);
  });
});

describe('group collapse phases', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    resetGroupCollapse();
  });
  afterEach(() => {
    resetGroupCollapse();
    vi.useRealTimers();
  });

  it('S-43_collapsing runs an `out` phase that ends on its own', () => {
    syncGroupCollapse([{ id: 'g', collapsed: false }]);
    expect(groupCollapsePhase('g')).to.equal(undefined);

    syncGroupCollapse([{ id: 'g', collapsed: true }]);
    expect(groupCollapsePhase('g')).to.equal('out');
    // While it closes the hit-mask must hold the shape it last pushed.
    expect(anyGroupCollapsing()).to.equal(true);

    vi.advanceTimersByTime(GROUP_COLLAPSE_MS);
    expect(groupCollapsePhase('g')).to.equal(undefined);
    expect(anyGroupCollapsing()).to.equal(false);
  });

  it('S-43_expanding runs an `in` phase, which never holds the mask', () => {
    syncGroupCollapse([{ id: 'g', collapsed: true }]);
    syncGroupCollapse([{ id: 'g', collapsed: false }]);
    expect(groupCollapsePhase('g')).to.equal('in');
    // Opening only ADDS painted area, and the region is already a superset of
    // it, so there is nothing to hold back.
    expect(anyGroupCollapsing()).to.equal(false);
    vi.advanceTimersByTime(GROUP_COLLAPSE_MS);
    expect(groupCollapsePhase('g')).to.equal(undefined);
  });

  it('S-43_re-toggling mid-flight replaces the phase, and the old timer cannot end it early', () => {
    syncGroupCollapse([{ id: 'g', collapsed: false }]);
    syncGroupCollapse([{ id: 'g', collapsed: true }]);
    vi.advanceTimersByTime(GROUP_COLLAPSE_MS - 40);
    syncGroupCollapse([{ id: 'g', collapsed: false }]);
    expect(groupCollapsePhase('g')).to.equal('in');

    // The first timer's deadline passes: it must not clear the new phase.
    vi.advanceTimersByTime(50);
    expect(groupCollapsePhase('g')).to.equal('in');
    vi.advanceTimersByTime(GROUP_COLLAPSE_MS);
    expect(groupCollapsePhase('g')).to.equal(undefined);
  });

  it('S-43_a group deleted mid-animation drops its phase, so the mask cannot stay held', () => {
    syncGroupCollapse([{ id: 'g', collapsed: false }]);
    syncGroupCollapse([{ id: 'g', collapsed: true }]);
    expect(anyGroupCollapsing()).to.equal(true);

    syncGroupCollapse([]); // removed from the layout
    expect(groupCollapsePhase('g')).to.equal(undefined);
    expect(anyGroupCollapsing()).to.equal(false);
  });

  it('S-43_reduced motion starts no phase at all, so every surface flips at once', () => {
    // jsdom ships no matchMedia at all (which is why the source calls it
    // optionally — no media query support means no reduced-motion request).
    const real = window.matchMedia;
    window.matchMedia = ((q: string) => ({ matches: q.includes('reduced-motion') })) as typeof window.matchMedia;
    try {
      syncGroupCollapse([{ id: 'g', collapsed: false }]);
      syncGroupCollapse([{ id: 'g', collapsed: true }]);
      expect(groupCollapsePhase('g')).to.equal(undefined);
      expect(anyGroupCollapsing()).to.equal(false);
    } finally {
      window.matchMedia = real;
    }
  });
});
