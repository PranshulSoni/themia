import { createMemo, createSignal, onMount, onCleanup, For, Show, Switch, Match } from 'solid-js';
import { createPolling } from '../../../src/createPolling';
import { createCachedFetch } from '../../../src/createCachedFetch';
import { createManualRefresh } from '../../office/calendar/manualRefresh.ts';
import { invoke, isDemo } from '../../../src/demo/invoke.ts';
import { demoClaudeUsage } from '../../../src/demo/fixtures.ts';
import { globalDesign } from '../../../src/design.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import {
  utilizationPct, usageSeverity, resetLabel, clampRefreshMinutes, usageView, buildBars,
  usageDensity, labelSize, barLabel,
  type ClaudeUsage as Usage, type UsageView,
} from './claudeUsage.ts';
import './ClaudeUsage.css';
import { t } from '../../../src/i18n.ts';

export interface ClaudeUsageProps {
  headerMode?: HeaderMode;
  accountIds?: string[];
  refreshMinutes?: number;
  label?: string;
  showOpus?: boolean;
  showSonnet?: boolean;
  _refresh?: number;
}

export default function ClaudeUsage(props: ClaudeUsageProps) {
  const refreshMs = createMemo(() => clampRefreshMinutes(props.refreshMinutes) * 60_000);

  function firstAccountId(): string | null {
    const ids = props.accountIds;
    return Array.isArray(ids) && ids.length > 0 ? ids[0] : null;
  }

  const cache = createCachedFetch<Usage>({
    namespace: 'claude-usage',
    // Account id keys the cache; the token is never part of the key (no
    // fingerprinting by secrets: it lives only in the backend store).
    key: () => {
      if (isDemo()) return 'demo';
      const acc = firstAccountId();
      return acc ? acc : null;
    },
    staleMs: refreshMs,
    fetcher: async (_key, _signal) => {
      if (isDemo()) return demoClaudeUsage();
      const accountId = firstAccountId();
      if (!accountId) throw new Error('No Claude account');
      return invoke('email_get_claude_usage', { accountId });
    },
  });

  createPolling(() => { void cache.refresh(true); }, refreshMs, 'claude-usage');

  // [S-13] Widget-level "Refresh" context action bumps the `_refresh` config
  // token; force a re-fetch when it changes. The mount run is skipped
  // (createCachedFetch's own effect covers first load), so a `_refresh` token
  // persisted in config from a previous session can't force a redundant network
  // fetch on app start.
  createManualRefresh(() => props._refresh, () => { void cache.refresh(true); });

  const bars = createMemo(() => {
    const u = cache.data();
    return u ? buildBars(u, !!props.showOpus, !!props.showSonnet) : [];
  });

  const view = createMemo<UsageView>(() => usageView({
    hasAccount: isDemo() || firstAccountId() !== null,
    hasData: cache.data() !== null,
    hasError: cache.error() !== null,
  }));

  // [S-17] The rows are laid out to FIT the box they were given; the widget
  // never scrolls. `.claude-usage__rows` is sized purely by the flex parent
  // (flex: 1 + min-height: 0) and carries no padding, so measuring it can't
  // feed back into what the density then renders inside it.
  // NaN = not measured yet, which is NOT the same as a measured zero-height box
  // (a widget too short to hold anything) — see usageDensity.
  const [box, setBox] = createSignal({ w: NaN, h: NaN });
  let rowsEl: HTMLDivElement | undefined;

  onMount(() => {
    const el = rowsEl;
    if (!el) return;
    // Measure once synchronously so the FIRST paint already uses the right
    // density (a ResizeObserver's first callback can be a frame late).
    setBox({ w: el.clientWidth, h: el.clientHeight });
    const ro = new ResizeObserver((entries) => {
      const r = entries[0]?.contentRect;
      if (r) setBox({ w: r.width, h: r.height });
    });
    ro.observe(el);
    onCleanup(() => ro.disconnect());
  });

  // The global font scale is part of the budget: at 1.4 a row is ~40% taller,
  // so the same box holds fewer of them before a part has to go.
  const density = createMemo(() => usageDensity(box().h, bars().length, globalDesign.fontScale));
  // [S-18] Width picks the label variant, independently of the height density.
  const lblSize = createMemo(() => labelSize(box().w, density(), globalDesign.fontScale));

  return (
    <div class="claude-usage-widget">
      <WidgetHeader mode={props.headerMode ?? 'compact'} title={props.label || 'Claude Usage'} />

      <div class="claude-usage-body widget-body">
        <div
          ref={rowsEl}
          class="claude-usage__rows"
          classList={{ [`claude-usage__rows--${density()}`]: true }}
        >
          <Switch>
            <Match when={view() === 'empty'}>
              <div class="claude-usage__state">{t('claudeUsage.signIn')}</div>
            </Match>
            <Match when={view() === 'loading'}>
              <div class="claude-usage__state">Loading…</div>
            </Match>
            <Match when={view() === 'error'}>
              <div class="claude-usage__state claude-usage__state--error">{String(cache.error())}</div>
            </Match>
            <Match when={view() === 'loaded'}>
              <For each={bars()}>
                {(bar) => {
                  const pct = createMemo(() => utilizationPct(bar.win.utilization));
                  const sev = createMemo(() => usageSeverity(pct()));
                  // [S-18] A narrow widget drops the "resets in " prefix.
                  const reset = createMemo(() =>
                    resetLabel(bar.win.resets_at, Date.now(), lblSize() !== 'full'));
                  return (
                    <div class="claude-usage__row" title={`${bar.label} — ${Math.round(pct())}%`}>
                      <span class="claude-usage__row-label">{barLabel(bar, lblSize())}</span>
                      <span class="claude-usage__row-pct">{Math.round(pct())}%</span>
                      <div class="claude-usage__track">
                        <div
                          class="claude-usage__fill"
                          classList={{
                            'claude-usage__fill--warn': sev() === 'warn',
                            'claude-usage__fill--crit': sev() === 'crit',
                          }}
                          style={{ width: `${pct()}%` }}
                        />
                      </div>
                      {/* [S-05] Only the loosest density has room for the reset
                          line, and it keeps its slot even when this window has no
                          reset, so every row in the widget stays the same height. */}
                      <Show when={density() === 'full'}>
                        <span class="claude-usage__reset">{reset() || ' '}</span>
                      </Show>
                    </div>
                  );
                }}
              </For>
            </Match>
          </Switch>
        </div>
      </div>
    </div>
  );
}
