# Calculator: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Renders a scrollable history list above an expression input.
- [S-02] Each history row displays the original expression and its `= result`.
- [S-03] While typing, shows a live preview of the evaluated result as `= N` above the input; renders `Error` in an error style when evaluation fails.
- [S-04] Empty expression hides the result preview.

## Configuration
- [S-05] `decimalPlaces`: segmented control with options 2 / 4 / 6 / 8, applied via `toFixed` on the live result.
- [S-06] `hideHeader` toggle hides the widget title bar.

## Data / backend
- [S-07] Expression evaluation sanitizes input to characters `0-9+-*/().,%^ `, drops thousands commas, maps `%` → `/100`, and evaluates a small arithmetic grammar (`+ - * /`, `^` power right-assoc, unary sign, parentheses, decimals) with a self-contained recursive-descent parser. It must NOT use `eval` / `new Function`: the app CSP (`script-src 'self' 'wasm-unsafe-eval'`) blocks unsafe-eval, so a `new Function` evaluator throws for every expression in the bundled app. See `evalExpr.ts` / `evalExpr.test.ts`.
- [S-08] Non-finite or non-numeric results surface as the string `Error`.
- [S-09] History is in-memory only, capped at 8 most-recent entries.
- [S-12] In demo / picker-preview mode (`isDemo()`), history seeds with a few realistic past calculations (newest-first) so the preview isn't blank.

## Interactions
- [S-10] Pressing Enter commits the current expression to history (unless empty or Error) and clears the input.
- [S-11] Clicking a history row populates the input with that row's numeric result (stringified).
