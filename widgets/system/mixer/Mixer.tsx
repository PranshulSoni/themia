import { createSignal, createEffect, createMemo, Index, Show } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import {
  SlidersHorizontal, Volume2, Volume1, VolumeX, Bell, AppWindow, RotateCcw,
} from 'lucide-solid';
import { invoke } from '../../../src/demo/invoke.ts';
import { createPolling } from '../../../src/createPolling.ts';
import { logger } from '../../../src/debugLog.ts';
import { showCtxMenu } from '../../../src/ctxMenuStore.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { refreshInterval } from '../refreshInterval.ts';
import { t } from '../../../src/i18n.ts';
import {
  buildRows, applyOverrides, reconcileOverrides, displayPct, clampLevel,
  iconPaths, flag, MASTER_KEY,
  type MixerSession, type MasterState,
} from './mixerRows.ts';
import { createLatestWriter, type LatestWriter } from './latestWriter.ts';
import './Mixer.css';

export interface MixerProps {
  headerMode?: HeaderMode;
  label?: string;
  interval?: number;
  groupByApp?: unknown;
  showMaster?: unknown;
  showSystemSounds?: unknown;
  onlyActive?: unknown;
  showIcons?: unknown;
  _refresh?: number;
}

/** Icon extraction runs at 2x the 20px slot so the shell hands back the crisp
 *  40px variant rather than an upscaled 16px one. */
const ICON_PX = 40;

export default function Mixer(props: MixerProps) {
  const [sessions, setSessions] = createSignal<MixerSession[]>([]);
  const [master, setMaster] = createSignal<MasterState | null>(null);
  const [icons, setIcons] = createSignal<Record<string, string>>({});
  const [overrides, setOverrides] = createSignal<Record<string, number>>({});
  const [loaded, setLoaded] = createSignal(false);
  // Paths already asked for, so a failed or icon-less exe is not re-fetched on
  // every tick. Deliberately a plain Set (non-reactive): mutating it must not
  // re-trigger the effect that fills it.
  const requestedPaths = new Set<string>();

  const interval = () => refreshInterval(props.interval, 'mixer');
  const grouped = () => flag(props.groupByApp, true);
  const showMaster = () => flag(props.showMaster, true);
  const showIcons = () => flag(props.showIcons, true);

  const rows = createMemo(() =>
    applyOverrides(
      buildRows(sessions(), master(), {
        showMaster: showMaster(),
        showSystemSounds: flag(props.showSystemSounds, false),
        onlyActive: flag(props.onlyActive, false),
      }),
      overrides(),
    ),
  );

  // A machine with no output device fails EVERY poll. Logging each one would
  // write a line every couple of seconds for as long as the widget is on the
  // desktop, so a repeat of the same message is dropped and the next success
  // re-arms it — the failure is still reported, once, and again if it changes.
  let lastPollError: string | null = null;
  function reportPollError(message: string | null) {
    if (message === lastPollError) return;
    lastPollError = message;
    if (message) logger.error('mixer', message);
  }

  async function refresh() {
    let failure: string | null = null;
    try {
      const list = await invoke('mixer_get_sessions', { grouped: grouped() });
      setSessions(list);
      setOverrides((prev) => reconcileOverrides(prev, list));
    } catch (e) {
      // No output device, or a browser/demo preview with no backend: keep the
      // last list rather than blanking the widget.
      failure = `mixer_get_sessions failed: ${e}`;
    }
    if (showMaster()) {
      try {
        setMaster(await invoke('volume_get'));
      } catch (e) {
        failure ??= `volume_get failed: ${e}`;
      }
    }
    reportPollError(failure);
    setLoaded(true);
  }

  createPolling(refresh, () => Math.max(1, interval()) * 1000, 'mixer');

  // Instant first paint + the chrome's manual refresh button (createPolling
  // staggers its own first tick).
  createEffect(() => {
    void props._refresh;
    void grouped();
    refresh();
  });

  // [S-13] one batched icon fetch for the executables currently on screen.
  createEffect(() => {
    if (!showIcons()) return;
    const paths = iconPaths(rows()).filter((p) => !requestedPaths.has(p));
    if (!paths.length) return;
    paths.forEach((p) => requestedPaths.add(p));
    invoke('get_file_icons_batch', { paths, size: ICON_PX })
      .then((map) => setIcons((prev) => ({ ...prev, ...map })))
      // Logged, not swallowed: a missing icon degrades to the generic glyph.
      .catch((e) => logger.error('mixer', `get_file_icons_batch failed: ${e}`));
  });

  // ── Writes ────────────────────────────────────────────────────────────────

  // [S-11] one single-flight writer per row, so muting app B never discards the
  // slider value still queued for app A.
  const writers = new Map<string, LatestWriter<number>>();
  function levelWriter(key: string): LatestWriter<number> {
    let w = writers.get(key);
    if (!w) {
      w = createLatestWriter(
        (level: number) =>
          key === MASTER_KEY
            ? invoke('volume_set', { level })
            : invoke('mixer_set_volume', { key, level }),
        (err) => logger.error('mixer', `set volume failed: ${err}`),
      );
      writers.set(key, w);
    }
    return w;
  }

  function setMuted(row: MixerSession, muted: boolean) {
    const call = row.key === MASTER_KEY
      ? invoke('volume_set_mute', { muted })
      : invoke('mixer_set_mute', { key: row.key, muted });
    // Refresh on completion rather than optimistically: the command resolves
    // only after Windows applied it, so the next read is the truth.
    call.then(refresh).catch((e) => logger.error('mixer', `set mute failed: ${e}`));
  }

  function onInput(row: MixerSession, e: InputEvent & { currentTarget: HTMLInputElement }) {
    const level = clampLevel(Number(e.currentTarget.value) / 100);
    setOverrides((prev) => ({ ...prev, [row.key]: level }));
    // [S-12] a muted row that is dragged up unmutes, the same way the volume
    // widget behaves — a slider that moves but stays silent reads as broken.
    if (row.muted && level > 0) setMuted(row, false);
    levelWriter(row.key)(level);
  }

  function onRowContextMenu(e: MouseEvent, row: MixerSession) {
    e.preventDefault();
    e.stopPropagation();
    showCtxMenu(e, [
      {
        icon: row.muted ? Volume2 : VolumeX,
        label: row.muted ? t('mixer.unmute') : t('mixer.mute'),
        action: () => setMuted(row, !row.muted),
      },
      {
        icon: RotateCcw,
        label: t('mixer.reset'),
        action: () => {
          setOverrides((prev) => ({ ...prev, [row.key]: 1 }));
          if (row.muted) setMuted(row, false);
          levelWriter(row.key)(1);
        },
      },
    ]);
  }

  // ── Render ────────────────────────────────────────────────────────────────

  function rowName(row: MixerSession): string {
    if (row.key === MASTER_KEY) return t('mixer.master');
    if (row.is_system) return t('mixer.systemSounds');
    return row.name || t('mixer.unknownApp');
  }

  function rowIcon(row: MixerSession) {
    if (row.key === MASTER_KEY) return Volume2;
    if (row.is_system) return Bell;
    return AppWindow;
  }

  function muteIcon(row: MixerSession) {
    if (row.muted) return VolumeX;
    return displayPct(row) < 50 ? Volume1 : Volume2;
  }

  return (
    <div class="mixer-widget">
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={SlidersHorizontal}
        title={props.label || t('widget.mixer.label')}
      />
      <div class="widget-body mixer-body">
        <Show when={loaded()} fallback={<div class="mixer-msg">{t('widgetUi.loading')}</div>}>
          <Show when={rows().length} fallback={<div class="mixer-msg">{t('mixer.empty')}</div>}>
            {/* [S-11] `Index`, NOT `For`: every poll and every drag event
                produces fresh row objects, and `For` keys by identity, so it
                would tear down and rebuild the whole list — including the
                <input> the user is currently dragging, which drops the drag
                after the first mouse move. `Index` keys by position and hands
                each row down as an accessor, so only the bound values update. */}
            <Index each={rows()}>
              {(row) => (
                <div
                  class="mixer-row"
                  classList={{ 'is-muted': row().muted, 'is-active': row().active }}
                  onContextMenu={(e) => onRowContextMenu(e, row())}
                >
                  <Show when={showIcons()}>
                    <div class="mixer-icon">
                      <Show
                        when={row().path && icons()[row().path!]}
                        fallback={<Dynamic component={rowIcon(row())} size={16} strokeWidth={1.5} />}
                      >
                        <img src={icons()[row().path!]} alt="" draggable={false} />
                      </Show>
                    </div>
                  </Show>
                  <div class="mixer-main">
                    <div class="mixer-top">
                      <span class="mixer-name" title={rowName(row())}>{rowName(row())}</span>
                      <Show when={row().sessions > 1}>
                        {/* [S-03] how many sessions this one slider drives. */}
                        <span class="mixer-count" title={t('mixer.sessions', { count: row().sessions })}>
                          {row().sessions}
                        </span>
                      </Show>
                      <span class="mixer-pct">{displayPct(row())}<span class="mixer-unit">%</span></span>
                      <button
                        class="mixer-mute"
                        onClick={() => setMuted(row(), !row().muted)}
                        title={row().muted ? t('mixer.unmute') : t('mixer.mute')}
                      >
                        <Dynamic component={muteIcon(row())} size={14} strokeWidth={1.5} />
                      </button>
                    </div>
                    <input
                      class="mixer-slider"
                      type="range"
                      min="0"
                      max="100"
                      value={displayPct(row())}
                      onInput={(e) => onInput(row(), e)}
                      style={`--mix-pct:${displayPct(row())}%`}
                      aria-label={rowName(row())}
                    />
                  </div>
                </div>
              )}
            </Index>
          </Show>
        </Show>
      </div>
    </div>
  );
}
