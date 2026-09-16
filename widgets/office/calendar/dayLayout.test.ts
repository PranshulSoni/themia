import { describe, it, expect } from 'vitest';
import { assignEventLanes } from './dayLayout.ts';

describe('assignEventLanes ([S-06] day-view overlap)', () => {
  it('empty input', () => {
    expect(assignEventLanes([])).toEqual([]);
  });

  it('non-overlapping events each get a single full-width column', () => {
    const a = assignEventLanes([{ start: 0, end: 60 }, { start: 60, end: 120 }, { start: 120, end: 180 }]);
    expect(a).toEqual([{ col: 0, cols: 1 }, { col: 0, cols: 1 }, { col: 0, cols: 1 }]);
  });

  // Regression: two overlapping meetings used to render at the same left/right
  // and hide each other. They must split into two side-by-side lanes.
  it('two overlapping events split into two side-by-side lanes', () => {
    const a = assignEventLanes([{ start: 0, end: 60 }, { start: 30, end: 90 }]);
    expect(a[0]).toEqual({ col: 0, cols: 2 });
    expect(a[1]).toEqual({ col: 1, cols: 2 });
  });

  it('returns assignments in input order regardless of start times', () => {
    const a = assignEventLanes([{ start: 30, end: 90 }, { start: 0, end: 60 }]);
    expect(a[0]).toEqual({ col: 1, cols: 2 }); // the later-starting one (index 0)
    expect(a[1]).toEqual({ col: 0, cols: 2 });
  });

  it('a gap between clusters resets the lane count', () => {
    const a = assignEventLanes([{ start: 0, end: 60 }, { start: 30, end: 90 }, { start: 120, end: 180 }]);
    expect(a[0]).toEqual({ col: 0, cols: 2 });
    expect(a[1]).toEqual({ col: 1, cols: 2 });
    expect(a[2]).toEqual({ col: 0, cols: 1 });
  });

  it('reuses a freed column within a cluster', () => {
    const a = assignEventLanes([{ start: 0, end: 30 }, { start: 10, end: 40 }, { start: 35, end: 60 }]);
    expect(a[0]).toEqual({ col: 0, cols: 2 });
    expect(a[1]).toEqual({ col: 1, cols: 2 });
    expect(a[2]).toEqual({ col: 0, cols: 2 }); // reuses lane 0 (freed at 30 <= 35)
  });

  it('touching intervals do not count as overlapping', () => {
    const a = assignEventLanes([{ start: 0, end: 60 }, { start: 60, end: 120 }]);
    expect(a).toEqual([{ col: 0, cols: 1 }, { col: 0, cols: 1 }]);
  });

  it('three-way overlap yields three lanes', () => {
    const a = assignEventLanes([{ start: 0, end: 90 }, { start: 10, end: 90 }, { start: 20, end: 90 }]);
    expect(a.map(x => x.cols)).toEqual([3, 3, 3]);
    expect(new Set(a.map(x => x.col))).toEqual(new Set([0, 1, 2]));
  });
});
