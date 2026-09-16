import { createSignal, createEffect, onCleanup, untrack, Show } from 'solid-js';
import { Portal } from 'solid-js/web';
import { Pipette } from 'lucide-solid';
import { beginPointerDrag } from './input/pointerDrag.ts';
import { globalDesign } from './design.ts';
import { popupMount } from './popupLayer.ts';
import { normalizeEyedropperHex } from './eyedropper.ts';
import { showToast } from './toastStore.ts';
import { logger } from './debugLog.ts';
import { t } from './i18n.ts';
import './ColorPicker.css';

interface ColorPickerProps {
  value: string;
  onChange: (hex: string) => void;
  alpha?: number;
  onAlphaChange?: (a: number) => void;
}

const PRESETS = [
  '#ffffff', '#c0c0c0', '#808080', '#000000',
  '#ef4444', '#f97316', '#eab308', '#22c55e',
  '#06b6d4', '#3b82f6', '#8b5cf6', '#ec4899',
  '#60cdff', '#34d399', '#fbbf24', '#f87171',
];

function hexToHsv(hex: string): [number, number, number] {
  const r = parseInt(hex.slice(1, 3), 16) / 255;
  const g = parseInt(hex.slice(3, 5), 16) / 255;
  const b = parseInt(hex.slice(5, 7), 16) / 255;
  const max = Math.max(r, g, b), min = Math.min(r, g, b);
  const d = max - min;
  let h = 0;
  if (d !== 0) {
    if (max === r) h = ((g - b) / d + 6) % 6;
    else if (max === g) h = (b - r) / d + 2;
    else h = (r - g) / d + 4;
    h *= 60;
  }
  const s = max === 0 ? 0 : d / max;
  return [h, s, max];
}

function hsvToHex(h: number, s: number, v: number): string {
  const c = v * s;
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
  const m = v - c;
  let r = 0, g = 0, b = 0;
  if (h < 60) { r = c; g = x; }
  else if (h < 120) { r = x; g = c; }
  else if (h < 180) { g = c; b = x; }
  else if (h < 240) { g = x; b = c; }
  else if (h < 300) { r = x; b = c; }
  else { r = c; b = x; }
  const toHex = (n: number) => Math.round((n + m) * 255).toString(16).padStart(2, '0');
  return `#${toHex(r)}${toHex(g)}${toHex(b)}`;
}

function hexToRgb(hex: string): [number, number, number] {
  return [
    parseInt(hex.slice(1, 3), 16),
    parseInt(hex.slice(3, 5), 16),
    parseInt(hex.slice(5, 7), 16),
  ];
}

function rgbToHex(r: number, g: number, b: number): string {
  const toHex = (n: number) => Math.max(0, Math.min(255, Math.round(n))).toString(16).padStart(2, '0');
  return `#${toHex(r)}${toHex(g)}${toHex(b)}`;
}

export default function ColorPicker(props: ColorPickerProps) {
  const [open, setOpen] = createSignal(false);
  const [hue, setHue] = createSignal(0);
  const [sat, setSat] = createSignal(0);
  const [val, setVal] = createSignal(1);
  const [hexInput, setHexInput] = createSignal('');
  const [popoverPos, setPopoverPos] = createSignal({ x: 0, y: 0 });

  let swatchRef!: HTMLButtonElement;
  let gradientRef!: HTMLDivElement;
  let popoverRef!: HTMLDivElement;
  let interacting = false;

  // Sync HSV from prop only when not actively interacting
  createEffect(() => {
    const v = props.value;
    if (interacting) return;
    const [h, s, vv] = hexToHsv(v);
    untrack(() => {
      setHue(h);
      setSat(s);
      setVal(vv);
      setHexInput(v);
    });
  });

  function emitColor(h: number, s: number, v: number) {
    interacting = true;
    const hex = hsvToHex(h, s, v);
    setHexInput(hex);
    props.onChange(hex);
    // Reset interacting flag after Solid's batch update
    queueMicrotask(() => { interacting = false; });
  }

  function computePopoverPos() {
    const s = globalDesign.uiScale || 1;
    const rect = swatchRef.getBoundingClientRect();
    let x = rect.left / s;
    let y = rect.bottom / s + 6;
    const vw = window.innerWidth / s;
    const vh = window.innerHeight / s;
    if (x + 304 > vw) x = vw - 304;
    if (y + 340 > vh) y = rect.top / s - 346;
    // Clamp to the top-left edge so a narrow viewport (left-shift) or a swatch
    // near the top (flip-up) can't push the popover partly off-screen.
    x = Math.max(0, x);
    y = Math.max(0, y);
    setPopoverPos({ x, y });
  }

  function openPopover() {
    computePopoverPos();
    setOpen(true);
  }

  // Position is computed at open time, but the config panel can scroll (or the
  // window resize) while the popover is open, moving the swatch out from under
  // it. Re-anchor on scroll/resize so the popover tracks its swatch. Capture
  // phase so scrolls on any ancestor scroll container are caught, not just the
  // window. Event-driven (no polling).
  createEffect(() => {
    if (!open()) return;
    const onReflow = () => computePopoverPos();
    window.addEventListener('scroll', onReflow, true);
    window.addEventListener('resize', onReflow);
    onCleanup(() => {
      window.removeEventListener('scroll', onReflow, true);
      window.removeEventListener('resize', onReflow);
    });
  });

  // Close on click outside; use setTimeout to avoid catching the opening click
  createEffect(() => {
    if (open()) {
      const handler = (e: MouseEvent) => {
        const target = e.target as HTMLElement;
        if (popoverRef && !popoverRef.contains(target) && !swatchRef.contains(target)) {
          setOpen(false);
        }
      };
      // Delay adding listener to next frame so it doesn't catch the opening click
      const raf = requestAnimationFrame(() => {
        document.addEventListener('mousedown', handler);
      });
      onCleanup(() => {
        cancelAnimationFrame(raf);
        document.removeEventListener('mousedown', handler);
      });
    }
  });

  // Gradient drag
  function gradientPos(e: MouseEvent) {
    const rect = gradientRef.getBoundingClientRect();
    const s = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
    const v = Math.max(0, Math.min(1, 1 - (e.clientY - rect.top) / rect.height));
    setSat(s);
    setVal(v);
    emitColor(hue(), s, v);
  }

  function onGradientDown(e: MouseEvent) {
    // beginPointerDrag preventDefaults the mousedown by default (was explicit
    // here) and owns the mousemove/mouseup cleanup.
    gradientPos(e);
    beginPointerDrag(e, { onMove: (_d, ev) => gradientPos(ev) });
  }

  function onHueInput(e: Event) {
    const h = parseFloat((e.target as HTMLInputElement).value);
    setHue(h);
    emitColor(h, sat(), val());
  }

  function onAlphaInput(e: Event) {
    const a = parseFloat((e.target as HTMLInputElement).value);
    props.onAlphaChange?.(a);
  }

  // Commit a fully-formed hex from a non-drag source (hex field, RGB fields,
  // preset swatch, pipette): push it to the consumer and resync the HSV
  // controls. The drag paths use emitColor() instead (they own `interacting`).
  function applyHex(hex: string) {
    props.onChange(hex);
    const [h, s, v] = hexToHsv(hex);
    setHue(h); setSat(s); setVal(v);
    setHexInput(hex);
  }

  function onHexCommit() {
    let v = hexInput().trim();
    if (!v.startsWith('#')) v = '#' + v;
    if (/^#[0-9a-fA-F]{6}$/.test(v)) applyHex(v.toLowerCase());
    else setHexInput(props.value);
  }

  function onRgbChange(channel: number, el: HTMLInputElement) {
    const rgb = hexToRgb(props.value);
    const clamped = Math.max(0, Math.min(255, parseInt(el.value) || 0));
    rgb[channel] = clamped;
    applyHex(rgbToHex(...rgb));
    // Clamping can yield the same hex (e.g. 300 -> 255 when already 255), so
    // props.value never changes and the value={rgb()[i]} binding won't re-run,
    // leaving the out-of-range text the user typed on screen. Force the DOM
    // input to display the clamped value.
    el.value = String(clamped);
  }

  function selectPreset(hex: string) {
    applyHex(hex);
  }

  // Pipette: pick any pixel on screen via the EyeDropper API. The button is
  // rendered only when the API exists (WebView2 / Chromium), so `Ctor` is
  // present here; the guard just narrows the optional `Window.EyeDropper` type.
  async function pickFromScreen() {
    const Ctor = window.EyeDropper;
    if (!Ctor) return;
    try {
      const { sRGBHex } = await new Ctor().open();
      const hex = normalizeEyedropperHex(sRGBHex);
      if (hex) applyHex(hex);
      else logger.warn('color-picker', `eyedropper returned unparseable value: ${sRGBHex}`);
    } catch (err) {
      // AbortError → user dismissed the picker (Esc / click-away). Expected.
      if (err instanceof DOMException && err.name === 'AbortError') return;
      logger.error('color-picker', `eyedropper failed: ${String(err)}`);
      showToast(t('colorPicker.pickFailed'), 'error');
    }
  }

  const rgb = () => hexToRgb(props.value);
  const eyeDropperSupported = typeof window !== 'undefined' && typeof window.EyeDropper === 'function';

  return (
    <>
      <button ref={swatchRef} class="cp-swatch-btn" onClick={() => open() ? setOpen(false) : openPopover()}>
        <div class="cp-swatch-inner" style={{ background: props.onAlphaChange ? `rgba(${hexToRgb(props.value).join(',')},${props.alpha ?? 1})` : props.value }} />
      </button>

      <Show when={open()}>
        <Portal mount={popupMount()}>
        <div ref={popoverRef} class="cp-popover"
          style={{ left: `${popoverPos().x}px`, top: `${popoverPos().y}px` }}
          onMouseDown={e => e.stopPropagation()}>

          {/* Gradient area */}
          <div ref={gradientRef} class="cp-gradient"
            style={{ background: `hsl(${hue()}, 100%, 50%)` }}
            onMouseDown={onGradientDown}>
            <div class="cp-gradient-white" />
            <div class="cp-gradient-black" />
            <div class="cp-gradient-cursor"
              style={{ left: `${sat() * 100}%`, top: `${(1 - val()) * 100}%` }} />
          </div>

          {/* Hue slider */}
          <div class="cp-slider-row">
            <span class="cp-slider-label">H</span>
            <input type="range" class="cp-hue-slider" min="0" max="360" step="1"
              value={hue()} onInput={onHueInput} />
          </div>

          {/* Alpha slider */}
          <Show when={props.onAlphaChange}>
            <div class="cp-slider-row">
              <span class="cp-slider-label">A</span>
              <input type="range" class="cp-alpha-slider" min="0" max="1" step="0.01"
                value={props.alpha ?? 1} onInput={onAlphaInput}
                style={{
                  background: `linear-gradient(to right, transparent, ${props.value})`,
                }} />
            </div>
          </Show>

          {/* Hex + RGB inputs */}
          <div class="cp-inputs">
            <Show when={eyeDropperSupported}>
              <button type="button" class="cp-pipette" onClick={pickFromScreen}
                title={t('colorPicker.pickFromScreen')}
                aria-label={t('colorPicker.pickFromScreen')}>
                <Pipette size={14} strokeWidth={1.5} />
              </button>
            </Show>
            <input class="cp-hex-input" value={hexInput()}
              onInput={e => setHexInput(e.target.value)}
              onBlur={onHexCommit}
              onKeyDown={e => { if (e.key === 'Enter') onHexCommit(); }}
            />
            <div class="cp-rgb-group">
              <input type="number" class="cp-rgb-input" min="0" max="255"
                value={rgb()[0]} onChange={e => onRgbChange(0, e.currentTarget)} />
              <input type="number" class="cp-rgb-input" min="0" max="255"
                value={rgb()[1]} onChange={e => onRgbChange(1, e.currentTarget)} />
              <input type="number" class="cp-rgb-input" min="0" max="255"
                value={rgb()[2]} onChange={e => onRgbChange(2, e.currentTarget)} />
            </div>
          </div>
          <div class="cp-inputs">
            <Show when={eyeDropperSupported}><span class="cp-pipette-spacer" /></Show>
            <span class="cp-input-label-hex">Hex</span>
            <div class="cp-rgb-group">
              <span class="cp-input-label">R</span>
              <span class="cp-input-label">G</span>
              <span class="cp-input-label">B</span>
            </div>
          </div>

          {/* Presets */}
          <div class="cp-presets">
            {PRESETS.map(c => (
              <button class="cp-preset" style={{ background: c }}
                onClick={() => selectPreset(c)} />
            ))}
          </div>
        </div>
        </Portal>
      </Show>
    </>
  );
}
