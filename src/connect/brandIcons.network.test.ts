// ---------------------------------------------------------------------------
// Live brand-mark suite — [S-47], [S-48]
//
// Opt-in: excluded from `npm run check` by the `**/*.network.test.ts` exclude in
// vitest.config.ts, and run on its own with
//
//     npm run test:brands
//
// The committed marks are a snapshot of files that live on other people's
// servers. Those move: a site redesigns, an asset host is retired, a query
// string is rotated. This suite re-contacts every `source` and fails when one
// no longer serves an image, so a dead mark is caught here rather than by a
// user looking at a broken tile.
//
// It deliberately does NOT compare bytes. A provider redrawing its favicon is
// their business, not a test failure — the answer to a genuine refresh is to
// re-run `node scripts/gen-brand-icons.mjs` and review the diff.
// ---------------------------------------------------------------------------
import { describe, it, expect } from 'vitest';
import { BRAND_ICONS, type BrandIcon } from './brandIcons.ts';

const TIMEOUT_MS = 20_000;

// The generator scrapes as a browser does; a host that serves an unknown agent
// something else would otherwise look like drift.
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36';

const IMAGE_MAGIC = [
  (b: Buffer) => b.subarray(0, 4).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47])),   // png
  (b: Buffer) => b.subarray(0, 4).equals(Buffer.from([0, 0, 1, 0])),               // ico
  (b: Buffer) => b.subarray(0, 3).toString('ascii') === 'GIF',
  (b: Buffer) => b.subarray(0, 400).toString('utf8').toLowerCase().includes('<svg'),
];

describe('S-47 published brand marks are still published', () => {
  for (const [key, icon] of Object.entries(BRAND_ICONS) as [string, BrandIcon][]) {
    it(`${key}: ${icon.source} still serves an image`, async () => {
      const res = await fetch(icon.source, {
        headers: { 'user-agent': UA, accept: '*/*' },
        redirect: 'follow',
        signal: AbortSignal.timeout(TIMEOUT_MS - 2_000),
      });
      expect(res.status, `${key}: HTTP status`).toBe(200);
      const bytes = Buffer.from(await res.arrayBuffer());
      // Sniffed, not trusted: a site that 200s its SPA shell for a missing
      // asset would otherwise pass on the status code alone.
      expect(
        IMAGE_MAGIC.some(test => test(bytes)),
        `${key}: ${icon.source} no longer returns an image — re-run scripts/gen-brand-icons.mjs`,
      ).toBe(true);
    }, TIMEOUT_MS);
  }
});
