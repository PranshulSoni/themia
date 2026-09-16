import { createSignal, createMemo, onMount, For, Show } from 'solid-js';
import { LayoutGrid, List, AlignJustify, Monitor, Filter, X } from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import { invoke } from '../../../src/demo/invoke.ts';
import '../../../src/styles/config-base.css';
import './AppsConfig.css';
import { t } from '../../../src/i18n.ts';

interface AppEntry {
  name: string;
  path: string;
  category: string;
}

interface AppsConfigData {
  viewMode?: string;
  hiddenCategories?: string[];
  hiddenApps?: string[];
}

interface AppsConfigProps {
  config: AppsConfigData;
  onChange: (c: AppsConfigData) => void;
}

export default function AppsConfig(props: AppsConfigProps) {
  const [allApps, setAllApps] = createSignal<AppEntry[]>([]);

  onMount(async () => {
    try {
      const result = await invoke('list_start_menu_apps');
      setAllApps(result ?? []);
    } catch {
      // ignore: app list is best-effort; leave the filter list empty on failure
    }
  });

  function update<K extends keyof AppsConfigData>(key: K, value: AppsConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  const hiddenCategories = (): string[] => props.config.hiddenCategories ?? [];
  const hiddenApps = (): string[] => props.config.hiddenApps ?? [];

  const categories = createMemo(() => {
    const set = new Set<string>();
    for (const a of allApps()) set.add(a.category || 'Other');
    return [...set].sort((a, b) => a.localeCompare(b));
  });

  const hiddenAppRows = createMemo(() => {
    const byPath = new Map(allApps().map(a => [a.path, a]));
    return hiddenApps().map(path => {
      const found = byPath.get(path);
      const name = found?.name ?? (path.split(/[\\/]/).pop() || path);
      return { path, name };
    });
  });

  function toggleCategory(cat: string, on: boolean) {
    const cur = hiddenCategories();
    const next = on ? cur.filter(c => c !== cat) : (cur.includes(cat) ? cur : [...cur, cat]);
    update('hiddenCategories', next);
  }

  function unhideApp(path: string) {
    update('hiddenApps', hiddenApps().filter(p => p !== path));
  }

  return (
    <ConfigTabs tabs={[
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
      { key: 'filter',  label: t('apps.filterTab'),  icon: Filter },
    ]}>
      {(tab) => <>
        <Show when={tab === 'display'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('apps.viewLabel')}</span>
              <span class="design-desc">{t('apps.viewDesc')}</span>
            </div>
            <Dropdown
              value={props.config.viewMode ?? 'grid'}
              options={[
                { value: 'grid',    label: t('apps.viewGrid'),    icon: () => <LayoutGrid size={13} strokeWidth={1.5} /> },
                { value: 'list',    label: t('apps.viewList'),    icon: () => <List size={13} strokeWidth={1.5} /> },
                { value: 'compact', label: t('apps.viewCompact'), icon: () => <AlignJustify size={13} strokeWidth={1.5} /> },
              ]}
              onChange={(v) => update('viewMode', v)}
            />
          </div>
        </Show>

        <Show when={tab === 'filter'}>
          <div class="apps-filter-block">
            <div class="apps-filter-head">
              <span class="design-label">{t('apps.categoriesLabel')}</span>
              <span class="design-desc">{t('apps.categoriesDesc')}</span>
            </div>
            <Show when={categories().length > 0} fallback={<div class="apps-filter-empty">{t('apps.noCategories')}</div>}>
              <For each={categories()}>{cat => {
                const shown = () => !hiddenCategories().includes(cat);
                return (
                  <div class="apps-filter-row">
                    <span class="apps-filter-name">{cat}</span>
                    <ToggleSwitch
                      checked={shown()}
                      onLabel={t('toggle.shown')}
                      offLabel={t('toggle.hidden')}
                      onChange={(next) => toggleCategory(cat, next)}
                    />
                  </div>
                );
              }}</For>
            </Show>
          </div>

          <div class="apps-filter-block">
            <div class="apps-filter-head">
              <span class="design-label">{t('apps.hiddenLabel')}</span>
              <span class="design-desc">{t('apps.hiddenDesc')}</span>
            </div>
            <Show when={hiddenAppRows().length > 0} fallback={<div class="apps-filter-empty">{t('apps.noHidden')}</div>}>
              <For each={hiddenAppRows()}>{row => (
                <div class="apps-filter-row">
                  <span class="apps-filter-name" title={row.path}>{row.name}</span>
                  <button class="apps-filter-remove" onClick={() => unhideApp(row.path)} title={t('apps.unhide')}>
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
