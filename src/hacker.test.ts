// [S-23] Hacker startup animation: per-widget variant assignment + timing.
// The visual draw lives in components/HackerTile.tsx; this file covers only the
// deterministic profile, which needs no DOM.

import { describe, it, expect } from 'vitest';
import {
  hackerVariant,
  HACKER_VARIANTS,
  hackerTileTiming,
  type HackerPanelKind,
} from './hacker.ts';

describe('S-23 per-widget animation variants', () => {
  it('covers every movie-hacker trope across the variant list', () => {
    const kinds = new Set<HackerPanelKind>(HACKER_VARIANTS.map(v => v.kind));
    for (const k of ['console', 'molecule', 'radar', 'camera', 'wave'] as HackerPanelKind[]) {
      expect(kinds.has(k)).toBe(true);
    }
    // Two console variants: a log feed and a hex dump.
    expect(HACKER_VARIANTS.filter(v => v.kind === 'console').length).toBe(2);
    expect(HACKER_VARIANTS.some(v => v.kind === 'console' && v.hex)).toBe(true);
  });

  it('assigns by widget index and cycles deterministically', () => {
    const n = HACKER_VARIANTS.length;
    for (let i = 0; i < 25; i++) {
      expect(hackerVariant(i)).toEqual(hackerVariant(i)); // stable
      expect(hackerVariant(i)).toBe(HACKER_VARIANTS[i % n]); // cycles
    }
  });

  it('gives ADJACENT widgets different kinds (incl. the wrap)', () => {
    // The whole point of the request: a row of tiles reads as distinct feeds.
    for (let i = 0; i < HACKER_VARIANTS.length * 3; i++) {
      expect(hackerVariant(i).kind).not.toBe(hackerVariant(i + 1).kind);
    }
  });

  it('handles negative / out-of-range indices safely', () => {
    expect(hackerVariant(-1)).toBe(HACKER_VARIANTS[HACKER_VARIANTS.length - 1]);
    expect(hackerVariant(0)).toBe(HACKER_VARIANTS[0]);
  });
});

describe('S-23 tile timing: start-to-finish equals the set duration', () => {
  it("the first widget's tile finishes at exactly the configured duration", () => {
    // The user's contract: the set duration IS the animation length.
    expect(hackerTileTiming(500, 0).endMs).toBe(500);
    expect(hackerTileTiming(2000, 0).endMs).toBe(2000);
    expect(hackerTileTiming(800, 0).endMs).toBe(800);
  });

  it('cover + fade always sum to the duration (no overrun)', () => {
    for (const d of [120, 500, 1234, 2000]) {
      const t = hackerTileTiming(d, 0);
      expect(t.revealMs + t.fadeMs).toBe(d);
      expect(t.fadeMs).toBeGreaterThan(0);
      expect(t.revealMs).toBeGreaterThan(0);
    }
  });

  it('later widgets only cascade by the [S-15] stagger (capped)', () => {
    const d = 500;
    expect(hackerTileTiming(d, 1).endMs).toBe(d + 45);
    expect(hackerTileTiming(d, 2).endMs).toBe(d + 90);
    // capped at 540ms past the first widget
    expect(hackerTileTiming(d, 100).endMs).toBe(d + 540);
  });
});
