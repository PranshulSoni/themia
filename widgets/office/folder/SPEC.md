# Folder Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Renders configured local folder path as a list, details table, grid, or icon-grid of files and subfolders.
- [S-02] Shows an empty-state message prompting the user to select a folder when no path is configured (suppressed in demo/preview mode, which renders demo file entries instead).
- [S-03] Shows a loading message while the initial directory read is in flight.
- [S-04] Shows an error message when the directory read fails.
- [S-06] Header shows a folder icon plus the folder name (or custom label) and can be hidden via `hideHeader`.
- [S-07] Header double-click opens the current folder in Windows Explorer.
- [S-08] When the folder is a git repo, header shows a branch badge with staged/modified/untracked/deleted/conflicted counts (not in grid or icon-grid views).
- [S-09] Files can show a cloud-sync badge (OneDrive / cloud pin status) in details, list, grid, and icon-grid views; in grid/icon-grid views the badge overlays the bottom-right corner of the file icon.
- [S-10] Thumbnails are fetched and displayed for image files in grid and icon-grid views.
- [S-11] Filenames hide their extension by default unless `showFileExtension` is enabled.
- [S-12] Cut-pending items are shown dimmed until the cut is completed or cleared.
- [S-13] Drop-target folders highlight during native drag-over.

## Configuration
- [S-14] `path`: local directory to display; picked via a native folder-browse dialog in the config panel.
- [S-15] `viewMode`: `list` | `details` | `grid` | `icons` (icon grid).
- [S-16] `itemSize`: `xs` | `small` | `medium` | `large` controls row density for list/details.
- [S-17] `gridItemSize`: `small` | `medium` | `large` controls tile size in grid and icon-grid views.
- [S-18] `gridStretchHeight`: expand grid/icon-grid tiles to fill available widget height.
- [S-19] `detailsColumns`: toggles visibility of Size, Modified, Type, Cloud, Git, Item-count columns in details view.
- [S-20] `navigationMode`: `explorer` opens items in Windows Explorer; `navigate` opens folders inside the widget.
- [S-21] `filter`: comma-separated filename filter keywords.
- [S-22] `showHidden`: toggles display of dot-files and hidden entries.
- [S-23] `showFileExtension`: toggles inclusion of the extension suffix in displayed names.
- [S-24] `hideHeader`: toggles the widget title bar.
- [S-25] *(removed)* Per-widget item spacing is no longer configurable; list/grid gap uses the global `--item-gap` token.
- [S-58] `clickMode`: `single` opens items on plain left-click; `double` (default) opens on double-click. Ctrl/Shift-click always selects without opening regardless of mode.
- [S-67] `icons` (icon grid) view mode renders the grid view without filenames: the static label is hidden and tiles shrink to the icon (`iconSize + padding` square cells) so icons pack denser than the labelled grid. It shares the grid's stretch-to-fill columns (`minmax(col,1fr)`), `gridItemSize`, `gridStretchHeight`, keyboard grid-nav, icon sizing, thumbnails, and cloud badge; only the label differs. The full name is shown as a hover `title`. Renaming still shows its inline input in place of the (absent) label. Effective column width / row height math lives beside the grid memos; `gridLayout.ts` stays the single template source. (Replaced the removed grid-density option — grid is always the comfortable stretch-to-fill layout.)

## Context menu
- [S-26] Widget chrome right-click offers Open-in-Explorer, view-mode switcher, item-size selector (or grid-size selector in grid/icon-grid views), toggle hidden files, toggle file extensions, and details-view column toggles.
- [S-27] Item right-click offers Open, Rename (single selection only), Copy, Cut, Paste, Copy-path, Properties, Delete, plus injected Windows Shell context-menu entries for the item.
- [S-28] Background right-click offers Paste, Select-All, Copy-path, a New submenu (folder + ShellNew templates), Windows Shell background entries, and Refresh.
- [S-29] Image-item right-click adds inline widget view/visibility toggles.
- [S-30] Copy / Cut labels show the selection count when more than one item is selected.
- [S-31] Paste is disabled when there is no current path.

## Data / backend
- [S-32] Directory contents are read via `@tauri-apps/plugin-fs` `readDir`.
- [S-33] Filesystem watcher (`watch`) invalidates and reloads the view when files change; a 5s poll fallback runs if the watcher fails to start.
- [S-34] `folder-changed` Tauri events from other folder widgets trigger a reload when they match the current path.
- [S-63] The fs watcher is unwatched on `beforeunload` as well as on Solid cleanup, exactly once (idempotent handle, `watchLifecycle.ts`): a page reload skips `onCleanup`, and the orphaned Rust watcher would otherwise stream events into a dead Channel forever ("[TAURI] Couldn't find callback id" spam on every fs event; see src-tauri [LOG-03] for the log-side filter). A `watch()` that resolves after release unwatches immediately instead of re-arming.
- [S-35] File attributes (size, modified, cloud sync, git status, item count) are lazily fetched via Tauri commands and cached.
- [S-36] Git repo status is fetched for the current folder to populate the header badge.
- [S-37] ShellNew templates and Windows Shell menu entries are fetched once and cached via `get_shell_new_entries` / `get_shell_menu_entries`.
- [S-38] Rename invokes `fs_rename`; Delete invokes `fs_delete`; Paste invokes `fs_paste`; New-folder invokes `fs_create_folder`; New-file invokes `fs_write_file`.
- [S-39] Copy/Cut store file paths on the Windows system clipboard via `clipboard_set_files`; Paste reads them via `clipboard_get_files`.
- [S-40] Open invokes `open_file`; `.lnk` files are resolved via `resolve_shortcut` before opening.
- [S-41] Cloud pin changes are written via `set_cloud_pin`.
- [S-42] Windows Shell verbs are executed via `execute_shell_command`; file properties open via `show_file_properties`.
- [S-64] Shell icon extraction (`get_file_icon` / `get_file_icons_batch`) serializes its `SHGetImageList` acquisition behind a process-global lock: concurrent first calls race shell32's lazy per-process image-list init and transiently fail with `E_OUTOFMEMORY` (0x8007000E). Only the acquisition is serialized (per-path extraction stays parallel), and concurrent batches must each return a complete result set (test `s64_concurrent_icon_batches_all_succeed`). The in-loop retry remains as a fallback for cold-COM transients and is expected to stay silent.

## Interactions
- [S-43] Single click selects; Ctrl-click toggles; Shift-click range-selects; clicking a selected item in single-click mode deselects it.
- [S-44] Double-click opens the file, or navigates into the folder when `navigationMode` is `navigate`.
- [S-45] Ctrl+C / Ctrl+X / Ctrl+V / Ctrl+A perform copy, cut, paste, select-all on the widget's selection.
- [S-46] Delete key deletes the current selection; F2 renames a single-selected item; Enter opens the selected item; Escape clears selection.
- [S-61] Clicking outside this widget instance (another widget, the widget's own chrome, or the desktop) clears the file selection via a global mousedown handler; clicking outside any folder widget also drops the paste-target highlight. Clicking outside the app entirely (another app or the desktop) clears the file selection too, driven by the Rust `WH_MOUSE_LL` mouse hook's `themia-blur` broadcast (fires on every physical click outside all themia windows, focus-independent; the foreground hook also broadcasts it for keyboard focus changes, and a DOM `window blur` fallback exists for dev/non-Tauri). This leaves the paste target intact so the copy-here → switch app → copy a file → return → paste workflow still works. Mousedowns inside a context menu are ignored so a menu action can read the selection first.
- [S-47] Backspace navigates back through history when `navigationMode` is `navigate`.
- [S-48] Arrow keys, Home, End, PageUp, PageDown move selection, with grid-aware column jumps; Shift extends the selection range.
- [S-49] Mouse buttons 4/5 (back/forward) navigate history when `navigationMode` is `navigate`.
- [S-50] Header search button toggles an inline filename search input (Esc clears and closes).
- [S-51] Header sort button toggles A-Z / Z-A (hidden in details view, where column headers drive sorting).
- [S-52] Header Back/Forward/Home buttons appear in `navigate` mode.
- [S-53] Files can be dragged out of the widget to Explorer or other drop targets via native OLE drag.
- [S-54] Files dragged in from Explorer or other folder widgets drop onto the widget (or a hovered subfolder); plain drop moves, Ctrl-drop copies; virtual-file drops always copy via `save_virtual_files`.
- [S-55] New files/folders created via the background menu auto-start a rename on the new entry.
- [S-56] Keyboard routing uses a Rust low-level hook (widget windows are `WS_EX_NOACTIVATE`); clicking the widget activates its keyboard router so shortcuts reach it.
- [S-57] Scroll position is saved per path and restored when navigating back/forward.
- [S-59] Root `.folder-view` carries `click-mode-single` / `click-mode-double` class; item cursor is `pointer` in single-click mode and `default` in double-click mode.
- [S-60] After navigating into a subfolder (navigate mode, back/forward, or history nav), icons for the new folder's entries are fetched and rendered: the icon-load effect must not be dropped by an in-flight batch from the previous path.
- [S-65] The press→click/rubber-band/OLE-drag gesture is driven by a single pure state machine (`gesture.ts`): every exit path (mouse release, drag hand-off, transient `get_cursor_state` failure, Solid cleanup) resets the in-flight candidate to a known-clean baseline, and each new press (`beginPress`) fully wipes prior state — including the stale drag origin — BEFORE arming. A plain left-click can therefore never be permanently misclassified as a drag from leftover state (which previously swallowed the `mouseup`, so single-click open/navigate silently died after a while until reload; right-click was unaffected). A monotonic generation token guarantees a fast re-press cannot leave two poll loops racing the same state. Regression: `gesture.test.ts` (#58).
- [S-62] In details view the column header row stays pinned (sticky) to the top of the scroll container while rows scroll under it; its background is fully transparent so it reads at the body's own lightness (a solid --card fill would composite darker and look like the header bar; a backdrop-filter blur is impossible because the widget body already owns one and backdrop-filters don't nest).
