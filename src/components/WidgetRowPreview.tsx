import { ErrorBoundary, createSignal, onMount, onCleanup } from 'solid-js';
import type { WidgetDefinition } from '../widgets/registry.tsx';
import { demoWidgetConfigs } from '../demo/fixtures.ts';
import { previewSpanFor, spanFromSize } from '../demo/previewAspects.ts';

// Preview of a widget rendered with demo fixtures, shown in the Add-Widget
// picker's persistent right-hand pane. The parent swaps `type`/`def` as the
// cursor moves across the card list, so a single widget subtree is mounted at
// a time and re-renders reactively when the hovered widget changes.

interface WidgetRowPreviewProps {
  /** Widget registry key, used to look up demo config overrides so widgets
   *  whose data lives in config (notes/countdown/shortcut) show
   *  meaningful content in the preview. */
  type: string;
  def: WidgetDefinition;
}

// Natural rendering size per grid cell: what the inner widget renders into.
// Matches the desktop grid's MIN_CELL_PX so the preview reproduces the widget
// at its true on-desktop density (then only ever scaled DOWN to fit the pane,
// never up, since upscaling is what made small widgets look oversized/blurry).
const CELL_PX = 96;
// Fallback budget used before the live frame is measured (and if measurement
// somehow fails). Mirrors the real config window's preview pane.
const FALLBACK_W = 340;
const FALLBACK_H = 520;

export default function WidgetRowPreview(props: WidgetRowPreviewProps) {
  // Preview aspect: a per-type override (tunable via the dev aspect panel,
  // committed in previewAspects.json) wins over the widget's grid defaultSpan.
  const span = () => previewSpanFor(props.type, spanFromSize(props.def.defaultSize));
  const cols = () => span()?.cols ?? 2;
  const rows = () => span()?.rows ?? 2;

  const natW = () => cols() * CELL_PX;
  const natH = () => rows() * CELL_PX;

  // Available space in the preview frame (the parent element), tracked live so
  // the widget fills the pane and adapts when the window is resized.
  const [avail, setAvail] = createSignal({ w: FALLBACK_W, h: FALLBACK_H });
  let boxEl: HTMLDivElement | undefined;
  onMount(() => {
    const frame = boxEl?.parentElement;
    if (!frame) return;
    const measure = () => setAvail({ w: frame.clientWidth, h: frame.clientHeight });
    const ro = new ResizeObserver(measure);
    ro.observe(frame);
    measure();
    onCleanup(() => ro.disconnect());
  });

  // Render the widget at native density (scale 1), shrinking only when it
  // doesn't fit the available frame. This keeps every widget at a consistent,
  // true-to-desktop size instead of each widget scaling by a different factor.
  const scale = () => Math.min(1, avail().w / natW(), avail().h / natH());
  const dims = () => ({
    w: Math.round(natW() * scale()),
    h: Math.round(natH() * scale()),
  });

  return (
    <div
      ref={boxEl}
      class="widget-pane-preview"
      style={`width: ${dims().w}px; height: ${dims().h}px;`}
      aria-hidden="true"
    >
      <ErrorBoundary fallback={<div class="widget-pane-preview-fallback" />}>
        <div
          class="widget-pane-preview-stage"
          style={`width: ${natW()}px; height: ${natH()}px; transform: scale(${scale()});`}
        >
          {(() => {
            const C = props.def.component;
            const previewConfig = {
              ...(props.def.defaultConfig ?? {}),
              ...(demoWidgetConfigs[props.type] ?? {}),
            };
            return (
              <C
                {...previewConfig}
                label={props.def.label}
                _editMode={false}
              />
            );
          })()}
        </div>
      </ErrorBoundary>
    </div>
  );
}
