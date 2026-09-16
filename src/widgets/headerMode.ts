// Header-mode types + resolution, kept in a standalone (lucide/tauri-free)
// module so the resolver is node-importable for unit tests. registry.tsx
// re-exports these, so widgets keep importing from '../widgets/registry.tsx'.
// See src/components/WidgetHeader.SPEC.md.

/** Closed set of header presentations every widget supports ([S-01]). */
export type HeaderMode = 'normal' | 'compact' | 'none';

const HEADER_MODES: readonly HeaderMode[] = ['normal', 'compact', 'none'];

export function isHeaderMode(v: unknown): v is HeaderMode {
  return typeof v === 'string' && (HEADER_MODES as readonly string[]).includes(v);
}

interface HeaderModeDef {
  defaultHeaderMode?: HeaderMode;
  noHeader?: boolean;
}

/** Resolve a widget's effective header mode ([S-15]/[S-16]). Single source of
 *  truth for both the live grid render and the config UI, so the selector and
 *  the rendered widget never disagree. Pure read, no rewrite of stored config
 *  (user data, incl. legacy `hideHeader`, is never deleted). */
export function resolveHeaderMode(
  config: Record<string, unknown> | undefined,
  def: HeaderModeDef,
): HeaderMode {
  if (def.noHeader) return 'none';
  const raw = config?.headerMode;
  if (isHeaderMode(raw)) return raw;
  if (raw !== undefined) {
    console.warn('[widget-header] ignoring out-of-set headerMode:', raw);
  }
  if (config?.hideHeader === true) return 'none';
  return def.defaultHeaderMode ?? 'normal';
}
