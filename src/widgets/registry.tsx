import type { Component, JSX } from 'solid-js';
import type { CtxMenuIconProps } from '../ctxMenuStore.ts';
import { resolveHeaderMode, isHeaderMode, type HeaderMode } from './headerMode.ts';
import { t } from '../i18n.ts';

export type CtxIcon = Component<CtxMenuIconProps> | string;

// Office
import folder from '../../widgets/office/folder/index.tsx';
import email from '../../widgets/office/email/index.tsx';
import contacts from '../../widgets/office/contacts/index.tsx';
import calendar from '../../widgets/office/calendar/index.tsx';
import todo from '../../widgets/office/todo/index.tsx';
import onedrive from '../../widgets/office/onedrive/index.tsx';

// Online
import github from '../../widgets/online/github/index.tsx';
import githubNotifications from '../../widgets/online/github-notifications/index.tsx';
import githubReviewRequested from '../../widgets/online/github-review-requested/index.tsx';
import githubAssignedIssues from '../../widgets/online/github-assigned-issues/index.tsx';
import weatherCurrent from '../../widgets/online/weather-current/index.tsx';
import weatherForecast from '../../widgets/online/weather-forecast/index.tsx';
import rss from '../../widgets/online/rss/index.tsx';
import stocks from '../../widgets/online/stocks/index.tsx';
import webElement from '../../widgets/online/web-element/index.tsx';
import videoStream from '../../widgets/online/video-stream/index.tsx';
import claudeUsage from '../../widgets/online/claude-usage/index.tsx';

// System
import cpu from '../../widgets/system/cpu/index.tsx';
import screentime from '../../widgets/system/screentime/index.tsx';
import ram from '../../widgets/system/ram/index.tsx';
import gpu from '../../widgets/system/gpu/index.tsx';
import net from '../../widgets/system/net/index.tsx';
import disk from '../../widgets/system/disk/index.tsx';
import ping from '../../widgets/system/ping/index.tsx';
import volume from '../../widgets/system/volume/index.tsx';
import mixer from '../../widgets/system/mixer/index.tsx';
import power from '../../widgets/system/power/index.tsx';
import battery from '../../widgets/system/battery/index.tsx';
import bluetooth from '../../widgets/system/bluetooth/index.tsx';

// Others
import clock from '../../widgets/others/clock/index.tsx';
import apps from '../../widgets/others/apps/index.tsx';
import shortcut from '../../widgets/others/shortcut/index.tsx';
import music from '../../widgets/others/music/index.tsx';
import notes from '../../widgets/others/notes/index.tsx';
import calculator from '../../widgets/others/calculator/index.tsx';
import gallery from '../../widgets/others/gallery/index.tsx';
import help from '../../widgets/others/help/index.tsx';
import themia from '../../widgets/others/themia/index.tsx';
import colorSwatch from '../../widgets/others/color-swatch/index.tsx';
import countdown from '../../widgets/others/countdown/index.tsx';
import viewSwitcher from '../../widgets/others/view-switcher/index.tsx';
import webSearch from '../../widgets/others/web-search/index.tsx';
import habitTracker from '../../widgets/others/habit-tracker/index.tsx';
import moodTracker from '../../widgets/others/mood-tracker/index.tsx';
import pomodoro from '../../widgets/others/pomodoro/index.tsx';
import scratchpad from '../../widgets/others/scratchpad/index.tsx';
import quote from '../../widgets/others/quote/index.tsx';
import sunMoon from '../../widgets/others/sun-moon/index.tsx';
import clipboard from '../../widgets/others/clipboard/index.tsx';
import stack from '../../widgets/others/stack/index.tsx';
import tabs from '../../widgets/others/tabs/index.tsx';
import group from '../../widgets/others/group/index.tsx';

// ── Types ────────────────────────────────────────────────────────────────────

export interface ContextMenuItem {
  icon?: CtxIcon;
  label?: string;
  action?: () => void;
  active?: boolean;
  disabled?: boolean;
  danger?: boolean;
  sep?: boolean;
  header?: boolean;
  children?: ContextMenuItem[];
  shortcut?: string;
}

/** A widget's persisted settings: an open, heterogeneous key/value bag. Values
 *  are `unknown` so reads are narrowed at the use site rather than trusted. */
export type WidgetConfig = Record<string, unknown>;

// Header-mode types + resolver live in a lucide/tauri-free module so the
// resolver stays node-importable for unit tests; re-exported here so widgets and
// UI keep importing them from '../widgets/registry.tsx'.
export { resolveHeaderMode, isHeaderMode };
export type { HeaderMode };

export type ViewActionsFn = (
  config: WidgetConfig,
  setConfig: (partial: WidgetConfig) => void,
  key: string,
  views: [string, string, CtxIcon][],
) => ContextMenuItem[];

export interface WidgetDefinition {
  // A widget registry is heterogeneous: each widget registers a display + a
  // config component with its OWN, mutually-incompatible props interface (a
  // config component takes `{ config, onChange }`; a display component takes the
  // spread config fields). There is no single non-`any` supertype for all of
  // them, so these two fields are the one legitimate type-erasure boundary in
  // the app; render sites narrow to the concrete props. Typing them as anything
  // stricter would force unsafe casts that LIE about types in 40+ widget files,
  // which is worse; `any` is the correct exception here.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  component: Component<any>;
  /** Optional: a widget with no user-configurable options omits this, and
   *  ConfigWindow renders the shared "no options" placeholder instead. Widgets
   *  used to be forced to supply one, so the two option-less widgets each
   *  hand-rolled that placeholder -- one of them untranslated. */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  configComponent?: Component<any>;
  label: string;
  /** One-line, plain-language summary shown in the Add-Widget picker list. */
  description?: string;
  icon: () => JSX.Element;
  category: string;
  providers?: string[];
  /** Default size in PIXELS. Converted to a fraction of the current screen at
   *  placement time (src/SPEC.md [S-10]). */
  defaultSize: { width: number; height: number };
  defaultConfig: WidgetConfig;
  noHeader?: boolean;
  /** Suppress the grid-level `.widget` chrome (border/background), for
   *  container widgets that must paint nothing themselves (stack [S-07]). */
  noChrome?: boolean;
  /** Offer the per-instance "Transparent background" toggle (src/SPEC [S-32]).
   *  Opt in only for glanceable widgets whose content stands on its own over a
   *  wallpaper (a clock, a temperature, an icon grid); dense list/editing
   *  widgets need their card surface to stay readable, so they omit it. The
   *  flag gates the CONFIG ROW and the rendering: dropping it later makes the
   *  widget opaque again rather than leaving a stale invisible instance. */
  canTransparent?: boolean;
  /** Hidden from the Add-Widget picker unless `globalDesign.devMode` is on.
   *  Existing instances always render; the flag gates DISCOVERY only, never
   *  user data (stack [S-10]). */
  devOnly?: boolean;
  /** Header mode applied when a widget instance has no explicit `headerMode`
   *  (WidgetHeader.SPEC [S-12]). Defaults to `'normal'` when omitted. */
  defaultHeaderMode?: HeaderMode;
  /** Opt in to the "Open in window" chrome action, which pops the widget out
   *  into its own decorated OS window (`open_widget_window` → WidgetWindow).
   *  Suited to content/editing widgets that benefit from a resizable, focused
   *  workspace; omit for glanceable/ambient widgets. */
  popOut?: boolean;
  contextActions?: (
    config: WidgetConfig,
    setConfig: (partial: WidgetConfig) => void,
    viewActions: ViewActionsFn,
  ) => ContextMenuItem[];
}

// ── Helpers ──────────────────────────────────────────────────────────────────

export function viewActions(
  config: WidgetConfig,
  setConfig: (partial: WidgetConfig) => void,
  key: string,
  views: [string, string, CtxIcon][],
): ContextMenuItem[] {
  const cur = config[key] ?? views[0][0];
  return [
    { header: true, label: t('ctxMenu.view') },
    ...views.map(([val, label, icon]) => ({
      icon, label,
      active: cur === val,
      action: () => setConfig({ [key]: val }),
    })),
  ];
}

// ── Registry ─────────────────────────────────────────────────────────────────

const registry: Record<string, WidgetDefinition> = {
  folder,
  email,
  contacts,
  calendar,
  todo,
  onedrive,
  clock,
  github,
  githubNotifications,
  githubReviewRequested,
  githubAssignedIssues,
  rss,
  stocks,
  webElement,
  videoStream,
  claudeUsage,
  music,

  cpu,
  ram,
  gpu,
  net,
  disk,
  ping,
  battery,
  bluetooth,
  screentime,
  apps,
  weatherCurrent,
  weatherForecast,
  shortcut,
  notes,
  calculator,
  gallery,
  help,
  themia,
  colorSwatch,
  countdown,
  viewSwitcher,

  webSearch,
  habitTracker,
  moodTracker,
  pomodoro,
  scratchpad,
  quote,
  sunMoon,
  volume,
  mixer,
  power,
  clipboard,
  stack,
  tabs,
  group,
};

export default registry;
