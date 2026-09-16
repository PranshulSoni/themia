// @vitest-environment jsdom
//
// Design presets ([S-12]..[S-15]): a named bundle of appearance fields the user
// can apply in one click, with the picker reading "Custom" whenever the live
// design doesn't match any preset exactly.
//
// The two properties that matter and are easy to get wrong:
//   - applying a preset then matching it must round-trip (else the picker says
//     "Custom" immediately after you pick a preset, which looks broken);
//   - a preset must not touch a field it doesn't own (uiScale / fontScale /
//     animations), because clobbering someone's 150% zoom or their reduced
//     motion choice to apply a colour scheme is hostile.

import { describe, it, expect, beforeEach } from 'vitest';
import {
  DESIGN_PRESETS, PRESET_FIELDS, applyDesignPreset, matchDesignPreset, presetPreview,
  type PresetId,
} from './designPresets.ts';
import { globalDesign, setGlobalDesign, globalDesignSchema, FONT_STACKS, type GlobalDesign } from './design.ts';

const ids = () => DESIGN_PRESETS.map(p => p.id);
const byId = (id: PresetId) => DESIGN_PRESETS.find(p => p.id === id)!;
const snapshot = (): GlobalDesign => JSON.parse(JSON.stringify(globalDesign));

beforeEach(() => {
  localStorage.clear();
});

describe('[S-12] a preset owns exactly the appearance fields', () => {
  it('[S-12] covers the 13 agreed fields, no more', () => {
    expect([...PRESET_FIELDS].sort()).toEqual([
      'cornerRadius', 'fontFamily', 'headerBgColor', 'headerBgOpacity',
      'primaryColor', 'theme', 'widgetBgColor', 'widgetBgOpacity',
      'widgetBorderColor', 'widgetBorderOpacity', 'widgetBorderStyle',
      'widgetBorderWidth', 'widgetGap',
    ]);
  });

  it('[S-12] never owns uiScale, fontScale, or any animation field', () => {
    // Accessibility + motion preferences are the user's, not a theme's.
    const forbidden = ['uiScale', 'fontScale', 'widgetStartupAnimation',
      'widgetTransitionAnimation', 'widgetUnlockAnimation', 'widgetStartupSound',
      'widgetStartupAnimationMs', 'widgetTransitionAnimationMs', 'widgetUnlockAnimationMs'];
    for (const f of forbidden) expect(PRESET_FIELDS).not.toContain(f);
  });

  it('[S-12] every preset declares every owned field (no partial bundles)', () => {
    for (const p of DESIGN_PRESETS) {
      for (const f of PRESET_FIELDS) {
        expect(Object.keys(p.values), `${p.id} missing ${f}`).toContain(f);
      }
      expect(Object.keys(p.values).sort()).toEqual([...PRESET_FIELDS].sort());
    }
  });
});

describe('the default preset is borderless and equals the shipped default', () => {
  it('default paints NO widget border (width 0)', () => {
    // The picker tile (and the shipped look) must not draw a border around
    // widgets. Width 0 paints nothing regardless of style.
    expect(byId('default').values.widgetBorderWidth).toBe(0);
  });

  it('default matches DEFAULT_GLOBAL exactly, so a fresh install reads as Default', () => {
    // globalDesign === DEFAULT_GLOBAL on a fresh install; if the default preset's
    // owned fields drift from it, matchDesignPreset returns Custom on first run.
    const shipped = globalDesignSchema.defaults();
    for (const f of PRESET_FIELDS) {
      expect(byId('default').values[f], `default preset .${f} vs shipped default`)
        .toEqual(shipped[f]);
    }
  });
});

describe('[S-13] applying a preset overrides the current settings', () => {
  it.each(ids())('[S-13] applying %s sets every owned field to the preset value', (id) => {
    const preset = DESIGN_PRESETS.find(p => p.id === id)!;
    applyDesignPreset(id);
    for (const f of PRESET_FIELDS) {
      expect(globalDesign[f], `${id}.${f}`).toEqual(preset.values[f]);
    }
  });

  it('[S-13] applying a preset leaves the fields it does not own untouched', () => {
    setGlobalDesign('uiScale', 1.5);
    setGlobalDesign('fontScale', 1.3);
    setGlobalDesign('widgetStartupAnimation', 'none');
    setGlobalDesign('language', 'de');
    setGlobalDesign('devMode', true);

    applyDesignPreset('glass');

    // A user at 150% zoom with motion off keeps both after picking a theme.
    expect(globalDesign.uiScale).toBe(1.5);
    expect(globalDesign.fontScale).toBe(1.3);
    expect(globalDesign.widgetStartupAnimation).toBe('none');
    expect(globalDesign.language).toBe('de');
    expect(globalDesign.devMode).toBe(true);
  });

  it('[S-13] the applied preset is persisted, not just held in memory', () => {
    applyDesignPreset('daylight');
    const raw = localStorage.getItem('widget-global-design');
    expect(raw).not.toBeNull();
    const stored = JSON.parse(raw!);
    const daylight = DESIGN_PRESETS.find(p => p.id === 'daylight')!;
    for (const f of PRESET_FIELDS) expect(stored[f], `stored ${f}`).toEqual(daylight.values[f]);
  });
});

describe('[S-14] the picker reports the matching preset, else Custom', () => {
  it.each(ids())('[S-14] apply(%s) then match() round-trips to the same id', (id) => {
    applyDesignPreset(id);
    // If this fails the picker would read "Custom" the instant you pick a
    // preset -- the single most obvious way this feature can look broken.
    expect(matchDesignPreset(globalDesign)).toBe(id);
  });

  it('[S-14] changing ONE owned field drops the match to Custom', () => {
    applyDesignPreset('midnight');
    expect(matchDesignPreset(globalDesign)).toBe('midnight');
    setGlobalDesign('cornerRadius', 21);
    expect(matchDesignPreset(globalDesign)).toBeNull(); // null === Custom
  });

  it('[S-14] changing a NON-owned field keeps the match', () => {
    applyDesignPreset('midnight');
    setGlobalDesign('uiScale', 1.5);
    setGlobalDesign('widgetStartupAnimation', 'flip');
    // Zoom and motion are not part of the look the preset describes.
    expect(matchDesignPreset(globalDesign)).toBe('midnight');
  });

  it('[S-14] every preset is distinguishable from every other', () => {
    // Two presets with identical values would make the picker ambiguous: the
    // first would always win and the second could never be reported.
    const seen = new Map<string, PresetId>();
    for (const p of DESIGN_PRESETS) {
      const key = JSON.stringify(PRESET_FIELDS.map(f => p.values[f]));
      expect(seen.has(key), `${p.id} has identical values to ${seen.get(key)}`).toBe(false);
      seen.set(key, p.id);
    }
  });
});

describe('[S-14b] presetPreview derives a thumbnail that mirrors the preset look', () => {
  // The grid card previews a preset by rendering a mock widget from these
  // derived values. They must track the same defaulting applyGlobalDesign() uses
  // (theme default when a colour is '', rgba composited at the preset's opacity,
  // font family -> stack), or the thumbnail lies about what selecting it does.

  it('[S-14b] every preset yields a preview with no NaN/undefined channel', () => {
    for (const p of DESIGN_PRESETS) {
      const v = presetPreview(p.values);
      for (const rgba of [v.cardBg, v.headerBg, v.borderColor]) {
        expect(rgba, `${p.id} rgba`).toMatch(/^rgba\(\d+,\d+,\d+,[\d.]+\)$/);
      }
      expect(v.primary, `${p.id} primary`).toMatch(/^#[0-9a-fA-F]{6}$/);
      expect(Number.isFinite(v.radius), `${p.id} radius`).toBe(true);
      expect(Number.isFinite(v.borderWidth), `${p.id} borderWidth`).toBe(true);
    }
  });

  it('[S-14b] an empty colour falls back to the theme default, not black', () => {
    // `default` pins every colour to '' (theme-adaptive). Dark card default is
    // #1e1e1e; a black fallback would mean the '' case was mishandled.
    const v = presetPreview(byId('default').values);
    expect(v.isDark).toBe(true);
    expect(v.cardBg).toBe('rgba(30,30,30,0.9)');
    expect(v.primary).toBe('#60cdff');
  });

  it('[S-14b] a light preset resolves light text + backdrop', () => {
    const v = presetPreview(byId('daylight').values);
    expect(v.isDark).toBe(false);
    expect(v.foreground).toBe('rgba(0,0,0,0.87)');
  });

  it('[S-14b] explicit colours and opacity composite exactly', () => {
    // terminal: near-opaque phosphor-green, square, monospace.
    const v = presetPreview(byId('terminal').values);
    expect(v.cardBg).toBe('rgba(5,13,7,0.97)');
    expect(v.radius).toBe(0);
    expect(v.fontStack).toBe(FONT_STACKS.mono);
  });

  it('[S-14b] a borderless preset reports width 0 / style none', () => {
    const v = presetPreview(byId('glass').values);
    expect(v.borderStyle).toBe('none');
    expect(v.borderWidth).toBe(0);
  });
});

describe('[S-15] presets are a closed, validated set', () => {
  it('[S-15] applying an unknown id is a no-op, not a half-applied design', () => {
    applyDesignPreset('midnight');
    const before = snapshot();
    // @ts-expect-error deliberately outside the union (a stale persisted id, or
    // a value crossing the IPC/storage trust boundary).
    applyDesignPreset('no-such-preset');
    expect(snapshot()).toEqual(before);
  });

  it('[S-15] matching tolerates a design carrying junk in an owned field', () => {
    applyDesignPreset('midnight');
    // A corrupt persisted value must read as Custom, never crash the picker.
    const corrupt = { ...snapshot(), cornerRadius: 'oops' } as unknown as GlobalDesign;
    expect(matchDesignPreset(corrupt)).toBeNull();
  });

  it('[S-15] ids are stable, kebab-case, and unique', () => {
    const seen = new Set<string>();
    for (const p of DESIGN_PRESETS) {
      expect(p.id).toMatch(/^[a-z][a-z0-9-]*$/);
      expect(seen.has(p.id)).toBe(false);
      seen.add(p.id);
    }
    expect(DESIGN_PRESETS.length).toBeGreaterThanOrEqual(4);
  });
});
