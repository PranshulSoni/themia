import { createMemo, createEffect, untrack, Show, Switch, Match, For } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { createCachedFetch } from '../../../src/createCachedFetch';
import { isDemo } from '../../../src/demo/invoke.ts';
import { logger } from '../../../src/debugLog.ts';
import { t } from '../../../src/i18n.ts';
import {
  openUrl, timeAgo, fetchAccountCreds, ghHeaders, repoFromRepositoryUrl,
} from '../github/_shared.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './GitHubAssignedIssues.css';

interface RawIssue {
  number: number;
  title: string;
  html_url: string;
  repository_url: string;
  updated_at: string;
  state: string;
  user: { login: string };
  pull_request?: unknown;
}

interface Item {
  number: number;
  title: string;
  html_url: string;
  repo: string;
  updated_at: string;
}

interface Props {
  headerMode?: HeaderMode;
  label?: string;
  accountIds?: string[];
  refreshMinutes?: number;
  count?: number;
  includeMentioned?: boolean;
  _refresh?: number;
}

function demoItems(): Item[] {
  const now = Date.now();
  return [
    { number: 312, title: 'Add notifications widget', html_url: 'https://github.com/themia/themia-app/issues/312',
      repo: 'themia/themia-app', updated_at: new Date(now - 18 * 60_000).toISOString() },
    { number: 207, title: 'Investigate cache key collisions on Enterprise', html_url: 'https://github.com/themia/themia-app/issues/207',
      repo: 'themia/themia-app', updated_at: new Date(now - 6 * 3_600_000).toISOString() },
    { number: 11, title: 'Polish empty states', html_url: 'https://github.com/themia/themia-docs/issues/11',
      repo: 'themia/themia-docs', updated_at: new Date(now - 3 * 86_400_000).toISOString() },
  ];
}

export default function GitHubAssignedIssues(props: Props) {
  const refreshMs = createMemo(() => Math.max(1, props.refreshMinutes ?? 5) * 60_000);
  const accountIds = createMemo<string[]>(() => Array.isArray(props.accountIds) ? props.accountIds : []);
  const count = createMemo(() => Math.max(1, Math.min(25, props.count ?? 8)));
  const includeMentioned = createMemo(() => props.includeMentioned === true);

  const cache = createCachedFetch<Item[]>({
    namespace: 'github-assigned-issues',
    key: () => {
      if (isDemo()) return 'demo';
      const ids = accountIds();
      if (ids.length === 0) return null;
      return `${ids.join(',')}|${count()}|${includeMentioned() ? 'mention' : 'assign'}`;
    },
    staleMs: refreshMs,
    fetcher: async (_key, signal) => {
      if (isDemo()) return demoItems();
      const ids = accountIds();
      const per = count();
      const base = 'is:open is:issue archived:false';
      const q = encodeURIComponent(
        includeMentioned()
          ? `${base} (assignee:@me OR mentions:@me)`
          : `${base} assignee:@me`,
      );
      const all: Item[] = [];
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
        }));
      }));
      for (const r of results) {
        if (r.status === 'fulfilled') all.push(...r.value);
        else logger.error('github-assigned-issues', `account fetch failed: ${r.reason}`);
      }
      all.sort((a, b) => b.updated_at.localeCompare(a.updated_at));
      return all.slice(0, per);
    },
  });

  createPolling(() => { void cache.refresh(true); }, refreshMs, 'github-assigned-issues');

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
    <div class="gh-ai">
      <Switch>
        <Match when={view() === 'empty'}>
          <div class="gh-ai__msg widget-body">{t('github.assignedSignIn')}</div>
        </Match>
        <Match when={view() === 'loading'}>
          <div class="gh-ai__msg widget-body">{t('common.loading')}</div>
        </Match>
        <Match when={view() === 'error'}>
          <div class="gh-ai__msg gh-ai__msg--error widget-body">{cache.error()}</div>
        </Match>
        <Match when={view() === 'loaded'}>
          <WidgetHeader
            mode={props.headerMode ?? 'compact'}
            title={props.label || t('github.assignedTitle')}
            secondary={
              <Show when={(cache.data()?.length ?? 0) > 0}>
                <span class="gh-ai__count">{cache.data()!.length}</span>
              </Show>
            }
          />
          <div class="gh-ai__body widget-body">
            <Show
              when={(cache.data()?.length ?? 0) > 0}
              fallback={<div class="gh-ai__msg">{t('github.assignedEmpty')}</div>}
            >
              <div
                class="gh-ai__list"
                style={{ gap: 'var(--item-gap,1px)' }}
              >
                <For each={cache.data() ?? []}>
                  {(item) => (
                    <div class="gh-ai__item" onClick={() => openUrl(item.html_url)}>
                      <span class="gh-ai__num">#{item.number}</span>
                      <span class="gh-ai__title-text">{item.title}</span>
                      <span class="gh-ai__repo">{item.repo}</span>
                      <span class="gh-ai__time">{timeAgo(item.updated_at)}</span>
                    </div>
                  )}
                </For>
              </div>
            </Show>
          </div>
        </Match>
      </Switch>
    </div>
  );
}
