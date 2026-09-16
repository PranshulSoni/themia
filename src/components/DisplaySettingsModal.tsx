import { Show, createSignal, onMount, onCleanup, type JSX } from 'solid-js';
import type { MonitorDescriptor } from '../ipc.ts';
import { logger } from '../debugLog.ts';
import {
  ArrowUpLeft, ArrowUp, ArrowUpRight, ArrowLeft, ArrowRight, ArrowDownLeft, ArrowDown, ArrowDownRight,
} from 'lucide-solid';
import {
  viewStore,
  getMonitorViewMap,
  getMonitorDisabledMap,
  getMonitorScaleMap,
  setMonitorDisabled,
  setMonitorView,
  setMonitorScale,
  monitorLabel,
  fetchActiveMonitors,
  getViewBarPositionMap, setViewBarPositionForMonitor,
  getViewBarSizeMap, setViewBarSizeForMonitor,
  getViewBarIconOnlyMap, setViewBarIconOnlyForMonitor,
  getViewBarHiddenMap, setViewBarHiddenForMonitor,
  getViewBarVisibleViewsMap, setViewBarVisibleViewsForMonitor,
} from '../views.ts';
import type { ViewBarPosition, ViewBarSize } from '../views.ts';
import NumberStepper from './NumberStepper.tsx';
import Dropdown, { type DropdownOption } from './Dropdown.tsx';
import { VIEW_ICON_MAP } from './IconPicker.tsx';
import ToggleSwitch from './ToggleSwitch.tsx';
import VisibleViewsSelect from './VisibleViewsSelect.tsx';
import { t } from '../i18n.ts';
import '../styles/config-base.css';
import './SettingsModal.css';

const positionOptions = (): { label: string; value: ViewBarPosition; icon: () => JSX.Element }[] => [
  { label: t('viewBar.posTopLeft'), value: 'top-left', icon: () => <ArrowUpLeft size={14} strokeWidth={1.5} /> },
  { label: t('viewBar.posTopCenter'), value: 'top-center', icon: () => <ArrowUp size={14} strokeWidth={1.5} /> },
  { label: t('viewBar.posTopRight'), value: 'top-right', icon: () => <ArrowUpRight size={14} strokeWidth={1.5} /> },
  { label: t('viewBar.posLeftCenter'), value: 'left-center', icon: () => <ArrowLeft size={14} strokeWidth={1.5} /> },
  { label: t('viewBar.posRightCenter'), value: 'right-center', icon: () => <ArrowRight size={14} strokeWidth={1.5} /> },
  { label: t('viewBar.posBottomLeft'), value: 'bottom-left', icon: () => <ArrowDownLeft size={14} strokeWidth={1.5} /> },
  { label: t('viewBar.posBottomCenter'), value: 'bottom-center', icon: () => <ArrowDown size={14} strokeWidth={1.5} /> },
  { label: t('viewBar.posBottomRight'), value: 'bottom-right', icon: () => <ArrowDownRight size={14} strokeWidth={1.5} /> },
];

interface DisplaySettingsBodyProps {
  monitorId: string;
}

// The Display settings content panel for the unified config window. Operates on
// a single physical monitor, identified by its STABLE hardware id ([MI-01]; NOT
// the `\\.\DISPLAYn` ordinal, which Windows reassigns across reboots — #70). The
// shell passes the id in via props. Holds that monitor's settings: show/hide
// themia, assigned view, widget scale.
export default function DisplaySettingsBody(props: DisplaySettingsBodyProps) {
  // The live topology: gates the "can't disable the last active display" rule,
  // and is the label source (the id itself is an opaque device path and must
  // never be shown). Refreshed on plug/unplug.
  const [activeMonitors, setActiveMonitors] = createSignal<MonitorDescriptor[]>([]);
  const refreshActive = () => {
    fetchActiveMonitors()
      .then(setActiveMonitors)
      .catch(e => {
        // An unknown topology disables the last-display guard rather than
        // silently pretending there are no monitors, so surface it.
        logger.error('displays', `get_active_monitors failed: ${e}`);
        setActiveMonitors([]);
      });
  };
  onMount(() => {
    refreshActive();
    window.addEventListener('themia:display-changed', refreshActive);
    onCleanup(() => window.removeEventListener('themia:display-changed', refreshActive));
  });

  const disabled = () => !!getMonitorDisabledMap()[props.monitorId];
  const viewId = () => getMonitorViewMap()[props.monitorId] as string | undefined;
  const scale = () => getMonitorScaleMap()[props.monitorId] ?? 1;

  // View-bar config is stored per monitor; these read/write this display's slot.
  const barHidden = () => !!getViewBarHiddenMap()[props.monitorId];
  const barPosition = () => getViewBarPositionMap()[props.monitorId] ?? 'bottom-left';
  const barSize = () => getViewBarSizeMap()[props.monitorId] ?? 'medium';
  const barIconOnly = () => !!getViewBarIconOnlyMap()[props.monitorId];
  const visibleList = () => getViewBarVisibleViewsMap()[props.monitorId] ?? [];

  const enabledActiveCount = () => {
    const disabledMap = getMonitorDisabledMap();
    return activeMonitors().filter(m => !disabledMap[m.id]).length;
  };
  const isLastEnabled = () => !disabled() && enabledActiveCount() <= 1;

  return (
    <div class="settings-content cfg">
      <span class="section-label">{monitorLabel(props.monitorId, activeMonitors())}</span>

      <div class="design-row">
        <div class="design-left">
          <span class="design-label">{t('display.showThemia.label')}</span>
          <span class="design-desc">
            {disabled()
              ? t('display.showThemia.descHidden')
              : t('display.showThemia.descShown')}
          </span>
        </div>
        <ToggleSwitch
          checked={!disabled()}
          disabled={isLastEnabled()}
          title={isLastEnabled() ? t('display.cantDisableLast') : undefined}
          onLabel={t('toggle.shown')}
          offLabel={t('toggle.hidden')}
          onChange={() => setMonitorDisabled(props.monitorId, !disabled())}
        />
      </div>

      <Show when={!disabled()}>
        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('display.view.label')}</span>
            <span class="design-desc">{t('display.view.desc')}</span>
          </div>
          <Dropdown
            value={viewId() ?? ''}
            options={viewStore.list.map((s): DropdownOption => ({
              label: s.name,
              value: s.id,
              icon: s.icon && VIEW_ICON_MAP[s.icon]
                ? () => VIEW_ICON_MAP[s.icon!](14)
                : undefined,
            }))}
            onChange={(v) => setMonitorView(props.monitorId, v)}
          />
        </div>

        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('display.widgetScale.label')}</span>
            <span class="design-desc">{t('display.widgetScale.desc')}</span>
          </div>
          <NumberStepper
            value={Math.round(scale() * 100)}
            min={50}
            max={200}
            step={10}
            unit="%"
            onChange={(v) => setMonitorScale(props.monitorId, v / 100)}
          />
        </div>

        <div class="divider" />
        <span class="section-label">{t('viewBar.section')}</span>
        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('display.showViewBar.label')}</span>
            <span class="design-desc">{t('display.showViewBar.desc')}</span>
          </div>
          <ToggleSwitch
            checked={!barHidden()}
            onLabel={t('toggle.shown')}
            offLabel={t('toggle.hidden')}
            onChange={() => setViewBarHiddenForMonitor(props.monitorId, !barHidden())}
          />
        </div>
        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('viewBar.position.label')}</span>
            <span class="design-desc">{t('viewBar.position.desc')}</span>
          </div>
          <Dropdown
            value={barPosition()}
            options={positionOptions()}
            onChange={(v) => setViewBarPositionForMonitor(props.monitorId, v as ViewBarPosition)}
          />
        </div>
        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('viewBar.size.label')}</span>
            <span class="design-desc">{t('viewBar.size.desc')}</span>
          </div>
          <div class="view-bar-size-toggle">
            {(['medium', 'large', 'xl'] as ViewBarSize[]).map(s => (
              <button
                class={barSize() === s ? 'active' : ''}
                onClick={() => setViewBarSizeForMonitor(props.monitorId, s)}
              >
                {s === 'medium' ? t('display.sizeM') : s === 'large' ? t('display.sizeL') : t('display.sizeXL')}
              </button>
            ))}
          </div>
        </div>
        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('viewBar.showLabels.label')}</span>
            <span class="design-desc">{t('viewBar.showLabels.desc')}</span>
          </div>
          <ToggleSwitch
            checked={!barIconOnly()}
            onLabel={t('toggle.shown')}
            offLabel={t('toggle.hidden')}
            onChange={() => setViewBarIconOnlyForMonitor(props.monitorId, !barIconOnly())}
          />
        </div>

        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('display.visibleViews.label')}</span>
            <span class="design-desc">{t('display.visibleViews.desc')}</span>
          </div>
          <VisibleViewsSelect
            stored={visibleList()}
            onChange={(raw) => setViewBarVisibleViewsForMonitor(props.monitorId, raw)}
            noneFallbackKey={viewId() ?? viewStore.list[0]?.id}
            noneActionLabel={t('display.visibleOnlyAssigned')}
          />
        </div>
      </Show>
    </div>
  );
}
