// ---------------------------------------------------------------------------
// Connect-window provider catalog
//
// Single source of truth for the "add a connection" flow. Both the Connections
// picker (src/components/AccountManager.tsx) and the dedicated Connect window
// (src/ConnectWindow.tsx) read from here.
//
//  - `form`         providers collect typed fields and `submit()` them.
//  - `oauth-device` providers (Microsoft) show a device code + poll for sign-in.
//  - `oauth-browser`providers (Google) open the browser and block on a callback.
//
// Every backend add/auth path already *connects to validate* before persisting
// (see widgets/office/email/src/lib.rs), so a successful submit/auth is itself
// the connection test. There is no separate "test" command.
// ---------------------------------------------------------------------------

import { invoke } from '../ipc.ts';
import type { JSX } from 'solid-js';
import { t } from '../i18n.ts';

export interface ConnectField {
  /** Key passed to `submit` in the values map. */
  name: string;
  label: string;
  type: 'text' | 'password' | 'url' | 'number';
  placeholder?: string;
  optional?: boolean;
  hint?: JSX.Element;
  default?: string;
  /** Render on the same row as the field before it, rather than below. For a
   *  pair that is really one setting — an IMAP server and its port. */
  inline?: boolean;
  /** Size to its content instead of filling the row (a port is 3 digits). */
  narrow?: boolean;
}

export type ConnectKind = 'form' | 'oauth-device' | 'oauth-browser' | 'oauth-paste';

export interface ConnectProvider {
  key: string;
  label: string;
  /** One-line description shown in the picker and atop the form. */
  blurb: string;
  /**
   * Widgets this connection feeds, as `widget.<key>` i18n keys ([S-49 email]).
   * Answers the question the picker otherwise leaves hanging — "what do I
   * actually get for connecting this?" — and localizes for free, since these
   * are the same labels the Add-Widget picker shows.
   *
   * The registry's own `providers` field can't serve this: only a handful of
   * widgets declare it, and none of the protocol ones do. `connectUsesAreReal`
   * (providers.test.ts) guards every key against the message catalog so a
   * renamed widget fails a test instead of rendering a raw key.
   */
  uses: string[];
  group: 'service' | 'protocol';
  /** Pre-release gate: HIDDEN from the picker unless devMode is on
   *  (see connect/visibility.ts). Gates discovery only; existing connections
   *  keep working. */
  devOnly?: boolean;
  kind: ConnectKind;
  /** `oauth-device` providers only: which device flow to drive. Carried here so
   *  the Connect window runs ONE device-code path for every such provider
   *  instead of a per-provider branch. [S-51] */
  device?: {
    start: () => Promise<DeviceAuth>;
    /** Resolves the new account id once approved, or null while still pending. */
    poll: (deviceCode: string) => Promise<string | null>;
  };
  /** Step-by-step guidance shown above the form / auth panel. */
  steps?: string[];
  /** Form providers only. */
  fields?: ConnectField[];
  /** Form providers only. Shapes values into the matching invoke() call. */
  submit?: (values: Record<string, string>) => Promise<unknown>;
}

export interface DeviceAuth {
  verification_url: string;
  user_code: string;
  device_code: string;
  interval: number;
}

const trimOrNull = (v: string | undefined): string | null => {
  const t = (v ?? '').trim();
  return t.length > 0 ? t : null;
};

// ── OAuth helpers (Microsoft device-code, Google loopback) ───────────────────

export async function microsoftStartAuth(): Promise<DeviceAuth> {
  const auth = await invoke('email_start_device_auth');
  // Best-effort: open the verification page so the user can type the code.
  invoke('email_open_url', { url: auth.verification_url }).catch(() => {});
  return auth;
}

/** Returns the new account id when sign-in completes, or null while pending. */
export async function microsoftPoll(deviceCode: string): Promise<string | null> {
  const acc = await invoke('email_poll_auth', { deviceCode });
  return acc?.id ?? null;
}

// ── GitHub device code ───────────────────────────────────────────────────────
// Identical grant to Microsoft's: a desktop app has nowhere safe to keep a
// client secret, and the device flow is the grant designed around that. [S-51]

export async function githubStartAuth(): Promise<DeviceAuth> {
  const auth = await invoke('email_start_github_device_auth');
  invoke('email_open_url', { url: auth.verification_url }).catch(() => {});
  return auth;
}

export async function githubPoll(deviceCode: string): Promise<string | null> {
  const acc = await invoke('email_poll_github_device_auth', { deviceCode });
  return acc?.id ?? null;
}

export function openExternal(url: string): void {
  invoke('email_open_url', { url }).catch(() => {});
}

/** Opens the consent page and blocks (up to ~5 min) on the loopback callback. */
export async function googleAuth(): Promise<void> {
  const start = await invoke('email_start_google_auth');
  invoke('email_open_url', { url: start.auth_url }).catch(() => {});
  await invoke('email_complete_google_auth', { state: start.state });
}

// ── Claude OAuth (authorization-code + PKCE, manual code paste) ───────────────
// No whitelisted loopback for Claude's client, so the user copies the `code#state`
// the callback page shows and pastes it back — hence the two-call start/complete.

/** Begins Claude OAuth: opens the consent page and returns the state to echo back. */
export async function claudeStart(): Promise<string> {
  const start = await invoke('email_start_claude_auth');
  invoke('email_open_url', { url: start.auth_url }).catch(() => {});
  return start.state;
}

/** Finishes Claude OAuth by exchanging the pasted `code#state` value. */
export async function claudeComplete(code: string, state: string): Promise<{ email: string }> {
  return invoke('email_complete_claude_auth', { code: code.trim(), state, nickname: null });
}

// ── Catalog ──────────────────────────────────────────────────────────────────

export const CONNECT_PROVIDERS: ConnectProvider[] = [
  {
    key: 'microsoft',
    label: 'Microsoft',
    get blurb() { return t('connect.provider.microsoft.blurb'); },
    uses: ['email', 'calendar', 'contacts', 'todo', 'onedrive'],
    group: 'service',
    kind: 'oauth-device',
    device: { start: microsoftStartAuth, poll: microsoftPoll },
    get steps() {
      return [
        t('connect.provider.microsoft.step1'),
        t('connect.provider.microsoft.step2'),
        t('connect.provider.microsoft.step3'),
      ];
    },
  },
  {
    key: 'google',
    label: 'Google',
    get blurb() { return t('connect.provider.google.blurb'); },
    uses: ['email', 'calendar', 'contacts', 'todo'],
    group: 'service',
    devOnly: true, // hidden behind developer mode until launch
    kind: 'oauth-browser',
    get steps() {
      return [
        t('connect.provider.google.step1'),
        t('connect.provider.google.step2'),
        t('connect.provider.google.step3'),
      ];
    },
  },
  {
    key: 'github',
    label: 'GitHub',
    get blurb() { return t('connect.provider.github.blurb'); },
    uses: ['github', 'githubNotifications', 'githubAssignedIssues', 'githubReviewRequested'],
    group: 'service',
    // Pre-release gate, NOT a provisioning marker: the OAuth app is registered
    // and the flow works, this is simply not launched yet. (Google sits behind
    // the same flag with a fully provisioned client id.) Unhiding is a release
    // decision. The token card below stays visible, so GitHub is reachable
    // either way. [S-51]
    devOnly: true,
    kind: 'oauth-device',
    device: { start: githubStartAuth, poll: githubPoll },
    get steps() {
      return [
        t('connect.provider.github.step1'),
        t('connect.provider.github.step2'),
        t('connect.provider.github.step3'),
      ];
    },
  },
  {
    // Kept alongside the OAuth card, not replaced by it: GitHub Enterprise
    // Server cannot authenticate against a github.com OAuth app, so a token is
    // the only way in for those users. Both land on the same stored account
    // (`provider: "github"`), so widgets can't tell them apart. [S-51]
    key: 'github-pat',
    get label() { return t('connect.provider.githubPat.label'); },
    get blurb() { return t('connect.provider.githubPat.blurb'); },
    uses: ['github', 'githubNotifications', 'githubAssignedIssues', 'githubReviewRequested'],
    group: 'service',
    kind: 'form',
    get steps() {
      return [
        t('connect.provider.githubPat.step1'),
        t('connect.provider.githubPat.step2'),
      ];
    },
    get fields() {
      return [
        { name: 'nickname', get label() { return t('connect.field.nickname'); }, type: 'text' as const,
          get placeholder() { return t('connect.provider.githubPat.nicknamePlaceholder'); }, optional: true,
          get hint() { return t('connect.provider.githubPat.nicknameHint'); } },
        { name: 'token', get label() { return t('connect.provider.githubPat.tokenLabel'); }, type: 'password' as const,
          placeholder: 'ghp_…',
          get hint() {
            // Three keys rather than one, because the middle run is a click
            // target: a translator moves the link within the sentence without
            // the string ever having to carry markup.
            return (
              <>
                {t('connect.provider.githubPat.tokenHintNeed')}{' '}
                <span class="device-url" onClick={() => openExternal('https://github.com/settings/tokens/new?scopes=repo,read:user,notifications&description=Themia')}>
                  {t('connect.provider.githubPat.tokenHintLink')}
                </span>{' '}
                {t('connect.provider.githubPat.tokenHintScopes', { scopes: 'repo, read:user, notifications' })}
              </>
            );
          } },
        { name: 'apiBaseUrl', get label() { return t('connect.provider.githubPat.apiBaseLabel'); }, type: 'text' as const,
          placeholder: 'https://api.github.com', optional: true,
          get hint() { return t('connect.provider.githubPat.apiBaseHint'); } },
      ];
    },
    submit: (v) => invoke('email_add_github_account', {
      token: v.token.trim(),
      apiBaseUrl: trimOrNull(v.apiBaseUrl),
      nickname: trimOrNull(v.nickname),
    }),
  },
  {
    key: 'claude',
    label: 'Claude Code',
    get blurb() { return t('connect.provider.claude.blurb'); },
    uses: ['claudeUsage'],
    group: 'service',
    kind: 'oauth-paste',
    get steps() {
      return [
        t('connect.provider.claude.step1'),
        t('connect.provider.claude.step2'),
        t('connect.provider.claude.step3'),
      ];
    },
  },
  {
    key: 'imap',
    label: 'IMAP',
    get blurb() { return t('connect.provider.imap.blurb'); },
    uses: ['email'],
    group: 'protocol',
    kind: 'form',
    get steps() {
      return [
        t('connect.provider.imap.step1'),
        t('connect.provider.imap.step2'),
      ];
    },
    get fields() {
      return [
        { name: 'displayName', get label() { return t('connect.field.displayName'); }, type: 'text' as const,
          get placeholder() { return t('connect.provider.imap.displayNamePlaceholder'); }, optional: true,
          get hint() { return t('connect.field.displayNameHint'); } },
        // Server + port are one setting split across two boxes, so they share a
        // row. They used to be separated by the username field, which read as
        // three unrelated questions and split the pair the presets fill.
        { name: 'host', get label() { return t('connect.provider.imap.hostLabel'); }, type: 'text' as const,
          placeholder: 'imap.gmail.com' },
        { name: 'port', get label() { return t('connect.field.port'); }, type: 'number' as const,
          placeholder: '993', default: '993', inline: true, narrow: true },
        { name: 'username', get label() { return t('connect.field.username'); }, type: 'text' as const,
          get placeholder() { return t('connect.field.usernamePlaceholder'); } },
        { name: 'password', get label() { return t('connect.field.password'); }, type: 'password' as const,
          get placeholder() { return t('connect.field.passwordPlaceholder'); },
          get hint() { return t('connect.field.passwordHint'); } },
      ];
    },
    submit: (v) => invoke('email_add_imap_account', {
      host: v.host.trim(),
      port: Number(v.port) || 993,
      username: v.username.trim(),
      password: v.password,
      displayName: (v.displayName ?? '').trim() || v.username.trim(),
      nickname: null,
    }),
  },
  {
    key: 'caldav',
    label: 'CalDAV',
    get blurb() { return t('connect.provider.caldav.blurb'); },
    uses: ['calendar'],
    group: 'protocol',
    kind: 'form',
    get steps() {
      return [
        t('connect.provider.caldav.step1'),
        t('connect.provider.caldav.step2'),
        t('connect.provider.caldav.step3'),
      ];
    },
    get fields() {
      return [
        { name: 'displayName', get label() { return t('connect.field.displayName'); }, type: 'text' as const,
          get placeholder() { return t('connect.provider.caldav.displayNamePlaceholder'); }, optional: true,
          get hint() { return t('connect.field.displayNameHint'); } },
        { name: 'url', get label() { return t('connect.provider.caldav.urlLabel'); }, type: 'url' as const,
          placeholder: 'https://cloud.example.com',
          get hint() { return t('connect.provider.caldav.urlHint'); } },
        { name: 'username', get label() { return t('connect.field.username'); }, type: 'text' as const,
          get placeholder() { return t('connect.field.usernamePlaceholder'); } },
        { name: 'password', get label() { return t('connect.field.password'); }, type: 'password' as const,
          get placeholder() { return t('connect.field.passwordPlaceholder'); },
          get hint() { return t('connect.field.passwordHint'); } },
      ];
    },
    submit: (v) => invoke('email_add_caldav_account', {
      url: v.url.trim(),
      username: v.username.trim(),
      password: v.password,
      displayName: (v.displayName ?? '').trim() || v.username.trim(),
      nickname: null,
    }),
  },
  {
    key: 'rss',
    label: 'RSS / Atom',
    get blurb() { return t('connect.provider.rss.blurb'); },
    uses: ['rss'],
    group: 'protocol',
    kind: 'form',
    get steps() {
      return [
        t('connect.provider.rss.step1'),
        t('connect.provider.rss.step2'),
      ];
    },
    get fields() {
      return [
        { name: 'nickname', get label() { return t('connect.field.name'); }, type: 'text' as const,
          get placeholder() { return t('connect.provider.rss.namePlaceholder'); }, optional: true,
          get hint() { return t('connect.provider.rss.nameHint'); } },
        { name: 'url', get label() { return t('connect.provider.rss.urlLabel'); }, type: 'url' as const,
          placeholder: 'https://example.com/feed.xml',
          get hint() { return t('connect.provider.rss.urlHint'); } },
      ];
    },
    submit: (v) => invoke('email_add_rss_feed', {
      url: v.url.trim(),
      nickname: trimOrNull(v.nickname),
    }),
  },
  {
    key: 'carddav',
    label: 'CardDAV',
    get blurb() { return t('connect.provider.carddav.blurb'); },
    uses: ['contacts'],
    group: 'protocol',
    kind: 'form',
    get steps() {
      return [
        t('connect.provider.carddav.step1'),
        t('connect.provider.carddav.step2'),
        t('connect.provider.carddav.step3'),
      ];
    },
    get fields() {
      return [
        { name: 'displayName', get label() { return t('connect.field.displayName'); }, type: 'text' as const,
          get placeholder() { return t('connect.provider.carddav.displayNamePlaceholder'); }, optional: true,
          get hint() { return t('connect.field.displayNameHint'); } },
        { name: 'url', get label() { return t('connect.provider.carddav.urlLabel'); }, type: 'url' as const,
          placeholder: 'https://cloud.example.com',
          get hint() { return t('connect.provider.carddav.urlHint'); } },
        { name: 'username', get label() { return t('connect.field.username'); }, type: 'text' as const,
          get placeholder() { return t('connect.field.usernamePlaceholder'); } },
        { name: 'password', get label() { return t('connect.field.password'); }, type: 'password' as const,
          get placeholder() { return t('connect.field.passwordPlaceholder'); },
          get hint() { return t('connect.field.passwordHint'); } },
      ];
    },
    submit: (v) => invoke('email_add_carddav_account', {
      url: v.url.trim(),
      username: v.username.trim(),
      password: v.password,
      displayName: (v.displayName ?? '').trim() || v.username.trim(),
      nickname: null,
    }),
  },
];

export function findProvider(key: string | null | undefined): ConnectProvider | undefined {
  return CONNECT_PROVIDERS.find(p => p.key === key);
}
