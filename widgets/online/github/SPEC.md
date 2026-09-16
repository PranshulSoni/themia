# GitHub: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Shows empty prompt "Select repository to see your activity" when no repo configured.
- [S-02] Shows "Loading..." while first fetch is in flight and no repo data cached.
- [S-03] Shows error message on fetch failure; 404 produces hint to add a token with repo scope.
- [S-04] Header row displays repo full_name (or custom label override), latest release tag badge, and latest workflow-run status badge (passing / failing / running / pending).
- [S-05] Stats row shows stargazer count, fork count, and open-issue count formatted with k/M suffixes.
- [S-06] Meta row shows primary language, "Updated <time ago>" of last push, and default branch name.
- [S-07] Commit list shows short SHA (7 chars), first line of message, and relative time.
- [S-08] PR list shows #number colored by state (purple merged, green open, red closed), title, relative creation time.
- [S-09] Issue list shows #number, title, relative creation time; PRs filtered out of issue list.
- [S-10] Pipeline list shows colored status dot (yellow in-progress/queued, green success, red failure, muted other), workflow name, head branch, relative time.
- [S-11] Release badge clickable; opens release URL.

## Configuration
- [S-12] `repo`: "owner/repo" or full URL; supports github.com and GitHub Enterprise (api/v3 prefix).
- [S-13] Auth token lives on a central `github`-provider account (Accounts panel), not in widget config. Widget config field `accountIds: string[]` selects which account to use; the widget reads its access token at fetch time via Tauri `email_get_github_token`. Token is never persisted in widget config or in the cache key.
- [S-14] `commitCount` (0-10, default 3): number of recent commits; 0 hides commit list.
- [S-15] `prCount` (0-10, default 3): number of PRs; 0 hides PR list.
- [S-40] `prState`: PR state filter, `open` | `closed` | `all` (typed union, default `open`). `closed` includes merged PRs (anything not open). Hidden in config when `prCount` is 0.
- [S-41] `prSort`: PR sort order by `created_at`, `newest` | `oldest` (typed union, default `newest`). Hidden in config when `prCount` is 0.
- [S-16] `issueCount` (0-10, default 3): number of open issues; 0 hides issue list.
- [S-17] `pipelineCount` (0-10, default 5): number of workflow runs; 0 hides pipeline list.
- [S-18] `hideHeader`: toggles widget title bar.
- [S-19] *(removed)* Per-widget item spacing is no longer configurable; list gap uses the global `--item-gap` token.
- [S-20] `refreshMinutes`: 5 / 15 / 30 / 60 (default 5).
- [S-21] `label`: overrides displayed repo name.
- [S-22] Config has Source / Display / Refresh tabs; Source tab renders an `AccountToggleList` filtered to the `github` provider (no token input). Adding a token happens via the Accounts panel's GitHub provider.

## Context menu
- [S-23] "Refresh" action triggers immediate reload via `_refresh` bump.

## Data / backend
- [S-24] Fetches `/repos/{owner}/{repo}` for metadata.
- [S-25] Fetches `/repos/{owner}/{repo}/actions/runs?per_page={pipelineCount}` for workflows.
- [S-26] Fetches `/repos/{owner}/{repo}/commits?per_page={commitCount}`.
- [S-27] Fetches `/repos/{owner}/{repo}/pulls?state=all&sort=created&direction=desc&per_page={min(prCount*3+5,100)}` (over-fetch all states), then applies the `prState` filter + `prSort` order client-side and slices to `prCount`.
- [S-28] Fetches `/repos/{owner}/{repo}/issues?state=open&per_page={issueCount+5}` then filters out PRs.
- [S-29] Fetches `/repos/{owner}/{repo}/releases/latest`.
- [S-30] Uses `Accept: application/vnd.github+json` and optional `Authorization: Bearer <token>` header.
- [S-31] Polls every `refreshMinutes` minutes via `createPolling`.
- [S-32] Demo mode returns `demoGithub()` fixture.

## Interactions
- [S-33] Clicking a commit, PR, issue, pipeline run, or release badge invokes Tauri `open_url` to open the corresponding html_url.

## Caching
- [S-34] Cached repo activity persists across app restarts (localStorage); cold launch shows last-known stats/commits/PRs/issues/runs instantly without "Loading..." flash.
- [S-35] GitHub widgets configured with the same repo + count knobs share a single cache entry: only one batch of API requests fires per refresh. Auth token is NOT part of the cache key (avoid fingerprinting secrets).
- [S-36] On fetch failure when cached repo data is present, last-known data remains visible (error state suppressed).

## Accounts
- [S-37] Account picker uses `AccountToggleList` filtered to the `github` provider. First selected account id drives auth and host resolution.
- [S-38] Legacy `config.token` auto-migrates to a github account on first config open via Tauri `email_add_github_account`. On success the token field is removed from widget config and `accountIds` is populated; on failure the token field is preserved and an inline error is shown.
- [S-39] GitHub account stores `api_base_url` (default `https://api.github.com`). When an account is selected, that URL overrides any host parsed from the `repo` field, so the widget repo field can be just `owner/repo`. The selected account id is included in the cache key so widgets pointing at different accounts/hosts do not collide.
