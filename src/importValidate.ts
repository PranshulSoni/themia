// Pure validation + version-migration for imported settings files. Kept free of
// Tauri/localStorage deps so it can be unit-tested; this is a trust boundary: a
// malformed or old settings file must never corrupt the live app, and the
// v1/v2 → v3 migrations must preserve the user's data.

export interface ThemiaExport {
  version: 3;
  appVersion: string;
  exportedAt: string;
  screens: { id: string; name: string }[];
  monitorScreenMap: Record<string, string>;
  monitorVisibleScreensMap?: Record<string, string[]>;
  defaultScreenForNewDisplays: string | null;
  /** All design lives here: theme, uiScale, primaryColor, widgetBgColor, etc. */
  globalDesign: Record<string, unknown>;
  layouts: Record<string, unknown>;
  /** The To Do widget's device-level local lists (todo [S-57]). The only widget
   *  data that does NOT ride along inside a layout's widget config, so it has to
   *  be carried here explicitly or an import would leave every local To Do
   *  widget pointing at a list that doesn't exist. Optional: a file written
   *  before device lists existed has none, which is not an error. */
  todoLocalLists?: unknown;
  editMode: boolean;
  autoCheckUpdates: boolean;
}

// Visual fields that may still be hiding inside a v2 export's per-screen
// `screenDesigns` blob. On v2→v3 upgrade, the first screen's values for these
// keys are folded up into `globalDesign` when it doesn't already have them.
export const V2_PER_SCREEN_VISUAL_FIELDS = [
  'primaryColor','widgetBgColor','widgetBgOpacity','cornerRadius',
  'widgetBorderStyle','widgetBorderWidth','widgetBorderColor',
];

/**
 * Validate + upgrade a parsed settings object to the current (v3) shape.
 * Throws a descriptive Error on anything it can't safely import, so the caller
 * falls back rather than writing half-built state.
 */
export function validate(input: unknown): ThemiaExport {
  if (!input || typeof input !== 'object') throw new Error('Invalid file: not a JSON object');
  // After the object guard, work against a mutable string-keyed view so the
  // in-place version migrations below stay type-checked without `any`.
  const data = input as Record<string, unknown>;
  if (data.version !== 1 && data.version !== 2 && data.version !== 3) {
    throw new Error('Invalid file: unsupported version');
  }
  if (!Array.isArray(data.screens)) throw new Error('Invalid file: missing screens');
  if (typeof data.globalDesign !== 'object') throw new Error('Invalid file: missing design');
  if (typeof data.layouts !== 'object') throw new Error('Invalid file: missing layouts');

  // v1 → v3: the legacy `globalDesign` blob was a single mixed bag of
  // theme/scale + visual fields. The new model keeps all of those on
  // `globalDesign`, so copy through as-is.
  if (data.version === 1) {
    data.globalDesign = { ...(data.globalDesign as Record<string, unknown> ?? {}) };
    data.version = 3;
  }

  // v2 → v3: visual fields used to live per-screen under `screenDesigns`. Pull
  // them up to `globalDesign` from the first entry so users don't lose their
  // accent on import. `globalDesign` wins when both define the same field.
  if (data.version === 2) {
    const screenDesigns = (data.screenDesigns ?? {}) as Record<string, Record<string, unknown>>;
    const firstKey = Object.keys(screenDesigns)[0];
    const firstScreen: Record<string, unknown> = firstKey ? (screenDesigns[firstKey] ?? {}) : {};
    const merged: Record<string, unknown> = { ...firstScreen };
    for (const k of V2_PER_SCREEN_VISUAL_FIELDS) {
      // Only fold in fields the legacy screen actually defined; let globalDesign override.
      if (firstScreen[k] === undefined) delete merged[k];
    }
    data.globalDesign = { ...merged, ...(data.globalDesign as Record<string, unknown> ?? {}) };
    delete data.screenDesigns;
    data.version = 3;
  }

  // Header surface (header*BgColor/Opacity) split off from the widget body. An
  // export made before the split won't carry the header fields; seed them from
  // the widget surface so an imported theme keeps header == body (same forward
  // migration the live store does in design.ts). Only fills when absent.
  {
    const gd = data.globalDesign as Record<string, unknown>;
    if (!('headerBgColor' in gd) && 'widgetBgColor' in gd) gd.headerBgColor = gd.widgetBgColor;
    if (!('headerBgOpacity' in gd) && 'widgetBgOpacity' in gd) gd.headerBgOpacity = gd.widgetBgOpacity;
  }

  // Validated above; the runtime shape now matches ThemiaExport. The cast goes
  // through `unknown` because the working type (a plain Record) and ThemiaExport
  // don't structurally overlap for TS, but we have checked the fields by hand.
  return data as unknown as ThemiaExport;
}
