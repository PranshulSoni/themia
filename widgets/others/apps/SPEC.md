# Apps: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Lists installed Start Menu applications on mount, grouped by category.
- [S-02] Shows "Loading apps..." while the initial fetch is in flight.
- [S-03] Shows "No apps found." when the filtered list is empty.
- [S-04] Grid view renders large tiled icons within category sections; list view renders small rows with icon + name; compact view renders flat name-only rows with no grouping.
- [S-05] Category headers are hidden for a lone "Other" bucket.
- [S-06] Loads each app's icon asynchronously via `get_file_icon` (size 48), with max 6 parallel requests in a background queue.
- [S-07] Until an icon loads, the tile shows hue-colored initials (first letters of up to two name words), hue derived deterministically from the name.
- [S-08] Search bar filters apps by case-insensitive substring of name or category.
- [S-09] Search bar shows a clear (✕) button that resets the query when non-empty.

## Configuration
- [S-10] `viewMode`: grid / list / compact layout selector.
- [S-11] `hideHeader` toggle hides the search bar/title area.
- [S-12] *(removed)* Item spacing is no longer configurable; gap is fixed at 10px (grid) / 4px (list).

## Context menu
- [S-13] Widget context action `Refresh` bumps a `_refresh` config key with the current timestamp.
- [S-14] Widget context actions include view-mode switcher (Grid / List / Compact).
- [S-15] Right-clicking an app shows item menu: Launch, Open location, Copy path.

## Data / backend
- [S-16] Calls Tauri `list_start_menu_apps` to populate the list.
- [S-17] Calls `get_file_icon` per app path to resolve icons.
- [S-18] Launch calls `open_file` with the app path (shell "Open" verb, launches the .lnk); Open location strips the filename and calls `open_in_explorer` on the parent directory.
- [S-19] Copy path writes `app.path` to the system clipboard.

## Interactions
- [S-20] Single-click on an app launches it.
- [S-21] Right-click on an app opens the item context menu.

## Persistent filtering
- [S-22] `hiddenCategories` config excludes apps whose category is listed; applied before search.
- [S-23] `hiddenApps` config excludes apps whose path is listed; applied before search.
- [S-24] Item context menu includes "Hide from list", which appends the app's path to `hiddenApps`.
- [S-25] Config has a "Filter" section listing every unique category as a toggle plus the current hidden-apps list with remove buttons.
