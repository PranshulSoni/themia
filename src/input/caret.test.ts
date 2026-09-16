// @vitest-environment jsdom
import { describe, it, expect } from 'vitest';
import { clampCaret, caretOffsetFromPoint, focusWithCaret } from './caret.ts';

describe('clampCaret', () => {
  it('passes an in-range offset through', () => {
    expect(clampCaret(3, 10)).toBe(3);
  });

  it('clamps past the end to the length', () => {
    expect(clampCaret(99, 4)).toBe(4);
  });

  it('clamps a negative offset to 0', () => {
    expect(clampCaret(-2, 4)).toBe(0);
  });

  it('parks a non-finite offset at the end', () => {
    expect(clampCaret(NaN, 6)).toBe(6);
  });

  it('truncates a fractional offset', () => {
    expect(clampCaret(2.9, 10)).toBe(2);
  });
});

describe('caretOffsetFromPoint', () => {
  it('returns null when the document exposes no hit test', () => {
    // jsdom implements neither spelling, which is the fallback path itself.
    expect(caretOffsetFromPoint(10, 10, 5)).toBeNull();
  });

  it('clamps whatever the hit test reports to the text length', () => {
    const doc = document as unknown as {
      caretPositionFromPoint?: (x: number, y: number) => { offset: number } | null;
    };
    const original = doc.caretPositionFromPoint;
    doc.caretPositionFromPoint = () => ({ offset: 42 });
    try {
      expect(caretOffsetFromPoint(1, 1, 7)).toBe(7);
    } finally {
      doc.caretPositionFromPoint = original;
    }
  });
});

describe('focusWithCaret', () => {
  it('places a collapsed caret at the offset instead of selecting the value', () => {
    const el = document.createElement('input');
    el.value = 'read the docs';
    document.body.append(el);

    focusWithCaret(el, 5);

    expect(el.selectionStart).toBe(5);
    expect(el.selectionEnd).toBe(5);
    el.remove();
  });

  it('parks the caret at the end when there is no click point', () => {
    const el = document.createElement('textarea');
    el.value = 'ship it';
    document.body.append(el);

    focusWithCaret(el, null);

    expect(el.selectionStart).toBe(7);
    expect(el.selectionEnd).toBe(7);
    el.remove();
  });
});
