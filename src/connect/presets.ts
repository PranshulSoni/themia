// ---------------------------------------------------------------------------
// Connect-form presets
//
// The protocol providers (IMAP / CalDAV / CardDAV) ask for a server address and
// a port, which most people have never seen. A preset fills those fields from
// the provider's published settings, so "Gmail" is one click instead of a
// support-page hunt.
//
// Kept JSX-free (like connect/visibility.ts) so it unit-tests without pulling in
// the Solid catalog. Two test suites guard it:
//   - presets.test.ts          offline invariants (shape, uniqueness, ports)
//   - presets.network.test.ts  opt-in live probe of every endpoint below,
//                              excluded from `npm run check` (see [S-48])
//
// Server settings do drift. Nothing here is trusted blindly: selecting a preset
// probes the endpoint on the user's own machine and badges it when unreachable
// (connect/probe.ts), and the user can always edit every field afterwards.
// ---------------------------------------------------------------------------

import type { BrandIconKey } from './brandIcons.ts';

/** Protocol providers that accept a preset. Mirrors the `key` of the matching
 *  entry in CONNECT_PROVIDERS. */
export type PresetProtocol = 'imap' | 'caldav' | 'carddav';

/** What to contact to prove the preset's server settings still resolve.
 *  `none` = self-hosted preset whose host the user supplies, so there is
 *  nothing generic to probe. Mirrors the Rust `ProbeTarget` enum. */
export type PresetProbe =
  | { kind: 'imap'; host: string; port: number }
  | { kind: 'dav'; url: string }
  | { kind: 'none' };

export interface ConnectPreset {
  /** Stable id, unique within the whole catalog. */
  id: string;
  label: string;
  protocol: PresetProtocol;
  /**
   * Brand colour, used by the monogram fallback below for its tint + glyph,
   * following the app's own subtle-fill idiom so it stays legible in either
   * theme without per-brand contrast tuning. [S-47]
   */
  brand: string;
  /**
   * The mark this provider publishes for itself, from the generated
   * `BRAND_ICONS` (its own favicon). Optional so a preset can be added before
   * its brand is in the catalog; that one shows a monogram until the generator
   * is re-run for it. Every preset shipped today has a mark.
   */
  icon?: BrandIconKey;
  /** Email domains that pick this preset automatically as the user types their
   *  address. Lowercase, no leading `@`. */
  domains: string[];
  /** Field values merged into the connect form, keyed by ConnectField.name.
   *  May contain a `{placeholder}` the user must replace (self-hosted). */
  values: Record<string, string>;
  probe: PresetProbe;
  /** The provider rejects the account password; an app password is required. */
  appPassword?: boolean;
  /** Where that app password (or the IMAP/DAV toggle) is created. */
  helpUrl?: string;
  /** One extra line of setup guidance, shown once the preset is chosen. */
  note?: string;
}

/** A `{token}` in a preset value: something only the user can fill in. */
export const PLACEHOLDER_RE = /\{[a-z-]+\}/gi;

// ── Catalog ──────────────────────────────────────────────────────────────────
//
// Ordered by how often people actually pick them. Self-hosted entries sort last
// within each protocol because they need a server address typed in.
//
// There is deliberately no "Other" entry: a preset that fills the fields with
// `{imap-server}` is a worse answer than an empty form. The preset page offers
// "Enter it myself" as its own action, which goes to a blank form. [S-47]

const IMAP_PRESETS: ConnectPreset[] = [
  {
    id: 'imap-gmail',
    label: 'Gmail',
    protocol: 'imap',
    brand: '#EA4335',
    icon: 'gmail',
    domains: ['gmail.com', 'googlemail.com'],
    values: { host: 'imap.gmail.com', port: '993' },
    probe: { kind: 'imap', host: 'imap.gmail.com', port: 993 },
    appPassword: true,
    helpUrl: 'https://myaccount.google.com/apppasswords',
    note: 'Google rejects your normal password. Turn on 2-Step Verification, then create an app password and paste that below.',
  },
  {
    id: 'imap-yahoo',
    label: 'Yahoo Mail',
    protocol: 'imap',
    brand: '#6001D2',
    icon: 'yahoo',
    domains: ['yahoo.com', 'yahoo.co.uk', 'yahoo.de', 'yahoo.fr', 'ymail.com', 'rocketmail.com'],
    values: { host: 'imap.mail.yahoo.com', port: '993' },
    probe: { kind: 'imap', host: 'imap.mail.yahoo.com', port: 993 },
    appPassword: true,
    helpUrl: 'https://login.yahoo.com/account/security',
    note: 'Yahoo requires an app password — generate one under Account security.',
  },
  {
    id: 'imap-icloud',
    label: 'iCloud Mail',
    protocol: 'imap',
    brand: '#3693F3',
    icon: 'icloud',
    domains: ['icloud.com', 'me.com', 'mac.com'],
    values: { host: 'imap.mail.me.com', port: '993' },
    probe: { kind: 'imap', host: 'imap.mail.me.com', port: 993 },
    appPassword: true,
    helpUrl: 'https://account.apple.com/account/manage',
    note: 'Apple requires an app-specific password. Sign in to your Apple Account and create one under Sign-In and Security.',
  },
  {
    id: 'imap-fastmail',
    label: 'Fastmail',
    protocol: 'imap',
    brand: '#0067B9',
    icon: 'fastmail',
    domains: ['fastmail.com', 'fastmail.fm', 'messagingengine.com'],
    values: { host: 'imap.fastmail.com', port: '993' },
    probe: { kind: 'imap', host: 'imap.fastmail.com', port: 993 },
    appPassword: true,
    helpUrl: 'https://app.fastmail.com/settings/security/apppasswords',
    note: 'Create an app password with Mail (IMAP) access.',
  },
  {
    id: 'imap-zoho',
    label: 'Zoho Mail',
    protocol: 'imap',
    brand: '#E42527',
    icon: 'zoho',
    domains: ['zoho.com', 'zohomail.com'],
    values: { host: 'imap.zoho.com', port: '993' },
    probe: { kind: 'imap', host: 'imap.zoho.com', port: 993 },
    appPassword: true,
    helpUrl: 'https://accounts.zoho.com/home#security/apppassword',
    note: 'Enable IMAP under Mail Settings, then create an app password. On a European account use imap.zoho.eu instead.',
  },
  {
    id: 'imap-gmx',
    label: 'GMX',
    protocol: 'imap',
    brand: '#1C449B',
    icon: 'gmx',
    domains: ['gmx.net', 'gmx.de', 'gmx.com', 'gmx.at', 'gmx.ch'],
    values: { host: 'imap.gmx.net', port: '993' },
    probe: { kind: 'imap', host: 'imap.gmx.net', port: 993 },
    note: 'Switch on “IMAP access” in your GMX settings first, otherwise sign-in is refused.',
  },
  {
    id: 'imap-webde',
    label: 'WEB.DE',
    protocol: 'imap',
    brand: '#C9A400',
    icon: 'webde',
    domains: ['web.de'],
    values: { host: 'imap.web.de', port: '993' },
    probe: { kind: 'imap', host: 'imap.web.de', port: 993 },
    note: 'Switch on “IMAP access” in your WEB.DE settings first, otherwise sign-in is refused.',
  },
  {
    id: 'imap-mailbox-org',
    label: 'mailbox.org',
    protocol: 'imap',
    brand: '#E30613',
    icon: 'mailboxorg',
    domains: ['mailbox.org'],
    values: { host: 'imap.mailbox.org', port: '993' },
    probe: { kind: 'imap', host: 'imap.mailbox.org', port: 993 },
  },
  {
    id: 'imap-posteo',
    label: 'Posteo',
    protocol: 'imap',
    brand: '#1F9E4C',
    icon: 'posteo',
    domains: ['posteo.de', 'posteo.net'],
    values: { host: 'posteo.de', port: '993' },
    probe: { kind: 'imap', host: 'posteo.de', port: 993 },
  },
  {
    id: 'imap-yandex',
    label: 'Yandex Mail',
    protocol: 'imap',
    brand: '#FC3F1D',
    icon: 'yandex',
    domains: ['yandex.com', 'yandex.ru', 'ya.ru'],
    values: { host: 'imap.yandex.com', port: '993' },
    probe: { kind: 'imap', host: 'imap.yandex.com', port: 993 },
    appPassword: true,
    helpUrl: 'https://id.yandex.com/security/app-passwords',
    note: 'Enable IMAP in Yandex Mail settings, then use an app password.',
  },
  {
    id: 'imap-aol',
    label: 'AOL Mail',
    protocol: 'imap',
    brand: '#0F69FF',
    icon: 'aol',
    domains: ['aol.com', 'aim.com'],
    values: { host: 'imap.aol.com', port: '993' },
    probe: { kind: 'imap', host: 'imap.aol.com', port: 993 },
    appPassword: true,
    helpUrl: 'https://login.aol.com/account/security',
    note: 'AOL requires an app password — generate one under Account security.',
  },
];

const CALDAV_PRESETS: ConnectPreset[] = [
  {
    id: 'caldav-icloud',
    label: 'Apple iCloud',
    protocol: 'caldav',
    brand: '#3693F3',
    icon: 'icloud',
    domains: ['icloud.com', 'me.com', 'mac.com'],
    values: { url: 'https://caldav.icloud.com' },
    probe: { kind: 'dav', url: 'https://caldav.icloud.com' },
    appPassword: true,
    helpUrl: 'https://account.apple.com/account/manage',
    note: 'Apple requires an app-specific password, created under Sign-In and Security.',
  },
  {
    id: 'caldav-fastmail',
    label: 'Fastmail',
    protocol: 'caldav',
    brand: '#0067B9',
    icon: 'fastmail',
    domains: ['fastmail.com', 'fastmail.fm', 'messagingengine.com'],
    values: { url: 'https://caldav.fastmail.com/dav/' },
    probe: { kind: 'dav', url: 'https://caldav.fastmail.com/dav/' },
    appPassword: true,
    helpUrl: 'https://app.fastmail.com/settings/security/apppasswords',
    note: 'Create an app password with Calendars (CalDAV) access.',
  },
  {
    id: 'caldav-mailbox-org',
    label: 'mailbox.org',
    protocol: 'caldav',
    brand: '#E30613',
    icon: 'mailboxorg',
    domains: ['mailbox.org'],
    values: { url: 'https://dav.mailbox.org/' },
    probe: { kind: 'dav', url: 'https://dav.mailbox.org/' },
  },
  {
    id: 'caldav-posteo',
    label: 'Posteo',
    protocol: 'caldav',
    brand: '#1F9E4C',
    icon: 'posteo',
    domains: ['posteo.de', 'posteo.net'],
    values: { url: 'https://posteo.de:8443/' },
    probe: { kind: 'dav', url: 'https://posteo.de:8443/' },
  },
  {
    id: 'caldav-yahoo',
    label: 'Yahoo Calendar',
    protocol: 'caldav',
    brand: '#6001D2',
    icon: 'yahoo',
    domains: ['yahoo.com', 'yahoo.co.uk', 'ymail.com', 'rocketmail.com'],
    values: { url: 'https://caldav.calendar.yahoo.com' },
    probe: { kind: 'dav', url: 'https://caldav.calendar.yahoo.com' },
    appPassword: true,
    helpUrl: 'https://login.yahoo.com/account/security',
    note: 'Yahoo requires an app password — generate one under Account security.',
  },
  {
    id: 'caldav-nextcloud',
    label: 'Nextcloud / ownCloud',
    protocol: 'caldav',
    brand: '#0082C9',
    icon: 'nextcloud',
    domains: [],
    values: { url: 'https://{your-server}/remote.php/dav' },
    probe: { kind: 'none' },
    note: 'Replace {your-server} with the address you open Nextcloud at, e.g. cloud.example.com.',
  },
  {
    id: 'caldav-baikal',
    label: 'Baïkal',
    protocol: 'caldav',
    brand: '#5A7D9A',
    icon: 'baikal',
    domains: [],
    values: { url: 'https://{your-server}/dav.php' },
    probe: { kind: 'none' },
    note: 'Replace {your-server} with your Baïkal address.',
  },
  {
    id: 'caldav-synology',
    label: 'Synology Calendar',
    protocol: 'caldav',
    brand: '#0C3D6B',
    icon: 'synology',
    domains: [],
    values: { url: 'https://{your-nas}:5001/caldav.php' },
    probe: { kind: 'none' },
    note: 'Replace {your-nas} with your NAS address. 5001 is the default HTTPS port.',
  },
];

const CARDDAV_PRESETS: ConnectPreset[] = [
  {
    id: 'carddav-icloud',
    label: 'Apple iCloud',
    protocol: 'carddav',
    brand: '#3693F3',
    icon: 'icloud',
    domains: ['icloud.com', 'me.com', 'mac.com'],
    values: { url: 'https://contacts.icloud.com' },
    probe: { kind: 'dav', url: 'https://contacts.icloud.com' },
    appPassword: true,
    helpUrl: 'https://account.apple.com/account/manage',
    note: 'Apple requires an app-specific password, created under Sign-In and Security.',
  },
  {
    id: 'carddav-fastmail',
    label: 'Fastmail',
    protocol: 'carddav',
    brand: '#0067B9',
    icon: 'fastmail',
    domains: ['fastmail.com', 'fastmail.fm', 'messagingengine.com'],
    values: { url: 'https://carddav.fastmail.com/dav/' },
    probe: { kind: 'dav', url: 'https://carddav.fastmail.com/dav/' },
    appPassword: true,
    helpUrl: 'https://app.fastmail.com/settings/security/apppasswords',
    note: 'Create an app password with Contacts (CardDAV) access.',
  },
  {
    id: 'carddav-mailbox-org',
    label: 'mailbox.org',
    protocol: 'carddav',
    brand: '#E30613',
    icon: 'mailboxorg',
    domains: ['mailbox.org'],
    values: { url: 'https://dav.mailbox.org/' },
    probe: { kind: 'dav', url: 'https://dav.mailbox.org/' },
  },
  {
    id: 'carddav-posteo',
    label: 'Posteo',
    protocol: 'carddav',
    brand: '#1F9E4C',
    icon: 'posteo',
    domains: ['posteo.de', 'posteo.net'],
    values: { url: 'https://posteo.de:8443/' },
    probe: { kind: 'dav', url: 'https://posteo.de:8443/' },
  },
  {
    id: 'carddav-yahoo',
    label: 'Yahoo Contacts',
    protocol: 'carddav',
    brand: '#6001D2',
    icon: 'yahoo',
    domains: ['yahoo.com', 'yahoo.co.uk', 'ymail.com', 'rocketmail.com'],
    values: { url: 'https://carddav.address.yahoo.com' },
    probe: { kind: 'dav', url: 'https://carddav.address.yahoo.com' },
    appPassword: true,
    helpUrl: 'https://login.yahoo.com/account/security',
    note: 'Yahoo requires an app password — generate one under Account security.',
  },
  {
    id: 'carddav-nextcloud',
    label: 'Nextcloud / ownCloud',
    protocol: 'carddav',
    brand: '#0082C9',
    icon: 'nextcloud',
    domains: [],
    values: { url: 'https://{your-server}/remote.php/dav' },
    probe: { kind: 'none' },
    note: 'Replace {your-server} with the address you open Nextcloud at, e.g. cloud.example.com.',
  },
  {
    id: 'carddav-baikal',
    label: 'Baïkal',
    protocol: 'carddav',
    brand: '#5A7D9A',
    icon: 'baikal',
    domains: [],
    values: { url: 'https://{your-server}/dav.php' },
    probe: { kind: 'none' },
    note: 'Replace {your-server} with your Baïkal address.',
  },
  {
    id: 'carddav-synology',
    label: 'Synology Contacts',
    protocol: 'carddav',
    brand: '#0C3D6B',
    icon: 'synology',
    domains: [],
    values: { url: 'https://{your-nas}:5001/carddav.php' },
    probe: { kind: 'none' },
    note: 'Replace {your-nas} with your NAS address. 5001 is the default HTTPS port.',
  },
];

export const CONNECT_PRESETS: ConnectPreset[] = [
  ...IMAP_PRESETS,
  ...CALDAV_PRESETS,
  ...CARDDAV_PRESETS,
];

// ── Lookups ──────────────────────────────────────────────────────────────────

/** Whether this provider key has presets at all (only the protocol ones do). */
export function isPresetProtocol(key: string): key is PresetProtocol {
  return key === 'imap' || key === 'caldav' || key === 'carddav';
}

export function presetsFor(protocol: PresetProtocol): ConnectPreset[] {
  return CONNECT_PRESETS.filter(p => p.protocol === protocol);
}

export function findPreset(id: string | null | undefined): ConnectPreset | undefined {
  return CONNECT_PRESETS.find(p => p.id === id);
}

/** Domain of an email address, lowercased. Empty when there isn't one yet —
 *  this runs on every keystroke, so a half-typed address must not match. */
export function emailDomain(address: string): string {
  const at = address.lastIndexOf('@');
  if (at < 0) return '';
  return address.slice(at + 1).trim().toLowerCase();
}

/**
 * The preset that serves `address`, or undefined. Matches the exact domain and
 * any subdomain of it (`mail.gmx.net` → GMX), never a suffix collision
 * (`notgmail.com` must not match `gmail.com`). Catch-all presets carry no
 * domains and so are never auto-selected.
 */
export function matchPresetByEmail(
  protocol: PresetProtocol,
  address: string,
): ConnectPreset | undefined {
  const domain = emailDomain(address);
  if (!domain) return undefined;
  return presetsFor(protocol).find(p =>
    p.domains.some(d => domain === d || domain.endsWith(`.${d}`)),
  );
}

// ── Placeholders ─────────────────────────────────────────────────────────────

/** The `{tokens}` still left in the form, e.g. `['{your-server}']`. A form with
 *  any of these can't be submitted: only the user knows the real value. */
export function remainingPlaceholders(values: Record<string, string>): string[] {
  const found: string[] = [];
  for (const v of Object.values(values)) {
    for (const m of (v ?? '').match(PLACEHOLDER_RE) ?? []) {
      if (!found.includes(m)) found.push(m);
    }
  }
  return found;
}

/** Merge a preset's values over the current form, leaving fields the preset
 *  doesn't own (username, password, display name) untouched. */
export function applyPreset(
  preset: ConnectPreset,
  current: Record<string, string>,
): Record<string, string> {
  return { ...current, ...preset.values };
}

/** Field names any preset writes, for the "did the user hand-edit this?" check
 *  that stops auto-detection from clobbering typed-in server settings. */
export function presetOwnedFields(protocol: PresetProtocol): string[] {
  const names = new Set<string>();
  for (const p of presetsFor(protocol)) for (const k of Object.keys(p.values)) names.add(k);
  return [...names];
}
