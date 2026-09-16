// ---------------------------------------------------------------------------
// "Assign app" picker window (mounted from main.tsx on `?layoutpicker=1`)
//
// A focused window for assigning one source (a Start Menu app, a URL, or a
// custom path) to a single layout pane. On confirm it broadcasts the choice
// back to the layout editor window via the `layout-assign` transient message
// (keyed by the pane's `leaf` id), then closes.
// ---------------------------------------------------------------------------

import { createSignal, createMemo, onMount, onCleanup, Show, type JSX } from 'solid-js';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { invoke } from './ipc.ts';
import { listen } from '@tauri-apps/api/event';
import { X, Rocket, Link as LinkIcon, FileCode } from 'lucide-solid';
import { t } from './i18n.ts';

import Dropdown from './components/Dropdown.tsx';
import ToggleSwitch from './components/ToggleSwitch.tsx';
import { postTransient } from './sync.ts';
import { globalDesign } from './design.ts';
import type { Assignment } from '../widgets/others/shortcut/layout.ts';
import './App.css';
import './styles/config-base.css';
import './LayoutPickerWindow.css';

interface AppEntry { name: string; path: string; category: string; }

function baseName(p: string): string {
  return p.replace(/\\/g, '/').split('/').pop() || p;
}
function closeWindow(): void {
  getCurrentWindow().close().catch(() => {});
}

export default function LayoutPickerWindow(): JSX.Element {
  void globalDesign.theme;

  const [leafId, setLeafId] = createSignal(new URLSearchParams(window.location.search).get('leaf') ?? '');
  const [kind, setKind] = createSignal<Assignment['kind']>('lnk');
  const [target, setTarget] = createSignal('');
  const [label, setLabel] = createSignal<string | undefined>(undefined);
  const [matchHint, setMatchHint] = createSignal<string | undefined>(undefined);
  const [args, setArgs] = createSignal('');
  const [reuse, setReuse] = createSignal(true);
  const [apps, setApps] = createSignal<AppEntry[]>([]);

  const appOptions = createMemo(() => apps().map(a => ({ value: a.path, label: a.name })));
  const canAssign = createMemo(() => target().trim().length > 0);

  onMount(() => {
    invoke('list_start_menu_apps').then(a => setApps(a ?? [])).catch(() => {});

    // Re-invoking the picker retargets it to a different pane.
    const unlistenNav = listen<{ leaf: string }>('layout-picker-navigate', (e) => {
      if (e.payload?.leaf) {
        setLeafId(e.payload.leaf);
        resetForm();
      }
    });
    onCleanup(() => { unlistenNav.then(fn => fn()).catch(() => {}); });
  });

  function resetForm() {
    setKind('lnk'); setTarget(''); setLabel(undefined); setMatchHint(undefined);
    setArgs(''); setReuse(true);
  }

  function changeKind(k: Assignment['kind']) {
    setKind(k); setTarget(''); setLabel(undefined); setMatchHint(undefined);
  }

  async function pickApp(path: string) {
    setTarget(path);
    const found = apps().find(a => a.path === path);
    setLabel(found?.name);
    try {
      const t = await invoke('resolve_shortcut', { path });
      setMatchHint(baseName(t.target_path));
    } catch { setMatchHint(undefined); }
  }
  function setPath(p: string) {
    setTarget(p);
    setLabel(baseName(p));
    setMatchHint(baseName(p));
  }
  function setUrl(u: string) {
    setTarget(u);
    setLabel(u);
    setMatchHint(undefined);
  }

  function assign() {
    if (!canAssign()) return;
    const assignment: Assignment = {
      kind: kind(),
      target: target().trim(),
      label: label(),
      args: kind() === 'path' && args().trim() ? args().trim() : undefined,
      matchHint: reuse() ? matchHint() : undefined,
      reuseIfRunning: reuse(),
    };
    postTransient({ type: 'layout-assign', leafId: leafId(), assignment: assignment as unknown as Record<string, unknown> });
    closeWindow();
  }

  return (
    <div class="layout-picker-window">
      <header class="layout-picker-head">
        <span class="layout-picker-title">{t('layoutPicker.title')}</span>
        <button class="layout-picker-close" title={t('common.close')} onClick={closeWindow}>
          <X size={15} strokeWidth={1.5} />
        </button>
      </header>

      <div class="layout-picker-body cfg">
        <div class="field-col">
          <span class="design-label">{t('layoutPicker.sourceLabel')}</span>
          <Dropdown
            value={kind()}
            options={[
              { value: 'lnk',  label: t('layoutPicker.kindLnk'),  icon: () => <Rocket size={13} strokeWidth={1.5} /> },
              { value: 'url',  label: 'URL',                      icon: () => <LinkIcon size={13} strokeWidth={1.5} /> },
              { value: 'path', label: t('layoutPicker.kindPath'), icon: () => <FileCode size={13} strokeWidth={1.5} /> },
            ]}
            onChange={(v) => changeKind(v as Assignment['kind'])}
          />
        </div>

        <Show when={kind() === 'lnk'}>
          <div class="field-col">
            <span class="design-label">{t('layoutPicker.appLabel')}</span>
            <Dropdown
              value={target()}
              options={appOptions()}
              onChange={(v) => pickApp(v)}
              searchable
              searchPlaceholder={t('layoutPicker.searchApps')}
            />
          </div>
        </Show>
        <Show when={kind() === 'url'}>
          <div class="field-col">
            <span class="design-label">URL</span>
            <input class="text-input" type="text" placeholder="https://example.com"
              value={target()} onInput={(e) => setUrl(e.currentTarget.value)} />
          </div>
        </Show>
        <Show when={kind() === 'path'}>
          <div class="field-col">
            <span class="design-label">{t('layoutPicker.pathLabel')}</span>
            <input class="text-input" type="text" placeholder={t('layoutPicker.pathPlaceholder')}
              value={target()} onInput={(e) => setPath(e.currentTarget.value)} />
          </div>
          <div class="field-col">
            <span class="design-label">{t('layoutPicker.argumentsLabel')}</span>
            <input class="text-input" type="text" placeholder={t('layoutPicker.argsPlaceholder')}
              value={args()} onInput={(e) => setArgs(e.currentTarget.value)} />
          </div>
        </Show>

        <div class="layout-picker-row">
          <span class="design-label" title={t('layoutPicker.reuseTitle')}>
            {t('layoutPicker.reuseLabel')}
          </span>
          <ToggleSwitch checked={reuse()} onLabel={t('toggle.yes')} offLabel={t('toggle.no')} onChange={setReuse} />
        </div>
        <Show when={reuse()}>
          <div class="field-col">
            <span class="design-label" title={t('layoutPicker.matchExeTitle')}>
              {t('layoutPicker.matchExeLabel')}
            </span>
            <input class="text-input" type="text" placeholder={t('layoutPicker.matchExePlaceholder')}
              value={matchHint() ?? ''} onInput={(e) => setMatchHint(e.currentTarget.value || undefined)} />
          </div>
        </Show>

        <div class="layout-picker-actions">
          <button class="btn-secondary" onClick={closeWindow}>{t('common.cancel')}</button>
          <button class="btn-primary" onClick={assign} disabled={!canAssign()}>{t('layoutPicker.assign')}</button>
        </div>
      </div>
    </div>
  );
}
