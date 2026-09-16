# Now Playing Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Shows current media title and artist/album, with transport controls (prev / play-pause / next) and a seekable progress bar with elapsed/total time labels.
- [S-22] The transport cluster sits inside the now-playing row, right of the
  title/subtitle (art | title+artist | prev/play/next), not in a separate band
  below it. `.music-info` takes the spare width so the cluster is right-aligned;
  the cluster never shrinks, so a long title ellipsizes instead. Below 170px
  container width the buttons tighten (gap 0, smaller padding) alongside the
  album art hiding.
- [S-02] When nothing is playing, renders a "No media playing" placeholder with a music icon.
- [S-03] Album art is shown as a thumbnail when `showAlbumArt` is on and thumbnail data is present; shows a placeholder music icon tile when art is unavailable.
- [S-04] Play/pause button shows Pause when playing, Play when paused.
- [S-05] Prev and next buttons are disabled when `has_previous` / `has_next` are false.
- [S-06] Progress time is interpolated via requestAnimationFrame while playing, anchored to the last known position and pause-frozen while the app window is inactive or while user is dragging.
- [S-07] Times formatted as `M:SS`.

## Configuration
- [S-08] `showAlbumArt` toggle hides/shows the artwork area.
- [S-09] `hideHeader` toggle hides the widget title bar.
- [S-10] `pollInterval` segmented control (1s / 2s / 5s), legacy; watcher is event-driven, value currently unused.
- [S-20] `hideWhenIdle` (`never` | `noSession` | `notPlaying`, default `never`,
  themia-releases#97) makes the widget paint NOTHING while idle rather than show
  the [S-02] placeholder: `noSession` hides only when Windows reports no media
  session at all; `notPlaying` also hides a paused one (the card a media app
  leaves sitting there after you stop). The value crosses the persisted-config
  trust boundary, so it is read through `resolveHideWhenIdle`, which falls back
  to `never` — the visible, recoverable state — on any unrecognised value.
  While hidden:
  - the whole tree is unmounted (header + body ARE the widget's background), the
    grid tile drops its border and pointer events (`.widget.is-hidden`), and the
    tile is excluded from the window hit-mask, so clicks land on the Windows
    desktop instead of an invisible box. The widget reports its state through
    `src/widgetVisibility.ts`; the grid and the rect push only react.
  - the spectrum capture thread is released ([S-SPEC-05] ref-counting), since
    nobody can see the bars.
  - the component itself stays alive, so the `music://update` listener
    re-reveals it the moment playback resumes.
  - EDIT MODE ALWAYS WINS: hiding is suspended for the whole time the grid is
    editable, which is the one way back to a widget that has hidden itself.
    The widget owns that rule, so there is exactly one source of truth.
- [S-21] Registry `defaultHeaderMode: 'compact'` (WidgetHeader.SPEC [S-12]/[S-16]):
  the title bar is pure chrome here — the track title sits directly beneath it and
  already identifies the widget — so the slim label-led band is the default and
  the 40px `normal` band is opt-in. Instances carrying an explicit `headerMode`
  (or legacy `hideHeader`) are unaffected; only the unset default moves. The
  component's own `props.headerMode ?? 'compact'` fallback mirrors the registry
  value so prop-less render sites (picker preview, dev harnesses) agree with the
  grid.

## Data / backend
- [S-11] On mount, calls `music_start_watcher` (idempotent) and fetches initial snapshot via `music_get_now_playing`.
- [S-12] Listens to Tauri event `music://update` for pushed `NowPlayingInfo` snapshots.
- [S-13] Transport buttons call `music_play_pause`, `music_next`, `music_previous`.
- [S-14] Seeking calls `music_seek` with `positionMs`.

## Interactions
- [S-15] Clicking the progress bar track seeks to the clicked position.
- [S-16] Mouse-drag on the progress bar performs live scrubbing; release commits the seek.
- [S-17] Keyboard (when widget has activation): Enter or Space toggles play/pause; Left/Right arrows jump previous/next track; Shift+Left/Right seeks ±5s.
- [S-18] Keyboard input is ignored when an INPUT or TEXTAREA has focus.
- [S-19] Left-clicking the widget surface activates the keyboard handler.

## Audio spectrum (experimental)

- **[S-SPEC-01]** A `showSpectrum` config toggle (default off) starts/stops a
  native WASAPI loopback capture thread (`music_start_spectrum` /
  `music_stop_spectrum`); the capture thread is idempotent and stops on toggle-off
  and unmount.
- **[S-SPEC-02]** The backend emits `music://spectrum` (a `Vec<f32>` of 24 band
  levels, 0..1) at ~30 fps from a windowed FFT; the widget renders them as bars.
- **[S-SPEC-03]** The capture thread balances COM (drop guard), uses an
  `AtomicBool` stop flag, and is wrapped in `catch_unwind`; a capture failure
  emits `music://spectrum-error` and never crashes the app.
- **[S-SPEC-04]** The capture thread is self-healing: a transient capture error
  (default render endpoint change / output-device switch, mix-format change,
  device invalidation) re-acquires the endpoint and retries (with backoff) until
  the stop flag is set, rather than exiting and freezing the bars. `start()`
  reaps a finished worker handle and respawns instead of treating it as still
  running, so the spectrum recovers without a full app restart.
- **[S-SPEC-05]** The shared capture thread is reference-counted: `start()` adds
  a subscriber, `stop()` removes one, and the thread is torn down only when the
  last subscriber leaves. A second widget instance, a view switch, or a
  config-driven remount (which overlaps the new instance's `start()` with the old
  instance's `stop()`) therefore never kills the spectrum for the instances that
  remain. Ref count is carried across a respawn after a worker death.
