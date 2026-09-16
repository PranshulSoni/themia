import { createSignal, createMemo, createEffect, untrack, onCleanup, Show, Switch, Match } from 'solid-js';
import { Video } from 'lucide-solid';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import { createManualRefresh } from '../../office/calendar/manualRefresh.ts';
import { isDemo } from '../../../src/demo/invoke.ts';
import { demoVideoFrame } from '../../../src/demo/fixtures.ts';
import { t } from '../../../src/i18n.ts';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { resolveConfig, retryDelayMs, streamTitle, objectFitFor, withCacheBuster } from './videoStream.ts';
import './VideoStream.css';

interface VideoStreamProps {
  headerMode?: HeaderMode;
  url?: string;
  label?: string;
  fit?: string;
  rotation?: number;
  paused?: boolean;
  refreshSeconds?: number;
  _refresh?: number;
}

type Phase = 'connecting' | 'live' | 'error';

/** One connection attempt. A NEW object per attempt so `<Show keyed>` replaces
 *  the <img> ELEMENT rather than re-assigning its `src`: re-assigning the same
 *  URL does not re-open a multipart stream the server already closed. */
interface Connection {
  src: string;
  gen: number;
}

export default function VideoStream(props: VideoStreamProps) {
  const cfg = createMemo(() => resolveConfig(props));

  // MJPEG-over-HTTP (multipart/x-mixed-replace) is decoded by the webview
  // itself, so the whole widget is an <img> — no Rust, no per-frame IPC. Demo /
  // Add-Widget-preview swaps in a still stand-in frame so the preview needs
  // neither a camera nor a network.
  const src = createMemo(() => (isDemo() ? demoVideoFrame() : cfg().url));

  // Interval mode ([S-17]). The webview decodes every frame the server pushes
  // and offers no way to ask for fewer, so the only real throttle is to stop
  // holding the connection open: grab one frame, paint it to a canvas, hang up,
  // wait. The canvas is what stays on screen in between.
  const interval = createMemo(() => cfg().refreshSeconds > 0);
  const active = createMemo(() => !!src() && !cfg().paused);

  const [phase, setPhase] = createSignal<Phase>('connecting');
  const [generation, setGeneration] = createSignal(0);
  /** Interval mode holds no socket between grabs; true only while one is open. */
  const [grabbing, setGrabbing] = createSignal(true);
  let attempt = 0;
  let canvas: HTMLCanvasElement | undefined;
  /** Loaders we cancelled ourselves, so their resulting `error` event is not
   *  mistaken for the camera failing. */
  const cancelled = new WeakSet<HTMLImageElement>();

  let retryTimer: ReturnType<typeof setTimeout> | null = null;
  let pollTimer: ReturnType<typeof setTimeout> | null = null;
  function clearTimers(): void {
    if (retryTimer !== null) { clearTimeout(retryTimer); retryTimer = null; }
    if (pollTimer !== null) { clearTimeout(pollTimer); pollTimer = null; }
  }
  onCleanup(clearTimers);

  /** Mount a fresh <img>, which is what actually opens a connection. */
  function openConnection(): void {
    clearTimers();
    setGrabbing(true);
    setGeneration(g => g + 1);
  }

  /** Start over from scratch: new connection, blank slate, backoff reset. */
  function restart(): void {
    attempt = 0;
    setPhase('connecting');
    openConnection();
  }

  // Any change to what we are connecting to (or how) starts over.
  createEffect(() => {
    void src();
    void cfg().paused;
    void cfg().refreshSeconds;
    untrack(restart);
  });

  createManualRefresh(() => props._refresh, restart);

  /** Copy the decoded frame onto the canvas so it survives the hang-up. */
  function paint(img: HTMLImageElement): void {
    const c = canvas;
    if (!c) return;
    const w = img.naturalWidth;
    const h = img.naturalHeight;
    // A zero-sized decode or a lost 2d context leaves the previous frame up;
    // the next grab repaints. Never throws, never blanks what is already shown.
    if (!w || !h) return;
    if (c.width !== w) c.width = w;
    if (c.height !== h) c.height = h;
    const ctx = c.getContext('2d');
    if (!ctx) return;
    ctx.drawImage(img, 0, 0, w, h);
  }

  function onFrame(img: HTMLImageElement): void {
    // Chromium fires `load` once the first part of a multipart response has
    // decoded, so this IS the "stream is up" signal, not just "bytes arrived".
    clearTimers();
    attempt = 0;
    if (interval()) {
      paint(img);
      setGrabbing(false);   // unmounting the loader is what closes the socket
      pollTimer = setTimeout(
        () => { pollTimer = null; openConnection(); },
        cfg().refreshSeconds * 1000,
      );
    }
    setPhase('live');
  }

  function onFailure(img: HTMLImageElement): void {
    // Our own cancellation ([S-18]) surfaces as an error event on a detached
    // element. That is not the camera failing, so it must not trip the backoff.
    if (cancelled.has(img)) return;
    // Camera rebooting, wrong URL, Wi-Fi drop: back off and retry. The timer is
    // ARMED BY this event and disarmed on success/pause/unmount, so it never
    // sequences state (CLAUDE.md: no wall-clock timing in correctness paths).
    clearTimers();
    setGrabbing(false);
    setPhase('error');
    attempt += 1;
    retryTimer = setTimeout(() => { retryTimer = null; openConnection(); }, retryDelayMs(attempt));
  }

  const connection = createMemo<Connection | null>(() => {
    const url = src();
    if (!url || cfg().paused) return null;
    if (interval() && !grabbing()) return null;   // hung up until the next grab
    const gen = generation();
    // Live mode hands the camera its URL byte-identical ([S-09]); only interval
    // mode needs the cache-buster, and only because it re-requests ([S-17]).
    return { src: interval() ? withCacheBuster(url, gen) : url, gen };
  });

  const title = createMemo(() => streamTitle(cfg().url, cfg().label, t('widget.videoStream.label')));

  return (
    <div class="videostream-widget">
      <WidgetHeader mode={props.headerMode ?? 'compact'} icon={Video} title={title()} />
      <div class="widget-body videostream-body">
        <div
          class="vs-frame"
          data-rot={cfg().rotation}
          data-phase={active() ? phase() : 'idle'}
        >
          <Show when={connection()} keyed>
            {(c) => {
              let el: HTMLImageElement | undefined;
              // [S-18] Dropping the element out of the DOM does NOT abort its
              // load in Chromium, and an MJPEG stream never finishes on its
              // own, so every teardown path — pause, URL change, interval
              // hang-up, retry, widget unmount — would leak an open socket that
              // keeps the camera pushing frames. Clearing `src` is what
              // actually cancels it. Doing this in the keyed child's cleanup
              // covers all of those paths with one mechanism.
              onCleanup(() => {
                if (!el) return;
                cancelled.add(el);
                el.src = '';
              });
              return (
                <img
                  ref={el}
                  class={interval() ? 'vs-loader' : 'vs-img'}
                  src={c.src}
                  alt=""
                  referrerpolicy="no-referrer"
                  style={interval() ? undefined : { 'object-fit': objectFitFor(cfg().fit) }}
                  onLoad={(e) => onFrame(e.currentTarget)}
                  onError={(e) => onFailure(e.currentTarget)}
                />
              );
            }}
          </Show>

          <Show when={interval()}>
            <canvas
              class="vs-img"
              ref={canvas}
              style={{ 'object-fit': objectFitFor(cfg().fit) }}
            />
          </Show>

          <Switch>
            <Match when={!src()}>
              <div class="vs-msg" innerHTML={t('videoStream.empty')} />
            </Match>
            <Match when={cfg().paused}>
              <div class="vs-msg">{t('videoStream.paused')}</div>
            </Match>
            <Match when={phase() === 'connecting'}>
              <div class="vs-msg">{t('videoStream.connecting')}</div>
            </Match>
            <Match when={phase() === 'error'}>
              <div class="vs-msg">
                <span class="vs-msg__head">{t('videoStream.offline')}</span>
                <span class="vs-msg__sub">{t('videoStream.retrying')}</span>
              </div>
            </Match>
          </Switch>
        </div>
      </div>
    </div>
  );
}
