import { createMemo, createEffect, onMount, onCleanup } from 'solid-js';
import { For, Show } from 'solid-js';
import { accountStore, loadAccounts, accountRevision, bumpRevision } from '../accountStore.ts';
import { onTransient } from '../sync.ts';
import { ProviderLogo } from './AccountManager.tsx';
import ToggleSwitch from './ToggleSwitch.tsx';
import { providerLabel } from '../providerLabel.ts';
import { Users } from 'lucide-solid';
import type { Accessor, JSX } from 'solid-js';
import './AccountManager.css';
import { t } from '../i18n.ts';

export interface AccountToggleListProps {
  /** Provider types to show, e.g. ['microsoft', 'imap'] */
  providerFilter: string[];
  /** Provider badge labels, e.g. { microsoft: 'M365', imap: 'IMAP' } */
  providerBadges: Record<string, string>;
  /** Currently selected account IDs */
  accountIds: Accessor<string[]>;
  /** Toggle an account on/off */
  onToggle: (id: string) => void;
  /** Open the Connections settings page (shows every connection, unfiltered). */
  onManageAccounts?: () => void;
  /** Optional content rendered directly beneath each account row (e.g. the
   *  calendar widget's per-account calendar picker). Return null to render
   *  nothing for a given account. */
  perAccountExtra?: (accountId: string) => JSX.Element | null;
  /** Optional non-account sources rendered as the FIRST rows of the list,
   *  styled + selected exactly like account rows (e.g. the To Do widget's
   *  "This device" local-storage option). Rendered by this component so their
   *  `active` accessor stays reactive. */
  leadingOptions?: ToggleListOption[];
}

/** A non-account source shown in the toggle list (e.g. "This device"). */
export interface ToggleListOption {
  id: string;
  name: string;
  badge?: string;
  sublabel?: string;
  /** A pre-built icon element (static); e.g. `<HardDrive class="provider-logo" …/>`. */
  icon: JSX.Element;
  /** Reactive selected-state accessor. */
  active: Accessor<boolean>;
  onToggle: () => void;
  /** Render the switch as non-interactive. For a widget whose list holds this
   *  ONE source (Mood Tracker): there is nothing to switch to, so an enabled
   *  switch would invite a click that cannot do anything. */
  disabled?: boolean;
  /** Optional content rendered directly beneath this option's row, inside the
   *  same entry — the leading-option counterpart of `perAccountExtra` (e.g. the
   *  Notes widget's note picker). A function, not an element, so it is built
   *  where it is rendered and its reactivity tracks there. */
  extra?: () => JSX.Element | null;
}

export default function AccountToggleList(props: AccountToggleListProps): JSX.Element {
  // Load accounts on mount and whenever revision bumps
  createEffect(() => {
    accountRevision();
    loadAccounts();
  });

  // The Connect window (a separate OS window) broadcasts on success, so reload
  // a freshly added account shows up here without reopening the config.
  onMount(() => {
    const off = onTransient('accounts-changed', () => { loadAccounts(); bumpRevision(); });
    onCleanup(off);
  });

  const filtered = createMemo(() =>
    accountStore.accounts.filter(a => props.providerFilter.includes(a.provider))
  );

  return (
    <div class="account-toggle-list">
      <For each={props.leadingOptions ?? []}>{opt => (
        <div class="account-entry">
          <div classList={{ 'account-row': true, active: opt.active() }}>
            {opt.icon}
            <div class="account-info" onClick={opt.onToggle}>
              <div class="account-name-row">
                <span class="account-name">{opt.name}</span>
                <Show when={opt.badge}><span class="provider-badge">{opt.badge}</span></Show>
              </div>
              <Show when={opt.sublabel}><span class="account-email">{opt.sublabel}</span></Show>
            </div>
            <div class="account-btns">
              <ToggleSwitch
                checked={opt.active()}
                disabled={opt.disabled}
                onLabel={t('account.active')}
                offLabel={t('account.inactive')}
                onChange={opt.onToggle}
              />
            </div>
          </div>
          {opt.extra?.()}
        </div>
      )}</For>

      <For each={filtered()}>{acc => {
        const active = () => props.accountIds().includes(acc.id);
        return (
          <div class="account-entry">
            <div classList={{ 'account-row': true, active: active() }}>
              <ProviderLogo provider={acc.provider} />
              <div class="account-info" onClick={() => props.onToggle(acc.id)}>
                <div class="account-name-row">
                  <span class="account-name">{acc.nickname ?? acc.display_name}</span>
                  <span class="provider-badge">{providerLabel(acc.provider)}</span>
                </div>
                <span class="account-email">{acc.email}</span>
              </div>
              <div class="account-btns">
                <ToggleSwitch
                  checked={active()}
                  onLabel={t('account.active')}
                  offLabel={t('account.inactive')}
                  onChange={() => props.onToggle(acc.id)}
                />
              </div>
            </div>
            {props.perAccountExtra?.(acc.id)}
          </div>
        );
      }}</For>

      <Show when={props.onManageAccounts}>
        <div class="account-toggle-hint">
          <button class="manage-accounts-btn" onClick={() => props.onManageAccounts?.()}>
            <Users size={13} strokeWidth={1.5} /> Manage connections
          </button>
        </div>
      </Show>
    </div>
  );
}
