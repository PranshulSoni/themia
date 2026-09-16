import { createMemo, createEffect, untrack, Show, Switch, Match, For } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { createCachedFetch } from '../../../src/createCachedFetch';
import { isDemo } from '../../../src/demo/invoke.ts';
import { logger } from '../../../src/debugLog.ts';
import { t } from '../../../src/i18n.ts';
import {
  openUrl, timeAgo, fetchAccountCreds, ghHeaders, repoFromRepositoryUrl, mergeAccountResults,
} from '../github/_shared.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './GitHubReviewRequested.css';

interface RawIssue {
  number: number;
  title: string;
  state: string;
  html_url: string;
  repository_url: string;
  updated_at: string;
  draft?: boolean;
  user: { login: string };
  pull_request?: { merged_at: string | null };
}

interface Item {
  number: number;
  title: string;
  html_url: string;
  repo: string;
  updated_at: string;
  draft: boolean;
  author: string;
}

interface Props {
  headerMode?: HeaderMode;
  label?: string;
  accountIds?: string[];
  refreshMinutes?: number;
  count?: number;
  includeDrafts?: boolean;
  _refresh?: number;
}

function demoItems(): Item[] {
  const now = Date.now();
  return [
    { number: 142, title: 'feat: GitHub notifications widget', html_url: 'https://github.com/themia/themia-app/pull/142',
      repo: 'themia/themia-app', updated_at: new Date(now - 25 * 60_000).toISOString(), draft: false, author: 'alice' },
    { number: 88, title: 'fix: race in cache key memo', html_url: 'https://github.com/themia/themia-app/pull/88',
      repo: 'themia/themia-app', updated_at: new Date(now - 3 * 3_600_000).toISOString(), draft: false, author: 'bob' },
    { number: 7, title: 'chore: bump tauri to 2.4', html_url: 'https://github.com/themia/themia-deps/pull/7',
      repo: 'themia/themia-deps', updated_at: new Date(now - 2 * 86_400_000).toISOString(), draft: true, author: 'carol' },
  ];
}

export default function GitHubReviewRequested(props: Props) {
  const refreshMs = createMemo(() => Math.max(1, props.refreshMinutes ?? 5) * 60_000);
  const accountIds = createMemo<string[]>(() => Array.isArray(props.accountIds) ? props.accountIds : []);
  const count = createMemo(() => Math.max(1, Math.min(25, props.count ?? 8)));
  const includeDrafts = createMemo(() => props.includeDrafts === true);

  const cache = createCachedFetch<Item[]>({
    namespace: 'github-review-requested',
    key: () => {
      if (isDemo()) return 'demo';
      const ids = accountIds();
      if (ids.length === 0) return null;
      return `${ids.join(',')}|${count()}|${includeDrafts() ? 'drafts' : 'nodrafts'}`;
    },
    staleMs: refreshMs,
    fetcher: async (_key, signal) => {
      if (isDemo()) {
        const all = demoItems();
        return includeDrafts() ? all : all.filter(i => !i.draft);
      }
      const ids = accountIds();
      const per = count();
      const q = encodeURIComponent('is:open is:pr review-requested:@me archived:false');
      const results = await Promise.allSettled(ids.map(async (id) => {
        const creds = await fetchAccountCreds(id);
        if (!creds) return [] as Item[];
        const url = `${creds.apiBase}/search/issues?q=${q}&sort=updated&order=desc&per_page=${per}`;
        const res = await fetch(url, { headers: ghHeaders(creds.token), signal });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = await res.json() as { items: RawIssue[] };
        return (data.items ?? []).map<Item>(i => ({
          number: i.number,
          title: i.title,
          html_url: i.html_url,
          repo: repoFromRepositoryUrl(i.repository_url),
          updated_at: i.updated_at,
          draft: i.draft === true,
          author: i.user?.login ?? '',
        }));
      }));

      // A superseded fetch aborts `signal`, rejecting each in-flight `fetch`
      // with an AbortError. `Promise.allSettled` swallows those rejections, and
      // `mergeAccountResults` still returns the empty/partial results of the
      // accounts that resolved (e.g. a missing-creds account's fulfilled []),
      // so the fetcher would resolve normally and clobber the PRIOR key's cache
      // entry with stale data — defeating createCachedFetch's AbortError guard.
      // Re-throw so that guard fires and skips the write.
      if (signal.aborted) throw new DOMException('aborted', 'AbortError');

      const all = mergeAccountResults(results, (reason) =>
        logger.error('github-review-requested', `account fetch failed: ${reason}`));
      const filtered = includeDrafts() ? all : all.filter(i => !i.draft);
      filtered.sort((a, b) => b.updated_at.localeCompare(a.updated_at));
      return filtered.slice(0, per);
    },
  });

  createPolling(() => { void cache.refresh(true); }, refreshMs, 'github-review-requested');

  // Manual refresh: the context-menu action bumps `_refresh`; force a re-fetch.
  createEffect(() => {
    void props._refresh;
    untrack(() => { void cache.refresh(true); });
  });

  const view = createMemo<'empty' | 'loading' | 'error' | 'loaded'>(() => {
    if (!isDemo() && accountIds().length === 0) return 'empty';
    if (cache.data()) return 'loaded';
    if (cache.error()) return 'error';
    if (cache.loading()) return 'loading';
    return 'empty';
  });

  return (
    <div class="gh-rr">
      <Switch>
        <Match when={view() === 'empty'}>
          <div class="gh-rr__msg widget-body">{t('github.reviewRequestedEmpty')}</div>
        </Match>
        <Match when={view() === 'loading'}>
          <div class="gh-rr__msg widget-body">{t('common.loading')}</div>
        </Match>
        <Match when={view() === 'error'}>
          <div class="gh-rr__msg gh-rr__msg--error widget-body">{cache.error()}</div>
        </Match>
        <Match when={view() === 'loaded' && cache.data()}>
          {(() => {
            const items = cache.data()!;
            return (
              <>
                <WidgetHeader
                  mode={props.headerMode ?? 'compact'}
                  title={props.label || t('github.reviewRequestedTitle')}
                  secondary={
                    <Show when={items.length > 0}>
                      <span class="gh-rr__count">{items.length}</span>
                    </Show>
                  }
                />
                <div class="gh-rr__body widget-body">
                <Show when={items.length === 0}>
                  <div class="gh-rr__msg">{t('github.reviewRequestedNone')}</div>
                </Show>
                <div
                  class="gh-rr__list"
                  style={{ gap: 'var(--item-gap,1px)' }}
                >
                  <For each={items}>
                    {(item) => (
                      <div class="gh-rr__item" onClick={() => openUrl(item.html_url)}>
                        <span
                          class="gh-rr__num"
                          classList={{ 'gh-rr__num--draft': item.draft }}
                        >#{item.number}</span>
                        <span class="gh-rr__title-text">{item.title}</span>
                        <span class="gh-rr__repo">{item.repo}</span>
                        <span class="gh-rr__time">{timeAgo(item.updated_at)}</span>
                      </div>
                    )}
                  </For>
                </div>
                </div>
              </>
            );
          })()}
        </Match>
      </Switch>
    </div>
  );
}
