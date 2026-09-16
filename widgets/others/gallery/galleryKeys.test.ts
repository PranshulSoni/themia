import { describe, it, expect } from 'vitest';
import { galleryKeyAction } from './galleryKeys.ts';

describe('galleryKeyAction (Gallery [S-29]/[S-37])', () => {
  it('maps slideshow nav keys when not typing', () => {
    expect(galleryKeyAction('ArrowLeft', 'slideshow', false)).toBe('prev');
    expect(galleryKeyAction('ArrowRight', 'slideshow', false)).toBe('next');
    expect(galleryKeyAction(' ', 'slideshow', false)).toBe('togglePause');
  });

  // Regression for themia-releases #53: Space was eaten out of focused text
  // inputs (notes/todo) that shared the gallery's window. While a text input is
  // focused the gallery must claim NOTHING so every key reaches the input.
  it('claims no key while a text input is focused', () => {
    expect(galleryKeyAction(' ', 'slideshow', true)).toBeNull();
    expect(galleryKeyAction('ArrowLeft', 'slideshow', true)).toBeNull();
    expect(galleryKeyAction('ArrowRight', 'slideshow', true)).toBeNull();
  });

  it('claims nothing outside slideshow mode', () => {
    expect(galleryKeyAction(' ', 'grid', false)).toBeNull();
    expect(galleryKeyAction('ArrowLeft', 'scatter', false)).toBeNull();
    expect(galleryKeyAction('ArrowRight', 'mosaic', false)).toBeNull();
  });

  it('ignores unrelated keys (they reach the input / desktop untouched)', () => {
    expect(galleryKeyAction('a', 'slideshow', false)).toBeNull();
    expect(galleryKeyAction('Enter', 'slideshow', false)).toBeNull();
    expect(galleryKeyAction('Escape', 'slideshow', false)).toBeNull();
  });
});
