/**
 * Interpolate `{name}` placeholders in a translation string.
 *
 * Single-pass, with a FIXED regex (never built from caller input), unlike the
 * previous per-param `new RegExp(\`\\{${p}\\}\`)`, which threw on param names
 * containing regex metacharacters and could re-substitute a value that itself
 * looked like a placeholder (order-dependent). Here each `{token}` is resolved
 * exactly once against `params`; unknown tokens are left untouched.
 */
export function interpolate(
  template: string,
  params?: Record<string, string | number>,
): string {
  if (!params) return template;
  return template.replace(/\{(\w+)\}/g, (match, key: string) =>
    Object.prototype.hasOwnProperty.call(params, key) ? String(params[key]) : match,
  );
}
