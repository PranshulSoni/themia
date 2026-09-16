import { describe, it, expect } from 'vitest';
import { viewInitials, layoutKey, isDemoView } from './viewState.ts';

describe('viewInitials', () => {
  it.each([
    ['Home Office', 'HO'],   // multi-word → first letter of first two words
    ['Work', 'WO'],          // single word → first two chars
    ['a  b', 'AB'],          // collapses runs of whitespace
    ['home', 'HO'],          // upper-cased
    ['A', 'A'],              // single char → itself
    ['', '?'],               // empty → ?
    ['   ', '?'],            // whitespace-only → ?
    ['One Two Three', 'OT'], // only first two words used
  ])('%j -> %j', (input, expected) => {
    expect(viewInitials(input)).toBe(expected);
  });

  it('tolerates a null/undefined name', () => {
    // viewInitials guards `name ?? ''`; callers occasionally pass loosely-typed
    // values from persisted state.
    expect(viewInitials(undefined as unknown as string)).toBe('?');
  });
});

describe('layoutKey', () => {
  it('maps the default view to the legacy un-suffixed key', () => {
    // layout.ts + storage compatibility depend on this exact mapping.
    expect(layoutKey('default')).toBe('widget-layout');
  });
  it('suffixes every other view id', () => {
    expect(layoutKey('work')).toBe('widget-layout-work');
    expect(layoutKey('view-123')).toBe('widget-layout-view-123');
  });
});

describe('isDemoView', () => {
  it('matches the demo- prefix only', () => {
    expect(isDemoView('demo-utilities')).toBe(true);
    expect(isDemoView('default')).toBe(false);
    expect(isDemoView('my-demo')).toBe(false); // prefix, not substring
  });
});
