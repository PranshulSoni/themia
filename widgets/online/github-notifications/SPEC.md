# GitHub Notifications: Spec

Behavior tags are stable IDs ([N-NN]). When adding behavior, append a new [N-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [N-01] Shows empty prompt "Sign in with a GitHub account to see notifications." when `accountIds` is empty (not in demo mode).
- [N-02] Shows "Loading…" while first fetch is in flight and no cached data.
- [N-03] Shows error message on fetch failure when no cached data is present.
- [N-04] Shows "No notifications." when the fetch returns an empty list.
- [N-05] Header row shows custom label (or "Notifications") and an unread count badge when at least one item is unread.
- [N-06] Each item shows: reason badge (colored), subject title (truncated), repo full_name (truncated), relative time.
- [N-07] Read items render at reduced opacity; unread items at full opacity.
- [N-08] Reason colors: `security_advisory` red; `review_requested`/`mention`/`team_mention`/`assign` amber; `author` primary; everything else muted.

## Configuration
- [N-09] `accountIds: string[]`: multi-account picker, providerFilter `['github']`. Empty disables fetch.
- [N-10] `count` (1–20, default 5): total items shown after merging accounts.
- [N-11] `includeRead` (bool, default false): passes `all=true` to GitHub API when true.
- [N-12] `participatingOnly` (bool, default false): passes `participating=true` to GitHub API when true.
- [N-13] `refreshMinutes` (5–60 step 5, default 5).
- [N-14] *(removed)* Per-widget item spacing is no longer configurable; list gap uses the global `--item-gap` token.
- [N-15] `label`: overrides displayed header title.
- [N-16] Config has Source / Display / Refresh tabs; Source uses `AccountToggleList` (no token field).

## Context menu
- [N-17] Right-clicking a notification item opens an item-level menu with:
  - "Open in browser": invokes Tauri `open_url`.
  - "Mark as read": `PATCH {apiBase}/notifications/threads/{id}` then refresh; disabled when item is already read.
  - "Mark all in {repo} as read": `PUT {apiBase}/repos/{repo}/notifications` then refresh.
- [N-18] Widget-level context menu has "Refresh" (via `_refresh` bump).

## Data / backend
- [N-19] `GET {apiBase}/notifications?all={includeRead}&participating={participatingOnly}&per_page={count}` per account.
- [N-20] Uses `Authorization: Bearer <token>` + `Accept: application/vnd.github+json`.
- [N-21] Multi-account merge: each account fetched in parallel via `Promise.allSettled`; results concatenated and sorted desc by `updated_at`, then sliced to `count`. Failed accounts are logged and skipped.
- [N-22] Subject `html_url` resolved from `subject.url` (preferred) via `subjectUrlToHtml`, falling back to `repository.html_url` when the subject URL is absent or is a non-convertible API URL (e.g. a `…/comments/<id>` comment URL, which carries no issue/PR number). `latest_comment_url` is not used for navigation: a comment API URL cannot be turned into a web URL and would 404.

## Interactions
- [N-23] Clicking an item invokes Tauri `open_url` with the resolved `html_url`.

## Caching
- [N-24] Polls every `refreshMinutes` minutes via `createPolling`; pauses while app inactive.
- [N-25] Cache key composition: `{accountIds.join(',')}|{count}|{'all'|'unread'}|{'part'|'any'}`. Token deliberately excluded: never fingerprint by secrets.
- [N-26] Cached items persist across app restarts via localStorage (`themia.cache.github-notifications.*`).

## Accounts
- [N-27] Account-aware: each item carries its `account_id` so mark-as-read calls use the correct account's token and host.
- [N-28] When 0 accounts are selected, the widget shows the empty prompt and performs no network calls.
