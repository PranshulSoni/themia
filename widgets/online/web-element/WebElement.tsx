import { createMemo, createEffect, untrack } from 'solid-js';
import { Switch, Match, Show } from 'solid-js/web';
import { SquareMousePointer } from 'lucide-solid';
import { createCachedFetch } from '../../../src/createCachedFetch';
import { createPolling } from '../../../src/createPolling';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import { t } from '../../../src/i18n.ts';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { resolveConfig } from './webElement.ts';
import { loadDisplayDoc, type DisplayDoc } from './loadPage.ts';
import './WebElement.css';

interface WebElementProps {
  headerMode?: HeaderMode;
  url?: string;
  selector?: string;
  label?: string;
  refreshMinutes?: number;
  _refresh?: number;
}

function hostOf(url: string): string {
  try { return new URL(url).hostname.replace(/^www\./, ''); } catch { return ''; }
}

export default function WebElement(props: WebElementProps) {
  const cfg = createMemo(() => resolveConfig(props));
  const refreshMs = createMemo(() => cfg().refreshMinutes * 60_000);

  const cache = createCachedFetch<DisplayDoc>({
    namespace: 'web-element',
    maxEntries: 20,
    key: () => {
      const c = cfg();
      if (!c.url || !c.selector) return null;
      return `${c.url}::${c.selector}`;
    },
    staleMs: refreshMs,
    fetcher: (_key, signal) => {
      const { url, selector } = cfg();
      return loadDisplayDoc(url, selector, signal);
    },
  });

  createPolling(() => { void cache.refresh(true); }, refreshMs, 'web-element');

  // Manual refresh: the context-menu action bumps `_refresh`; force a re-fetch.
  createEffect(() => {
    void props._refresh;
    untrack(() => { void cache.refresh(true); });
  });

  const headerTitle = createMemo(() => cfg().label || hostOf(cfg().url) || t('widget.webElement.label'));

  const view = createMemo<'empty' | 'loading' | 'error' | 'notfound' | 'loaded'>(() => {
    const c = cfg();
    if (!c.url || !c.selector) return 'empty';
    const d = cache.data();
    if (d) return d.found ? 'loaded' : 'notfound';
    if (cache.error()) return 'error';
    return 'loading';
  });

  return (
    <div class="webelement-widget">
      <Switch>
        <Match when={view() === 'empty'}>
          <div class="widget-body"><div class="webelement-widget__msg" innerHTML={t('webElement.empty')} /></div>
        </Match>
        <Match when={view() === 'loading'}>
          <div class="widget-body"><div class="webelement-widget__msg">{t('widgetUi.loading')}</div></div>
        </Match>
        <Match when={view() === 'error'}>
          <div class="widget-body"><div class="webelement-widget__msg webelement-widget__msg--error">{cache.error()}</div></div>
        </Match>
        <Match when={view() === 'notfound'}>
          <div class="widget-body"><div class="webelement-widget__msg" innerHTML={t('webElement.notFound')} /></div>
        </Match>
        <Match when={view() === 'loaded'}>
          <Show when={headerTitle()}>
            <WidgetHeader mode={props.headerMode ?? 'normal'} icon={SquareMousePointer} title={headerTitle()} />
          </Show>
          <div class="widget-body">
            <iframe
              class="webelement-widget__frame"
              sandbox=""
              referrerpolicy="no-referrer"
              srcdoc={cache.data()?.srcdoc ?? ''}
              title={headerTitle()}
            />
          </div>
        </Match>
      </Switch>
    </div>
  );
}
