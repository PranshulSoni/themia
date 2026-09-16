// Injected (via Tauri initialization_script) into the OFF-SCREEN render webview
// that loads a live site for the Web Element widget. Runs in the page realm
// BEFORE the page's own scripts, on every navigation.
//
// Job: once the page has loaded and the DOM has gone quiet (JS-rendered content
// settled), send the post-JS `document.documentElement.outerHTML` back to the
// Rust host over the native WebView2 channel. The host (lib.rs
// `web_element_render_html`) forwards it to the widget, which clips to the
// selected element and renders it in its sandboxed iframe.
//
// No Tauri IPC is used, only `window.chrome.webview.postMessage`, the native
// host↔page channel, so the remote page never gains `invoke()` access.
(function () {
  // Top frame only: sub-frames (ads, embeds) must not post their own HTML.
  if (window.top !== window) return;
  // about:blank is the webview's idle state between renders, never report it.
  if (/^about:/i.test(location.href)) return;

  // A background render page must not pop windows or block on dialogs.
  try { window.open = function () { return null; }; } catch (e) { /* ignore */ }
  try {
    window.alert = function () {};
    window.confirm = function () { return false; };
    window.prompt = function () { return null; };
  } catch (e) { /* ignore */ }

  var done = false;
  var lastMutation = Date.now();
  var observer = null;

  function post() {
    if (done) return;
    done = true;
    if (observer) { try { observer.disconnect(); } catch (e) { /* ignore */ } }
    var html = '';
    try { html = document.documentElement ? document.documentElement.outerHTML : ''; } catch (e) { /* ignore */ }
    try { window.chrome.webview.postMessage(JSON.stringify({ kind: 'themia-html', html: html })); } catch (e) { /* ignore */ }
  }

  // Quiet-period settle: report once the DOM has been idle for 600ms, or after a
  // hard 8s cap, event/condition-driven, not a fixed sleep.
  function tick() {
    if (done) return;
    if (Date.now() - lastMutation >= 600 || performance.now() >= 8000) post();
    else setTimeout(tick, 150);
  }

  function start() {
    try {
      observer = new MutationObserver(function () { lastMutation = Date.now(); });
      observer.observe(document.documentElement, { subtree: true, childList: true, attributes: true, characterData: true });
    } catch (e) { /* ignore */ }
    lastMutation = Date.now();
    setTimeout(tick, 150);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
  else start();

  // Absolute backstop so the host's await always resolves well within its timeout.
  setTimeout(post, 9000);
})();
