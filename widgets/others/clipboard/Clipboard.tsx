import { createSignal, createEffect, For, Show, onMount, onCleanup } from 'solid-js';
import { Clipboard as ClipIcon, Trash2, Check } from 'lucide-solid';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { demoClipboardItems } from '../../../src/demo/fixtures.ts';
import { listen } from '@tauri-apps/api/event';
import { logger } from '../../../src/debugLog.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './Clipboard.css';
import { t } from '../../../src/i18n.ts';

export interface ClipboardProps {
  maxItems?: number;
  headerMode?: HeaderMode;
  label?: string;
}

export default function Clipboard(props: ClipboardProps) {
  const [items, setItems] = createSignal<string[]>(isDemo() ? demoClipboardItems() : []);
  const [copied, setCopied] = createSignal<string | null>(null);
  const max = () => Math.max(1, Math.min(200, props.maxItems ?? 50));

  function push(text: string) {
    if (!text) return;
    setItems((prev) => {
      const deduped = prev.filter((t) => t !== text);
      return [text, ...deduped].slice(0, max());
    });
  }

  // Lowering the history size in config must trim the existing list, not just
  // cap future pushes. Re-run whenever max() changes.
  createEffect(() => {
    const m = max();
    setItems((prev) => (prev.length > m ? prev.slice(0, m) : prev));
  });

  onMount(() => {
    invoke('clipboard_start').catch((err) => logger.error('clipboard', String(err)));
  });

  const unlisten = listen<string>('clipboard://update', (e) => {
    if (typeof e.payload === 'string') push(e.payload);
  });
  onCleanup(() => { unlisten.then((u) => u()).catch(() => {}); });

  function recopy(text: string) {
    invoke('clipboard_set', { text })
      .then(() => {
        setCopied(text);
        setTimeout(() => setCopied(null), 1200);
      })
      .catch((err) => logger.error('clipboard', String(err)));
    push(text);
  }

  return (
    <div class="clipboard-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={ClipIcon}
        title={props.label || 'Clipboard'}
        actions={
          <Show when={items().length > 0}>
            <button class="clipboard-clear" onClick={() => setItems([])} title={t('clipboard.clearHistory')}>
              <Trash2 size={13} strokeWidth={1.5} />
            </button>
          </Show>
        }
      />

      <div class="widget-body">
      <Show
        when={items().length > 0}
        fallback={<div class="clipboard-empty"><span>{t('clipboard.empty')}</span></div>}
      >
        <div class="clipboard-list">
          <For each={items()}>
            {(text) => (
              <button class="clipboard-item" onClick={() => recopy(text)} title={t('clipboard.clickToCopy')}>
                <span class="clipboard-text">{text}</span>
                <span class="clipboard-action" classList={{ 'is-copied': copied() === text }}>
                  <Show when={copied() === text} fallback={<ClipIcon size={13} strokeWidth={1.5} />}>
                    <Check size={13} strokeWidth={1.5} />
                  </Show>
                </span>
              </button>
            )}
          </For>
        </div>
      </Show>
      </div>
    </div>
  );
}
