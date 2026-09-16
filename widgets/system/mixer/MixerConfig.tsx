import { Show } from 'solid-js';
import { Monitor, RefreshCw } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import { refreshInterval } from '../refreshInterval.ts';
import { flag } from './mixerRows.ts';
import '../../../src/styles/config-base.css';
import type { JSX } from 'solid-js';
import { t } from '../../../src/i18n.ts';

export interface MixerConfigProps {
  config: Record<string, unknown>;
  onChange: (config: Record<string, unknown>) => void;
  globals?: Record<string, unknown>;
}

export default function MixerConfig(props: MixerConfigProps): JSX.Element {
  // [S-13 Settings.SPEC] read props.config directly; never mirror it into local
  // signals, or the panel drifts from the widget it configures.
  const config = () => props.config ?? {};
  function update(key: string, value: unknown) {
    props.onChange({ ...config(), [key]: value });
  }

  return (
    <ConfigTabs tabs={[
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
      { key: 'refresh', label: t('configTabs.refresh'), icon: RefreshCw },
    ]}>
      {(tab) => <>
        <Show when={tab === 'display'}>
          {/* [S-03] the feature themia-releases#112 asked for: one slider per
              app instead of one per audio session. */}
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('mixer.cfg.groupLabel')}</span>
              <span class="design-desc">{t('mixer.cfg.groupDesc')}</span>
            </div>
            <ToggleSwitch
              checked={flag(config().groupByApp, true)}
              onLabel={t('toggle.on')}
              offLabel={t('toggle.off')}
              onChange={(v) => update('groupByApp', v)}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('mixer.cfg.masterLabel')}</span>
              <span class="design-desc">{t('mixer.cfg.masterDesc')}</span>
            </div>
            <ToggleSwitch
              checked={flag(config().showMaster, true)}
              onLabel={t('toggle.shown')}
              offLabel={t('toggle.hidden')}
              onChange={(v) => update('showMaster', v)}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('mixer.cfg.activeLabel')}</span>
              <span class="design-desc">{t('mixer.cfg.activeDesc')}</span>
            </div>
            <ToggleSwitch
              checked={flag(config().onlyActive, false)}
              onLabel={t('toggle.on')}
              offLabel={t('toggle.off')}
              onChange={(v) => update('onlyActive', v)}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('mixer.cfg.systemLabel')}</span>
              <span class="design-desc">{t('mixer.cfg.systemDesc')}</span>
            </div>
            <ToggleSwitch
              checked={flag(config().showSystemSounds, false)}
              onLabel={t('toggle.shown')}
              offLabel={t('toggle.hidden')}
              onChange={(v) => update('showSystemSounds', v)}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('mixer.cfg.iconsLabel')}</span>
              <span class="design-desc">{t('mixer.cfg.iconsDesc')}</span>
            </div>
            <ToggleSwitch
              checked={flag(config().showIcons, true)}
              onLabel={t('toggle.shown')}
              offLabel={t('toggle.hidden')}
              onChange={(v) => update('showIcons', v)}
            />
          </div>
        </Show>

        <Show when={tab === 'refresh'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('widgetUi.refreshLabel')}</span>
              <span class="design-desc">{t('widgetUi.refreshDescUpdates')}</span>
            </div>
            <NumberStepper
              value={refreshInterval(config().interval, 'mixer')}
              min={1}
              max={60}
              step={1}
              unit="s"
              onChange={(v) => update('interval', v)}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
