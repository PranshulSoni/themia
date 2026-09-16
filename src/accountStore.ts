import { createSignal } from 'solid-js';
import { createStore } from 'solid-js/store';
// Demo-aware invoke: in demo mode this returns fixture accounts (incl. the demo
// RSS feed connections) so the Connections page + toggle lists are populated;
// outside demo it falls through to the real backend unchanged.
import { invoke } from './demo/invoke.ts';

/** Closed set of connection providers (1.13/B4, no stringly providers). The
 *  backend (widget_email) only ever emits these; `'demo'` is the demo-mode
 *  fixture provider. Use `isProvider` to narrow a genuinely-untrusted string. */
export type Provider =
  | 'microsoft'
  | 'google'
  | 'imap'
  | 'caldav'
  | 'carddav'
  | 'rss'
  | 'github'
  | 'claude'
  | 'demo';

const PROVIDERS: readonly Provider[] = [
  'microsoft', 'google', 'imap', 'caldav', 'carddav', 'rss', 'github', 'claude', 'demo',
];

export function isProvider(v: unknown): v is Provider {
  return typeof v === 'string' && (PROVIDERS as readonly string[]).includes(v);
}

export interface Account {
  id: string;
  email: string;
  display_name: string;
  nickname?: string | null;
  provider: Provider;
}

interface AccountStoreState {
  accounts: Account[];
}

export const [accountStore, setAccountStore] = createStore<AccountStoreState>({
  accounts: [],
});

const [rev, setRev] = createSignal(0);

/** Reactive revision counter, increments on any account mutation */
export const accountRevision = rev;

export async function loadAccounts(): Promise<void> {
  const raw = await invoke('email_list_accounts');
  // The IPC boundary types `provider` as a loose string; narrow each to the
  // closed `Provider` set (unknown → fall back to 'imap', the generic mail
  // provider; the backend never emits an unknown value, this just satisfies
  // the trust boundary without an `as` cast).
  const all: Account[] = raw.map((a) => ({
    ...a,
    provider: isProvider(a.provider) ? a.provider : 'imap',
  }));
  // Backend uses a HashMap, so iteration order is non-deterministic.
  // Sort here so every consumer (manager, toggle lists, filters) sees the
  // same order across widgets.
  all.sort((a, b) => {
    const p = a.provider.localeCompare(b.provider);
    if (p !== 0) return p;
    const e = a.email.toLowerCase().localeCompare(b.email.toLowerCase());
    if (e !== 0) return e;
    return a.id.localeCompare(b.id);
  });
  setAccountStore('accounts', all);
}

/** Bump revision so widgets know to re-check */
export function bumpRevision(): void {
  setRev(r => r + 1);
}
