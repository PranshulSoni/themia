/**
 * Documentation screenshot exporter.
 *
 * Walks the Add-Widget picker in the DEV-only `?pickeronly` harness and
 * captures each widget's live preview — the same pane the user sees when
 * choosing a widget, rendered with demo fixtures — once per theme.
 *
 * Emits into `dist-shots/`:
 *   widgets.json            manifest (registry + per-locale label/description)
 *   widgets/<id>-light.png  preview at 2×
 *   widgets/<id>-dark.png
 *
 * Consumed by themia-website: see its scripts/import-widget-shots.mjs.
 */
import { browser, $, $$ } from '@wdio/globals';
import { mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// Mirrors the DEV-only hook attached by src/dev/docsManifest.ts. Redeclared
// here because this tsconfig covers tests/shots only, not src/.
declare global {
  interface Window {
    __themiaDocsManifest?: () => unknown;
  }
}

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(__dirname, '..', '..');
const OUT_DIR = resolve(ROOT, 'dist-shots');
const SHOT_DIR = resolve(OUT_DIR, 'widgets');

const THEMES = ['light', 'dark'] as const;
type Theme = (typeof THEMES)[number];

/** Minimum time to let a mounted widget settle before we even start checking.
 *  Covers the synchronous render plus the first animation frame. */
const SETTLE_MIN_MS = 600;
/** Give up waiting for the picture to stop changing and shoot anyway. A widget
 *  that never settles (a ticking clock, a spectrum analyser) hits this. */
const SETTLE_MAX_MS = 8_000;
/** How long the rendered content must stay unchanged to count as settled. */
const SETTLE_QUIET_MS = 500;

/** Boot the picker harness with a given theme applied.
 *
 *  The theme is read from persisted `widget-global-design` at module init, so it
 *  has to be in localStorage BEFORE the app's JS runs — hence load, seed, reload.
 *  `devMode: false` matters: it keeps `devOnly` widgets out of the picker, so the
 *  export covers exactly the shipping set. */
async function bootPicker(theme: Theme): Promise<void> {
  await browser.url('/?pickeronly');
  await browser.execute((t: string) => {
    const k = 'widget-global-design';
    let cur: Record<string, unknown> = {};
    try { cur = JSON.parse(localStorage.getItem(k) ?? '{}'); } catch { /* seed fresh */ }
    localStorage.setItem(k, JSON.stringify({ ...cur, theme: t, devMode: false, language: 'en' }));
  }, theme);
  await browser.url('/?pickeronly');
  await $('button.widget-card[data-widget-type]').waitForExist({ timeout: 30_000 });
}

/** Registry keys in picker order, deduped.
 *
 *  `folder` and `shortcut` appear twice: the picker lists curated Developer
 *  Favorites in addition to their own category bucket (pickerGroups.ts). Both
 *  cards render the same widget, so the first occurrence wins. */
async function visibleWidgetTypes(): Promise<string[]> {
  const types = await browser.execute(() =>
    Array.from(document.querySelectorAll<HTMLElement>('button.widget-card[data-widget-type]'))
      .map((el) => el.dataset.widgetType ?? ''),
  );
  return [...new Set(types.filter(Boolean))];
}

/**
 * Each widget's icon as SVG markup, read straight off the rendered picker card.
 *
 * The registry declares icons as Solid components (`icon: () => <Brain/>`), so
 * there is no name to export — but the picker has already rendered them, and
 * lifting the markup gives the docs the exact same glyph the app shows rather
 * than a hand-maintained second mapping that would drift.
 */
async function widgetIcons(): Promise<Record<string, string>> {
  return browser.execute(() => {
    const out: Record<string, string> = {};
    for (const el of Array.from(document.querySelectorAll<HTMLElement>('button.widget-card[data-widget-type]'))) {
      const type = el.dataset.widgetType;
      const svg = el.querySelector('.widget-card-icon svg');
      if (type && svg && !out[type]) out[type] = svg.outerHTML;
    }
    return out;
  });
}

/** Select a widget in the picker and wait until its preview is the one mounted.
 *
 *  Selection goes through `focus()` rather than a synthetic hover: the card
 *  handles both (`onMouseEnter` / `onFocus`), and focus is deterministic in a
 *  headless browser where pointer emulation is not.
 *
 *  The settle signal is the pane's label matching the card's — both render the
 *  same translated string, so an equal label means the pane has swapped to this
 *  widget rather than still showing the previous one. */
async function selectWidget(type: string): Promise<void> {
  const expectedLabel = await browser.execute((t: string) => {
    const card = document.querySelector<HTMLElement>(`button.widget-card[data-widget-type="${t}"]`);
    if (!card) return null;
    card.scrollIntoView({ block: 'center' });
    card.focus();
    return card.querySelector('.widget-card-label')?.textContent?.trim() ?? '';
  }, type);

  if (expectedLabel === null) throw new Error(`[shots] no picker card for "${type}"`);

  await browser.waitUntil(
    async () => {
      const paneLabel = await browser.execute(() =>
        document.querySelector('.picker-preview-label')?.textContent?.trim() ?? null,
      );
      return paneLabel === expectedLabel;
    },
    { timeout: 15_000, timeoutMsg: `[shots] preview pane never showed "${type}"` },
  );

  // The ErrorBoundary fallback means the widget threw while mounting; capturing
  // an empty box would silently ship a blank image to the docs.
  const crashed = await browser.execute(() => !!document.querySelector('.widget-pane-preview-fallback'));
  if (crashed) throw new Error(`[shots] widget "${type}" crashed in preview`);

  await settlePreview();
}

/** Signature of what the preview currently shows: its text plus the number of
 *  drawn elements. A widget whose first data poll has not resolved yet has a
 *  much smaller signature than the same widget a second later. */
async function previewSignature(): Promise<string> {
  return browser.execute(() => {
    const stage = document.querySelector('.widget-pane-preview-stage');
    if (!stage) return '';
    const drawn = stage.querySelectorAll('svg, img, canvas, path, rect').length;
    return `${drawn}|${(stage as HTMLElement).innerText.replace(/\s+/g, ' ').trim()}`;
  });
}

/**
 * Wait until the preview stops changing.
 *
 * A fixed pause was wrong: every widget polls its data through the demo invoke
 * on its own `interval`, so a flat delay either shoots the RAM widget before its
 * first sample lands (blank card) or wastes seconds on the widgets that render
 * synchronously. Polling the rendered signature adapts to each widget.
 */
async function settlePreview(): Promise<void> {
  await browser.pause(SETTLE_MIN_MS);
  const deadline = Date.now() + SETTLE_MAX_MS;
  let last = await previewSignature();
  let quietSince = Date.now();

  while (Date.now() < deadline) {
    await browser.pause(150);
    const now = await previewSignature();
    if (now !== last) {
      last = now;
      quietSince = Date.now();
      continue;
    }
    if (Date.now() - quietSince >= SETTLE_QUIET_MS) return;
  }
}

describe('docs: widget screenshot export', () => {
  it('captures every picker-visible widget in both themes', async () => {
    // Start from a clean output dir so a widget removed from the registry does
    // not leave a stale PNG the website would keep publishing.
    rmSync(OUT_DIR, { recursive: true, force: true });
    mkdirSync(SHOT_DIR, { recursive: true });

    let manifest: unknown = null;
    let captured = 0;
    const report: Record<string, string> = {};

    for (const theme of THEMES) {
      await bootPicker(theme);

      if (!manifest) {
        manifest = await browser.execute(() =>
          window.__themiaDocsManifest ? window.__themiaDocsManifest() : null,
        );
        if (!manifest) throw new Error('[shots] window.__themiaDocsManifest missing (is this a DEV build?)');

        const icons = await widgetIcons();
        const entries = (manifest as { widgets: { id: string; iconSvg?: string }[] }).widgets;
        for (const w of entries) w.iconSvg = icons[w.id] ?? '';
        const missingIcons = entries.filter((w) => !w.iconSvg).map((w) => w.id);
        if (missingIcons.length) console.log(`[shots] no icon for: ${missingIcons.join(', ')}`);
      }

      const types = await visibleWidgetTypes();
      if (types.length === 0) throw new Error('[shots] picker rendered no widget cards');
      console.log(`[shots] ${theme}: ${types.length} widgets`);

      for (const type of types) {
        await selectWidget(type);
        const box = await $('.widget-pane-preview');
        await box.saveScreenshot(resolve(SHOT_DIR, `${type}-${theme}.png`));
        captured++;
        if (theme === THEMES[0]) report[type] = await previewSignature();
      }
    }

    writeFileSync(resolve(OUT_DIR, 'widgets.json'), JSON.stringify(manifest, null, 2) + '\n', 'utf8');

    // What each preview actually rendered. Auditing 100 PNGs by eye does not
    // scale and is how the blank RAM/Disk cards shipped in the first place;
    // this makes "empty" and "needs configuration" greppable.
    writeFileSync(resolve(OUT_DIR, 'preview-report.json'), JSON.stringify(report, null, 2) + '\n', 'utf8');

    const thin = Object.entries(report).filter(([, sig]) => (sig.split('|')[1] ?? '').length < 12);
    if (thin.length) {
      console.log(`[shots] ${thin.length} preview(s) rendered almost no text:`);
      for (const [type, sig] of thin) console.log(`  ${type}: ${JSON.stringify(sig)}`);
    }
    console.log(`[shots] wrote ${captured} screenshots + widgets.json to ${OUT_DIR}`);
  });
});
