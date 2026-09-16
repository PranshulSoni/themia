// Display-path orchestration for the Web Element widget. Kept separate from the
// pure helpers in webElement.ts so those stay unit-testable without mocking the
// backend.
//
// The page HTML comes from `web_element_render_html`, a real off-screen
// WebView2 that EXECUTES the page's JavaScript and returns the settled, post-JS
// DOM (so SPAs / Google etc. actually have content). We then clip to the pinned
// element and inline its stylesheets for fidelity, exactly as before.

import { invoke } from '../../../src/demo/invoke.ts';
import {
  extractElement,
  collectStylesheetLinks,
  rewriteCssUrls,
  buildSrcdoc,
} from './webElement.ts';

// Bounded so one heavy site can't fan out into dozens of fetches or bloat the
// cache. Excess sheets/bytes are dropped: the look degrades, nothing breaks.
const MAX_SHEETS = 6;
const MAX_CSS_BYTES = 512 * 1024;

/** Fetch + url-rebase the page's external stylesheets, best-effort and bounded. */
async function externalCss(html: string, baseUrl: string, signal?: AbortSignal): Promise<string> {
  const links = collectStylesheetLinks(html, baseUrl).slice(0, MAX_SHEETS);
  const sheets = await Promise.all(links.map(async (href) => {
    try {
      return rewriteCssUrls(await invoke('http_get', { url: href }), href);
    } catch {
      return ''; // ignore: best-effort fidelity; a missing sheet just degrades the look
    }
  }));
  if (signal?.aborted) throw new DOMException('aborted', 'AbortError');
  let out = '';
  for (const css of sheets) {
    if (out.length + css.length > MAX_CSS_BYTES) break;
    out += css + '\n';
  }
  return out;
}

export interface DisplayDoc {
  /** False when the selector matched nothing on the (re-)rendered page. */
  found: boolean;
  srcdoc: string;
}

/** Display path: render the page (with JS), clip to `selector`. Honors the signal. */
export async function loadDisplayDoc(url: string, selector: string, signal: AbortSignal): Promise<DisplayDoc> {
  const html = await invoke('web_element_render_html', { url });
  if (signal.aborted) throw new DOMException('aborted', 'AbortError');

  const found = extractElement(html, selector);
  if (!found.found) return { found: false, srcdoc: '' };

  const extraCss = await externalCss(html, url, signal);
  return {
    found: true,
    srcdoc: buildSrcdoc({ html: found.html, styles: found.styles, extraCss, baseHref: url, scroll: false }),
  };
}
