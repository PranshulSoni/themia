// Stack widget model: pins widgets/others/stack/SPEC.md [S-03]. The stack is
// accordion-only; tabbed presentation is a separate widget
// (widgets/others/tabs).

import { describe, it, expect } from 'vitest';
import { resolveExpandedId } from './stackModel.ts';

describe('the one open child ([S-03]): title rows, at most one open', () => {
  const kids = ['a', 'b', 'c'];

  it('S-03_absent/empty resolves to nothing open (title rows only)', () => {
    expect(resolveExpandedId(kids, undefined)).toBeNull();
    expect(resolveExpandedId(kids, null)).toBeNull();
    expect(resolveExpandedId(kids, '')).toBeNull();
  });

  it('S-03_a current child id opens exactly that child', () => {
    expect(resolveExpandedId(kids, 'b')).toBe('b');
  });

  it('S-03_a stale id (child popped out / removed) opens nothing', () => {
    expect(resolveExpandedId(kids, 'gone')).toBeNull();
    // ...and is never rewritten, so it re-applies once the child is back.
    expect(resolveExpandedId([...kids, 'gone'], 'gone')).toBe('gone');
  });

  it('S-03_junk resolves to nothing open, never a half-built state', () => {
    for (const junk of [42, true, ['b'], { id: 'b' }]) {
      expect(resolveExpandedId(kids, junk)).toBeNull();
    }
  });

  it('S-03_an empty stack has nothing to open', () => {
    expect(resolveExpandedId([], 'b')).toBeNull();
  });

  it('S-03_a stack carrying only an older build\'s key opens nothing', () => {
    // `collapsedIds` / `expandedIds` from earlier builds are inert: the stack
    // reads `expandedId` and nothing else, so an old instance starts closed
    // rather than carrying a second, contradictory source of truth ([S-15]).
    expect(resolveExpandedId(kids, ['a', 'b'])).toBeNull();
  });
});
