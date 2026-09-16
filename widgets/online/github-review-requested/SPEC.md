# GitHub Review-Requested PRs: Spec

Behavior tags are stable IDs ([R-NN]). Append new tags; never renumber or reuse.

## Display
- [R-01] Shows empty prompt "Sign in with a GitHub account to see review requests." when `accountIds` is empty (not in demo).
- [R-02] Shows "Loading…" while first fetch is in flight and no cached data.
- [R-03] Shows error message on fetch failure when no cached data is present.
- [R-04] Shows "No pending reviews." when fetch returns empty.
- [R-05] Header row shows custom label (or "Review requested") and a count badge.
- [R-06] Each item shows: PR number (green; muted when draft), title (truncated), repo (truncated), relative time.
- [R-07] Draft PRs render with muted PR-number color and are hidden by default.

## Configuration
- [R-08] `accountIds: string[]`: multi-account picker, providerFilter `['github']`. Empty disables fetch.
- [R-09] `count` (1–25, default 8): total items after merging accounts.
- [R-10] `includeDrafts` (bool, default false): include draft PRs in results.
- [R-11] `refreshMinutes` (5–60 step 5, default 5).
- [R-12] *(removed)* Per-widget item spacing is no longer configurable; list gap uses the global `--item-gap` token.
- [R-13] `label`: overrides displayed header title.
- [R-14] Config has Source / Display / Refresh tabs; Source uses `AccountToggleList`.

## Context menu
- [R-15] Widget-level context menu has "Refresh" (via `_refresh` bump).

## Data / backend
- [R-16] `GET {apiBase}/search/issues?q=is:open+is:pr+review-requested:@me+archived:false&sort=updated&order=desc&per_page={count}` per account.
- [R-17] Uses `Authorization: Bearer <token>` + `Accept: application/vnd.github+json`.
- [R-18] Multi-account merge: parallel `Promise.allSettled`, concat, sort desc by `updated_at`, slice to `count`. Failed accounts logged and skipped.
- [R-19] Repo name derived from `repository_url` (`https://…/repos/owner/repo` → `owner/repo`).
- [R-20] Draft filter applied client-side after fetch when `includeDrafts=false`.

## Interactions
- [R-21] Clicking an item invokes Tauri `open_url` with the PR's `html_url`.

## Caching
- [R-22] Polls every `refreshMinutes` minutes via `createPolling`; pauses while app inactive.
- [R-23] Cache key composition: `{accountIds.join(',')}|{count}|{'drafts'|'nodrafts'}`. Token excluded.
- [R-24] Cached items persist across app restarts via localStorage (`themia.cache.github-review-requested.*`).

## Accounts
- [R-25] When 0 accounts are selected, the widget shows the empty prompt and performs no network calls.
- [R-26] Search runs against `/search/issues` of each selected account's apiBase, so Enterprise accounts query their own host.
