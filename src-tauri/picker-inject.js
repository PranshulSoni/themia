// Injected (via Tauri initialization_script) into the LIVE picker webview that
// loads the real site for the Web Element widget. Runs in the page realm, so it
// handles REAL clicks natively (no cross-realm/parent-listener fragility).
//
// Two modes:
//   • BROWSE (default): the page is fully interactive: dismiss cookie/consent
//     banners, expand menus, scroll, even navigate to another page. We touch
//     nothing.
//   • PICK: hovering outlines the element under the cursor; clicking pins it,
//     computing a stable CSS selector and sending it (plus the CURRENT url, so
//     navigating first works) to the Rust host over the native WebView2 channel
//     (`window.chrome.webview.postMessage`). No Tauri IPC; the remote page never
//     gains `invoke()`.
//
// cssPath MIRRORS widgets/online/web-element/webElement.ts `cssPath` (kept in
// sync; the unit test there is the reference). An injected script can't import,
// hence the copy.
(function () {
  if (window.top !== window) return; // top frame only
  if (/^about:/i.test(location.href)) return;

  var BAR_ID = '__themia_pick_bar__';
  var HILITE = '__themia_pick_hilite__';
  var PICKING = '__themia_picking__'; // on <html> while in pick mode

  var mode = 'browse'; // 'browse' | 'pick'
  var hovered = null;

  function isUsableId(el, id) {
    if (!/^[A-Za-z][\w-]*$/.test(id)) return false;
    try { return el.ownerDocument.querySelectorAll('#' + id).length === 1; } catch (e) { return false; }
  }
  function cssPath(el) {
    if (!el || el.nodeType !== 1) return '';
    var parts = [];
    var node = el;
    while (node && node.nodeType === 1) {
      var tag = node.tagName.toLowerCase();
      if (tag === 'html') break;
      var id = node.getAttribute('id');
      if (id && isUsableId(node, id)) { parts.unshift('#' + id); return parts.join(' > '); }
      var seg = tag;
      var parent = node.parentElement;
      if (parent) {
        var same = Array.prototype.filter.call(parent.children, function (c) { return c.tagName === node.tagName; });
        if (same.length > 1) seg += ':nth-of-type(' + (same.indexOf(node) + 1) + ')';
      }
      parts.unshift(seg);
      node = node.parentElement;
    }
    return parts.join(' > ');
  }
  function labelOf(el) {
    var t = (el.textContent || '').replace(/\s+/g, ' ').trim();
    return t ? t.slice(0, 48) : el.tagName.toLowerCase();
  }
  function send(obj) {
    try { window.chrome.webview.postMessage(JSON.stringify(obj)); } catch (e) { /* ignore */ }
  }
  function inBar(el) {
    while (el) { if (el.id === BAR_ID) return true; el = el.parentElement; }
    return false;
  }
  function clearHover() {
    if (hovered) { hovered.classList.remove(HILITE); hovered = null; }
  }

  function setMode(next) {
    mode = next;
    var picking = mode === 'pick';
    document.documentElement.classList.toggle(PICKING, picking);
    if (!picking) clearHover();
    var btn = document.getElementById('__themia_pick_toggle__');
    var hint = document.getElementById('__themia_hint__');
    if (btn) { btn.classList.toggle('__themia_on', picking); btn.textContent = picking ? '✓ Picking, click an element' : '◉ Pick element'; }
    if (hint) hint.textContent = picking
      ? 'Click any element to pin it · Esc to keep browsing'
      : 'Browse freely (dismiss popups, navigate), then “Pick element”.';
  }

  function onOver(e) {
    if (mode !== 'pick') return;
    var t = e.target;
    if (!t || t.nodeType !== 1 || inBar(t)) return;
    if (hovered && hovered !== t) hovered.classList.remove(HILITE);
    if (t.tagName !== 'HTML' && t.tagName !== 'BODY') { t.classList.add(HILITE); hovered = t; }
  }
  function onClick(e) {
    var t = e.target;
    if (!t || t.nodeType !== 1 || inBar(t)) return; // toolbar handles its own clicks
    if (mode !== 'pick') return;                    // BROWSE: let the page have the click
    e.preventDefault();
    e.stopPropagation();
    clearHover();
    var sel = cssPath(t);
    if (!sel) return;
    send({ kind: 'themia-picked', selector: sel, label: labelOf(t), url: location.href });
  }
  function onKey(e) {
    if (e.key === 'Escape' && mode === 'pick') { e.preventDefault(); setMode('browse'); }
  }

  function build() {
    if (document.getElementById(BAR_ID)) return;

    var style = document.createElement('style');
    style.textContent =
      'html.' + PICKING + ' *{cursor:crosshair !important;}' +
      '.' + HILITE + '{outline:2px solid #60cdff !important;outline-offset:-2px;' +
      'background:rgba(96,205,255,0.12) !important;}' +
      '#' + BAR_ID + '{position:fixed!important;top:0!important;left:0!important;right:0!important;' +
      'z-index:2147483647!important;display:flex!important;align-items:center!important;gap:12px!important;' +
      'padding:10px 14px!important;background:#1e1e1eee!important;color:#fff!important;' +
      'font:600 13px system-ui,sans-serif!important;box-shadow:0 1px 6px rgba(0,0,0,.4)!important;' +
      'backdrop-filter:blur(8px)!important;}' +
      '#' + BAR_ID + ' .__themia_spacer{flex:1!important;}' +
      '#' + BAR_ID + ' button{font:inherit!important;color:#fff!important;background:transparent!important;' +
      'border:1px solid #ffffff44!important;border-radius:6px!important;padding:5px 12px!important;cursor:pointer!important;}' +
      '#' + BAR_ID + ' button.__themia_on{background:#60cdff22!important;border-color:#60cdff!important;color:#aee6ff!important;}';
    (document.head || document.documentElement).appendChild(style);

    var bar = document.createElement('div');
    bar.id = BAR_ID;
    bar.innerHTML =
      '<span id="__themia_hint__"></span>' +
      '<span class="__themia_spacer"></span>' +
      '<button type="button" id="__themia_pick_toggle__"></button>' +
      '<button type="button" id="__themia_cancel__">Cancel</button>';
    (document.body || document.documentElement).appendChild(bar);

    bar.querySelector('#__themia_pick_toggle__').addEventListener('click', function (e) {
      e.preventDefault(); e.stopPropagation();
      setMode(mode === 'pick' ? 'browse' : 'pick');
    }, true);
    bar.querySelector('#__themia_cancel__').addEventListener('click', function (e) {
      e.preventDefault(); e.stopPropagation();
      send({ kind: 'themia-cancel' });
    }, true);

    document.addEventListener('mouseover', onOver, true);
    document.addEventListener('click', onClick, true);
    document.addEventListener('keydown', onKey, true);
    setMode('browse');
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', build, { once: true });
  else build();
})();
