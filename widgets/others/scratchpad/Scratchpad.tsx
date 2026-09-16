import { onMount, onCleanup, createSignal, createEffect, on, For, Show } from 'solid-js';
import { Pencil, Eraser, Trash2 } from 'lucide-solid';
import { useLayout } from '../../../src/viewContext.tsx';
import { logger } from '../../../src/debugLog.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './Scratchpad.css';
import { t } from '../../../src/i18n.ts';

// Preset palette + pen sizes. Closed sets that the toolbar reads from.
export const COLORS = ['#60cdff', '#ffffff', '#ef4444', '#4ade80', '#fbbf24', '#a78bfa'];
export const SIZES = [2, 4, 8];

type Tool = 'pen' | 'eraser';

export interface ScratchpadProps {
  image?: string;
  strokeColor?: string;
  penSize?: number;
  headerMode?: HeaderMode;
  label?: string;
  _widgetId?: string;
}

export default function Scratchpad(props: ScratchpadProps) {
  const layout = useLayout();
  let canvas!: HTMLCanvasElement;
  let ctx: CanvasRenderingContext2D | null = null;
  let drawing = false;

  const [color, setColor] = createSignal(props.strokeColor || COLORS[0]);
  const [size, setSize] = createSignal(props.penSize ?? SIZES[1]);
  const [tool, setTool] = createSignal<Tool>('pen');

  // Re-sync the toolbar signals when config changes externally (e.g. the config
  // panel's pen colour/size controls); otherwise they stay frozen at mount.
  createEffect(on(() => props.strokeColor, (v) => setColor(v || COLORS[0]), { defer: true }));
  createEffect(on(() => props.penSize, (v) => setSize(v ?? SIZES[1]), { defer: true }));

  function persist(patch: Record<string, unknown>) {
    const id = props._widgetId;
    if (!id) return;
    const w = layout.layout.widgets.find((w) => w.id === id);
    layout.updateWidgetConfig(id, { ...(w?.config ?? {}), ...patch });
  }

  function persistImage() {
    if (!canvas || canvas.width === 0) return;
    try {
      persist({ image: canvas.toDataURL('image/png') });
    } catch (e) {
      logger.error('scratchpad', `persist failed: ${e}`);
    }
  }

  function pickColor(c: string) {
    setColor(c);
    setTool('pen');
    persist({ strokeColor: c });
  }

  function pickSize(s: number) {
    setSize(s);
    persist({ penSize: s });
  }

  function toggleEraser() {
    setTool((tl) => (tl === 'eraser' ? 'pen' : 'eraser'));
  }

  // (Re)size the backing store to the element box × DPR, then repaint the last
  // persisted image scaled to fit. Called on mount and on resize.
  function fit() {
    if (!canvas) return;
    const rect = canvas.getBoundingClientRect();
    if (rect.width === 0 || rect.height === 0) return;
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.round(rect.width * dpr);
    canvas.height = Math.round(rect.height * dpr);
    ctx = canvas.getContext('2d');
    if (!ctx) return;
    ctx.scale(dpr, dpr);
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    paint(props.image, rect.width, rect.height);
  }

  function paint(url: string | undefined, w: number, h: number) {
    if (!url || !ctx) return;
    const img = new Image();
    img.onload = () => ctx && ctx.drawImage(img, 0, 0, w, h);
    img.src = url;
  }

  function point(e: PointerEvent): { x: number; y: number } {
    const r = canvas.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
  }

  function down(e: PointerEvent) {
    // Only the primary (left) button draws; a right/middle press opens the
    // widget context menu and must not paint a stroke.
    if (e.button !== 0) return;
    if (!ctx) return;
    drawing = true;
    const p = point(e);
    ctx.beginPath();
    ctx.moveTo(p.x, p.y);
    canvas.setPointerCapture(e.pointerId);
  }

  function move(e: PointerEvent) {
    if (!drawing || !ctx) return;
    const p = point(e);
    // Eraser cuts existing pixels (destination-out); pen paints normally.
    ctx.globalCompositeOperation = tool() === 'eraser' ? 'destination-out' : 'source-over';
    ctx.strokeStyle = color();
    ctx.lineWidth = tool() === 'eraser' ? size() * 2.5 : size();
    ctx.lineTo(p.x, p.y);
    ctx.stroke();
  }

  function up() {
    if (!drawing) return;
    drawing = false;
    persistImage();
  }

  function clear() {
    if (!ctx) return;
    const r = canvas.getBoundingClientRect();
    ctx.clearRect(0, 0, r.width, r.height);
    // Persist the empty state (not a transparent-PNG data URL); otherwise
    // props.image stays truthy and the "Draw here" placeholder never returns.
    persist({ image: '' });
  }

  onMount(() => {
    fit();
    const ro = new ResizeObserver(() => fit());
    ro.observe(canvas);
    onCleanup(() => ro.disconnect());
  });

  return (
    <div class="scratchpad-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={Pencil}
        title={props.label || 'Scratchpad'}
        actions={
          <div class="sp-tools">
            <div class="sp-colors">
              <For each={COLORS}>
                {(c) => (
                  <button
                    class="sp-swatch"
                    classList={{ active: color() === c && tool() === 'pen' }}
                    style={`background:${c}`}
                    onClick={() => pickColor(c)}
                    title={t('scratchpad.penColorTitle')}
                  />
                )}
              </For>
            </div>
            <div class="sp-divider" />
            <div class="sp-sizes">
              <For each={SIZES}>
                {(s) => (
                  <button
                    class="sp-size"
                    classList={{ active: size() === s }}
                    onClick={() => pickSize(s)}
                    title={`Pen size ${s}px`}
                  >
                    <span class="sp-dot" style={`width:${s + 2}px;height:${s + 2}px`} />
                  </button>
                )}
              </For>
            </div>
            <div class="sp-divider" />
            <button
              class="sp-tool"
              classList={{ active: tool() === 'eraser' }}
              onClick={toggleEraser}
              title={t('scratchpad.eraser')}
            ><Eraser size={14} strokeWidth={1.5} /></button>
            <button class="sp-tool" onClick={clear} title={t('scratchpad.clearAll')}>
              <Trash2 size={14} strokeWidth={1.5} />
            </button>
          </div>
        }
      />

      <div class="widget-body">
      <div class="scratchpad-canvas-wrap">
        <canvas
          ref={canvas}
          class="scratchpad-canvas"
          onPointerDown={down}
          onPointerMove={move}
          onPointerUp={up}
          onPointerLeave={up}
        />
        {/* Faint hint on a blank pad, hidden once a drawing is persisted. */}
        <Show when={!props.image}>
          <div class="scratchpad-placeholder">
            <Pencil size={20} strokeWidth={1.5} />
            <span>{t('scratchpad.drawHere')}</span>
          </div>
        </Show>
      </div>
      </div>
    </div>
  );
}
