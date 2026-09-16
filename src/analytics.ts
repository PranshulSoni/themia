/**
 * Frontend half of anonymous usage analytics: the transport.
 *
 * TelemetryDeck ships no Rust SDK, so their official `@telemetrydeck/sdk` runs
 * here in the webview. Consent, cadence and the content of every signal
 * deliberately stay in the backend — this module can only send what
 * `analytics_plan` / `analytics_track` hands it, so a fault in the frontend
 * cannot re-enable a user who opted out, nor attach anything to a signal.
 * See docs/ANALYTICS.md.
 *
 * The one thing the frontend contributes is `collectEnv()`: the handful of
 * facts only a webview can see (screen, locale, time zone, colour scheme),
 * which the backend merges into TelemetryDeck's default parameters ([S-18]).
 *
 * Delivery markers are persisted by the backend and ONLY on a confirmed success
 * response, so a failed send is retried on the next launch rather than lost.
 */
import TelemetryDeck from '@telemetrydeck/sdk';
import { invoke } from './ipc.ts';
import type {
  AnalyticsEnv,
  AnalyticsErrorCategory,
  AnalyticsEvent,
  AnalyticsPlan,
  AnalyticsSignal,
  AnalyticsTrackedEvent,
} from './ipc.ts';
import { lang } from './i18n.ts';
import { logger, setErrorSink } from './debugLog.ts';

/** Sends one signal; resolves true only on a success response. */
export type SignalFn = (signal: AnalyticsSignal) => Promise<boolean>;

/**
 * The webview-only facts that go on every signal ([S-18]).
 *
 * Everything here is a property of the machine or its settings, never of the
 * user: no window contents, no account, no path. A value the browser won't give
 * us is left out, so the backend drops the parameter instead of sending "".
 */
export function collectEnv(): AnalyticsEnv {
  const env: AnalyticsEnv = {};

  const locale = navigator.language;
  if (locale) {
    env.locale = locale;
    // `de-CH` → language `de`, region `CH`. Intl.Locale is available in
    // WebView2; a malformed tag must not take startup down.
    try {
      const parsed = new Intl.Locale(locale);
      if (parsed.language) env.preferredLanguage = parsed.language;
      if (parsed.region) env.region = parsed.region;
    } catch {
      // ignore: an unparseable tag just means no language/region breakdown.
    }
  }
  // Themia's own UI language, which the user can set independently of Windows.
  env.appLanguage = lang();

  try {
    const tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
    if (tz) env.timeZone = tz;
  } catch {
    // ignore: time zone is a nice-to-have breakdown.
  }

  env.colorScheme = window.matchMedia?.('(prefers-color-scheme: light)').matches ? 'Light' : 'Dark';
  env.layoutDirection = document.dir === 'rtl' ? 'rightToLeft' : 'leftToRight';

  if (window.screen?.width) env.screenWidth = window.screen.width;
  if (window.screen?.height) env.screenHeight = window.screen.height;
  if (window.devicePixelRatio) env.screenScale = window.devicePixelRatio;

  return env;
}

/**
 * Run one round of analytics: ask the backend what may be sent, send it, and
 * confirm each success back so the marker is persisted.
 *
 * The three collaborators are injected so the protocol can be tested without
 * the network or the real SDK. Returns the signal types actually delivered.
 */
export async function runPlan(
  getPlan: () => Promise<AnalyticsPlan>,
  makeSignal: (plan: AnalyticsPlan) => SignalFn,
  markSent: (event: AnalyticsEvent) => Promise<void>,
): Promise<string[]> {
  let plan: AnalyticsPlan;
  try {
    plan = await getPlan();
  } catch (e) {
    // Analytics must never take down startup.
    logger.warn('analytics', `could not read plan: ${String(e)}`);
    return [];
  }

  // Empty plan = opted out, unconfigured, nothing due, or another window
  // already claimed the work. Build no client and touch no network. Optional
  // chaining because an e2e recorder can stub the command out entirely, and a
  // missing plan must read as "nothing to send", not as a crash.
  if (!plan?.signals?.length) return [];

  const send = makeSignal(plan);
  const delivered: string[] = [];

  for (const signal of plan.signals) {
    try {
      if (!(await send(signal))) {
        logger.warn('analytics', `${signal.type} was not accepted; will retry next launch`);
        continue;
      }
      if (signal.marker) await markSent(signal.marker);
      delivered.push(signal.type);
    } catch (e) {
      // One failing signal must not abandon the rest of the plan.
      logger.warn('analytics', `sending ${signal.type} failed: ${String(e)}`);
    }
  }

  return delivered;
}

/**
 * Builds the real SDK-backed sender for a plan.
 *
 * Exported for the adapter test; prefer `sendDueSignals` at call sites.
 */
export function sdkSignal(plan: AnalyticsPlan): SignalFn {
  const td = new TelemetryDeck({
    appID: plan.appId,
    clientUser: plan.clientUser,
    // Pinned by the backend rather than left to the SDK default, so a change to
    // that default cannot silently reroute Themia's data.
    target: plan.target,
    testMode: plan.testMode,
  });
  return async (signal) => {
    const res = await td.signal(signal.type, signal.payload);
    return Boolean(res?.ok);
  };
}

/** Send whatever the given plan holds. Shared by every entry point below. */
async function dispatch(getPlan: () => Promise<AnalyticsPlan>): Promise<string[]> {
  return runPlan(
    getPlan,
    sdkSignal,
    async (event) => { await invoke('analytics_mark_sent', { event }); },
  );
}

/** One round of due lifecycle signals against the real backend + real SDK. */
export async function sendDueSignals(): Promise<string[]> {
  return dispatch(() => invoke('analytics_plan', { env: collectEnv() }));
}

/**
 * Report something that just happened. The backend decides whether it may be
 * sent at all and builds the whole signal; this only puts it on the wire.
 */
export async function track(event: AnalyticsTrackedEvent): Promise<void> {
  await dispatch(() => invoke('analytics_track', { event }));
}

/**
 * Report an error to TelemetryDeck's Errors preset.
 *
 * `id` groups similar errors and must be a stable slug (`email.imap.authFailed`),
 * never an interpolated string. `message` is optional and must be a STATIC
 * description — the backend drops anything shaped like user data ([S-22]).
 */
export function reportError(
  id: string,
  category: AnalyticsErrorCategory = 'thrown-exception',
  message?: string,
): void {
  void track({ event: 'error', id, category, message });
}

/**
 * Error reporting is capped per window ([S-24]).
 *
 * A failing subsystem can log the same error every tick, and a signal per tick
 * would be both useless and rude. One signal per distinct source is enough to
 * see that it happened; the count belongs in the user's log file, not here.
 */
const reportedSources = new Set<string>();
const MAX_ERROR_SOURCES = 25;

/**
 * Report ERROR log records to TelemetryDeck's Errors preset ([S-24]).
 *
 * Hooks the logger rather than adding call sites, so it covers every subsystem
 * plus the uncaught-exception and unhandled-rejection handlers that already
 * funnel into it. Only the SOURCE travels: `[email] IMAP login failed for
 * me@example.com` reports `log.email` and nothing else.
 *
 * Install once per window, alongside the crash handlers.
 */
export function installErrorReporting(): void {
  setErrorSink((source) => {
    // Analytics failures log through this same pipeline; reporting them would
    // be a loop that feeds itself.
    if (source === 'analytics') return;
    if (reportedSources.size >= MAX_ERROR_SOURCES) return;
    if (reportedSources.has(source)) return;
    reportedSources.add(source);

    const category: AnalyticsErrorCategory =
      source === 'uncaught' || source === 'unhandledrejection' ? 'thrown-exception' : 'app-state';
    reportError(`log.${source}`, category);
  });
}

/** Report a move between screens, for the dashboard's navigation funnel. */
export function reportNavigation(from: string, to: string): void {
  if (from === to) return;
  void track({ event: 'navigation', from, to });
}

/**
 * Themia's auxiliary windows, keyed by the URL param that selects them in
 * `main.tsx`. Names are ours and fixed — never a view name, monitor id or any
 * other value the user or their hardware supplies ([S-23]).
 */
const WINDOW_SCREENS: ReadonlyArray<readonly [string, string]> = [
  ['connect', 'connect'],
  ['layout', 'layout-editor'],
  ['shortcutadd', 'shortcut-add'],
  ['layoutpicker', 'layout-picker'],
  ['trayflyout', 'tray-flyout'],
  ['widgetwindow', 'widget-window'],
  ['config', 'settings'],
];

/** The screen a window renders, or null for the widget grid (the app itself). */
export function windowScreen(params: URLSearchParams): string | null {
  for (const [param, screen] of WINDOW_SCREENS) {
    if (params.has(param)) return screen;
  }
  return null;
}

/**
 * Report that one of the auxiliary windows opened. The grid window is skipped:
 * it IS the app, and its opening is already the session signal.
 */
export function reportWindowOpened(params: URLSearchParams): void {
  const screen = windowScreen(params);
  if (screen) reportNavigation('app', screen);
}

/** Report a completed license activation. */
export function reportPurchase(
  productId: string,
  kind: 'subscription' | 'one-time-purchase' = 'one-time-purchase',
): void {
  void track({ event: 'purchase', productId, kind });
}

/**
 * Record the user's analytics decision — from the first-run prompt or from the
 * Settings toggle, which are the same decision and so the same call.
 *
 * The backend returns a plan holding the one-time `app_opt_out` signal ONLY
 * when a consent that was actually given is being revoked, and that signal is
 * deliberately not gated by the answer it announces ([S-07]). Declining the
 * first-run prompt sends nothing at all: there was never any consent to leave.
 */
export async function setAnalyticsConsent(granted: boolean): Promise<void> {
  await dispatch(() => invoke('analytics_set_consent', { granted }));
}

/**
 * Re-ask the backend hourly so a machine left running overnight still reports
 * the next day's heartbeat. The interval is only a poll: the actual once-a-day
 * decision is the backend's on-disk day marker, so this is telemetry cadence,
 * never a correctness path.
 */
const RECHECK_MS = 60 * 60 * 1000;

/**
 * Start analytics for this window. Returns a cleanup function that closes the
 * session, so the next launch's signals carry an accurate average session
 * length ([S-19]).
 */
export function startAnalytics(): () => void {
  const startedAt = Date.now();
  void sendDueSignals();
  const timer = setInterval(() => { void sendDueSignals(); }, RECHECK_MS);

  // A widget window is usually torn down rather than closed politely, so the
  // session is also closed on pagehide. Both paths are idempotent: the backend
  // just overwrites the previous length.
  const end = (): void => {
    const seconds = Math.max(0, Math.round((Date.now() - startedAt) / 1000));
    void invoke('analytics_session_end', { seconds }).catch(() => {
      // ignore: a lost session length costs one average, never correctness.
    });
  };
  window.addEventListener('pagehide', end);

  return () => {
    clearInterval(timer);
    window.removeEventListener('pagehide', end);
    end();
  };
}
