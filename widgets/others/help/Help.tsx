import { createSignal, Show, For, type Component } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import {
  LifeBuoy,
  Sparkles,
  LayoutGrid,
  Monitor,
  Palette,
} from 'lucide-solid';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';
import { parseRich } from '../../../src/i18nRich.ts';
import './Help.css';

interface HelpProps {
  headerMode?: HeaderMode;
  label?: string;
}

type TabId = 'basics' | 'widgets' | 'views' | 'customize';

interface TabDef {
  id: TabId;
  icon: Component<{ size: number; strokeWidth: number }>;
}

const TABS: TabDef[] = [
  { id: 'basics',    icon: Sparkles },
  { id: 'widgets',   icon: LayoutGrid },
  { id: 'views',     icon: Monitor },
  { id: 'customize', icon: Palette },
];

/** [S-14] Renders a prose translation, turning its `*term*` / `_ui element_`
 *  markers into real <strong>/<em> nodes. Text only — never HTML. */
function Rich(props: { k: string }) {
  return (
    <For each={parseRich(t(props.k))}>{(tok) => (
      <Show when={tok.kind !== 'text'} fallback={tok.text}>
        <Dynamic component={tok.kind === 'strong' ? 'strong' : 'em'}>{tok.text}</Dynamic>
      </Show>
    )}</For>
  );
}

/** [S-14] A bullet list built from prose keys, one key per <li>. */
function Bullets(props: { keys: string[] }) {
  return (
    <ul class="help-widget__bullets">
      <For each={props.keys}>{(k) => <li><Rich k={k} /></li>}</For>
    </ul>
  );
}

function BasicsTab() {
  return (
    <>
      <p class="help-widget__intro"><Rich k="help.basics.intro" /></p>
      <div class="help-widget__section-title">{t('help.basics.goodToKnow')}</div>
      <Bullets keys={[
        'help.basics.editMode',
        'help.basics.rightClick',
        'help.basics.views',
        'help.basics.screens',
      ]} />
      <p class="help-widget__tip"><Rich k="help.basics.tip" /></p>
    </>
  );
}

function WidgetsTab() {
  return (
    <>
      <p class="help-widget__intro"><Rich k="help.widgets.intro" /></p>
      <div class="help-widget__section-title">{t('help.widgets.working')}</div>
      <Bullets keys={[
        'help.widgets.add',
        'help.widgets.configure',
        'help.widgets.moveResize',
        'help.widgets.copyPaste',
        'help.widgets.remove',
      ]} />
      <div class="help-widget__section-title">{t('help.widgets.whatYouCanAdd')}</div>
      <Bullets keys={[
        'help.widgets.office',
        'help.widgets.online',
        'help.widgets.system',
        'help.widgets.others',
      ]} />
    </>
  );
}

function ViewsTab() {
  return (
    <>
      <p class="help-widget__intro"><Rich k="help.views.intro" /></p>
      <div class="help-widget__section-title">{t('help.views.switching')}</div>
      <Bullets keys={['help.views.switchClick', 'help.views.switchRemember']} />
      <div class="help-widget__section-title">{t('help.views.managing')}</div>
      <Bullets keys={[
        'help.views.manageCreate',
        'help.views.manageMenu',
        'help.views.manageReorder',
      ]} />
    </>
  );
}

function CustomizeTab() {
  return (
    <>
      <p class="help-widget__intro"><Rich k="help.customize.intro" /></p>
      <div class="help-widget__section-title">{t('help.customize.look')}</div>
      <Bullets keys={[
        'help.customize.background',
        'help.customize.accent',
        'help.customize.corners',
        'help.customize.border',
      ]} />
      <div class="help-widget__section-title">{t('help.customize.app')}</div>
      <Bullets keys={[
        'help.customize.theme',
        'help.customize.language',
        'help.customize.textSize',
      ]} />
      <div class="help-widget__section-title">{t('help.customize.grid')}</div>
      <Bullets keys={['help.customize.columnsRows', 'help.customize.spacing']} />
    </>
  );
}

export default function Help(props: HelpProps) {
  const [active, setActive] = createSignal<TabId>('basics');

  return (
    <div class="help-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={LifeBuoy}
        title={props.label || t('widget.help.label')}
      />

      <div class="widget-body">
      <div class="help-widget__tabs" role="tablist">
        <For each={TABS}>{(tab) => (
          <button
            type="button"
            role="tab"
            data-tab={tab.id}
            aria-selected={active() === tab.id ? 'true' : 'false'}
            class="help-widget__tab"
            classList={{ 'help-widget__tab--active': active() === tab.id }}
            onClick={() => setActive(tab.id)}
          >
            <span class="help-widget__tab-icon"><Dynamic component={tab.icon} size={13} strokeWidth={1.5} /></span>
            <span class="help-widget__tab-label">{t(`help.tabs.${tab.id}`)}</span>
          </button>
        )}</For>
      </div>

      <div class="help-widget__body" role="tabpanel">
        <Show when={active() === 'basics'}><BasicsTab /></Show>
        <Show when={active() === 'widgets'}><WidgetsTab /></Show>
        <Show when={active() === 'views'}><ViewsTab /></Show>
        <Show when={active() === 'customize'}><CustomizeTab /></Show>
      </div>
      </div>
    </div>
  );
}
