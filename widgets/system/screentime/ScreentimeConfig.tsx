import { Show, For, createMemo, createSignal } from 'solid-js';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import {
  Monitor, ChartPie, ChartColumn, Grid3x3, Ban, X,
  Calendar, CalendarRange, CalendarDays,
} from 'lucide-solid';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import MetricMultiSelect from '../../../src/widgets/MetricMultiSelect.tsx';
import { canonicalVisible } from '../../../src/widgets/metricSet.ts';
import {
  parseHidden, parsePeriod, clampStyle, resolveView, stylesFor, MAX_APPS_LIMIT,
  parseGoalSecs, parseAppGoals,
  type DisplayStyle, type Period,
} from './screentimeDisplay.ts';
import { METRIC_DEFS } from './metrics.ts';
import '../../../src/styles/config-base.css';
import './ScreentimeConfig.css';
import type { JSX } from 'solid-js';
import { t } from '../../../src/i18n.ts';

export interface ScreentimeConfigData {
  visibleMetrics?: string[];
  maxApps?: number;
  // [S-46] `period` may be absent on configs written before it existed, where
  // `displayStyle` could also hold a period ('week'/'month').
  period?: Period;
  displayStyle?: DisplayStyle | 'week' | 'month';
  hiddenApps?: string[];
  // [S-59] daily limits in SECONDS: the whole screen time, and per app by exe.
  goalSecs?: number;
  appGoals?: Record<string, number>;
}

/// [S-59] the panel edits limits in whole minutes; the config stores seconds so
/// it lines up with everything else the widget measures.
const GOAL_STEP_MIN = 15;
const GOAL_MAX_MIN = 24 * 60;

/// [S-46] the label + icon for each style, so the dropdown is built from
/// `stylesFor` rather than repeating the pairing.
const STYLE_LABEL_KEY: Record<DisplayStyle, string> = {
  doughnut: 'screentime.cfg.styleDoughnut',
  bars: 'screentime.cfg.styleBars',
  heatmap: 'screentime.cfg.styleHeatmap',
  none: 'screentime.cfg.styleNone',
};

const STYLE_ICON: Record<DisplayStyle, typeof ChartPie> = {
  doughnut: ChartPie,
  bars: ChartColumn,
  heatmap: Grid3x3,
  none: Ban,
};

/// Prettify an exe stem for display in the config (the store's friendly name
/// lives on the backend, not in config): "chrome" → "Chrome".
function prettyExe(exe: string): string {
  return exe ? exe.charAt(0).toUpperCase() + exe.slice(1) : exe;
}

export interface ScreentimeConfigProps {
  config: ScreentimeConfigData;
  onChange: (config: ScreentimeConfigData) => void;
  globals?: Record<string, unknown>;
}

export default function ScreentimeConfig(props: ScreentimeConfigProps): JSX.Element {
  const config = (): ScreentimeConfigData => props.config ?? {};
  function update<K extends keyof ScreentimeConfigData>(k: K, v: ScreentimeConfigData[K]) {
    props.onChange({ ...config(), [k]: v });
  }

  const hiddenApps = createMemo(() => parseHidden(config().hiddenApps));
  function unhideApp(exe: string) {
    update('hiddenApps', hiddenApps().filter((e) => e !== exe));
  }

  // [S-46] the persisted pair, with the pre-[S-46] single field absorbed.
  const view = createMemo(() => resolveView(config().period, config().displayStyle));

  /// [S-46] the style dropdown offers only what the current period can draw.
  const styleOptions = createMemo(() => stylesFor(view().period).map((s) => ({
    value: s,
    label: t(STYLE_LABEL_KEY[s]),
    icon: () => {
      const Icon = STYLE_ICON[s];
      return <Icon size={13} strokeWidth={1.5} />;
    },
  })));

  /// [S-46] changing the period rewrites BOTH fields, so the pair on disk is
  /// always one the widget can draw (and the legacy reading stops applying).
  function setPeriod(raw: string) {
    const period = parsePeriod(raw) ?? 'day';
    props.onChange({ ...config(), period, displayStyle: clampStyle(period, view().style) });
  }

  // ── [S-59] goals ─────────────────────────────────────────────────────────
  const goalMinutes = () => Math.round((parseGoalSecs(config().goalSecs) ?? 0) / 60);
  const appGoals = createMemo(() => parseAppGoals(config().appGoals));

  /// 0 minutes means "no goal", so the key is dropped rather than stored as a
  /// zero-second limit everything would instantly blow past.
  function setGoalMinutes(min: number) {
    const next = { ...config() };
    if (min > 0) next.goalSecs = min * 60;
    else delete next.goalSecs;
    props.onChange(next);
  }

  function setAppGoal(exe: string, min: number) {
    const next = { ...appGoals() };
    if (min > 0) next[exe] = min * 60;
    else delete next[exe];
    update('appGoals', next);
  }

  /// Apps to offer a limit for: the ones this widget already knows about —
  /// whatever is hidden, plus whatever already has a goal — so the panel needs
  /// no data fetch of its own. Anything else is typed in by exe key.
  const [newExe, setNewExe] = createSignal('');
  function addAppGoal() {
    const exe = newExe().trim().toLowerCase();
    if (!exe || appGoals()[exe]) return;
    setAppGoal(exe, 60);
    setNewExe('');
  }

  return (
    <ConfigTabs tabs={[
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
    ]}>
      {(tab) => <>
        <Show when={tab === 'display'}>
          {/* [S-46] two independent axes: WHAT is covered, then HOW it's drawn. */}
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('screentime.cfg.period')}</span>
              <span class="design-desc">{t('screentime.cfg.periodDesc')}</span>
            </div>
            <Dropdown
              value={view().period}
              options={[
                { value: 'day', label: t('screentime.cfg.periodDay'), icon: () => <Calendar size={13} strokeWidth={1.5} /> },
                /* The window periods name their span outright — the same
                   wording the widget header uses ([S-41]), so the setting and
                   what it produces read alike. */
                { value: 'week', label: t('screentime.last7Days'), icon: () => <CalendarRange size={13} strokeWidth={1.5} /> },
                { value: 'month', label: t('screentime.last30Days'), icon: () => <CalendarDays size={13} strokeWidth={1.5} /> },
              ]}
              onChange={(v) => setPeriod(v)}
            />
          </div>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('screentime.cfg.style')}</span>
              <span class="design-desc">{t('screentime.cfg.styleDesc')}</span>
            </div>
            <Dropdown
              value={view().style}
              options={styleOptions()}
              onChange={(v) => update('displayStyle', clampStyle(view().period, v))}
            />
          </div>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('widgetUi.metricsLabel')}</span>
              <span class="design-desc">{t('widgetUi.metricsDesc')}</span>
            </div>
            <MetricMultiSelect
              defs={METRIC_DEFS}
              value={config().visibleMetrics ?? canonicalVisible(METRIC_DEFS)}
              onChange={(v) => update('visibleMetrics', v)}
              reorder={false}
            />
          </div>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('screentime.appsShownLabel')}</span>
              <span class="design-desc">{t('screentime.appsShownDesc')}</span>
            </div>
            <NumberStepper
              value={config().maxApps ?? 7}
              min={0}
              max={MAX_APPS_LIMIT}
              step={1}
              onChange={(v) => update('maxApps', v)}
            />
          </div>

          {/* [S-59] daily limits: the whole screen time, then per app. */}
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('screentime.cfg.goal')}</span>
              <span class="design-desc">{t('screentime.cfg.goalDesc')}</span>
            </div>
            <NumberStepper
              value={goalMinutes()}
              min={0}
              max={GOAL_MAX_MIN}
              step={GOAL_STEP_MIN}
              onChange={setGoalMinutes}
            />
          </div>

          <div class="sctcfg-filter-block">
            <div class="sctcfg-filter-head">
              <span class="design-label">{t('screentime.cfg.appGoals')}</span>
              <span class="design-desc">{t('screentime.cfg.appGoalsDesc')}</span>
            </div>
            <For each={Object.keys(appGoals()).sort()}>{(exe) => (
              <div class="sctcfg-filter-row">
                <span class="sctcfg-filter-name" title={exe}>{prettyExe(exe)}</span>
                <NumberStepper
                  value={Math.round(appGoals()[exe] / 60)}
                  min={0}
                  max={GOAL_MAX_MIN}
                  step={GOAL_STEP_MIN}
                  onChange={(v) => setAppGoal(exe, v)}
                />
                <button
                  class="sctcfg-filter-remove"
                  onClick={() => setAppGoal(exe, 0)}
                  title={t('common.remove')}
                  aria-label={t('common.remove')}
                >
                  <X size={12} strokeWidth={1.5} />
                </button>
              </div>
            )}</For>
            <div class="sctcfg-filter-row">
              <input
                class="text-input sctcfg-goal-input"
                value={newExe()}
                placeholder={t('screentime.cfg.appGoalPlaceholder')}
                onInput={(e) => setNewExe(e.currentTarget.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') addAppGoal(); }}
              />
              <button class="sctcfg-goal-add" onClick={addAppGoal}>{t('common.add')}</button>
            </div>
          </div>

          <div class="sctcfg-filter-block">
            <div class="sctcfg-filter-head">
              <span class="design-label">{t('screentime.cfg.hidden')}</span>
              <span class="design-desc">{t('screentime.cfg.hiddenDesc')}</span>
            </div>
            <Show
              when={hiddenApps().length > 0}
              fallback={<div class="sctcfg-filter-empty">{t('screentime.cfg.noHidden')}</div>}
            >
              <For each={hiddenApps()}>{(exe) => (
                <div class="sctcfg-filter-row">
                  <span class="sctcfg-filter-name" title={exe}>{prettyExe(exe)}</span>
                  <button
                    class="sctcfg-filter-remove"
                    onClick={() => unhideApp(exe)}
                    title={t('common.remove')}
                    aria-label={t('common.remove')}
                  >
                    <X size={12} strokeWidth={1.5} />
                  </button>
                </div>
              )}</For>
            </Show>
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
