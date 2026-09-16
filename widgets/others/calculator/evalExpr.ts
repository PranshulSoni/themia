// Calculator expression evaluator.
//
// Evaluates a small arithmetic grammar (+ - * / ^, parentheses, unary sign,
// percent, decimals) WITHOUT `eval` / `new Function`, so it works under the
// app CSP (`script-src 'self' 'wasm-unsafe-eval'`, which blocks unsafe-eval).
// The previous `new Function(...)` implementation threw a CSP violation for
// EVERY expression in the bundled app, surfacing as "Error". See
// evalExpr.test.ts for the CSP regression guard. [S-07]/[S-08]
//
// Precedence (low -> high): + -  <  * /  <  unary +/-  <  ^ (right-assoc).

type Tok =
  | { t: 'num'; v: number }
  | { t: 'op'; v: '+' | '-' | '*' | '/' | '^' }
  | { t: 'lp' }
  | { t: 'rp' };

function tokenize(s: string): Tok[] {
  const toks: Tok[] = [];
  let i = 0;
  while (i < s.length) {
    const c = s[i];
    if (c === ' ') { i++; continue; }
    if (c === '(') { toks.push({ t: 'lp' }); i++; continue; }
    if (c === ')') { toks.push({ t: 'rp' }); i++; continue; }
    if (c === '+' || c === '-' || c === '*' || c === '/' || c === '^') {
      toks.push({ t: 'op', v: c });
      i++;
      continue;
    }
    if ((c >= '0' && c <= '9') || c === '.') {
      let j = i;
      let dot = false;
      while (j < s.length && ((s[j] >= '0' && s[j] <= '9') || (s[j] === '.' && !dot))) {
        if (s[j] === '.') dot = true;
        j++;
      }
      const n = Number(s.slice(i, j));
      if (!isFinite(n)) throw new Error('bad number');
      toks.push({ t: 'num', v: n });
      i = j;
      continue;
    }
    throw new Error('unexpected char');
  }
  return toks;
}

// Recursive-descent parser that evaluates as it descends.
class Parser {
  private p = 0;
  constructor(private readonly toks: Tok[]) {}

  private peek(): Tok | undefined { return this.toks[this.p]; }
  private next(): Tok | undefined { return this.toks[this.p++]; }

  parse(): number {
    const v = this.expr();
    if (this.p !== this.toks.length) throw new Error('trailing tokens');
    return v;
  }

  private expr(): number {
    let v = this.term();
    for (;;) {
      const t = this.peek();
      if (t && t.t === 'op' && (t.v === '+' || t.v === '-')) {
        this.next();
        const r = this.term();
        v = t.v === '+' ? v + r : v - r;
      } else break;
    }
    return v;
  }

  private term(): number {
    let v = this.factor();
    for (;;) {
      const t = this.peek();
      if (t && t.t === 'op' && (t.v === '*' || t.v === '/')) {
        this.next();
        const r = this.factor();
        v = t.v === '*' ? v * r : v / r;
      } else break;
    }
    return v;
  }

  // Unary +/- binds tighter than * / but looser than ^.
  private factor(): number {
    const t = this.peek();
    if (t && t.t === 'op' && (t.v === '+' || t.v === '-')) {
      this.next();
      const r = this.factor();
      return t.v === '-' ? -r : r;
    }
    return this.power();
  }

  // Right-associative power; a unary exponent (2^-2) is allowed via factor().
  private power(): number {
    const base = this.primary();
    const t = this.peek();
    if (t && t.t === 'op' && t.v === '^') {
      this.next();
      return Math.pow(base, this.factor());
    }
    return base;
  }

  private primary(): number {
    const t = this.next();
    if (!t) throw new Error('unexpected end');
    if (t.t === 'num') return t.v;
    if (t.t === 'lp') {
      const v = this.expr();
      const close = this.next();
      if (!close || close.t !== 'rp') throw new Error('missing )');
      return v;
    }
    throw new Error('unexpected token');
  }
}

function safeEval(expr: string): number | string {
  const sanitised = expr.replace(/[^0-9+\-*/().,%^ ]/g, '');
  if (!sanitised.trim()) return '';
  // Drop thousands commas; map percent to /100 (matches the historical [S-07]
  // substitution intent).
  const normalised = sanitised.replace(/,/g, '').replace(/%/g, '/100');
  try {
    const toks = tokenize(normalised);
    if (toks.length === 0) return '';
    const result = new Parser(toks).parse();
    if (typeof result !== 'number' || !isFinite(result)) return 'Error';
    return result;
  } catch {
    return 'Error';
  }
}

export { safeEval };
