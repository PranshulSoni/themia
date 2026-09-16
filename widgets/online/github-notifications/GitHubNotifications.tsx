import { createMemo, Show, Switch, Match, For } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { createCachedFetch } from '../../../src/createCachedFetch';
import { createManualRefresh } from '../../office/calendar/manualRefresh.ts';
import { isDemo } from '../../../src/demo/invoke.ts';
import { logger } from '../../../src/debugLog.ts';
import { showCtxMenu } from '../../../src/ctxMenuStore.ts';
import { showToast } from '../../../src/toastStore.ts';
import { t } from '../../../src/i18n.ts';
import { ExternalLink, CheckCheck } from 'lucide-solid';
import {
  openUrl, timeAgo, fetchAccountCreds, ghHeaders, subjectUrlToHtml,
} from '../github/_shared.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './GitHubNotifications.css';

interface RawNotification {
  id: string;
  unread: boolean;
  reason: string;
  updated_at: string;
  subject: { title: string; type: string; url: string | null; latest_comment_url: string | null };
  repository: { full_name: string; html_url: string };
}

interface Item {
  id: string;
  account_id: string;
  unread: boolean;
  reason: string;
  updated_at: string;
  title: string;
  type: string;
  html_url: string;
  repo_full_name: string;
  repo_html_url: string;
  api_base: string;
}

interface Props {
  headerMode?: HeaderMode;
  label?: string;
  accountIds?: string[];
  refreshMinutes?: number;
  count?: number;
  includeRead?: boolean;
  participatingOnly?: boolean;
  _refresh?: number;
}

function reasonLabel(reason: string): string {
  switch (reason) {
    case 'review_requested': return 'review';
    case 'mention': return 'mention';
    case 'assign': return 'assigned';
    case 'security_advisory': return 'security';
    case 'team_mention': return 'team';
    case 'comment': return 'comment';
    case 'author': return 'author';
    case 'state_change': return 'state';
    case 'subscribed': return 'sub';
    case 'ci_activity': return 'ci';
    default: return reason;
  }
}

function reasonColor(reason: string): string {
  switch (reason) {
    case 'security_advisory': return 'var(--destructive)';
    case 'review_requested':
    case 'mention':
    case 'team_mention':
    case 'assign': return '#e5a00d';
    case 'author': return 'var(--primary)';
    default: return 'var(--muted-foreground)';
  }
}

function demoItems(): Item[] {
  const now = Date.now();
  return [
    {
      id: 'd1', account_id: 'demo', unread: true, reason: 'review_requested',
      updated_at: new Date(now - 12 * 60_000).toISOString(),
      title: 'feat: extract shared github helpers', type: 'PullRequest',
      html_url: 'https://github.com/themia/themia-app/pull/123',
      repo_full_name: 'themia/themia-app', repo_html_url: 'https://github.com/themia/themia-app',
      api_base: 'https://api.github.com',
    },
    {
      id: 'd2', account_id: 'demo', unread: true, reason: 'mention',
      updated_at: new Date(now - 90 * 60_000).toISOString(),
      title: 'Spec for assigned-issues widget', type: 'Issue',
      html_url: 'https://github.com/themia/themia-app/issues/45',
      repo_full_name: 'themia/themia-app', repo_html_url: 'https://github.com/themia/themia-app',
      api_base: 'https://api.github.com',
    },
    {
      id: 'd3', account_id: 'demo', unread: false, reason: 'subscribed',
      updated_at: new Date(now - 4 * 3_600_000).toISOString(),
      title: 'CI: green again on master', type: 'CheckSuite',
      html_url: 'https://github.com/themia/themia-app',
      repo_full_name: 'themia/themia-app', repo_html_url: 'https://github.com/themia/themia-app',
      api_base: 'https://api.github.com',
    },
  ];
}

export default function GitHubNotifications(props: Props) {
  const refreshMs = createMemo(() => Math.max(1, props.refreshMinutes ?? 5) * 60_000);
  const accountIds = createMemo<string[]>(() => Array.isArray(props.accountIds) ? props.accountIds : []);
  const count = createMemo(() => Math.max(1, Math.min(20, props.count ?? 5)));
  const includeRead = createMemo(() => props.includeRead === true);
  const participating = createMemo(() => props.participatingOnly === true);

  const cache = createCachedFetch<Item[]>({
    namespace: 'github-notifications',
    key: () => {
      if (isDemo()) return 'demo';
      const ids = accountIds();
      if (ids.length === 0) return null;
      return `${ids.join(',')}|${count()}|${includeRead() ? 'all' : 'unread'}|${participating() ? 'part' : 'any'}`;
    },
    staleMs: refreshMs,
    fetcher: async (_key, signal) => {
      if (isDemo()) return demoItems();
      const ids = accountIds();
      const per = count();
      const all: Item[] = [];
      const results = await Promise.allSettled(ids.map(async (id) => {
        const creds = await fetchAccountCreds(id);
        if (!creds) return [] as Item[];
        const qs = `all=${includeRead() ? 'true' : 'false'}&participating=${participating() ? 'true' : 'false'}&per_page=${per}`;
        const res = await fetch(`${creds.apiBase}/notifications?${qs}`, { headers: ghHeaders(creds.token), signal });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = await res.json() as RawNotification[];
        return data.map<Item>(n => ({
          id: n.id,
          account_id: id,
          unread: n.unread,
          reason: n.reason,
          updated_at: n.updated_at,
          title: n.subject.title,
          type: n.subject.type,
          // [N-22] Prefer subject.url: it carries the issue/PR number and converts
          // to a real web URL. latest_comment_url is a comment API URL with no
          // issue number, so it can't be turned into a working link.
          html_url: subjectUrlToHtml(n.subject.url ?? n.subject.latest_comment_url, n.repository.html_url),
          repo_full_name: n.repository.full_name,
          repo_html_url: n.repository.html_url,
          api_base: creds.apiBase,
        }));
      }));
      for (const r of results) {
        if (r.status === 'fulfilled') all.push(...r.value);
        else logger.error('github-notifications', `account fetch failed: ${r.reason}`);
      }
      all.sort((a, b) => b.updated_at.localeCompare(a.updated_at));
      return all.slice(0, per);
    },
  });

  createPolling(() => { void cache.refresh(true); }, refreshMs, 'github-notifications');

  // [N-18] Widget-level "Refresh" context action bumps the `_refresh` config
  // token; force a re-fetch when it changes. The mount run is skipped
  // (createCachedFetch's own effect covers first load), so a `_refresh` token
  // persisted in config from a previous session can't force a redundant network
  // fetch on app start.
  createManualRefresh(() => props._refresh, () => { void cache.refresh(true); });

  async function markAsRead(item: Item): Promise<void> {
    if (isDemo()) return;
    const creds = await fetchAccountCreds(item.account_id);
    if (!creds) return;
    try {
      const res = await fetch(`${creds.apiBase}/notifications/threads/${item.id}`, {
        method: 'PATCH',
        headers: ghHeaders(creds.token),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      void cache.refresh(true);
    } catch (e) {
      logger.error('github-notifications', `mark-as-read failed: ${e}`);
      showToast(t('github.markReadFailed'), 'error');
    }
  }

  async function markRepoRead(item: Item): Promise<void> {
    if (isDemo()) return;
    const creds = await fetchAccountCreds(item.account_id);
    if (!creds) return;
    try {
      const res = await fetch(`${creds.apiBase}/repos/${item.repo_full_name}/notifications`, {
        method: 'PUT',
        headers: { ...ghHeaders(creds.token), 'Content-Type': 'application/json' },
        body: JSON.stringify({}),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      void cache.refresh(true);
    } catch (e) {
      logger.error('github-notifications', `mark-repo-read failed: ${e}`);
      showToast(t('github.markRepoReadFailed'), 'error');
    }
  }

  function onItemContext(e: MouseEvent, item: Item): void {
    e.preventDefault();
    e.stopPropagation();
    showCtxMenu(e, [
      { icon: ExternalLink, label: t('github.openInBrowser'), action: () => openUrl(item.html_url) },
      { sep: true },
      { icon: CheckCheck, label: t('github.markAsRead'), action: () => { void markAsRead(item); }, disabled: !item.unread },
      { icon: CheckCheck, label: `Mark all in ${item.repo_full_name} as read`, action: () => { void markRepoRead(item); } },
    ]);
  }

  const view = createMemo<'empty' | 'loading' | 'error' | 'loaded'>(() => {
    if (!isDemo() && accountIds().length === 0) return 'empty';
    if (cache.data()) return 'loaded';
    if (cache.error()) return 'error';
    if (cache.loading()) return 'loading';
    return 'empty';
  });

  return (
    <div class="gh-notif">
      <Switch>
        <Match when={view() === 'empty'}>
          <div class="gh-notif__msg widget-body">{t('github.notificationsSignIn')}</div>
        </Match>
        <Match when={view() === 'loading'}>
          <div class="gh-notif__msg widget-body">Loading…</div>
        </Match>
        <Match when={view() === 'error'}>
          <div class="gh-notif__msg gh-notif__msg--error widget-body">{cache.error()}</div>
        </Match>
        <Match when={view() === 'loaded' && cache.data()}>
          {(items) => (
              <>
                <WidgetHeader
                  mode={props.headerMode ?? 'compact'}
                  title={props.label || 'Notifications'}
                  secondary={
                    <Show when={items().some(i => i.unread)}>
                      <span class="gh-notif__count">{items().filter(i => i.unread).length}</span>
                    </Show>
                  }
                />
                <div class="gh-notif__body widget-body">
                <Show when={items().length === 0}>
                  <div class="gh-notif__msg">{t('github.notificationsEmpty')}</div>
                </Show>
                <div
                  class="gh-notif__list"
                  style={{ gap: 'var(--item-gap,1px)' }}
                >
                  <For each={items()}>
                    {(item) => (
                      <div
                        class="gh-notif__item"
                        classList={{ 'gh-notif__item--unread': item.unread }}
                        onClick={() => openUrl(item.html_url)}
                        onContextMenu={(e) => onItemContext(e, item)}
                      >
                        <span class="gh-notif__reason" style={{ color: reasonColor(item.reason) }}>
                          {reasonLabel(item.reason)}
                        </span>
                        <span class="gh-notif__subject">{item.title}</span>
                        <span class="gh-notif__repo">{item.repo_full_name}</span>
                        <span class="gh-notif__time">{timeAgo(item.updated_at)}</span>
                      </div>
                    )}
                  </For>
                </div>
                </div>
              </>
          )}
        </Match>
      </Switch>
    </div>
  );
}
