// Calculator expression evaluator.
//
// [S-07]/[S-08] regression: the widget runs under the app CSP
// (`script-src 'self' 'wasm-unsafe-eval'`), which blocks JS `eval` /
// `new Function`. The old implementation evaluated via `new Function(...)`,
// so EVERY expression threw a CSP violation and surfaced as "Error" in the
// bundled app (broken since v0.12.0). The CSP-simulation test below stubs the
// `Function` constructor to throw, reproducing that failure mode in node.
import { describe, it, expect } from 'vitest';
import { safeEval } from './evalExpr.ts';

describe('calculator: safeEval [S-07]/[S-08]', () => {
  it('[S-07] evaluates WITHOUT the Function constructor (CSP blocks unsafe-eval)', () => {
    const RealFunction = globalThis.Function;
    // Simulate the webview CSP: any use of the Function constructor throws.
    globalThis.Function = function () {
      throw new EvalError('CSP: unsafe-eval blocked');
    } as unknown as FunctionConstructor;
    try {
      expect(safeEval('2+3')).to.equal(5);
      expect(safeEval('10*4')).to.equal(40);
    } finally {
      globalThis.Function = RealFunction;
    }
  });

  it('[S-08] add / subtract / multiply / divide', () => {
    expect(safeEval('2+3')).to.equal(5);
    expect(safeEval('10-4')).to.equal(6);
    expect(safeEval('10*4')).to.equal(40);
    expect(safeEval('20/5')).to.equal(4);
  });

  it('[S-08] respects operator precedence and parentheses', () => {
    expect(safeEval('2+3*4')).to.equal(14);
    expect(safeEval('(2+3)*4')).to.equal(20);
    expect(safeEval('2*3+4*5')).to.equal(26);
  });

  it('[S-08] unary minus and plus', () => {
    expect(safeEval('-5')).to.equal(-5);
    expect(safeEval('3*-2')).to.equal(-6);
    expect(safeEval('-(2+3)')).to.equal(-5);
    expect(safeEval('+7')).to.equal(7);
  });

  it('[S-07] power (^) is right-associative', () => {
    expect(safeEval('2^10')).to.equal(1024);
    expect(safeEval('2^3^2')).to.equal(512); // 2^(3^2) = 2^9
  });

  it('[S-07] percent maps to /100', () => {
    expect(safeEval('50%')).to.equal(0.5);
    expect(safeEval('200%')).to.equal(2);
  });

  it('[S-08] decimals', () => {
    expect(safeEval('1.5+2.5')).to.equal(4);
    expect(safeEval('.5*2')).to.equal(1);
  });

  it('[S-04] empty / whitespace-only yields empty string', () => {
    expect(safeEval('')).to.equal('');
    expect(safeEval('   ')).to.equal('');
  });

  it('[S-08] non-finite (divide by zero) surfaces Error', () => {
    expect(safeEval('1/0')).to.equal('Error');
    expect(safeEval('0/0')).to.equal('Error');
  });

  it('[S-08] malformed expressions surface Error, never throw', () => {
    expect(safeEval('2+')).to.equal('Error');
    expect(safeEval('*3')).to.equal('Error');
    expect(safeEval('(2+3')).to.equal('Error');
    expect(safeEval('2 3')).to.equal('Error');
  });
});
