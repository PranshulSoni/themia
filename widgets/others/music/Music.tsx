import { createSignal, createEffect, onCleanup, onMount, Index } from 'solid-js';
import { appActive } from '../../../src/appActive';
import { Show } from 'solid-js';
import { invoke } from '../../../src/demo/invoke.ts';
import { listen } from '@tauri-apps/api/event';
import { Music as MusicIcon, SkipBack, SkipForward, Play, Pause } from 'lucide-solid';
import { registerWidgetKeyboard, type WidgetKeyEvent } from '../../../src/input/widgetKeyboard.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import { t } from '../../../src/i18n.ts';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { setWidgetHidden } from '../../../src/widgetVisibility.ts';
import { resolveHideWhenIdle, shouldHideMusic } from './hideWhenIdle.ts';
import './Music.css';
import type { JSX } from 'solid-js';

export interface MusicProps {
  pollInterval?: number;
  showAlbumArt?: boolean;
  showSpectrum?: boolean;
  /** `HideWhenIdle`; `unknown` because it crosses the persisted-config trust
   *  boundary and is resolved through `resolveHideWhenIdle` ([S-20]). */
  hideWhenIdle?: unknown;
  label?: string;
  headerMode?: HeaderMode;
  _widgetId?: string;
  _editMode?: boolean;
}

interface NowPlayingInfo {
  title: string;
  artist: string;
  album: string;
  thumbnail_base64: string | null;
  position_ms: number;
  duration_ms: number;
  is_playing: boolean;
  has_next: boolean;
  has_previous: boolean;
}

function fmtTime(ms: number): string {
  const totalSec = Math.floor(ms / 1000);
  const m = Math.floor(totalSec / 60);
  const s = totalSec % 60;
  return `${m}:${s.toString().padStart(2, '0')}`;
}

export default function Music(props: MusicProps): JSX.Element {
  void props.pollInterval; // legacy config field; watcher is event-driven now
  const showArt = () => props.showAlbumArt ?? true;
  const showSpectrum = () => props.showSpectrum ?? false;

  const [info, setInfo] = createSignal<NowPlayingInfo | null>(null);
  const [localPos, setLocalPos] = createSignal(0);
  const [isSeeking, setIsSeeking] = createSignal(false);

  // ── Hide when idle ([S-20]) ──────────────────────────────────────────────
  // The widget paints NOTHING while idle — no header, no body — so the tile is
  // fully transparent rather than an empty card. It reports the state to
  // `widgetVisibility` so the grid drops the tile's border and App.tsx drops it
  // from the window hit-mask (clicks fall through to the desktop). Edit mode
  // suspends hiding entirely, which is how the user gets the widget back.
  const hidden = () => shouldHideMusic(
    resolveHideWhenIdle(props.hideWhenIdle),
    info(),
    !!props._editMode,
  );
  createEffect(() => {
    const id = props._widgetId;
    if (!id) return; // picker preview / demo harness: no tile to hide
    setWidgetHidden(id, hidden());
  });
  // Component-scoped (not inside the effect): the flag must survive the effect
  // re-running and be cleared exactly once, on unmount, so a removed widget
  // can't leave its id hiding a tile forever.
  onCleanup(() => { if (props._widgetId) setWidgetHidden(props._widgetId, false); });

  // Audio spectrum (WASAPI loopback + FFT). Only run the native capture while
  // the toggle is on AND the widget is actually painting; the effect re-runs on
  // toggle and cleans up on unmount. Capturing system audio to drive bars
  // nobody can see is pure waste, and the shared thread is ref-counted
  // ([S-SPEC-05]) so leaving/rejoining is safe.
  const [bands, setBands] = createSignal<number[]>([]);
  createEffect(() => {
    if (!showSpectrum() || hidden()) return;
    invoke('music_start_spectrum').catch(() => {});
    const un = listen<number[]>('music://spectrum', (e) => setBands(e.payload || []));
    onCleanup(() => {
      un.then((u) => u()).catch(() => {});
      invoke('music_stop_spectrum').catch(() => {});
      setBands([]);
    });
  });

  // Anchor for position extrapolation, updated whenever we receive fresh data
  let lastPollTime = 0;
  let lastPollPos = 0;

  function applySnapshot(result: NowPlayingInfo | null) {
    setInfo(result);
    if (result) {
      lastPollTime = performance.now();
      lastPollPos = result.position_ms;
      if (!isSeeking()) setLocalPos(result.position_ms);
    }
  }

  onMount(() => {
    // Start the native GSMTC watcher (idempotent across widget instances)
    invoke('music_start_watcher').catch(() => {});
    // Fetch an initial snapshot so the UI isn't empty until the first event
    invoke('music_get_now_playing')
      .then(applySnapshot)
      .catch(() => setInfo(null));
  });

  // Subscribe to backend-pushed updates (media/playback/timeline/session events)
  const unlistenP = listen<NowPlayingInfo | null>('music://update', (e) => {
    applySnapshot(e.payload);
  });
  onCleanup(() => { unlistenP.then(u => u()).catch(() => {}); });

  // Interpolate position while playing. 2Hz is plenty: the time label only
  // changes once per second and the bar moves sub-pixel between ticks.
  createEffect(() => {
    const cur = info();
    if (!cur?.is_playing) return;

    const id = setInterval(() => {
      if (!appActive() || isSeeking()) return;
      const elapsed = performance.now() - lastPollTime;
      const est = Math.min(lastPollPos + elapsed, info()?.duration_ms ?? 0);
      setLocalPos(est);
    }, 500);
    onCleanup(() => clearInterval(id));
  });

  function progress(): number {
    const dur = info()?.duration_ms ?? 0;
    if (dur <= 0) return 0;
    return Math.min(100, (localPos() / dur) * 100);
  }

  // Control commands: the backend fires PlaybackInfo/MediaProperties events
  // after each action, so no manual refresh is needed.
  async function handlePlayPause() {
    try { await invoke('music_play_pause'); } catch {
      // ignore: no active media session to control
    }
  }

  async function handleNext() {
    try { await invoke('music_next'); } catch {
      // ignore: no active media session to control
    }
  }

  async function handlePrevious() {
    try { await invoke('music_previous'); } catch {
      // ignore: no active media session to control
    }
  }

  function handleBarClick(e: MouseEvent) {
    const bar = e.currentTarget as HTMLElement;
    const rect = bar.getBoundingClientRect();
    const ratio = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
    const seekMs = Math.round(ratio * (info()?.duration_ms ?? 0));
    setLocalPos(seekMs);
    lastPollPos = seekMs;
    lastPollTime = performance.now();
    invoke('music_seek', { positionMs: seekMs }).catch(() => {});
  }

  // ── Keyboard controls ─────────────────────────────────────────────────────
  // Enter/Space toggles play, ←/→ skip tracks, Shift+←/→ seek ±5 s.
  async function handleSeekRelative(deltaMs: number) {
    const d = info();
    if (!d || d.duration_ms <= 0) return;
    const next = Math.max(0, Math.min(d.duration_ms, localPos() + deltaMs));
    setLocalPos(next);
    lastPollPos = next;
    lastPollTime = performance.now();
    try { await invoke('music_seek', { positionMs: Math.round(next) }); } catch {
      // ignore: seek unsupported by the current media session
    }
  }

  function handleKey(e: WidgetKeyEvent): void {
    const tag = document.activeElement?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA') return;
    if (e.key === 'Enter' || e.key === ' ')      { e.preventDefault(); handlePlayPause(); }
    else if (e.key === 'ArrowLeft' && e.shiftKey) { e.preventDefault(); handleSeekRelative(-5000); }
    else if (e.key === 'ArrowRight' && e.shiftKey){ e.preventDefault(); handleSeekRelative(5000); }
    else if (e.key === 'ArrowLeft')  { e.preventDefault(); if (info()?.has_previous) handlePrevious(); }
    else if (e.key === 'ArrowRight') { e.preventDefault(); if (info()?.has_next) handleNext(); }
  }

  let _kbd: ReturnType<typeof registerWidgetKeyboard> | undefined;
  onMount(() => { _kbd = registerWidgetKeyboard(handleKey); });
  onCleanup(() => _kbd?.dispose());
  function activateKbd(e: MouseEvent): void { if (e.button === 0) _kbd?.activate(); }

  function handleBarMouseDown(e: MouseEvent) {
    e.preventDefault();
    setIsSeeking(true);
    handleBarClick(e);

    const bar = (e.currentTarget as HTMLElement);

    function onMove(ev: MouseEvent) {
      const rect = bar.getBoundingClientRect();
      const ratio = Math.max(0, Math.min(1, (ev.clientX - rect.left) / rect.width));
      const seekMs = Math.round(ratio * (info()?.duration_ms ?? 0));
      setLocalPos(seekMs);
    }

    function onUp(ev: MouseEvent) {
      const rect = bar.getBoundingClientRect();
      const ratio = Math.max(0, Math.min(1, (ev.clientX - rect.left) / rect.width));
      const seekMs = Math.round(ratio * (info()?.duration_ms ?? 0));
      setLocalPos(seekMs);
      lastPollPos = seekMs;
      lastPollTime = performance.now();
      invoke('music_seek', { positionMs: seekMs }).catch(() => {});
      setIsSeeking(false);
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
    }

    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', onUp);
  }

  return (
    // [S-20] While hidden the whole tree is unmounted — the header and body ARE
    // the widget's background (App.css), so not rendering them is what makes the
    // tile transparent. The component itself stays alive (signals, the
    // `music://update` listener), so it re-appears the moment playback resumes.
    <Show when={!hidden()}>
    <div class="music-widget" onMouseDown={activateKbd}>
      {/* [S-21] The fallback mirrors the registry `defaultHeaderMode`, so the
          render sites that pass no prop (picker preview, dev harnesses) show the
          same header the grid does. */}
      <WidgetHeader
        mode={props.headerMode ?? 'compact'}
        icon={MusicIcon}
        title={props.label || t('widget.music.label')}
      />
      <div class="widget-body music-body">
      <Show when={info()} fallback={
        <div class="music-empty">
          <MusicIcon size={24} strokeWidth={1.5} />
          <span>{t('music.noMedia')}</span>
        </div>
      }>
        {(data) => (
          <div class="music-content">
            <Show when={showArt() && data().thumbnail_base64}>
              <div class="music-art">
                <img src={data().thumbnail_base64!} alt="" draggable={false} />
              </div>
            </Show>
            <Show when={showArt() && !data().thumbnail_base64}>
              <div class="music-art music-art-placeholder">
                <MusicIcon size={20} strokeWidth={1.5} />
              </div>
            </Show>
            <div class="music-info">
              <div class="music-title">{data().title}</div>
              <div class="music-artist">{data().artist || data().album}</div>
            </div>
            {/* [S-22] Transport rides in the now-playing row, right of the
                title/subtitle, instead of its own band below. */}
            <div class="music-controls">
              <button
                class="music-ctrl-btn"
                disabled={!data().has_previous}
                onClick={handlePrevious}
              >
                <SkipBack size={14} strokeWidth={1.5} />
              </button>
              <button class="music-ctrl-btn music-ctrl-play" onClick={handlePlayPause}>
                <Show when={data().is_playing} fallback={<Play size={16} strokeWidth={1.5} />}>
                  <Pause size={16} strokeWidth={1.5} />
                </Show>
              </button>
              <button
                class="music-ctrl-btn"
                disabled={!data().has_next}
                onClick={handleNext}
              >
                <SkipForward size={14} strokeWidth={1.5} />
              </button>
            </div>
          </div>
        )}
      </Show>

      {/* The spectrum reflects system-wide WASAPI loopback audio, which exists
          independent of a GSMTC media session. Render it whenever the toggle is
          on — matching the always-on capture thread — rather than gating it
          behind info(), so it still animates when audio plays with no session. */}
      <Show when={showSpectrum()}>
        <div class="music-spectrum">
          {/* Index, not For: the band count is fixed (24) and it's the
              per-bar *value* that changes each frame. For keys by value and
              bakes the height in at node-creation, so reused nodes freeze.
              Index keys by position and passes a signal, so height stays
              reactive. */}
          <Index each={bands()}>
            {(b) => <div class="music-bar" style={`height:${Math.max(3, b() * 100)}%`} />}
          </Index>
        </div>
      </Show>

      <Show when={info()}>
        {(data) => (
          <>
            <div class="music-progress">
              <span class="music-time">{fmtTime(localPos())}</span>
              <div class="music-bar-track" onMouseDown={handleBarMouseDown}>
                <div class="music-bar-fill" style={`width: ${progress()}%`} />
              </div>
              <span class="music-time">{fmtTime(data().duration_ms)}</span>
            </div>
          </>
        )}
      </Show>
      </div>
    </div>
    </Show>
  );
}
