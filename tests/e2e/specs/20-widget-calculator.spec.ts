/**
 * Calculator widget.
 *
 * Covers input -> Enter -> result + history, history click reloads result,
 * and graceful divide-by-zero handling.
 *
 * Behavioral notes (from widgets/others/calculator/Calculator.tsx):
 *  - root:     `.calc-widget`
 *  - input:    `.calc-widget__input`  (onKeyDown Enter -> submit())
 *  - result:   `.calc-widget__result` (shown only when expr non-empty;
 *                                      displays "= <n>" or "Error")
 *  - history:  `.calc-widget__hist-item`, each has an `.calc-widget__hist-expr`
 *              and `.calc-widget__hist-result`. Clicking loads the *result*
 *              into the expression input (per source: setExpr(h.result)).
 *  - bad math / 1/0: safeEval returns the string "Error".
 */
import { expect } from 'chai';
import { browser, $, $$ } from '@wdio/globals';
import {
  resetAppState, pickWidget, clickGridCell, waitForPersist,
  configModalIsOpen, closeConfigModal, leaveEditMode,
} from '../helpers/app.js';

async function typeExpression(expr: string): Promise<void> {
  const input = await $('.calc-widget__input');
  await input.click();
  // setValue clears+types, mimicking a user entering a fresh expression.
  await input.setValue(expr);
}

describe('widget: Calculator', () => {
  beforeEach(async () => {
    await resetAppState();
    await pickWidget('Calculator');
    await clickGridCell(2, 2);
    await waitForPersist();
    if (await configModalIsOpen()) await closeConfigModal();
    // Leave edit mode so .widget.edit overlay does not intercept input clicks.
    await leaveEditMode();
    // Belt-and-braces: force-clear edit mode at the storage level in case the
    // Ctrl+E hotkey did not land (OS focus on a closing config window).
    await browser.execute(() => {
      const k = 'themia-edit-mode';
      localStorage.setItem(k, 'false');
      window.dispatchEvent(new StorageEvent('storage', { key: k, newValue: 'false' }));
    });
    await browser.waitUntil(async () => {
      return !(await $('.widget.edit').isExisting());
    }, { timeout: 3_000, timeoutMsg: '.widget.edit overlay still present' });
    await $('.calc-widget').waitForDisplayed({ timeout: 3_000 });
  });

  it('evaluates 2+3 -> = 5 and pressing Enter records history', async () => {
    await typeExpression('2+3');
    // Result memo is reactive: should render "= 5" before hitting Enter.
    await browser.waitUntil(async () => {
      const el = await $('.calc-widget__result');
      if (!(await el.isExisting())) return false;
      return (await el.getText()).includes('5');
    }, { timeout: 2_000, timeoutMsg: 'result did not render' });

    const result = await $('.calc-widget__result').getText();
    expect(result).to.match(/=\s*5/);

    // Commit to history.
    await browser.keys(['Enter']);

    // History should now have one entry containing "2+3" and "= 5".
    await browser.waitUntil(async () => (await $$('.calc-widget__hist-item').length) >= 1, {
      timeout: 2_000, timeoutMsg: 'history row not recorded',
    });
    const histExpr = await $('.calc-widget__hist-item .calc-widget__hist-expr').getText();
    const histRes  = await $('.calc-widget__hist-item .calc-widget__hist-result').getText();
    expect(histExpr).to.equal('2+3');
    expect(histRes).to.match(/=\s*5/);
  });

  it('clicking a history item reloads it into the input', async () => {
    await typeExpression('10*4');
    await browser.keys(['Enter']);
    await browser.waitUntil(async () => (await $$('.calc-widget__hist-item').length) >= 1, {
      timeout: 2_000,
    });

    // Input is cleared after submit.
    const input = await $('.calc-widget__input');
    expect(await input.getValue()).to.equal('');

    // Click the history row. Per Calculator.tsx it loads `h.result` (= "40")
    // back into the input, not the expression. Assert accordingly.
    await $('.calc-widget__hist-item').click();
    await browser.waitUntil(async () => {
      const v = await input.getValue();
      return v !== '';
    }, { timeout: 2_000, timeoutMsg: 'input did not reload' });
    expect(await input.getValue()).to.equal('40');
  });

  it('divide by zero does not crash, shows Error', async () => {
    await typeExpression('1/0');
    // Result memo should surface "Error" string (isFinite check in safeEval).
    await browser.waitUntil(async () => {
      const el = await $('.calc-widget__result');
      if (!(await el.isExisting())) return false;
      return (await el.getText()).toLowerCase().includes('error');
    }, { timeout: 2_000, timeoutMsg: 'Error text did not surface for 1/0' });

    const classes = await $('.calc-widget__result').getAttribute('class');
    expect(classes).to.include('error');

    // Widget itself is still alive.
    expect(await $('.calc-widget').isDisplayed()).to.equal(true);
  });
});
