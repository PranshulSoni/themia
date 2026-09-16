// @vitest-environment jsdom
//
// Issue #81: "Live RSS link fail to update for 2 days ... The refresh button
// never respond."
//
// The refresh WAS firing. Two things hid that from the user, and this file
// covers the half that lives in the feed layer: every distinct fetch failure
// (403 bot challenge, 404, SSRF block, timeout) was swallowed by a bare
// `catch {}` and rethrown as the flat sentence "No items found in feed" - which
// then went to the log too, so neither the widget nor a bug report could say
// what had actually happened.
//
// The other half (the failure being invisible while cached items render) is
// covered in src/createCachedFetch.test.ts.

import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { aggregateFeeds, parseFeed, RSS_ACCEPT, type FetchedFeed } from './feed.ts';

const RSS_XML = `<?xml version="1.0"?>
<rss version="2.0"><channel>
  <title>Chan</title>
  <item><title>Old</title><link>https://e.com/1</link><pubDate>Mon, 01 Jan 2024 00:00:00 GMT</pubDate><description>&lt;b&gt;a&lt;/b&gt;</description></item>
  <item><title>New</title><link>https://e.com/2</link><pubDate>Wed, 03 Jan 2024 00:00:00 GMT</pubDate><description>b</description></item>
</channel></rss>`;

const ok = (title: string, xml = RSS_XML): FetchedFeed => ({ title, data: parseFeed(xml, 50) });
const bad = (title: string, error: string): FetchedFeed => ({ title, data: null, error });

describe('issue_81 a failed feed reports why', () => {
  it('S-33_ a single failing feed throws its REAL reason, not "No items found in feed"', () => {
    expect(() => aggregateFeeds([bad('Inoreader', 'HTTP 403 Forbidden')], 50))
      .toThrowError('Inoreader: HTTP 403 Forbidden');
  });

  it('S-33_ every failing feed is named, not just the first', () => {
    expect(() => aggregateFeeds([bad('A', 'HTTP 404 Not Found'), bad('B', 'Request failed: timeout')], 50))
      .toThrowError('A: HTTP 404 Not Found; B: Request failed: timeout');
  });

  it('S-33_ an unnamed feed still reports its reason', () => {
    expect(() => aggregateFeeds([bad('', 'HTTP 429 Too Many Requests')], 50))
      .toThrowError('HTTP 429 Too Many Requests');
  });

  it('S-33_ a genuinely empty feed still says so - "empty" and "blocked" stay distinguishable', () => {
    const empty: FetchedFeed = { title: 'Quiet', data: { title: 'Quiet', items: [] } };
    expect(() => aggregateFeeds([empty], 50)).toThrowError('No items found in feed');
  });
});

describe('issue_81 a partly failing feed set stays honest [S-34]', () => {
  it('S-34_ items from the healthy feed render, and the failure is recorded', () => {
    const out = aggregateFeeds([ok('Good'), bad('Bad', 'HTTP 500 Internal Server Error')], 50);
    expect(out.items).toHaveLength(2);
    expect(out.failures).toEqual(['Bad: HTTP 500 Internal Server Error']);
  });

  it('S-34_ a clean fetch records no failures', () => {
    expect(aggregateFeeds([ok('Good')], 50).failures).toEqual([]);
  });
});

describe('aggregateFeeds preserves the pre-existing merge rules', () => {
  it('S-30_ newest first, undated last, capped at maxItems', () => {
    const undated: FetchedFeed = {
      title: 'U',
      data: { title: 'U', items: [{ title: 'Undated', link: 'https://e.com/3', date: null, snippet: '' }] },
    };
    const out = aggregateFeeds([ok('Good'), undated], 50);
    expect(out.items.map(i => i.title)).toEqual(['New', 'Old', 'Undated']);
    expect(aggregateFeeds([ok('Good'), undated], 2).items).toHaveLength(2);
  });

  it('S-04_ a single feed keeps the connection name over the parsed <title>', () => {
    expect(aggregateFeeds([ok('My Rename')], 50).title).toBe('My Rename');
    expect(aggregateFeeds([{ title: '', data: parseFeed(RSS_XML, 50) }], 50).title).toBe('Chan');
  });

  it('S-04_ several feeds resolve no single title', () => {
    expect(aggregateFeeds([ok('A'), ok('B')], 50).title).toBe('');
  });
});

describe('parseFeed', () => {
  it('S-17_ reads RSS 2.0 and strips HTML from the snippet', () => {
    const out = parseFeed(RSS_XML, 50);
    expect(out.title).toBe('Chan');
    expect(out.items[0]).toMatchObject({ title: 'Old', link: 'https://e.com/1', snippet: 'a' });
  });

  it('S-18_ reads Atom, preferring updated over published', () => {
    const atom = `<?xml version="1.0"?>
      <feed xmlns="http://www.w3.org/2005/Atom">
        <title>Atom Feed</title>
        <entry>
          <title>Entry</title>
          <link href="https://e.com/a"/>
          <published>2024-01-01T00:00:00Z</published>
          <updated>2024-02-01T00:00:00Z</updated>
          <summary>sum</summary>
        </entry>
      </feed>`;
    const out = parseFeed(atom, 50);
    expect(out.title).toBe('Atom Feed');
    expect(out.items[0]).toMatchObject({
      title: 'Entry', link: 'https://e.com/a', date: '2024-02-01T00:00:00Z', snippet: 'sum',
    });
  });

  it('S-09_ maxItems caps what is parsed', () => {
    expect(parseFeed(RSS_XML, 1).items).toHaveLength(1);
  });

  it('S-03_ a non-feed document (e.g. an HTML challenge page) yields no items', () => {
    expect(parseFeed('<html><body><h1>Just a moment…</h1></body></html>', 50).items).toEqual([]);
  });
});

describe('content negotiation is shared with the add-a-feed path', () => {
  it('S-35_ RSS_ACCEPT matches the Rust RSS_ACCEPT byte for byte', () => {
    // The add-a-feed validation lives in Rust and the refresh lives here, so the
    // constant is unavoidably written twice. Read the other copy and compare,
    // rather than asserting this one against a literal of itself: drift between
    // the two is exactly how a feed validates on add and then never refreshes
    // (#81), and a self-comparison could not see it.
    const rust = readFileSync(
      resolve(__dirname, '../../office/email/src/lib.rs'),
      'utf-8',
    );
    const m = rust.match(/const RSS_ACCEPT: &str\s*=\s*\n?\s*"([^"]+)"/);
    expect(m, 'RSS_ACCEPT not found in widgets/office/email/src/lib.rs').not.toBeNull();
    expect(RSS_ACCEPT).toBe(m![1]);
  });
});
