// Offline invariants for the connect preset catalog. Nothing here touches the
// network — the live endpoint check is the opt-in presets.network.test.ts suite.
import { describe, it, expect } from 'vitest';
import {
  CONNECT_PRESETS, presetsFor, findPreset, isPresetProtocol,
  emailDomain, matchPresetByEmail, remainingPlaceholders, applyPreset,
  presetOwnedFields, PLACEHOLDER_RE,
  type PresetProtocol,
} from './presets.ts';
import { BRAND_ICONS } from './brandIcons.ts';

const PROTOCOLS: PresetProtocol[] = ['imap', 'caldav', 'carddav'];

describe('preset catalog shape', () => {
  it('ids are unique', () => {
    const ids = CONNECT_PRESETS.map(p => p.id);
    expect(ids.length).toBe(new Set(ids).size);
  });

  it('every preset belongs to a real protocol provider', () => {
    for (const p of CONNECT_PRESETS) expect(isPresetProtocol(p.protocol), p.id).toBe(true);
  });

  it('labels are unique within a protocol', () => {
    for (const proto of PROTOCOLS) {
      const labels = presetsFor(proto).map(p => p.label);
      expect(new Set(labels).size, proto).toBe(labels.length);
    }
  });

  it('has no catch-all "Other" entry — that is the page\'s own action', () => {
    // A preset that fills the form with `{imap-server}` is a worse answer than
    // an empty form, so "Enter it myself" is a button on the preset page, not a
    // catalog entry. [S-47]
    for (const p of CONNECT_PRESETS) {
      expect(p.label.toLowerCase(), p.id).not.toMatch(/^other\b/);
    }
  });

  it('domain-backed presets sort before the self-hosted ones', () => {
    // Auto-detection can only match a preset with domains, so the ones a typed
    // address can reach should be what the eye hits first.
    for (const proto of PROTOCOLS) {
      const list = presetsFor(proto);
      expect(list.length, proto).toBeGreaterThan(3);
      const lastWithDomains = list.map(p => p.domains.length > 0).lastIndexOf(true);
      const firstWithout = list.map(p => p.domains.length === 0).indexOf(true);
      if (firstWithout >= 0) expect(firstWithout, proto).toBeGreaterThan(lastWithDomains);
    }
  });

  it('every preset carries a brand colour and the provider\'s own mark', () => {
    for (const p of CONNECT_PRESETS) {
      expect(p.brand, `${p.id} brand`).toMatch(/^#[0-9a-f]{6}$/i);
      // Every provider in the catalog publishes a favicon, so every tile shows
      // a real mark; the monogram is the fallback for a preset added before its
      // brand is generated, not something the shipped catalog relies on. [S-47]
      expect(p.icon, `${p.id} has no brand mark — add it to BRAND_SITES and re-run scripts/gen-brand-icons.mjs`).toBeTruthy();
      // A typo'd key would silently fall back to the monogram, so pin it here.
      expect(Object.keys(BRAND_ICONS), `${p.id} icon`).toContain(p.icon);
    }
  });

  it('does not offer Outlook.com over IMAP', () => {
    // Microsoft sign-in is the recommended path (no password, never expires),
    // so an IMAP tile competing with it on the same screen is a worse option.
    for (const p of presetsFor('imap')) {
      expect(p.label.toLowerCase(), 'no Outlook IMAP preset').not.toContain('outlook');
      expect(p.domains, `${p.id} claims an Outlook domain`).not.toContain('outlook.com');
    }
  });

  it('domains are lowercase, bare, and claimed by only one preset per protocol', () => {
    for (const proto of PROTOCOLS) {
      const seen = new Map<string, string>();
      for (const p of presetsFor(proto)) {
        for (const d of p.domains) {
          expect(d, `${p.id} domain`).toBe(d.toLowerCase());
          expect(d.startsWith('@'), `${p.id} domain ${d}`).toBe(false);
          expect(d.includes('.'), `${p.id} domain ${d}`).toBe(true);
          expect(seen.get(d), `${d} claimed twice in ${proto}`).toBeUndefined();
          seen.set(d, p.id);
        }
      }
    }
  });

  it('IMAP presets carry a host + a plausible port', () => {
    for (const p of presetsFor('imap')) {
      expect(p.values.host, p.id).toBeTruthy();
      expect(Number(p.values.port), p.id).toBeGreaterThan(0);
      expect(Number(p.values.port), p.id).toBeLessThan(65536);
    }
  });

  it('DAV presets carry an https URL', () => {
    for (const p of [...presetsFor('caldav'), ...presetsFor('carddav')]) {
      expect(p.values.url?.startsWith('https://'), p.id).toBe(true);
    }
  });

  it('a probe target matches the values it claims to check', () => {
    for (const p of CONNECT_PRESETS) {
      if (p.probe.kind === 'imap') {
        expect(p.probe.host, p.id).toBe(p.values.host);
        expect(String(p.probe.port), p.id).toBe(p.values.port);
      } else if (p.probe.kind === 'dav') {
        expect(p.probe.url, p.id).toBe(p.values.url);
      }
    }
  });

  it('a templated preset is never probed, and a probed one is never templated', () => {
    for (const p of CONNECT_PRESETS) {
      const templated = remainingPlaceholders(p.values).length > 0;
      expect(templated, `${p.id} templated/probe mismatch`).toBe(p.probe.kind === 'none');
    }
  });

  it('an app-password preset explains where to get one', () => {
    for (const p of CONNECT_PRESETS.filter(x => x.appPassword)) {
      expect(p.helpUrl?.startsWith('https://'), p.id).toBe(true);
      expect(p.note, p.id).toBeTruthy();
    }
  });

  it('offers at least ten IMAP providers', () => {
    expect(presetsFor('imap').length).toBeGreaterThanOrEqual(10);
  });

  it('findPreset round-trips every id and rejects unknown ones', () => {
    for (const p of CONNECT_PRESETS) expect(findPreset(p.id)?.id).toBe(p.id);
    expect(findPreset('nope')).toBeUndefined();
    expect(findPreset(null)).toBeUndefined();
  });
});

describe('emailDomain', () => {
  it('takes the part after the last @', () => {
    expect(emailDomain('me@gmail.com')).toBe('gmail.com');
    expect(emailDomain('odd@name@fastmail.com')).toBe('fastmail.com');
  });
  it('lowercases and trims', () => {
    expect(emailDomain('  Me@GMail.CoM  ')).toBe('gmail.com');
  });
  it('is empty for a half-typed address', () => {
    expect(emailDomain('')).toBe('');
    expect(emailDomain('me')).toBe('');
  });
});

describe('matchPresetByEmail', () => {
  it('matches the well-known providers', () => {
    expect(matchPresetByEmail('imap', 'me@gmail.com')?.id).toBe('imap-gmail');
    expect(matchPresetByEmail('imap', 'me@icloud.com')?.id).toBe('imap-icloud');
    expect(matchPresetByEmail('caldav', 'me@fastmail.com')?.id).toBe('caldav-fastmail');
    expect(matchPresetByEmail('carddav', 'me@me.com')?.id).toBe('carddav-icloud');
  });

  it('matches a subdomain of a claimed domain', () => {
    expect(matchPresetByEmail('imap', 'me@mail.gmx.net')?.id).toBe('imap-gmx');
  });

  it('does not match a suffix collision', () => {
    expect(matchPresetByEmail('imap', 'me@notgmail.com')).toBeUndefined();
    expect(matchPresetByEmail('imap', 'me@example.com')).toBeUndefined();
  });

  it('never auto-selects a catch-all preset', () => {
    for (const proto of PROTOCOLS) {
      const hit = matchPresetByEmail(proto, 'me@totally-unknown-host.example');
      expect(hit, proto).toBeUndefined();
    }
  });

  it('ignores a bare username', () => {
    expect(matchPresetByEmail('imap', 'nathaniel')).toBeUndefined();
  });
});

describe('placeholders', () => {
  it('finds every distinct token', () => {
    expect(remainingPlaceholders({ url: 'https://{your-server}/dav' })).toEqual(['{your-server}']);
    expect(remainingPlaceholders({ a: '{x}', b: '{x}', c: '{y}' })).toEqual(['{x}', '{y}']);
  });
  it('is empty once the user replaced them', () => {
    expect(remainingPlaceholders({ url: 'https://cloud.example.com/remote.php/dav' })).toEqual([]);
    expect(remainingPlaceholders({})).toEqual([]);
  });
  it('is stateless across calls (global regex lastIndex guard)', () => {
    const values = { url: 'https://{your-server}/dav' };
    expect(remainingPlaceholders(values)).toEqual(remainingPlaceholders(values));
    expect(PLACEHOLDER_RE.lastIndex).toBe(0);
  });
});

describe('applyPreset', () => {
  it('overwrites the server fields and keeps the credentials', () => {
    const before = { host: '', port: '993', username: 'me@gmail.com', password: 'secret' };
    const after = applyPreset(findPreset('imap-gmail')!, before);
    expect(after.host).toBe('imap.gmail.com');
    expect(after.username).toBe('me@gmail.com');
    expect(after.password).toBe('secret');
  });

  it('replaces a previously chosen preset cleanly', () => {
    const gmail = applyPreset(findPreset('imap-gmail')!, {});
    const yahoo = applyPreset(findPreset('imap-yahoo')!, gmail);
    expect(yahoo.host).toBe('imap.mail.yahoo.com');
  });
});

describe('presetOwnedFields', () => {
  it('lists the fields presets write, so hand edits can be detected', () => {
    expect(presetOwnedFields('imap').sort()).toEqual(['host', 'port']);
    expect(presetOwnedFields('caldav')).toEqual(['url']);
    expect(presetOwnedFields('carddav')).toEqual(['url']);
  });
});
