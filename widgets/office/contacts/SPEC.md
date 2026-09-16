# Contacts Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Shows contacts merged from all configured Microsoft, Google, and/or CardDAV accounts.
- [S-02] Header shows a users icon and "Contacts" title, hidden when `hideHeader` is true.
- [S-03] Search bar under the header filters contacts by name, email, company, or job title.
- [S-04] Empty state prompts the user to select an account when none configured.
- [S-05] Loading state shown while initial fetch is pending.
- [S-06] Reauth state shows a lock icon and "Sign Out & Re-connect" button when a backend auth error occurs.
- [S-07] Error state shows the raw error message for non-auth failures.
- [S-08] "No contacts found." message shows when the filtered list is empty.
- [S-09] Avatars display two-letter initials on an HSL-hashed background color derived from the display name.
- [S-10] List view shows avatar, name, job title · company, and email/phone rows.
- [S-11] Compact view shows a small avatar, name, and a single secondary field (email or company or phone).
- [S-12] Details view shows a sortable-style table with user-selected columns (Name / Email / Phone / Company / Title).
- [S-13] Grid view shows avatar cards with name and company (or job title if no company).

## Configuration
- [S-14] `accountIds`: multi-select Microsoft/Google/CardDAV accounts.
- [S-15] `viewMode`: `list` | `compact` | `details` | `grid`.
- [S-16] `rowColumns`: columns visible in details view (Name, Email, Phone, Company, Title); stays in fixed canonical order, at least one must remain active.
- [S-17] `count`: max number of contacts to show (min 5, step 5, **no upper bound** — the stepper's plus button never disables).
- [S-18] `defaultSearch`: pre-filter query applied when the widget first loads.
- [S-19] `hideHeader`: toggle widget title bar (also hides the search bar).
- [S-20] *(removed)* Per-widget item spacing is no longer configurable; list gap uses the global `--item-gap` token.

## Context menu
- [S-21] Widget chrome right-click offers Refresh and view-mode switcher (List/Compact/Details/Grid).
- [S-22] Contact right-click shows a header line with display name and job·company, then contact actions: Send email / Copy email (if email present), Call / Message / Copy phone (if phone present), Copy name, plus a Refresh entry.

## Data / backend
- [S-23] Contacts fetched via `contacts_list` Tauri command across all configured accounts in parallel; per-account fetch size is `clamp(count, 500, 1000)` — never below 500 so search spans the address book, never above the 1000-item page limit Graph (`$top`) and Google People (`pageSize`) accept.
- [S-24] `contacts_not_authorized` error surfaces the reauth UI.
- [S-25] Reauth signs out the selected accounts via `email_sign_out_account`.
- [S-26] Mail / tel / sms links open through `open_in_explorer`.
- [S-27] Copy actions use `navigator.clipboard.writeText`.
- [S-28] Reloads when the account filter, count, or `_refresh` token changes.

## Interactions
- [S-29] Clicking a contact toggles selection highlight; clicking the widget background deselects.
- [S-30] Right-click selects the item and opens its context menu.
- [S-31] Arrow Up/Down move selection; Home/End jump to first/last; Escape clears selection.
- [S-32] Enter on a selected contact opens `mailto:` if email exists, else `tel:` if phone exists.
- [S-33] Typing in the search box filters the list live; X button clears the query.
- [S-34] Focusing the search input pushes widget windows to the desktop layer (`push_windows_to_desktop`).
- [S-35] Keyboard input routed via the Rust low-level hook; mousedown on the widget activates it.
- [S-36] Account list also accepts CardDAV accounts; CardDAV contacts use the same `contacts_list` dispatch path (provider-dispatched to `fetch_carddav_contacts`).
- [S-37] The Source tab's account toggle list is scoped to contacts-relevant providers (Microsoft / Google / CardDAV): only those connections appear as toggles. The "Manage connections" button opens the full Connections page **unfiltered**: every connection is listed and the same **Add connection** button is offered ([S-40 email]), identical to reaching it from the sidebar. (Per-widget scoping lives only in the toggle list, never on the Connections page.)
- [S-38] Losing application focus clears the selection highlight. Clicking outside the Themia app entirely (another app or the desktop) lands no DOM click in the webview, so the background-click deselect ([S-29]) can't catch it; the Rust `WH_MOUSE_LL` mouse hook's `themia-blur` broadcast clears the selection so it isn't pinned across app-switches (focus-independent, fires on every physical click outside all themia windows; the foreground hook also broadcasts it for keyboard focus changes, and a DOM `window blur` fallback exists for dev/non-Tauri). Mirrors the folder ([S-61]) and shortcut ([S-64]) widgets.
