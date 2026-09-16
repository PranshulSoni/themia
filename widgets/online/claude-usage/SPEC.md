# Claude Usage: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Header shows label ("Claude Usage" by default, or `label` override).
- [S-02] Empty state "Connect a Claude account…" when no account is selected.
- [S-03] Shows "Loading…" on first fetch with no cached data; the error string when a fetch fails with no cached data.
- [S-04] Renders one bar per window: "Session (5h)" (`five_hour`) and "Weekly (all models)" (`seven_day`) always; "Weekly (Opus)" / "Weekly (Sonnet)" whenever the matching config toggle is on. A per-model window the endpoint reports as `null` (no active limit) is shown as a 0% bar rather than hidden, so a toggled-on model always renders a bar. Row assembly is the pure `buildBars` in `claudeUsage.ts`, which also carries each row's short and mini label ([S-18]).
- [S-05] Each bar shows a rounded percentage, a fill whose width = the percentage, and (when the window's `resets_at` is in the future) a "resets in …" line. The reset line renders only at the `full` density ([S-17]) and keeps its slot there even when that window has no reset, so every row in the widget is the same height. The endpoint's `utilization` is already a percentage (0..100) and is only clamped, never rescaled — `utilizationPct` must not treat a small value as a 0..1 fraction. [U-01]
- [S-06] Bar color is severity-based: accent (`--primary`) below 70%, amber at 70–89%, `--destructive` at 90%+.

## Data / backend
- [S-07] Usage is read from `GET https://api.anthropic.com/api/oauth/usage` (header `anthropic-beta: oauth-2025-04-20`, bearer = the stored Claude access token) via the Rust command `email_get_claude_usage`. The token is refreshed when (near-)expired before the call, and on a 401 the call forces one refresh and retries once (a token invalidated before its stored expiry self-heals). The response is parsed strictly into `{ five_hour, seven_day, seven_day_opus, seven_day_sonnet }`; unknown buckets are dropped, a non-JSON body is a hard error (never a silent all-zero default). 401 → "reconnect" and 403 → a distinct "missing permission" message that surfaces Anthropic's own reason; neither ever deletes the stored account (N2).
- [S-08] The Claude account is added via an OAuth authorization-code + PKCE flow (`email_start_claude_auth` opens the consent page; `email_complete_claude_auth` exchanges the pasted `code#state`). It requests the **`user:profile`** scope, which the usage endpoint requires — a `claude setup-token` token carries only `user:inference` and is rejected 403 (issue #72), so the setup-token path was removed. Tokens are validated against the usage endpoint before persisting and stored in the shared account store as provider `claude` with an access + refresh token (DPAPI-encrypted at rest), refreshed single-flight per account. The OAuth endpoints are undocumented (they mirror the CLI's own subscription login) and may change without notice. Themia never reads Claude Code's own `~/.claude/.credentials.json`.

## Configuration
- [S-09] `accountIds`: selected Claude connection(s); first id is used. Picker filters the account store to provider `claude`.
- [S-10] `showOpus` / `showSonnet` (default false): reveal the per-model weekly bars.
- [S-11] `refreshMinutes`: 5–60 (default 15); clamped. Kept slow to respect the endpoint's rate limits.
- [S-12] `label`: optional header title override.

## Context menu
- [S-13] "Refresh" action triggers an immediate reload via `_refresh` bump.

## Caching / refresh
- [S-14] Polls every `refreshMinutes` via `createPolling`; cached per account id (never keyed by the token).
- [S-15] Demo mode returns `demoClaudeUsage()` and bypasses the account requirement.

## Fitting the widget box
- [S-17] The body never scrolls: the rows are drawn at the densest layout that still fits the measured height, so the content always fits the window. `usageDensity(bodyHeight, rowCount, fontScale)` picks one of four, loosest first — `full` (label + percentage, bar, reset line), `compact` (drops the reset line), `tight` (label · bar · percentage on one line), `micro` (the same line, smaller, rows sharing the leftover height equally so any height still fits). The per-row budgets in `DENSITY_BUDGET` must stay in sync with `ClaudeUsage.css`; the global font scale multiplies the text part of each budget, so bigger text drops a layer sooner. `.claude-usage__rows` is measured with a `ResizeObserver` (plus one synchronous read on mount, so the first paint is already right) — it takes its size from the flex parent and carries no padding, so the density it selects can never change what was measured. Before the first measurement (`NaN`) and while there are no bars, the density is `full`; a MEASURED zero — a widget whose header and padding already used the whole box — is the opposite case and lands on `micro`, the only layout with no height floor. `micro` also carries no row gap: a flex gap is a fixed cost that would outgrow a very short box on its own, so the space between bars comes out of the (shrinking) row heights instead.
- [S-18] Labels shorten with the WIDTH rather than disappearing: each bar carries three variants — "Session (5h)" / "Session" / "5h", "Weekly (all models)" / "Weekly" / "7d", "Weekly (Opus)" / "Opus" / "Opus", "Weekly (Sonnet)" / "Sonnet" / "Son." — and `labelSize(bodyWidth, density, fontScale)` picks one from the width actually left for the label, which is smaller on a one-line (`tight`/`micro`) row because the bar and percentage share that line. Below `full` the reset line drops its "resets in " prefix and shows the duration alone. Each row's `title` always carries the full label and percentage, so nothing the layout cut is unreachable. Body padding shrinks by CSS container query instead, since it depends only on the widget box and not on the bar count. The HEADER is never part of this: it follows `headerMode` ([S-01]) at every size, and no width or height makes the widget hide it.

## Pre-release gate
- [S-16] (removed) The Claude Code feature shipped generally available: the `devOnly` pre-release gate was dropped from both the widget registry (`index.tsx`) and the `claude` connect provider (`src/connect/providers.tsx`), so the widget and connection are visible to all users regardless of `globalDesign.devMode`.

## Open / unverified
- [U-01] (resolved) `utilization` is a percentage (0..100), confirmed against a live Max 5x payload (`42.5` = 42.5%). The adaptive fraction branch is gone: it made a real 1% session render as 100%.
- [U-02] The usage endpoint is undocumented and may change shape or auth without notice.
