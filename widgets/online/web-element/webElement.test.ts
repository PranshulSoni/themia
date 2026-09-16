// @vitest-environment jsdom
import { describe, it, expect } from 'vitest';
import {
  resolveConfig,
  cssPath,
  sanitizeForRender,
  extractElement,
  collectStylesheetLinks,
  rewriteCssUrls,
  buildSrcdoc,
  absolutizeUrls,
  DEFAULT_REFRESH_MINUTES,
  MIN_REFRESH_MINUTES,
  MAX_REFRESH_MINUTES,
} from './webElement.ts';

function parse(html: string): Document {
  return new DOMParser().parseFromString(html, 'text/html');
}

describe('web-element: resolveConfig [S-09]', () => {
  it('returns typed defaults for an absent/empty config', () => {
    expect(resolveConfig(undefined)).toEqual({
      url: '', selector: '', label: '', refreshMinutes: DEFAULT_REFRESH_MINUTES,
    });
    expect(resolveConfig({})).toEqual({
      url: '', selector: '', label: '', refreshMinutes: DEFAULT_REFRESH_MINUTES,
    });
  });

  it('trims url + selector strings', () => {
    const c = resolveConfig({ url: '  https://x.com  ', selector: '  #a > b  ', label: 'Hi' });
    expect(c.url).toBe('https://x.com');
    expect(c.selector).toBe('#a > b');
    expect(c.label).toBe('Hi');
  });

  it('falls back to defaults on corrupt (non-string / NaN) values, never throws', () => {
    const c = resolveConfig({ url: 42, selector: null, label: {}, refreshMinutes: 'soon' });
    expect(c.url).toBe('');
    expect(c.selector).toBe('');
    expect(c.label).toBe('');
    expect(c.refreshMinutes).toBe(DEFAULT_REFRESH_MINUTES);
  });

  it('clamps refreshMinutes into [MIN, MAX]', () => {
    expect(resolveConfig({ refreshMinutes: 1 }).refreshMinutes).toBe(MIN_REFRESH_MINUTES);
    expect(resolveConfig({ refreshMinutes: 99999 }).refreshMinutes).toBe(MAX_REFRESH_MINUTES);
    expect(resolveConfig({ refreshMinutes: 30 }).refreshMinutes).toBe(30);
  });
});

describe('web-element: cssPath [S-02]', () => {
  it('round-trips: querySelector(cssPath(el)) === el for nested, id-less DOM', () => {
    const doc = parse(`<body><main><section><p>a</p><p>b</p><p>c</p></section></main></body>`);
    const target = doc.querySelectorAll('p')[2]; // third <p>
    const sel = cssPath(target);
    expect(doc.querySelector(sel)).toBe(target);
  });

  it('anchors on a usable, unique id', () => {
    const doc = parse(`<body><div><span id="price">$9</span></div></body>`);
    const target = doc.querySelector('#price')!;
    const sel = cssPath(target);
    expect(doc.querySelector(sel)).toBe(target);
    expect(sel).toContain('#price');
  });

  it('disambiguates repeated same-tag siblings via :nth-of-type', () => {
    const doc = parse(`<body><ul><li>1</li><li>2</li><li>3</li><li>4</li></ul></body>`);
    const target = doc.querySelectorAll('li')[3];
    const sel = cssPath(target);
    expect(doc.querySelector(sel)).toBe(target);
  });

  it('does not anchor on a non-unique id (still round-trips)', () => {
    const doc = parse(`<body><div id="dup"><b>x</b></div><div id="dup"><b>y</b></div></body>`);
    const target = doc.querySelectorAll('b')[1]; // the second <b>
    const sel = cssPath(target);
    expect(doc.querySelector(sel)).toBe(target);
  });
});

describe('web-element: sanitizeForRender [S-08]', () => {
  it('strips <script>, on* handlers, and javascript: urls but keeps content', () => {
    const out = sanitizeForRender(
      `<div onclick="steal()">hello<script>evil()</script>` +
      `<a href="javascript:alert(1)">link</a><img src="x.png"></div>`,
    );
    expect(out).toContain('hello');
    expect(out).not.toMatch(/<script/i);
    expect(out).not.toMatch(/onclick/i);
    expect(out).not.toMatch(/javascript:/i);
    expect(out).toContain('x.png'); // benign image survives
  });

  it('drops nested iframe/object/embed', () => {
    const out = sanitizeForRender(`<div><iframe src="//e"></iframe><object></object><embed></div>`);
    expect(out).not.toMatch(/<iframe|<object|<embed/i);
  });
});

describe('web-element: extractElement [S-03] [S-05]', () => {
  const PAGE = `<html><head><style>.box{color:red}</style></head>` +
    `<body><div id="card"><h2>Title</h2><script>track()</script></div></body></html>`;

  it('returns the matched element outerHTML + page styles when found', () => {
    const r = extractElement(PAGE, '#card');
    expect(r.found).toBe(true);
    expect(r.html).toContain('Title');
    expect(r.html).not.toMatch(/<script/i); // matched subtree is sanitized
    expect(r.styles).toContain('.box{color:red}');
  });

  it('reports found:false when the selector matches nothing (element gone)', () => {
    const r = extractElement(PAGE, '#missing');
    expect(r.found).toBe(false);
    expect(r.html).toBe('');
  });

  it('never throws on an invalid selector, treats it as not found', () => {
    expect(() => extractElement(PAGE, '>>>bad')).not.toThrow();
    expect(extractElement(PAGE, '>>>bad').found).toBe(false);
  });
});

describe('web-element: stylesheet inlining [S-04]', () => {
  it('collectStylesheetLinks resolves hrefs to absolute, ignores non-stylesheets', () => {
    const html = `<head>` +
      `<link rel="stylesheet" href="/css/main.css">` +
      `<link rel="stylesheet" href="https://cdn.example/b.css">` +
      `<link rel="icon" href="/favicon.ico">` +
      `<link rel="preload" href="/x.css">` +
      `</head>`;
    const links = collectStylesheetLinks(html, 'https://site.test/a/page.html');
    expect(links).toContain('https://site.test/css/main.css');
    expect(links).toContain('https://cdn.example/b.css');
    expect(links).not.toContain('https://site.test/favicon.ico');
    expect(links.some(l => l.includes('favicon'))).toBe(false);
  });

  it('rewriteCssUrls rebases relative url(...) against the stylesheet URL', () => {
    const css = `.a{background:url(img/bg.png)} .b{background:url("../f/x.woff")}`;
    const out = rewriteCssUrls(css, 'https://site.test/css/main.css');
    expect(out).toContain('https://site.test/css/img/bg.png');
    expect(out).toContain('https://site.test/f/x.woff');
  });

  it('rewriteCssUrls leaves absolute + data urls untouched', () => {
    const css = `.a{background:url(https://cdn/x.png)} .b{background:url(data:image/png;base64,AAAA)}`;
    const out = rewriteCssUrls(css, 'https://site.test/css/main.css');
    expect(out).toContain('https://cdn/x.png');
    expect(out).toContain('data:image/png;base64,AAAA');
  });
});

describe('web-element: buildSrcdoc [S-03]', () => {
  it('inlines styles and emits NO <base> (the inherited CSP refuses it)', () => {
    const doc = buildSrcdoc({
      html: '<div>hi</div>',
      styles: '.box{color:red}',
      extraCss: '.x{color:blue}',
      baseHref: 'https://site.test/p/',
    });
    expect(doc).toMatch(/^<!doctype html>/i);
    expect(doc).not.toContain('<base');
    expect(doc).toContain('.box{color:red}');
    expect(doc).toContain('.x{color:blue}');
    expect(doc).toContain('<div>hi</div>');
  });

  // [#57] Regression: a <base href> is silently refused by the CSP the sandboxed
  // iframe inherits (base-uri 'self'), so relative URLs were never resolving.
  // buildSrcdoc must absolutize them into the body instead.
  it('[#57] absolutizes relative body URLs against baseHref', () => {
    const doc = buildSrcdoc({
      html: '<img src="/img/a.png"><a href="sub/page.html">x</a>',
      baseHref: 'https://site.test/p/',
    });
    expect(doc).toContain('src="https://site.test/img/a.png"');
    expect(doc).toContain('href="https://site.test/p/sub/page.html"');
  });
});

describe('web-element: absolutizeUrls [S-03][#57]', () => {
  const BASE = 'https://site.test/p/';
  it('rewrites root-relative and relative src/href to absolute', () => {
    expect(absolutizeUrls('<img src="/a.png">', BASE)).toContain('src="https://site.test/a.png"');
    expect(absolutizeUrls('<a href="b.html">', BASE)).toContain('href="https://site.test/p/b.html"');
  });
  it('leaves absolute, protocol-relative, data and anchor URLs untouched', () => {
    expect(absolutizeUrls('<img src="https://cdn.test/x.png">', BASE)).toContain('src="https://cdn.test/x.png"');
    expect(absolutizeUrls('<img src="//cdn.test/x.png">', BASE)).toContain('src="//cdn.test/x.png"');
    expect(absolutizeUrls('<img src="data:image/png;base64,AAAA">', BASE)).toContain('src="data:image/png;base64,AAAA"');
    expect(absolutizeUrls('<a href="#top">', BASE)).toContain('href="#top"');
  });
  it('rewrites each candidate in a srcset, preserving descriptors', () => {
    const out = absolutizeUrls('<img srcset="/a.png 1x, /b.png 2x">', BASE);
    expect(out).toContain('https://site.test/a.png 1x');
    expect(out).toContain('https://site.test/b.png 2x');
  });
  it('is a no-op without a base', () => {
    expect(absolutizeUrls('<img src="/a.png">', '')).toBe('<img src="/a.png">');
  });
});
