# GitHub Assigned Issues: Spec

Behavior tags are stable IDs ([A-NN]). Append new tags; never renumber or reuse.

## Display
- [A-01] Shows empty prompt "Sign in with a GitHub account to see assigned issues." when `accountIds` is empty (not in demo).
- [A-02] Shows "Loading…" while first fetch is in flight and no cached data.
- [A-03] Shows error message on fetch failure when no cached data is present.
- [A-04] Shows "No assigned issues." when fetch returns empty.
- [A-05] Header row shows custom label (or "Assigned issues") and a count badge.
- [A-06] Each item shows: issue number (green), title (truncated), repo (truncated), relative time.

## Configuration
- [A-07] `accountIds: string[]`: multi-account picker, providerFilter `['github']`. Empty disables fetch.
- [A-08] `count` (1–25, default 8): total items after merging accounts.
- [A-09] `includeMentioned` (bool, default false): OR `mentions:@me` into the query when true.
- [A-10] `refreshMinutes` (5–60 step 5, default 5).
- [A-11] *(removed)* Per-widget item spacing is no longer configurable; list gap uses the global `--item-gap` token.
- [A-12] `label`: overrides displayed header title.
- [A-13] Config has Source / Display / Refresh tabs; Source uses `AccountToggleList`.

## Context menu
- [A-14] Widget-level context menu has "Refresh" (via `_refresh` bump).

## Data / backend
- [A-15] `GET {apiBase}/search/issues?q={query}&sort=updated&order=desc&per_page={count}` per account, where `{query}` is:
  - `is:open is:issue archived:false assignee:@me` (default), or
  - `is:open is:issue archived:false (assignee:@me OR mentions:@me)` when `includeMentioned=true`.
- [A-16] Uses `Authorization: Bearer <token>` + `Accept: application/vnd.github+json`.
- [A-17] Multi-account merge: parallel `Promise.allSettled`, concat, sort desc by `updated_at`, slice to `count`. Failed accounts logged and skipped.
- [A-18] Repo name derived from `repository_url` (`https://…/repos/owner/repo` → `owner/repo`).

## Interactions
- [A-19] Clicking an item invokes Tauri `open_url` with the issue's `html_url`.

## Caching
- [A-20] Polls every `refreshMinutes` minutes via `createPolling`; pauses while app inactive.
- [A-21] Cache key composition: `{accountIds.join(',')}|{count}|{'mention'|'assign'}`. Token excluded.
- [A-22] Cached items persist across app restarts via localStorage (`themia.cache.github-assigned-issues.*`).

## Accounts
- [A-23] When 0 accounts are selected, the widget shows the empty prompt and performs no network calls.
- [A-24] Search runs against `/search/issues` of each selected account's apiBase.
