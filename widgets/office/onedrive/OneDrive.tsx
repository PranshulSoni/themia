import { createSignal, createEffect, untrack, For } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { Show, Switch, Match } from 'solid-js/web';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { Cloud, Folder, FileText, Image, Film, FileSpreadsheet, File, ChevronLeft } from 'lucide-solid';
import { t, lang } from '../../../src/i18n.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './OneDrive.css';

interface DriveItem {
  id: string;
  name: string;
  size: number;
  last_modified: string;
  web_url: string;
  is_folder: boolean;
  mime_type: string | null;
}

interface OneDriveProps {
  accountId?: string;
  fileCount?: number;
  showPath?: string;
  label?: string;
  headerMode?: HeaderMode;
  _refresh?: number;
}

function timeAgo(iso: string): string {
  if (!iso) return '';
  const diff = Date.now() - new Date(iso).getTime();
  if (diff < 60_000) return t('onedrive.timeJustNow');
  if (diff < 3_600_000) return t('onedrive.timeMinutes', { n: Math.floor(diff / 60_000) });
  if (diff < 86_400_000) return t('onedrive.timeHours', { n: Math.floor(diff / 3_600_000) });
  if (diff < 7 * 86_400_000) return t('onedrive.timeDays', { n: Math.floor(diff / 86_400_000) });
  return new Date(iso).toLocaleDateString(lang(), { month: 'short', day: 'numeric' });
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return bytes + ' B';
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
  if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  return (bytes / (1024 * 1024 * 1024)).toFixed(1) + ' GB';
}

function fileIcon(item: DriveItem) {
  if (item.is_folder) return <Folder size={16} strokeWidth={1.5} />;
  const mime = item.mime_type ?? '';
  const ext = item.name.split('.').pop()?.toLowerCase() ?? '';
  if (mime.startsWith('image/') || ['png', 'jpg', 'jpeg', 'gif', 'svg', 'webp'].includes(ext)) return <Image size={16} strokeWidth={1.5} />;
  if (mime.startsWith('video/') || ['mp4', 'mkv', 'avi', 'mov'].includes(ext)) return <Film size={16} strokeWidth={1.5} />;
  if (['xlsx', 'xls', 'csv'].includes(ext)) return <FileSpreadsheet size={16} strokeWidth={1.5} />;
  if (['doc', 'docx', 'pdf', 'txt', 'md'].includes(ext)) return <FileText size={16} strokeWidth={1.5} />;
  return <File size={16} strokeWidth={1.5} />;
}

export default function OneDrive(props: OneDriveProps) {
  const [view, setView] = createSignal<'empty' | 'loading' | 'error' | 'loaded'>('empty');
  const [items, setItems] = createSignal<DriveItem[]>([]);
  const [error, setError] = createSignal('');
  const [folderStack, setFolderStack] = createSignal<{ id: string; name: string }[]>([]);

  const count = () => props.fileCount ?? 15;
  const showPath = () => props.showPath ?? 'recent';

  // Bump the request id up front so any in-flight fetch (a superseded
  // navigation or a late background poll) becomes stale the moment a fresh
  // load starts, and can't overwrite the current folder's view (#110).
  let loadSeq = 0;
  async function load(folderId?: string) {
    const acct = props.accountId || (isDemo() ? 'demo-account' : '');
    if (!acct) { setView('empty'); return; }
    const my = ++loadSeq;
    setView('loading');
    try {
      let result: DriveItem[];
      if (folderId) {
        result = await invoke('onedrive_list_folder', { accountId: acct, folderId, count: count() });
      } else if (showPath() === 'root') {
        result = await invoke('onedrive_list_folder', { accountId: acct, folderId: 'root', count: count() });
      } else {
        result = await invoke('onedrive_recent_files', { accountId: acct, count: count() });
      }
      if (my !== loadSeq) return;
      setItems(result);
      setView('loaded');
    } catch (e: unknown) {
      if (my !== loadSeq) return;
      setError(String(e).replace(/^Error:\s*/, ''));
      setView('error');
    }
  }

  createEffect(() => {
    void props.accountId;
    void props._refresh;
    void props.showPath;
    void props.fileCount;
    setFolderStack([]);
    untrack(() => load());
  });

  createPolling(() => {
    const stack = folderStack();
    load(stack.length ? stack[stack.length - 1].id : undefined);
  }, () => 5 * 60_000, 'onedrive');

  function openItem(item: DriveItem) {
    if (item.is_folder) {
      setFolderStack(s => [...s, { id: item.id, name: item.name }]);
      setItems([]);
      load(item.id);
    } else {
      invoke('open_url', { url: item.web_url });
    }
  }

  function goBack() {
    const stack = folderStack();
    if (stack.length <= 1) {
      setFolderStack([]);
      setItems([]);
      load();
    } else {
      const newStack = stack.slice(0, -1);
      setFolderStack(newStack);
      setItems([]);
      load(newStack[newStack.length - 1].id);
    }
  }

  return (
    <div class="onedrive-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={Cloud}
        title={props.label || t('widget.onedrive.label')}
      />
      <div class="widget-body">
      <Switch>
        <Match when={view() === 'empty'}>
          <div class="onedrive-widget__msg">
            <div class="onedrive-widget__empty">
              <Cloud size={24} strokeWidth={1.2} />
              <span innerHTML={t('onedrive.selectAccount')} />
            </div>
          </div>
        </Match>
        <Match when={view() === 'loading' && !items().length}>
          <div class="onedrive-widget__msg">{t('widgetUi.loading')}</div>
        </Match>
        <Match when={view() === 'error'}>
          <div class="onedrive-widget__msg onedrive-widget__msg--error">{error()}</div>
        </Match>
        <Match when={items().length > 0 || folderStack().length > 0}>
          <Show when={folderStack().length > 0}>
            <div class="onedrive-widget__breadcrumb" onClick={goBack}>
              <ChevronLeft size={14} strokeWidth={1.5} />
              <span>{folderStack()[folderStack().length - 1]?.name ?? 'Back'}</span>
            </div>
          </Show>
          <div class="onedrive-widget__list">
            <For each={items()}>
              {(item) => (
                <div class="onedrive-widget__item" onClick={() => openItem(item)}>
                  <div class="onedrive-widget__item-icon">{fileIcon(item)}</div>
                  <div class="onedrive-widget__item-info">
                    <span class="onedrive-widget__item-name">{item.name}</span>
                    <span class="onedrive-widget__item-meta">
                      {!item.is_folder && formatSize(item.size)}
                      {!item.is_folder && item.last_modified && ' \u00b7 '}
                      {item.last_modified && timeAgo(item.last_modified)}
                    </span>
                  </div>
                </div>
              )}
            </For>
          </div>
          <div class="onedrive-widget__fade" />
        </Match>
        <Match when={view() === 'loaded'}>
          <div class="onedrive-widget__msg">
            <div class="onedrive-widget__empty">
              <Cloud size={24} strokeWidth={1.2} />
              <span>{t('onedrive.noFiles')}</span>
            </div>
          </div>
        </Match>
      </Switch>
      </div>
    </div>
  );
}
