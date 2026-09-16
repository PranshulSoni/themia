import { describe, it, expect } from 'vitest';
import {
  clampTitleLines,
  TITLE_LINES_MIN,
  TITLE_LINES_MAX,
  TITLE_LINES_DEFAULT,
} from './titleLines.ts';

// [S-49] `titleLines` is an untrusted config value; a bad one must degrade to
// the default, never to a zero-height or unbounded row.

describe('clampTitleLines [S-49]', () => {
  it('passes an in-range line count through', () => {
    expect(clampTitleLines(1)).to.equal(1);
    expect(clampTitleLines(3)).to.equal(3);
    expect(clampTitleLines(TITLE_LINES_MAX)).to.equal(TITLE_LINES_MAX);
  });

  it('falls back to the default when absent or not a number', () => {
    expect(clampTitleLines(undefined)).to.equal(TITLE_LINES_DEFAULT);
    expect(clampTitleLines(null)).to.equal(TITLE_LINES_DEFAULT);
    expect(clampTitleLines('3')).to.equal(TITLE_LINES_DEFAULT);
    expect(clampTitleLines({})).to.equal(TITLE_LINES_DEFAULT);
    expect(clampTitleLines(NaN)).to.equal(TITLE_LINES_DEFAULT);
    expect(clampTitleLines(Infinity)).to.equal(TITLE_LINES_DEFAULT);
  });

  it('clamps out-of-range numbers instead of rejecting them', () => {
    expect(clampTitleLines(0)).to.equal(TITLE_LINES_MIN);
    expect(clampTitleLines(-5)).to.equal(TITLE_LINES_MIN);
    expect(clampTitleLines(99)).to.equal(TITLE_LINES_MAX);
  });

  it('floors a fractional value', () => {
    expect(clampTitleLines(2.9)).to.equal(2);
    expect(clampTitleLines(0.5)).to.equal(TITLE_LINES_MIN);
  });
});
