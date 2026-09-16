import { createMemo } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { createCachedFetch } from '../../../src/createCachedFetch';
import { createManualRefresh } from '../../office/calendar/manualRefresh.ts';
import { Show, Switch, Match, For } from 'solid-js/web';
import { Star, GitFork, CircleDot, GitBranch } from 'lucide-solid';
import { isDemo } from '../../../src/demo/invoke.ts';
import { demoGithub } from '../../../src/demo/fixtures.ts';
import { logger } from '../../../src/debugLog.ts';
import { t } from '../../../src/i18n.ts';
import { openUrl, timeAgo, fetchAccountCreds, ghHeaders } from './_shared.ts';
import { filterSortPRs, asPrStateFilter, asPrSortOrder } from './prFilterSort.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './GitHub.css';

interface WorkflowRun {
  id: number;
  name: string;
  status: string;
  conclusion: string | null;
  head_branch: string;
  created_at: string;
  html_url: string;
}

interface Commit {
  sha: string;
  html_url: string;
  commit: { message: string; author: { name: string; date: string } };
}

interface PullRequest {
  number: number;
  title: string;
  state: string;
  merged_at: string | null;
  html_url: string;
  created_at: string;
  user: { login: string };
}

interface Issue {
  number: number;
  title: string;
  html_url: string;
  created_at: string;
  pull_request?: unknown;
  labels: { name: string; color: string }[];
}

interface Release {
  tag_name: string;
  html_url: string;
}

interface Repo {
  full_name: string;
  stargazers_count: number;
  forks_count: number;
  open_issues_count: number;
  language: string | null;
  pushed_at: string;
  default_branch: string;
}

interface RepoBundle {
  repo: Repo;
  runs: WorkflowRun[];
  commits: Commit[];
  prs: PullRequest[];
  issues: Issue[];
  release: Release | null;
}

interface GitHubProps {
  headerMode?: HeaderMode;
  repo?: string;
  accountIds?: string[];
  refreshMinutes?: number;
  label?: string;
  commitCount?: number;
  prCount?: number;
  prState?: string;
  prSort?: string;
  issueCount?: number;
  pipelineCount?: number;
  _refresh?: number;
}

function parseRepo(input: string, apiOverride?: string | null): { api: string; repo: string } | null {
  if (!input) return null;
  try {
    const url = new URL(input);
    const parts = url.pathname.replace(/^\//, '').replace(/\.git$/, '').split('/');
    if (parts.length < 2) return null;
    const repo = `${parts[0]}/${parts[1]}`;
    if (apiOverride) return { api: apiOverride, repo };
    if (url.hostname === 'github.com') {
      return { api: 'https://api.github.com', repo };
    }
    return { api: `${url.origin}/api/v3`, repo };
  } catch {
    if (!input.includes('/')) return null;
    return { api: apiOverride ?? 'https://api.github.com', repo: input };
  }
}

export default function GitHub(props: GitHubProps) {
  function prColor(pr: PullRequest): string {
    if (pr.merged_at) return '#a371f7';
    if (pr.state === 'open') return '#2ea043';
    return 'var(--destructive)';
  }

  function runColor(run: WorkflowRun): string {
    if (run.status === 'in_progress' || run.status === 'queued') return '#e5a00d';
    if (run.conclusion === 'success') return '#2ea043';
    if (run.conclusion === 'failure') return 'var(--destructive)';
    return 'var(--muted-foreground)';
  }

  function fmtNum(n: number): string {
    if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M';
    if (n >= 1_000) return (n / 1_000).toFixed(1) + 'k';
    return String(n);
  }

  const refreshMs = createMemo(() => Math.max(1, props.refreshMinutes ?? 5) * 60_000);

  function firstAccountId(): string | null {
    const ids = props.accountIds;
    return Array.isArray(ids) && ids.length > 0 ? ids[0] : null;
  }

  const cache = createCachedFetch<RepoBundle>({
    namespace: 'github',
    // NOTE: token deliberately excluded from cache key: never fingerprint by secrets.
    // Account id IS included so widgets pointing at different accounts/hosts
    // do not collide. Token is fetched on demand inside fetcher.
    key: () => {
      if (isDemo()) return 'demo';
      const parsed = parseRepo(props.repo ?? '');
      if (!parsed) return null;
      const acc = firstAccountId() ?? '';
      const prState = asPrStateFilter(props.prState);
      const prSort = asPrSortOrder(props.prSort);
      return `${acc}|${parsed.api}|${parsed.repo}|${props.commitCount ?? 3},${props.prCount ?? 3},${prState},${prSort},${props.issueCount ?? 3},${props.pipelineCount ?? 5}`;
    },
    staleMs: refreshMs,
    fetcher: async (_key, signal) => {
      if (isDemo()) {
        const d = demoGithub();
        const demoPrs = filterSortPRs(d.prs as PullRequest[], asPrStateFilter(props.prState), asPrSortOrder(props.prSort))
          .slice(0, props.prCount ?? 3);
        return {
          repo: d.repo,
          runs: d.runs as WorkflowRun[],
          commits: d.commits as Commit[],
          prs: demoPrs,
          issues: d.issues as Issue[],
          release: d.release as Release | null,
        };
      }
      const accountId = firstAccountId();
      let token: string | null = null;
      let apiOverride: string | null = null;
      if (accountId) {
        const creds = await fetchAccountCreds(accountId);
        if (creds) {
          token = creds.token;
          apiOverride = creds.apiBase;
        }
      }
      const parsed = parseRepo(props.repo ?? '', apiOverride);
      if (!parsed) throw new Error('Invalid repo');
      const { api, repo } = parsed;
      const headers: Record<string, string> = token ? ghHeaders(token) : { Accept: 'application/vnd.github+json' };

      const repoRes = await fetch(`${api}/repos/${repo}`, { headers, signal });
      if (!repoRes.ok) {
        const body = await repoRes.json().catch(() => ({}));
        const msg = body.message ?? `HTTP ${repoRes.status}`;
        if (repoRes.status === 404) {
          throw new Error('Repository not found. If private, add a token with the repo scope in settings.');
        }
        throw new Error(msg);
      }
      const repoData = await repoRes.json();

      let runs: WorkflowRun[] = [];
      try {
        const r = await fetch(`${api}/repos/${repo}/actions/runs?per_page=${Math.max(props.pipelineCount ?? 5, 1)}`, { headers, signal });
        if (r.ok) {
          const body = await r.json();
          runs = body.workflow_runs ?? [];
        }
      } catch (e) { logger.error('github', `failed to fetch workflow runs: ${e}`); }

      let commits: Commit[] = [];
      if ((props.commitCount ?? 3) > 0) {
        try {
          const r = await fetch(`${api}/repos/${repo}/commits?per_page=${props.commitCount ?? 3}`, { headers, signal });
          if (r.ok) commits = await r.json();
        } catch (e) { logger.error('github', `failed to fetch commits: ${e}`); }
      }

      let prs: PullRequest[] = [];
      const prCount = props.prCount ?? 3;
      if (prCount > 0) {
        try {
          // Over-fetch `state=all` (the filter is applied client-side so a single
          // fetch serves any state filter) so that after filtering to the chosen
          // state there are still enough PRs to fill `prCount`.
          const fetchN = Math.min(prCount * 3 + 5, 100);
          const r = await fetch(`${api}/repos/${repo}/pulls?state=all&sort=created&direction=desc&per_page=${fetchN}`, { headers, signal });
          if (r.ok) {
            const all: PullRequest[] = await r.json();
            const prState = asPrStateFilter(props.prState);
            const prSort = asPrSortOrder(props.prSort);
            prs = filterSortPRs(all, prState, prSort).slice(0, prCount);
          }
        } catch (e) { logger.error('github', `failed to fetch PRs: ${e}`); }
      }

      let issues: Issue[] = [];
      if ((props.issueCount ?? 3) > 0) {
        try {
          const r = await fetch(`${api}/repos/${repo}/issues?state=open&per_page=${(props.issueCount ?? 3) + 5}`, { headers, signal });
          if (r.ok) {
            const all: Issue[] = await r.json();
            issues = all.filter(i => !i.pull_request).slice(0, props.issueCount ?? 3);
          }
        } catch (e) { logger.error('github', `failed to fetch issues: ${e}`); }
      }

      let release: Release | null = null;
      try {
        const r = await fetch(`${api}/repos/${repo}/releases/latest`, { headers, signal });
        if (r.ok) release = await r.json();
      } catch (e) { logger.error('github', `failed to fetch latest release: ${e}`); }

      return { repo: repoData, runs, commits, prs, issues, release };
    },
  });

  createPolling(() => { void cache.refresh(true); }, refreshMs, 'github');

  // [S-23] Widget-level "Refresh" context action bumps the `_refresh` config
  // token; force a re-fetch when it changes. The mount run is skipped
  // (createCachedFetch's own effect covers first load), so a `_refresh` token
  // persisted in config from a previous session can't force a redundant network
  // fetch on app start.
  createManualRefresh(() => props._refresh, () => { void cache.refresh(true); });

  const view = createMemo<'empty' | 'loading' | 'error' | 'loaded'>(() => {
    if (!isDemo() && !parseRepo(props.repo ?? '')) return 'empty';
    if (cache.data()) return 'loaded';
    if (cache.error()) return 'error';
    return 'loading';
  });

  return (
    <div class="github-widget">
      <Switch>
        <Match when={view() === 'empty'}>
          <div class="github-widget__state-msg widget-body" innerHTML={t('github.selectRepo')} />
        </Match>
        <Match when={view() === 'loading'}>
          <div class="github-widget__state-msg widget-body">{t('widgetUi.loading')}</div>
        </Match>
        <Match when={view() === 'error'}>
          <div class="github-widget__state-msg github-widget__state-msg--error widget-body">{cache.error()}</div>
        </Match>
        <Match when={view() === 'loaded' && cache.data()}>
          {(() => {
            const bundle = cache.data()!;
            const rd = bundle.repo;
            const runs = bundle.runs;
            const commits = bundle.commits;
            const prs = bundle.prs;
            const issues = bundle.issues;
            const release = bundle.release;
            return (
              <>
                <WidgetHeader
                  mode={props.headerMode ?? 'compact'}
                  title={props.label || rd.full_name}
                  secondary={
                    <>
                      <Show when={release}>
                        <span
                          class="github-widget__release-badge"
                          onClick={() => openUrl(release!.html_url)}
                        >{release!.tag_name}</span>
                      </Show>
                      <Show when={runs.length > 0}>
                        <span
                          class="github-widget__pipeline-badge"
                          classList={{
                            'github-widget__pipeline-badge--ok': runs[0].conclusion === 'success',
                            'github-widget__pipeline-badge--fail': runs[0].conclusion === 'failure',
                            'github-widget__pipeline-badge--pending': runs[0].status === 'in_progress' || runs[0].status === 'queued',
                          }}
                        >{runs[0].conclusion === 'success' ? t('github.pipelinePassing') : runs[0].conclusion === 'failure' ? t('github.pipelineFailing') : runs[0].status === 'in_progress' ? t('github.pipelineRunning') : t('github.pipelinePending')}</span>
                      </Show>
                    </>
                  }
                />

                <div class="github-widget__body widget-body">
                <div class="github-widget__stats-area">
                  <div class="github-widget__stat">
                    <Star class="github-widget__stat-icon" size={13} strokeWidth={1.5} />
                    <span class="github-widget__stat-val">{fmtNum(rd.stargazers_count)}</span>
                  </div>
                  <div class="github-widget__stat">
                    <GitFork class="github-widget__stat-icon" size={13} strokeWidth={1.5} />
                    <span class="github-widget__stat-val">{fmtNum(rd.forks_count)}</span>
                  </div>
                  <div class="github-widget__stat">
                    <CircleDot class="github-widget__stat-icon" size={13} strokeWidth={1.5} />
                    <span class="github-widget__stat-val">{fmtNum(rd.open_issues_count)}</span>
                  </div>
                </div>

                <div class="github-widget__meta-row">
                  <Show when={rd.language}>
                    <span class="github-widget__lang">{rd.language}</span>
                  </Show>
                  <span class="github-widget__pushed">{t('github.updated', { time: timeAgo(rd.pushed_at) })}</span>
                  <span class="github-widget__branch"><GitBranch size={11} strokeWidth={1.5} /> {rd.default_branch}</span>
                </div>

                <Show when={(props.commitCount ?? 3) > 0 && commits.length > 0}>
                  <div class="github-widget__commits" style={{ gap: 'var(--item-gap,1px)' }}>
                    <For each={commits}>
                      {(c) => (
                        <div class="github-widget__commit" onClick={() => openUrl(c.html_url)}>
                          <span class="github-widget__commit-sha">{c.sha.slice(0, 7)}</span>
                          <span class="github-widget__commit-msg">{c.commit.message.split('\n')[0]}</span>
                          <span class="github-widget__commit-time">{timeAgo(c.commit.author.date)}</span>
                        </div>
                      )}
                    </For>
                  </div>
                </Show>

                <Show when={(props.prCount ?? 3) > 0 && prs.length > 0}>
                  <div class="github-widget__prs" style={{ gap: 'var(--item-gap,1px)' }}>
                    <For each={prs}>
                      {(pr) => (
                        <div class="github-widget__pr" onClick={() => openUrl(pr.html_url)}>
                          <span class="github-widget__pr-num" style={{ color: prColor(pr) }}>#{pr.number}</span>
                          <span class="github-widget__pr-title">{pr.title}</span>
                          <span class="github-widget__pr-time">{timeAgo(pr.created_at)}</span>
                        </div>
                      )}
                    </For>
                  </div>
                </Show>

                <Show when={(props.issueCount ?? 3) > 0 && issues.length > 0}>
                  <div class="github-widget__issues" style={{ gap: 'var(--item-gap,1px)' }}>
                    <For each={issues}>
                      {(issue) => (
                        <div class="github-widget__issue" onClick={() => openUrl(issue.html_url)}>
                          <span class="github-widget__issue-num">#{issue.number}</span>
                          <span class="github-widget__issue-title">{issue.title}</span>
                          <span class="github-widget__issue-time">{timeAgo(issue.created_at)}</span>
                        </div>
                      )}
                    </For>
                  </div>
                </Show>

                <Show when={(props.pipelineCount ?? 5) > 0 && runs.length > 0}>
                  <div class="github-widget__pipelines" style={{ gap: 'var(--item-gap,1px)' }}>
                    <For each={runs}>
                      {(run) => (
                        <div class="github-widget__run" onClick={() => openUrl(run.html_url)}>
                          <span class="github-widget__run-dot" style={{ background: runColor(run) }} />
                          <span class="github-widget__run-name">{run.name}</span>
                          <span class="github-widget__run-branch">{run.head_branch}</span>
                          <span class="github-widget__run-time">{timeAgo(run.created_at)}</span>
                        </div>
                      )}
                    </For>
                  </div>
                </Show>
                </div>
              </>
            );
          })()}
        </Match>
      </Switch>
    </div>
  );
}
