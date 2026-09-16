import { invoke } from '../../../src/demo/invoke.ts';
import { logger } from '../../../src/debugLog.ts';
import { t, lang } from '../../../src/i18n.ts';

export function openUrl(url: string): void {
  invoke('open_url', { url }).catch(e => logger.error('github', `open_url failed: ${e}`));
}

export function timeAgo(iso: string): string {
  if (!iso) return '';
  const diff = Date.now() - new Date(iso).getTime();
  if (Number.isNaN(diff)) return '';
  if (diff < 60_000) return t('email.timeJustNow');
  if (diff < 3_600_000) return Math.floor(diff / 60_000) + 'm';
  if (diff < 86_400_000) return Math.floor(diff / 3_600_000) + 'h';
  if (diff < 7 * 86_400_000) return Math.floor(diff / 86_400_000) + 'd';
  return new Date(iso).toLocaleDateString(lang(), { month: 'short', day: 'numeric' });
}

export interface AccountCreds {
  accountId: string;
  token: string;
  apiBase: string;
}

export async function fetchAccountCreds(accountId: string): Promise<AccountCreds | null> {
  let token: string | null = null;
  let apiBase: string | null = null;
  try { token = await invoke('email_get_github_token', { accountId }); } catch (e) {
    logger.error('github', `email_get_github_token failed: ${e}`);
  }
  try { apiBase = await invoke('email_get_github_api_base_url', { accountId }); } catch (e) {
    logger.error('github', `email_get_github_api_base_url failed: ${e}`);
  }
  if (!token) return null;
  return { accountId, token, apiBase: apiBase || 'https://api.github.com' };
}

export function ghHeaders(token: string): Record<string, string> {
  return {
    Authorization: `Bearer ${token}`,
    Accept: 'application/vnd.github+json',
  };
}

/** Parse "https://api.github.com/repos/owner/repo" → "owner/repo". */
export function repoFromRepositoryUrl(repositoryUrl: string): string {
  const m = repositoryUrl.match(/\/repos\/([^/]+\/[^/]+)\/?$/);
  return m ? m[1] : '';
}

/** Convert a GitHub API subject URL (used by /notifications) to an html_url.
 *  Examples:
 *    /repos/o/r/pulls/12   → https://github.com/o/r/pull/12
 *    /repos/o/r/issues/34  → https://github.com/o/r/issues/34
 *    /repos/o/r/releases/9 → https://github.com/o/r/releases/9
 *  [N-22] Only recognized subject shapes with a single trailing segment convert.
 *  A comment API URL (…/issues/comments/123 or …/pulls/comments/123) carries the
 *  comment id, not the issue/PR number, so it cannot be turned into a web URL —
 *  it (and any other unrecognized shape) falls back to `repoHtmlUrl` instead of
 *  producing a github.com link that 404s. */
export function subjectUrlToHtml(subjectUrl: string | null | undefined, repoHtmlUrl: string): string {
  if (!subjectUrl) return repoHtmlUrl;
  try {
    const u = new URL(subjectUrl);
    // host of api.github.com → github.com; api/v3 hosts → strip /api/v3
    const host = u.host === 'api.github.com' ? 'github.com' : u.host;
    const path = u.pathname.replace(/^\/api\/v3/, '');
    const pull = path.match(/^\/repos\/([^/]+)\/([^/]+)\/pulls\/(\d+)$/);
    if (pull) return `${u.protocol}//${host}/${pull[1]}/${pull[2]}/pull/${pull[3]}`;
    // `[^/]+` (not `.+`) so a two-segment `comments/<id>` tail never matches.
    const other = path.match(/^\/repos\/([^/]+)\/([^/]+)\/(issues|releases|commits)\/([^/]+)$/);
    if (other) return `${u.protocol}//${host}/${other[1]}/${other[2]}/${other[3]}/${other[4]}`;
    return repoHtmlUrl;
  } catch {
    return repoHtmlUrl;
  }
}

/** Merge parallel per-account `Promise.allSettled` results into one list.
 *  [R-18] Rejected accounts are logged (via `onError`) and skipped, so a partial
 *  outage still renders the accounts that succeeded. [R-03] BUT when EVERY
 *  account failed (and there was at least one to fetch) the whole fetch is a
 *  failure: the first rejection is re-thrown instead of resolving to an empty
 *  list, so the widget renders its error state rather than a misleading
 *  "no results" message on a total network/auth outage. A missing-creds account
 *  contributes a fulfilled `[]`, which counts as success and never trips this. */
export function mergeAccountResults<T>(
  results: PromiseSettledResult<T[]>[],
  onError: (reason: unknown) => void,
): T[] {
  const merged: T[] = [];
  let anyFulfilled = false;
  let firstReason: unknown;
  for (const r of results) {
    if (r.status === 'fulfilled') {
      anyFulfilled = true;
      merged.push(...r.value);
    } else {
      if (firstReason === undefined) firstReason = r.reason;
      onError(r.reason);
    }
  }
  if (results.length > 0 && !anyFulfilled) {
    throw firstReason instanceof Error ? firstReason : new Error(String(firstReason));
  }
  return merged;
}
