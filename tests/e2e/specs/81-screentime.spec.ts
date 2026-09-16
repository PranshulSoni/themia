/**
 * Screen Time widget (widgets/system/screentime/SPEC.md).
 *
 * The tracking backend (foreground hook, idle sampler) can't run under
 * webdriver, so these specs stub `screentime_get_day` through
 * `window.__themiaInvoke.setInvoke` and assert the display behaviors
 * [S-01]..[S-06], [S-10]..[S-12] and the export-privacy contract [S-34].
 * Backend accrual is covered by the Rust unit tests in
 * widgets/system/screentime/src/lib.rs; [S-07]..[S-09] (poll cadence, demo
 * fixture, metric multi-select) are not asserted here.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import {
  resetAppState, stubThemiaInvoke, seedWidgets, waitForConfigWindow, switchToMain,
  closeConfigModal,
} from '../helpers/app.js';
import { settle } from '../helpers/wait.js';

const DAY_STATS = {
  date: '2026-07-02',
  total_secs: 3 * 3600 + 24 * 60, // 3h 24m
  apps: [
    { exe: 'code',    name: 'Visual Studio Code', secs: 2 * 3600 },      // 2h 0m
    { exe: 'chrome',  name: 'Google Chrome',      secs: 1 * 3600 + 60 }, // 1h 1m
    { exe: 'discord', name: 'Discord',            secs: 23 * 60 },       // 23m
  ],
};

const EMPTY_DAY = { date: '2026-07-02', total_secs: 0, apps: [] };

// [S-37] a 1x1 transparent PNG stands in for an extracted app icon.
const ICON_URL =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';

// [S-36]/[S-37] a realistic day (total == sum of apps, as the backend
// guarantees). Each app carries a path for icon extraction. With a maxApps cap
// below the app count, the apps beyond the cap fold into an "Other" wedge.
const DAY_WITH_PATHS = {
  date: '2026-07-02',
  total_secs: 4 * 3600, // 2h + 1h + 40m + 20m
  apps: [
    { exe: 'code',    name: 'Visual Studio Code', secs: 2 * 3600, path: 'C:/apps/code.exe' },
    { exe: 'chrome',  name: 'Google Chrome',      secs: 1 * 3600, path: 'C:/apps/chrome.exe' },
    { exe: 'discord', name: 'Discord',            secs: 40 * 60,  path: 'C:/apps/discord.exe' },
    { exe: 'spotify', name: 'Spotify',            secs: 20 * 60,  path: 'C:/apps/spotify.exe' },
  ],
};

const ICONS_BY_PATH = {
  'C:/apps/code.exe': ICON_URL,
  'C:/apps/chrome.exe': ICON_URL,
  'C:/apps/discord.exe': ICON_URL,
  'C:/apps/spotify.exe': ICON_URL,
};

// [S-10] fixture with MORE apps than the cap, so the cap is actually
// exercised (a 3-app fixture with maxApps 3 would pass even if maxApps were
// ignored).
const MANY_APPS = {
  date: '2026-07-02',
  total_secs: 5 * 3600,
  apps: [
    { exe: 'code',     name: 'Visual Studio Code', secs: 2 * 3600 },
    { exe: 'chrome',   name: 'Google Chrome',      secs: 3600 },
    { exe: 'discord',  name: 'Discord',            secs: 40 * 60 },
    { exe: 'spotify',  name: 'Spotify',            secs: 30 * 60 },
    { exe: 'explorer', name: 'Windows Explorer',   secs: 20 * 60 },
  ],
};

/** Polling has a 0–2s stagger before the first refresh; allow ~8s total. */
async function waitForWidgetText(fragment: string, timeout = 8_000): Promise<void> {
  await browser.waitUntil(async () => {
    const txt = await browser.execute(() => {
      const el = document.querySelector('.sctw-widget') as HTMLElement | null;
      return el ? (el.textContent ?? '') : '';
    });
    return typeof txt === 'string' && txt.includes(fragment);
  }, { timeout, timeoutMsg: `expected screentime widget to contain "${fragment}"` });
}

async function widgetText(): Promise<string> {
  const txt = await browser.execute(() => {
    const el = document.querySelector('.sctw-widget') as HTMLElement | null;
    return el ? (el.textContent ?? '') : '';
  });
  return txt as string;
}

// ─────────────────────────────────────────────────────────────────────────────
describe('screentime: totals and app list', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'screentime_get_day': DAY_STATS, '*': null });
    await seedWidgets([{ type: 'screentime', col: 2, row: 2, colSpan: 3, rowSpan: 3 }]);
  });

  it('S-02: renders sctw-widget and the formatted daily total', async () => {
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('3h 24m');
  });

  it('S-01: header secondary hosts the day nav (calendar pattern)', async () => {
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('3h 24m');
    const header = await browser.execute(() => {
      const nav = document.querySelector('.sctw-widget .widget-header .sctw-nav');
      return {
        inHeader: !!nav,
        title: nav?.querySelector('.sctw-nav-title')?.textContent ?? null,
        hasTodayDot: !!nav?.querySelector('.sctw-today-btn'),
      };
    }) as { inHeader: boolean; title: string | null; hasTodayDot: boolean };
    expect(header.inHeader).to.equal(true);
    expect(header.title).to.equal('Today');
    expect(header.hasTodayDot).to.equal(true);
  });

  it('S-03: lists apps sorted by usage with names and durations', async () => {
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('Visual Studio Code');
    const txt = await widgetText();
    expect(txt).to.include('Google Chrome');
    expect(txt).to.include('Discord');
    // [S-04] per-app durations formatted
    expect(txt).to.include('2h 0m');
    expect(txt).to.include('1h 1m');
    expect(txt).to.include('23m');
    // [S-03] sorted descending: Code before Chrome before Discord.
    expect(txt.indexOf('Visual Studio Code')).to.be.lessThan(txt.indexOf('Google Chrome'));
    expect(txt.indexOf('Google Chrome')).to.be.lessThan(txt.indexOf('Discord'));
  });

});

// ─────────────────────────────────────────────────────────────────────────────
describe('screentime: maxApps config and empty day', () => {
  it('S-10: maxApps caps the rendered rows below the fixture app count', async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'screentime_get_day': MANY_APPS, '*': null });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 3, rowSpan: 3,
      config: { maxApps: 3 }, // fixture has 5 apps, so the cap actually bites
    }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('Visual Studio Code');
    const rows = await browser.execute(
      // Real rows only: [S-56] pads the rest with hidden filler.
      () => document.querySelectorAll('.sctw-legend-row:not(.sctw-legend-row--ghost)').length,
    );
    expect(rows).to.equal(4); // top 3 apps + the aggregate "Other"
    // The two lowest-usage apps fell off the list, into "Other".
    const txt = await widgetText();
    expect(txt).to.not.include('Spotify');
    expect(txt).to.not.include('Windows Explorer');
    expect(txt).to.include('Other');
  });

  it('S-06: empty day renders the "No activity yet" placeholder', async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'screentime_get_day': EMPTY_DAY, '*': null });
    await seedWidgets([{ type: 'screentime', col: 2, row: 2, colSpan: 3, rowSpan: 3 }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('No activity yet');
  });

  it('#159: a zero-usage day hides the total, keeping only the placeholder', async () => {
    // Regression for #159: formatDuration(0) rendered a "<1m" total that
    // looked wrong stacked directly above "No activity yet". The total row
    // (.sctw-total) must not render at all when total_secs === 0.
    await resetAppState();
    await stubThemiaInvoke({ 'screentime_get_day': EMPTY_DAY, '*': null });
    await seedWidgets([{ type: 'screentime', col: 2, row: 2, colSpan: 3, rowSpan: 3 }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('No activity yet');
    const hasTotal = await browser.execute(
      () => !!document.querySelector('.sctw-widget .sctw-total'),
    );
    expect(hasTotal).to.equal(false);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
describe('screentime: doughnut display (S-36) and app icons (S-37)', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubThemiaInvoke({
      'screentime_get_day': DAY_WITH_PATHS,
      'get_file_icons_batch': ICONS_BY_PATH,
      '*': null,
    });
  });

  it('S-36: doughnut renders one wedge per shown app, filling the ring, total centered', async () => {
    // Default maxApps (7) > 4 apps, so every app shows and the wedges cover the
    // whole day: no "Other" remainder.
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 4,
      config: { displayStyle: 'doughnut' },
    }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await $('.sctw-donut-svg').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('Visual Studio Code');
    const segs = await browser.execute(
      () => document.querySelectorAll('.sctw-donut-seg').length,
    );
    expect(segs).to.equal(4);
    // The day total lives in the ring's hole (sum of the apps = 4h).
    const center = await browser.execute(
      () => document.querySelector('.sctw-donut-center .sctw-donut-total')?.textContent ?? '',
    );
    expect(center).to.equal('4h 0m');
    // A doughnut is a ring, not the horizontal split bar ([S-50]).
    const bars = await browser.execute(() => document.querySelectorAll('.sctw-hbar').length);
    expect(bars).to.equal(0);
  });

  it('S-36: apps beyond maxApps fold into a single "Other" wedge', async () => {
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 4,
      config: { displayStyle: 'doughnut', maxApps: 3 }, // 4 apps → top 3 + Other
    }]);
    await $('.sctw-donut-svg').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('Other');
    const segs = await browser.execute(
      () => document.querySelectorAll('.sctw-donut-seg').length,
    );
    expect(segs).to.equal(4); // 3 apps + Other
    // Center still reflects the whole shown total.
    const center = await browser.execute(
      () => document.querySelector('.sctw-donut-center .sctw-donut-total')?.textContent ?? '',
    );
    expect(center).to.equal('4h 0m');
  });

  it('S-40: hovering a wedge highlights it, dims the rest, and swaps the centre', async () => {
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 4,
      config: { displayStyle: 'doughnut' },
    }]);
    await $('.sctw-donut-svg').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('Google Chrome');
    const state = await browser.execute(() => {
      const segs = Array.from(document.querySelectorAll<SVGCircleElement>('.sctw-donut-seg'));
      segs[1].dispatchEvent(new MouseEvent('mouseenter', { bubbles: false }));
      return {
        active: segs.map((s) => s.classList.contains('is-active')),
        dim: segs.map((s) => s.classList.contains('is-dim')),
        focusName: document.querySelector('.sctw-donut-focus-name')?.textContent ?? null,
        legendActive: Array.from(document.querySelectorAll('.sctw-legend-row'))
          .map((r) => r.classList.contains('is-active')),
      };
    }) as { active: boolean[]; dim: boolean[]; focusName: string | null; legendActive: boolean[] };
    expect(state.active[1]).to.equal(true);
    expect(state.dim[0]).to.equal(true);
    expect(state.focusName).to.equal('Google Chrome'); // centre swapped to the app
    expect(state.legendActive[1]).to.equal(true); // legend mirrors the wedge
  });

  it('S-37: icons render in the legend, one per app', async () => {
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 4,
      config: { displayStyle: 'doughnut' },
    }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await $('.sctw-legend').waitForExist({ timeout: 4_000 });
    await browser.waitUntil(
      async () => (await browser.execute(
        () => document.querySelectorAll('.sctw-legend-row img.sctw-app-icon').length,
      )) as number > 0,
      { timeout: 4_000, timeoutMsg: 'expected app icons to load in the legend' },
    );
    const legendIcons = await browser.execute(
      () => document.querySelectorAll('.sctw-legend-row img.sctw-app-icon').length,
    );
    expect(legendIcons).to.equal(4); // one per app; "Other" uses a color dot
  });

  it('S-39: right-click "Hide from list" removes the app and re-bases the total', async () => {
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 3, rowSpan: 4,
      config: { displayStyle: 'doughnut' },
    }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('Spotify');

    // Right-click the Spotify row.
    const opened = await browser.execute((name: string) => {
      const rows = Array.from(document.querySelectorAll<HTMLElement>('.sctw-legend-row'));
      for (const r of rows) {
        if ((r.textContent ?? '').includes(name)) {
          const b = r.getBoundingClientRect();
          r.dispatchEvent(new MouseEvent('contextmenu', {
            bubbles: true, cancelable: true, view: window,
            clientX: b.left + 4, clientY: b.top + 4, button: 2,
          }));
          return true;
        }
      }
      return false;
    }, 'Spotify');
    expect(opened, 'failed to dispatch contextmenu on Spotify').to.equal(true);

    await $('.ctx-menu').waitForDisplayed({ timeout: 2_000 });
    const clicked = await browser.execute(() => {
      const items = Array.from(document.querySelectorAll<HTMLElement>('.ctx-menu .ctx-item'));
      for (const item of items) {
        if ((item.querySelector('.ctx-label')?.textContent?.trim() ?? '') === 'Hide from list') {
          item.click();
          return true;
        }
      }
      return false;
    });
    expect(clicked, 'failed to click Hide from list').to.equal(true);

    // Spotify gone; total re-based from 4h to 3h 40m (its 20m removed).
    await browser.waitUntil(async () => {
      const rows = await browser.execute(() => Array.from(
        document.querySelectorAll('.sctw-legend-row:not(.sctw-legend-row--ghost) .sctw-app-name'),
      ).map((el) => (el.textContent ?? '').trim()));
      return Array.isArray(rows) && rows.length === 3 && !rows.some((n) => n === 'Spotify');
    }, { timeout: 5_000, timeoutMsg: 'Spotify never disappeared after Hide from list' });
    await waitForWidgetText('3h 40m');

    // Persisted into the widget's config.
    await settle(300, 'layout save debounce');
    const hiddenApps = await browser.execute(() => {
      const raw = localStorage.getItem('widget-layout') ?? '{}';
      const widgets = (JSON.parse(raw).widgets ?? []) as { type: string; config?: Record<string, unknown> }[];
      return widgets.find((w) => w.type === 'screentime')?.config?.hiddenApps ?? null;
    }) as string[] | null;
    expect(hiddenApps).to.deep.equal(['spotify']);
  });
});

function dateMinusDays(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() - days);
  const m = String(d.getMonth() + 1).padStart(2, '0');
  return `${d.getFullYear()}-${m}-${String(d.getDate()).padStart(2, '0')}`;
}

/** [S-45] the backend contract: one entry per day, OLDEST first, ending today.
 *  Every non-idle day splits 2:1 between two apps, so the bars have something
 *  to stack ([S-48]) and the legend has two entries. */
function rangeFixture(n: number, secsAt: (i: number) => number) {
  return Array.from({ length: n }, (_, i) => {
    const secs = secsAt(i);
    return {
      date: dateMinusDays(n - 1 - i),
      total_secs: secs,
      apps: secs > 0 ? [
        { exe: 'code', name: 'Visual Studio Code', secs: (secs / 3) * 2 },
        { exe: 'chrome', name: 'Google Chrome', secs: secs / 3 },
      ] : [],
    };
  });
}

// Strictly increasing, so today (last) is the busiest day: 10m..1h 10m,
// 4h 40m over the window.
const WEEK_RANGE = rangeFixture(7, (i) => (i + 1) * 600);
// Every 5th day idle, so the heatmap has real gaps as well as levels.
const MONTH_RANGE = rangeFixture(30, (i) => (i % 5 === 0 ? 0 : (i % 4) * 1800 + 1800));

// ─────────────────────────────────────────────────────────────────────────────
describe('screentime: period × style (S-46) — bars (S-42), heatmap (S-43)', () => {
  it('S-42: renders one bar per day of the last 7, tallest on the busiest', async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'screentime_get_range': WEEK_RANGE, '*': null });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 4,
      config: { period: 'week', displayStyle: 'bars' },
    }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await $('.sctw-week').waitForExist({ timeout: 8_000 });

    const cols = await browser.execute(
      () => document.querySelectorAll('.sctw-week-col').length,
    );
    expect(cols).to.equal(7);

    // Heights are the share of the busiest day, so the last (busiest) fills its
    // track and the first (1/7 of it) is ~14%. A column's parts sum to its bar.
    const columns = await browser.execute(() => Array.from(
      document.querySelectorAll<HTMLElement>('.sctw-week-col'),
    ).map((col) => Array.from(col.querySelectorAll<HTMLElement>('.sctw-week-part'))
      .reduce((sum, el) => sum + parseFloat(el.style.height || '0'), 0))) as number[];
    expect(columns.length).to.equal(7);
    expect(columns[6]).to.be.closeTo(100, 0.05);
    expect(columns[0]).to.be.closeTo(100 / 7, 0.5);

    // [S-41] the header names the span instead of hosting the day nav.
    const header = await browser.execute(() => ({
      range: document.querySelector('.sctw-widget .widget-header .sctw-range-label')?.textContent ?? null,
      nav: !!document.querySelector('.sctw-widget .sctw-nav'),
    })) as { range: string | null; nav: boolean };
    expect(header.range).to.equal('Last 7 days');
    expect(header.nav).to.equal(false);

    // The readout is the window total (600 * (1+..+7) = 16800s).
    await waitForWidgetText('4h 40m');
  });

  it('S-44: hovering a bar swaps the readout to that day', async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'screentime_get_range': WEEK_RANGE, '*': null });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 4,
      config: { period: 'week', displayStyle: 'bars' },
    }]);
    await $('.sctw-week').waitForExist({ timeout: 8_000 });
    const state = await browser.execute(() => {
      const cols = Array.from(document.querySelectorAll('.sctw-week-col'));
      cols[1].dispatchEvent(new MouseEvent('mouseenter', { bubbles: false }));
      return {
        total: document.querySelector('.sctw-total')?.textContent ?? null,
        sub: !!document.querySelector('.sctw-range-sub'),
        active: cols[1].classList.contains('is-active'),
        dimmedOthers: cols[0].classList.contains('is-dim'),
      };
    }) as { total: string | null; sub: boolean; active: boolean; dimmedOthers: boolean };
    expect(state.total).to.equal('20m'); // day 2 of the window = 1200s
    expect(state.sub).to.equal(true);    // its date, beside the duration
    expect(state.active).to.equal(true);
    expect(state.dimmedOthers).to.equal(true);
  });

  it('S-43: month renders 30 day cells in a 7-wide grid, padded to whole rows', async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'screentime_get_range': MONTH_RANGE, '*': null });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 4,
      config: { period: 'month', displayStyle: 'heatmap' },
    }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await $('.sctw-heat-grid').waitForExist({ timeout: 8_000 });

    const grid = await browser.execute(() => ({
      cells: document.querySelectorAll('.sctw-heat-cell').length,
      blanks: document.querySelectorAll('.sctw-heat-cell.is-blank').length,
      // [S-43] level 0 = a day with no usage at all (6 of the 30 here).
      level0: document.querySelectorAll('.sctw-heat-cell.sctw-heat-l0').length,
      level4: document.querySelectorAll('.sctw-heat-cell.sctw-heat-l4').length,
      today: document.querySelectorAll('.sctw-heat-cell.is-today').length,
      dowLabels: document.querySelectorAll('.sctw-heat-dow').length,
      range: document.querySelector('.sctw-widget .sctw-range-label')?.textContent ?? null,
    })) as Record<string, number | string | null>;

    expect(grid.cells).to.equal(35);   // 30 days + 5 leading blanks = 5 rows of 7
    expect(grid.blanks).to.equal(5);
    expect(grid.level0).to.equal(6);   // i % 5 === 0 → 6 idle days
    expect(grid.level4).to.be.greaterThan(0);
    expect(grid.today).to.equal(1);
    expect(grid.dowLabels).to.equal(7);
    expect(grid.range).to.equal('Last 30 days');
    // No day list or ring in a window layout.
    const others = await browser.execute(() => ({
      donut: document.querySelectorAll('.sctw-donut-seg').length,
    })) as { donut: number };
    expect(others.donut).to.equal(0);
  });

  it('S-41: a window with no usage at all shows the placeholder', async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'screentime_get_range': rangeFixture(7, () => 0), '*': null });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 4,
      config: { period: 'week', displayStyle: 'bars' },
    }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('No activity yet');
    const charts = await browser.execute(
      () => document.querySelectorAll('.sctw-week, .sctw-heat').length,
    );
    expect(charts).to.equal(0);
  });

  it('S-46: the axes are independent — a MONTH of bars, a WEEK of heat cells', async () => {
    // The pairing the old single field couldn't express: period and style are
    // chosen separately, so these two combinations must render.
    await resetAppState();
    await stubThemiaInvoke({ 'screentime_get_range': MONTH_RANGE, '*': null });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 4,
      config: { period: 'month', displayStyle: 'bars' },
    }]);
    await $('.sctw-week').waitForExist({ timeout: 8_000 });
    const monthBars = await browser.execute(() => ({
      cols: document.querySelectorAll('.sctw-week-col').length,
      // Past 7 columns the per-column weekday labels are dropped [S-42].
      labels: document.querySelectorAll('.sctw-week-dow').length,
      dense: !!document.querySelector('.sctw-week--dense'),
      range: document.querySelector('.sctw-range-label')?.textContent ?? null,
    })) as { cols: number; labels: number; dense: boolean; range: string | null };
    expect(monthBars.cols).to.equal(30);
    expect(monthBars.labels).to.equal(0);
    expect(monthBars.dense).to.equal(true);
    expect(monthBars.range).to.equal('Last 30 days');

    await resetAppState();
    await stubThemiaInvoke({ 'screentime_get_range': WEEK_RANGE, '*': null });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 4,
      config: { period: 'week', displayStyle: 'heatmap' },
    }]);
    await $('.sctw-heat-grid').waitForExist({ timeout: 8_000 });
    const weekHeat = await browser.execute(() => ({
      cells: document.querySelectorAll('.sctw-heat-cell').length,
      blanks: document.querySelectorAll('.sctw-heat-cell.is-blank').length,
      short: !!document.querySelector('.sctw-heat--short'),
      range: document.querySelector('.sctw-range-label')?.textContent ?? null,
    })) as { cells: number; blanks: number; short: boolean; range: string | null };
    expect(weekHeat.cells).to.equal(7); // one full row, no padding
    expect(weekHeat.blanks).to.equal(0);
    expect(weekHeat.short).to.equal(true);
    expect(weekHeat.range).to.equal('Last 7 days');
  });

  it('S-48: each bar is stacked by app, in the legend\'s order', async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'screentime_get_range': WEEK_RANGE, '*': null });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 5,
      config: { period: 'week', displayStyle: 'bars' },
    }]);
    await $('.sctw-week').waitForExist({ timeout: 8_000 });
    await waitForWidgetText('Visual Studio Code'); // the window legend [S-03]

    const stacked = await browser.execute(() => {
      const cols = Array.from(document.querySelectorAll('.sctw-week-col'));
      const parts = (i: number) => Array.from(
        cols[i].querySelectorAll<HTMLElement>('.sctw-week-part'),
      ).map((el) => ({ h: parseFloat(el.style.height || '0'), bg: el.style.backgroundColor }));
      return {
        perColumn: cols.map((c) => c.querySelectorAll('.sctw-week-part').length),
        busiest: parts(6),
        // [S-56] `:not(--ghost)`: the list is padded to `maxApps` with filler
        // rows that carry the same spans, and those are not legend entries.
        legend: Array.from(document.querySelectorAll('.sctw-legend-row:not(.sctw-legend-row--ghost) .sctw-app-name'))
          .map((el) => (el.textContent ?? '').trim()),
        legendSecs: Array.from(document.querySelectorAll('.sctw-legend-row:not(.sctw-legend-row--ghost) .sctw-app-secs'))
          .map((el) => (el.textContent ?? '').trim()),
        // [S-51] the colour key on each legend row.
        swatches: Array.from(document.querySelectorAll<HTMLElement>('.sctw-legend-swatch'))
          .map((el) => el.style.backgroundColor),
        // [S-52] the per-day average beside each total, and the headline one.
        avgs: Array.from(document.querySelectorAll('.sctw-legend-row .sctw-app-avg'))
          .map((el) => (el.textContent ?? '').trim()),
        headlineAvg: document.querySelector('.sctw-total-avg')?.textContent?.trim() ?? null,
        // [S-51]/[S-52] column order within a row: name, dot, average, total.
        rowOrder: Array.from(document.querySelectorAll('.sctw-legend-row')[0].children)
          .map((el) => el.className.split(' ').find((c) => c.startsWith('sctw-')) ?? ''),
      };
    }) as {
      perColumn: number[]; busiest: { h: number; bg: string }[];
      legend: string[]; legendSecs: string[]; swatches: string[]; avgs: string[];
      headlineAvg: string | null; rowOrder: string[];
    };

    // Two apps per day → two parts per bar, every day.
    expect(stacked.perColumn).to.deep.equal([2, 2, 2, 2, 2, 2, 2]);
    // Split 2:1 in legend order (the bigger app first, at the base of the bar).
    expect(stacked.busiest[0].h).to.be.closeTo(200 / 3, 0.05);
    expect(stacked.busiest[1].h).to.be.closeTo(100 / 3, 0.05);
    expect(stacked.busiest[0].bg).to.not.equal(stacked.busiest[1].bg);
    // [S-03] the legend aggregates the window: 400*28 = 3h 6m, 200*28 = 1h 33m.
    expect(stacked.legend).to.deep.equal(['Visual Studio Code', 'Google Chrome']);
    expect(stacked.legendSecs).to.deep.equal(['3h 6m', '1h 33m']);
    // [S-51] each row's dot is the colour that app has in the bars.
    expect(stacked.swatches).to.deep.equal(stacked.busiest.map((p) => p.bg));
    // [S-52] divided by the 7-day window: 11200/7 = 26m40s, 5600/7 = 13m20s.
    expect(stacked.avgs).to.deep.equal(['26m/day', '13m/day']);
    // The headline carries the window's own average: 16800/7 = 40m.
    expect(stacked.headlineAvg).to.equal('40m/day');
    // [S-51]/[S-52] the dot follows the title; the average sits left of the total.
    expect(stacked.rowOrder).to.deep.equal([
      'sctw-app-icon', 'sctw-app-name', 'sctw-legend-swatch', 'sctw-app-avg', 'sctw-app-secs',
    ]);
  });

  it('S-46: a pre-split config (displayStyle held the period) still renders', async () => {
    // Regression for the migration: configs written before period existed put
    // 'week'/'month' in displayStyle. They must resolve to period + chart style,
    // not to an empty body.
    await resetAppState();
    await stubThemiaInvoke({ 'screentime_get_range': MONTH_RANGE, '*': null });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 4,
      config: { displayStyle: 'month' }, // legacy: no period key at all
    }]);
    await $('.sctw-heat-grid').waitForExist({ timeout: 8_000 });
    const legacy = await browser.execute(() => ({
      cells: document.querySelectorAll('.sctw-heat-cell').length,
      range: document.querySelector('.sctw-range-label')?.textContent ?? null,
    })) as { cells: number; range: string | null };
    expect(legacy.cells).to.equal(35);
    expect(legacy.range).to.equal('Last 30 days');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
describe('screentime: day bars (S-50), settings and metrics (S-53/S-56/S-57)', () => {
  beforeEach(async () => {
    await resetAppState();
    await stubThemiaInvoke({
      'screentime_get_day': DAY_WITH_PATHS,
      'get_file_icons_batch': ICONS_BY_PATH,
      '*': null,
    });
  });

  it('S-50: day + bars is ONE horizontal bar split by app, filling the width', async () => {
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 3,
      config: { period: 'day', displayStyle: 'bars' },
    }]);
    await $('.sctw-hbar').waitForExist({ timeout: 8_000 });
    const bar = await browser.execute(() => {
      const parts = Array.from(document.querySelectorAll<HTMLElement>('.sctw-hbar-part'));
      return {
        count: parts.length,
        widths: parts.map((el) => parseFloat(el.style.width || '0')),
        colors: new Set(parts.map((el) => el.style.backgroundColor)).size,
        total: document.querySelector('.sctw-total')?.textContent ?? null,
      };
    }) as { count: number; widths: number[]; colors: number; total: string | null };

    expect(bar.count).to.equal(4); // one per app, no "Other" under the default cap
    expect(bar.widths.reduce((a, b) => a + b, 0)).to.be.closeTo(100, 0.05);
    expect(bar.widths[0]).to.be.closeTo(50, 0.05); // 2h of 4h
    expect(bar.colors).to.equal(4); // each app keeps its own stable colour
    expect(bar.total).to.equal('4h 0m');
    // It is a bar, not a ring.
    const others = await browser.execute(
      () => document.querySelectorAll('.sctw-donut-seg').length,
    );
    expect(others).to.equal(0);
  });

  it('S-53: style `none` drops the chart, maxApps 0 drops the app list', async () => {
    // None + a list: the readout survives, the drawing does not.
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 3,
      config: { period: 'day', displayStyle: 'none' },
    }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('Visual Studio Code');
    let state = await browser.execute(() => ({
      charts: document.querySelectorAll('.sctw-hbar, .sctw-donut, .sctw-heat').length,
      // [S-56] filler rows pad the list to `maxApps`; only real entries count.
      legend: document.querySelectorAll('.sctw-legend-row:not(.sctw-legend-row--ghost)').length,
      total: document.querySelector('.sctw-total')?.textContent ?? null,
    })) as { charts: number; legend: number; total: string | null };
    expect(state.charts).to.equal(0);
    expect(state.legend).to.equal(4);
    expect(state.total).to.equal('4h 0m');

    // maxApps 0 on a doughnut: no list, but the ring still segments by app
    // (chartCap falls back to the default rather than one grey "Other").
    await resetAppState();
    await stubThemiaInvoke({
      'screentime_get_day': DAY_WITH_PATHS, 'get_file_icons_batch': ICONS_BY_PATH, '*': null,
    });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 4,
      config: { period: 'day', displayStyle: 'doughnut', maxApps: 0 },
    }]);
    await $('.sctw-donut-svg').waitForExist({ timeout: 8_000 });
    const capped = await browser.execute(() => ({
      segs: document.querySelectorAll('.sctw-donut-seg').length,
      legend: document.querySelectorAll('.sctw-legend-row').length,
    })) as { segs: number; legend: number };
    expect(capped.segs).to.equal(4); // all four apps, not one "Other"
    expect(capped.legend).to.equal(0);

    // Both off: just the readout.
    await resetAppState();
    await stubThemiaInvoke({
      'screentime_get_day': DAY_WITH_PATHS, 'get_file_icons_batch': ICONS_BY_PATH, '*': null,
    });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 3,
      config: { period: 'day', displayStyle: 'none', maxApps: 0 },
    }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('4h 0m');
    state = await browser.execute(() => ({
      charts: document.querySelectorAll('.sctw-hbar, .sctw-donut, .sctw-heat').length,
      legend: document.querySelectorAll('.sctw-legend-row').length,
      total: document.querySelector('.sctw-total')?.textContent ?? null,
    })) as { charts: number; legend: number; total: string | null };
    expect(state.charts).to.equal(0);
    expect(state.legend).to.equal(0);
    expect(state.total).to.equal('4h 0m');
  });

  it('S-57: a legacy metric set speaks through the settings that replaced it', async () => {
    // Drawing metric absent → no chart, but the list and readout stay.
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 3,
      config: { period: 'day', displayStyle: 'bars', visibleMetrics: ['apps'] },
    }]);
    await $('.sctw-legend').waitForExist({ timeout: 8_000 });
    let state = await browser.execute(() => ({
      bar: document.querySelectorAll('.sctw-hbar').length,
      legend: document.querySelectorAll('.sctw-legend-row:not(.sctw-legend-row--ghost)').length,
      total: document.querySelectorAll('.sctw-total').length,
    })) as { bar: number; legend: number; total: number };
    expect(state.bar).to.equal(0);
    expect(state.legend).to.equal(4);
    expect(state.total).to.equal(1); // the readout is not a chart
    // [S-52] a DAY period shows no per-day average: its total already is one day.
    const dayAvgs = await browser.execute(() => document.querySelectorAll('.sctw-app-avg').length);
    expect(dayAvgs).to.equal(0);

    // `apps` metric absent → no app list, chart intact.
    await resetAppState();
    await stubThemiaInvoke({
      'screentime_get_day': DAY_WITH_PATHS, 'get_file_icons_batch': ICONS_BY_PATH, '*': null,
    });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 3,
      config: { period: 'day', displayStyle: 'bars', visibleMetrics: ['graphic'] },
    }]);
    await $('.sctw-hbar').waitForExist({ timeout: 8_000 });
    state = await browser.execute(() => ({
      bar: document.querySelectorAll('.sctw-hbar').length,
      legend: document.querySelectorAll('.sctw-legend-row').length,
      total: document.querySelectorAll('.sctw-total').length,
    })) as { bar: number; legend: number; total: number };
    expect(state.bar).to.equal(1);
    expect(state.legend).to.equal(0);
    expect(state.total).to.equal(1);
  });

  it('S-56: the app list pads to maxApps with hidden filler rows', async () => {
    // 4 apps in the fixture, 10 asked for → 4 real rows + 6 invisible.
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 6,
      config: { period: 'day', displayStyle: 'doughnut', maxApps: 10 },
    }]);
    await $('.sctw-legend').waitForExist({ timeout: 8_000 });
    const rows = await browser.execute(() => {
      const ghosts = Array.from(
        document.querySelectorAll<HTMLElement>('.sctw-legend-row--ghost'),
      );
      return {
        total: document.querySelectorAll('.sctw-legend-row').length,
        real: document.querySelectorAll('.sctw-legend-row:not(.sctw-legend-row--ghost)').length,
        ghosts: ghosts.length,
        // Hidden, and therefore also out of hit-testing.
        hidden: ghosts.every((el) => getComputedStyle(el).visibility === 'hidden'),
        // Same height as a real row, so the chart above doesn't move.
        sameHeight: ghosts.every((el) => Math.abs(
          el.getBoundingClientRect().height
          - (document.querySelector('.sctw-legend-row:not(.sctw-legend-row--ghost)')
            ?.getBoundingClientRect().height ?? 0),
        ) < 0.5),
      };
    }) as { total: number; real: number; ghosts: number; hidden: boolean; sameHeight: boolean };
    expect(rows.real).to.equal(4);
    expect(rows.ghosts).to.equal(6);
    expect(rows.total).to.equal(10);
    expect(rows.hidden).to.equal(true);
    expect(rows.sameHeight).to.equal(true);
  });

  it('S-57/S-52: the `average` metric switches every per-day figure off', async () => {
    await resetAppState();
    await stubThemiaInvoke({ 'screentime_get_range': WEEK_RANGE, '*': null });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 5,
      config: {
        period: 'week', displayStyle: 'bars', visibleMetrics: ['switches'],
      },
    }]);
    await $('.sctw-week').waitForExist({ timeout: 8_000 });
    const off = await browser.execute(() => ({
      headline: document.querySelectorAll('.sctw-total-avg').length,
      rows: document.querySelectorAll('.sctw-app-avg').length,
      legend: document.querySelectorAll('.sctw-legend-row').length,
    })) as { headline: number; rows: number; legend: number };
    expect(off.headline).to.equal(0);
    expect(off.rows).to.equal(0);
    expect(off.legend).to.be.greaterThan(0); // the rest of the row is untouched
  });

  it('S-58/S-59: the percent metric and the daily goals render on the rows', async () => {
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 5,
      config: {
        period: 'day', displayStyle: 'doughnut',
        visibleMetrics: ['average', 'percent'],
        // 4h day: the 3h overall limit is exceeded, Code's 2h sits exactly on
        // its 2h limit (not over), Chrome's 1h is under a 90m limit.
        goalSecs: 3 * 3600,
        appGoals: { code: 2 * 3600, chrome: 90 * 60 },
      },
    }]);
    await $('.sctw-legend').waitForExist({ timeout: 8_000 });
    const shown = await browser.execute(() => {
      const row = (name: string) => Array.from(
        document.querySelectorAll<HTMLElement>('.sctw-legend-row'),
      ).find((r) => (r.querySelector('.sctw-app-name')?.textContent ?? '').trim() === name);
      const goalOf = (name: string) => {
        const el = row(name)?.querySelector<HTMLElement>('.sctw-app-goal');
        return el
          ? { text: el.textContent?.trim() ?? '', over: el.classList.contains('is-over') }
          : null;
      };
      return {
        pcts: Array.from(document.querySelectorAll('.sctw-app-pct'))
          .map((el) => (el.textContent ?? '').trim()),
        totalGoal: document.querySelector('.sctw-total-goal')?.textContent?.trim() ?? null,
        totalOver: !!document.querySelector('.sctw-total.is-over'),
        code: goalOf('Visual Studio Code'),
        chrome: goalOf('Google Chrome'),
        discord: goalOf('Discord'),
      };
    }) as {
      pcts: string[]; totalGoal: string | null; totalOver: boolean;
      code: { text: string; over: boolean } | null;
      chrome: { text: string; over: boolean } | null;
      discord: unknown;
    };

    // [S-58] 2h, 1h, 40m and 20m of a 4h day.
    expect(shown.pcts).to.deep.equal(['50%', '25%', '17%', '8%']);
    // [S-59] the overall limit, exceeded.
    expect(shown.totalGoal).to.equal('of 3h 0m');
    expect(shown.totalOver).to.equal(true);
    // Exactly at the limit is NOT over it; under it is not either.
    expect(shown.code).to.deep.equal({ text: 'of 2h 0m', over: false });
    expect(shown.chrome).to.deep.equal({ text: 'of 1h 30m', over: false });
    // An app without a limit shows none.
    expect(shown.discord).to.equal(null);
  });

  it('S-55: the `switches` metric shows the period\'s app-switch count', async () => {
    // Off by default: an existing widget gains nothing it did not ask for.
    await resetAppState();
    await stubThemiaInvoke({
      'screentime_get_day': { ...DAY_WITH_PATHS, switch_count: 42 },
      'get_file_icons_batch': ICONS_BY_PATH,
      '*': null,
    });
    await seedWidgets([{ type: 'screentime', col: 2, row: 2, colSpan: 3, rowSpan: 3 }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('4h 0m');
    const byDefault = await browser.execute(
      () => document.querySelectorAll('.sctw-switches').length,
    );
    expect(byDefault).to.equal(0);

    // Switched on, it reads the day's count.
    await resetAppState();
    await stubThemiaInvoke({
      'screentime_get_day': { ...DAY_WITH_PATHS, switch_count: 42 },
      'get_file_icons_batch': ICONS_BY_PATH,
      '*': null,
    });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 3, rowSpan: 3,
      config: { visibleMetrics: ['average', 'switches'] },
    }]);
    await $('.sctw-switches').waitForExist({ timeout: 8_000 });
    const day = await browser.execute(
      () => document.querySelector('.sctw-switches')?.textContent?.trim() ?? null,
    );
    expect(day).to.equal('42 switches');

    // A window sums its days: the fixture carries 3 per day over 7 days.
    await resetAppState();
    await stubThemiaInvoke({
      'screentime_get_range': WEEK_RANGE.map((d) => ({ ...d, switch_count: 3 })),
      '*': null,
    });
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 5,
      config: {
        period: 'week', displayStyle: 'bars',
        visibleMetrics: ['average', 'switches'],
      },
    }]);
    await $('.sctw-switches').waitForExist({ timeout: 8_000 });
    const window7 = await browser.execute(
      () => document.querySelector('.sctw-switches')?.textContent?.trim() ?? null,
    );
    expect(window7).to.equal('21 switches');
  });

  it('S-57: a config with both old metrics on keeps its chart AND its list', async () => {
    // Regression for the migration: ['total','apps'] must not read as
    // "everything off" now that neither key is a metric.
    await seedWidgets([{
      type: 'screentime', col: 2, row: 2, colSpan: 4, rowSpan: 4,
      config: { period: 'day', visibleMetrics: ['total', 'apps'] },
    }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('Visual Studio Code');
    const kept = await browser.execute(() => ({
      segs: document.querySelectorAll('.sctw-donut-seg').length,
      rows: document.querySelectorAll('.sctw-legend-row:not(.sctw-legend-row--ghost)').length,
      total: document.querySelector('.sctw-total')?.textContent ?? null,
    })) as { segs: number; rows: number; total: string | null };
    expect(kept.segs).to.equal(4); // the default style is the doughnut now
    expect(kept.rows).to.equal(4);
    expect(kept.total).to.equal('4h 0m');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
describe('screentime: config panel — period and style dropdowns (S-35c)', () => {
  /** Open the widget's config in its detached window (countdown-spec pattern). */
  async function openScreentimeConfig(): Promise<void> {
    await switchToMain();
    await browser.execute(() => {
      const w = document.querySelector('.widget') as HTMLElement | null;
      w?.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2 }));
    });
    await $('.ctx-menu').waitForDisplayed({ timeout: 2_000 });
    await browser.execute(() => {
      const items = Array.from(document.querySelectorAll('.ctx-menu .ctx-item')) as HTMLElement[];
      items.find((i) => (i.querySelector('.ctx-label')?.textContent ?? '').trim() === 'Configure')?.click();
    });
    await waitForConfigWindow('widget', 8_000);
    await $('.dropdown-trigger').waitForExist({ timeout: 4_000 });
  }

  /** Open the dropdown of the config row with this label, and list its options.
   *  Addressed by label, not position — the shared header surface adds its own
   *  dropdown (and its own "Style" row), which would shift any index; that one
   *  is excluded by its `header-mode-dropdown` class. */
  async function openDropdownOptions(rowLabel: string): Promise<string[]> {
    const found = await browser.execute((want: string) => {
      const rows = Array.from(document.querySelectorAll<HTMLElement>('.design-row'));
      const row = rows.find((r) =>
        (r.querySelector('.design-label')?.textContent ?? '').trim() === want
        && !!r.querySelector('.dropdown-trigger:not(.header-mode-dropdown)'));
      const trigger = row?.querySelector<HTMLElement>('.dropdown-trigger');
      trigger?.click();
      return !!trigger;
    }, rowLabel);
    expect(found, `no dropdown row labelled "${rowLabel}"`).to.equal(true);
    await $('.dropdown-panel').waitForDisplayed({ timeout: 2_000 });
    return await browser.execute(() => Array.from(
      document.querySelectorAll('.dropdown-panel .dropdown-item'),
    ).map((el) => (el.textContent ?? '').trim())) as string[];
  }

  async function pickOption(label: string): Promise<void> {
    await browser.execute((want: string) => {
      const items = Array.from(document.querySelectorAll<HTMLElement>('.dropdown-panel .dropdown-item'));
      items.find((el) => (el.textContent ?? '').trim() === want)?.click();
    }, label);
  }

  afterEach(async () => { await closeConfigModal(); });

  it('S-35c/S-46: the style options follow the period, and both fields persist', async () => {
    await resetAppState();
    await stubThemiaInvoke({
      'screentime_get_day': DAY_STATS,
      'screentime_get_range': [],
      '*': null,
    });
    await seedWidgets([{ type: 'screentime', col: 2, row: 2, colSpan: 3, rowSpan: 3 }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await openScreentimeConfig();

    // Period offers the three spans; the default is Day.
    expect(await openDropdownOptions('Period')).to.deep.equal(
      ['Day', 'Last 7 days', 'Last 30 days'],
    );
    await pickOption('Day');

    // While the period is Day, only the app-drawing styles are offered.
    expect(await openDropdownOptions('Style')).to.deep.equal(['Doughnut', 'Bars', 'None']);
    await pickOption('Doughnut');

    // Switching to a window period swaps the style options to the day-drawing
    // ones — the pairing lives in one place ([S-46] stylesFor).
    await openDropdownOptions('Period');
    await pickOption('Last 7 days');
    await settle(200, 'config write debounce');
    expect(await openDropdownOptions('Style')).to.deep.equal(['Bars', 'Heatmap', 'None']);
    await pickOption('Heatmap');
    await settle(300, 'layout save debounce');

    // Both fields are persisted, as a pair the widget can draw.
    await switchToMain();
    const cfg = await browser.execute(() => {
      const raw = localStorage.getItem('widget-layout') ?? '{}';
      const widgets = (JSON.parse(raw).widgets ?? []) as { type: string; config?: Record<string, unknown> }[];
      return widgets.find((w) => w.type === 'screentime')?.config ?? null;
    }) as Record<string, unknown> | null;
    expect(cfg).to.not.equal(null);
    expect(cfg!.period).to.equal('week');
    expect(cfg!.displayStyle).to.equal('heatmap');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
describe('screentime: day history navigation (S-11/S-12) and export privacy (S-34)', () => {
  /** Stub that records every screentime_get_day args object it receives. */
  async function stubRecordingArgs(): Promise<void> {
    await browser.execute((stats: unknown) => {
      const hook = (window as { __themiaInvoke?: { setInvoke: (fn: unknown) => void } }).__themiaInvoke;
      if (!hook?.setInvoke) throw new Error('__themiaInvoke.setInvoke not exposed');
      (window as unknown as { __sctArgs: unknown[] }).__sctArgs = [];
      hook.setInvoke(async (cmd: string, args: unknown) => {
        if (cmd === 'screentime_get_day') {
          (window as unknown as { __sctArgs: unknown[] }).__sctArgs.push(args ?? null);
          return stats;
        }
        return null;
      });
    }, DAY_STATS);
  }

  async function recordedArgs(): Promise<Record<string, unknown>[]> {
    return await browser.execute(
      () => (window as unknown as { __sctArgs: Record<string, unknown>[] }).__sctArgs,
    ) as Record<string, unknown>[];
  }

  function localDateMinusDays(days: number): string {
    const d = new Date();
    d.setDate(d.getDate() - days);
    const m = String(d.getMonth() + 1).padStart(2, '0');
    return `${d.getFullYear()}-${m}-${String(d.getDate()).padStart(2, '0')}`;
  }

  beforeEach(async () => {
    await resetAppState();
    await stubRecordingArgs();
    await seedWidgets([{ type: 'screentime', col: 2, row: 2, colSpan: 3, rowSpan: 3 }]);
    await $('.sctw-widget').waitForExist({ timeout: 4_000 });
    await waitForWidgetText('3h 24m');
  });

  it('S-11/S-12: prev steps to yesterday with a date arg; title and next-disabled follow', async () => {
    // Today: title "Today", next disabled, no date arg sent.
    const title0 = await $('.sctw-nav-title').getText();
    expect(title0).to.equal('Today');
    expect(await $('.sctw-nav-next').getAttribute('disabled')).to.exist;
    const argsBefore = await recordedArgs();
    expect(argsBefore.length).to.be.greaterThan(0);
    expect(argsBefore[argsBefore.length - 1]).to.deep.equal({});

    await $('.sctw-nav-prev').click();
    await browser.waitUntil(
      async () => (await $('.sctw-nav-title').getText()) === 'Yesterday',
      { timeout: 4_000, timeoutMsg: 'expected title to become "Yesterday"' },
    );
    const args = await recordedArgs();
    expect(args[args.length - 1]).to.deep.equal({ date: localDateMinusDays(1) });
    expect(await $('.sctw-nav-next').getAttribute('disabled')).to.not.exist;

    // The today dot (calendar pattern) resets to today.
    await $('.sctw-today-btn').click();
    await browser.waitUntil(
      async () => (await $('.sctw-nav-title').getText()) === 'Today',
      { timeout: 4_000, timeoutMsg: 'expected title to reset to "Today"' },
    );
  });

  it('S-34: navigating history never writes usage data into the persisted layout', async () => {
    await $('.sctw-nav-prev').click();
    await browser.waitUntil(
      async () => (await $('.sctw-nav-title').getText()) === 'Yesterday',
      { timeout: 4_000, timeoutMsg: 'expected title to become "Yesterday"' },
    );
    // Layout saves are debounced (~50ms); settle so a pending write can't
    // slip in after we read.
    await settle(300, 'layout save debounce');
    // The settings export serializes localStorage layouts; the screentime
    // widget's persisted config must stay free of usage data and dates.
    const layoutCheck = await browser.execute(() => {
      const raw = localStorage.getItem('widget-layout') ?? '{}';
      const widgets = (JSON.parse(raw).widgets ?? []) as { type: string; config?: Record<string, unknown> }[];
      const sct = widgets.find((w) => w.type === 'screentime');
      return {
        widgetFound: !!sct,
        configKeys: Object.keys(sct?.config ?? {}),
        rawHasUsage: /total_secs|Visual Studio Code|selectedDate|dayOffset|"date"/.test(raw),
      };
    }) as { widgetFound: boolean; configKeys: string[]; rawHasUsage: boolean };
    // Guard against a vacuous pass: the widget entry must exist in the layout.
    expect(layoutCheck.widgetFound, 'screentime entry missing from layout').to.equal(true);
    expect(layoutCheck.rawHasUsage).to.equal(false);
    const allowed = ['label', 'maxApps', 'visibleMetrics', 'period', 'displayStyle',
      'hiddenApps', 'goalSecs', 'appGoals', 'headerMode', 'hideHeader'];
    for (const k of layoutCheck.configKeys) {
      expect(allowed, `unexpected persisted config key "${k}"`).to.include(k);
    }
  });
});
