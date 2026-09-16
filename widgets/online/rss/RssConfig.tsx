import { Show } from 'solid-js';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import AccountToggleList from '../../../src/components/AccountToggleList.tsx';
import MetricMultiSelect from '../../../src/widgets/MetricMultiSelect.tsx';
import { Monitor, Database, RefreshCw } from 'lucide-solid';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import { RSS_FIELD_DEFS } from './fields.ts';
import { t } from '../../../src/i18n.ts';
import '../../../src/styles/config-base.css';

interface RssConfigData {
  feedIds?: string[];
  maxItems?: number;
  fields?: string[];
  /** Legacy per-item toggles: migrated to `fields` on first edit. */
  showSnippet?: boolean;
  showSource?: boolean;
  refreshMinutes?: number;
}

interface RssConfigProps {
  config: RssConfigData;
  onChange: (c: RssConfigData) => void;
  globals?: { onManageAccounts?: (providerFilter?: string[]) => void };
}

// A function, not a const: the labels must re-read t() after a
// language change (cf. headerModeOptions in ConfigTabs.tsx).
function tabs() {
  return [
    { key: 'source', label: t('configTabs.source'), icon: Database },
    { key: 'display', label: t('configTabs.display'), icon: Monitor },
    { key: 'refresh', label: t('configTabs.refresh'), icon: RefreshCw },
  ];
}

export default function RssConfig(props: RssConfigProps) {
  function update<K extends keyof RssConfigData>(key: K, value: RssConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  const feedIds = () => props.config.feedIds ?? [];
  function toggleFeed(id: string) {
    const cur = feedIds();
    update('feedIds', cur.includes(id) ? cur.filter(f => f !== id) : [...cur, id]);
  }

  // Visible per-item fields. Prefer the unified `fields`; fall back to the legacy
  // showSnippet/showSource booleans so an unedited widget shows the same fields.
  const fields = (): string[] => {
    if (props.config.fields) return props.config.fields;
    const legacy: string[] = [];
    if (props.config.showSource ?? true) legacy.push('source');
    if (props.config.showSnippet ?? true) legacy.push('preview');
    legacy.push('date');
    return legacy;
  };
  function updateFields(v: string[]) {
    // Drop the legacy booleans once the user edits fields, keeping a single source of truth.
    const { showSnippet: _a, showSource: _b, ...rest } = props.config;
    props.onChange({ ...rest, fields: v });
  }

  return (
    <div class="rss-config">
      <ConfigTabs tabs={tabs()}>
        {(tab) => <>
          <Show when={tab === 'source'}>
            <AccountToggleList
              providerFilter={['rss']}
              providerBadges={{ rss: 'RSS' }}
              accountIds={feedIds}
              onToggle={toggleFeed}
              onManageAccounts={props.globals?.onManageAccounts}
            />
          </Show>

          <Show when={tab === 'display'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('rss.maxItems')}</span>
                <span class="design-desc">{t('rss.maxItemsDesc')}</span>
              </div>
              {/* Was a raw number input -- the only one left in the online
                  group. Note the old min="5" disagreed with the `v >= 1` guard
                  beside it; NumberStepper has one floor. */}
              <NumberStepper
                value={props.config.maxItems ?? 20}
                min={5}
                max={50}
                step={5}
                onChange={(v) => update('maxItems', v)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('rss.fields.label')}</span>
                <span class="design-desc">{t('rss.fields.desc')}</span>
              </div>
              <MetricMultiSelect
                defs={RSS_FIELD_DEFS}
                value={fields()}
                onChange={updateFields}
              />
            </div>

          </Show>

          <Show when={tab === 'refresh'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('rss.refreshInterval')}</span>
                <span class="design-desc">{t('rss.refreshIntervalDesc')}</span>
              </div>
              <NumberStepper
                value={props.config.refreshMinutes ?? 15}
                min={5}
                max={120}
                step={5}
                unit="m"
                onChange={(v) => update('refreshMinutes', v)}
              />
            </div>
          </Show>
        </>}
      </ConfigTabs>
    </div>
  );
}
