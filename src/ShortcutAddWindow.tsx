// ---------------------------------------------------------------------------
// "Add shortcut" window (mounted from main.tsx on `?shortcutadd=1`)
//
// One list of every kind of shortcut a Shortcut widget can hold: a link, a file,
// a folder, an app layout, and the Windows locations (Recycle Bin, This PC, …)
// with their real shell icons. Items are appended through the shared per-view
// layout store, so the widget window and the config window update live.
// ---------------------------------------------------------------------------

import { createSignal, createMemo, createEffect, onMount, onCleanup, For, Show, type JSX } from 'solid-js';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { listen } from '@tauri-apps/api/event';
import { open as openDialog } from '@tauri-apps/plugin-dialog';
import { Dynamic } from 'solid-js/web';
import { Globe, File as FileIcon, Folder as FolderIcon, LayoutTemplate } from 'lucide-solid';

import { invoke } from './ipc.ts';
import { getViewLayoutStore, updateWidgetConfig } from './layout.ts';
import { globalDesign } from './design.ts';
import { logger } from './debugLog.ts';
import ToggleSwitch from './components/ToggleSwitch.tsx';
import { SHELL_QUICK_SETS } from '../widgets/others/shortcut/quickSets.ts';
import { newId, normalizeUrl, type ShortcutItem } from '../widgets/others/shortcut/items.ts';
import { LAYOUT_PATH, newLeaf } from '../widgets/others/shortcut/layout.ts';
import './App.css';
import './styles/config-base.css';
import './ShortcutAddWindow.css';
import { t } from './i18n.ts';

const DBG = 'shortcut';

function readParams() {
  const p = new URLSearchParams(window.location.search);
  return { widgetId: p.get('widget') ?? '', viewId: p.get('view') ?? '' };
}

function closeWindow(): void {
  getCurrentWindow().close().catch(() => {});
}

export default function ShortcutAddWindow(): JSX.Element {
  // Touch globalDesign so design.ts applies the theme palette to :root.
  void globalDesign.theme;

  const init = readParams();
  const [widgetId, setWidgetId] = createSignal(init.widgetId);
  const [viewId, setViewId] = createSignal(init.viewId);
  const [urlDraft, setUrlDraft] = createSignal('');

  // The store must exist in this window so updateWidgetConfig routes writes to
  // it and our reads stay reactive to cross-window storage events.
  const store = createMemo(() => getViewLayoutStore(viewId()));
  const widget = createMemo(() => store().layout.widgets.find(w => w.id === widgetId()));
  const items = createMemo<ShortcutItem[]>(() => (widget()?.config?.items ?? []) as ShortcutItem[]);

  function appendItems(next: ShortcutItem[]): void {
    const w = widget();
    if (!w || next.length === 0) return;
    updateWidgetConfig(w.id, { ...(w.config ?? {}), items: [...items(), ...next] });
  }

  // ── Real Windows icons for the location rows ──────────────────────────────
  // Same source as the widget's ([S-17]); the lucide glyph is the placeholder
  // until the shell answers.
  const [iconCache, setIconCache] = createSignal<Record<string, string>>({});
  const iconInFlight = new Set<string>();
  createEffect(() => {
    for (const q of SHELL_QUICK_SETS) {
      if (iconCache()[q.path] != null || iconInFlight.has(q.path)) continue;
      iconInFlight.add(q.path);
      invoke('get_file_icon', { path: q.path, size: 32 })
        .then(src => setIconCache(prev => ({ ...prev, [q.path]: src })))
        .catch(e => {
          setIconCache(prev => ({ ...prev, [q.path]: '' }));
          logger.error(DBG, `get_file_icon(${q.path}) failed: ${e}`);
        })
        .finally(() => iconInFlight.delete(q.path));
    }
  });

  // ── Actions ───────────────────────────────────────────────────────────────
  function applyUrl(): void {
    const url = normalizeUrl(urlDraft());
    if (!url) return;
    appendItems([{ id: newId(), path: url }]);
    setUrlDraft('');
  }

  async function pick(directory: boolean): Promise<void> {
    const selected = await openDialog({ directory, multiple: true });
    const paths = selected == null ? [] : (Array.isArray(selected) ? selected : [selected]);
    appendItems(paths.map(p => ({ id: newId(), path: p })));
    // Picking re-raises Explorer's dialog over the desktop; put the widget
    // windows back where they belong.
    invoke('push_windows_to_desktop').catch(() => {});
  }

  function addLayout(): void {
    const w = widget();
    if (!w) return;
    const id = newId();
    appendItems([{ id, path: LAYOUT_PATH, label: t('shortcut.newLayout'), layout: newLeaf() }]);
    invoke('open_layout_window', {
      parentLabel: getCurrentWindow().label,
      widgetId: w.id,
      viewId: viewId(),
      itemId: id,
    }).catch(e => logger.error(DBG, `open_layout_window failed: ${e}`));
    closeWindow();
  }

  /** A Windows location is a switch, not an append-only row: turning it on adds
   *  it, turning it off removes every item pointing at it again. */
  function setLocation(path: string, on: boolean): void {
    const w = widget();
    if (!w) return;
    const has = items().some(x => x.path === path);
    if (on === has) return;
    const next = on
      ? [...items(), { id: newId(), path }]
      : items().filter(x => x.path !== path);
    updateWidgetConfig(w.id, { ...(w.config ?? {}), items: next });
  }

  onMount(() => {
    // Re-invoking open_shortcut_add_window focuses this window and retargets it.
    const unlistenNav = listen<{ widget: string; view: string }>('shortcut-add-navigate', (e) => {
      const p = e.payload;
      if (!p) return;
      setViewId(p.view);
      setWidgetId(p.widget);
    });
    function onKey(e: KeyboardEvent) { if (e.key === 'Escape') closeWindow(); }
    document.addEventListener('keydown', onKey);
    onCleanup(() => {
      unlistenNav.then(fn => fn()).catch(() => {});
      document.removeEventListener('keydown', onKey);
    });
  });

  return (
    <div class="shortcut-add-window">
      {/* No in-page header: the OS title bar IS this window's header (the Rust
          spec builds it decorated and titled "Add shortcut"). A second one only
          repeats the title and the close button. */}
      <div class="sa-body cfg">
        <Show
          when={widget()}
          fallback={<div class="sa-empty">{t('app.widgetNotFound')}</div>}
        >
          <span class="section-label">{t('shortcut.addSectionNew')}</span>
          <div class="sa-list">
            {/* The link field is always open: it is the most common add and
                hiding it behind a disclosure was one click for nothing. */}
            <div class="sa-row sa-row-static">
              <span class="sa-row-ic"><Globe size={20} strokeWidth={1.5} /></span>
              <span class="sa-row-text">
                <span class="sa-row-label">{t('shortcut.chipUrl')}</span>
                <span class="sa-row-desc">{t('shortcut.chipUrlDesc')}</span>
              </span>
            </div>

            <div class="sa-url-form">
              <input
                class="text-input"
                type="text"
                placeholder="https://example.com"
                value={urlDraft()}
                onInput={(e) => setUrlDraft(e.currentTarget.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') { e.preventDefault(); applyUrl(); }
                }}
              />
              <button class="btn-primary" onClick={applyUrl} disabled={!urlDraft().trim()}>
                {t('common.add')}
              </button>
            </div>

            <button class="sa-row" onClick={() => void pick(false)}>
              <span class="sa-row-ic"><FileIcon size={20} strokeWidth={1.5} /></span>
              <span class="sa-row-text">
                <span class="sa-row-label">{t('shortcut.chipFile')}</span>
                <span class="sa-row-desc">{t('shortcut.chipFileDesc')}</span>
              </span>
            </button>

            <button class="sa-row" onClick={() => void pick(true)}>
              <span class="sa-row-ic"><FolderIcon size={20} strokeWidth={1.5} /></span>
              <span class="sa-row-text">
                <span class="sa-row-label">{t('ctxMenu.folder')}</span>
                <span class="sa-row-desc">{t('shortcut.chipFolderDesc')}</span>
              </span>
            </button>

            <button class="sa-row" onClick={addLayout}>
              <span class="sa-row-ic"><LayoutTemplate size={20} strokeWidth={1.5} /></span>
              <span class="sa-row-text">
                <span class="sa-row-label">{t('shortcut.chipLayout')}</span>
                <span class="sa-row-desc">{t('shortcut.chipLayoutDesc')}</span>
              </span>
            </button>
          </div>

          <span class="section-label">{t('shortcut.addSectionLocations')}</span>
          <div class="sa-list">
            <For each={SHELL_QUICK_SETS}>
              {(q) => {
                const already = () => items().some(x => x.path === q.path);
                const src = () => iconCache()[q.path];
                return (
                  <div class="sa-row sa-row-static sa-row-compact" title={q.path}>
                    <span class="sa-row-ic">
                      <Show
                        when={src()}
                        fallback={<Dynamic component={q.icon} size={20} strokeWidth={1.5} />}
                      >
                        <img class="sa-row-img" src={src()} alt="" draggable={false} />
                      </Show>
                    </span>
                    <span class="sa-row-label">{q.label}</span>
                    <ToggleSwitch
                      checked={already()}
                      aria-label={q.label}
                      onChange={(on) => setLocation(q.path, on)}
                    />
                  </div>
                );
              }}
            </For>
          </div>
        </Show>
      </div>
    </div>
  );
}
