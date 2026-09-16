# OneDrive Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Shows OneDrive files for a single configured Microsoft account, either recent files or the root folder.
- [S-02] Header shows a cloud icon and "OneDrive" (or custom `label`); hidden when `hideHeader` true.
- [S-03] Empty state shows a cloud icon with "Select account to see your files." prompt.
- [S-04] Loading message shown on initial fetch.
- [S-05] Error message shown on fetch failure.
- [S-06] Each item shows a type-specific icon (folder, image, video, spreadsheet, document, generic file) based on mime type or extension.
- [S-07] File rows show name plus a meta line of "size · relative-time"; folder rows show just the time.
- [S-08] Relative time: "just now" / "Nm ago" / "Nh ago" / "Nd ago" / "Mon Day".
- [S-09] Size formatted as B / KB / MB / GB.
- [S-10] When inside a subfolder, a back breadcrumb bar shows a chevron and the current folder name.
- [S-11] Bottom fade overlay on the list.

## Configuration
- [S-12] `accountId`: single Microsoft account (toggle in config panel clears or sets it).
- [S-13] `fileCount`: number of files to load (5–50, step 5, default 15).
- [S-14] `showPath`: `recent` shows recent files; `root` browses from the OneDrive root.
- [S-15] `hideHeader`: toggle the widget title bar.

## Context menu
- [S-16] Widget chrome right-click offers Refresh (stamps a `_refresh` token to trigger reload).

## Data / backend
- [S-17] Recent files fetched via `onedrive_recent_files` when `showPath` is `recent`.
- [S-18] Folder contents fetched via `onedrive_list_folder` with `root` or a specific folder id.
- [S-19] Polls every 5 minutes, refetching the currently viewed folder (or recent files).
- [S-20] Reloads and resets folder stack when account, `_refresh`, `showPath`, or `fileCount` change.
- [S-21] File open invokes `open_url` with the item's `web_url`.

## Interactions
- [S-22] Clicking a folder pushes it onto the folder stack and loads its contents.
- [S-23] Clicking a file opens its OneDrive web URL in the browser.
- [S-24] Clicking the back breadcrumb pops the folder stack; popping past the root returns to the configured default view (recent or root).
