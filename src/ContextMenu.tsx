import { createSignal, createEffect, createMemo, onCleanup } from 'solid-js';
import { For, Show } from 'solid-js';
import { Dynamic, Portal } from 'solid-js/web';
import { ctxMenu, closeCtxMenu, type CtxMenuItem, type CtxMenuIconProps } from './ctxMenuStore.ts';
import type { Component } from 'solid-js';
import { globalDesign } from './design.ts';
import { popupMount } from './popupLayer.ts';
import { Check, ChevronRight } from 'lucide-solid';
import { createTypeAhead, findByPrefix, isTypeAheadKey } from './input/kbdNav.ts';

function CtxIcon(props: { icon: CtxMenuItem['icon'] }) {
  return (
    <Show
      when={typeof props.icon === 'function'}
      fallback={<>{props.icon ?? ''}</>}
    >
      <Dynamic component={props.icon as Component<CtxMenuIconProps>} size={14} strokeWidth={1.5} />
    </Show>
  );
}

function MenuPanel(props: { items: CtxMenuItem[]; parentRect?: DOMRect; isSubmenu?: boolean; onClose?: () => void }) {
  let panelEl: HTMLDivElement | undefined;
  const [hoveredIdx, setHoveredIdx] = createSignal<number>(-1);
  const [openSubIdx, setOpenSubIdx] = createSignal<number>(-1);
  let subTimeout: ReturnType<typeof setTimeout> | undefined;
  const itemEls: Map<number, HTMLDivElement> = new Map();
  const typeAhead = createTypeAhead();

  const navIndices = createMemo(() =>
    props.items
      .map((item, i) => ({ item, i }))
      .filter(({ item }) => !item.sep && !item.header && !item.disabled)
      .map(({ i }) => i)
  );

  createEffect(() => {
    if (!props.isSubmenu && ctxMenu.menu) {
      setHoveredIdx(-1);
      setOpenSubIdx(-1);
      Promise.resolve().then(() => panelEl?.focus());
    }
  });

  onCleanup(() => { if (subTimeout) clearTimeout(subTimeout); });

  function handleHover(i: number, item: CtxMenuItem) {
    setHoveredIdx(i);
    if (subTimeout) clearTimeout(subTimeout);
    if (item.children?.length) {
      subTimeout = setTimeout(() => setOpenSubIdx(i), 120);
    } else {
      setOpenSubIdx(-1);
    }
  }

  function handleLeave(i: number) {
    if (hoveredIdx() === i && openSubIdx() !== i) setHoveredIdx(-1);
  }

  function handleClick(item: CtxMenuItem) {
    if (item.disabled) return;
    if (item.children?.length) { setOpenSubIdx(hoveredIdx()); return; }
    item.action?.();
    if (!item.keepOpen) closeCtxMenu();
  }

  function handleKey(e: KeyboardEvent) {
    if (e.key === 'Escape') {
      e.preventDefault(); e.stopPropagation();
      if (props.isSubmenu && props.onClose) props.onClose();
      else closeCtxMenu();
      return;
    }
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      const curr = navIndices().indexOf(hoveredIdx());
      setHoveredIdx(navIndices()[(curr + 1) % navIndices().length] ?? -1);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      const curr = navIndices().indexOf(hoveredIdx());
      setHoveredIdx(navIndices()[(curr - 1 + navIndices().length) % navIndices().length] ?? -1);
    } else if (e.key === 'ArrowRight') {
      const item = props.items[hoveredIdx()];
      if (item?.children?.length) { setOpenSubIdx(hoveredIdx()); }
    } else if (e.key === 'ArrowLeft') {
      if (props.isSubmenu && props.onClose) { e.preventDefault(); e.stopPropagation(); props.onClose(); }
    } else if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      const item = props.items[hoveredIdx()];
      if (item && !item.disabled && !item.sep && !item.header) handleClick(item);
    } else if (isTypeAheadKey(e)) {
      // Type-ahead: jump to the first navigable item whose label starts with
      // the accumulated query. Resets after 500ms of inactivity.
      const query = typeAhead.push(e.key);
      const idxs = navIndices();
      const startFrom = Math.max(0, idxs.indexOf(hoveredIdx()) + (query.length === 1 ? 1 : 0));
      const searchList = idxs.map(i => props.items[i]);
      const hit = findByPrefix(searchList, (it) => String(it.label ?? ''), query, startFrom);
      if (hit >= 0) { e.preventDefault(); setHoveredIdx(idxs[hit]); }
    }
  }

  function getItemRect(idx: number): DOMRect | undefined {
    return itemEls.get(idx)?.getBoundingClientRect();
  }

  return (
    <div
      class={props.isSubmenu ? 'ctx-submenu' : 'ctx-menu'}
      role="menu"
      tabIndex="-1"
      ref={el => { panelEl = el; if (props.isSubmenu) Promise.resolve().then(() => el?.focus()); }}
      style={props.isSubmenu && props.parentRect
        ? subPosition(props.parentRect)
        : !props.isSubmenu && ctxMenu.menu
          ? `left:${ctxMenu.menu.x}px;top:${ctxMenu.menu.y}px`
          : ''}
      onKeyDown={handleKey}
    >
      <For each={props.items}>
        {(item, i) => (
          <>
            {item.sep && <div class="ctx-sep" role="separator" />}
            {item.header && <div class="ctx-header">{item.label}</div>}
            {!item.sep && !item.header && (
              <div
                class={`ctx-item${item.active ? ' active' : ''}${item.danger ? ' danger' : ''}${item.disabled ? ' disabled' : ''}${hoveredIdx() === i() ? ' hovered' : ''}`}
                role="menuitem"
                tabIndex="0"
                ref={el => { if (item.children?.length) itemEls.set(i(), el); }}
                onClick={() => handleClick(item)}
                onMouseEnter={() => handleHover(i(), item)}
                onMouseLeave={() => handleLeave(i())}
              >
                <span class="ctx-icon"><CtxIcon icon={item.icon} /></span>
                <span class="ctx-label">{item.label}</span>
                {item.shortcut && <span class="ctx-shortcut">{item.shortcut}</span>}
                {item.children?.length
                  ? <span class="ctx-chevron"><ChevronRight size={12} strokeWidth={1.5} /></span>
                  : <span class="ctx-check">{item.active ? <Check size={12} strokeWidth={2} /> : ''}</span>
                }
                <Show when={openSubIdx() === i() && item.children?.length}>
                  <Portal mount={popupMount()}>
                    <MenuPanel
                      items={item.children!}
                      parentRect={getItemRect(i())}
                      isSubmenu
                      onClose={() => { setOpenSubIdx(-1); setHoveredIdx(i()); panelEl?.focus(); }}
                    />
                  </Portal>
                </Show>
              </div>
            )}
          </>
        )}
      </For>
    </div>
  );
}

function subPosition(parentRect: DOMRect): string {
  // The menu lives in `.popup-layer` (widget window) / scaled `body` (config
  // window), both `transform: scale(uiScale)`. getBoundingClientRect returns
  // viewport (post-scale) pixels, but the submenu's `left`/`top` CSS are
  // interpreted in pre-scale pixels. Divide by scale to match the top-
  // level menu's coordinate system (ctxMenuStore does the same).
  const s = globalDesign.uiScale || 1;
  const gap = 2;
  const subWidth = 200;
  const subEstHeight = 300;
  const winW = window.innerWidth / s;
  const winH = window.innerHeight / s;
  let x = parentRect.right / s + gap;
  let y = parentRect.top / s;
  if (x + subWidth > winW - 8) x = parentRect.left / s - subWidth - gap;
  if (y + subEstHeight > winH - 8) y = Math.max(8, winH - subEstHeight - 8);
  return `left:${x}px;top:${y}px`;
}

/**
 * Right-click on the backdrop: dismiss the open menu *and* open the one for
 * whatever sits under the pointer, in a single click (Windows shell behaviour).
 * The full-window backdrop swallows the hit-test, so without the replay the
 * first right-click only closed the menu and a second was needed to open the
 * new one. `stopPropagation` keeps App's window-level desktop handler from
 * acting on the swallowed original event; the replayed one drives it instead.
 */
function handleBackdropCtx(e: MouseEvent): void {
  e.preventDefault();
  e.stopPropagation();
  // `.popup-layer` is pointer-events:none, so hit-testing only surfaces the
  // menu chrome itself plus the real content underneath it.
  const under = document
    .elementsFromPoint(e.clientX, e.clientY)
    .find(el => !el.closest('.ctx-backdrop, .ctx-menu, .ctx-submenu'));
  closeCtxMenu();
  under?.dispatchEvent(new MouseEvent('contextmenu', {
    bubbles: true, cancelable: true, view: window,
    button: 2, buttons: 2,
    clientX: e.clientX, clientY: e.clientY,
    screenX: e.screenX, screenY: e.screenY,
  }));
}

export default function ContextMenu() {
  // Portal into `.popup-layer` (widget window) / `body` (config window) so the
  // menu follows the general uiScale, not the per-monitor widget scale of
  // `#app.app-layer` where this component is mounted.
  return (
    <Show when={ctxMenu.menu}>
      <Portal mount={popupMount()}>
        <div
          class="ctx-backdrop"
          data-themia-interactive="true"
          onClick={closeCtxMenu}
          onContextMenu={handleBackdropCtx}
        />
        <MenuPanel items={ctxMenu.menu!.items} />
      </Portal>
    </Show>
  );
}
