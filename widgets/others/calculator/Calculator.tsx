import { createSignal, createMemo, For } from 'solid-js';
import { Show } from 'solid-js/web';
import { isDemo } from '../../../src/demo/invoke.ts';
import { demoCalcHistory } from '../../../src/demo/fixtures.ts';
import { Calculator as CalcIcon } from 'lucide-solid';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { safeEval } from './evalExpr.ts';
import { t } from '../../../src/i18n.ts';
import './Calculator.css';

interface CalculatorProps {
  decimalPlaces?: number;
  label?: string;
  headerMode?: HeaderMode;
}

export default function Calculator(props: CalculatorProps) {
  const decimals = () => props.decimalPlaces ?? 4;

  const [expr, setExpr] = createSignal('');
  const [history, setHistory] = createSignal<{ expr: string; result: string }[]>(
    isDemo() ? demoCalcHistory() : [],
  );

  const result = createMemo(() => {
    const e = expr();
    if (!e.trim()) return '';
    const r = safeEval(e);
    if (typeof r === 'string') return r;
    return parseFloat(r.toFixed(decimals()));
  });

  function submit() {
    const r = result();
    if (r === '' || r === 'Error') return;
    setHistory(h => [{ expr: expr(), result: String(r) }, ...h].slice(0, 8));
    setExpr('');
  }

  return (
    <div class="calc-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'none'}
        icon={CalcIcon}
        title={props.label || t('widget.calculator.label')}
      />
      <div class="widget-body calc-widget__calc">
        <div class="calc-widget__history">
          <For each={history()}>
            {(h) => (
              <div class="calc-widget__hist-item" onClick={() => setExpr(h.result.toString())}>
                <span class="calc-widget__hist-expr">{h.expr}</span>
                <span class="calc-widget__hist-result">= {h.result}</span>
              </div>
            )}
          </For>
        </div>
        <div class="calc-widget__input-area">
          <Show when={result() !== ''}>
            <div class="calc-widget__result" classList={{ error: result() === 'Error' }}>
              {result() === 'Error' ? 'Error' : `= ${result()}`}
            </div>
          </Show>
          <input
            class="calc-widget__input"
            type="text"
            value={expr()}
            onInput={(e) => setExpr(e.currentTarget.value)}
            onKeyDown={(e) => e.key === 'Enter' && submit()}
            placeholder="2 + 3 * 4"
            spellcheck={false}
          />
        </div>
      </div>
    </div>
  );
}
