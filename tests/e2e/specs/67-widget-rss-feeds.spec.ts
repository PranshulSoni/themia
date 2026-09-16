/**
 * RSS widget: multiple feeds managed as connections.
 *
 * Covers:
 *  - [S-08] Widget config holds `feedIds` (managed feed connection ids), not a
 *           free-text URL.
 *  - [S-29] Feeds are provider:'rss' connections surfaced via email_list_accounts;
 *           only rss-provider connections feed the widget.
 *  - [S-30] A widget aggregates all selected feeds into one list, merged and
 *           sorted newest-first across feeds, capped at maxItems.
 *  - [S-31] Legacy single `feedUrl` still renders as a fallback when no feedIds
 *           are selected (pre-migration safety net).
 *  - [S-26] Each aggregated item shows a source row (host derived from its link),
 *           making the mixed-source list legible.
 *  - [S-32] The `fields` multi-select chooses which optional per-item fields
 *           render (source / preview / date); hiding `source` drops the host row.
 *  - [S-04] With 2+ feeds the header falls back to the joined feed names so it
 *           never collapses when no single feed title is available.
 *
 * Runs against the real app by overriding the demo-aware invoke wrapper
 * (`window.__themiaInvoke.setInvoke`) so feed accounts + feed XML are
 * deterministic and offline-safe. Mirrors the seedWidgets pattern used by
 * 65-widget-smoke / 66-widget-office.
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import { resetAppState, seedWidgets, switchToMain } from '../helpers/app.js';

/** Install a deterministic invoke that serves rss feed accounts + per-host XML. */
async function stubFeeds(): Promise<void> {
  await switchToMain();
  await browser.execute(() => {
    const hook = (window as any).__themiaInvoke;
    if (!hook?.setInvoke) throw new Error('__themiaInvoke.setInvoke not exposed');
    const feedXml = (title: string, host: string) =>
      `<?xml version="1.0" encoding="UTF-8"?><rss version="2.0"><channel><title>${title}</title>` +
      `<item><title>${title} story 1</title><link>https://${host}/1</link>` +
      `<pubDate>${new Date().toUTCString()}</pubDate><description>fresh</description></item>` +
      `<item><title>${title} story 2</title><link>https://${host}/2</link>` +
      `<pubDate>${new Date(Date.now() - 3_600_000).toUTCString()}</pubDate><description>older</description></item>` +
      `</channel></rss>`;
    hook.setInvoke(async (cmd: string, args: any) => {
      if (cmd === 'email_list_accounts') {
        return [
          { id: 'rss:verge', email: 'https://feeds.test/verge', display_name: 'The Verge',   nickname: null, provider: 'rss' },
          { id: 'rss:hn',    email: 'https://feeds.test/hn',    display_name: 'Hacker News',  nickname: null, provider: 'rss' },
          // A non-rss connection that must be ignored by the feed resolver.
          { id: 'imap:x',    email: 'me@example.com',           display_name: 'Mail',         nickname: null, provider: 'imap' },
        ];
      }
      if (cmd === 'http_get') {
        const url: string = args?.url ?? '';
        if (url.includes('verge')) return feedXml('Verge', 'theverge.com');
        if (url.includes('hn'))    return feedXml('HN', 'news.ycombinator.com');
        return feedXml('Other', 'example.com');
      }
      return null;
    });
  });
}

async function hostTexts(): Promise<string[]> {
  return browser.execute(() =>
    Array.from(document.querySelectorAll('.rss-widget__item-host')).map(e => e.textContent ?? ''),
  );
}

describe('rss multi-feed connections', () => {
  beforeEach(async () => { await resetAppState(); });

  // [S-30][S-29][S-26] Two selected feeds merge into one mixed-source list.
  it('[S-30] aggregates items from multiple selected feed connections', async () => {
    await stubFeeds();
    await seedWidgets([{
      type: 'rss', colSpan: 4, rowSpan: 5,
      config: { feedIds: ['rss:verge', 'rss:hn'], maxItems: 10, showSource: true },
    }]);

    await $('.rss-widget__item').waitForDisplayed({ timeout: 8_000 });

    const hosts = await hostTexts();
    // 2 feeds × 2 items each.
    expect(hosts.length).to.equal(4);
    // Mixed sources from both feeds (S-26 source rows + S-30 aggregation).
    expect(new Set(hosts).size).to.equal(2);
    expect(hosts).to.include('theverge.com');
    expect(hosts).to.include('news.ycombinator.com');
  });

  // [S-30] Newest-first ordering across feeds; maxItems caps the merged list.
  it('[S-30] merges newest-first and respects maxItems', async () => {
    await stubFeeds();
    await seedWidgets([{
      type: 'rss', colSpan: 4, rowSpan: 5,
      config: { feedIds: ['rss:verge', 'rss:hn'], maxItems: 2, showSource: true },
    }]);

    await $('.rss-widget__item').waitForDisplayed({ timeout: 8_000 });

    const titles = await browser.execute(() =>
      Array.from(document.querySelectorAll('.rss-widget__item-title')).map(e => e.textContent ?? ''),
    );
    // Capped at 2; both are the freshest ("story 1") items, one per feed.
    expect(titles.length).to.equal(2);
    expect(titles.every(t => t.includes('story 1'))).to.equal(true);
  });

  // [S-32] The `fields` selection hides the per-item source row when `source`
  // is not among the chosen fields (items still render).
  it('[S-32] hiding the source field drops the host row', async () => {
    await stubFeeds();
    await seedWidgets([{
      type: 'rss', colSpan: 4, rowSpan: 5,
      config: { feedIds: ['rss:verge', 'rss:hn'], maxItems: 10, fields: ['preview', 'date'] },
    }]);

    await $('.rss-widget__item').waitForDisplayed({ timeout: 8_000 });

    const counts = await browser.execute(() => ({
      items: document.querySelectorAll('.rss-widget__item').length,
      hosts: document.querySelectorAll('.rss-widget__item-host').length,
      snippets: document.querySelectorAll('.rss-widget__item-snippet').length,
    }));
    expect(counts.items).to.equal(4);
    expect(counts.hosts).to.equal(0);     // source field hidden
    expect(counts.snippets).to.be.greaterThan(0); // preview still shown
  });

  // [S-04] Two feeds → header stays visible, showing the joined feed names.
  it('[S-04] multi-feed header falls back to joined feed names', async () => {
    await stubFeeds();
    await seedWidgets([{
      type: 'rss', colSpan: 4, rowSpan: 5,
      config: { feedIds: ['rss:verge', 'rss:hn'], maxItems: 10 },
    }]);

    await $('.rss-widget__item').waitForDisplayed({ timeout: 8_000 });

    const header = await $('.rss-widget .widget-header .wh-text');
    await header.waitForDisplayed({ timeout: 4_000 });
    const text = await header.getText();
    expect(text).to.include('The Verge');
    expect(text).to.include('Hacker News');
  });

  // [S-31] Legacy single feedUrl still renders when no feedIds are selected.
  it('[S-31] legacy feedUrl renders as a single-feed fallback', async () => {
    await stubFeeds();
    await seedWidgets([{
      type: 'rss', colSpan: 4, rowSpan: 5,
      config: { feedUrl: 'https://feeds.test/verge', maxItems: 10, showSource: true },
    }]);

    await $('.rss-widget__item').waitForDisplayed({ timeout: 8_000 });

    const hosts = await hostTexts();
    expect(hosts.length).to.equal(2);
    expect(new Set(hosts)).to.deep.equal(new Set(['theverge.com']));
  });
});
