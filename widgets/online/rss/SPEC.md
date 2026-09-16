# RSS Feed: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Shows empty prompt "Select feed to see your articles" with settings-gear hint when no feeds are selected (no `feedIds`, no legacy `feedUrl`).
- [S-02] Shows "Loading..." on first load with no cached items.
- [S-03] Shows error message on fetch or parse failure (e.g., "No items found in feed").
- [S-04] Header shows RSS icon and a title; hidden when `hideHeader` true or no title resolves. Title precedence: custom `label` → the single feed's own `<title>` → (multiple feeds) the selected feeds' names joined with ", " → the generic widget label ("RSS Feed"). The multi-feed fallback exists so selecting 2+ feeds never collapses the header.
- [S-05] Each item shows title, optional snippet (plaintext-stripped from HTML), and relative time if pubDate/updated/published present.
- [S-26] Each item optionally shows a source row above the title: site favicon (Google s2 favicon service, keyed on item link hostname) and the hostname (www. stripped). Favicon falls back to a globe icon on load error. Hidden when item link is not a valid URL.
- [S-27] At container width ≤160px the source hostname label is hidden but the favicon remains.
- [S-06] Time ago formatted as "just now", "Nm ago", "Nh ago", "Nd ago", or localized month/day for older entries.
- [S-07] Scrollable list with bottom fade overlay.

## Configuration
- [S-08] `feedIds` (default `[]`): ids of managed RSS feed connections (provider `rss`) to show. Selected from a toggle list on the Source tab (no free-text URL field), scoped to the `rss` provider; "Manage connections" opens the full Connections page **unfiltered** (every connection + add-service provider shown, identical to the sidebar).
- [S-29] Feeds are managed as connections, not per-widget: added/renamed/removed on the Connections page (provider `rss`), stored in the shared account store. Adding a feed validates it by fetching once; the feed's `<title>` becomes the connection's display name (fallback: host). A feed is referenced by every widget that selects it.
- [S-30] A widget aggregates all selected feeds into one list, merged and sorted newest-first across feeds; undated items sink to the bottom; the combined list is capped at `maxItems`. Per-item source rows (S-26) make mixed sources legible.
- [S-31] Legacy `feedUrl` (single-feed string) auto-migrates on launch into a feed connection, rewriting the widget to `feedIds`. Until migration runs (or if it fails, e.g. offline), `feedUrl` still works as a single-feed fallback.
- [S-09] `maxItems` (default 20, range 5-50): caps number of items parsed from feed.
- [S-36] `maxItems` is a **display cap, not a fetch count**. Themia asks the feed
  URL for whatever that URL serves and then trims to `maxItems`; it never asks
  for more. A host that serves fewer (Inoreader's stream endpoints default to 20
  and take an `n=` query param) can never fill a larger setting, and raising the
  number changes nothing. The config hint says so, because #81 read the old
  wording as "fetch 50".
- [S-32] `fields` (default `['source','preview','date']`): a multi-select on the Display tab ("Fields") choosing which optional per-item fields render: `source` (favicon + hostname row), `preview` (snippet), `date` (relative time). The item title is the primary line and always shows. Mirrors the email widget's field picker (`MetricMultiSelect`).
- [S-10] `showSnippet` / [S-28] `showSource` (legacy booleans): honored only when `fields` is absent (pre-migration widgets); `showSource`→`source`, `showSnippet`→`preview`, with `date` implicitly on (date was always shown before the field picker). The first edit of the Fields control writes `fields` and drops both booleans.
- [S-11] `hideHeader`: toggles widget title bar.
- [S-12] *(removed)* Item spacing is no longer configurable; the list gap is fixed at 0px.
- [S-13] `refreshMinutes`: 5 / 15 / 30 / 60 (default 15).
- [S-14] Config has Source / Display / Refresh tabs.

## Context menu
- [S-15] "Refresh" action triggers immediate reload via `_refresh` bump.

## Refresh feedback
- [S-33] A fetch in flight is visible. A badge in the widget's top-right corner
  spins while `cache.refreshing()` is true, in **every** header mode (including
  `none`) and whether or not a title resolved. Rationale (#81): `loading` covers
  only the first load, and a widget with cached items renders those items
  whether a refresh succeeded, changed nothing, or failed, so the context-menu
  Refresh [S-15] previously had **no observable effect at all** and was reported
  as a dead button while firing correctly.
- [S-34] A failed refresh is visible without being destructive. Items stay on
  screen ([S-25]); the corner badge switches to a warning icon whose tooltip
  names the real reason and the age of what is being shown ("Last refresh
  failed: HTTP 403 Forbidden. Showing items from 2d ago."). Applies to a total
  failure and to a partial one, where some feeds in the set returned and others
  did not (`FeedData.failures`).
- [S-35] Every failure carries its real cause end to end: `http_get` rejects a
  non-2xx with the status (`HTTP 403 Forbidden`) instead of returning the
  challenge/error page as if it were the document, logs it, and the widget
  attributes it to the feed that failed (`Tech News: HTTP 403 Forbidden`). The
  generic "No items found in feed" now means only what it says - a feed that
  fetched and parsed but held no items.

## Data / backend
- [S-16] Fetches feed XML via Tauri `http_get` command (avoids browser CORS),
  sending `Accept: application/rss+xml, …` so a host that serves the human HTML
  page to `*/*` returns the feed instead. The add-a-feed validation in
  `widgets/office/email/src/lib.rs` sends the byte-identical header and the same
  User-Agent (netproxy [PX-10]); they disagreed until #81, so a feed could
  validate on add and then be challenged on every refresh.
- [S-17] Parses RSS 2.0: reads `channel > title`, `item > title/link/pubDate/description`.
- [S-18] Parses Atom: reads `feed > title`, `entry > title`, `entry > link[href]`, `entry > updated` (fallback `published`), `entry > summary` (fallback `content`).
- [S-19] Snippet text has HTML tags stripped via DOM textContent extraction.
- [S-20] Polls every `refreshMinutes` minutes via `createPolling`.
- [S-21] Demo mode exposes RSS feed connections via the `email_list_accounts` demo stub; the `http_get` demo stub returns a per-host fixture feed keyed on the feed URL, so multiple demo feeds aggregate into a mixed-source list. With no feeds selected, falls back to the `demo://feed` fixture.

## Interactions
- [S-22] Clicking an item invokes Tauri `open_url` with the item link.

## Caching
- [S-23] Cached feed items persist across app restarts (localStorage); cold launch shows last-known articles instantly without "Loading..." flash.
- [S-24] RSS widgets with the same set of feeds (sorted feed URLs) and maxItems share a single cache entry: only one batch of network requests fires per refresh.
- [S-25] On fetch failure when cached items are present, last-known items remain
  visible rather than being replaced by an error screen. The failure is not
  hidden, though: it surfaces through the [S-34] badge. Suppressing it outright
  is what let a feed sit two days stale with no indication (#81).
