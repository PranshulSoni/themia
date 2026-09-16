# Gallery Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Source
- [S-31] `source` is a closed set: `folder` (scan a directory, multi-image, default), `file` (one local image), `bing` (Bing's daily "Photo of the Day"). Unset legacy configs default to `folder`.
- [S-32] `folder` source uses `path` + `recursive`; `file` source uses `filePath`; `bing` source needs no per-widget setup (`isConfigured` is always true for it).
- [S-33] `file` and `bing` are single-image sources: they render one full-bleed image (the `.gallery-single` view) honoring `fit`, bypassing the grid/slideshow/scatter/mosaic layouts entirely.
- [S-34] `bing` source fetches the same Bing HPImageArchive JSON as the standalone Photo-of-the-Day widget via the `http_get` backend command (parsed through a typed shape, fetch/parse failure logged + shows "Could not load photo", never throws); image = `https://www.bing.com` + returned path.
- [S-35] `bing` source shows an optional title+credit caption overlay, toggled by `showCaption` (default on); `file`/`folder` sources never show a caption.
- [S-36] Layout switching (config Display "Layout", widget context menu "View" section, registry context actions) and "Max images" are shown only for the `folder` source.

## Display
- [S-01] Four layout modes (folder source only): grid (tiled thumbnails), slideshow (single image + prev/pause/next controls + counter), scatter (randomly positioned, rotated photos), mosaic (recursively subdivided rectangular cells).
- [S-02] When no folder is configured (folder source, not demo), shows "Select folder to see your images. Click ⚙ to configure."; the `file` source with no image shows "Select an image to display. Click ⚙ to configure."
- [S-03] While loading with no cached images, shows "Loading...".
- [S-04] When the scan returns zero images, shows "No images found".
- [S-05] Scatter layout randomizes position (centered around middle, radius ≤28%), size (25–45%), and rotation (±12°) per image on each generation.
- [S-06] Mosaic layout subdivides a 100×100 canvas recursively, alternating axis with a randomized 0.35–0.65 split ratio, one image per leaf cell.
- [S-07] Up to 20 images are ever rendered (`maxImages` clamped to [1,20]); if source folder has more, a random subset is selected.
- [S-08] Slideshow shows the current index as `N / total` in the controls bar.
- [S-09] Slideshow advances automatically on an interval (`slideshowInterval` seconds, min 1), paused when user-paused or when there is ≤1 image.
- [S-10] `fit: cover` or `fit: contain` sets the CSS object-fit of each image.

## Configuration
- [S-11] `path`: folder picker ("Browse…") that invokes the Tauri directory dialog and stores the selection.
- [S-12] After folder picking, calls `push_windows_to_desktop`.
- [S-13] `recursive` toggle includes subfolders in the image scan.
- [S-14] `mode`: segmented Grid / Slideshow / Scatter / Mosaic.
- [S-15] `fit`: segmented Cover / Contain. Applies to every source (folder grid/slideshow/scatter, Single image, Photo of the Day); shown in the Display tab directly under Layout. Folder+mosaic ignores it (always Cover) and hides the control.
- [S-16] `maxImages`: numeric + range slider (1–20, default 12).
- [S-17] Mosaic tiles render full-bleed (no inter-tile gap); spacing is not configurable.
- [S-18] `slideshowInterval` (≥1 s, default 10), shown only in slideshow mode.
- [S-18a] Slideshow auto-advances and arrow-key/prev-next nav update the *displayed image*: the single `<img>` is reused across slides, so the image element reacts to `src`/`path` changes, not just the index counter.
- [S-19] `refreshInterval`: segmented 5m / 15m / 30m / 1h for periodic rescan.
- [S-20] `hideHeader` toggle hides the widget title bar.

## Context menu
- [S-21] Widget context actions include layout switcher (Grid / Slideshow / Scatter / Mosaic) and `Refresh images` (updates `refreshNonce` to current timestamp).
- [S-22] Right-clicking an image shows: Open, Show in Explorer, then layout-switch section (Grid/Slideshow/Scatter/Mosaic marked active), Refresh images, Configure, Remove widget (danger, with `window.confirm`).

## Data / backend
- [S-23] Calls `scan_images(path, recursive)` to list image file paths.
- [S-24] Calls `read_image_base64(path)` lazily per image; loads limited to 2 concurrent via an internal queue.
- [S-25] Opening an image calls `open_file(path)`; "Show in Explorer" calls `open_in_explorer(path)`.
- [S-26] Polls `refresh` at `refreshInterval` minutes to re-scan the folder; also refreshes when `path`, `recursive`, or `refreshNonce` change.
- [S-27] In demo mode, uses `demoGalleryImages()` fixtures instead of the Tauri scan.

## Interactions
- [S-28] Clicking a thumbnail/cell opens the file (when the image has an underlying path).
- [S-29] Slideshow keyboard (only while the widget owns keyboard focus, claimed on click via the shared widget-keyboard hook, like Music, never a global `document` listener): Left/Right arrow → prev/next; Space → toggle pause.
- [S-37] The slideshow keyboard never disturbs typing: while any text input is focused the gallery claims no key (the Rust hook stands down and `galleryKeyAction` guards on it), so Space/arrows reach the focused notes/todo input natively (regression for themia-releases #53).
- [S-30] Slideshow control bar: chevron-left prev, play/pause toggle, chevron-right next.
