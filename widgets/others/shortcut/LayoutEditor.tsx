// Visual tree editor for a Shortcut `layout` item. Edits a single app-layout
// tree: start from one pane, split horizontally/vertically, resize dividers,
// and assign each leaf a source. Assignment happens IN-PANE: each pane carries
// its own source picker (App / URL / File / Folder) centred inside it, shows the
// chosen app/file icon, and applies on selection: no side panel, no "change"
// button, no picker window. Used inside the detached layout-editor window.
import { createSignal, createMemo, createEffect, onMount, Show } from 'solid-js';
import type { JSX } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import {
  SplitSquareHorizontal, SplitSquareVertical, X,
  Rocket, Link as LinkIcon, FileCode, FolderOpen, FolderSearch,
} from 'lucide-solid';
import { open as openDialog } from '@tauri-apps/plugin-dialog';
import { invoke } from '../../../src/demo/invoke.ts';
import type { MonitorDescriptor } from '../../../src/ipc.ts';
import { logger } from '../../../src/debugLog.ts';
import Dropdown from '../../../src/components/Dropdown.tsx';
import {
  MIN_RATIO, MAX_RATIO,
  updateNode, splitLeaf, removeLeafFromTree, isRootSplit, validateNode,
  type Assignment, type LayoutNode, type LeafNode,
} from './layout.ts';
import './LayoutEditor.css';
import { t } from '../../../src/i18n.ts';

interface AppEntry { name: string; path: string; category: string; }

type Kind = Assignment['kind'];

interface LayoutEditorProps {
  node: LayoutNode;
  onChange: (node: LayoutNode) => void;
}

function baseName(p: string): string {
  return p.replace(/\\/g, '/').split('/').filter(Boolean).pop() || p;
}
function faviconUrl(url: string): string | null {
  try {
    const origin = new URL(url).origin;
    return `https://t3.gstatic.com/faviconV2?client=SOCIAL&type=FAVICON`
      + `&fallback_opts=TYPE,SIZE,URL&url=${encodeURIComponent(origin)}&size=64`;
  } catch { return null; }
}

const KIND_OPTIONS = [
  { value: 'lnk',      label: t('shortcut.kindApp'),    icon: () => <Rocket size={13} strokeWidth={1.5} /> },
  { value: 'url',      label: 'URL',    icon: () => <LinkIcon size={13} strokeWidth={1.5} /> },
  { value: 'path',     label: t('shortcut.chipFile'),   icon: () => <FileCode size={13} strokeWidth={1.5} /> },
  { value: 'explorer', label: t('shortcut.kindFolder'), icon: () => <FolderOpen size={13} strokeWidth={1.5} /> },
];

export default function LayoutEditor(props: LayoutEditorProps) {
  const [monitors, setMonitors] = createSignal<MonitorDescriptor[]>([]);
  const [apps, setApps] = createSignal<AppEntry[]>([]);
  const appOptions = createMemo(() => apps().map(a => ({ value: a.path, label: a.name })));

  // Per-leaf draft of the chosen source kind, used while a pane is still
  // unassigned (so the right input shows before a target exists).
  const [draftKind, setDraftKind] = createSignal<Record<string, Kind>>({});

  // Windows shell icon cache for file/app/folder targets (data URLs), keyed by
  // target path. URL targets use a favicon <img> directly instead.
  const [iconCache, setIconCache] = createSignal<Record<string, string>>({});
  const iconInFlight = new Set<string>();

  onMount(async () => {
    // `get_monitor_work_areas` is gone: it was a strict subset of
    // `get_active_monitors`, which also carries identity + the primary flag.
    try { setMonitors((await invoke('get_active_monitors')) ?? []); } catch (e) {
      // Best-effort (the canvas just falls back to 16:9), but never silent: a
      // swallowed failure here is exactly how the removed command went
      // unnoticed at this call site.
      logger.error('displays', `get_active_monitors failed: ${e}; layout canvas falls back to a 16:9 aspect`);
    }
    try { setApps((await invoke('list_start_menu_apps')) ?? []); } catch {
      // ignore: app list is best-effort; editor still works with no apps
    }
  });

  // Always read the tree through the validating memo so partially-saved or
  // older-shape data is healed transparently.
  const root = createMemo<LayoutNode>(() => validateNode(props.node));

  // Lazily fetch shell icons for every assigned file/app/folder target.
  createEffect(() => {
    const targets: string[] = [];
    (function walk(n: LayoutNode) {
      if (n.kind === 'leaf') {
        const a = n.assignment;
        if (a && a.target && a.kind !== 'url') targets.push(a.target);
      } else { walk(n.a); walk(n.b); }
    })(root());
    for (const t of targets) {
      if (iconCache()[t] != null || iconInFlight.has(t)) continue;
      iconInFlight.add(t);
      invoke('get_file_icon', { path: t, size: 48 })
        .then(src => setIconCache(prev => ({ ...prev, [t]: src })))
        .catch(() => setIconCache(prev => ({ ...prev, [t]: '' })))
        .finally(() => iconInFlight.delete(t));
    }
  });

  function mutate(fn: (root: LayoutNode) => LayoutNode) {
    props.onChange(fn(root()));
  }

  // ── Tree actions ───────────────────────────────────────────────────────
  function onSplit(leafId: string, direction: 'vertical' | 'horizontal') {
    mutate(r => splitLeaf(r, leafId, direction));
  }
  function onRemoveLeaf(leafId: string) {
    if (!isRootSplit(root())) return;
    mutate(r => removeLeafFromTree(r, leafId));
  }

  // ── Assignment ───────────────────────────────────────────────────────────
  function setAssignment(leafId: string, assignment: Assignment | undefined) {
    mutate(r => updateNode(r, leafId, (n) => {
      if (n.kind !== 'leaf') return n;
      return assignment ? { ...n, assignment } : { kind: 'leaf', id: n.id };
    }));
  }

  function effectiveKind(leaf: LeafNode): Kind {
    return leaf.assignment?.kind ?? draftKind()[leaf.id] ?? 'lnk';
  }

  function changeKind(leaf: LeafNode, kind: Kind) {
    setDraftKind(prev => ({ ...prev, [leaf.id]: kind }));
    // Switching source clears any existing assignment; the new kind needs its
    // own target before the pane is "assigned" again.
    if (leaf.assignment) setAssignment(leaf.id, undefined);
  }

  function assignApp(leaf: LeafNode, path: string) {
    if (!path) { setAssignment(leaf.id, undefined); return; }
    const found = apps().find(a => a.path === path);
    const apply = (matchHint?: string) => setAssignment(leaf.id, {
      kind: 'lnk', target: path, label: found?.name ?? baseName(path),
      matchHint, reuseIfRunning: true,
    });
    invoke('resolve_shortcut', { path })
      .then(t => apply(baseName(t.target_path)))
      .catch(() => apply(undefined));
  }

  function assignText(leaf: LeafNode, kind: Kind, raw: string) {
    const target = raw.trim();
    if (!target) { setAssignment(leaf.id, undefined); return; }
    if (kind === 'url') {
      const url = /^https?:\/\//i.test(target) ? target : `https://${target}`;
      setAssignment(leaf.id, { kind: 'url', target: url, label: url, reuseIfRunning: false });
    } else if (kind === 'explorer') {
      setAssignment(leaf.id, { kind: 'explorer', target, label: baseName(target), reuseIfRunning: false });
    } else {
      setAssignment(leaf.id, { kind: 'path', target, label: baseName(target), matchHint: baseName(target), reuseIfRunning: true });
    }
  }

  async function browse(leaf: LeafNode, kind: Kind) {
    try {
      const selected = await openDialog({ directory: kind === 'explorer', multiple: false });
      if (typeof selected === 'string') assignText(leaf, kind, selected);
    } catch { /* dialog cancelled / unavailable */ }
  }

  // ── Canvas aspect (best-effort: primary monitor work area) ───────────────
  // `get_active_monitors` reports MONITORINFOF_PRIMARY, so this now really is
  // the primary's aspect; it used to take whichever monitor enumerated first.
  const aspect = createMemo(() => {
    const mons = monitors();
    if (!mons.length) return 16 / 9;
    const m = mons.find(x => x.primary) ?? mons[0];
    if (!m || m.height <= 0) return 16 / 9;
    return m.width / m.height;
  });

  // ── Divider drag ─────────────────────────────────────────────────────────
  function onSetRatio(splitId: string, ratio: number) {
    const clamped = Math.max(MIN_RATIO, Math.min(MAX_RATIO, ratio));
    mutate(r => updateNode(r, splitId, n => n.kind === 'split' ? { ...n, ratio: clamped } : n));
  }
  function startDrag(e: PointerEvent, splitId: string, direction: 'vertical' | 'horizontal', containerEl: HTMLElement) {
    e.preventDefault();
    e.stopPropagation();
    const rect = containerEl.getBoundingClientRect();
    function move(ev: PointerEvent) {
      const ratio = direction === 'vertical'
        ? (ev.clientX - rect.left) / rect.width
        : (ev.clientY - rect.top) / rect.height;
      onSetRatio(splitId, ratio);
    }
    function up() {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
    }
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  }

  // ── Pane icon ──────────────────────────────────────────────────────────────
  function iconFor(a: Assignment) {
    if (a.kind === 'url') {
      const fav = faviconUrl(a.target);
      return fav
        ? <img class="leaf-assign-icon-img" src={fav} alt="" draggable={false} />
        : <LinkIcon size={22} strokeWidth={1.5} />;
    }
    const src = iconCache()[a.target];
    if (src) return <img class="leaf-assign-icon-img" src={src} alt="" draggable={false} />;
    if (a.kind === 'explorer') return <FolderOpen size={22} strokeWidth={1.5} />;
    if (a.kind === 'path') return <FileCode size={22} strokeWidth={1.5} />;
    return <Rocket size={22} strokeWidth={1.5} />;
  }

  // ── Pane renderer (recursive) ────────────────────────────────────────────
  function renderPane(node: LayoutNode | null | undefined): JSX.Element {
    if (!node || typeof node !== 'object') return null;
    if (node.kind === 'leaf') {
      const leaf = node;
      const leafId = node.id;
      const a = () => leaf.assignment;
      const kind = () => effectiveKind(leaf);
      const canRemove = () => isRootSplit(root());
      const stop = (e: Event) => e.stopPropagation();
      return (
        <div class="applayouts-leaf" classList={{ assigned: !!a() }}>
          <div class="applayouts-leaf-toolbar" onPointerDown={stop} onClick={stop}>
            <button class="applayouts-leaf-btn" title={t('layoutEditor.splitHorizontal')} onClick={() => onSplit(leafId, 'horizontal')}>
              <SplitSquareVertical size={12} strokeWidth={1.5} />
            </button>
            <button class="applayouts-leaf-btn" title={t('layoutEditor.splitVertical')} onClick={() => onSplit(leafId, 'vertical')}>
              <SplitSquareHorizontal size={12} strokeWidth={1.5} />
            </button>
            <Show when={canRemove()}>
              <button class="applayouts-leaf-btn danger" title={t('layoutEditor.removePane')} onClick={() => onRemoveLeaf(leafId)}>
                <X size={12} strokeWidth={1.5} />
              </button>
            </Show>
          </div>

          <div class="leaf-assign" onPointerDown={stop} onClick={stop}>
            <div class="leaf-assign-preview">
              <Show
                when={a()}
                fallback={<span class="leaf-assign-icon-ph"><Dynamic component={KIND_OPTIONS.find(k => k.value === kind())!.icon} /></span>}
              >
                <span class="leaf-assign-icon">{iconFor(a()!)}</span>
                <span class="leaf-assign-name" title={a()!.target}>{a()!.label || a()!.target}</span>
              </Show>
            </div>

            <div class="leaf-assign-controls">
              <Dropdown
                class="leaf-assign-source"
                value={kind()}
                options={KIND_OPTIONS}
                onChange={(v) => changeKind(leaf, v as Kind)}
              />

              <Show when={kind() === 'lnk'}>
                <Dropdown
                  value={a()?.kind === 'lnk' ? a()!.target : ''}
                  options={appOptions()}
                  onChange={(v) => assignApp(leaf, v)}
                  searchable
                  searchPlaceholder={t('layoutEditor.searchApps')}
                />
              </Show>

              <Show when={kind() === 'url'}>
                <input
                  class="text-input leaf-assign-input"
                  type="text"
                  placeholder="https://example.com"
                  value={a()?.kind === 'url' ? a()!.target : ''}
                  onChange={(e) => assignText(leaf, 'url', e.currentTarget.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter') e.currentTarget.blur(); }}
                />
              </Show>

              <Show when={kind() === 'path' || kind() === 'explorer'}>
                <div class="leaf-assign-path">
                  <input
                    class="text-input leaf-assign-input"
                    type="text"
                    spellcheck={false}
                    placeholder={kind() === 'explorer' ? 'C:\\Folder' : 'C:\\Path\\To\\App.exe'}
                    value={(a()?.kind === 'path' || a()?.kind === 'explorer') ? a()!.target : ''}
                    onChange={(e) => assignText(leaf, kind(), e.currentTarget.value)}
                    onKeyDown={(e) => { if (e.key === 'Enter') e.currentTarget.blur(); }}
                  />
                  <button
                    class="leaf-assign-browse"
                    title={kind() === 'explorer' ? 'Choose a folder' : 'Choose a file'}
                    onClick={() => browse(leaf, kind())}
                  ><FolderSearch size={14} strokeWidth={1.5} /></button>
                </div>
              </Show>

              <Show when={a()}>
                <button class="leaf-assign-clear" onClick={() => setAssignment(leafId, undefined)}>
                  <X size={11} strokeWidth={1.5} /> Clear
                </button>
              </Show>
            </div>
          </div>
        </div>
      );
    }
    // split
    let containerEl: HTMLDivElement | undefined;
    const dirClass = node.direction === 'vertical' ? 'split-row' : 'split-col';
    const ratioPct = `${node.ratio * 100}%`;
    return (
      <div class={`applayouts-split ${dirClass}`} ref={containerEl}>
        <div class="applayouts-split-pane" style={{ flex: `0 0 ${ratioPct}` }}>
          {renderPane(node.a)}
        </div>
        <div
          class={`applayouts-divider ${dirClass}`}
          onPointerDown={(e) => containerEl && startDrag(e, node.id, node.direction, containerEl)}
          title={t('layoutEditor.dragResize')}
        />
        <div class="applayouts-split-pane" style={{ flex: '1 1 0' }}>
          {renderPane(node.b)}
        </div>
      </div>
    );
  }

  return (
    <div class="applayouts-editor">
      <div class="applayouts-canvas" style={{ 'aspect-ratio': `${aspect()}` }}>
        {renderPane(root())}
      </div>
    </div>
  );
}
