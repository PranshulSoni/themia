// Design presets: a named bundle of appearance settings the user applies in one
// click. Selecting one OVERRIDES the current values for the fields it owns; the
// picker reads "Custom" whenever the live design matches no preset exactly.
//
// Scope is deliberate. A preset owns the LOOK — theme, accent, font family,
// widget/header surfaces, border, shape. It does NOT own uiScale, fontScale or
// any animation setting: those are accessibility and motion preferences the user
// set for themselves, and silently resetting someone's 150% zoom (or re-enabling
// motion they turned off) to apply a colour scheme would be hostile. That
// boundary is pinned by designPresets.test.ts [S-12]/[S-13].

import { globalDesign, setGlobalDesignFields, hexToRgba, resolveFontStack, type GlobalDesign, type WidgetBorderStyle } from './design.ts';
import { logger } from './debugLog.ts';

/** The design fields a preset controls. Everything else is the user's. */
export const PRESET_FIELDS = [
  'theme',
  'primaryColor',
  'fontFamily',
  'widgetBgColor',
  'widgetBgOpacity',
  'headerBgColor',
  'headerBgOpacity',
  'widgetBorderStyle',
  'widgetBorderWidth',
  'widgetBorderColor',
  'widgetBorderOpacity',
  'cornerRadius',
  'widgetGap',
] as const satisfies readonly (keyof GlobalDesign)[];

export type PresetField = (typeof PRESET_FIELDS)[number];

/** The subset of the design a preset describes. */
export type PresetValues = Pick<GlobalDesign, PresetField>;

/** Closed set, not a loose string: a preset id crosses the storage boundary. */
export type PresetId = 'default' | 'midnight' | 'daylight' | 'glass' | 'terminal' | 'paper';

export interface DesignPreset {
  id: PresetId;
  /** i18n key under `settings.style.preset.name.*`. Resolved at render so the
   *  label re-reads after a language change (Settings.SPEC.md [S-09]). */
  labelKey: string;
  values: PresetValues;
}

// An empty-string colour means "the theme's default", which stays adaptive
// across a light/dark switch — so `default` is expressible without pinning hex.
export const DESIGN_PRESETS: DesignPreset[] = [
  {
    // The shipped look. Present so a user who has been experimenting can get
    // back without knowing what the original 13 values were.
    id: 'default',
    labelKey: 'settings.style.preset.name.default',
    values: {
      theme: 'dark',
      primaryColor: '',
      fontFamily: 'system',
      widgetBgColor: '',
      widgetBgOpacity: 0.9,
      headerBgColor: '',
      headerBgOpacity: 0.9,
      // Borderless (width 0) — must mirror DEFAULT_GLOBAL exactly, or a fresh
      // install (globalDesign === DEFAULT_GLOBAL) would read as "Custom" instead
      // of "Default" in the picker.
      widgetBorderStyle: 'solid',
      widgetBorderWidth: 0,
      widgetBorderColor: '',
      widgetBorderOpacity: 1,
      cornerRadius: 8,
      widgetGap: 12,
    },
  },
  {
    // Near-black, tight, high contrast.
    id: 'midnight',
    labelKey: 'settings.style.preset.name.midnight',
    values: {
      theme: 'dark',
      primaryColor: '#7c8cff',
      fontFamily: 'system',
      widgetBgColor: '#0b0b0f',
      widgetBgOpacity: 0.96,
      headerBgColor: '#131320',
      headerBgOpacity: 0.96,
      widgetBorderStyle: 'solid',
      widgetBorderWidth: 1,
      widgetBorderColor: '#7c8cff',
      widgetBorderOpacity: 0.18,
      cornerRadius: 6,
      widgetGap: 10,
    },
  },
  {
    // Light theme, soft and airy.
    id: 'daylight',
    labelKey: 'settings.style.preset.name.daylight',
    values: {
      theme: 'light',
      primaryColor: '#0078d4',
      fontFamily: 'system',
      widgetBgColor: '#ffffff',
      widgetBgOpacity: 0.86,
      headerBgColor: '#f3f4f8',
      headerBgOpacity: 0.92,
      widgetBorderStyle: 'solid',
      widgetBorderWidth: 1,
      widgetBorderColor: '#000000',
      widgetBorderOpacity: 0.1,
      cornerRadius: 12,
      widgetGap: 14,
    },
  },
  {
    // Heavy glass: very translucent, borderless, generously rounded. The blur
    // tracks opacity in applyGlobalDesign, so a low alpha genuinely reads as
    // glass rather than as a flat wash.
    id: 'glass',
    labelKey: 'settings.style.preset.name.glass',
    values: {
      theme: 'dark',
      primaryColor: '#8ad8ff',
      fontFamily: 'system',
      widgetBgColor: '#1b2030',
      widgetBgOpacity: 0.55,
      headerBgColor: '#1b2030',
      headerBgOpacity: 0.35,
      widgetBorderStyle: 'none',
      widgetBorderWidth: 0,
      widgetBorderColor: '#ffffff',
      widgetBorderOpacity: 0.25,
      cornerRadius: 18,
      widgetGap: 16,
    },
  },
  {
    // Monospace, square, phosphor green.
    id: 'terminal',
    labelKey: 'settings.style.preset.name.terminal',
    values: {
      theme: 'dark',
      primaryColor: '#3ddc84',
      fontFamily: 'mono',
      widgetBgColor: '#050d07',
      widgetBgOpacity: 0.97,
      headerBgColor: '#050d07',
      headerBgOpacity: 0.97,
      widgetBorderStyle: 'solid',
      widgetBorderWidth: 1,
      widgetBorderColor: '#3ddc84',
      widgetBorderOpacity: 0.45,
      cornerRadius: 0,
      widgetGap: 8,
    },
  },
  {
    // Light, serif, printed-page feel: opaque cards, hairline rules.
    id: 'paper',
    labelKey: 'settings.style.preset.name.paper',
    values: {
      theme: 'light',
      primaryColor: '#8a5a2b',
      fontFamily: 'serif',
      widgetBgColor: '#faf7f0',
      widgetBgOpacity: 1,
      headerBgColor: '#f0e9dc',
      headerBgOpacity: 1,
      widgetBorderStyle: 'solid',
      widgetBorderWidth: 1,
      widgetBorderColor: '#8a5a2b',
      widgetBorderOpacity: 0.22,
      cornerRadius: 4,
      widgetGap: 12,
    },
  },
];

const BY_ID = new Map<PresetId, DesignPreset>(DESIGN_PRESETS.map(p => [p.id, p]));

/**
 * Apply every field a preset owns, in ONE atomic write.
 *
 * An unknown id is ignored with a loud log rather than partially applied: the
 * id can arrive from a stale persisted value or across the IPC boundary, and a
 * half-applied design is worse than no change ([S-15]).
 */
export function applyDesignPreset(id: PresetId): void {
  const preset = BY_ID.get(id);
  if (!preset) {
    logger.error('design', `applyDesignPreset: unknown preset ${JSON.stringify(id)} — ignored`);
    return;
  }
  setGlobalDesignFields({ ...preset.values });
}

/**
 * The preset the given design matches exactly, or null for "Custom".
 *
 * Compares only the owned fields, so a user who set a preset and then changed
 * their zoom or turned animations off still reads as that preset — those aren't
 * part of the look it describes ([S-14]).
 */
export function matchDesignPreset(design: Pick<GlobalDesign, PresetField>): PresetId | null {
  for (const preset of DESIGN_PRESETS) {
    if (PRESET_FIELDS.every(f => Object.is(design[f], preset.values[f]))) return preset.id;
  }
  return null;
}

/** The live design's preset, or null for Custom. Reactive: call in a memo. */
export function activeDesignPreset(): PresetId | null {
  return matchDesignPreset(globalDesign);
}

// ---------------------------------------------------------------------------
// Preview thumbnail
//
// The picker grid renders a small mock widget per preset. Its colours are
// derived HERE, not inlined in the card, so a thumbnail composites a preset's
// look with the SAME defaulting rules applyGlobalDesign() applies to the live
// design: an '' colour resolves to the theme default (never black), the fill is
// the hex composited at the preset's opacity, and the font family maps to its
// stack. Pure + deterministic (every preset pins theme to dark|light), so it is
// unit-tested rather than eyeballed — a divergence from applyGlobalDesign would
// make the card lie about what selecting it does.
// ---------------------------------------------------------------------------

export interface PresetPreview {
  isDark: boolean;
  /** Desktop tone painted behind the card so a translucent fill reads as glass
   *  rather than as a flat wash. */
  backdrop: string;
  cardBg: string;      // rgba()
  headerBg: string;    // rgba()
  borderStyle: WidgetBorderStyle;
  borderWidth: number; // px
  borderColor: string; // rgba()
  primary: string;     // hex
  radius: number;      // px
  fontStack: string;
  foreground: string;      // text on the card
  mutedForeground: string; // secondary text on the card
}

export function presetPreview(values: PresetValues): PresetPreview {
  // Presets pin theme to 'dark' | 'light'; 'system' can't be resolved for a
  // static thumbnail, so treat anything but 'light' as dark (matches the
  // shipped presets and keeps the preview deterministic).
  const isDark = values.theme !== 'light';
  const cardHex = values.widgetBgColor || (isDark ? '#1e1e1e' : '#ffffff');
  const headerHex = values.headerBgColor || (isDark ? '#1e1e1e' : '#ffffff');
  const borderHex = values.widgetBorderColor || (isDark ? '#ffffff' : '#000000');
  return {
    isDark,
    backdrop: isDark ? '#2c2c30' : '#dfe1e7',
    cardBg: hexToRgba(cardHex, values.widgetBgOpacity),
    headerBg: hexToRgba(headerHex, values.headerBgOpacity),
    borderStyle: values.widgetBorderStyle,
    borderWidth: values.widgetBorderWidth,
    borderColor: hexToRgba(borderHex, values.widgetBorderOpacity),
    primary: values.primaryColor || (isDark ? '#60cdff' : '#0078d4'),
    radius: values.cornerRadius,
    fontStack: resolveFontStack(values.fontFamily),
    foreground: isDark ? 'rgba(255,255,255,0.886)' : 'rgba(0,0,0,0.87)',
    mutedForeground: isDark ? 'rgba(255,255,255,0.544)' : 'rgba(0,0,0,0.54)',
  };
}
