// The group's area surface ([S-13]): mode fallback + the CSS string. Pure, so
// a junk config can be proven to degrade to the theme wash rather than reaching
// the DOM as `background: undefined`.
import { describe, it, expect } from 'vitest';
import { groupAreaBackground, groupAreaPaints, groupBgMode, GROUP_BG_DEFAULTS } from './groupStyle.ts';

describe('group area background ([S-13])', () => {
  it('S-13_default (and junk) keeps the theme wash: no inline background at all', () => {
    for (const v of [undefined, '', 'default', 'plaid', 7, null]) {
      expect(groupBgMode(v)).toBe('default');
      expect(groupAreaBackground({ bgMode: v })).toBeNull();
    }
  });

  it('S-13_a config left over from the dropped GRADIENT mode falls back to the theme wash', () => {
    // The mode was removed, so old data must resolve like any other unknown
    // value — never render half a gradient.
    expect(groupBgMode('gradient')).toBe('default');
    expect(groupAreaBackground({ bgMode: 'gradient', bgColor: '#ff0080' })).toBeNull();
  });

  it('S-13_a config left over from the dropped NONE mode falls back to the theme wash', () => {
    // Clearing the area is the transparent switch now ([S-32]); an old `none`
    // must resolve like any other unknown value, not paint `transparent` from
    // a mode the dropdown no longer offers.
    expect(groupBgMode('none')).toBe('default');
    expect(groupAreaBackground({ bgMode: 'none' })).toBeNull();
  });

  it('S-13_solid uses the configured colour + alpha', () => {
    expect(groupAreaBackground({ bgMode: 'solid', bgColor: '#ff0080', bgAlpha: 0.5 }))
      .toBe('rgba(255,0,128,0.5)');
  });

  it('S-13_a malformed colour / alpha falls back per field instead of emitting garbage', () => {
    expect(groupAreaBackground({ bgMode: 'solid', bgColor: 'red', bgAlpha: 5 }))
      .toBe(`rgba(96,205,255,${GROUP_BG_DEFAULTS.alpha})`);
  });
});

// The hit-mask follows PAINT (src/SPEC [S-17]): a group that shows the desktop
// through its area must not cover that desktop with an invisible window.
describe('does the area paint? ([S-13a])', () => {
  it('S-13a_the theme wash paints; the transparent switch clears it', () => {
    expect(groupAreaPaints({})).toBe(true);
    expect(groupAreaPaints({ bgMode: 'default' })).toBe(true);
    expect(groupAreaPaints({ transparentBg: true })).toBe(false);
    // Only the boolean true counts — a junk value is not a transparent group.
    expect(groupAreaPaints({ transparentBg: 'yes' })).toBe(true);
  });

  it('S-13a_a solid fill paints, and keeps painting under the transparent switch', () => {
    // An explicitly picked colour is set INLINE and outranks the switch's
    // stylesheet rule, so the mask has to agree with what is on screen.
    expect(groupAreaPaints({ bgMode: 'solid' })).toBe(true);
    expect(groupAreaPaints({ bgMode: 'solid', bgAlpha: 0.4, transparentBg: true })).toBe(true);
  });

  it('S-13a_solid at alpha 0 is see-through, whichever mode the user picked', () => {
    expect(groupAreaPaints({ bgMode: 'solid', bgAlpha: 0 })).toBe(false);
    // Out of range is not a choice: it falls back to the default alpha.
    expect(groupAreaPaints({ bgMode: 'solid', bgAlpha: -1 })).toBe(true);
  });

  it('S-13a_a dropped `none` config paints, since it resolves to the theme wash', () => {
    expect(groupAreaPaints({ bgMode: 'none' })).toBe(true);
  });
});
