// @vitest-environment jsdom
//
// issue #43 (themia-releases), "weird color issues with settings": a faint
// vertical band + a grey/darker patch across the detached config window.
//
// Root cause: the config-surface fill (`--config-bg`) was a TRANSLUCENT
// rgba(…,0.97) painted on ALL THREE of `html, body, #app`. Because
// `body.config-window` is `transform: scale(--ui-scale)`, the three coats
// stopped perfectly coinciding once scaled: the overlap count (2 vs 3 coats,
// each letting a sliver of desktop bleed through the translucent fill) changed
// across the window, leaving a faint vertical band.
//
// Fix (ConfigWindow.css + design.ts): ONE OPAQUE fill on `html` ALONE, with
// `body`/`#app` transparent, so one layer paints one uniform surface. This pins
// BOTH halves so neither can silently regress:
//   1. `--config-bg` resolves to a fully opaque colour (alpha === 1) in both
//      themes; a translucent fill lets the desktop bleed through.
//   2. The stylesheet paints that fill on `html` only; body/#app are
//      transparent, so the fill is never double-/triple-coated across the
//      scaled layer.

import { describe, it, expect, beforeEach } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { applyGlobalDesign, setGlobalDesign } from './design.ts';

// Alpha channel of a CSS rgb()/rgba() colour. rgb(...) with no 4th component
// is fully opaque (alpha 1); rgba(...,a) reports `a`.
function alphaOf(color: string): number {
  const m = color.match(/rgba?\(([^)]+)\)/i);
  if (!m) return NaN;
  const parts = m[1].split(',').map(s => s.trim());
  return parts.length >= 4 ? parseFloat(parts[3]) : 1;
}

const configBg = () => document.documentElement.style.getPropertyValue('--config-bg').trim();

describe('config window keeps its inline root background in sync with the theme (regression)', () => {
  // index.html paints `document.documentElement.style.background` inline on the
  // config window's FIRST frame (before design.ts runs) to avoid a flash. An
  // inline style outranks the `:root { background: var(--config-bg) }` rule, so
  // if applyGlobalDesign() doesn't KEEP it in sync, the value set at window-open
  // wins forever: open Settings while dark, then pick a LIGHT preset, and the
  // stale dark inline background stays under the now-dark text -> dark-on-dark,
  // unusable. (Dark presets looked fine: white text on the stale dark.)
  const rootBg = () => document.documentElement.style.background;

  beforeEach(() => {
    document.body.className = '';
    document.documentElement.style.background = '';
  });

  it('a light theme repaints the config root opaque white, overriding a stale dark inline bg', () => {
    document.body.classList.add('config-window');
    document.documentElement.style.background = 'rgba(26, 26, 28, 0.97)'; // stale, set at open
    setGlobalDesign('theme', 'light');
    applyGlobalDesign();
    expect(rootBg()).toBe('rgb(255, 255, 255)');
  });

  it('a dark theme repaints the config root opaque dark', () => {
    document.body.classList.add('config-window');
    setGlobalDesign('theme', 'dark');
    applyGlobalDesign();
    expect(rootBg()).toBe('rgb(26, 26, 28)');
  });

  it('the inline root bg it paints is fully opaque (never translucent)', () => {
    document.body.classList.add('config-window');
    setGlobalDesign('theme', 'light');
    applyGlobalDesign();
    expect(alphaOf(rootBg())).toBe(1);
  });

  it('a widget window is left transparent (no inline root bg painted)', () => {
    // Widget windows show the desktop through; they must never get an opaque root.
    document.body.classList.add('widget-window');
    setGlobalDesign('theme', 'light');
    applyGlobalDesign();
    expect(rootBg()).toBe('');
  });
});

describe('issue_43 config window paints a single opaque fill (no vertical band)', () => {
  it('issue_43 --config-bg is fully opaque in dark theme', () => {
    setGlobalDesign('theme', 'dark');
    applyGlobalDesign();
    const bg = configBg();
    expect(bg).not.toBe('');
    expect(alphaOf(bg)).toBe(1); // was rgba(26, 26, 28, 0.97) before the fix
  });

  it('issue_43 --config-bg is fully opaque in light theme', () => {
    setGlobalDesign('theme', 'light');
    applyGlobalDesign();
    const bg = configBg();
    expect(bg).not.toBe('');
    expect(alphaOf(bg)).toBe(1); // was rgba(255, 255, 255, 0.97) before the fix
  });

  // jsdom can't cascade the imported stylesheet, so characterise the source
  // directly (cf. ipc.test.ts pinning lib.rs command registration).
  describe('issue_43 stylesheet layering', () => {
    // Resolve from the vitest root (cwd), not import.meta.url: under the jsdom
    // environment import.meta.url is an http:// URL, which readFileSync rejects.
    const css = readFileSync(resolve(process.cwd(), 'src/ConfigWindow.css'), 'utf8');
    // Flat rule scanner (ConfigWindow.css has no nesting/@media): strip comments,
    // then split into { selector, body } pairs.
    const rules = [...css.replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/([^{}]+)\{([^}]*)\}/g)]
      .map(m => ({ selector: m[1].trim().replace(/\s+/g, ' '), body: m[2] }));

    it('issue_43 paints --config-bg on the root element ALONE, not the html,body,#app group', () => {
      const fillRules = rules.filter(r => /background:\s*var\(\s*--config-bg/.test(r.body));
      expect(fillRules.length).toBeGreaterThan(0);
      // Exactly ONE element (the root); before issue_43 the selector was
      // `html, body, #app`, which triple-coated the (translucent) fill. It is
      // `:root` rather than a bare `html` for the reason pinned below: `:root`
      // (a pseudo-class, specificity 0,1,0) out-specifies App.css's
      // `html { background: transparent }` (0,0,1), so the opaque fill wins the
      // cascade regardless of bundler emit order.
      for (const r of fillRules) expect(r.selector).toBe(':root');
    });

    it('issue_43 leaves body and #app transparent so the fill is never multi-coated', () => {
      const transparent = rules
        .filter(r => /background:\s*transparent/.test(r.body))
        .map(r => r.selector)
        .join(' | ');
      // No such rule existed before the fix.
      expect(transparent).toMatch(/body/);
      expect(transparent).toMatch(/#app/);
    });

    it('issue_43 the var() fallback is opaque rgb() not translucent rgba()', () => {
      const fill = rules.find(r => /background:\s*var\(\s*--config-bg/.test(r.body));
      const fallback = fill?.body.match(/var\(\s*--config-bg\s*,\s*(rgba?\([^)]*\))/);
      expect(fallback).not.toBeNull();
      expect(alphaOf(fallback![1])).toBe(1); // was rgba(26, 26, 28, 0.97) before the fix
    });
  });

  // Regression: applying a light preset (Daylight/Paper) left the settings
  // window dark-on-dark and unusable. The theme tokens were correct
  // (--config-bg resolved to white), but the OPAQUE FILL never painted: the
  // config fill was a bare `html { background: var(--config-bg) }`, tying
  // App.css's `html { background: transparent }` at equal specificity. The
  // winner is then decided by bundler emit order, and when App.css won the
  // config window's root painted TRANSPARENT — showing the dark webview/frame
  // through, regardless of theme. Fix: the config fill uses `:root` (0,1,0),
  // which out-specifies the bare `html` (0,0,1) transparent rule, so specificity
  // (checked before source order) settles the cascade deterministically.
  describe('config fill out-specifies App.css transparent html (regression)', () => {
    const parseRules = (css: string) =>
      [...css.replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/([^{}]+)\{([^}]*)\}/g)]
        .map(m => ({ selector: m[1].trim().replace(/\s+/g, ' '), body: m[2] }));

    // Specificity of a single simple selector, as an (id, class/pseudo-class,
    // type) tuple. Enough for the two selectors in play (`html` and `:root`).
    const specificity = (sel: string): [number, number, number] => [
      (sel.match(/#[\w-]+/g) ?? []).length,
      (sel.match(/\.[\w-]+|:[\w-]+|\[[^\]]+\]/g) ?? []).length,
      (sel.match(/(^|[\s>+~])[a-z][\w-]*/gi) ?? []).length,
    ];
    const gt = (a: [number, number, number], b: [number, number, number]) =>
      a[0] !== b[0] ? a[0] > b[0] : a[1] !== b[1] ? a[1] > b[1] : a[2] > b[2];

    const cfgCss = readFileSync(resolve(process.cwd(), 'src/ConfigWindow.css'), 'utf8');
    const appCss = readFileSync(resolve(process.cwd(), 'src/App.css'), 'utf8');

    it('the config --config-bg fill beats every transparent html rule in App.css', () => {
      const fill = parseRules(cfgCss).find(r => /background:\s*var\(\s*--config-bg/.test(r.body));
      expect(fill, 'config fill rule not found').toBeDefined();

      // Every App.css rule that paints an html-targeting selector transparent.
      const antagonists = parseRules(appCss)
        .filter(r => /background:\s*transparent/.test(r.body))
        .flatMap(r => r.selector.split(',').map(s => s.trim()))
        .filter(s => /(^|[\s>+~])html($|[\s>+~.:#[])|^html$/.test(s));
      expect(antagonists.length, 'guard: App.css transparent html rule not found').toBeGreaterThan(0);

      const fillSpec = specificity(fill!.selector);
      for (const a of antagonists) {
        expect(gt(fillSpec, specificity(a)),
          `config fill "${fill!.selector}" must out-specify App.css "${a}"`).toBe(true);
      }
    });
  });
});
