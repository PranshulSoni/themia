import { createSignal, createMemo, For, Show } from 'solid-js';
import { Timer as TimerIcon, Trash2, Settings, Check } from 'lucide-solid';
import { createPolling } from '../../../src/createPolling';
import { showCtxMenu, updateCtxMenuItems, type CtxMenuItem } from '../../../src/ctxMenuStore.ts';
import { useLayout } from '../../../src/viewContext.tsx';
import {
  formatRemaining, FORMAT_LABELS,
  type Timer, type DisplayFormat,
} from './timers.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './Countdown.css';
import { t } from '../../../src/i18n.ts';

export interface CountdownProps {
  timers?: Timer[];
  headerMode?: HeaderMode;
  label?: string;
  _widgetId?: string;
  _editMode?: boolean;
}

export default function Countdown(props: CountdownProps) {
  const layoutStore = useLayout();
  const timers = () => (props.timers ?? []) as Timer[];
  const headerMode = (): HeaderMode => props.headerMode ?? 'normal';

  const [now, setNow] = createSignal(new Date());

  const tickMs = createMemo(() =>
    timers().some(t => t.format === 'dhms') ? 1000 : 30000,
  );
  createPolling(() => setNow(new Date()), tickMs);

  function writeTimers(next: Timer[]) {
    if (!props._widgetId) return;
    layoutStore.updateWidgetConfig(props._widgetId, {
      timers: next,
      headerMode: headerMode(),
      label: props.label ?? '',
    });
  }

  function setTimerFormat(id: string, format: DisplayFormat) {
    writeTimers(timers().map(t => t.id === id ? { ...t, format } : t));
  }

  function removeTimer(id: string) {
    writeTimers(timers().filter(t => t.id !== id));
  }

  // The parameter is `timer`, not `t`: `t` is the translation function this
  // menu now calls, and a shadowing parameter would silently capture it.
  function handleItemCtx(e: MouseEvent, timer: Timer) {
    e.preventDefault(); e.stopPropagation();
    const formats: DisplayFormat[] = ['auto', 'days', 'dhm', 'dhms'];
    // Menu items are an eager snapshot; with keepOpen the active format changes
    // while the menu stays open, so rebuild + re-push after each pick so the
    // checkmark tracks the selection ([C-60][C-61]). Mirrors WidgetGrid.
    const buildItems = (activeFormat: DisplayFormat): CtxMenuItem[] => [
      { header: true, label: t('countdown.ctxDisplay') },
      ...formats.map(f => ({
        icon: f === activeFormat ? Check : undefined,
        label: FORMAT_LABELS[f],
        active: f === activeFormat,
        keepOpen: true,
        action: () => { setTimerFormat(timer.id, f); updateCtxMenuItems(buildItems(f)); },
      })),
      { sep: true },
      { icon: Trash2, label: t('common.remove'), danger: true, action: () => removeTimer(timer.id) },
    ];
    showCtxMenu(e, buildItems(timer.format));
  }

  return (
    <div class="countdown-widget">
      <WidgetHeader
        mode={headerMode()}
        icon={TimerIcon}
        title={props.label || 'Countdowns'}
      />

      <div class="widget-body">
      <Show
        when={timers().length > 0}
        fallback={
          <div class="countdown-empty">
            <span>
              No timers. Click <Settings size={13} strokeWidth={1.5} style="display:inline-block;vertical-align:-2px" /> to add.
            </span>
          </div>
        }
      >
        <div class="countdown-list">
          <For each={timers()}>
            {(t) => {
              const target = createMemo(() => new Date(t.target));
              const result = createMemo(() => formatRemaining(target(), now(), t.format));
              const dateStr = createMemo(() =>
                target().toLocaleString(undefined, {
                  month: 'short', day: 'numeric',
                  hour: 'numeric', minute: '2-digit',
                }),
              );
              return (
                <div
                  class="countdown-item"
                  classList={{ elapsed: result().elapsed }}
                  onContextMenu={(e) => handleItemCtx(e, t)}
                  title={target().toLocaleString()}
                >
                  <div class="countdown-info">
                    <span class="countdown-label">{t.label || 'Untitled'}</span>
                    <span class="countdown-date">{dateStr()}</span>
                  </div>
                  <span class="countdown-remaining">{result().text}</span>
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
