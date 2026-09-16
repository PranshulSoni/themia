// Regenerate src/connect/brandIcons.ts from each provider's OWN website.
//
//   node scripts/gen-brand-icons.mjs
//
// The marks in the Connect window identify real services, so they come from the
// source of truth for what a service looks like: the icon that service
// publishes for itself (`<link rel="icon">`, apple-touch-icon, /favicon.ico).
// Nothing here is drawn by hand or approximated from memory.
//
// Fetched at generation time and inlined as base64, never fetched at runtime:
// the Connect window has to draw offline, and drawing it must not tell a dozen
// third parties which providers a user is browsing. `brandIcons.test.ts` guards
// the committed data offline; `brandIcons.network.test.ts` (`npm run
// test:brands`) re-contacts every source URL and fails when one dies.
//
// Image work (ICO unpacking, resampling) lives in scripts/lib/favicon-image.mjs.
import { writeFileSync } from 'node:fs';
import { sniff, toTilePng } from './lib/favicon-image.mjs';

/** Real browsers get the real markup; some of these hosts serve an unknown
 *  agent a stub page that declares no icons at all. */
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36';

/** A mark that will not fit a 22px disc for less than this is the wrong file. */
const MAX_BYTES = 24 * 1024;
const FETCH_TIMEOUT_MS = 20_000;

/**
 * key → the page whose published icon represents this brand.
 *
 * `page` is the service's own site, and the product's own page where the
 * product has one, so Gmail gets Gmail's mark rather than Google's. `icon` pins
 * one file directly, for a host whose page cannot be scraped.
 */
export const BRAND_SITES = {
  // Services on the picker
  microsoft: { title: 'Microsoft', page: 'https://www.microsoft.com/' },
  google: { title: 'Google', page: 'https://www.google.com/' },
  github: { title: 'GitHub', page: 'https://github.com/' },
  claude: { title: 'Claude', page: 'https://claude.ai/' },
  // Brands behind the IMAP / CalDAV / CardDAV presets
  gmail: { title: 'Gmail', page: 'https://mail.google.com/' },
  yahoo: { title: 'Yahoo Mail', page: 'https://mail.yahoo.com/' },
  icloud: { title: 'iCloud', page: 'https://www.icloud.com/' },
  fastmail: { title: 'Fastmail', page: 'https://www.fastmail.com/' },
  zoho: { title: 'Zoho Mail', page: 'https://www.zoho.com/mail/' },
  gmx: { title: 'GMX', page: 'https://www.gmx.net/' },
  webde: { title: 'WEB.DE', page: 'https://web.de/' },
  mailboxorg: { title: 'mailbox.org', page: 'https://mailbox.org/' },
  posteo: { title: 'Posteo', page: 'https://posteo.de/' },
  yandex: { title: 'Yandex Mail', page: 'https://mail.yandex.com/' },
  aol: { title: 'AOL Mail', page: 'https://www.aol.com/' },
  nextcloud: { title: 'Nextcloud', page: 'https://nextcloud.com/' },
  baikal: { title: 'Baïkal', page: 'https://sabre.io/baikal/' },
  synology: { title: 'Synology', page: 'https://www.synology.com/' },
};

// ── Fetching ────────────────────────────────────────────────────────────────

async function get(url) {
  const res = await fetch(url, {
    headers: { 'user-agent': UA, accept: '*/*' },
    redirect: 'follow',
    signal: AbortSignal.timeout(FETCH_TIMEOUT_MS),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res;
}

function pxOf(sizes) {
  const m = (sizes || '').toLowerCase().match(/(\d+)x(\d+)/);
  return m ? Number(m[1]) : 0;
}

function isSvg(c) {
  return c.type.includes('svg') || /\.svg(\?|$)/i.test(c.url);
}

/**
 * Prefer a raster around tile size, then a vector, then whatever is left.
 * A 16px favicon is a smudge on a HiDPI display, and a 512px PWA icon is
 * kilobytes of data URI for a 22px disc.
 */
function score(c) {
  if (isSvg(c)) return 90;
  if (c.px >= 32) return 100 - Math.min(60, Math.abs(c.px - 64) / 4);
  if (c.rel === 'fallback') return 20;
  if (c.px > 0) return 10;  // declared 16x16: last resort
  return 30;                // unsized <link rel=icon>, usually a 32px png
}

/** The icon links a page declares, plus the well-known path, best first. */
function candidates(html, pageUrl) {
  const out = [];
  for (const tag of html.match(/<link\b[^>]*>/gi) ?? []) {
    const attr = (name) => {
      const m = tag.match(new RegExp(`${name}\\s*=\\s*("([^"]*)"|'([^']*)'|([^\\s>]+))`, 'i'));
      return m ? (m[2] ?? m[3] ?? m[4] ?? '').trim() : '';
    };
    const rel = attr('rel').toLowerCase();
    if (!/\b(shortcut\s+)?icon\b|\bapple-touch-icon(-precomposed)?\b/.test(rel)) continue;
    // A mask-icon is a monochrome silhouette for Safari's pinned tabs, not the
    // brand's mark; it would put a flat black shape on the tile.
    if (rel.includes('mask-icon')) continue;
    const href = attr('href');
    if (!href || href.startsWith('data:')) continue;
    try {
      out.push({ url: new URL(href, pageUrl).toString(), type: attr('type').toLowerCase(), px: pxOf(attr('sizes')), rel });
    } catch {
      // A malformed href is the page's problem; the next candidate stands.
    }
  }
  out.push({ url: new URL('/favicon.ico', pageUrl).toString(), type: '', px: 0, rel: 'fallback' });
  return out.sort((a, b) => score(b) - score(a));
}

/** Walks the page's candidates until one yields a usable image. */
async function iconFor(key, { page, icon }) {
  const list = icon
    ? [{ url: icon, type: '', px: 0, rel: 'pinned' }]
    : candidates(await (await get(page)).text(), page);

  const problems = [];
  for (const c of list.slice(0, 8)) {
    try {
      const fetched = Buffer.from(await (await get(c.url)).arrayBuffer());
      const kind = sniff(fetched);
      if (!kind) { problems.push(`${c.url}: not an image`); continue; }
      const { buf, mime, note } = toTilePng(fetched, kind);
      if (buf.length > MAX_BYTES) { problems.push(`${c.url}: ${(buf.length / 1024).toFixed(1)} KB over budget`); continue; }
      return { source: c.url, mime, bytes: buf.length, data: buf.toString('base64'), note };
    } catch (e) {
      problems.push(`${c.url}: ${e.message}`);
    }
  }
  throw new Error(`no usable icon for ${key}\n    ${problems.join('\n    ')}`);
}

// ── Generate ────────────────────────────────────────────────────────────────

const results = [];
const failures = [];
for (const [key, brand] of Object.entries(BRAND_SITES)) {
  try {
    const got = await iconFor(key, brand);
    results.push([key, brand, got]);
    console.log(`  ok   ${key.padEnd(11)} ${got.mime.padEnd(14)} ${(got.bytes / 1024).toFixed(1).padStart(5)} KB  ${got.source}`
      + (got.note ? `\n         ${got.note}` : ''));
  } catch (e) {
    failures.push(key);
    console.error(`  FAIL ${key.padEnd(11)} ${e.message}`);
  }
}

const entries = results.map(([key, brand, got]) =>
  `  ${key}: {\n` +
  `    title: ${JSON.stringify(brand.title)},\n` +
  `    site: ${JSON.stringify(brand.page)},\n` +
  `    source: ${JSON.stringify(got.source)},\n` +
  `    mime: ${JSON.stringify(got.mime)},\n` +
  `    data: ${JSON.stringify(got.data)},\n` +
  `  },`,
);

const out = `// GENERATED by scripts/gen-brand-icons.mjs — do not edit by hand.
//
// Each mark is the icon the service publishes for itself, fetched from its own
// site (\`source\`) and inlined as base64. Trademarks belong to their owners;
// these identify the service the user is connecting to.
//
// Inlined rather than fetched at runtime: the Connect window has to draw
// offline, and drawing it must not tell a dozen third parties which providers a
// user is browsing. An .ico bundling several sizes is unpacked to the one the
// tile needs, so a mark costs ~1 KB instead of ~20 (scripts/lib/favicon-image.mjs);
// the published artwork itself is never redrawn.
//
// Re-run the generator to refresh. \`brandIcons.test.ts\` checks this file
// offline; \`brandIcons.network.test.ts\` (\`npm run test:brands\`) re-contacts
// every source URL.

export interface BrandIcon {
  /** Brand name, for the tile's tooltip and alt text. */
  title: string;
  /** The service's own site — where the mark came from. */
  site: string;
  /** The exact file fetched, so a refresh is reproducible and auditable. */
  source: string;
  mime: string;
  /** base64 of the image bytes. */
  data: string;
}

export const BRAND_ICONS = {
${entries.join('\n')}
} as const satisfies Record<string, BrandIcon>;

export type BrandIconKey = keyof typeof BRAND_ICONS;

/** \`src\` for an \`<img>\`. */
export function brandIconSrc(icon: BrandIcon): string {
  return \`data:\${icon.mime};base64,\${icon.data}\`;
}
`;

writeFileSync(new URL('../src/connect/brandIcons.ts', import.meta.url), out);
const kb = (results.reduce((n, [, , g]) => n + g.bytes, 0) / 1024).toFixed(1);
console.log(`\nwrote ${results.length} brand marks (${kb} KB of image data)`);
if (failures.length) {
  console.error(`\n${failures.length} brand(s) produced no icon: ${failures.join(', ')}`);
  console.error('Pin an `icon:` URL for them in BRAND_SITES, or drop them from the catalog.');
  process.exit(1);
}
