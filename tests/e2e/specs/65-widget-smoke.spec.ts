/**
 * Smoke specs for widgets that were previously uncovered by the e2e suite:
 *   - RSS feed
 *   - Stocks
 *   - Weather (current)
 *   - Weather forecast
 *
 * Each widget is seeded directly into the active layout via the test-only
 * `seedWidgets` helper, which bypasses the ~2–3 s `pickWidget` +
 * `clickGridCell` flow and just verifies the component renders without
 * crashing under its default (empty) config.
 *
 * NOTE: YouTube is intentionally absent: the widget source exists under
 * widgets/online/youtube/ but is not imported into src/widgets/registry.tsx
 * so it never appears in the picker. Re-add once the import is restored.
 */
import { expect } from 'chai';
import { $, $$ } from '@wdio/globals';
import { resetAppState, stubThemiaInvoke, seedWidgets } from '../helpers/app.js';

interface Case {
  type: string;
  rootSelector: string;
}

const CASES: Case[] = [
  { type: 'rss',             rootSelector: '.rss-widget' },
  { type: 'stocks',          rootSelector: '.stocks-widget' },
  { type: 'weatherCurrent',  rootSelector: '.weather-current-widget' },
  { type: 'weatherForecast', rootSelector: '.weather-forecast-widget' },
];

describe('widget smoke (uncovered widgets)', () => {
  before(async () => {
    await resetAppState();
    await stubThemiaInvoke({ '*': null });
    // Seed every uncovered widget once. They all short-circuit on empty
    // config (no feed URL / symbols / coords / channel / layouts) and
    // render their placeholder rather than firing the real backend.
    await seedWidgets(CASES.map((c, i) => ({
      type: c.type,
      col: 1 + (i * 3),
      row: 1,
      colSpan: 3,
      rowSpan: 2,
    })));
  });

  for (const c of CASES) {
    it(`${c.type} renders ${c.rootSelector} with default config`, async () => {
      await $(c.rootSelector).waitForExist({ timeout: 5_000 });
      expect(await $$(c.rootSelector).length).to.be.at.least(1);
    });
  }
});
