import { createSignal, createMemo, createEffect, untrack } from 'solid-js';
import { Show, For, Switch, Match } from 'solid-js';
import { Rocket, FolderOpen, Copy, EyeOff, Search, X } from 'lucide-solid';
import { invoke } from '../../../src/demo/invoke.ts';
import { showCtxMenu } from '../../../src/ctxMenuStore.ts';
import { logger } from '../../../src/debugLog.ts';
import { useLayout } from '../../../src/viewContext.tsx';
import { t } from '../../../src/i18n.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './Apps.css';

export interface AppsProps {
  headerMode?: HeaderMode;
  viewMode?: string;
  label?: string;
  hiddenCategories?: string[];
  hiddenApps?: string[];
  _widgetId?: string;
  _refresh?: number;
}

interface AppEntry {
  name: string;
  path: string;
  category: string;
}

export default function Apps(props: AppsProps) {
  const layout = useLayout();
  const [apps, setApps] = createSignal<AppEntry[]>([]);
  const [search, setSearch] = createSignal('');
  const [icons, setIcons] = createSignal<Record<string, string>>({});
  const [loading, setLoading] = createSignal(true);

  let iconQueue: string[] = [];
  let iconWorking = false;

  const filtered = createMemo(() => {
    const hCats = new Set(props.hiddenCategories ?? []);
    const hApps = new Set(props.hiddenApps ?? []);
    const base = apps().filter(a => !hCats.has(a.category || 'Other') && !hApps.has(a.path));
    const q = search().trim().toLowerCase();
    return q
      ? base.filter(a => a.name.toLowerCase().includes(q) || a.category.toLowerCase().includes(q))
      : base;
  });

  const grouped = createMemo(() => {
    if (props.viewMode === 'compact') return [];
    const map = new Map<string, AppEntry[]>();
    for (const a of filtered()) {
      const cat = a.category || 'Other';
      if (!map.has(cat)) map.set(cat, []);
      map.get(cat)!.push(a);
    }
    return [...map.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  });

  // -- Icon loading --
  async function drainIconQueue() {
    if (iconWorking) return;
    iconWorking = true;
    while (iconQueue.length) {
      const batch = iconQueue.splice(0, 6);
      await Promise.all(batch.map(async (path) => {
        try {
          const data = await invoke('get_file_icon', { path, size: 48 });
          setIcons(prev => ({ ...prev, [path]: data }));
        } catch {
          // ignore: missing icon is non-fatal, fall back to initials
        }
      }));
    }
    iconWorking = false;
  }

  function enqueueIcons(list: AppEntry[]) {
    const cur = icons();
    for (const a of list) {
      if (!cur[a.path] && !iconQueue.includes(a.path)) iconQueue.push(a.path);
    }
    drainIconQueue();
  }

  // -- Load --
  async function loadApps() {
    setLoading(true);
    try {
      const result = await invoke('list_start_menu_apps');
      setApps(result);
      enqueueIcons(result);
    } catch (e) { logger.error('apps', `load failed: ${e}`); }
    setLoading(false);
  }

  // Load on mount + whenever the manual `_refresh` nonce bumps (context-menu Refresh).
  createEffect(() => {
    void props._refresh;
    untrack(loadApps);
  });

  // -- Actions --
  function launch(app: AppEntry) {
    invoke('open_file', { path: app.path }).catch(() => {});
  }

  function openLocation(app: AppEntry) {
    const dir = app.path.replace(/[^\\/]+[\\/]?$/, '');
    invoke('open_in_explorer', { path: dir }).catch(() => {});
  }

  function hideApp(app: AppEntry) {
    const id = props._widgetId;
    if (!id) return;
    const current = props.hiddenApps ?? [];
    if (current.includes(app.path)) return;
    const w = layout.layout.widgets.find((w) => w.id === id);
    layout.updateWidgetConfig(id, { ...(w?.config ?? {}), hiddenApps: [...current, app.path] });
  }

  function onCtxMenu(e: MouseEvent, app: AppEntry) {
    e.preventDefault();
    showCtxMenu(e, [
      { icon: Rocket,     label: t('apps.launch'),        action: () => launch(app) },
      { icon: FolderOpen, label: t('apps.openLocation'),  action: () => openLocation(app) },
      { icon: Copy,       label: t('apps.copyPath'),      action: () => navigator.clipboard.writeText(app.path).catch(() => {}) },
      { sep: true },
      { icon: EyeOff,     label: t('apps.hideFromList'), action: () => hideApp(app) },
    ]);
  }

  function initials(name: string): string {
    return name.split(/\s+/).filter(Boolean).slice(0, 2).map(w => w[0].toUpperCase()).join('') || '?';
  }

  function hue(name: string): number {
    let h = 0;
    for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) & 0xffffffff;
    return Math.abs(h) % 360;
  }

  return (
    <div class="apps-wrap">

      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={Search}
        title={props.label || 'Apps'}
        titleOverride={
          <div class="search-bar">
            <input
              class="search-input"
              type="text"
              placeholder={t('apps.searchPlaceholder')}
              value={search()}
              onInput={(e) => setSearch(e.currentTarget.value)}
            />
            <Show when={search()}>
              <button class="search-clear" onClick={() => setSearch('')}><X size={13} strokeWidth={1.5} /></button>
            </Show>
          </div>
        }
      />

      <div class="widget-body apps-body">
      <Switch>
        <Match when={loading()}>
          <div class="empty">{t('apps.loadingApps')}</div>
        </Match>

        <Match when={filtered().length === 0}>
          <div class="empty">{t('apps.noApps')}</div>
        </Match>

        <Match when={props.viewMode === 'compact'}>
          <div class="compact-list">
            <For each={filtered()}>
              {(app) => (
                <div
                  class="compact-item"
                  onClick={() => launch(app)}
                  onContextMenu={(e) => onCtxMenu(e, app)}
                  title={app.name}
                >
                  {app.name}
                </div>
              )}
            </For>
          </div>
        </Match>

        <Match when={props.viewMode === 'list'}>
          <div class="scroll-area">
            <For each={grouped()}>
              {([cat, items]) => (
                <>
                  <Show when={cat !== 'Other' || grouped().length > 1}>
                    <div class="cat-label">{cat}</div>
                  </Show>
                  <For each={items}>
                    {(app) => (
                      <div
                        class="list-item"
                        onClick={() => launch(app)}
                        onContextMenu={(e) => onCtxMenu(e, app)}
                      >
                        <div class="av av-sm" style={{ '--h': `${hue(app.name)}deg` }}>
                          <Show when={icons()[app.path]} fallback={initials(app.name)}>
                            <img src={icons()[app.path]} alt="" draggable={false} />
                          </Show>
                        </div>
                        <span class="list-name">{app.name}</span>
                      </div>
                    )}
                  </For>
                </>
              )}
            </For>
          </div>
        </Match>

        <Match when={true}>
          {/* Grid view */}
          <div class="scroll-area">
            <For each={grouped()}>
              {([cat, items]) => (
                <>
                  <Show when={cat !== 'Other' || grouped().length > 1}>
                    <div class="cat-label">{cat}</div>
                  </Show>
                  <div class="grid-row">
                    <For each={items}>
                      {(app) => (
                        <div
                          class="grid-item"
                          onClick={() => launch(app)}
                          onContextMenu={(e) => onCtxMenu(e, app)}
                          title={app.name}
                        >
                          <div class="av av-lg" style={{ '--h': `${hue(app.name)}deg` }}>
                            <Show when={icons()[app.path]} fallback={initials(app.name)}>
                              <img src={icons()[app.path]} alt="" draggable={false} />
                            </Show>
                          </div>
                          <span class="grid-name">{app.name}</span>
                        </div>
                      )}
                    </For>
                  </div>
                </>
              )}
            </For>
          </div>
        </Match>
      </Switch>
      </div>

    </div>
  );
}
