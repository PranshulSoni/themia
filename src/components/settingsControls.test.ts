// Settings.SPEC.md [S-01]/[S-02]: every control that can render `disabled` must
// LOOK disabled.
//
// The bug this pins: `.design-toggle` (App.css) defined base / `.on` / `.danger`
// but no `:disabled`, while four live call sites passed `disabled` — license
// activate + deactivate, check-for-updates, and the newsletter subscribe button.
// A disabled Activate rendered pixel-identical to an enabled one: same opacity,
// same pointer cursor, no cue that the click was inert. SettingsModal.tsx even
// carried a `classList={{ disabled: … }}` workaround for it, but `.disabled` had
// no rule either, so the workaround was dead too.
//
// It's the [S-01] scan (not the single-class assertion) that has teeth: it walks
// every <button> in src/ and fails for ANY class that can render disabled
// without a disabled rule, so the next unstyled one can't ship either.

import { describe, it, expect } from 'vitest';
import { readdirSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { readSource, parseRules, disabledRulesFor, findButtons, primaryClass, type CssRule } from '../../tests/cssRules.ts';

function walk(dir: string, ext: string, out: string[] = []): string[] {
  for (const e of readdirSync(dir)) {
    if (e === 'node_modules' || e === 'dist' || e === 'target') continue;
    const p = join(dir, e);
    if (statSync(p).isDirectory()) walk(p, ext, out);
    else if (p.endsWith(ext)) out.push(p);
  }
  return out;
}

const SRC = join(process.cwd(), 'src');
const rel = (p: string) => relative(process.cwd(), p).replace(/\\/g, '/');

// Every rule in every stylesheet the settings surfaces load, flattened. A
// button's disabled rule may live in a different file from its call site
// (e.g. `.save-btn` is styled in config-base.css, used all over), so the
// disabled-state lookup is repo-wide rather than per-file.
const allCssRules: CssRule[] = walk(SRC, '.css')
  .flatMap(f => parseRules(readSource(rel(f))));

// Classes that are structural containers or non-interactive, so a `disabled`
// attribute on the same tag belongs to a child/sibling rather than this class.
const NOT_A_CONTROL = new Set(['kbd-focusable', 'on', 'danger', 'active', 'primary']);

describe('Settings [S-01] every disable-able control has a disabled style', () => {
  const buttons = walk(SRC, '.tsx')
    .flatMap(f => findButtons(readSource(rel(f)), rel(f)))
    .filter(b => b.hasDisabled && b.classes.length > 0);

  it('[S-01] the scan finds the known disable-able buttons (guards the scanner)', () => {
    // If this drops to ~0 the scanner silently broke and every assertion below
    // would vacuously pass.
    expect(buttons.length).toBeGreaterThanOrEqual(8);
  });

  it('[S-01] no button can render disabled without a disabled rule for its class', () => {
    const offenders: string[] = [];
    for (const b of buttons) {
      const styleable = b.classes.filter(c => !NOT_A_CONTROL.has(c));
      if (styleable.length === 0) continue;
      // At least ONE class on the tag must carry the disabled treatment.
      const covered = styleable.some(c => disabledRulesFor(allCssRules, c).length > 0);
      if (!covered) offenders.push(`${b.file}:${b.line} class="${b.classes.join(' ')}"`);
    }
    // Was: 4 offenders, all `.design-toggle` (license activate/deactivate,
    // check-updates, newsletter subscribe).
    expect(offenders).toEqual([]);
  });

  // `.design-toggle` was the class this suite was written for. [S-11] retired it
  // into the role family; `.btn-secondary` is what its 21 bare sites became, so
  // the assertion follows the button rather than the name.
  it('[S-01] .btn-secondary specifically has a disabled rule', () => {
    expect(disabledRulesFor(allCssRules, 'btn-secondary').length).toBeGreaterThan(0);
  });

  it('[S-01] the disabled rule declares both opacity and cursor', () => {
    const rules = disabledRulesFor(allCssRules, 'btn-secondary');
    const body = rules.map(r => r.body).join(';');
    expect(body).toMatch(/opacity\s*:/);
    expect(body).toMatch(/cursor\s*:/);
  });
});

describe('Settings [S-02] the disabled treatment is uniform', () => {
  // Documented exceptions (see Settings.SPEC.md [S-02]): opacity is load-bearing
  // rather than decorative for these two.
  const EXCEPTIONS = new Set(['msd-row-action-btn', 'ns-btn']);

  const CONTROLS = [
    'btn-primary', 'btn-secondary', 'view-name-edit-input',
    // Was `.add-provider-btn`, the inline provider grid on the Connections
    // page. That grid is gone ([S-40 email]) — the catalog now lives only in
    // the Connect window, so its picker button carries the same duty here.
    'connect-provider-btn', 'design-switch', 'design-btn',
  ];

  it.each(CONTROLS)('[S-02] .%s disables at opacity 0.5 / not-allowed', (cls) => {
    if (EXCEPTIONS.has(cls)) return;
    const body = disabledRulesFor(allCssRules, cls).map(r => r.body).join(';');
    expect(body, `no disabled rule for .${cls}`).not.toBe('');
    const opacity = body.match(/opacity\s*:\s*([\d.]+)/);
    expect(opacity?.[1], `.${cls} disabled opacity`).toBe('0.5');
    expect(body, `.${cls} disabled cursor`).toMatch(/cursor\s*:\s*not-allowed/);
  });
});

describe('Settings [S-11] one button family, one scale', () => {
  const FAMILY = ['btn-primary', 'btn-secondary', 'btn-danger', 'btn-add', 'btn-reset'];

  // The vocabulary [S-11] replaced. `.design-toggle` + `.design-reset-btn` were
  // App.css's undocumented parallel set (25 sites, none of them reachable from
  // the documented family); the other four were the config-base names that
  // described a DIALOG ACTION rather than a role -- which is how "Export
  // settings" ended up marked up as a `cancel-btn`.
  const RETIRED = ['save-btn', 'cancel-btn', 'danger-btn', 'add-btn', 'reset-btn',
                   'design-toggle', 'design-reset-btn'];

  const BASE = 'src/styles/config-base.css';
  const baseRules = parseRules(readSource(BASE));

  /** Declarations that apply to `.<cls>` in its RESTING state, merged in source
   *  order across every rule whose selector list targets it. The family shares
   *  one multi-selector rule for the scale, so a lookup keyed on the first
   *  selector (primaryClass) would see only `btn-primary`; this matches any
   *  selector in the list whose last compound is `.<cls>`. */
  function restingDecls(rules: CssRule[], cls: string): Record<string, string> {
    const target = new RegExp(`\\.${cls}(?![\\w-])\\s*$`);
    const out: Record<string, string> = {};
    for (const r of rules) {
      const hit = r.selector.split(',').some(s => {
        const sel = s.trim();
        // `:not(:disabled)` marks the ENABLED case; strip it before asking
        // whether this rule is state-scoped, or every hover rule reads as one.
        const bare = sel.replace(/:not\([^)]*\)/g, '');
        if (/:(hover|disabled|focus|focus-within|focus-visible|active)/.test(bare)) return false;
        return target.test(sel);
      });
      if (!hit) continue;
      for (const d of r.body.split(';')) {
        const i = d.indexOf(':');
        if (i < 0) continue;
        out[d.slice(0, i).trim().toLowerCase()] = d.slice(i + 1).trim();
      }
    }
    return out;
  }

  it('[S-11] the family is declared by config-base.css, scoped under .cfg', () => {
    const offenders: string[] = [];
    for (const cls of FAMILY) {
      const rules = baseRules.filter(r =>
        r.selector.split(',').some(s => new RegExp(`\\.${cls}(?![\\w-])`).test(s)));
      if (rules.length === 0) { offenders.push(`.${cls} is not declared by ${BASE}`); continue; }
      for (const r of rules) {
        for (const s of r.selector.split(',')) {
          const sel = s.trim();
          if (!new RegExp(`\\.${cls}(?![\\w-])`).test(sel)) continue;
          if (!/^\.cfg\b/.test(sel)) offenders.push(`${BASE}: "${sel}" is not scoped under .cfg`);
        }
      }
    }
    expect(offenders).toEqual([]);
  });

  it('[S-11] every role renders at the one scale: min-height 28px / font-size 13px', () => {
    // The scale config-base.css's own comment already asked for ("Match the
    // height/scale of the other controls (Dropdown trigger = 28px)") and that
    // `.text-input` alone honoured. Was: save ~38/15, cancel ~36/15,
    // design-toggle ~24/12, stepper 24/11 -- four scales in one panel.
    const offenders: string[] = [];
    for (const cls of FAMILY) {
      const d = restingDecls(baseRules, cls);
      if (d['min-height'] !== '28px') offenders.push(`.${cls} min-height = ${d['min-height'] ?? '(unset)'}`);
      if (d['font-size'] !== '13px') offenders.push(`.${cls} font-size = ${d['font-size'] ?? '(unset)'}`);
    }
    expect(offenders).toEqual([]);
  });

  it('[S-11] the family matches the baseline controls it is sized against', () => {
    // `.text-input` and NumberStepper are the same 28px row as the family; if
    // one of them moves, the family is no longer "one scale" and this fails
    // rather than letting the scales fork again.
    expect(restingDecls(baseRules, 'text-input')['height'], '.text-input height').toBe('28px');
    expect(restingDecls(baseRules, 'text-input')['font-size'], '.text-input font-size').toBe('13px');
    const stepper = parseRules(readSource('src/components/NumberStepper.css'));
    expect(restingDecls(stepper, 'number-stepper')['height'], '.number-stepper height').toBe('28px');
  });

  it('[S-11] no stylesheet declares a retired button class', () => {
    const offenders: string[] = [];
    for (const file of [...walk(SRC, '.css'), ...walk(join(process.cwd(), 'widgets'), '.css')].map(rel)) {
      const text = readSource(file);
      for (const r of parseRules(text)) {
        for (const cls of RETIRED) {
          // Exact class token. A blunt /\badd-btn\b/ also matches INSIDE
          // `view-add-btn` (the views-bar add button, which is not part of this
          // family and keeps its name) -- a previous attempt renamed it to
          // `view-btn-add` and broke the views bar.
          if (!new RegExp(`\\.${cls}(?![\\w-])`).test(r.selector)) continue;
          const line = text.slice(0, text.indexOf(r.selector)).split('\n').length;
          offenders.push(`${file}:${line} declares .${cls}`);
        }
      }
    }
    expect(offenders).toEqual([]);
  });

  it('[S-11] no component renders a retired button class', () => {
    const offenders: string[] = [];
    for (const file of [...walk(SRC, '.tsx'), ...walk(join(process.cwd(), 'widgets'), '.tsx')].map(rel)) {
      const text = readSource(file);
      text.split('\n').forEach((line, i) => {
        for (const cls of RETIRED) {
          // Same exact-token rule as above: `class="view-add-btn"` must not hit.
          if (new RegExp(`(?<![\\w-])${cls}(?![\\w-])`).test(line)) {
            offenders.push(`${file}:${i + 1} uses .${cls}`);
          }
        }
      });
    }
    expect(offenders).toEqual([]);
  });

  it('[S-11] the views-bar add button keeps its own name (regression: the rename trap)', () => {
    // `.view-add-btn` is views-bar chrome, not a config button. It shares a
    // substring with `add-btn` and nothing else.
    const app = readSource('src/App.css');
    expect(app).toMatch(/\.view-add-btn\s*\{/);
    expect(readSource('src/App.tsx') + app).not.toMatch(/view-btn-add/);
  });

  it('[S-11] the family builds its tints from tokens, never a pre-mixed literal', () => {
    // [S-05]. `.danger-btn` hardcoded rgba(239,68,68,.3)/.08 -- the default
    // --destructive, pre-mixed -- and was the only hardcoded rgba in the file.
    const offenders: string[] = [];
    for (const r of baseRules) {
      if (!FAMILY.some(c => new RegExp(`\\.${c}(?![\\w-])`).test(r.selector))) continue;
      if (/rgba?\(/.test(r.body)) offenders.push(`${r.selector} { ${r.body.trim()} }`);
    }
    expect(offenders).toEqual([]);
  });
});

describe('Settings [S-18] widget config CSS scopes under its own root', () => {
  it('[S-18] no class under the global .cfg scope is claimed by two stylesheets', () => {
    // `.cfg` is the GLOBAL config scope, so `.cfg .items-list` in one widget's
    // stylesheet styles every OTHER config's `.items-list` too. A bare
    // `.cfg .foo` is only harmful when a second file also claims `.foo` --
    // `.cfg .timer-row` is unique to countdown and collides with nothing. So the
    // rule is about collisions, not about bareness.
    const claims = new Map<string, string[]>(); // class -> files declaring it under .cfg
    const files = [
      'src/styles/config-base.css',
      ...walk(join(process.cwd(), 'widgets'), '.css').map(rel).filter(f => /Config\.css$/.test(f)),
    ];
    for (const file of files) {
      for (const r of parseRules(readSource(file))) {
        const first = r.selector.split(',')[0].trim();
        if (!/^\.cfg\s/.test(first)) continue;
        // A last compound with TWO classes (`.items-list.shortcut-items-list`)
        // is a deliberate, widget-scoped override of a base class -- the
        // supported way to opt out. Only single-class claims can collide.
        const compounds = first.split(/\s+|\s*>\s*/).filter(Boolean);
        const last = compounds[compounds.length - 1];
        if ((last.match(/\./g) ?? []).length > 1) continue;
        const cls = primaryClass(first);
        if (!cls) continue;
        const at = claims.get(cls) ?? [];
        if (!at.includes(file)) at.push(file);
        claims.set(cls, at);
      }
    }
    const collisions = [...claims.entries()]
      .filter(([, fs]) => fs.length > 1)
      .map(([cls, fs]) => `.${cls} claimed by ${fs.length}: ${fs.join(', ')}`);
    // Was: .items-list (countdown + habit-tracker), .item-remove (countdown +
    // habit-tracker + shortcut), .add-btn (config-base + shortcut).
    expect(collisions).toEqual([]);
  });
});

describe('Settings [S-16]/[S-17] one control per job', () => {
  const configTsxFiles = walk(join(process.cwd(), 'widgets'), '.tsx')
    .map(rel).filter(f => /Config\.tsx$/.test(f));

  it('[S-16] no config renders a raw number input', () => {
    const offenders: string[] = [];
    for (const f of configTsxFiles) {
      readSource(f).split('\n').forEach((line, i) => {
        if (/type="number"/.test(line)) offenders.push(`${f}:${i + 1}`);
      });
    }
    // Was: 11 sites across contacts, email, onedrive, rss, clipboard,
    // color-swatch, gallery, pomodoro (x2), sun-moon (x2).
    expect(offenders).toEqual([]);
  });

  it('[S-16] no config hand-builds the stepper out of design-val-wrap', () => {
    const offenders: string[] = [];
    for (const f of configTsxFiles) {
      readSource(f).split('\n').forEach((line, i) => {
        if (/class="design-val-input"/.test(line)) offenders.push(`${f}:${i + 1}`);
      });
    }
    // Was: GalleryConfig, in a panel that used NumberStepper for its other two.
    expect(offenders).toEqual([]);
  });

  it('[S-17] no config uses .section-label as the label of a single control', () => {
    // `.section-label` over a LIST or a prose block is a legitimate section
    // heading (countdown's "Timers", habit-tracker's "Habits"). The defect is
    // narrower: using it as the label for ONE control, where `.design-row` +
    // `.design-label` + `.design-desc` is the contract — those rows render an
    // uppercase heading per field and carry no description at all.
    const CONTROL = /<(input|Dropdown|NumberStepper|ToggleSwitch|MultiSelectDropdown|ColorPicker)\b/;
    const COLLECTION = /<(For|Index|Show)\b|items-list|<p\b|empty-hint/;
    const offenders: string[] = [];
    for (const f of configTsxFiles) {
      const text = readSource(f);
      const re = /<div class="field">([\s\S]*?)<\/div>/g;
      let m: RegExpExecArray | null;
      while ((m = re.exec(text)) !== null) {
        const body = m[1];
        if (!/<span class="section-label">/.test(body)) continue;
        if (COLLECTION.test(body)) continue;      // a section over a list/prose
        if (!CONTROL.test(body)) continue;         // no control -> not this defect
        offenders.push(`${f}:${text.slice(0, m.index).split('\n').length}`);
      }
    }
    // Was: pomodoro (x2), clipboard, sun-moon (x3), web-search.
    expect(offenders).toEqual([]);
  });
});

describe('Settings [S-10] the design-row vocabulary has one owner', () => {
  const VOCAB = ['design-row', 'design-left', 'design-label', 'design-desc',
                 'design-val', 'design-val-wrap', 'design-val-input',
                 'design-val-unit', 'design-range-wrap'];

  it('[S-10] only config-base.css declares it, and only scoped under .cfg', () => {
    const offenders: string[] = [];
    for (const file of walk(SRC, '.css').map(rel)) {
      const text = readSource(file);
      for (const r of parseRules(text)) {
        const cls = primaryClass(r.selector);
        if (!cls || !VOCAB.includes(cls)) continue;
        const line = text.slice(0, text.indexOf(r.selector)).split('\n').length;
        if (file !== 'src/styles/config-base.css') {
          offenders.push(`${file}:${line} redeclares .${cls} — config-base.css owns it`);
        } else if (!/^\.cfg\b/.test(r.selector)) {
          offenders.push(`${file}:${line} declares .${cls} unscoped — must be under .cfg`);
        }
      }
    }
    // Was: App.css carried an unscoped duplicate of all nine.
    expect(offenders).toEqual([]);
  });
});

describe('Settings [S-08]/[S-09] config chrome is translated and re-reads on language change', () => {
  const configTsx = [
    ...walk(join(process.cwd(), 'widgets'), '.tsx').map(rel).filter(f => /Config\.tsx$/.test(f)),
    'src/components/ConfigTabs.tsx',
  ];

  it('[S-08] the scan sees the config components (guards the scanner)', () => {
    expect(configTsx.length).toBeGreaterThanOrEqual(30);
  });

  it('[S-08] no config hardcodes a Source/Display/Refresh tab label', () => {
    // These three already ship translated in all nine locales as
    // configTabs.source/display/refresh; hardcoding them bypassed the
    // translations that were already there.
    const offenders: string[] = [];
    for (const f of configTsx) {
      readSource(f).split('\n').forEach((line, i) => {
        if (/label:\s*'(Source|Display|Refresh)'/.test(line)) offenders.push(`${f}:${i + 1}`);
      });
    }
    expect(offenders).toEqual([]);
  });

  it('[S-09] no module-level const holds a t() call (it would freeze the boot language)', () => {
    const offenders: string[] = [];
    for (const f of configTsx) {
      const lines = readSource(f).split('\n');
      let inConst = false;
      let start = 0;
      for (let i = 0; i < lines.length; i++) {
        const l = lines[i];
        // Module scope only: a top-level `const X = [` / `= {` with no indent.
        if (!inConst && /^const\s+\w+(\s*:[^=]+)?\s*=\s*[[{]\s*$/.test(l)) { inConst = true; start = i; continue; }
        if (inConst) {
          if (/^[\]}]\s*(as const)?\s*;?\s*$/.test(l)) { inConst = false; continue; }
          if (/\bt\(/.test(l)) {
            offenders.push(`${f}:${start + 1} const initialiser calls t() — make it a function`);
            inConst = false;
          }
        }
      }
    }
    // Was: nine widget configs holding `const tabs = [{ label: t('…') }]`.
    expect(offenders).toEqual([]);
  });
});

describe('Settings [S-07] .settings-content and .cfg never fight at equal specificity', () => {
  // They are always on the same element, so an equal-specificity overlap is
  // resolved by bundler emit order, not intent -- on every config page in the app.
  const declaredBy = (file: string, selector: string): Set<string> => {
    const props = new Set<string>();
    for (const r of parseRules(readSource(file))) {
      if (r.selector !== selector) continue;
      for (const d of r.body.split(';')) {
        const p = d.split(':')[0].trim().toLowerCase();
        if (p) props.add(p);
      }
    }
    return props;
  };

  it('[S-07] no bare .settings-content rule declares a property .cfg also declares', () => {
    const cfg = declaredBy('src/styles/config-base.css', '.cfg');
    const content = declaredBy('src/components/SettingsModal.css', '.settings-content');
    expect(cfg.size, 'guard: .cfg rule not found').toBeGreaterThan(3);
    const overlap = [...content].filter(p => cfg.has(p));
    // Was: padding, display, flex-direction, gap, overflow-y, flex, min-height
    // -- five identical, two silently different.
    expect(overlap).toEqual([]);
  });

  it('[S-07] the intended denser spacing is declared at .settings-content.cfg', () => {
    const dense = declaredBy('src/components/SettingsModal.css', '.settings-content.cfg');
    expect([...dense].sort()).toEqual(['gap', 'padding']);
  });
});

describe('Settings [S-04] widget config CSS positions shared controls, never restyles them', () => {
  // Appearance belongs to the class's owner. A widget may size/place a shared
  // control in its layout; making it LOOK different is what let Calendar's
  // account rows drift away from the same rows in ten sibling configs.
  const APPEARANCE = /^(background|border|padding|font|color|box-shadow|letter-spacing|text-transform|text-decoration|outline)/;

  /** class -> the stylesheet that owns its appearance. */
  const owners = new Map<string, string>();
  for (const f of ['src/styles/config-base.css', ...walk(join(SRC, 'components'), '.css').map(rel)]) {
    for (const r of parseRules(readSource(f))) {
      const c = primaryClass(r.selector);
      if (c && !owners.has(c)) owners.set(c, f);
    }
  }

  const widgetConfigCss = walk(join(process.cwd(), 'widgets'), '.css')
    .map(rel)
    .filter(f => /Config\.css$/.test(f));

  it('[S-04] the scan sees the config stylesheets (guards the scanner)', () => {
    // A floor, not a count: this only exists so a broken scanner can't make the
    // assertion below pass vacuously. It dropped from 20 when the 12 dead
    // config stylesheets were deleted, and from 15 when the two weather configs
    // gave up theirs to the shared LocationSearch.css.
    expect(widgetConfigCss.length).toBeGreaterThanOrEqual(13);
    expect(owners.size).toBeGreaterThanOrEqual(100);
  });

  it('[S-04] no widget config restyles a class it does not own', () => {
    const offenders: string[] = [];
    for (const file of widgetConfigCss) {
      const text = readSource(file);
      for (const r of parseRules(text)) {
        const cls = primaryClass(r.selector);
        if (!cls || !owners.has(cls)) continue;
        const props = r.body.split(';')
          .map(d => d.split(':')[0].trim().toLowerCase())
          .filter(p => APPEARANCE.test(p));
        if (props.length === 0) continue; // layout-only tweak: allowed
        const line = text.slice(0, text.indexOf(r.selector)).split('\n').length;
        offenders.push(`${file}:${line} restyles .${cls} (owned by ${owners.get(cls)}) via ${props.join(', ')}`);
      }
    }
    // Was: 8x CalendarConfig.css re-declaring AccountToggleList's rows with
    // drifted values, + FolderViewConfig.css pill-ifying .col-chip against the
    // base's 5px radius.
    expect(offenders).toEqual([]);
  });
});
