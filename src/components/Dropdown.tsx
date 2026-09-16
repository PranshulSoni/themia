import { createSignal, createEffect, createMemo, For, Show, onCleanup, type JSX } from 'solid-js';
import { Portal } from 'solid-js/web';
import { ChevronDown } from 'lucide-solid';
import { globalDesign } from '../design.ts';
import { popupMount } from '../popupLayer.ts';
import { createTypeAhead, findByPrefix, isTypeAheadKey, nextIndexLinear } from '../input/kbdNav.ts';
import { t } from '../i18n.ts';
import './Dropdown.css';

export interface DropdownOption {
  label: string;
  value: string;
  icon?: () => JSX.Element;
}

interface DropdownProps {
  value: string;
  options: DropdownOption[];
  onChange: (value: string) => void;
  class?: string;
  searchable?: boolean;
  searchPlaceholder?: string;
}

export default function Dropdown(props: DropdownProps) {
  const [open, setOpen] = createSignal(false);
  const [panelStyle, setPanelStyle] = createSignal<Record<string, string>>({});
  const [focusedIdx, setFocusedIdx] = createSignal<number>(-1);
  const [query, setQuery] = createSignal('');
  const typeAhead = createTypeAhead();
  let triggerRef: HTMLButtonElement | undefined;
  let panelRef: HTMLDivElement | undefined;
  let searchRef: HTMLInputElement | undefined;

  // When searchable + a query is typed, the panel shows only matching options.
  // Keyboard nav, height calc and selection all operate on this filtered view.
  const visibleOptions = createMemo(() => {
    if (!props.searchable) return props.options;
    const q = query().trim().toLowerCase();
    if (!q) return props.options;
    return props.options.filter(o => o.label.toLowerCase().includes(q));
  });

  const selectedOption = () => props.options.find(o => o.value === props.value);
  const selectedLabel = () => selectedOption()?.label ?? props.value;
  const selectedIcon = () => selectedOption()?.icon;

  function handleClickOutside(e: MouseEvent) {
    if (triggerRef && triggerRef.contains(e.target as Node)) return;
    if (panelRef && panelRef.contains(e.target as Node)) return;
    close();
  }

  function handleKeyDown(e: KeyboardEvent) {
    // stopPropagation so Escape closes ONLY the dropdown, not the config window
    // behind it (escapeStack listens at window; this listener runs first).
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); triggerRef?.focus(); return; }
    if (e.key === 'Tab') { close(); return; }
    const opts = visibleOptions();
    if (e.key === 'Enter' || e.key === ' ') {
      // Space is a normal character while typing in the search input.
      if (e.key === ' ' && props.searchable) return;
      e.preventDefault();
      const i = focusedIdx();
      if (i >= 0 && i < opts.length) select(opts[i].value);
      return;
    }
    const next = nextIndexLinear(e.key, focusedIdx(), opts.length);
    if (next !== null) {
      e.preventDefault();
      setFocusedIdx(next);
      scrollFocusedIntoView();
      return;
    }
    // The search input owns plain-character typing; type-ahead is only for the
    // non-searchable dropdown.
    if (!props.searchable && isTypeAheadKey(e)) {
      const q = typeAhead.push(e.key);
      const hit = findByPrefix(
        opts,
        (o) => o.label,
        q,
        Math.max(0, focusedIdx() + (q.length === 1 ? 1 : 0)),
      );
      if (hit >= 0) { e.preventDefault(); setFocusedIdx(hit); scrollFocusedIntoView(); }
    }
  }

  function scrollFocusedIntoView(): void {
    queueMicrotask(() => {
      panelRef?.querySelectorAll<HTMLElement>('.dropdown-item')[focusedIdx()]
        ?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    });
  }

  function close() {
    setOpen(false);
    setQuery('');
    document.removeEventListener('mousedown', handleClickOutside);
    document.removeEventListener('keydown', handleKeyDown);
    window.removeEventListener('scroll', handleReposition, { capture: true });
    window.removeEventListener('resize', handleReposition);
  }

  // Measure the trigger and place the panel against it. Called on open AND on
  // every ancestor scroll / window resize while open, so the panel stays
  // anchored to the button instead of floating where it first opened (it's
  // position:fixed, so it does not move with the page on its own).
  function reposition() {
    if (!triggerRef) return;
    const s = globalDesign.uiScale || 1;
    const rect = triggerRef.getBoundingClientRect();
    const top = rect.bottom / s;
    const triggerTop = rect.top / s;
    const width = rect.width / s;
    const viewH = window.innerHeight / s;
    const viewW = window.innerWidth / s;
    const rightFromViewport = viewW - rect.right / s;
    // Fit the panel to the viewport. Open downward by default; flip up only when
    // below can't show a full panel AND above has more room. Either way clamp the
    // panel's max-height to the space actually available on the chosen side, so a
    // long list (e.g. every installed font) never runs off the bottom of the
    // screen — which left its lower rows below the viewport edge and effectively
    // unscrollable. PANEL_MAX mirrors .dropdown-panel's CSS max-height.
    const MARGIN = 8;
    const PANEL_MAX = 300;
    const spaceBelow = viewH - top - MARGIN;
    const spaceAbove = triggerTop - MARGIN;
    const flipUp = spaceBelow < PANEL_MAX && spaceAbove > spaceBelow;
    const maxH = Math.min(PANEL_MAX, Math.max(0, flipUp ? spaceAbove : spaceBelow));
    const style: Record<string, string> = {
      right: `${rightFromViewport}px`,
      'min-width': `${width}px`,
      'max-height': `${maxH}px`,
    };
    if (flipUp) {
      style.bottom = `${viewH - triggerTop + 4}px`;
      style.top = 'auto';
    } else {
      style.top = `${top + 4}px`;
      style.bottom = 'auto';
    }
    // Horizontal clamp (once the panel is rendered so its width is known): the
    // panel is right-anchored to the trigger and extends leftward, so a wide
    // panel near the left edge would clip offscreen — ease the right anchor so it
    // sits 8px from the left viewport edge instead.
    if (panelRef) {
      const panelW = panelRef.offsetWidth;
      const triggerRight = rect.right / s;
      if (triggerRight - panelW < 8) {
        style.right = `${Math.max(8, viewW - panelW - 8)}px`;
      }
    }
    setPanelStyle(style);
  }

  // Re-anchor the open panel to its trigger on scroll/resize. Capture phase so
  // scrolls of NESTED scroll containers (e.g. the settings body) are caught —
  // scroll events don't bubble, so a bubble-phase window listener would miss them.
  function handleReposition() {
    if (open()) reposition();
  }

  function open_() {
    if (!triggerRef) return;
    setQuery('');
    reposition();
    setFocusedIdx(Math.max(0, visibleOptions().findIndex(o => o.value === props.value)));
    typeAhead.reset();
    setOpen(true);
    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleKeyDown);
    window.addEventListener('scroll', handleReposition, { capture: true, passive: true });
    window.addEventListener('resize', handleReposition);
  }

  function toggle() {
    if (open()) close(); else open_();
  }

  function handleTriggerKey(e: KeyboardEvent) {
    if (open()) return;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      open_();
    }
  }

  function select(value: string) {
    props.onChange(value);
    close();
    triggerRef?.focus();
  }

  function onSearchInput(e: InputEvent) {
    setQuery((e.currentTarget as HTMLInputElement).value);
    setFocusedIdx(visibleOptions().length > 0 ? 0 : -1);
  }

  // When the panel opens, scroll the focused item into view, (when searchable)
  // move keyboard focus to the search input, and re-run positioning now that the
  // panel is in the DOM so the horizontal clamp (which needs its rendered width)
  // applies.
  createEffect(() => {
    if (!open()) return;
    scrollFocusedIntoView();
    if (props.searchable) queueMicrotask(() => searchRef?.focus());
    queueMicrotask(() => reposition());
  });

  onCleanup(() => {
    document.removeEventListener('mousedown', handleClickOutside);
    document.removeEventListener('keydown', handleKeyDown);
    window.removeEventListener('scroll', handleReposition, { capture: true });
    window.removeEventListener('resize', handleReposition);
  });

  return (
    <>
      <button
        class={`dropdown-trigger kbd-focusable${open() ? ' open' : ''}${props.class ? ' ' + props.class : ''}`}
        onClick={toggle}
        onKeyDown={handleTriggerKey}
        ref={triggerRef}
        aria-haspopup="listbox"
        aria-expanded={open()}
      >
        <Show when={selectedIcon()}><span class="dropdown-item-icon">{selectedIcon()!()}</span></Show>
        <span>{selectedLabel()}</span>
        <ChevronDown size={12} strokeWidth={1.5} class="dropdown-chevron" />
      </button>
      <Show when={open()}>
        <Portal mount={popupMount()}>
          <div class="dropdown-panel" style={panelStyle()} ref={panelRef} role="listbox">
            {/* Search stays OUTSIDE the scroll area (a flex sibling of the list),
                so it's pinned while the list below scrolls. */}
            <Show when={props.searchable}>
              <input
                ref={searchRef}
                class="dropdown-search"
                type="text"
                value={query()}
                placeholder={props.searchPlaceholder ?? ''}
                onInput={onSearchInput}
                autocomplete="off"
                spellcheck={false}
              />
            </Show>
            <div class="dropdown-list">
              <For each={visibleOptions()}>
                {(opt, i) => (
                  <div
                    classList={{
                      'dropdown-item': true,
                      active: opt.value === props.value,
                      'kbd-focused': focusedIdx() === i(),
                    }}
                    role="option"
                    aria-selected={opt.value === props.value}
                    onMouseEnter={() => setFocusedIdx(i())}
                    onClick={() => select(opt.value)}
                  >
                    <Show when={opt.icon}><span class="dropdown-item-icon">{opt.icon!()}</span></Show>
                    {opt.label}
                  </div>
                )}
              </For>
              <Show when={props.searchable && visibleOptions().length === 0}>
                <div class="dropdown-empty">{t('common.noResults')}</div>
              </Show>
            </div>
          </div>
        </Portal>
      </Show>
    </>
  );
}
