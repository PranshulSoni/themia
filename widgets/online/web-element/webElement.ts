// Pure, DOM-light utilities for the Web Element widget.
//
// Every byte of HTML this widget handles is UNTRUSTED: it comes from an
// arbitrary site fetched through the `http_get` backend. The rules that follow
// from that (CLAUDE.md trust-boundary + security non-negotiables):
//   • Remote HTML is parsed with DOMParser and NEVER injected into the live app
//     DOM. The picked element is rendered only inside a no-script, sandboxed
//     iframe (see WebElement.tsx / WebElementConfig.tsx).
//   • The subtree we do render is sanitized (scripts / handlers / javascript:
//     URLs stripped) as defense-in-depth on top of the sandbox.
//   • Config is parsed through `resolveConfig` into a typed shape; absent and
//     corrupt values both fall back to defaults instead of crashing.

export interface WebElementConfig {
  /** Page to scrape. */
  url: string;
  /** CSS selector of the pinned element (produced by the picker, `cssPath`). */
  selector: string;
  /** Custom header label ('' → widget default). */
  label: string;
  /** Auto-refresh cadence in minutes (clamped to [MIN, MAX]). */
  refreshMinutes: number;
}

export const DEFAULT_REFRESH_MINUTES = 15;
export const MIN_REFRESH_MINUTES = 5;
export const MAX_REFRESH_MINUTES = 120;

/** Narrow an unknown persisted/props bag into a valid `WebElementConfig`. */
export function resolveConfig(raw: unknown): WebElementConfig {
  const o = (raw && typeof raw === 'object') ? raw as Record<string, unknown> : {};
  const str = (v: unknown): string => (typeof v === 'string' ? v : '');
  const rmRaw = o.refreshMinutes;
  const rm = (typeof rmRaw === 'number' && Number.isFinite(rmRaw))
    ? Math.round(rmRaw)
    : DEFAULT_REFRESH_MINUTES;
  return {
    url: str(o.url).trim(),
    selector: str(o.selector).trim(),
    label: str(o.label),
    refreshMinutes: Math.min(MAX_REFRESH_MINUTES, Math.max(MIN_REFRESH_MINUTES, rm)),
  };
}

// ── Selector computation ─────────────────────────────────────────────────────

// Anchor only on ids that are CSS-identifier-safe (no escaping needed) AND
// actually unique in the document; an auto-generated duplicate id would make
// the selector ambiguous.
const SAFE_ID = /^[A-Za-z][\w-]*$/;

function isUsableId(el: Element, id: string): boolean {
  if (!SAFE_ID.test(id)) return false;
  const doc = el.ownerDocument;
  if (!doc) return false;
  try {
    return doc.querySelectorAll(`#${id}`).length === 1;
  } catch {
    return false;
  }
}

/**
 * A stable, unique CSS selector for `el`. Invariant relied on by the tests:
 * `el.ownerDocument.querySelector(cssPath(el)) === el`.
 *
 * Prefers a unique-id anchor (shortest + most resilient to content shifts);
 * otherwise a `tag:nth-of-type(n)` chain up to (but not including) <html>.
 * `nth-of-type` is used over `nth-child` so the selector survives unrelated
 * sibling insertions of other tag names between refreshes.
 *
 * MIRRORED in `src-tauri/picker-inject.js` (an injected script can't import);
 * keep the two in sync; this unit-tested version is the reference.
 */
export function cssPath(el: Element): string {
  if (!el || el.nodeType !== 1) return '';
  const parts: string[] = [];
  let node: Element | null = el;
  while (node && node.nodeType === 1) {
    const tag = node.tagName.toLowerCase();
    if (tag === 'html') break;

    const id = node.getAttribute('id');
    if (id && isUsableId(node, id)) {
      parts.unshift(`#${id}`);
      return parts.join(' > ');
    }

    let seg = tag;
    const parent: Element | null = node.parentElement;
    if (parent) {
      const sameTag = Array.from(parent.children).filter(c => c.tagName === node!.tagName);
      if (sameTag.length > 1) seg += `:nth-of-type(${sameTag.indexOf(node) + 1})`;
    }
    parts.unshift(seg);
    node = node.parentElement;
  }
  return parts.join(' > ');
}

// ── Sanitization ─────────────────────────────────────────────────────────────

const DANGEROUS_TAGS = ['script', 'noscript', 'iframe', 'object', 'embed', 'base', 'meta', 'link'];
const URL_ATTRS = new Set(['href', 'src', 'xlink:href', 'action', 'formaction', 'srcdoc']);

function stripUnsafeAttrs(el: Element): void {
  for (const attr of Array.from(el.attributes)) {
    const name = attr.name.toLowerCase();
    if (name.startsWith('on')) { el.removeAttribute(attr.name); continue; }
    if (name === 'srcdoc') { el.removeAttribute(attr.name); continue; }
    if (URL_ATTRS.has(name) && /^\s*(javascript|vbscript|data:text\/html)/i.test(attr.value)) {
      el.removeAttribute(attr.name);
    }
  }
}

/** Strip dangerous elements + attributes from a subtree, in place. */
function sanitizeElement(el: Element): void {
  el.querySelectorAll(DANGEROUS_TAGS.join(',')).forEach(n => n.remove());
  stripUnsafeAttrs(el);
  el.querySelectorAll('*').forEach(stripUnsafeAttrs);
}

/** Sanitize a raw HTML fragment, returning safe innerHTML. */
export function sanitizeForRender(html: string): string {
  const body = new DOMParser().parseFromString(html, 'text/html').body;
  body.querySelectorAll(DANGEROUS_TAGS.join(',')).forEach(n => n.remove());
  body.querySelectorAll('*').forEach(stripUnsafeAttrs);
  return body.innerHTML;
}

// ── Extraction ───────────────────────────────────────────────────────────────

export interface ExtractResult {
  /** True iff the selector matched an element on the page. */
  found: boolean;
  /** Sanitized outerHTML of the matched element ('' when not found). */
  html: string;
  /** The page's inline `<style>` text, concatenated (for render fidelity). */
  styles: string;
}

function collectInlineStyles(doc: Document): string {
  return Array.from(doc.querySelectorAll('style')).map(s => s.textContent ?? '').join('\n');
}

/**
 * Parse `html`, locate `selector`, and return the sanitized element + page
 * styles. Distinguishes "matched nothing" ([S-05]) from a fetch failure (which
 * is the caller's concern) and NEVER throws; an invalid selector is treated as
 * not-found.
 */
export function extractElement(html: string, selector: string): ExtractResult {
  let doc: Document;
  try {
    doc = new DOMParser().parseFromString(html, 'text/html');
  } catch {
    return { found: false, html: '', styles: '' };
  }
  const styles = collectInlineStyles(doc);

  let el: Element | null = null;
  if (selector) {
    try { el = doc.querySelector(selector); } catch { el = null; }
  }
  if (!el) return { found: false, html: '', styles };

  sanitizeElement(el);
  return { found: true, html: el.outerHTML, styles };
}

// ── External stylesheet inlining (render fidelity) ──────────────────────────

function absUrl(href: string, base: string): string | null {
  try { return new URL(href, base).href; } catch { return null; }
}

/** Absolute URLs of every `<link rel="stylesheet">` in the page. */
export function collectStylesheetLinks(html: string, baseUrl: string): string[] {
  let doc: Document;
  try { doc = new DOMParser().parseFromString(html, 'text/html'); } catch { return []; }
  const out: string[] = [];
  doc.querySelectorAll('link[rel]').forEach(link => {
    const rel = (link.getAttribute('rel') ?? '').toLowerCase();
    if (!rel.split(/\s+/).includes('stylesheet')) return;
    const href = link.getAttribute('href');
    if (!href) return;
    const abs = absUrl(href, baseUrl);
    if (abs) out.push(abs);
  });
  return out;
}

/** Rebase relative `url(...)` references in a stylesheet against its own URL. */
export function rewriteCssUrls(css: string, sheetUrl: string): string {
  return css.replace(/url\(\s*(['"]?)([^'")]+)\1\s*\)/gi, (m, _q, rawUrl) => {
    const u = String(rawUrl).trim();
    if (/^(data:|https?:|\/\/|#)/i.test(u)) return `url("${u}")`;
    const abs = absUrl(u, sheetUrl);
    return abs ? `url("${abs}")` : m;
  });
}

// ── Iframe document assembly ────────────────────────────────────────────────

/** Absolutize a single relative URL against `base`; leave data/blob/absolute/
 *  anchor and non-navigational schemes untouched (null → keep the original). */
function absAttr(value: string, base: string): string | null {
  const v = value.trim();
  if (!v || /^(data:|blob:|https?:|\/\/|#|javascript:|mailto:|tel:)/i.test(v)) return null;
  return absUrl(v, base);
}

/** Absolutize every candidate URL in a `srcset` value against `base`. */
function absSrcset(value: string, base: string): string {
  return value.split(',').map(part => {
    const seg = part.trim();
    if (!seg) return '';
    const sp = seg.search(/\s/);
    const url = sp === -1 ? seg : seg.slice(0, sp);
    const rest = sp === -1 ? '' : seg.slice(sp);
    return (absAttr(url, base) ?? url) + rest;
  }).filter(Boolean).join(', ');
}

/**
 * Resolve relative `src`/`href`/`srcset` URLs in `html` to absolute against
 * `baseUrl`. The sandboxed display iframe inherits the app CSP, whose
 * `base-uri 'self'` REFUSES a `<base href>` pointing at the remote site, so
 * relative image/link URLs would otherwise never resolve ([S-03]). Rewrite them
 * at build time instead. Regex-based to match `rewriteCssUrls`; handles the
 * quoted attributes real pages emit.
 */
export function absolutizeUrls(html: string, baseUrl: string): string {
  if (!baseUrl) return html;
  const out = html.replace(/\b(src|href)\s*=\s*(["'])([^"']*)\2/gi, (m, attr, q, url) => {
    const abs = absAttr(url, baseUrl);
    return abs ? `${attr}=${q}${abs}${q}` : m;
  });
  return out.replace(/\bsrcset\s*=\s*(["'])([^"']*)\1/gi, (_m, q, val) => `srcset=${q}${absSrcset(val, baseUrl)}${q}`);
}

export interface SrcdocOptions {
  /** Body HTML: the picked element (display) or whole page body (picker). */
  html: string;
  /** Page inline `<style>` text. */
  styles?: string;
  /** Inlined external stylesheet text (already url-rebased). */
  extraCss?: string;
  /** Base URL the page's relative image/link URLs are absolutized against
   *  ([S-03]: a `<base href>` is refused by the CSP the sandboxed iframe inherits). */
  baseHref?: string;
  /** Allow scrolling inside the frame (picker). Default: clip (display). */
  scroll?: boolean;
}

/**
 * Assemble a self-contained srcdoc for a sandboxed iframe. Cascade order:
 * reset → external sheets → page inline styles (so inline overrides, matching
 * how the source page cascades).
 */
export function buildSrcdoc(opts: SrcdocOptions): string {
  const reset =
    'html,body{margin:0;padding:0;background:transparent;}' +
    `html,body{overflow:${opts.scroll ? 'auto' : 'hidden'};}`;
  const blocks = [reset, opts.extraCss ?? '', opts.styles ?? '']
    .filter(Boolean)
    .map(css => `<style>${css}</style>`)
    .join('');
  // No <base href>: the CSP the sandboxed iframe inherits (base-uri 'self')
  // refuses it, so relative URLs are absolutized into the body instead ([S-03]).
  const body = opts.baseHref ? absolutizeUrls(opts.html, opts.baseHref) : opts.html;
  return `<!doctype html><html><head><meta charset="utf-8">${blocks}</head>` +
    `<body>${body}</body></html>`;
}
