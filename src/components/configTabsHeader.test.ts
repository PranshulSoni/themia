// WidgetHeader.SPEC [S-13]: a registry-`noHeader` widget shows NO Header section
// in its config at all.
//
// The bug this pins: ConfigTabs gated only the dropdown and the title field on
// `!noHeader()`, so clock / color-swatch / themia / tabs / stack still rendered a
// "Header" section label, a "Style" row reading "This widget has no header", and
// a divider — three rows of chrome for a setting that can never be set.
//
// jsdom can't cascade the app's stylesheets and there's no Solid renderer in the
// unit suite, so this is a SOURCE scan (the pattern from configWindowBackground
// .test.ts / settingsControls.test.ts): it pins the gate itself rather than the
// rendered DOM.

import { describe, it, expect } from 'vitest';
import { readSource } from '../../tests/cssRules.ts';
import { en } from '../locales/en.ts';

const SRC = 'src/components/ConfigTabs.tsx';

describe('WidgetHeader [S-13] noHeader widgets get no Header config section', () => {
  it('[S-13] the header block is gated on !noHeader(), not just its dropdown', () => {
    const src = readSource(SRC);
    // The section label is the first thing in the block; whatever <Show> wraps it
    // must be the one that tests noHeader.
    const gate = src.match(/<Show when=\{([^}]*)\}>\s*\n\s*<span class="section-label">\{t\('configTabs\.header'\)\}/);
    expect(gate, 'header section-label is no longer inside a <Show> gate').not.toBeNull();
    expect(gate![1]).toMatch(/!\s*header\.noHeader\(\)/);
  });

  it('[S-13] nothing renders "this widget has no header" copy', () => {
    expect(readSource(SRC)).not.toContain('styleDescNoHeader');
    // Dead key removed from the dictionary too, so it can't be reintroduced by
    // autocomplete (locales.test.ts then forbids it in the other 8 locales).
    expect(Object.keys(en.configTabs)).not.toContain('styleDescNoHeader');
  });
});
