import { createSignal, createMemo, createEffect, onCleanup, onMount, untrack, type Accessor } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { Show, For, Switch, Match } from 'solid-js';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import {
  Mail, MailOpen, ArrowDownWideNarrow, ArrowUpNarrowWide,
  ExternalLink, MailCheck, MailMinus, Trash2, Archive, FolderInput, Copy, Reply,
} from 'lucide-solid';
import { showCtxMenu } from '../../../src/ctxMenuStore.ts';
import { resolveMetrics } from '../../../src/widgets/metricSet.ts';
import { LIST_FIELD_DEFS } from './columns.ts';
import { logger } from '../../../src/debugLog.ts';
import { providerLabel } from '../../../src/providerLabel.ts';
import { registerWidgetKeyboard, type WidgetKeyEvent } from '../../../src/input/widgetKeyboard.ts';
import { onAppBlur } from '../../../src/input/appBlur.ts';
import { t, lang } from '../../../src/i18n.ts';
import { useLayout } from '../../../src/viewContext.tsx';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './Email.css';

export interface EmailProps {
  accountIds?: string[];
  accountId?: string;
  folder?: string;
  messageCount?: number;
  viewMode?: string;
  previewLines?: number;
  rowColumns?: string[];
  listFields?: string[];
  label?: string;
  sortOrder?: string;
  headerMode?: HeaderMode;
  _refresh?: number;
  _widgetId?: string;
}

export interface EmailMessage {
  id: string;
  subject: string;
  from_name: string;
  from_email: string;
  received_at: string;
  is_read: boolean;
  preview: string;
  web_link: string;
  account_id: string;
  account_email: string;
  account_nickname: string;
  account_provider: string;
}

interface FolderStatus {
  unread: number;
  total: number;
}

interface ColDef {
  label: string;
  cellStyle: string;
}

const COL_DEFS: Record<string, ColDef> = {
  from:    { get label() { return t('email.col.from'); },    cellStyle: 'width:26%;flex-shrink:0' },
  subject: { get label() { return t('email.col.subject'); }, cellStyle: 'flex:1;min-width:0' },
  date:    { get label() { return t('email.col.date'); },    cellStyle: 'width:52px;flex-shrink:0;text-align:right' },
  preview: { get label() { return t('email.col.preview'); }, cellStyle: 'flex:1;min-width:0' },
  account: { get label() { return t('email.col.account'); }, cellStyle: 'width:68px;flex-shrink:0' },
};

const ICON_SW: number = 1.5;

function formatTime(iso: string, nowMs: number): string {
  const d = new Date(iso);
  const diff = nowMs - d.getTime();
  if (diff < 60_000)         return t('email.timeJustNow');
  if (diff < 3_600_000)      return `${Math.floor(diff / 60_000)}m`;
  if (diff < 86_400_000)     return `${Math.floor(diff / 3_600_000)}h`;
  if (diff < 7 * 86_400_000) return `${Math.floor(diff / 86_400_000)}d`;
  return d.toLocaleDateString(lang(), { month: 'short', day: 'numeric' });
}

function formatDateFull(iso: string, nowMs: number): string {
  const d = new Date(iso);
  const diff = nowMs - d.getTime();
  if (diff < 86_400_000) return d.toLocaleTimeString(lang(), { hour: 'numeric', minute: '2-digit' });
  if (diff < 7 * 86_400_000) return d.toLocaleDateString(lang(), { weekday: 'short' });
  return d.toLocaleDateString(lang(), { month: 'short', day: 'numeric' });
}

export default function Email(props: EmailProps) {
  const layout = useLayout();
  const [view, setView] = createSignal<'loading' | 'loaded' | 'empty' | 'error'>('loading');
  const [messages, setMessages] = createSignal<EmailMessage[]>([]);
  const [error, setError] = createSignal<string | null>(null);
  const [sortOrder, setSortOrder] = createSignal<string>(props.sortOrder ?? 'desc');
  const [canScrollDown, setCanScrollDown] = createSignal<boolean>(false);
  const [focusedIdx, setFocusedIdx] = createSignal<number>(-1);

  // [S-09] Relative timestamps ("just now" / Nm / Nh) are derived from the wall
  // clock, so a row rendered once as "just now" would stay "just now" forever
  // without a periodic re-read. Tick a `now` signal once a minute (minute-level
  // granularity) so formatTime / formatDateFull recompute and the list ages.
  // Mirrors the Clock widget's createPolling tick; pauses when the app sleeps.
  const [now, setNow] = createSignal<number>(Date.now());
  createPolling(() => setNow(Date.now()), () => 60_000, 'email-clock');

  let listEl: HTMLDivElement | undefined;
  let isLoading: boolean = false;
  let loadQueued: boolean = false;   // true → re-run load() once after the current run ends
  let knownStatus: Record<string, FolderStatus> = {};
  const [pollIds, setPollIds] = createSignal<string[]>([]);

  const effectiveFilter: Accessor<string[]> = createMemo(() => {
    const ids = props.accountIds;
    const id = props.accountId;
    return ids?.length ? ids : id ? [id] : [];
  });

  const folder = (): string => props.folder ?? 'inbox';
  const messageCount = (): number => props.messageCount ?? 10;
  const viewMode = (): string => props.viewMode ?? 'list';
  const previewLines = (): number => props.previewLines ?? 2;
  const rowColumns = (): string[] => props.rowColumns ?? ['from', 'subject', 'date'];

  // Which optional fields show in list/compact view (from + time are always shown).
  const listFields: Accessor<Set<string>> = createMemo(
    () => new Set(resolveMetrics(props.listFields, LIST_FIELD_DEFS).visible)
  );
  const showField = (key: string): boolean => listFields().has(key);

  const unreadCount: Accessor<number> = createMemo(() => messages().filter((m: EmailMessage) => !m.is_read).length);

  const sortedMessages: Accessor<EmailMessage[]> = createMemo(() =>
    [...messages()].sort((a: EmailMessage, b: EmailMessage) => {
      const diff = new Date(b.received_at).getTime() - new Date(a.received_at).getTime();
      return sortOrder() === 'desc' ? diff : -diff;
    })
  );

  const folderLabel: Accessor<string> = createMemo(() => {
    if (props.label) return props.label;
    const f = folder();
    if (f === 'inbox') return t('email.folderInbox');
    if (f === 'sentitems') return t('email.folderSent');
    if (f === 'drafts') return t('email.folderDrafts');
    if (f === 'deleteditems') return t('email.folderDeleted');
    if (f === 'junkemail') return t('email.folderJunk');
    return f;
  });

  const activeCols: Accessor<string[]> = createMemo(() =>
    (rowColumns()?.length ? rowColumns() : ['from', 'subject', 'date'])
      .filter((k: string) => COL_DEFS[k])
  );

  function checkScroll(): void {
    if (!listEl) return;
    setCanScrollDown(listEl.scrollHeight - listEl.scrollTop - listEl.clientHeight > 4);
  }

  createEffect(() => {
    void sortedMessages().length;
    queueMicrotask(checkScroll);
  });

  async function load(): Promise<void> {
    if (isLoading) {
      // Don't drop the update: a folder / messageCount / account-filter / _refresh
      // change made during an in-flight fetch queues a follow-up that re-reads the
      // current config once this load ends. The 30s status poll (checkForChanges)
      // only reloads on an unread/total delta, so without this the dropped change
      // would never recover and the widget would stay stale. Mirrors FolderView. [S-32]
      loadQueued = true;
      return;
    }
    isLoading = true;
    try {
      do {
        loadQueued = false;
        const filter = effectiveFilter();
        if (!filter.length && !isDemo()) {
          setMessages([]);
          setPollIds([]);
          setView('empty');
          continue;
        }
        if (!messages().length) setView('loading');
        try {
          const result = await invoke('email_get_messages', { accountId: '', folder: folder(), count: messageCount() });
          const filtered = (filter.length && !isDemo()) ? result.filter((m: EmailMessage) => filter.includes(m.account_id)) : result;
          setMessages(filtered);
          setView('loaded');
          const ids = new Set(messages().map(m => m.account_id));
          await initStatus([...ids]);
          setPollIds([...ids]);
        } catch (e) {
          if (String(e) === 'no_accounts') {
            setView('empty');
          } else {
            setError(String(e));
            setView('error');
          }
        }
      } while (loadQueued);
    } finally {
      isLoading = false;
    }
  }

  async function initStatus(ids: string[]): Promise<void> {
    for (const id of ids) {
      try { knownStatus[id] = await invoke('email_get_folder_status', { accountId: id, folder: folder() }); }
      catch (e) { logger.error('email', `email_get_folder_status failed: ${e}`); }
    }
  }

  createPolling(() => { const ids = pollIds(); if (ids.length) checkForChanges(ids); }, () => 30_000, 'email');

  async function checkForChanges(ids: string[]): Promise<void> {
    let changed = false;
    for (const id of ids) {
      try {
        const status = await invoke('email_get_folder_status', { accountId: id, folder: folder() });
        const prev = knownStatus[id];
        if (prev && (prev.unread !== status.unread || prev.total !== status.total)) changed = true;
        knownStatus[id] = status;
      } catch (e) { logger.error('email', `email_get_folder_status failed: ${e}`); }
    }
    if (changed) { knownStatus = {}; await load(); }
  }

  // Sync sortOrder from config prop (context menu can change it)
  createEffect(() => {
    const so = props.sortOrder;
    if (so) setSortOrder(so);
  });

  // Header sort toggle must persist to config — the single source of truth —
  // not just the local signal. Otherwise the next unrelated config change
  // re-spreads props and the sync effect above clobbers the toggle back to the
  // stored value ([S-23]). Mirror the store-merge idiom (scratchpad/tabs/stack):
  // read the widget's current full config and rewrite it so no other key drops.
  function toggleSortOrder(): void {
    const next = sortOrder() === 'desc' ? 'asc' : 'desc';
    setSortOrder(next);
    const id = props._widgetId;
    if (!id) return;
    const w = layout.layout.widgets.find((x) => x.id === id);
    layout.updateWidgetConfig(id, { ...(w?.config ?? {}), sortOrder: next });
  }

  // Reload whenever the account filter, folder, message count, or _refresh token changes.
  createEffect(() => {
    void effectiveFilter();
    void folder();
    void messageCount();
    void props._refresh;
    untrack(() => load());
  });


  // ── Per-message context menu ──────────────────────────────────────────────

  async function markRead(msg: EmailMessage, read: boolean): Promise<void> {
    try {
      await invoke('email_mark_message_read', { messageId: msg.id, isRead: read });
      setMessages((prev: EmailMessage[]) => prev.map((m: EmailMessage) => m.id === msg.id ? { ...m, is_read: read } : m));
    } catch (e) { logger.error('email', `Mark read failed: ${e}`); }
  }

  async function deleteMessage(msg: EmailMessage): Promise<void> {
    try {
      await invoke('email_delete_message', { messageId: msg.id });
      setMessages((prev: EmailMessage[]) => prev.filter((m: EmailMessage) => m.id !== msg.id));
    } catch (e) { logger.error('email', `Delete failed: ${e}`); }
  }

  async function moveMessage(msg: EmailMessage, dest: string): Promise<void> {
    try {
      await invoke('email_move_message', { messageId: msg.id, destinationFolder: dest });
      setMessages((prev: EmailMessage[]) => prev.filter((m: EmailMessage) => m.id !== msg.id));
    } catch (e) { logger.error('email', `Move failed: ${e}`); }
  }

  // ── Keyboard nav ──────────────────────────────────────────────────────────
  // Arrow keys move focus, Enter opens, Delete trashes the focused message.
  // WS_EX_NOACTIVATE windows don't get natural keyboard focus, so we route
  // through the shared Rust low-level hook (see widgetKeyboard.ts).

  function scrollFocusedIntoView(): void {
    queueMicrotask(() => {
      const rows = listEl?.querySelectorAll<HTMLElement>('.msg, .row-msg');
      rows?.[focusedIdx()]?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    });
  }

  function handleKey(e: WidgetKeyEvent): void {
    const tag = document.activeElement?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA') return;
    const msgs = sortedMessages();
    const n = msgs.length;
    if (!n) return;
    const curr = focusedIdx();
    const last = n - 1;
    if (e.key === 'ArrowDown')      { e.preventDefault(); setFocusedIdx(curr < 0 ? 0 : Math.min(last, curr + 1)); scrollFocusedIntoView(); }
    else if (e.key === 'ArrowUp')   { e.preventDefault(); setFocusedIdx(curr < 0 ? last : Math.max(0, curr - 1)); scrollFocusedIntoView(); }
    else if (e.key === 'Home')      { e.preventDefault(); setFocusedIdx(0); scrollFocusedIntoView(); }
    else if (e.key === 'End')       { e.preventDefault(); setFocusedIdx(last); scrollFocusedIntoView(); }
    else if (e.key === 'PageDown')  { e.preventDefault(); setFocusedIdx(curr < 0 ? 0 : Math.min(last, curr + 10)); scrollFocusedIntoView(); }
    else if (e.key === 'PageUp')    { e.preventDefault(); setFocusedIdx(curr < 0 ? 0 : Math.max(0, curr - 10)); scrollFocusedIntoView(); }
    else if (e.key === 'Enter')     {
      if (curr >= 0 && curr < n) {
        const m = msgs[curr];
        e.preventDefault();
        invoke('email_open_message', { webLink: m.web_link, messageId: m.id });
      }
    }
    else if (e.key === 'Delete')    {
      if (curr >= 0 && curr < n) {
        const m = msgs[curr];
        if (!m.account_id.startsWith('google:')) {
          e.preventDefault();
          deleteMessage(m);
          setFocusedIdx(Math.min(last - 1, curr));
        }
      }
    }
    else if (e.key === 'Escape')    { setFocusedIdx(-1); }
  }

  let _kbd: ReturnType<typeof registerWidgetKeyboard> | undefined;

  function handleGlobalMouseDown(e: MouseEvent): void {
    const target = e.target as HTMLElement | null;
    if (target?.closest('.ctx-menu, .ctx-submenu, .ctx-backdrop')) return;
    if (!target?.closest('.msg, .row-msg')) setFocusedIdx(-1);
  }

  let _cleanupBlur: (() => void) | undefined;
  onMount(() => {
    _kbd = registerWidgetKeyboard(handleKey);
    document.addEventListener('mousedown', handleGlobalMouseDown);
    // Clicking outside the Themia app clears the focused message highlight too.
    _cleanupBlur = onAppBlur(() => setFocusedIdx(-1));
  });
  onCleanup(() => {
    _kbd?.dispose();
    document.removeEventListener('mousedown', handleGlobalMouseDown);
    _cleanupBlur?.();
  });

  function activateKbd(e: MouseEvent): void {
    if (e.button === 0) _kbd?.activate();
  }

  // Keep focusedIdx in range when the message list shrinks.
  createEffect(() => {
    const n = sortedMessages().length;
    if (focusedIdx() >= n) setFocusedIdx(n - 1);
  });

  function handleMsgContextMenu(e: MouseEvent, msg: EmailMessage): void {
    // Google email uses the gmail.readonly scope, which disallows mark-read /
    // move / trash. Hide those actions for Google messages so the user doesn't
    // hit a silent API error.
    const readOnly = msg.account_id.startsWith('google:');

    const items = [
      {
        icon: ExternalLink,
        label: t('ctxMenu.open'),
        action: () => invoke('email_open_message', { webLink: msg.web_link, messageId: msg.id }),
      },
      {
        icon: Reply,
        label: t('ctxMenu.reply'),
        action: () => invoke('email_reply_to_message', { messageId: msg.id, fromEmail: msg.from_email, subject: msg.subject }),
      },
      ...(readOnly ? [] : [
        msg.is_read
          ? { icon: MailMinus, label: t('ctxMenu.markUnread'), action: () => markRead(msg, false) }
          : { icon: MailCheck, label: t('ctxMenu.markRead'), action: () => markRead(msg, true) },
        { icon: Archive, label: t('ctxMenu.archive'), action: () => moveMessage(msg, 'archive') },
        { icon: FolderInput, label: t('ctxMenu.junk'), action: () => moveMessage(msg, 'junkemail') },
      ]),
      { icon: Copy, label: t('ctxMenu.copySubject'), action: () => navigator.clipboard.writeText(msg.subject) },
      ...(readOnly ? [] : [
        { sep: true },
        { icon: Trash2, label: t('common.delete'), action: () => deleteMessage(msg), danger: true },
      ]),
    ];
    showCtxMenu(e, items);
  }

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div class="email-widget" onMouseDown={activateKbd}>
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={Mail}
        title={props.label || folderLabel()}
        secondary={
          <Show when={unreadCount() > 0}>
            <span class="badge">{unreadCount()}</span>
          </Show>
        }
        actions={
          <button class="icon-btn" title={sortOrder() === 'desc' ? t('ctxMenu.newestFirst') : t('ctxMenu.oldestFirst')}
            onClick={toggleSortOrder}>
            <Show when={sortOrder() === 'desc'} fallback={<ArrowUpNarrowWide size={13} strokeWidth={ICON_SW} />}>
              <ArrowDownWideNarrow size={13} strokeWidth={ICON_SW} />
            </Show>
          </button>
        }
      />

      <div class="widget-body">
      <Switch>
        <Match when={view() === 'loading' && !messages().length}>
          <div class="state-msg">{t('widgetUi.loading')}</div>
        </Match>
        <Match when={view() === 'empty'}>
          <div class="state-msg" innerHTML={t('email.selectAccount')} />
        </Match>
        <Match when={view() === 'error'}>
          <div class="state-msg error">{error()}</div>
        </Match>
        <Match when={view() === 'loaded'}>

          {/* ══ Row (detail) view ═══════════════════════════════════════════ */}
          <Show when={viewMode() === 'row'} fallback={
            /* ══ List / Compact view ════════════════════════════════════════ */
            <div class="list-wrap">
              <div class="list" ref={el => listEl = el} onScroll={checkScroll}>
                <For each={sortedMessages()} fallback={<div class="state-msg">{t('email.noMessages')}</div>}>
                  {(msg, i) => (
                    <div
                      classList={{ msg: true, unread: !msg.is_read, compact: viewMode() === 'compact', 'kbd-focused': focusedIdx() === i() }}
                      onClick={() => setFocusedIdx(i())}
                      onDblClick={() => invoke('email_open_message', { webLink: msg.web_link, messageId: msg.id })}
                      onContextMenu={(e) => handleMsgContextMenu(e, msg)}
                    >
                      <span classList={{ 'msg-icon': true, unread: !msg.is_read }}>
                        <Show when={msg.is_read} fallback={<Mail size={14} strokeWidth={ICON_SW} />}>
                          <MailOpen size={14} strokeWidth={ICON_SW} />
                        </Show>
                      </span>
                      <div class="msg-body">
                        <div class="msg-top">
                          <span classList={{ from: true, bold: !msg.is_read }}>{msg.from_name || msg.from_email}</span>
                          <span class="time">{formatTime(msg.received_at, now())}</span>
                        </div>
                        <Show when={viewMode() !== 'compact'} fallback={
                          <Show when={showField('subject') || showField('account')}>
                            <div class="compact-row">
                              <Show when={showField('subject')}>
                                <span classList={{ subject: true, bold: !msg.is_read }}>{msg.subject}</span>
                              </Show>
                              <Show when={showField('account')}>
                                <span class="account-tag">
                                  {msg.account_nickname || msg.account_email}
                                  <span class="account-tag-provider">{providerLabel(msg.account_provider)}</span>
                                </span>
                              </Show>
                            </div>
                          </Show>
                        }>
                          <Show when={showField('subject')}>
                            <div classList={{ subject: true, bold: !msg.is_read }}>{msg.subject}</div>
                          </Show>
                          <Show when={showField('preview') || showField('account')}>
                            <div class="preview-row">
                              <Show when={showField('preview')}>
                                <span class="preview" style={`-webkit-line-clamp:${previewLines()}`}>{msg.preview}</span>
                              </Show>
                              <Show when={showField('account')}>
                                <span class="account-tag">
                                  {msg.account_nickname || msg.account_email}
                                  <span class="account-tag-provider">{providerLabel(msg.account_provider)}</span>
                                </span>
                              </Show>
                            </div>
                          </Show>
                        </Show>
                      </div>
                    </div>
                  )}
                </For>
              </div>
              <Show when={canScrollDown()}>
                <div class="scroll-fade" aria-hidden="true"></div>
              </Show>
            </div>
          }>
            <div class="row-view-wrap">
              {/* Column header */}
              <div class="row-header">
                <div class="rh-inner">
                  <For each={activeCols()}>
                    {(col) => <span class="rh-cell" style={COL_DEFS[col].cellStyle}>{COL_DEFS[col].label}</span>}
                  </For>
                </div>
              </div>
              {/* Message rows */}
              <div class="row-list" ref={el => listEl = el} onScroll={checkScroll}>
                <For each={sortedMessages()} fallback={<div class="state-msg">{t('email.noMessages')}</div>}>
                  {(msg, i) => (
                    <div
                      classList={{ 'row-msg': true, unread: !msg.is_read, 'kbd-focused': focusedIdx() === i() }}
                      onClick={() => setFocusedIdx(i())}
                      onDblClick={() => invoke('email_open_message', { webLink: msg.web_link, messageId: msg.id })}
                      onContextMenu={(e) => handleMsgContextMenu(e, msg)}
                    >
                      <span classList={{ 'msg-icon': true, unread: !msg.is_read }}>
                        <Show when={msg.is_read} fallback={<Mail size={13} strokeWidth={ICON_SW} />}>
                          <MailOpen size={13} strokeWidth={ICON_SW} />
                        </Show>
                      </span>
                      <For each={activeCols()}>
                        {(col) => (
                          <Switch>
                            <Match when={col === 'from'}>
                              <span class="rc rc-from" style={COL_DEFS[col].cellStyle}>{msg.from_name || msg.from_email}</span>
                            </Match>
                            <Match when={col === 'subject'}>
                              <span class="rc rc-subject" style={COL_DEFS[col].cellStyle}>{msg.subject}</span>
                            </Match>
                            <Match when={col === 'date'}>
                              <span class="rc rc-date" style={COL_DEFS[col].cellStyle}>{formatDateFull(msg.received_at, now())}</span>
                            </Match>
                            <Match when={col === 'preview'}>
                              <span class="rc rc-preview" style={COL_DEFS[col].cellStyle}>{msg.preview}</span>
                            </Match>
                            <Match when={col === 'account'}>
                              <span class="rc rc-account" style={COL_DEFS[col].cellStyle}>
                                {msg.account_nickname || msg.account_email}
                                <span class="account-tag-provider">{providerLabel(msg.account_provider)}</span>
                              </span>
                            </Match>
                          </Switch>
                        )}
                      </For>
                    </div>
                  )}
                </For>
              </div>
              <Show when={canScrollDown()}>
                <div class="scroll-fade" aria-hidden="true"></div>
              </Show>
            </div>
          </Show>

        </Match>
      </Switch>
      </div>
    </div>
  );
}
