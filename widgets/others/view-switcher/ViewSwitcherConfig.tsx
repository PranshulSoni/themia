import { Show } from 'solid-js';
import { Monitor, ListChecks } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import VisibleViewsSelect from '../../../src/components/VisibleViewsSelect.tsx';
import { viewStore } from '../../../src/views.ts';
import '../../../src/styles/config-base.css';
import { t } from '../../../src/i18n.ts';

interface ViewSwitcherConfigData {
  visibleScreens?: string[];
  iconOnly?: boolean;
}

interface ViewSwitcherConfigProps {
  config: ViewSwitcherConfigData;
  globals?: Record<string, unknown>;
  onChange: (c: ViewSwitcherConfigData) => void;
}

export default function ViewSwitcherConfig(props: ViewSwitcherConfigProps) {
  const cfg = (): ViewSwitcherConfigData => props.config ?? {};
  const visible = () => cfg().visibleScreens ?? [];

  function update<K extends keyof ViewSwitcherConfigData>(key: K, value: ViewSwitcherConfigData[K]) {
    props.onChange({ ...cfg(), [key]: value });
  }

  return (
    <ConfigTabs tabs={[
      { key: 'views',    label: t('viewSwitcher.viewsTab'),    icon: ListChecks },
      { key: 'display',  label: t('configTabs.display'),  icon: Monitor },
    ]}>
      {(tab) => <>
        <Show when={tab === 'views'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('viewSwitcher.visibleLabel')}</span>
              <span class="design-desc">{t('viewSwitcher.visibleDesc')}</span>
            </div>
            <VisibleViewsSelect
              stored={visible()}
              onChange={(raw) => update('visibleScreens', raw)}
              noneFallbackKey={viewStore.activeId}
              noneActionLabel="Only current"
            />
          </div>
        </Show>

        <Show when={tab === 'display'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('viewSwitcher.iconOnlyLabel')}</span>
              <span class="design-desc">{t('viewSwitcher.iconOnlyDesc')}</span>
            </div>
            <ToggleSwitch
              checked={!!cfg().iconOnly}
              onLabel={t('toggle.yes')}
              offLabel={t('toggle.no')}
              onChange={(v) => update('iconOnly', v)}
            />
          </div>
        </Show>
      </>}
    </ConfigTabs>
  );
}
