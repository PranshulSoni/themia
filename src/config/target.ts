// Shared selector for the unified config window. A `ConfigTarget` identifies
// one node in the config tree (an app-settings page, a view, a widget, or
// the Add Widget page). It is colon-encoded into a string for the window URL
// param and the `config-navigate` Tauri event payload.

export type AppSettingsPage = 'general' | 'style' | 'animations' | 'license' | 'accounts' | 'updates' | 'logs';

export type ConfigTarget =
  | { kind: 'app'; page: AppSettingsPage }
  | { kind: 'view'; viewId: string }
  | { kind: 'display'; monitorId: string }
  | { kind: 'widget'; widgetId: string }
  /** The Add Widget page. `into` names a container widget: the picked widget is
   *  created inside it instead of going through click-to-place ([S-33]).
   *  `toTray` sends it to the tray collection instead (Tray.SPEC [S-01]); the
   *  two destinations are mutually exclusive. */
  | { kind: 'add'; into?: string; toTray?: boolean };

const APP_PAGES: AppSettingsPage[] = ['general', 'style', 'animations', 'license', 'accounts', 'updates', 'logs'];

/** Stands in for a container id in an `add:` selector to mean "the tray". Safe
 *  as a sentinel because a widget id is always `<type>-<millis>-<seq>`. */
const TRAY_SENTINEL = '@tray';

export function encodeTarget(t: ConfigTarget): string {
  switch (t.kind) {
    case 'app':     return `app:${t.page}`;
    case 'view':    return `view:${t.viewId}`;
    case 'display': return `display:${t.monitorId}`;
    case 'widget':  return `widget:${t.widgetId}`;
    case 'add':
      if (t.toTray) return `add:${TRAY_SENTINEL}`;
      return t.into ? `add:${t.into}` : 'add';
  }
}

/**
 * The screen name this target reports to analytics ([S-23]).
 *
 * Deliberately NOT `encodeTarget`: view, display and widget ids are per-install
 * values (a monitor id is derived from the device name), and a view's name is
 * whatever the user typed. Only the closed sets survive — the settings page and
 * the widget TYPE, which every id carries as its `<type>-<millis>-<seq>` prefix.
 */
export function analyticsPath(t: ConfigTarget): string {
  switch (t.kind) {
    case 'app':     return `settings/${t.page}`;
    case 'view':    return 'view';
    case 'display': return 'display';
    case 'widget': {
      const type = t.widgetId.split('-')[0];
      return type ? `widget/${type}` : 'widget';
    }
    case 'add':     return 'add-widget';
  }
}

// Tolerant parse: returns null on missing/garbage input so callers can fall
// back to the default node.
export function parseTarget(raw: string | null | undefined): ConfigTarget | null {
  if (!raw) return null;
  if (raw === 'add') return { kind: 'add' };
  const sep = raw.indexOf(':');
  if (sep < 0) return null;
  const kind = raw.slice(0, sep);
  const rest = raw.slice(sep + 1);
  if (!rest) return null;
  switch (kind) {
    case 'app':
      return APP_PAGES.includes(rest as AppSettingsPage)
        ? { kind: 'app', page: rest as AppSettingsPage }
        : null;
    case 'add':
      return rest === TRAY_SENTINEL ? { kind: 'add', toTray: true } : { kind: 'add', into: rest };
    case 'view':    return { kind: 'view', viewId: rest };
    case 'display': return { kind: 'display', monitorId: rest };
    case 'widget':  return { kind: 'widget', widgetId: rest };
    default:        return null;
  }
}
