import { Show } from 'solid-js';
import {
  Monitor, PanelTop, PanelBottom, PanelLeft, PanelRight,
  Paintbrush, Layers, Square, SquareDashed,
} from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import IconPicker from '../../../src/components/IconPicker.tsx';
import ColorPicker from '../../../src/ColorPicker.tsx';
import { ChildList } from '../../../src/components/ChildListConfig.tsx';
import { GROUP_BG_DEFAULTS, groupBgMode } from './groupStyle.ts';
import { t } from '../../../src/i18n.ts';
import '../../../src/styles/config-base.css';

interface GroupConfigData {
  collapsed?: boolean;
  headerSide?: string;
  borderMode?: string;
  title?: string;
  icon?: string;
  bgMode?: string;
  bgColor?: string;
  bgAlpha?: number;
}

interface GroupConfigProps {
  config: GroupConfigData;
  onChange: (c: GroupConfigData) => void;
  _widgetId?: string;
  globals?: { onAddToContainer?: (containerId: string) => void };
}

// The group's own settings (title bar + area surface) plus the shared container
// child list. Two differences from stack/tabs in the list: no reordering
// (members sit where they sit on the grid, src/SPEC [S-36]) and the pop-out
// action reads as "remove from group", since the widget does not move — it just
// stops belonging.
//
// Collapse is NOT mirrored here: the title bar owns it (group [S-03]), and a
// second control writing the same field would be the parallel path this
// codebase keeps out.
export default function GroupConfig(props: GroupConfigProps) {
  // Props are read directly, never mirrored into signals (Settings [S-13]).
  function update<K extends keyof GroupConfigData>(key: K, value: GroupConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }
  const bg = () => groupBgMode(props.config.bgMode);

  return (
    <ConfigTabs tabs={[{ key: 'display', label: t('configTabs.display'), icon: Monitor }]}>
      {(tab) => <Show when={tab === 'display'}>
        <span class="section-label">{t('group.section.header')}</span>

        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('group.title.label')}</span>
            <span class="design-desc">{t('group.title.desc')}</span>
          </div>
          {/* `header-mode-field` is what makes it a COMPACT, right-aligned
              field: a bare `.text-input` is `width: 100%`, which eats the row
              and squashes the label beside it. Same pair ConfigTabs' own title
              row uses, so the group's title input matches every other one. */}
          <input
            class="text-input header-mode-field"
            type="text"
            value={props.config.title ?? ''}
            placeholder={t('widget.group.label')}
            onInput={(e) => update('title', e.currentTarget.value)}
          />
        </div>

        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('group.icon.label')}</span>
            <span class="design-desc">{t('group.icon.desc')}</span>
          </div>
          <IconPicker
            value={props.config.icon ?? ''}
            onChange={(v) => update('icon', v)}
          />
        </div>

        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('group.headerSide.label')}</span>
            <span class="design-desc">{t('group.headerSide.desc')}</span>
          </div>
          <Dropdown
            value={props.config.headerSide || 'top'}
            options={[
              { value: 'top',    label: t('group.headerSide.top'),    icon: () => <PanelTop size={13} strokeWidth={1.5} /> },
              { value: 'bottom', label: t('group.headerSide.bottom'), icon: () => <PanelBottom size={13} strokeWidth={1.5} /> },
              { value: 'left',   label: t('group.headerSide.left'),   icon: () => <PanelLeft size={13} strokeWidth={1.5} /> },
              { value: 'right',  label: t('group.headerSide.right'),  icon: () => <PanelRight size={13} strokeWidth={1.5} /> },
            ]}
            onChange={(v) => update('headerSide', v)}
          />
        </div>

        <div class="divider" />
        <span class="section-label">{t('group.section.surface')}</span>

        {/* The BORDER is the grid tile's own ([S-15]); this only decides whether
            it is painted, so a group can read as a bare region. */}
        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('group.border.label')}</span>
            <span class="design-desc">{t('group.border.desc')}</span>
          </div>
          <Dropdown
            value={props.config.borderMode || 'theme'}
            options={[
              { value: 'theme', label: t('group.border.theme'), icon: () => <Square size={13} strokeWidth={1.5} /> },
              { value: 'none',  label: t('group.border.none'),  icon: () => <SquareDashed size={13} strokeWidth={1.5} /> },
            ]}
            onChange={(v) => update('borderMode', v)}
          />
        </div>

        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('group.bg.label')}</span>
            <span class="design-desc">{t('group.bg.desc')}</span>
          </div>
          {/* No "None" mode: clearing the area is the shared transparent
              switch above ([S-13]), which takes the title bar's fill with it
              instead of leaving a frosted bar over a see-through region. */}
          <Dropdown
            value={bg()}
            options={[
              { value: 'default',  label: t('group.bg.default'),  icon: () => <Layers size={13} strokeWidth={1.5} /> },
              { value: 'solid',    label: t('group.bg.solid'),    icon: () => <Paintbrush size={13} strokeWidth={1.5} /> },
            ]}
            onChange={(v) => update('bgMode', v)}
          />
        </div>

        <Show when={bg() === 'solid'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('group.bg.color')}</span>
            </div>
            <ColorPicker
              value={props.config.bgColor || GROUP_BG_DEFAULTS.color}
              onChange={(hex) => update('bgColor', hex)}
              alpha={props.config.bgAlpha ?? GROUP_BG_DEFAULTS.alpha}
              onAlphaChange={(a) => update('bgAlpha', a)}
            />
          </div>
        </Show>

        <div class="divider" />
        <ChildList
          labelKey="group.childrenLabel"
          emptyKey="group.childrenEmpty"
          addKey="group.addChild"
          popOutKey="group.removeFromGroup"
          showReorder={false}
          widgetId={props._widgetId}
          globals={props.globals}
        />
      </Show>}
    </ConfigTabs>
  );
}
