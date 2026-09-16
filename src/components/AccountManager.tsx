import { createSignal, onMount, onCleanup } from 'solid-js';
import { Show, For } from 'solid-js';
import { invoke } from '../ipc.ts';
import { ask } from '@tauri-apps/plugin-dialog';
import { getCurrentWindow } from '@tauri-apps/api/window';
import type { JSX, Accessor } from 'solid-js';
import { Pencil, Check, X, LogOut, Mail, CalendarDays, ContactRound, Plus, Rss } from 'lucide-solid';
import { providerLabel } from '../providerLabel.ts';
import { BrandMark } from './BrandMark.tsx';
import type { BrandIconKey } from '../connect/brandIcons.ts';
import { t } from '../i18n.ts';
import { onTransient } from '../sync.ts';
import '../styles/config-base.css';
import './AccountManager.css';

/**
 * Stored provider (and Connect-picker key) → the brand whose published mark
 * stands for it. `github-pat` is the token-based way into the SAME service, so
 * it shows the same mark; only the connect card differs. [S-51 email]
 *
 * A protocol has no vendor and so no entry: IMAP, CalDAV, CardDAV and RSS fall
 * through to the lucide glyphs below.
 */
const PROVIDER_BRANDS: Record<string, BrandIconKey> = {
  microsoft: 'microsoft',
  google: 'google',
  github: 'github',
  'github-pat': 'github',
  claude: 'claude',
};

export function ProviderLogo(props: { provider: string }) {
  const brand = PROVIDER_BRANDS[props.provider];
  if (brand) return <BrandMark brand={brand} class="provider-logo" />;
  if (props.provider === 'imap') {
    return <span class="provider-logo provider-logo-lucide"><Mail size={18} strokeWidth={1.5} /></span>;
  }
  if (props.provider === 'caldav') {
    return <span class="provider-logo provider-logo-lucide"><CalendarDays size={18} strokeWidth={1.5} /></span>;
  }
  if (props.provider === 'carddav') {
    return <span class="provider-logo provider-logo-lucide"><ContactRound size={18} strokeWidth={1.5} /></span>;
  }
  if (props.provider === 'rss') {
    return <span class="provider-logo provider-logo-lucide"><Rss size={18} strokeWidth={1.5} /></span>;
  }
  return <div class="provider-logo provider-logo-fallback">{props.provider[0]?.toUpperCase()}</div>;
}

// ── Types ────────────────────────────────────────────────────────────────────

export interface Account {
  id: string;
  email: string;
  display_name: string;
  nickname?: string | null;
  provider: string;
}

export interface AccountManagerProps {
  /** All accounts to display */
  accounts: Accessor<Account[]>;
  /** Called after any mutation (add, sign-out, rename) so parent can refresh */
  onMutated: () => void;
}

// ── Component ────────────────────────────────────────────────────────────────

export default function AccountManager(props: AccountManagerProps): JSX.Element {
  // Inline rename state
  const [editingId, setEditingId] = createSignal<string | null>(null);
  const [editingValue, setEditingValue] = createSignal<string>('');

  // The Connect window broadcasts this on success; refresh the list live.
  onMount(() => {
    const off = onTransient('accounts-changed', () => props.onMutated());
    onCleanup(off);
  });

  async function signOut(id: string): Promise<void> {
    // Disconnecting is destructive (widgets lose the account) and the button
    // sits right next to Rename, so confirm before tearing the connection down.
    const acc = props.accounts().find(a => a.id === id);
    const name = acc?.nickname ?? acc?.display_name ?? acc?.email ?? t('account.thisAccount');
    const ok = await ask(t('account.disconnectConfirm', { name }), {
      title: t('account.disconnectTitle'),
      kind: 'warning',
    });
    if (!ok) return;
    await invoke('email_sign_out_account', { accountId: id });
    props.onMutated();
  }

  function startEdit(acc: Account): void {
    setEditingId(acc.id);
    setEditingValue(acc.nickname ?? acc.display_name);
  }
  function cancelEdit(): void {
    setEditingId(null);
    setEditingValue('');
  }
  async function saveEdit(): Promise<void> {
    const id = editingId();
    if (!id) return;
    await invoke('email_rename_account', { accountId: id, nickname: editingValue() });
    cancelEdit();
    props.onMutated();
  }
  function editKeydown(e: KeyboardEvent): void {
    if (e.key === 'Enter') saveEdit();
    if (e.key === 'Escape') cancelEdit();
  }

  const hasAccounts = () => props.accounts().length > 0;

  /** Open the dedicated Connect window on its provider picker. [S-40]
   *
   *  This page used to duplicate that picker inline, so the full provider list
   *  sat in the middle of Settings and grew with every provider added. One
   *  button instead: the catalog lives in exactly one place (the Connect
   *  window), which is also the only place that can guide the user through
   *  what they picked. */
  function openConnect(): void {
    invoke('open_connect_window', {
      parentLabel: getCurrentWindow().label,
      provider: null,
    }).catch(() => {});
  }

  return (
    <div class="account-manager">

      {/* ── Connected accounts section ── */}
      <section class="am-section">
        <header class="am-section-head">
          <div class="am-section-heading">
            <span class="am-section-title">{t('account.connectedTitle')}</span>
            <span class="am-section-desc">
              {t('account.connectedDesc')}
            </span>
          </div>
          {/* Top-right: the standing action for this page, in the spot the eye
              lands on for "add" across the app. */}
          <button class="am-add-btn" onClick={openConnect}>
            <Plus size={15} strokeWidth={1.5} />
            {t('account.addConnection')}
          </button>
        </header>

        <Show when={hasAccounts()} fallback={
          <div class="am-empty">
            <div class="am-empty-title">{t('account.emptyTitle')}</div>
            <div class="am-empty-desc">{t('account.emptyDesc')}</div>
          </div>
        }>
          <div class="am-account-list">
            <For each={props.accounts()}>{acc => (
              <div class="account-row">
                <ProviderLogo provider={acc.provider} />
                <Show when={editingId() === acc.id} fallback={
                  <>
                    <div class="account-info">
                      <div class="account-name-row">
                        <span class="account-name">{acc.nickname ?? acc.display_name}</span>
                        <span class="provider-badge">{providerLabel(acc.provider)}</span>
                      </div>
                      <span class="account-email">{acc.email}</span>
                    </div>
                    <div class="account-btns">
                      <button class="edit-btn" onClick={() => startEdit(acc)}><Pencil size={13} strokeWidth={1.5} /> {t('common.rename')}</button>
                      <button class="btn-danger" onClick={() => signOut(acc.id)}><LogOut size={13} strokeWidth={1.5} /> {t('account.disconnect')}</button>
                    </div>
                  </>
                }>
                  <input
                    class="rename-input"
                    value={editingValue()}
                    onInput={e => setEditingValue(e.target.value)}
                    onKeyDown={editKeydown}
                    placeholder={t('account.label')}
                  />
                  <div class="rename-actions">
                    <button class="rename-ok" title={t('common.save')} onClick={saveEdit}><Check size={13} strokeWidth={1.5} /></button>
                    <button class="rename-cancel" title={t('common.cancel')} onClick={cancelEdit}><X size={13} strokeWidth={1.5} /></button>
                  </div>
                </Show>
              </div>
            )}</For>

            {/* Ghost row: the same shape as a real connection, drawn as an
                outline. It reads as the next slot in the list, so the place to
                add one is where the eye already is after scanning what's
                there — no hunting back up to the header. */}
            <button class="account-row am-add-ghost" onClick={openConnect}>
              <span class="am-ghost-logo"><Plus size={17} strokeWidth={1.5} /></span>
              <div class="account-info">
                <div class="account-name-row">
                  <span class="account-name">{t('account.addConnection')}</span>
                </div>
                <span class="account-email">{t('account.addGhostDesc')}</span>
              </div>
            </button>
          </div>
        </Show>
      </section>
    </div>
  );
}
