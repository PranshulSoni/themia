import { For } from 'solid-js';
import { Minus, Plus } from 'lucide-solid';
import { aspects, previewSpanFor, setPreviewAspect } from '../demo/previewAspects.ts';
import type { PreviewSpan } from '../demo/previewAspects.ts';
import './DevAspectPanel.css';

// DEV-ONLY tuner shown under the picker preview. Lets you snap the currently
// previewed widget onto one of a few shared aspect ratios (or nudge cols/rows),
// writing the choice straight into previewAspects.json (see setPreviewAspect).

interface DevAspectPanelProps {
  type: string;
  /** The widget's grid defaultSpan, used when it has no tuned override yet. */
  fallback: PreviewSpan;
}

// A small palette of canonical aspect ratios to snap onto. Keep this short;
// the whole point is to share a few ratios across many widgets.
const PRESETS: { label: string; cols: number; rows: number }[] = [
  { label: '1:1',  cols: 2, rows: 2 },
  { label: '4:3',  cols: 4, rows: 3 },
  { label: '3:4',  cols: 3, rows: 4 },
  { label: '3:2',  cols: 3, rows: 2 },
  { label: '2:3',  cols: 2, rows: 3 },
  { label: '2:1',  cols: 2, rows: 1 },
  { label: '1:2',  cols: 1, rows: 2 },
];

export default function DevAspectPanel(props: DevAspectPanelProps) {
  // `aspects[type]` makes this reactive to writes for the current widget.
  const span = () => (aspects[props.type] as PreviewSpan | undefined) ?? previewSpanFor(props.type, props.fallback);
  const isActive = (p: { cols: number; rows: number }) =>
    span().cols === p.cols && span().rows === p.rows;

  const set = (s: PreviewSpan) =>
    setPreviewAspect(props.type, { cols: Math.max(1, s.cols), rows: Math.max(1, s.rows) });

  const nudge = (axis: 'cols' | 'rows', delta: number) => {
    const cur = span();
    set({ cols: cur.cols, rows: cur.rows, [axis]: cur[axis] + delta });
  };

  return (
    <div class="dev-aspect-panel">
      <div class="dev-aspect-head">
        <span class="dev-aspect-title">Preview aspect</span>
        <span class="dev-aspect-badge">dev</span>
        <span class="dev-aspect-current">{span().cols}×{span().rows}</span>
      </div>

      <div class="dev-aspect-presets">
        <For each={PRESETS}>
          {(p) => (
            <button
              class="dev-aspect-preset"
              classList={{ active: isActive(p) }}
              onClick={() => set(p)}
            >
              {p.label}
            </button>
          )}
        </For>
      </div>

      <div class="dev-aspect-steppers">
        <div class="dev-aspect-stepper">
          <span class="dev-aspect-axis">cols</span>
          <button onClick={() => nudge('cols', -1)} disabled={span().cols <= 1}><Minus size={12} strokeWidth={2} /></button>
          <span class="dev-aspect-val">{span().cols}</span>
          <button onClick={() => nudge('cols', +1)}><Plus size={12} strokeWidth={2} /></button>
        </div>
        <div class="dev-aspect-stepper">
          <span class="dev-aspect-axis">rows</span>
          <button onClick={() => nudge('rows', -1)} disabled={span().rows <= 1}><Minus size={12} strokeWidth={2} /></button>
          <span class="dev-aspect-val">{span().rows}</span>
          <button onClick={() => nudge('rows', +1)}><Plus size={12} strokeWidth={2} /></button>
        </div>
      </div>

      <div class="dev-aspect-hint">writes to previewAspects.json</div>
    </div>
  );
}
