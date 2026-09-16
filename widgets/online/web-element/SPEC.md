# Web Element: Spec

Pins a single element from any web page and keeps it shown, refreshing on an
interval. The page is rendered by a REAL off-screen WebView2 that executes the
site's JavaScript (so SPAs / Google etc. work); the widget then clips to the
pinned element. Behavior tags are stable IDs: when adding behavior append a new
`[S-NN]`; never renumber or reuse. Mirror these tags in test names.

## Display
- [S-01] With no `url` or no `selector`, shows the empty prompt ("Pick an element
  from a website…") with a settings-gear hint.
- [S-02] The picker computes a stable, unique CSS selector for the clicked
  element (`cssPath`): a unique-id anchor when available, else a
  `tag:nth-of-type(n)` chain up to (not including) `<html>`. Invariant:
  `querySelector(cssPath(el)) === el`. `nth-of-type` (not `nth-child`) is used so
  the selector survives unrelated sibling insertions between refreshes.
- [S-03] The matched element's POST-JS HTML is rendered inside a **sandboxed,
  script-free iframe** (`sandbox=""`). Relative image/link URLs are absolutized
  against the source page URL at build time (`absolutizeUrls`), NOT via a
  `<base href>` — the sandboxed frame inherits the app CSP whose `base-uri 'self'`
  silently refuses a remote `<base>`, so a `<base>` would leave every relative URL
  broken. Assembled by `buildSrcdoc`. (The HTML comes from the WebView2 renderer,
  [S-16], so JS-generated content is present.)
- [S-04] Render fidelity: the page's inline `<style>` blocks plus up to 6 external
  stylesheets (fetched via `http_get`, `url(...)` rebased to absolute, capped at
  512 KB total) are inlined. Remote web fonts won't load (CSP `font-src`) →
  fallback font. Excess sheets/bytes are dropped, never fatal.
- [S-05] When the selector matches nothing on the (re-)rendered page, shows a
  distinct "Element not found" state, NOT the generic error state.
- [S-06] Render failure shows the error message; extraction never throws (an
  invalid selector is treated as not-found).
- [S-25] On a transient render failure when a good render is already cached, the
  last-good render stays visible (error suppressed), same as the shared cache's
  keep-last-good behavior.
- [S-10] The display iframe is `pointer-events:none`, a passive clip: no
  scrolling, no link navigation, no focus steal (suits always-on-desktop widgets).
- [S-11] Header shows the widget icon + a title: custom `label` → page hostname →
  the generic widget label ("Web Element"). Honors header mode (normal/compact/none).

## Configuration
- [S-07] `url` (default `''`): the page to render.
- [S-08] `selector` (default `''`): CSS selector of the pinned element, produced
  by the picker. `pickedLabel` (cosmetic) holds a friendly description shown in the
  config "Pinned element" row only; the display never reads it.
- [S-09] `refreshMinutes` (default 15, clamped to [5, 120]): config parses through
  `resolveConfig`; absent/corrupt values fall back to typed defaults, never crash.
- [S-12] Config has Source (URL + picker + current selection) and Refresh tabs.
- [S-33] Pre-release: discoverable in the Add-Widget picker only when developer
  mode (`globalDesign.devMode`) is on (registry `devOnly`, same gate as the
  stack). Existing instances always render for everyone; the flag gates
  DISCOVERY only, never user data.

## Picker (live WebView2 window)
- [S-13] "Pick element" opens a fresh picker WINDOW via the
  `open_web_element_picker_window` Rust command, built with
  `WebviewUrl::External(url)`, i.e. it loads the REAL live site (JS runs).
  `src-tauri/picker-inject.js` is injected via Tauri `initialization_script` and
  runs in the page's own realm, so it handles REAL clicks natively. The command
  SSRF-guards the URL (`check_url`) and closes any prior picker first.
- [S-14] The toolbar has a **Browse/Pick toggle**. In BROWSE mode (default) the page
  is fully interactive: the user can dismiss cookie/consent banners, expand menus,
  scroll, even navigate to another page; the script intercepts nothing. Clicking
  "Pick element" enters PICK mode: hovering outlines the element, the next click
  pins it (`preventDefault` stops the site's own click/navigation) and posts
  `{kind:'themia-picked', selector, label, url}` over the native WebView2 channel
  (`window.chrome.webview.postMessage`). `url` is the page's CURRENT location, so
  navigating before picking works. Esc leaves PICK mode back to BROWSE.
- [S-32] Rust's `add_WebMessageReceived` handler emits `web-element-picked`
  ({ widgetId, url, selector, pickedLabel }) to the config window, using the
  message's `url` (the page the user actually picked on), and closes the picker.
  The config applies it only when `widgetId` matches its instance. Cancel posts
  `{kind:'themia-cancel'}` → close.
- [S-31] `cssPath` in `picker-inject.js` MIRRORS `webElement.ts cssPath` (an
  injected script can't import); the unit-tested TS version is the reference.

## Security
- [S-08b] The remote page runs in its OWN webview, ISOLATED from the app webview,
  and is granted NO Tauri IPC (`withGlobalTauri` off, no remote-IPC capability);
  it can only use the native `window.chrome.webview.postMessage`, which Rust parses
  through a typed shape. Loading a user URL in a webview bypasses the `http_get`
  guard, so BOTH the render and picker commands call `check_url` first. The
  extracted display HTML is still sanitized + shown only in the no-script
  `sandbox=""` iframe (`sanitizeForRender`). No app CSP is relaxed.

## Context menu
- [S-15] "Refresh" action triggers an immediate re-fetch via the `_refresh` bump.

## Data / backend
- [S-16] Page HTML comes from `web_element_render_html`, an off-screen, reused,
  off-screen-VISIBLE WebView2 (label `webelement-render`) that loads the URL,
  executes JS, waits for the DOM to settle (`render-inject.js`: 600 ms mutation-
  idle, 8 s cap), and returns post-JS `documentElement.outerHTML` over the native
  WebView2 channel. SSRF-guarded; serialized through the one webview; 12 s timeout.
  Stylesheets are still inlined via `http_get` ([S-04]).
- [S-20] Polls every `refreshMinutes` minutes via `createPolling`; renders cached
  via `createCachedFetch` (namespace `web-element`, keyed on `url::selector`).
- [S-21] Demo mode: `web_element_render_html` returns a fixture page
  (`demoWebElementRender`), so the display previews without a backend. The live
  picker is native-only (no demo/browser-preview equivalent).

## Limitations
- [S-30] Windows-only (WebView2). Content inside cross-origin iframes or shadow DOM
  may not serialize into `outerHTML`; web fonts don't load ([S-04]). The display is
  a periodic rendered snapshot, not a live interactive view (chosen tradeoff).
