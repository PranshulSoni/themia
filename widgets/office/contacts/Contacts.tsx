import { createSignal, createMemo, createEffect, onCleanup, onMount, untrack } from 'solid-js';
import { Show, For, Switch, Match } from 'solid-js';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { showCtxMenu, type CtxMenuIconProps } from '../../../src/ctxMenuStore.ts';
import type { Component } from 'solid-js';
import {
  Search, X, Lock,
  Mail, Phone, MessageSquare, Copy, User, Users, RefreshCw,
} from 'lucide-solid';
import { logger } from '../../../src/debugLog.ts';
import { registerWidgetKeyboard, type WidgetKeyEvent } from '../../../src/input/widgetKeyboard.ts';
import { onAppBlur } from '../../../src/input/appBlur.ts';
import { t } from '../../../src/i18n.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { visibleContacts } from './contactsView.ts';
import './Contacts.css';
import type { JSX } from 'solid-js';

export interface Contact {
  id: string;
  display_name: string;
  email?: string | null;
  phone?: string | null;
  company?: string | null;
  job_title?: string | null;
}

type ViewMode = 'list' | 'compact' | 'details' | 'grid';
type ColKey = 'name' | 'email' | 'phone' | 'company' | 'title';

interface ContactAction {
  icon?: Component<CtxMenuIconProps> | string;
  label?: string;
  action?: () => void;
  sep?: boolean;
  header?: boolean;
}

export interface ContactsProps {
  headerMode?: HeaderMode;
  label?: string;
  accountIds?: string[];
  accountId?: string;
  defaultSearch?: string;
  count?: number;
  viewMode?: ViewMode;
  rowColumns?: ColKey[];
  _refresh?: number;
}

const SW: number = 1.5;
const I_SM: number = 12;

const COL_DEFS: Record<ColKey, { label: string; width: string; field: keyof Contact }> = {
  name:    { get label() { return t('contacts.col.name'); },    width: '1fr',   field: 'display_name' },
  email:   { get label() { return t('contacts.col.email'); },   width: '1.2fr', field: 'email' },
  phone:   { get label() { return t('contacts.col.phone'); },   width: '108px', field: 'phone' },
  company: { get label() { return t('contacts.col.company'); }, width: '1fr',   field: 'company' },
  title:   { get label() { return t('contacts.col.title'); },   width: '1fr',   field: 'job_title' },
};

function initials(name: string | undefined): string {
  if (!name) return '?';
  const parts = name.trim().split(/\s+/);
  return parts.length >= 2
    ? (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
    : (name[0] || '?').toUpperCase();
}

function avatarColor(name: string | undefined): string {
  const n = name || '';
  let h = 0;
  for (let i = 0; i < n.length; i++) h = (h * 31 + n.charCodeAt(i)) & 0xfffffff;
  return `hsl(${h % 360},38%,34%)`;
}

function openUrl(url: string): void { invoke('open_in_explorer', { path: url }).catch(e => logger.error('contacts', `open_in_explorer failed: ${e}`)); }
function copy(text: string): void   { navigator.clipboard.writeText(text).catch(e => logger.error('contacts', `clipboard write failed: ${e}`)); }

function contactActions(c: Contact): ContactAction[] {
  const actions: ContactAction[] = [];
  if (c.email) {
    actions.push(
      { icon: Mail,  label: t('ctxMenu.sendEmail'), action: () => openUrl(`mailto:${c.email}`) },
      { icon: Copy,  label: t('ctxMenu.copyEmail'), action: () => copy(c.email!) },
    );
  }
  if (c.phone) {
    if (actions.length) actions.push({ sep: true });
    actions.push(
      { icon: Phone,          label: t('ctxMenu.call'),       action: () => openUrl(`tel:${c.phone}`) },
      { icon: MessageSquare,  label: t('ctxMenu.message'),    action: () => openUrl(`sms:${c.phone}`) },
      { icon: Copy,           label: t('ctxMenu.copyPhone'), action: () => copy(c.phone!) },
    );
  }
  if (actions.length) actions.push({ sep: true });
  actions.push({ icon: User, label: t('ctxMenu.copyName'), action: () => copy(c.display_name) });
  return actions;
}

export default function Contacts(props: ContactsProps): JSX.Element {
  const [view, setView] = createSignal<string>('loading');
  const [allContacts, setAllContacts] = createSignal<Contact[]>([]);
  const [error, setError] = createSignal<string | null>(null);
  const [needsReauth, setNeedsReauth] = createSignal<boolean>(false);
  const [searchQuery, setSearchQuery] = createSignal<string>(props.defaultSearch || '');
  const [selectedId, setSelectedId] = createSignal<string | null>(null);

  const effectiveIds = createMemo((): string[] => {
    const ids = props.accountIds;
    const id = props.accountId;
    const out = ids?.length ? ids : id ? [id] : [];
    if (!out.length && isDemo()) return ['demo-account'];
    return out;
  });

  // Filter by the live search query first (so search spans the whole address
  // book), then apply the `count` cap. Before this the cap was a no-op. [S-17]
  const contacts = createMemo((): Contact[] =>
    visibleContacts(allContacts(), searchQuery(), props.count)
  );

  const activeCols = createMemo((): ColKey[] => {
    const rc = props.rowColumns;
    return (rc?.length ? rc : ['name', 'email', 'phone'] as ColKey[]).filter((k): k is ColKey => k in COL_DEFS);
  });

  const detailColTemplate = createMemo((): string =>
    activeCols().map(k => COL_DEFS[k].width).join(' ')
  );

  function handleCtx(e: MouseEvent, c: Contact): void {
    e.preventDefault(); e.stopPropagation();
    setSelectedId(c.id);
    const actions = contactActions(c);
    showCtxMenu(e, [
      ...actions,
      // No leading separator when the contact has no email/phone actions.
      ...(actions.length ? [{ sep: true }] : []),
      { icon: RefreshCw, label: t('ctxMenu.refresh'), action: () => { void load(); } },
    ]);
  }

  function handleClick(e: MouseEvent, c: Contact): void {
    e.stopPropagation();
    setSelectedId(prev => prev === c.id ? null : c.id);
  }

  async function reauth(): Promise<void> {
    const ids = untrack(effectiveIds);
    for (const id of ids) {
      await invoke('email_sign_out_account', { accountId: id }).catch(e => logger.error('contacts', `email_sign_out_account failed: ${e}`));
    }
    setView('reauth');
  }

  // Per-account fetch size: at least 500 (so search spans a whole address book
  // even on the small default cap), growing with the configured `count` since
  // that is now unbounded, and clamped to the 1000-item page limit both
  // providers enforce (Graph `$top`, People API `pageSize`). [S-23]
  function fetchCount(): number {
    const want = Number.isFinite(props.count) ? Math.floor(props.count as number) : 0;
    return Math.min(1000, Math.max(500, want));
  }

  async function load(): Promise<void> {
    const ids = untrack(effectiveIds);
    if (!ids.length) { setView('empty'); return; }
    if (!allContacts().length) setView('loading'); setNeedsReauth(false);
    try {
      const count = fetchCount();
      const results = await Promise.allSettled(
        ids.map((id: string) => invoke('contacts_list', { accountId: id, search: '', count }))
      );
      const merged: Contact[] = [];
      let hasAuthError: boolean = false;
      for (const r of results) {
        if (r.status === 'fulfilled') {
          merged.push(...r.value);
        } else {
          const msg = String(r.reason);
          if (msg === 'contacts_not_authorized') hasAuthError = true;
        }
      }
      if (merged.length > 0 || !hasAuthError) {
        setAllContacts(merged);
        setView('loaded'); setError(null);
      } else {
        setNeedsReauth(true); setView('error');
      }
    } catch (e) {
      const msg = String(e);
      if (msg === 'contacts_not_authorized')                                       { setNeedsReauth(true); setView('error'); }
      else if (msg.includes('Account not found') || msg.includes('no_accounts'))   { setView('empty'); }
      else { setError(msg); setView('error'); }
    }
  }

  createEffect(() => {
    void effectiveIds();
    void props.count;
    void props._refresh;
    untrack(load);
  });

  // ── Keyboard nav ──────────────────────────────────────────────────────────
  function scrollSelectedIntoView(): void {
    queueMicrotask(() => {
      const el = document.querySelector<HTMLElement>(`[data-contact-id="${globalThis.CSS?.escape?.(selectedId() ?? '') ?? selectedId()}"]`);
      el?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    });
  }

  function handleKey(e: WidgetKeyEvent): void {
    const tag = document.activeElement?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA') return;
    const list = contacts();
    if (!list.length) return;
    const currId = selectedId();
    const curr = currId ? list.findIndex(c => c.id === currId) : -1;
    const last = list.length - 1;
    if (e.key === 'ArrowDown')     { e.preventDefault(); setSelectedId(list[curr < 0 ? 0 : Math.min(last, curr + 1)].id); scrollSelectedIntoView(); }
    else if (e.key === 'ArrowUp')  { e.preventDefault(); setSelectedId(list[curr < 0 ? last : Math.max(0, curr - 1)].id); scrollSelectedIntoView(); }
    else if (e.key === 'Home')     { e.preventDefault(); setSelectedId(list[0].id); scrollSelectedIntoView(); }
    else if (e.key === 'End')      { e.preventDefault(); setSelectedId(list[last].id); scrollSelectedIntoView(); }
    else if (e.key === 'Escape')   { setSelectedId(null); }
    else if (e.key === 'Enter')    {
      if (curr >= 0) {
        const c = list[curr];
        e.preventDefault();
        if (c.email) openUrl(`mailto:${c.email}`);
        else if (c.phone) openUrl(`tel:${c.phone}`);
      }
    }
  }

  let _kbd: ReturnType<typeof registerWidgetKeyboard> | undefined;
  let _cleanupBlur: (() => void) | undefined;
  onMount(() => {
    _kbd = registerWidgetKeyboard(handleKey);
    // Clicking outside the Themia app entirely never lands a DOM click in the
    // webview, so the background-click deselect can't catch it. Clear the
    // selection highlight on app blur too.
    _cleanupBlur = onAppBlur(() => setSelectedId(null));
  });
  onCleanup(() => { _kbd?.dispose(); _cleanupBlur?.(); });
  function activateKbd(e: MouseEvent): void { if (e.button === 0) _kbd?.activate(); }

  const vm = (): ViewMode => props.viewMode ?? 'list';

  return (
    <div class="contacts-widget" onClick={() => setSelectedId(null)} onMouseDown={activateKbd}>

      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={Users}
        title={props.label || t('contacts.title')}
      />

      <div class="widget-body contacts-body">

      {/* ── Search bar ── */}
      <Show when={(props.headerMode ?? 'normal') !== 'none'}>
      <div class="search-bar">
        <span class="search-icon"><Search size={I_SM} strokeWidth={SW} /></span>
        <input
          class="search-input"
          type="text"
          placeholder={t('contacts.searchPlaceholder')}
          value={searchQuery()}
          onInput={e => setSearchQuery(e.target.value)}
          onMouseDown={() => invoke('push_windows_to_desktop').catch(() => {})}
        />
        <Show when={searchQuery()}>
          <button class="clear-btn" onClick={() => setSearchQuery('')} title={t('widgetUi.clear')}>
            <X size={I_SM} strokeWidth={SW} />
          </button>
        </Show>
      </div>
      </Show>

      {/* ── States ── */}
      <Switch>
        <Match when={view() === 'empty' || view() === 'reauth'}>
          <div class="state-msg" innerHTML={t('contacts.selectAccount')} />
        </Match>

        <Match when={view() === 'loading' && !allContacts().length}>
          <div class="state-msg dim">{t('widgetUi.loading')}</div>
        </Match>

        <Match when={view() === 'error' && needsReauth()}>
          <div class="reauth-box">
            <div class="reauth-icon"><Lock size={28} strokeWidth={SW} /></div>
            <div class="reauth-title">{t('contacts.accessTitle')}</div>
            <div class="reauth-body">{t('contacts.accessBody')}</div>
            <button class="reauth-btn" onClick={reauth}>{t('contacts.reauthBtn')}</button>
          </div>
        </Match>

        <Match when={view() === 'error'}>
          <div class="state-msg error">{error()}</div>
        </Match>

        <Match when={view() === 'loaded'}>
          <Show when={contacts().length > 0} fallback={<div class="state-msg dim">{t('contacts.noContacts')}</div>}>

            {/* ══ List view ═══════════════════════════════════════════════ */}
            <Show when={vm() === 'list'}>
              <div class="scroll-area">
                <div class="list-view" style={{ gap: 'var(--item-gap,1px)' }}>
                  <For each={contacts()}>{c =>
                    <div
                      classList={{ 'list-row': true, sel: selectedId() === c.id }}
                      data-contact-id={c.id}
                      onClick={e => handleClick(e, c)}
                      onContextMenu={e => handleCtx(e, c)}
                    >
                      <div class="avatar av-md" style={`background:${avatarColor(c.display_name)}`}>{initials(c.display_name)}</div>
                      <div class="list-info">
                        <span class="c-name">{c.display_name}</span>
                        <Show when={c.job_title || c.company}>
                          <span class="c-sub">{[c.job_title, c.company].filter(Boolean).join(' · ')}</span>
                        </Show>
                        <Show when={c.email}><span class="c-detail"><Mail size={I_SM} strokeWidth={SW} /> {c.email}</span></Show>
                        <Show when={c.phone}><span class="c-detail"><Phone size={I_SM} strokeWidth={SW} /> {c.phone}</span></Show>
                      </div>
                    </div>
                  }</For>
                </div>
              </div>
            </Show>

            {/* ══ Compact view ════════════════════════════════════════════ */}
            <Show when={vm() === 'compact'}>
              <div class="scroll-area">
                <div class="compact-view" style={{ gap: 'var(--item-gap,1px)' }}>
                  <For each={contacts()}>{c =>
                    <div
                      classList={{ 'compact-row': true, sel: selectedId() === c.id }}
                      data-contact-id={c.id}
                      onClick={e => handleClick(e, c)}
                      onContextMenu={e => handleCtx(e, c)}
                    >
                      <div class="avatar av-sm" style={`background:${avatarColor(c.display_name)}`}>{initials(c.display_name)}</div>
                      <span class="c-name compact-name">{c.display_name}</span>
                      <span class="compact-aside">{c.email || c.company || c.phone || ''}</span>
                    </div>
                  }</For>
                </div>
              </div>
            </Show>

            {/* ══ Details view ════════════════════════════════════════════ */}
            <Show when={vm() === 'details'}>
              <div class="scroll-area">
                <div class="details-container" style={`--det-cols:${detailColTemplate()};gap:var(--item-gap,1px)`}>
                  <div class="details-table">
                    <div class="details-header">
                      <For each={activeCols()}>{col =>
                        <div>{COL_DEFS[col].label}</div>
                      }</For>
                    </div>
                    <For each={contacts()}>{c =>
                      <div
                        classList={{ 'details-row': true, sel: selectedId() === c.id }}
                        data-contact-id={c.id}
                        onClick={e => handleClick(e, c)}
                        onContextMenu={e => handleCtx(e, c)}
                      >
                        <For each={activeCols()}>{col =>
                          <span classList={{ 'det-cell': true, 'det-name': col === 'name' }}>
                            <Show when={col === 'name'}>
                              <div class="avatar av-xs" style={`background:${avatarColor(c.display_name)}`}>{initials(c.display_name)}</div>
                            </Show>
                            {c[COL_DEFS[col].field] ?? ''}
                          </span>
                        }</For>
                      </div>
                    }</For>
                  </div>
                </div>
              </div>
            </Show>

            {/* ══ Grid (card) view ════════════════════════════════════════ */}
            <Show when={vm() === 'grid'}>
              <div class="scroll-area">
                <div class="grid-view" style={{ gap: 'var(--item-gap,1px)' }}>
                  <For each={contacts()}>{c =>
                    <div
                      classList={{ 'grid-card': true, sel: selectedId() === c.id }}
                      data-contact-id={c.id}
                      onClick={e => handleClick(e, c)}
                      onContextMenu={e => handleCtx(e, c)}
                    >
                      <div class="avatar av-lg" style={`background:${avatarColor(c.display_name)}`}>{initials(c.display_name)}</div>
                      <span class="gc-name">{c.display_name}</span>
                      <Show when={c.company}>
                        <span class="gc-sub">{c.company}</span>
                      </Show>
                      <Show when={!c.company && c.job_title}>
                        <span class="gc-sub">{c.job_title}</span>
                      </Show>
                    </div>
                  }</For>
                </div>
              </div>
            </Show>

          </Show>
        </Match>
      </Switch>

      </div>
    </div>
  );
}
