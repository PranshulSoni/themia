import { describe, it, expect } from 'vitest';
import { interpolate } from './i18nFormat.ts';

describe('interpolate', () => {
  it('returns the template unchanged when no params', () => {
    expect(interpolate('Hello {name}')).toBe('Hello {name}');
  });

  it('substitutes a single placeholder', () => {
    expect(interpolate('Update to {version}', { version: '1.2.3' })).toBe('Update to 1.2.3');
  });

  it('substitutes multiple placeholders', () => {
    expect(interpolate('{a} and {b}', { a: 'x', b: 'y' })).toBe('x and y');
  });

  it('stringifies numeric values', () => {
    expect(interpolate('{n} items', { n: 5 })).toBe('5 items');
  });

  it('leaves unknown placeholders untouched', () => {
    expect(interpolate('{known}/{unknown}', { known: 'k' })).toBe('k/{unknown}');
  });

  it('resolves each placeholder once, does NOT re-substitute a value that looks like a token', () => {
    // Regression for the old order-dependent loop: {a} -> "{b}" must NOT then
    // get replaced by b's value.
    expect(interpolate('{a}', { a: '{b}', b: 'BOOM' })).toBe('{b}');
  });

  it('handles a value that contains the same token text without looping', () => {
    expect(interpolate('{x}', { x: '{x}' })).toBe('{x}');
  });

  it('replaces every occurrence of a repeated placeholder', () => {
    expect(interpolate('{x}-{x}', { x: 'q' })).toBe('q-q');
  });
});
