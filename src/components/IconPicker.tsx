import { createEffect, createMemo, createSignal, onCleanup, Show, For } from 'solid-js';
import { Portal } from 'solid-js/web';
import { globalDesign } from '../design.ts';
import { popupMount } from '../popupLayer.ts';
import { nextIndexGrid } from '../input/kbdNav.ts';
import { ChevronDown, Ban } from 'lucide-solid';
import { VIEW_ICON_MAP } from './iconMap.tsx';
import { t } from '../i18n.ts';

// The catalogue lives in its own module so icon-only consumers (view bar, group
// widget, tray sync) do not pull this whole control into their bundles.
// Re-exported here because every existing importer reaches for it through this
// module.
export { VIEW_ICON_MAP, SCREEN_ICON_MAP, isKnownIcon } from './iconMap.tsx';

// Panel layout: must stay in sync with .icon-picker-panel CSS (8 cols × 32px).
const GRID_COLS = 8;

interface IconPickerProps {
  value: string;
  onChange: (v: string) => void;
  /** Offer a leading "no icon of my own" cell, which picks the empty string.
   *  A view always wants SOME icon (it falls back to its initials), but a tray
   *  widget's empty value means "use the widget's own icon" (Tray.SPEC [S-14]),
   *  and without this there is no way back to it once one is chosen. */
  allowClear?: boolean;
  /** Trigger text while nothing is picked. Defaults to "None"; a surface whose
   *  empty state means something more specific passes its own. */
  placeholder?: string;
}

export default function IconPicker(pickerProps: IconPickerProps) {
  const [pickerOpen, setPickerOpen] = createSignal(false);
  const [pickerStyle, setPickerStyle] = createSignal<Record<string, string>>({});
  const [focusedIdx, setFocusedIdx] = createSignal<number>(-1);
  let pickerTrigger: HTMLButtonElement | undefined;
  let pickerPanel: HTMLDivElement | undefined;

  // The clear cell is an ordinary entry keyed by the empty string, so roving
  // focus, Enter, and the active highlight all treat it like any other icon
  // instead of needing a parallel path.
  const iconKeys = createMemo(() =>
    pickerProps.allowClear ? ['', ...Object.keys(VIEW_ICON_MAP)] : Object.keys(VIEW_ICON_MAP));

  function closePicker_() {
    setPickerOpen(false);
    document.removeEventListener('mousedown', handleOutside);
    document.removeEventListener('keydown', handleKeyDown);
  }
  function handleOutside(e: MouseEvent) {
    if (pickerTrigger?.contains(e.target as Node)) return;
    if (pickerPanel?.contains(e.target as Node)) return;
    closePicker_();
  }
  function openPicker() {
    if (!pickerTrigger) return;
    const s = globalDesign.uiScale || 1;
    const rect = pickerTrigger.getBoundingClientRect();
    const viewW = window.innerWidth / s;
    const viewH = window.innerHeight / s;
    // Panel is a fixed 8-col grid of 32px buttons (8×32 + 7×2 gap + 2×6 pad).
    const PANEL_W = 282;
    const top = rect.bottom / s + 4;
    const flipUp = top + 220 > viewH;
    // Right-anchored to match Dropdown: panel's right edge aligns with trigger's
    // right edge and grows leftward. Clamp so the panel can't clip past the
    // left edge when the trigger sits near x=0.
    const rightFromViewport = viewW - rect.right / s;
    const right = Math.max(8, Math.min(rightFromViewport, viewW - PANEL_W - 8));
    const style: Record<string, string> = { right: `${right}px` };
    if (flipUp) { style.bottom = `${viewH - rect.top / s + 4}px`; style.top = 'auto'; }
    else { style.top = `${top}px`; style.bottom = 'auto'; }
    setPickerStyle(style);
    // Seed roving focus on the currently-picked icon, or the first one.
    const keys = iconKeys();
    const idx = keys.indexOf(pickerProps.value);
    setFocusedIdx(idx >= 0 ? idx : 0);
    setPickerOpen(true);
    document.addEventListener('mousedown', handleOutside);
    document.addEventListener('keydown', handleKeyDown);
  }
  function togglePicker() {
    if (pickerOpen()) { closePicker_(); return; }
    openPicker();
  }
  function pickIcon(key: string) {
    pickerProps.onChange(key);
    closePicker_();
    pickerTrigger?.focus();
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (!pickerOpen()) return;
    if (e.key === 'Escape') {
      // Local Escape closes only the picker; stop the bubble so the global
      // escape stack (which would close the whole config window) does not
      // also fire.
      e.preventDefault();
      e.stopPropagation();
      closePicker_();
      pickerTrigger?.focus();
      return;
    }
    if (e.key === 'Tab') { closePicker_(); return; }
    const keys = iconKeys();
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      const i = focusedIdx();
      if (i >= 0 && i < keys.length) pickIcon(keys[i]);
      return;
    }
    const next = nextIndexGrid(e.key, focusedIdx(), keys.length, GRID_COLS);
    if (next !== null) {
      e.preventDefault();
      setFocusedIdx(next);
      scrollFocusedIntoView();
    }
  }

  function handleTriggerKey(e: KeyboardEvent) {
    if (pickerOpen()) return;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      openPicker();
    }
  }

  function scrollFocusedIntoView(): void {
    queueMicrotask(() => {
      pickerPanel?.querySelectorAll<HTMLElement>('.icon-picker-btn')[focusedIdx()]
        ?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    });
  }

  createEffect(() => { if (pickerOpen()) scrollFocusedIntoView(); });

  onCleanup(() => {
    document.removeEventListener('mousedown', handleOutside);
    document.removeEventListener('keydown', handleKeyDown);
  });

  const currentIcon = () => pickerProps.value && VIEW_ICON_MAP[pickerProps.value];
  return (
    <>
      <button
        class={`dropdown-trigger kbd-focusable${pickerOpen() ? ' open' : ''}`}
        onClick={togglePicker}
        onKeyDown={handleTriggerKey}
        ref={pickerTrigger}
        aria-haspopup="grid"
        aria-expanded={pickerOpen()}
      >
        {currentIcon()
          ? VIEW_ICON_MAP[pickerProps.value](14)
          : <span style={{ opacity: '0.5' }}>{pickerProps.placeholder ?? t('app.iconPickerNone')}</span>}
        <ChevronDown size={12} strokeWidth={1.5} class="dropdown-chevron" />
      </button>
      <Show when={pickerOpen()}>
        <Portal mount={popupMount()}>
          <div class="icon-picker-panel" style={pickerStyle()} ref={pickerPanel} role="grid">
            <For each={iconKeys()}>
              {(key, i) => (
                <button
                  classList={{
                    'icon-picker-btn': true,
                    active: pickerProps.value === key,
                    'kbd-focused': focusedIdx() === i(),
                  }}
                  onMouseEnter={() => setFocusedIdx(i())}
                  onClick={() => pickIcon(key)}
                  title={key ? key.charAt(0).toUpperCase() + key.slice(1) : (pickerProps.placeholder ?? t('app.iconPickerNone'))}
                >{key ? VIEW_ICON_MAP[key](16) : <Ban size={16} strokeWidth={1.5} />}</button>
              )}
            </For>
          </div>
        </Portal>
      </Show>
    </>
  );
}
