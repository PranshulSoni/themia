// @vitest-environment node
//
// issue #64 (font size): the global "Font size" control multiplies --font-scale,
// which applyGlobalDesign sets on :root of EVERY window. A font-size only
// responds to it when it is expressed in terms of that scale. Widgets (and the
// on-desktop chrome) historically hardcoded absolute px, so the control did
// nothing for them — the user-reported bug. This guard pins the fix: every
// font-size in widget DISPLAY css + the on-desktop chrome files must be scaled;
// a bare `font-size: <N>px` there is a regression (that text won't grow/shrink
// with Font size).
//
// Settings / config surfaces are DELIBERATELY out of scope — they zoom via UI
// Scale instead — so this scan excludes *Config.css panels and the config
// windows. App.css is mixed (chrome + config in one file), so it is scaled
// section-by-section and verified visually rather than pinned here.

import { describe, it, expect } from 'vitest';
import { readdirSync, statSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { readSource } from '../tests/cssRules.ts';

function walkCss(dir: string, out: string[] = []): string[] {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (statSync(p).isDirectory()) walkCss(p, out);
    else if (name.endsWith('.css')) out.push(p);
  }
  return out;
}

const ROOT = process.cwd();
const rel = (p: string) => p.slice(ROOT.length + 1).replace(/\\/g, '/');

// Widget DISPLAY css (the on-desktop widgets), minus their *Config.css panels
// (config surfaces, out of scope), plus the widget chrome files.
const SCALED_FILES = [
  ...walkCss(resolve(ROOT, 'widgets')).map(rel).filter(p => !p.endsWith('Config.css')),
  'src/WidgetGrid.css',
  'src/WidgetWindow.css',
  'src/components/WidgetHeader.css',
];

// A bare pixel font-size. `font-size: calc(12px * var(--font-scale,1))` does NOT
// match (a 'c' follows the colon, not a digit); `inherit` / `var(...)` / rem/em
// don't either — only an un-scaled absolute px does.
const BARE_PX = /font-size:\s*\d+(?:\.\d+)?px/gi;

describe('issue_64 Font size scales widget + chrome text', () => {
  it('found the widget/chrome CSS files to scan', () => {
    expect(SCALED_FILES.length).toBeGreaterThan(40);
  });

  for (const file of SCALED_FILES) {
    it(`${file}: every font-size scales with --font-scale (no bare px)`, () => {
      const css = readSource(file).replace(/\/\*[\s\S]*?\*\//g, ''); // ignore comments
      expect(css.match(BARE_PX) ?? []).toEqual([]);
    });
  }
});
