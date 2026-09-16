/**
 * Stocks widget: session indicator and extended-hours tracking.
 *
 * Covers:
 *  - [S-32] When `dimWhenClosed` (default true) and the STOCK's session is
 *           closed, a "Market closed" badge with a Moon icon is shown. Crypto
 *           is never marked. With `dimWhenClosed:false` no marker shows at all.
 *           One symbol per widget; there is no list view.
 *  - [S-33] The phase comes from `meta.currentTradingPeriod`; a payload without
 *           one is unknown and shows no badge (#100).
 *  - [S-35] Inside a pre/post window with `showExtendedHours` on, the badge
 *           names the live session and the last extended print is shown. A
 *           symbol that doesn't trade extended hours stays "Market closed".
 *
 * Runs against the real app by overriding the demo-aware invoke wrapper
 * (`window.__themiaInvoke.setInvoke`) so the Yahoo/CoinGecko responses are
 * deterministic and offline-safe. Mirrors the seedWidgets + http_get stub
 * pattern used by 67-widget-rss-feeds.
 *
 * The Yahoo stub mirrors a real v8 chart payload, which notably carries NO
 * `marketState` field — the bug in #100 was reading exactly that. Windows are
 * built relative to the run's own clock so the intended phase is current
 * whenever and wherever the suite runs.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { resetAppState, seedWidgets, switchToMain } from '../helpers/app.js';

type Phase = 'open' | 'pre' | 'post' | 'closed' | 'none';

interface StubSymbol {
  symbol: string;
  /** Which session the exchange should be in right now. 'none' omits the
   *  schedule entirely (the "unknown" case). */
  phase: Phase;
  /** Yahoo's `hasPrePostMarketData`; defaults to true. */
  prePost?: boolean;
}

/** Install a deterministic invoke serving Yahoo chart + CoinGecko data. */
async function stubMarket(symbols: StubSymbol[]): Promise<void> {
  await switchToMain();
  await browser.execute((specs: StubSymbol[]) => {
    const hook = (window as any).__themiaInvoke;
    if (!hook?.setInvoke) throw new Error('__themiaInvoke.setInvoke not exposed');

    const nowSec = Math.floor(Date.now() / 1000);
    const REGULAR = 23_400; // 6.5h
    const PRE = 19_800;     // 5.5h
    const POST = 14_400;    // 4h

    /** Regular-session window placed so `phase` is the current one. */
    const regularFor = (phase: Phase) => {
      if (phase === 'open') return { start: nowSec - 3_600, end: nowSec + 3_600 };
      if (phase === 'pre') return { start: nowSec + 1_800, end: nowSec + 1_800 + REGULAR };
      if (phase === 'post') return { start: nowSec - 7_200, end: nowSec - 1_800 };
      // closed: yesterday's finished session.
      return { start: nowSec - 86_400 - REGULAR, end: nowSec - 86_400 };
    };

    const yahoo = (symbol: string) => {
      const spec = specs.find((s) => s.symbol === symbol)
        ?? { symbol, phase: 'open' as Phase, prePost: true };
      const regular = regularFor(spec.phase);
      const pre = { start: regular.start - PRE, end: regular.start };
      const post = { start: regular.end, end: regular.end + POST };

      // Two bars per window, and never a bar in the future.
      const samples: [number, number][] = [
        [pre.start + 300, 148], [pre.start + 600, 148.5],
        [regular.start + 300, 149], [regular.end - 300, 150],
        [post.start + 300, 151], [post.start + 600, 151.5],
      ];
      const visible = samples.filter(([t]) => t <= nowSec);

      return JSON.stringify({
        chart: { result: [{
          meta: {
            symbol,
            currency: 'USD',
            regularMarketPrice: 150,
            chartPreviousClose: 148,
            previousClose: 148,
            exchangeTimezoneName: 'America/New_York',
            hasPrePostMarketData: spec.prePost !== false,
            ...(spec.phase === 'none' ? {} : { currentTradingPeriod: { pre, regular, post } }),
          },
          timestamp: visible.map(([t]) => t),
          indicators: { quote: [{ close: visible.map(([, c]) => c) }] },
        }] },
      });
    };

    hook.setInvoke(async (cmd: string, args: any) => {
      if (cmd === 'http_get') {
        const url: string = args?.url ?? '';
        const ym = url.match(/finance\/chart\/([^?]+)/);
        if (ym) return yahoo(decodeURIComponent(ym[1]));
        if (url.includes('coingecko') && url.includes('market_chart')) {
          return JSON.stringify({ prices: [[0, 49000], [1, 50000]] });
        }
        if (url.includes('coingecko') && url.includes('simple/price')) {
          return JSON.stringify({ bitcoin: { usd: 50000, usd_24h_change: 2.5 } });
        }
        return JSON.stringify({});
      }
      return null;
    });
  }, symbols);
}

/** Count visible session badges across all stocks widgets. */
async function badgeCount(): Promise<number> {
  return browser.execute(() =>
    document.querySelectorAll('.stocks-widget__closed-badge').length,
  );
}

const SINGLE = (
  entry: Record<string, unknown>,
  config: Record<string, unknown> = {},
) => ({
  type: 'stocks' as const, colSpan: 4, rowSpan: 5,
  config: { symbols: [entry], showSparkline: true, ...config },
});

const STOCK = (symbol: string, name: string) => ({ symbol, type: 'stock', name });

describe('stocks session indicator', () => {
  beforeEach(async () => { await resetAppState(); });

  // [S-32] A closed stock shows the "Market closed" badge; nothing is dimmed.
  it('[S-32] closed stock shows the market-closed badge, never dims', async () => {
    await stubMarket([{ symbol: 'AAPL', phase: 'closed' }]);
    await seedWidgets([SINGLE(STOCK('AAPL', 'Apple'))]);

    const badge = await $('.stocks-widget__closed-badge');
    await badge.waitForDisplayed({ timeout: 8_000 });
    expect((await badge.getText()).toLowerCase()).to.include('closed');

    // No opacity dimming anywhere (the old behavior is gone).
    const dimmed = await browser.execute(() =>
      document.querySelectorAll('.stocks-widget--dimmed').length,
    );
    expect(dimmed, 'no dimmed widget').to.equal(0);
  });

  // [S-32][S-33] The #100 regression: the payload has no `marketState`, so the
  // old code called every stock closed while the market was trading.
  it('[S-32] open stock and crypto show no badge', async () => {
    await stubMarket([{ symbol: 'MSFT', phase: 'open' }]);
    await seedWidgets([
      SINGLE(STOCK('MSFT', 'Microsoft')),
      SINGLE({ symbol: 'bitcoin', type: 'crypto', name: 'Bitcoin' }),
    ]);

    await $('.stocks-single').waitForDisplayed({ timeout: 8_000 });
    await browser.waitUntil(async () => (await badgeCount()) === 0, { timeout: 8_000 });
    expect(await badgeCount(), 'open stock + crypto → no badge').to.equal(0);
  });

  // [S-33] No trading window at all → unknown, which must not render as closed.
  it('[S-33] stock with no trading window shows no badge', async () => {
    await stubMarket([{ symbol: 'TSLA', phase: 'none' }]);
    await seedWidgets([SINGLE(STOCK('TSLA', 'Tesla'))]);

    await $('.stocks-single').waitForDisplayed({ timeout: 8_000 });
    await browser.waitUntil(async () => (await badgeCount()) === 0, { timeout: 8_000 });
    expect(await badgeCount(), 'unknown session → no badge').to.equal(0);
  });

  // [S-32] dimWhenClosed:false suppresses the indicator entirely.
  it('[S-32] no indicator when dimWhenClosed is off', async () => {
    await stubMarket([{ symbol: 'AAPL', phase: 'closed' }]);
    await seedWidgets([SINGLE(STOCK('AAPL', 'Apple'), { dimWhenClosed: false })]);

    await $('.stocks-single').waitForDisplayed({ timeout: 8_000 });
    await browser.waitUntil(async () => (await badgeCount()) === 0, { timeout: 8_000 });
    expect(await badgeCount(), 'indicator suppressed when toggle off').to.equal(0);
  });

  // [S-35] After-hours trading running → the badge says so and the last
  // extended print is shown, rather than a flat "Market closed".
  it('[S-35] after-hours session shows an active badge and the extended print', async () => {
    await stubMarket([{ symbol: 'AAPL', phase: 'post' }]);
    await seedWidgets([SINGLE(STOCK('AAPL', 'Apple'), { showExtendedHours: true })]);

    const badge = await $('.stocks-widget__closed-badge');
    await badge.waitForDisplayed({ timeout: 8_000 });
    expect((await badge.getText()).toLowerCase()).to.include('after hours');

    const active = await browser.execute(() =>
      document.querySelectorAll('.stocks-widget__closed-badge--active').length,
    );
    expect(active, 'extended session reads as active').to.equal(1);

    const ext = await $('.stocks-single__extended');
    await ext.waitForDisplayed({ timeout: 8_000 });
    // Last post bar is 151.50 against a 150.00 regular close.
    expect(await ext.getText()).to.include('151.50');
    expect(await ext.getText()).to.include('+1.00%');
  });

  // [S-35] Pre-market is named too, and measured off the previous close.
  it('[S-35] pre-market session shows the pre-market badge', async () => {
    await stubMarket([{ symbol: 'AAPL', phase: 'pre' }]);
    await seedWidgets([SINGLE(STOCK('AAPL', 'Apple'), { showExtendedHours: true })]);

    const badge = await $('.stocks-widget__closed-badge');
    await badge.waitForDisplayed({ timeout: 8_000 });
    expect((await badge.getText()).toLowerCase()).to.include('pre-market');
  });

  // [S-35] Extended hours off → the regular session is closed, and that is all
  // the badge may claim.
  it('[S-35] after-hours window reads as closed when the toggle is off', async () => {
    await stubMarket([{ symbol: 'AAPL', phase: 'post' }]);
    await seedWidgets([SINGLE(STOCK('AAPL', 'Apple'))]);

    const badge = await $('.stocks-widget__closed-badge');
    await badge.waitForDisplayed({ timeout: 8_000 });
    expect((await badge.getText()).toLowerCase()).to.include('closed');
    const ext = await browser.execute(() =>
      document.querySelectorAll('.stocks-single__extended').length,
    );
    expect(ext, 'no extended print without the toggle').to.equal(0);
  });

  // [S-35] An instrument without extended trading (index, most non-US venues)
  // is closed after the bell even with the toggle on.
  it('[S-35] symbol without pre/post data stays closed inside the post window', async () => {
    await stubMarket([{ symbol: 'AAPL', phase: 'post', prePost: false }]);
    await seedWidgets([SINGLE(STOCK('AAPL', 'Apple'), { showExtendedHours: true })]);

    const badge = await $('.stocks-widget__closed-badge');
    await badge.waitForDisplayed({ timeout: 8_000 });
    expect((await badge.getText()).toLowerCase()).to.include('closed');
    const active = await browser.execute(() =>
      document.querySelectorAll('.stocks-widget__closed-badge--active').length,
    );
    expect(active, 'no active badge without extended data').to.equal(0);
  });
});
