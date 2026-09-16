// [S-25]/[S-26] Top-process row display helpers.
import { describe, it, expect } from 'vitest';
import { displayProcName, clampProcCount, TOP_PROCS_DEFAULT, TOP_PROCS_MAX } from './procsDisplay.ts';

describe('displayProcName [S-25]', () => {
  it('prefers the resolved friendly description over the image name', () => {
    expect(displayProcName({ name: 'chrome.exe', desc: 'Google Chrome' })).toBe('Google Chrome');
  });

  it('falls back to the image name when desc is absent, null, or blank', () => {
    expect(displayProcName({ name: 'chrome.exe' })).toBe('chrome.exe');
    expect(displayProcName({ name: 'chrome.exe', desc: null })).toBe('chrome.exe');
    expect(displayProcName({ name: 'chrome.exe', desc: '' })).toBe('chrome.exe');
    expect(displayProcName({ name: 'chrome.exe', desc: '   ' })).toBe('chrome.exe');
  });

  it('trims whitespace around the description', () => {
    expect(displayProcName({ name: 'x.exe', desc: '  Some App  ' })).toBe('Some App');
  });
});

describe('clampProcCount [S-26]', () => {
  it('defaults to 3 when unset', () => {
    expect(TOP_PROCS_DEFAULT).toBe(3);
    expect(clampProcCount(undefined)).toBe(3);
    expect(clampProcCount(null)).toBe(3);
  });

  it('passes through integers in range', () => {
    expect(clampProcCount(1)).toBe(1);
    expect(clampProcCount(5)).toBe(5);
    expect(clampProcCount(10)).toBe(10);
  });

  it('clamps to 1..TOP_PROCS_MAX', () => {
    expect(TOP_PROCS_MAX).toBe(10);
    expect(clampProcCount(0)).toBe(1);
    expect(clampProcCount(-4)).toBe(1);
    expect(clampProcCount(99)).toBe(10);
  });

  it('rejects non-numeric / non-finite junk with the default', () => {
    expect(clampProcCount('7')).toBe(3);
    expect(clampProcCount(NaN)).toBe(3);
    expect(clampProcCount(Infinity)).toBe(3);
    expect(clampProcCount({})).toBe(3);
  });

  it('rounds fractional persisted values', () => {
    expect(clampProcCount(4.6)).toBe(5);
  });
});
