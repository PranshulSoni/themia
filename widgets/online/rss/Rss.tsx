import { createMemo, createSignal, createEffect, onMount, onCleanup, For } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { createCachedFetch } from '../../../src/createCachedFetch';
import { createManualRefresh } from '../../office/calendar/manualRefresh.ts';
import { Show, Switch, Match } from 'solid-js/web';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { accountRevision } from '../../../src/accountStore.ts';
import { onTransient } from '../../../src/sync.ts';
import { Rss as RssIcon, Globe, RefreshCw, TriangleAlert } from 'lucide-solid';
import { t } from '../../../src/i18n.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import { resolveMetrics } from '../../../src/widgets/metricSet.ts';
import { RSS_FIELD_DEFS } from './fields.ts';
import { parseFeed, aggregateFeeds, RSS_ACCEPT, type FeedItem, type FeedData, type FetchedFeed } from './feed.ts';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './Rss.css';

interface RssProps {
  headerMode?: HeaderMode;
  /** Managed RSS feed connection ids (provider 'rss'). Aggregated together. */
  feedIds?: string[];
  /** Legacy single-feed URL: kept as a fallback until migration runs. */
  feedUrl?: string;
  refreshMinutes?: number;
  maxItems?: number;
  /** Visible per-item fields (RSS_FIELD_DEFS keys): 'source' | 'preview' | 'date'. */
  fields?: string[];
  /** Legacy per-item toggles: honored when `fields` is absent (pre-migration). */
  showSnippet?: boolean;
  showSource?: boolean;
  label?: string;
  _refresh?: number;
}

interface ResolvedFeed {
  url: string;
  title: string;
}

interface FeedAccount {
  id: string;
  email: string;
  display_name: string;
  nickname?: string | null;
  provider: string;
}

function sourceHost(link: string): string | null {
  try {
    return new URL(link).hostname.replace(/^www\./, '');
  } catch {
    return null;
  }
}

function faviconUrl(link: string): string | null {
  try {
    const hostname = new URL(link).hostname;
    return `https://www.google.com/s2/favicons?domain=${hostname}&sz=64`;
  } catch {
    return null;
  }
}

function timeAgo(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  if (diff < 0) return 'just now';
  if (diff < 60_000) return 'just now';
  if (diff < 3_600_000) return Math.floor(diff / 60_000) + 'm ago';
  if (diff < 86_400_000) return Math.floor(diff / 3_600_000) + 'h ago';
  if (diff < 7 * 86_400_000) return Math.floor(diff / 86_400_000) + 'd ago';
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
}

export default function Rss(props: RssProps) {
  const maxItems = () => props.maxItems ?? 20;

  // Which per-item fields are visible. Prefer the unified `fields` array; when a
  // pre-migration widget only has the legacy `showSnippet`/`showSource` booleans,
  // derive the set from those (date was always shown before the field picker).
  const visibleFields = createMemo<string[]>(() => {
    if (props.fields) return resolveMetrics(props.fields, RSS_FIELD_DEFS).visible;
    const legacy: string[] = [];
    if (props.showSource ?? true) legacy.push('source');
    if (props.showSnippet ?? true) legacy.push('preview');
    legacy.push('date');
    return legacy;
  });
  const showSource = () => visibleFields().includes('source');
  const showSnippet = () => visibleFields().includes('preview');
  const showDate = () => visibleFields().includes('date');
  const [failedIcons, setFailedIcons] = createSignal<Set<string>>(new Set());
  const refreshMs = createMemo(() => Math.max(1, props.refreshMinutes ?? 15) * 60_000);

  // Managed RSS feed connections (provider 'rss'). Loaded via the demo-aware
  // invoke so it resolves correctly both live and in demo mode. Kept fresh as
  // feeds are added/renamed/removed (revision bump + cross-window broadcast).
  const [feedAccounts, setFeedAccounts] = createSignal<FeedAccount[]>([]);
  async function reloadFeedAccounts(): Promise<void> {
    try {
      const all = await invoke('email_list_accounts');
      setFeedAccounts(all.filter(a => a.provider === 'rss'));
    } catch {
      setFeedAccounts([]);
    }
  }
  createEffect(() => { accountRevision(); void reloadFeedAccounts(); });
  onMount(() => {
    const off = onTransient('accounts-changed', () => { void reloadFeedAccounts(); });
    onCleanup(off);
  });

  // Resolve the widget's selected feed ids to feed URLs (+ display titles).
  // Falls back to the legacy single `feedUrl` (or the demo feed) when nothing
  // is selected, so existing widgets keep working until migration runs.
  const feeds = createMemo<ResolvedFeed[]>(() => {
    const rssAccounts = feedAccounts();
    const ids = props.feedIds ?? [];
    const resolved: ResolvedFeed[] = ids
      .map(id => rssAccounts.find(a => a.id === id))
      .filter((a): a is NonNullable<typeof a> => !!a)
      .map(a => ({ url: a.email, title: a.nickname || a.display_name }));

    if (resolved.length === 0) {
      const legacy = props.feedUrl?.trim();
      if (legacy) resolved.push({ url: legacy, title: '' });
      else if (isDemo()) resolved.push({ url: 'demo://feed', title: 'Demo Feed' });
    }
    return resolved;
  });

  const cache = createCachedFetch<FeedData>({
    namespace: 'rss',
    key: () => {
      const fs = feeds();
      if (!fs.length) return null;
      return fs.map(f => f.url).sort().join('|') + '::' + maxItems();
    },
    staleMs: refreshMs,
    fetcher: async (_key, signal) => {
      const fs = feeds();
      const per: FetchedFeed[] = await Promise.all(fs.map(async (f): Promise<FetchedFeed> => {
        try {
          const text = await invoke('http_get', { url: f.url, accept: RSS_ACCEPT });
          return { title: f.title, data: parseFeed(text, maxItems()) };
        } catch (e: unknown) {
          // Keep the REAL reason (HTTP status, SSRF block, timeout). Swallowing
          // it here is what made #81 undiagnosable: every distinct failure was
          // reported to the user, and written to the log, as the same flat
          // "No items found in feed" [S-33].
          const msg = e instanceof Error ? e.message : String(e);
          return { title: f.title, data: null, error: msg.replace(/^Error:\s*/, '') };
        }
      }));

      // A Tauri invoke can't be server-cancelled; if this fetch was superseded
      // while the requests were in flight, drop the result so it doesn't parse
      // or write into the (now stale) shared state.
      if (signal.aborted) throw new DOMException('aborted', 'AbortError');

      return aggregateFeeds(per, maxItems());
    },
  });

  createPolling(() => { void cache.refresh(true); }, refreshMs, 'rss');

  // [S-15] Context-menu "Refresh" bumps the `_refresh` config token; force a
  // re-fetch past the stale window. The mount run is skipped (the cache's own
  // effect covers first load, and a token persisted from a prior session must
  // not re-trigger a fetch on start).
  createManualRefresh(() => props._refresh, () => { void cache.refresh(true); });

  const feedTitle = createMemo(() => cache.data()?.title ?? '');
  const items = createMemo<FeedItem[]>(() => cache.data()?.items ?? []);

  // Header title. A single feed uses its own <title>. With multiple feeds the
  // fetcher leaves the data title empty (no single source), which previously
  // collapsed the header, so fall back to the selected feeds' names (then the
  // generic widget label) so the header never vanishes.
  const headerTitle = createMemo(() => {
    if (props.label) return props.label;
    const fs = feeds();
    if (fs.length <= 1) return feedTitle();
    const names = fs.map(f => f.title).filter(Boolean);
    return names.length ? names.join(', ') : t('widget.rss.label');
  });

  const view = createMemo<'empty' | 'loading' | 'error' | 'loaded'>(() => {
    if (!feeds().length) return 'empty';
    if (cache.data()) return 'loaded';
    if (cache.error()) return 'error';
    if (cache.loading()) return 'loading';
    // Feeds ARE selected here (the no-feeds case returned 'empty' above), so the
    // pre-fetch initial tick (data/error/loading all still unset) is a first
    // load, not an empty widget — never fall back to the "Select feed" prompt.
    return 'loading';
  });

  // Why the list on screen may not be current [S-34]. Covers both shapes: the
  // whole refresh failed (items survive by [S-25]), or some feeds in the set
  // failed while others returned. Null when the last fetch was clean.
  const failureReason = createMemo<string | null>(() => {
    if (cache.error() && items().length) return cache.error();
    const partial = cache.data()?.failures ?? [];
    return partial.length ? partial.join('; ') : null;
  });

  // Tooltip for the warning badge. Names the age of what is being shown when we
  // know it: "your items are two days old, and here is the reason why" is the
  // whole answer to #81, and it has to be reachable without opening the log.
  const failureTitle = createMemo(() => {
    const reason = failureReason() ?? '';
    const ts = cache.lastUpdated();
    return ts > 0
      ? t('widgetUi.refreshFailedSince', { reason, time: timeAgo(new Date(ts).toISOString()) })
      : t('widgetUi.refreshFailed', { reason });
  });

  function openLink(url: string) {
    if (url) invoke('open_url', { url });
  }

  return (
    <div class="rss-widget">
      <Switch>
        <Match when={view() === 'empty'}>
          <div class="widget-body"><div class="rss-widget__msg" innerHTML={t('rss.selectFeed')} /></div>
        </Match>
        <Match when={view() === 'loading'}>
          <div class="widget-body"><div class="rss-widget__msg">{t('widgetUi.loading')}</div></div>
        </Match>
        <Match when={view() === 'error'}>
          <div class="widget-body"><div class="rss-widget__msg rss-widget__msg--error">{cache.error()}</div></div>
        </Match>
        <Match when={view() === 'loaded'}>
          <Show when={headerTitle()}>
            <WidgetHeader
              mode={props.headerMode ?? 'normal'}
              icon={RssIcon}
              title={headerTitle()}
            />
          </Show>
          <div class="widget-body">
          <div class="rss-widget__list" style={{ gap: '0px' }}>
            <For each={items()}>
              {(item) => (
                <div class="rss-widget__item" onClick={() => openLink(item.link)}>
                  <Show when={showSource() && sourceHost(item.link)}>
                    {(host) => {
                      const fav = faviconUrl(item.link);
                      return (
                        <div class="rss-widget__item-source">
                          <span class="rss-widget__item-favicon">
                            <Show when={fav && !failedIcons().has(item.link)} fallback={<Globe size={12} strokeWidth={1.5} />}>
                              <img
                                src={fav!}
                                alt=""
                                onError={() => setFailedIcons(s => { const n = new Set(s); n.add(item.link); return n; })}
                              />
                            </Show>
                          </span>
                          <span class="rss-widget__item-host">{host()}</span>
                        </div>
                      );
                    }}
                  </Show>
                  <div class="rss-widget__item-title">{item.title}</div>
                  <Show when={showSnippet() && item.snippet}>
                    <div class="rss-widget__item-snippet">{item.snippet}</div>
                  </Show>
                  <Show when={showDate() && item.date}>
                    <div class="rss-widget__item-date">{timeAgo(item.date!)}</div>
                  </Show>
                </div>
              )}
            </For>
          </div>
          <div class="rss-widget__fade" />
          </div>
        </Match>
      </Switch>

      {/* Refresh feedback, deliberately OUTSIDE the header: it must show in
          every header mode (including `none`) and whether or not a title
          resolved. Without it the context-menu Refresh had no observable effect
          at all - not when it failed behind [S-25], not when it succeeded with
          unchanged items - which is why #81 reported the action as dead when it
          had been firing correctly the whole time [S-33].
          Last child on purpose: it overlaps the header band by paint order, so
          it needs no z-index of its own. */}
      <Show when={view() === 'loaded' && (cache.refreshing() || failureReason())}>
        <div class="rss-widget__status">
          <Show
            when={cache.refreshing()}
            fallback={
              <span class="rss-widget__status-badge rss-widget__status-badge--warn" title={failureTitle()}>
                <TriangleAlert size={12} strokeWidth={1.5} />
              </span>
            }
          >
            <span class="rss-widget__status-badge rss-widget__status-badge--spin" title={t('widgetUi.refreshing')}>
              <RefreshCw size={12} strokeWidth={1.5} />
            </span>
          </Show>
        </div>
      </Show>
    </div>
  );
}
