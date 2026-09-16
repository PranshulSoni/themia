// DEV-only. Builds the machine-readable widget manifest that the themia-website
// documentation consumes.
//
// Why this lives in the app rather than being parsed out of the source by the
// website: the registry is the only place that knows the real shipping widget
// set, and `src/locales/*` already carries every widget's label and description
// translated into all nine app locales. Re-deriving either on the website side
// is how the site's widget list silently drifted to 22 entries while the app
// shipped 49.
//
// Read by the docs screenshot exporter (`npm run shots`) off the `?pickeronly`
// harness, which is also DEV-only. Never reaches a production bundle.

import registry from '../widgets/registry.tsx';
import { spanFromSize, previewSpanFor } from '../demo/previewAspects.ts';

import { en } from '../locales/en.ts';
import { de } from '../locales/de.ts';
import { es } from '../locales/es.ts';
import { fr } from '../locales/fr.ts';
import { pt } from '../locales/pt.ts';
import { it } from '../locales/it.ts';
import { pl } from '../locales/pl.ts';
import { ja } from '../locales/ja.ts';
import { zh } from '../locales/zh.ts';

/** App locales, in the order the website lists them. `ru` and `hi` ship on the
 *  website but not in the app, so those two translations are authored there. */
const LOCALES = { en, de, es, fr, pt, it, pl, ja, zh } as const;
export type ManifestLocale = keyof typeof LOCALES;

export interface WidgetText {
  label: string;
  description: string;
}

export interface DocsWidgetEntry {
  /** Registry key, e.g. `weatherCurrent`. Stable; the docs URL slug derives from it. */
  id: string;
  /** Picker category bucket (`Office`, `Online`, `System`, `Layout`, `Others`, …). */
  category: string;
  /** Backing services, when the widget has any (`Microsoft 365`, `GitHub`, …). */
  providers: string[];
  /** Default placement size in pixels (registry `defaultSize`). */
  defaultSize: { width: number; height: number };
  /** Art-directed preview aspect used by the picker, in grid cells. The docs
   *  image box uses the same ratio so screenshots never letterbox. */
  previewSpan: { cols: number; rows: number };
  /** `defaultConfig` reduced to JSON-safe values: the option table's key list
   *  and shipped defaults. Options with no default are still listed. */
  defaultConfig: Record<string, unknown>;
  /** False when the widget has no user-configurable options at all (the app
   *  renders its "no options" placeholder instead of a config panel). */
  hasOptions: boolean;
  /** Offers the per-instance transparent-background toggle. */
  canTransparent: boolean;
  /** Offers "Open in window" (pops out into its own OS window). */
  popOut: boolean;
  /** Hidden from the picker unless developer mode is on — excluded from docs. */
  devOnly: boolean;
  /** Per-locale label + description, straight from the app's own translations
   *  so docs wording matches what the user sees in the picker. */
  text: Record<ManifestLocale, WidgetText>;
  /**
   * The widget's icon as SVG markup. Filled in by the exporter, not here: the
   * registry declares icons as Solid components, so the markup only exists once
   * the picker has rendered them. See tests/shots/widget-shots.spec.ts.
   */
  iconSvg?: string;
}

export interface DocsManifest {
  /** Locales the `text` maps are populated for. */
  locales: ManifestLocale[];
  /** Category key → translated heading per locale, from the app's own
   *  `category.<Name>` keys, so the website groups widgets under the same
   *  words the Add-Widget picker uses. */
  categories: Record<string, Record<ManifestLocale, string>>;
  /** Picker-visible widgets, sorted by id for a stable diff. */
  widgets: DocsWidgetEntry[];
}

/** Drop values that would not survive `JSON.stringify` (functions, undefined,
 *  symbols), so the emitted manifest is faithful to what the website reads. */
function jsonSafe(config: Record<string, unknown>): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(config)) {
    if (typeof v === 'function' || typeof v === 'symbol' || v === undefined) continue;
    out[k] = v;
  }
  return out;
}

/** `widget.<id>.{label,description}` out of a locale module, or null when that
 *  locale has no entry for the widget (a widget added since the last
 *  translation pass). Callers fall back to English. */
function textFor(dict: unknown, id: string): WidgetText | null {
  const widgetSection = (dict as Record<string, unknown>)?.widget as
    | Record<string, { label?: string; description?: string }>
    | undefined;
  const entry = widgetSection?.[id];
  if (!entry?.label) return null;
  return { label: entry.label, description: entry.description ?? '' };
}

export function buildDocsManifest(): DocsManifest {
  const widgets: DocsWidgetEntry[] = [];

  for (const [id, def] of Object.entries(registry)) {
    // `devOnly` widgets are not discoverable in a shipping build, so they get
    // no doc page. Recorded on the entry rather than dropped, so the website's
    // drift test can tell "intentionally undocumented" from "missing".
    const defaultConfig = jsonSafe(def.defaultConfig ?? {});
    const enText = textFor(en, id) ?? { label: def.label, description: def.description ?? '' };

    const text = {} as Record<ManifestLocale, WidgetText>;
    for (const loc of Object.keys(LOCALES) as ManifestLocale[]) {
      text[loc] = textFor(LOCALES[loc], id) ?? enText;
    }

    widgets.push({
      id,
      category: def.category ?? 'Other',
      providers: def.providers ?? [],
      defaultSize: def.defaultSize,
      previewSpan: previewSpanFor(id, spanFromSize(def.defaultSize)),
      defaultConfig,
      hasOptions: !!def.configComponent,
      canTransparent: !!def.canTransparent,
      popOut: !!def.popOut,
      devOnly: !!def.devOnly,
      text,
    });
  }

  // Category headings, for every category the shipping widgets actually use.
  const categories: Record<string, Record<ManifestLocale, string>> = {};
  for (const w of widgets) {
    if (categories[w.category]) continue;
    const perLocale = {} as Record<ManifestLocale, string>;
    for (const loc of Object.keys(LOCALES) as ManifestLocale[]) {
      const section = (LOCALES[loc] as Record<string, unknown>).category as
        | Record<string, string>
        | undefined;
      const enSection = (en as Record<string, unknown>).category as Record<string, string> | undefined;
      perLocale[loc] = section?.[w.category] ?? enSection?.[w.category] ?? w.category;
    }
    categories[w.category] = perLocale;
  }

  widgets.sort((a, b) => a.id.localeCompare(b.id));
  return { locales: Object.keys(LOCALES) as ManifestLocale[], categories, widgets };
}

declare global {
  interface Window {
    /** DEV-only hook the docs screenshot exporter reads. */
    __themiaDocsManifest?: () => DocsManifest;
  }
}

/** Attach the builder to `window` for the exporter. Called from the
 *  `?pickeronly` DEV harness in main.tsx. */
export function exposeDocsManifest(): void {
  window.__themiaDocsManifest = buildDocsManifest;
}
