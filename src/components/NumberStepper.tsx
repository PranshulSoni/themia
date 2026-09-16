import { Component, createSignal, onCleanup } from 'solid-js';
import { Minus, Plus } from 'lucide-solid';
import './NumberStepper.css';
import { t } from '../i18n.ts';

interface NumberStepperProps {
  value: number;
  min: number;
  /** Upper bound; omit for an unbounded control (no cap, plus never disabled). */
  max?: number;
  step?: number;
  unit?: string;
  width?: number;
  precision?: number;
  onChange: (v: number) => void;
}

function inferPrecision(step: number): number {
  if (step >= 1) return 0;
  const s = String(step);
  const dot = s.indexOf('.');
  return dot === -1 ? 0 : s.length - dot - 1;
}

function clamp(v: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, v));
}

function round(v: number, precision: number): number {
  const f = Math.pow(10, precision);
  return Math.round(v * f) / f;
}

const NumberStepper: Component<NumberStepperProps> = (props) => {
  const step = () => props.step ?? 1;
  const precision = () => props.precision ?? inferPrecision(step());
  const [draft, setDraft] = createSignal<string | null>(null);

  let repeatTimer: number | null = null;
  let initialTimer: number | null = null;

  const cancelRepeat = () => {
    if (initialTimer !== null) { clearTimeout(initialTimer); initialTimer = null; }
    if (repeatTimer !== null) { clearInterval(repeatTimer); repeatTimer = null; }
  };
  onCleanup(cancelRepeat);

  const max = () => props.max ?? Infinity;

  const apply = (delta: number) => {
    const next = round(clamp(props.value + delta, props.min, max()), precision());
    if (next !== props.value) props.onChange(next);
  };

  const startRepeat = (delta: number) => {
    apply(delta);
    initialTimer = window.setTimeout(() => {
      repeatTimer = window.setInterval(() => apply(delta), 50);
    }, 250);
  };

  const commitInput = (raw: string) => {
    setDraft(null);
    const parsed = parseFloat(raw);
    if (Number.isNaN(parsed)) return;
    const next = round(clamp(parsed, props.min, max()), precision());
    if (next !== props.value) props.onChange(next);
  };

  const display = () => {
    const d = draft();
    if (d !== null) return d;
    return precision() === 0 ? String(Math.round(props.value)) : props.value.toFixed(precision());
  };

  const atMin = () => props.value <= props.min;
  const atMax = () => props.value >= max();

  return (
    <div class="number-stepper">
      <button
        type="button"
        class="ns-btn ns-minus"
        disabled={atMin()}
        aria-label={t('widgetUi.decrement')}
        onPointerDown={(e) => {
          if (atMin()) return;
          e.preventDefault();
          (e.currentTarget as HTMLButtonElement).setPointerCapture(e.pointerId);
          startRepeat(-step());
        }}
        onPointerUp={cancelRepeat}
        onPointerLeave={cancelRepeat}
        onPointerCancel={cancelRepeat}
      >
        <Minus size={12} strokeWidth={1.5} />
      </button>
      <input
        type="number"
        class="ns-input"
        classList={{ 'ns-input--no-unit': !props.unit }}
        style={{ width: `${props.width ?? 96}px` }}
        min={props.min}
        max={props.max}
        step={step()}
        value={display()}
        onFocus={(e) => e.currentTarget.select()}
        onClick={(e) => {
          const el = e.currentTarget as HTMLInputElement;
          if (el.selectionStart === el.selectionEnd) el.select();
        }}
        onInput={(e) => setDraft(e.currentTarget.value)}
        onBlur={(e) => commitInput(e.currentTarget.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            commitInput((e.currentTarget as HTMLInputElement).value);
            (e.currentTarget as HTMLInputElement).blur();
          } else if (e.key === 'Escape') {
            setDraft(null);
            (e.currentTarget as HTMLInputElement).blur();
          }
        }}
      />
      <span class="ns-unit">{props.unit ?? ''}</span>
      <button
        type="button"
        class="ns-btn ns-plus"
        disabled={atMax()}
        aria-label={t('widgetUi.increment')}
        onPointerDown={(e) => {
          if (atMax()) return;
          e.preventDefault();
          (e.currentTarget as HTMLButtonElement).setPointerCapture(e.pointerId);
          startRepeat(step());
        }}
        onPointerUp={cancelRepeat}
        onPointerLeave={cancelRepeat}
        onPointerCancel={cancelRepeat}
      >
        <Plus size={12} strokeWidth={1.5} />
      </button>
    </div>
  );
};

export default NumberStepper;
