// @vitest-environment jsdom
//
// Settings.SPEC.md [S-05]/[S-06]: the user's accent must actually take effect.
//
// The bug: 18 CSS sites hardcoded the DEFAULT accent (#60cdff / rgb 96,205,255)
// or a shade hand-mixed from it (#7dd8ff, a lightened hover). Pick a red accent
// in Settings and the info-log chips stayed blue, the update button stayed blue,
// and a primary button flipped BACK to stock blue on hover. Worst of all the
// ToggleSwitch knob was #0a1a26 -- a navy chosen to contrast the default cyan --
// so a dark accent made the knob vanish into its own track, on the single
// most-used control in Settings.
//
// Two guards, because they fail differently:
//   [S-05] a source scan: the default literal may appear only in design.ts.
//   [S-06] a behavioural check: --primary-foreground really does flip to white
//          on a dark accent, which is what makes the knob visible.

import { describe, it, expect } from 'vitest';
import { readdirSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { readSource, parseRules } from '../../tests/cssRules.ts';
import { applyGlobalDesign, setGlobalDesign } from '../design.ts';

function walk(dir: string, ext: string, out: string[] = []): string[] {
  for (const e of readdirSync(dir)) {
    if (e === 'node_modules' || e === 'dist' || e === 'target') continue;
    const p = join(dir, e);
    if (statSync(p).isDirectory()) walk(p, ext, out);
    else if (p.endsWith(ext)) out.push(p);
  }
  return out;
}
const rel = (p: string) => relative(process.cwd(), p).replace(/\\/g, '/');
const cssFiles = walk(join(process.cwd(), 'src'), '.css').map(rel);

// The default accent, in every form it was found written: hex, spaced rgb
// channels, and the hand-lightened hover shade.
const ACCENT_LITERALS = [
  /#60cdff/i,                    // dark-theme default
  /#0078d4/i,                    // light-theme default
  /#7dd8ff/i,                    // hand-lightened hover of #60cdff
  /96\s*,\s*205\s*,\s*255/,      // rgb() channels of #60cdff
  /0\s*,\s*120\s*,\s*212/,       // rgb() channels of #0078d4
];

describe('Settings [S-05] no stylesheet hardcodes the default accent', () => {
  it('[S-05] the scan sees the stylesheets (guards the scanner)', () => {
    expect(cssFiles.length).toBeGreaterThanOrEqual(10);
  });

  it('[S-05] no src/**/*.css contains the default accent value', () => {
    const offenders: string[] = [];
    for (const f of cssFiles) {
      readSource(f).split('\n').forEach((line, i) => {
        if (line.trim().startsWith('*') || line.trim().startsWith('/*')) return; // prose
        for (const re of ACCENT_LITERALS) {
          if (re.test(line)) offenders.push(`${f}:${i + 1} ${line.trim()}`);
        }
      });
    }
    // Was: 18 sites across App.css, MultiSelectDropdown.css, SettingsModal.css,
    // ToggleSwitch.css.
    expect(offenders).toEqual([]);
  });

  it('[S-06] the ToggleSwitch knob is not a literal picked for the default accent', () => {
    const rules = parseRules(readSource('src/components/ToggleSwitch.css'));
    const knob = rules.find(r => /\.design-switch\.on::after/.test(r.selector));
    expect(knob, '.design-switch.on::after rule missing').toBeDefined();
    // Was `background: #0a1a26` -- invisible against a dark accent.
    expect(knob!.body).toMatch(/background:\s*var\(--primary-foreground\)/);
  });
});

describe('Settings [S-06] --primary-foreground tracks the accent luminance', () => {
  const primaryForeground = () =>
    document.documentElement.style.getPropertyValue('--primary-foreground').trim();

  it('[S-06] a LIGHT accent gets dark on-accent content', () => {
    setGlobalDesign('theme', 'dark');
    setGlobalDesign('primaryColor', '#60cdff'); // the default cyan, luminance > 0.6
    applyGlobalDesign();
    expect(primaryForeground()).toBe('rgba(0,0,0,0.88)');
  });

  it('[S-06] a DARK accent gets light on-accent content, so the knob stays visible', () => {
    setGlobalDesign('theme', 'dark');
    setGlobalDesign('primaryColor', '#1a2b4c'); // dark navy, luminance < 0.6
    applyGlobalDesign();
    // With the old literal knob (#0a1a26) this accent made the knob vanish.
    expect(primaryForeground()).toBe('#ffffff');
  });

  it('[S-06] a red accent still yields readable on-accent content', () => {
    setGlobalDesign('theme', 'dark');
    setGlobalDesign('primaryColor', '#c81e1e');
    applyGlobalDesign();
    expect(primaryForeground()).toBe('#ffffff');
    // ...and the accent itself is what the user picked, not the stock blue.
    expect(document.documentElement.style.getPropertyValue('--primary').trim()).toBe('#c81e1e');
  });
});
