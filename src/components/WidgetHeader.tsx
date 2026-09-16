import { Show, type JSX, type Component } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import { ExternalLink } from 'lucide-solid';
import type { HeaderMode } from '../widgets/registry.tsx';
import { useWidgetChrome } from '../widgetChrome.ts';
import { t } from '../i18n.ts';
import './WidgetHeader.css';

// The single header every widget renders through (WidgetHeader.SPEC).
// One component, three modes, no per-widget header markup or background CSS.
// The root keeps the global `widget-header` class so the header background/blur
// tokens in App.css apply; all per-mode LAYOUT lives in WidgetHeader.css keyed
// off `data-header-mode`.
export interface WidgetHeaderProps {
  mode: HeaderMode;
  /** lucide component reference (not JSX). Shown in `normal` only. [S-05] */
  icon?: Component<{ size?: number; strokeWidth?: number }>;
  /** Title text; caller passes `props.label || <default>`. [S-06] */
  title: string;
  /** Shown in both `normal` and `compact` (e.g. weather location). [S-07] */
  secondary?: JSX.Element;
  /** Right-aligned buttons; rendered in `normal` only. [S-08] */
  actions?: JSX.Element;
  /** Replaces the title text in `normal` (e.g. folder search input). [S-09] */
  titleOverride?: JSX.Element;
  onDblClick?: (e: MouseEvent) => void;
  /** Extra root class for per-widget hooks (container queries, etc.). */
  class?: string;
}

export default function WidgetHeader(props: WidgetHeaderProps): JSX.Element {
  // Grid-published chrome ([S-18]). Absent outside the grid (pop-out window,
  // previews, stack/tab children), which is exactly when it shouldn't render.
  const chrome = useWidgetChrome();
  const popOut = () => chrome?.popOut;

  return (
    <Show when={props.mode !== 'none'}>
      <div
        class={`widget-header${props.class ? ' ' + props.class : ''}`}
        data-header-mode={props.mode}
        onDblClick={(e) => props.onDblClick?.(e)}
      >
        <div class="wh-title">
          <Show when={props.mode === 'normal' && props.icon}>
            <span class="wh-icon">
              <Dynamic component={props.icon!} size={13} strokeWidth={1.5} />
            </span>
          </Show>
          <Show
            when={props.mode === 'normal' && props.titleOverride}
            fallback={<span class="wh-text">{props.title}</span>}
          >
            {props.titleOverride}
          </Show>
          <Show when={props.secondary}>
            <span class="wh-secondary">{props.secondary}</span>
          </Show>
        </div>
        <Show when={props.mode === 'normal' && (props.actions || popOut())}>
          <div class="wh-actions">
            {props.actions}
            {/* Chrome sits last, so it lands at the far right of the band and
                the widget's own actions keep their order. It takes real layout
                space rather than floating, which is the whole point: a floating
                button would sit on top of whatever the widget put here. */}
            <Show when={popOut()}>
              <button
                class="wh-popout-btn"
                title={t('ctxMenu.openInWindow')}
                aria-label={t('ctxMenu.openInWindow')}
                onMouseDown={(e) => e.stopPropagation()}
                onClick={(e) => { e.stopPropagation(); popOut()?.(); }}
              ><ExternalLink size={13} strokeWidth={1.5} /></button>
            </Show>
          </div>
        </Show>
      </div>
    </Show>
  );
}
