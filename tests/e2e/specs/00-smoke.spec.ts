/**
 * Smoke: verify webdriver can see the app DOM at all.
 * Prints what the session is attached to so we can diagnose boot failures.
 */
import { browser } from '@wdio/globals';

describe('smoke', () => {
  it('reports title, url, and first 300 chars of body', async () => {
    const title = await browser.getTitle();
    const url = await browser.getUrl();
    const info = await browser.execute(() => ({
      readyState: document.readyState,
      bodyLen: document.body?.innerHTML?.length ?? 0,
      bodySnippet: (document.body?.innerHTML ?? '').slice(0, 300),
      hasGrid: !!document.querySelector('.grid'),
      rootChildren: Array.from(document.body?.children ?? []).map(c => `${c.tagName}.${(c as HTMLElement).className}`),
    }));
    // eslint-disable-next-line no-console
    console.log('SMOKE ►', JSON.stringify({ title, url, ...info }, null, 2));
  });
});
