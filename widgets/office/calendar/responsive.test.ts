import { describe, it, expect } from 'vitest';
import { monthChipCapacity, eventDotColor } from './responsive.ts';

// [S-04] Month view chip count is adaptive: taller cells (bigger widget) show
// more chips; small cells fold to one chip + "+N". Clamped to 1..6.
describe('monthChipCapacity [S-04]', () => {
  it('S-04_ returns a safe default for zero/invalid sizes', () => {
    expect(monthChipCapacity(0)).toBe(2);
    expect(monthChipCapacity(-50)).toBe(2);
    expect(monthChipCapacity(NaN)).toBe(2);
  });

  it('S-04_ folds to a single chip in a very short cell', () => {
    expect(monthChipCapacity(28)).toBe(1);
  });

  it('S-04_ shows ~2 chips at a typical default cell height', () => {
    // 300px body / 6 rows ≈ 50px cell.
    expect(monthChipCapacity(50)).toBe(2);
  });

  it('S-04_ grows the chip count as the cell gets taller', () => {
    expect(monthChipCapacity(66)).toBe(3);
    expect(monthChipCapacity(100)).toBe(6);
  });

  it('S-04_ never exceeds 6 no matter how tall', () => {
    expect(monthChipCapacity(2000)).toBe(6);
  });
});

// [S-43] Compact month view: when the widget is too short for chips, each day
// shows colored dots instead. Birthdays use the warning hue; multi-account
// events keep their per-account palette color [S-08]; single-account events use
// the accent.
describe('eventDotColor [S-43]', () => {
  it('S-43_ birthdays are always the warning hue', () => {
    expect(eventDotColor('bd', undefined, false)).toBe('var(--warning)');
    expect(eventDotColor('bd', '#ff9660', true)).toBe('var(--warning)');
  });

  it('S-43_ single-account events use the accent, ignoring any color', () => {
    expect(eventDotColor('ev', undefined, false)).toBe('var(--primary)');
    expect(eventDotColor('ev', '#ff9660', false)).toBe('var(--primary)');
  });

  it('S-43_ multi-account events keep their per-account palette color', () => {
    expect(eventDotColor('ev', '#ff9660', true)).toBe('#ff9660');
  });

  it('S-43_ multi-account event with no color falls back to the accent', () => {
    expect(eventDotColor('ev', undefined, true)).toBe('var(--primary)');
  });
});
