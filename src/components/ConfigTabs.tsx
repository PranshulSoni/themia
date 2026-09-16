import { Show, For, createMemo, type JSX, type Component } from 'solid-js';
import { PanelTop, Minus, EyeOff } from 'lucide-solid';
import { useWidgetHeader } from './widgetHeaderContext.tsx';
import { sameSections } from './configSections.ts';
import Dropdown, { type DropdownOption } from './Dropdown.tsx';
import ToggleSwitch from './ToggleSwitch.tsx';
import NumberStepper from './NumberStepper.tsx';
import IconPicker from './IconPicker.tsx';
import { FLYOUT_MAX, FLYOUT_MIN } from '../trayLayout.ts';
import type { HeaderMode } from '../widgets/registry.tsx';
import { t } from '../i18n.ts';
import '../styles/config-base.css';

// A function, not a module const: the labels must re-read t() after a language
// change (cf. introAnimOptions in SettingsModal.tsx). As a const they were
// evaluated once at import and would have stayed in the boot language forever.
function headerModeOptions(): DropdownOption[] {
  return [
    { value: 'normal', label: t('configTabs.headerMode.normal'), icon: () => <PanelTop size={14} strokeWidth={1.5} /> },
    { value: 'compact', label: t('configTabs.headerMode.compact'), icon: () => <Minus size={14} strokeWidth={1.5} /> },
    { value: 'none', label: t('configTabs.headerMode.hidden'), icon: () => <EyeOff size={14} strokeWidth={1.5} /> },
  ];
}

export interface ConfigTab {
  key: string;
  label: string;
  icon: Component<{ size: number; strokeWidth: number }>;
}

interface ConfigTabsProps {
  tabs: ConfigTab[];
  children: (activeTab: string) => JSX.Element;
}

// Renders every config section ("tab") stacked on a single scrolling page.
// The `tabs` array now just describes section order + labels rather than
// driving a tab switcher: multi-section widgets get a labelled, divider-
// separated block per section; single-section widgets render flat. The
// `children` render-prop is called once per section with that section's key.
export default function ConfigTabs(props: ConfigTabsProps) {
  const header = useWidgetHeader();

  // [S-22] The section list is read through a memo compared BY CONTENT, not by
  // array reference.
  //
  // `props.tabs` is a getter, and a panel whose section list depends on its
  // config (To Do drops "Refresh" in local mode, [S-47]) re-evaluates it — and
  // hands back a brand-new array — on every config write. Rendering that array
  // with a plain `.map()` put this whole subtree in a computation that tracked
  // the config, so ONE setting change tore down and rebuilt every section:
  // labels, dividers and the entire `children` body. The panel visibly flashed,
  // and a control destroyed mid-gesture never saw its own pointerup — a held
  // NumberStepper leaked its repeat timer and ran the value away to its max on
  // its own (issue nwalser/themia-releases#80 follow-up).
  //
  // Comparing by content (`sameSections`) means the same sections re-render
  // nothing; a real change — a section added/removed, or the language switching
  // the labels — still comes through.
  const sections = createMemo(() => props.tabs, undefined, { equals: sameSections });
  // A function, not a setup-time const: To Do's section count changes when the
  // storage mode does, and the const captured the boot value forever.
  const multiSection = () => sections().length > 1;

  return (
    <div class="settings-content cfg">
      {/* A registry-`noHeader` widget has nothing to configure here, so the whole
          section is dropped — label, style row and divider ([S-13]). Telling the
          user "this widget has no header" is noise, not a setting. */}
      <Show when={header && !header.noHeader()}>
        <span class="section-label">{t('configTabs.header')}</span>
        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('configTabs.styleLabel')}</span>
            <span class="design-desc">{t('configTabs.styleDesc')}</span>
          </div>
          <Dropdown
            class="header-mode-dropdown"
            value={header!.headerMode()}
            options={headerModeOptions()}
            onChange={(v) => header!.onHeaderModeChange(v as HeaderMode)}
          />
        </div>
        <Show when={header!.headerMode() !== 'none'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('configTabs.titleLabel')}</span>
              <span class="design-desc">{t('configTabs.titleDesc')}</span>
            </div>
            <input
              class="text-input header-mode-field"
              type="text"
              placeholder={header!.defaultLabel() || t('configTabs.titlePlaceholder')}
              value={header!.label()}
              onInput={(e) => header!.onLabelChange(e.currentTarget.value)}
            />
          </div>
        </Show>
        <div class="divider" />
      </Show>
      {/* [S-32] Background transparency, shown only for registry
          `canTransparent` widgets — the ones whose content still reads over a
          wallpaper. Its own section, not a header row: a headerless widget
          (clock) drops the block above entirely but still offers this. */}
      <Show when={header?.canTransparent()}>
        <span class="section-label">{t('configTabs.background')}</span>
        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('configTabs.transparentLabel')}</span>
            <span class="design-desc">{t('configTabs.transparentDesc')}</span>
          </div>
          <ToggleSwitch
            checked={header!.transparentBg()}
            onLabel={t('toggle.on')}
            offLabel={t('toggle.off')}
            onChange={(v) => header!.onTransparentBgChange(v)}
          />
        </div>
        <div class="divider" />
      </Show>
      {/* [S-45] Always on top: move this widget to the monitor's topmost pin
          overlay so it stays visible over other applications. Hidden for tray
          widgets and container children, which have no layer of their own. */}
      <Show when={header?.pinnable()}>
        <span class="section-label">{t('configTabs.placement')}</span>
        <div class="design-row">
          <div class="design-left">
            <span class="design-label">{t('configTabs.alwaysOnTopLabel')}</span>
            <span class="design-desc">{t('configTabs.alwaysOnTopDesc')}</span>
          </div>
          <ToggleSwitch
            checked={header!.alwaysOnTop()}
            onLabel={t('toggle.on')}
            offLabel={t('toggle.off')}
            onChange={(v) => header!.onAlwaysOnTopChange(v)}
          />
        </div>
        <div class="divider" />
      </Show>
      {/* Tray.SPEC [S-07]: how big this widget's flyout opens. Present only for
          a widget that lives in the tray, so a desktop widget's page is
          unchanged. Two rows rather than one so each axis gets its own label —
          the panel is a fixed size, not a resizable window. */}
      <Show when={header?.trayFlyout()}>
        {(size) => (
          <>
            <span class="section-label">{t('tray.section')}</span>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('tray.nameLabel')}</span>
                <span class="design-desc">{t('tray.nameDesc')}</span>
              </div>
              <input
                class="text-input header-mode-field"
                type="text"
                placeholder={header!.trayNameFallback()}
                value={header!.trayName()}
                onInput={(e) => header!.onTrayNameChange(e.currentTarget.value)}
              />
            </div>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('tray.iconLabel')}</span>
                <span class="design-desc">{t('tray.iconDesc')}</span>
              </div>
              <IconPicker
                value={header!.trayIcon()}
                onChange={(v) => header!.onTrayIconChange(v)}
                allowClear
                placeholder={t('tray.iconDefault')}
              />
            </div>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('tray.widthLabel')}</span>
                <span class="design-desc">{t('tray.widthDesc')}</span>
              </div>
              <NumberStepper
                value={size().width}
                min={FLYOUT_MIN.width}
                max={FLYOUT_MAX.width}
                step={10}
                unit="px"
                onChange={(v) => header!.onTrayFlyoutChange({ width: v })}
              />
            </div>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('tray.heightLabel')}</span>
                <span class="design-desc">{t('tray.heightDesc')}</span>
              </div>
              <NumberStepper
                value={size().height}
                min={FLYOUT_MIN.height}
                max={FLYOUT_MAX.height}
                step={10}
                unit="px"
                onChange={(v) => header!.onTrayFlyoutChange({ height: v })}
              />
            </div>
            <div class="divider" />
          </>
        )}
      </Show>
      <For each={sections()}>
        {(tab, i) => (
          <>
            <Show when={multiSection()}>
              <Show when={i() > 0}><div class="divider" /></Show>
              <Show when={tab.label}><span class="section-label">{tab.label}</span></Show>
            </Show>
            {props.children(tab.key)}
          </>
        )}
      </For>
    </div>
  );
}
