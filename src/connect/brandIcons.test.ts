// [S-47] `brandIcons.ts` is GENERATED from each provider's published favicon and
// committed, so the Connect window draws offline and no user's machine has to
// contact a dozen third parties to render a settings page. That trade only
// holds if the committed data can't quietly become garbage, which is what this
// guards — offline, on the bytes themselves.
//
// The live counterpart (does each source URL still serve that icon?) is
// brandIcons.network.test.ts, excluded from `npm run check` like every other
// `*.network.test.ts`. [S-48]
import { describe, it, expect } from 'vitest';
import { BRAND_ICONS, brandIconSrc, type BrandIcon } from './brandIcons.ts';

const entries = Object.entries(BRAND_ICONS) as [string, BrandIcon][];

/** Leading bytes that prove the base64 really decodes to the claimed format. */
const MAGIC: Record<string, (b: Buffer) => boolean> = {
  'image/png': b => b.subarray(0, 4).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47])),
  'image/x-icon': b => b.subarray(0, 4).equals(Buffer.from([0, 0, 1, 0])),
  'image/gif': b => b.subarray(0, 3).toString('ascii') === 'GIF',
  'image/svg+xml': b => b.subarray(0, 400).toString('utf8').toLowerCase().includes('<svg'),
};

describe('S-47 generated brand marks', () => {
  it('is not silently empty', () => {
    // A generator run that resolved nothing would leave every tile on its
    // monogram with no other signal.
    expect(entries.length).toBeGreaterThanOrEqual(15);
  });

  it('every mark decodes to the image format it claims', () => {
    for (const [key, icon] of entries) {
      const bytes = Buffer.from(icon.data, 'base64');
      // base64 of an error page would still "decode"; the magic bytes are what
      // separate a mark from a soft-404 the generator swallowed.
      expect(MAGIC[icon.mime], `${key}: unexpected mime ${icon.mime}`).toBeTruthy();
      expect(MAGIC[icon.mime](bytes), `${key}: bytes are not ${icon.mime}`).toBe(true);
      expect(bytes.length, `${key} is suspiciously small`).toBeGreaterThan(100);
    }
  });

  it('no mark is big enough to be the wrong file', () => {
    // These fill a 22px plate. A 512px PWA icon or a full .ico bundle sneaking
    // in shows up here rather than as a fat bundle nobody notices.
    for (const [key, icon] of entries) {
      expect(Buffer.from(icon.data, 'base64').length / 1024, `${key} KB`).toBeLessThan(24);
    }
    const total = entries.reduce((n, [, i]) => n + Buffer.from(i.data, 'base64').length, 0);
    expect(total / 1024, 'total KB of brand marks').toBeLessThan(64);
  });

  it('each mark records where it came from, over https', () => {
    // `source` is what makes a refresh auditable: it says which published file
    // this is, so a regenerated diff can be checked rather than trusted.
    for (const [key, icon] of entries) {
      expect(icon.title.length, `${key} title`).toBeGreaterThan(0);
      expect(icon.site, `${key} site`).toMatch(/^https:\/\//);
      expect(icon.source, `${key} source`).toMatch(/^https:\/\//);
    }
  });

  it('renders as a data URI an <img> can use', () => {
    for (const [key, icon] of entries) {
      expect(brandIconSrc(icon), key).toBe(`data:${icon.mime};base64,${icon.data}`);
    }
  });
});
