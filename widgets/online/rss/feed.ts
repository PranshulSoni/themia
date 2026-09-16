// Feed parsing and multi-feed aggregation for the RSS widget.
//
// Extracted from Rss.tsx so the part that decides what the user is told when a
// fetch goes wrong is testable without mounting a widget. The component keeps
// rendering; this file keeps the rules.

export interface FeedItem {
  title: string;
  link: string;
  date: string | null;
  snippet: string;
}

export interface FeedData {
  title: string;
  items: FeedItem[];
  /**
   * Human-readable reason per feed that failed this fetch, e.g.
   * "Tech News: HTTP 403 Forbidden". Empty on a clean fetch. Persisted with the
   * rest of the entry, so a cold start still shows that the last refresh was
   * partial instead of silently presenting a short list as complete [S-34].
   */
  failures?: string[];
}

/**
 * Content negotiation for a feed fetch. Kept byte-identical to `RSS_ACCEPT` in
 * `widgets/office/email/src/lib.rs`, which validates a feed when it is added:
 * the two must ask the host the same question, or a feed can validate and then
 * never refresh (#81).
 */
export const RSS_ACCEPT =
  'application/rss+xml, application/atom+xml, application/xml;q=0.9, text/xml;q=0.9, */*;q=0.8';

function stripHtml(html: string): string {
  const tmp = document.createElement('div');
  tmp.innerHTML = html;
  return tmp.textContent || tmp.innerText || '';
}

export function parseFeed(xml: string, maxItems: number): FeedData {
  const doc = new DOMParser().parseFromString(xml, 'text/xml');

  const channel = doc.querySelector('channel');
  if (channel) {
    const feedTitle = channel.querySelector(':scope > title')?.textContent ?? '';
    const rssItems = [...channel.querySelectorAll('item')].slice(0, maxItems);
    return {
      title: feedTitle,
      items: rssItems.map(item => ({
        title: item.querySelector('title')?.textContent ?? '',
        link: item.querySelector('link')?.textContent ?? '',
        date: item.querySelector('pubDate')?.textContent ?? null,
        snippet: stripHtml(item.querySelector('description')?.textContent ?? ''),
      })),
    };
  }

  const feed = doc.querySelector('feed');
  if (feed) {
    const feedTitle = feed.querySelector(':scope > title')?.textContent ?? '';
    const entries = [...feed.querySelectorAll('entry')].slice(0, maxItems);
    return {
      title: feedTitle,
      items: entries.map(entry => ({
        title: entry.querySelector('title')?.textContent ?? '',
        link: entry.querySelector('link')?.getAttribute('href') ?? '',
        date: entry.querySelector('updated')?.textContent ?? entry.querySelector('published')?.textContent ?? null,
        snippet: stripHtml(entry.querySelector('summary')?.textContent ?? entry.querySelector('content')?.textContent ?? ''),
      })),
    };
  }

  return { title: '', items: [] };
}

export interface FetchedFeed {
  /** Display name for the feed, used to attribute a failure to its source. */
  title: string;
  /** Parsed payload, or null when this feed's fetch/parse failed. */
  data: FeedData | null;
  /** The real reason this feed failed: HTTP status, block reason, timeout. */
  error?: string;
}

/**
 * Merge one fetch's per-feed results into what the widget renders [S-30].
 *
 * Throws when there is nothing to show, and the thrown message carries the REAL
 * reasons [S-33]. This used to swallow every per-feed error and throw a flat
 * "No items found in feed", which is what made #81 undiagnosable: a 403, a DNS
 * failure, an SSRF block and a genuinely empty feed all produced the same
 * sentence, and the log file recorded only that sentence.
 */
export function aggregateFeeds(fetched: FetchedFeed[], maxItems: number): FeedData {
  const failures = fetched
    .filter(f => f.error)
    .map(f => (f.title ? `${f.title}: ${f.error}` : f.error!));

  const merged = fetched.flatMap(f => f.data?.items ?? []);

  if (!merged.length) {
    // Every reason, not the first: with several feeds selected, "the second one
    // is 404" is the answer and dropping it would hide it.
    if (failures.length) throw new Error(failures.join('; '));
    throw new Error('No items found in feed');
  }

  // Newest first across all feeds; undated items sink to the bottom.
  merged.sort((a, b) => {
    const ta = a.date ? new Date(a.date).getTime() : 0;
    const tb = b.date ? new Date(b.date).getTime() : 0;
    return tb - ta;
  });

  // Header title: the single feed's own title when exactly one is selected.
  // The connection's name wins over the parsed <title> so a user's rename sticks.
  const first = fetched[0];
  const title = fetched.length === 1 ? first.title || first.data?.title || '' : '';
  return { title, items: merged.slice(0, maxItems), failures };
}
