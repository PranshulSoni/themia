# Video Stream: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

Requested in themia-releases #102: a live view of an MJPEG-over-HTTP endpoint —
the format served by ustreamer, camera-streamer, mjpg-streamer, most IP cameras,
and the printer UIs (Mainsail / Fluidd / OctoPrint) the request named.

## Display
- [S-01] Header title is the `label` override, else the stream's `host:port` (how a LAN camera is actually recognised), else the widget name. Pure `streamTitle` in `videoStream.ts`.
- [S-02] With no usable URL the body shows the empty state ("Add a camera stream URL…", click ⚙).
- [S-03] From the moment a connection is opened until the first frame decodes, a "Connecting…" overlay covers the frame. The `<img>` itself stays at `opacity: 0` until `data-phase="live"`, so the webview's broken-image glyph never flashes through.
- [S-04] Once live, the picture fills the frame with `object-fit` from `fit`. No controls, no chrome: the widget is the picture.
- [S-16] Three fit modes, mapped by the pure `objectFitFor`: `contain` → letterbox (default), `cover` → crop, `stretch` → CSS `fill`, i.e. distort to both axes. The config name says what it does to the picture rather than repeating the CSS keyword.
- [S-05] On a failed/dropped connection the overlay reads "Stream unavailable" + "Reconnecting…" and the picture is hidden.

## Transport
- [S-14] MJPEG (`multipart/x-mixed-replace`) is decoded by the webview itself, so the entire widget is one `<img src=…>`: no Rust crate, no `invoke`, no per-frame IPC, no `src/ipc.ts` entry. `referrerpolicy="no-referrer"` so a camera never learns the app's internal page URL.
- [S-12] The app CSP's `img-src` includes `http:` (`src-tauri/tauri.conf.json`). Self-hosted cameras and printer hosts are plaintext HTTP on the LAN essentially without exception, so without it the widget can never load the streams the request is about. The widening is images-only; `script-src`/`connect-src`/`frame-src` are untouched, and `default-src 'self'` still governs everything else. The app page is served over `http://tauri.localhost`, which is not a cryptographic origin, so no mixed-content block applies to the plaintext stream.
- [S-10] A reconnect mounts a **new** `<img>` element (`<Show keyed>` on a per-attempt `Connection` object) rather than re-assigning `src`. Re-assigning the same URL does not re-open a multipart stream the server has already closed.
- [S-18] Tearing the element out of the DOM does **not** abort its load in Chromium, and an MJPEG response never finishes on its own — so removal alone leaves the socket open and the camera pushing frames into nothing. The keyed child's `onCleanup` clears `src`, which is what actually cancels it, and that one hook covers every teardown path: pause, URL change, interval hang-up, retry, and widget unmount. The element is added to a `cancelled` WeakSet first, because our own cancellation surfaces as an `error` event on the detached element and must not trip the [S-06] backoff.
  - Measured, since this is invisible from the DOM: streaming to a local MJPEG server and polling `Get-NetTCPConnection`, interval mode without the cleanup stacked up 6 never-closed connections in ~20s and the server logged no disconnects; with it, the server logs a matched connect/disconnect per grab, `0 left` in between, and zero sockets survive the run. Pausing a live stream likewise drops to zero — the claim [S-07] makes about bandwidth only holds because of this.

## Reconnect
- [S-06] An `error` event arms an exponential backoff — 2s, 4s, 8s … capped at 30s (`retryDelayMs`) — and each retry mounts a fresh element. The counter resets to zero on the first decoded frame, on a URL/pause change, and on a manual refresh. The timer is ARMED BY the error event and disarmed on success/pause/unmount, so it never sequences state (CLAUDE.md: no wall-clock timing in correctness paths).
- [S-11] The context-menu "Refresh" action bumps `_refresh`, which reconnects immediately with the backoff reset (wired through the shared `createManualRefresh`).

## Configuration
- [S-09] `resolveConfig` is the single trust boundary: the user-entered `url` reaches `<img src>` only if it parses as an absolute `http:`/`https:` URL. `javascript:`, `data:`, `file:`, `rtsp:` and unparseable text all collapse to `''`, which renders as [S-02]; the config panel additionally shows an inline "Enter a full http:// or https:// URL." hint. In live mode the accepted URL is passed through byte-identical apart from trimming — cameras route on the query string (`?action=stream`), so it is never normalised. (Interval mode is the one exception, and appends exactly one parameter; see [S-17].)
- [S-07] `paused` (default false) is **persisted**, so a parked camera stays parked across restarts and costs no bandwidth; the body then reads "Paused". Toggled from the context menu only — it is a state, not a setting.
- [S-08] `rotation` ∈ {0, 90, 180, 270} for sideways/upside-down cameras (a 3D-printer webcam is routinely mounted either way). 90/270 size the picture against the frame's opposite axis via container-query units (`100cqh`/`100cqw`), so a rotated stream still fits instead of being clipped. Settable in the config panel and cycled by the context-menu "Rotate" action.
- [S-13] In demo mode / the Add-Widget preview the source is swapped for `demoVideoFrame()`, an inline-SVG data URI, so the preview shows a plausible picture with no camera and no network. The picture is a real photograph, base64'd into the SVG rather than referenced as a bundled asset: SVG loaded through `<img>` runs in secure static mode and drops external references, so a URL would not paint. The SVG wrapper remains because it carries the camera chrome (record dot, `CAM 1`, `LIVE 1080p`) and its scrim over the photo.

## Cost
- [S-15] A live MJPEG stream is a persistent HTTP connection that decodes continuously for as long as the widget is on screen; there is no desktop-side "widget is occluded" signal to auto-pause against. [S-17] and [S-07] are the mitigations, and the default size is deliberately small (320×240).
- [S-17] `refreshSeconds` (0–300, default 0) caps the framerate. **0 = live**: hold the connection open, decode every frame the camera sends. **Any other value = interval mode**: open the connection, decode ONE frame, copy it onto a `<canvas>`, unmount the loading `<img>` (which is what actually closes the socket), and wait. The canvas is what stays on screen in between, so the picture never blanks.
  - This shape is forced by the transport: the webview decodes whatever the server pushes and offers no "give me fewer frames" control, so the only honest throttle is to stop holding the connection open. It therefore works for a plain snapshot endpoint (`?action=snapshot`, `/axis-cgi/jpg/image.cgi`) **and** for a full MJPEG stream URL — the latter just gets hung up on after its first frame.
  - Interval mode is the sole caller of `withCacheBuster`, which appends one `_t=<n>` parameter. Without it the webview's memory cache re-serves the previous response and the picture freezes; a re-mounted `<img>` on an unchanged URL is not a reliable re-request. Servers ignore unknown query parameters, but this is why [S-09]'s byte-identical guarantee is scoped to live mode. A non-http URL (the demo data: frame) is returned untouched.
  - The loader `<img>` is `display: none` (a hidden image still loads) and the canvas carries the same `.vs-img` class as the live image, so scaling, rotation and the reveal rule have exactly one definition for both modes.
  - A failed grab takes the normal [S-06] path: error state, backoff, retry. A zero-sized decode or an unavailable 2d context leaves the previous frame up rather than blanking it; the next grab repaints.

## Open / unverified
- [U-01] RTSP (the other half of #102's "if feasible") is **not** supported: no browser engine speaks RTSP, so it would need a Rust-side transcode to MJPEG/WebRTC and its own transport. Out of scope here; the issue's own framing is that MJPEG covers the majority of self-hosted/DIY cases.
- [U-02] A stream that *stalls* without the connection closing fires no `error`, so [S-06] cannot see it and the last frame stays on screen. Detecting it needs a frame-arrival watchdog, which is exactly the wall-clock polling the codebase avoids; "Refresh" is the manual escape hatch.
- [U-03] Streams behind HTTP Basic auth are not reachable: Chromium strips `user:pass@` credentials from subresource URLs, and an `<img>` cannot carry an `Authorization` header. Needs a Rust-side proxy to support.
- [U-04] Verified end-to-end against a real `multipart/x-mixed-replace` server on loopback: the widget reaches `data-phase="live"`, decodes 640×360 frames, and the frames keep advancing (four distinct canvas hashes over ~2.7s). **Not** verified against actual camera hardware on a LAN — a real camera adds slow/partial first frames, mid-stream stalls and vendor quirks that a local generator does not reproduce.
