# Themia UI Bug Audit — FINAL (audited + adversarially verified)

Every one of the 62 surfaces (47 widgets + 15 shell) was audited AND adversarially verified by two independent skeptics (correctness + user-visibility lenses).

**159 confirmed defects: 3 high, 82 medium, 74 low.** Verification filtered 285 raw auditor findings down to 159 real ones — the raw set carried a high false-positive rate (57 of 101 gap findings refuted), which is why the verification pass mattered.

Two verification passes on the 36 office/online/others widget scopes (115 confirmed) + a dedicated pass on the 26 system/shell scopes that the spend limit had blocked (44 confirmed).

---

## Confirmed HIGH (3)

- **`src/WidgetWindow.tsx:76`** — Pop-out widget windows never mount <ContextMenu />, so showCtxMenu() renders nothing — right-click is dead in the popped-out Todo window
- **`widgets/online/github-notifications/GitHubNotifications.tsx:142`** — Notification links resolve to GitHub 404 pages because the comment API URL is preferred over the subject URL
- **`widgets/others/gallery/Gallery.tsx:154`** — AsyncImage sets opacity 0 then re-sets the identical src, so onLoad never re-fires and the image stays permanently invisible

## Confirmed MEDIUM (82)

### core/shell
- `src/App.tsx:698` — View-chip drag indexes the filtered bar list but reorders the full view list, moving the wrong view
- `src/App.tsx:911` — deleteView never prunes the per-display visible-views whitelist, leaving that display with an empty view bar and no way to switch views
- `src/components/AccountManager.tsx:124` — Clicking a second provider on the Connections page silently focuses the stale Connect window instead of switching provider
- `src/components/AccountManager.tsx:163` — "Disconnect" destroys a connection on a single click with no confirmation, adjacent to "Rename"
- `src/components/AddWidgetPage.tsx:117` — Container keydown handler hijacks arrow keys destined for the open "Group by" dropdown, then Enter adds a widget instead of selecting the group-by option
- `src/components/ConfigTree.tsx:145` — Deleting the selected view from the tree's right-click menu silently retargets the panel to a different view while nothing is highlighted
- `src/components/ConfigTree.tsx:402` — Clicking a view row's chevron yanks keyboard focus (and the sidebar scroll) to a stale, unrelated tree row
- `src/components/Dropdown.tsx:55` — Dropdown's Escape handler doesn't stopPropagation, so Escape closes the whole config window along with the dropdown
- `src/components/HackerTile.tsx:358` — HackerTile plays its full canvas animation under prefers-reduced-motion, violating [S-23]'s suppression contract
- `src/components/MultiSelectDropdown.tsx:86` — MultiSelectDropdown rows have no keyboard path — a keyboard-only user cannot check or uncheck any item
- `src/input/escapeStack.ts:72` — The Rust WH_KEYBOARD_LL hook consumes Escape before it can reach escapeStack's window keydown listener, so editMode/placing/ctxMenu never see it
- `src/layout.ts:86` — Every user-created view is seeded with the Home view's widgets under IDENTICAL hardcoded ids, so findViewForWidget() mis-routes edits/deletes to the wrong view
- `src/WidgetGrid.tsx:167` — Every user-visible string in WidgetGrid.tsx is hardcoded English, so the widget context menu and edit toolbar stay English in a Chinese UI

### widget: apps
- `widgets/others/apps/Apps.tsx:112` — "Hide from list" replaces the whole widget config, wiping headerMode (and every other key not in the literal)
- `widgets/others/apps/index.tsx:23` — Context-menu "Refresh" does nothing — Apps never reads or tracks the `_refresh` token it writes

### widget: calendar
- `widgets/office/calendar/Calendar.tsx:381` — Changing an account's event color in config does not repaint the widget — the color is baked into each event object at fetch time and the fetch effect never tracks `accountColors`
- `widgets/office/calendar/Calendar.tsx:575` — Day view lays every timed event at `left:0; right:4px` with no lane splitting — overlapping meetings completely hide one another
- `widgets/office/calendar/CalendarConfig.tsx:36` — Config panel's local signals are seeded once and never re-sync with `props.config`, so a context-menu view change desyncs the dropdown and gets silently reverted by the next save

### widget: claude-usage
- `widgets/online/claude-usage/ClaudeUsage.css:13` — Widget body is `overflow: hidden` with no scroll path — enabling both per-model bars clips them off at the default widget size

### widget: clock
- `widgets/others/clock/ClockConfig.tsx:48` — Timezone dropdown search returns zero results for every multi-word city because option labels keep the raw IANA underscore

### widget: contacts
- `widgets/office/contacts/Contacts.css:252` — Details-view responsive CSS hardcodes a 3-column grid, so the default 5-column config blows the table out into multi-row cells below 280px
- `widgets/office/contacts/Contacts.tsx:168` — The "Contacts shown" (5–100) config control is a complete no-op — the fetch hardcodes 500 and the list is never sliced
- `widgets/office/contacts/ContactsConfig.tsx:58` — ContactsConfig.save() writes back all five mount-time signal snapshots, silently reverting a viewMode set from the widget's own chrome context menu

### widget: countdown
- `widgets/others/countdown/timers.ts:105` — Clearing the datetime-local field silently retargets the timer to the current instant and refills the input

### widget: cpu
- `widgets/system/cpu/CpuWidget.tsx:158` — Fan chip's value/N-A branch is chosen once and frozen by the same non-keyed <Show> truthiness memo, so an enabled fan metric never recovers from "Fan N/A" and renders a blank number if the sensor drops

### widget: disk
- `widgets/system/disk/DiskWidget.tsx:217` — Single-mode per-disk I/O list is clipped with no scroll when it overflows the widget body

### widget: email
- `widgets/office/email/Email.tsx:156` — load()'s isLoading re-entrancy guard silently DROPS a folder/count/_refresh change made during an in-flight fetch, and the 30s poll can never recover it
- `widgets/office/email/Email.tsx:210` — Header sort toggle writes only a local signal, so any unrelated config change clobbers the user's sort order back to the config value
- `widgets/office/email/EmailConfig.tsx:40` — EmailConfig snapshots config into local signals at mount only, so the panel shows stale values and its next save() reverts any context-menu change

### widget: folder
- `widgets/office/folder/FolderView.css:2` — .folder-view never declares container-type, so all five @container responsive blocks in FolderView.css are dead
- `widgets/office/folder/FolderView.tsx:1628` — Image-item context menu rewrites the whole widget config from a hardcoded key whitelist, silently dropping clickMode, gridItemSize and gridDensity
- `widgets/office/folder/FolderView.tsx:1598` — refresh() invalidates only attrCache; statCache/folderCountCache/gitRepoCache are only dropped on navigation, so Size, Modified and Items are frozen for the lifetime of the folder view
- `widgets/office/folder/FolderView.tsx:2027` — Grid view drag-over highlight compares dragOverTarget (a full path) against entry.name, so it never matches
- `widgets/office/folder/FolderView.tsx:1988` — FolderView.tsx contains zero t() calls — every user-visible string in the display component is hardcoded English while the config panel, registry and columns are fully localized

### widget: gallery
- `widgets/others/gallery/Gallery.tsx:287` — Image right-click menu rebuilds the config from hand-listed keys and silently drops `headerMode` (and any other non-gallery key)
- `widgets/others/gallery/Gallery.tsx:217` — No request-id/abort guard on the async loaders: an out-of-order scan_images/http_get result overwrites the current folder's images
- `widgets/others/gallery/Gallery.tsx:445` — currentIndex is never clamped when displayImages() shrinks; lowering Max images blanks the slideshow, shows a wrong counter, and throws on click

### widget: github
- `widgets/online/github/GitHub.tsx:245` — Context-menu "Refresh" is a no-op: GitHub.tsx never reads the `_refresh` token that index.tsx writes
- `widgets/online/github/GitHubConfig.tsx:122` — Repository text input has no debounce — every keystroke fires a live GitHub API request

### widget: github-assigned-issues
- `widgets/online/github-assigned-issues/GitHubAssignedIssues.tsx:126` — Loaded subtree is rebuilt on every poll — the scroll container is replaced, resetting scroll position
- `widgets/online/github-assigned-issues/index.tsx:27` — Context-menu "Refresh" is a dead action: the widget never reads the `_refresh` token it bumps

### widget: github-notifications
- `widgets/online/github-notifications/GitHubNotifications.tsx:223` — <For> is handed a frozen array captured by an IIFE, so the list can only update by tearing down and rebuilding the whole subtree
- `widgets/online/github-notifications/GitHubNotifications.tsx:164` — Mark-as-read swallows HTTP failures: no res.ok check, so a 403/401 silently reports success
- `widgets/online/github-notifications/index.tsx:28` — Widget-level "Refresh" context action is a no-op: the component never reads props._refresh

### widget: github-review-requested
- `widgets/online/github-review-requested/GitHubReviewRequested.tsx:99` — When every account's fetch fails, the fetcher resolves with [] instead of throwing, so an auth/network failure renders as "No pending reviews." instead of an error
- `widgets/online/github-review-requested/GitHubReviewRequested.tsx:97` — Aborted in-flight fetch is swallowed by Promise.allSettled and overwrites the previous key's cache with an empty array, defeating createCachedFetch's AbortError guard
- `widgets/online/github-review-requested/index.tsx:27` — Context-menu "Refresh" is dead: index.tsx bumps `_refresh` but the display never declares or tracks it

### widget: habit-tracker
- `widgets/others/habit-tracker/HabitTracker.tsx:48` — Checkbox toggle writes the bucket of the live clock but `done` compares against the polled clock, so the checkbox is unresponsive across every period rollover

### widget: help
- `widgets/others/help/Help.tsx:44` — Basics tab tells users to right-click the desktop and choose "Edit layout" — no such menu item exists, and the desktop menu is suppressed outside edit mode
- `widgets/others/help/Help.tsx:28` — Every user-visible string in the Help body and tab strip is hardcoded English; Help.tsx never imports t() even though index.tsx and HelpConfig.tsx in the same folder do

### widget: music
- `widgets/others/music/Music.tsx:236` — Audio spectrum never renders unless a media session exists, yet the native WASAPI capture thread starts regardless

### widget: net
- `widgets/system/net/NetConfig.tsx:45` — Config panel ignores the legacy `showGraph` key the widget still honours, so the panel shows Graph ON while the widget hides it — and silently re-enables it on the next unrelated edit

### widget: notes
- `widgets/others/notes/Notes.tsx:39` — Debounced persist replaces the whole config object, silently dropping the user's headerMode setting
- `widgets/others/notes/Notes.tsx:29` — onCleanup clears the pending debounce without flushing it, so unmounting mid-typing silently loses the note

### widget: pomodoro
- `widgets/others/pomodoro/Pomodoro.tsx:22` — `remaining` is seeded from `workMin` once at mount and never re-synced, so a duration change desyncs the timer from its own progress bar

### widget: quote
- `widgets/others/quote/Quote.css:39` — `justify-content: center` on a scrollable flex column makes the top of an overflowing quote permanently unreachable

### widget: rss
- `widgets/online/rss/Rss.tsx:42` — `_refresh` is declared as a prop but never tracked, so the context-menu "Refresh" action is a no-op (violates S-15)
- `widgets/online/rss/RssConfig.tsx:87` — Max items input has no debounce and feeds the cache key, so every keystroke blanks the widget and re-downloads every feed
- `widgets/online/rss/RssConfig.tsx:77` — Config panel hardcodes user-visible English on rows adjacent to ones that use t()

### widget: scratchpad
- `widgets/others/scratchpad/Scratchpad.tsx:30` — Pen color / pen size signals snapshot props at mount, so the config panel's controls are dead after first render
- `widgets/others/scratchpad/Scratchpad.tsx:94` — pointerdown has no `e.button` guard, so a right- or middle-button drag paints on the canvas while the widget context menu opens

### widget: shortcut
- `widgets/others/shortcut/Shortcut.css:190` — Details view ignores the "Item size" setting for shell-icon/favicon `<img>`s and for name text — only the lucide fallbacks resize, so icons mismatch within one list
- `widgets/others/shortcut/ShortcutConfig.tsx:379` — Every user-visible string in the Shortcut settings panel is hardcoded English, even though byte-identical translated keys already exist and the widget's own context menu is translated

### widget: stocks
- `widgets/online/stocks/Stocks.tsx:42` — Context-menu "Refresh" is dead: Stocks declares `_refresh` but never reads it
- `widgets/online/stocks/Stocks.tsx:177` — Cosmetic display name is baked into the fetch cache key — every keystroke blanks the widget and fires a new Yahoo/CoinGecko request
- `widgets/online/stocks/SymbolSearch.tsx:184` — Symbol search results are mouse-only — no keyboard path to pick a symbol

### widget: sun-moon
- `widgets/others/sun-moon/sun.ts:58` — nextSunEvent announces tomorrow's sunrise while the sun is still up, for every user with a negative UTC offset
- `widgets/others/sun-moon/SunMoon.css:17` — Body content (186px) exceeds the 200px default widget height and cannot shrink or scroll, clipping the moon-phase row on add

### widget: tabs
- `widgets/others/tabs/Tabs.tsx:152` — Show's children getter eagerly reads the active child's whole config object, so any self-write by that child fully remounts it (focus/scroll/selection lost)

### widget: todo
- `widgets/office/todo/Todo.tsx:114` — load() has no request-id or abort guard, so an in-flight poll for the old list can overwrite tasks fetched for the newly-selected list
- `widgets/office/todo/TodoConfig.tsx:88` — Switching the selected account keeps the previous account's listId, so the widget queries a list that doesn't belong to the new account
- `widgets/office/todo/TodoConfig.tsx:48` — Config panel seeds viewMode/showCompleted once at mount and rewrites them on every save, silently reverting changes made from the widget's context menu

### widget: view-switcher
- `widgets/others/view-switcher/ViewSwitcher.tsx:79` — Deleting a view from the switcher's context menu destroys the view and every widget on it with no confirmation

### widget: volume
- `widgets/system/volume/Volume.tsx:40` — Dragging the slider while muted clears mute in the UI but never on the backend, so audio stays silent and the widget snaps back

### widget: weather-current
- `widgets/online/weather-current/WeatherCurrent.css:115` — `.info-loc` is hidden at exactly the widget's own default width, making the [S-24] "Location name" toggle inert
- `widgets/online/weather-current/WeatherCurrent.tsx:78` — Context-menu "Refresh" is a no-op: the widget never tracks the `_refresh` config token it writes

### widget: weather-forecast
- `widgets/online/weather-forecast/index.tsx:29` — Context-menu "Refresh" is a no-op: the widget never reads the `_refresh` token it writes
- `widgets/online/weather-forecast/WeatherForecast.tsx:148` — Header title, day labels, and weather descriptions are hardcoded English in a file that uses t() for its other display strings
- `widgets/online/weather-forecast/WeatherForecast.tsx:116` — Day labels are computed at fetch time and persisted into the cache, so restored data labels a stale day as "Today"
- `widgets/online/weather-forecast/WeatherForecastConfig.tsx:46` — Config panel's `layout` signal is seeded from props once and never re-reads, so saving any field silently reverts a context-menu layout change

### widget: web-element
- `widgets/online/web-element/webElement.ts:233` — The srcdoc's <base href> is refused by the app CSP that the sandboxed iframe inherits (base-uri 'self'), so relative image/link URLs in the pinned element never resolve
- `widgets/online/web-element/WebElementConfig.tsx:104` — URL text input writes config on every keystroke with no debounce, churning the display widget's cache key and firing one serialized off-screen WebView2 page render per character

## Confirmed LOW (74)

### core/shell
- `src/ColorPicker.tsx:187` — Clamped RGB input keeps displaying the out-of-range number the user typed because the clamp produces the same hex and the value binding never re-runs
- `src/ColorPicker.tsx:116` — Popover position is computed once at open and never recomputed, so scrolling the config panel detaches the popover from its swatch
- `src/ColorPicker.tsx:115` — Popover flip-up and left-shift paths have no clamp to zero, pushing the popover partly off-screen
- `src/components/AccountManager.tsx:136` — Connections page and Connect window are hardcoded English while the fully-translated `account.*` locale namespace is dead
- `src/components/AddWidgetPage.tsx:176` — Dead `|| def.label` fallback: t() returns the raw key on a miss, and `widget.claudeUsage.label` is absent from every locale including en.ts
- `src/components/AddWidgetPage.tsx:19` — translateHeading returns early for provider mode, making the `t('app.pickerGroupGeneral')` branch unreachable — the "General" bucket heading is never translated
- `src/components/AddWidgetPage.tsx:176` — Widget picker renders the raw key "widget.claudeUsage.label" — the key is absent from every locale and the `|| def.label` fallback is unreachable
- `src/components/ConfigTree.tsx:161` — Deleting the currently-open widget from the tree's right-click menu strands the panel on a dead "Widget not found." page
- `src/components/ConfigTree.tsx:234` — restoreFocusOnRebuild is set on a path that never rebuilds the rows, so the flag sticks and later steals focus out of the content pane
- `src/components/SettingsModal.css:447` — SettingsModal.css hardcodes white-on-transparent literals instead of theme tokens — the changelog 'Show more' button loses its border and hover entirely in light theme
- `src/components/ToggleSwitch.tsx:20` — ToggleSwitch hardcodes English 'On'/'Off' as its state-label defaults although toggle.on/toggle.off exist in every locale
- `src/components/ViewSettingsModal.tsx:40` — View name input keeps a rejected value on screen: a whitespace-only (or untrimmed) edit is discarded by the store but never restored in the DOM
- `src/input/gridDragManager.ts:324` — Window-level click commits a widget placement even when the click was on the edit bar or view bar, dropping the widget somewhere the user never chose
- `src/input/gridDragManager.ts:273` — onMouseUp has no `e.button` guard, so a right-click while dragging commits the drop mid-gesture
- `src/WidgetGrid.tsx:168` — The widget-chrome context menu hardcodes English labels although the matching ctxMenu.* locale keys exist and the same actions are translated in ConfigTree
- `src/WidgetGrid.tsx:37` — widgetGap (max 48px) is subtracted from a widget whose minimum size is 32px, rendering it 0x0 and dropping it out of the hit mask
- `src/WidgetWindow.tsx:85` — Widget pop-out window renders hardcoded English empty/error states with no t() key

### widget: apps
- `widgets/others/apps/Apps.tsx:152` — Search placeholder and item context-menu labels are hardcoded English while t() is used in the same file

### widget: bluetooth
- `widgets/system/bluetooth/BluetoothWidget.css:60` — Long device names never ellipsize — `.bt-widget__item-name` is an inline span, so `overflow`/`text-overflow` are ignored

### widget: calculator
- `widgets/others/calculator/Calculator.tsx:45` — Widget header title hardcodes English 'Calculator' instead of the existing t('widget.calculator.label') key, which is translated in all 9 locales

### widget: calendar
- `widgets/office/calendar/Calendar.css:73` — `.reauth-btn:hover` hardcodes the accent color `#60cdff` as raw rgba instead of using the design token
- `widgets/office/calendar/Calendar.tsx:199` — `fetchData` has no request-sequence or abort guard — concurrent fetches apply out of order and paint the wrong month's events
- `widgets/office/calendar/CalendarConfig.tsx:98` — Five user-visible strings in the config panel are hardcoded English; the `calendar.weekStart` / `calendar.eventColors` locale keys were never added to any of the 9 locale files

### widget: claude-usage
- `widgets/online/claude-usage/ClaudeUsage.tsx:78` — `view()` falls through to the no-account empty state when a fetch is in flight on a shared cache entry, showing "Connect a Claude account" while an account is connected
- `widgets/online/claude-usage/ClaudeUsageConfig.tsx:77` — Config panel's "Title" field writes `config.label`, which the render site unconditionally overwrites — the field does nothing
- `widgets/online/claude-usage/index.tsx:29` — Context-menu "Refresh" is a no-op: `_refresh` is bumped but never read, and the mount path won't force a re-fetch (violates SPEC [S-13])

### widget: clipboard
- `widgets/others/clipboard/Clipboard.tsx:26` — Lowering "History size" never trims the existing list — max() is only consulted when a new entry arrives
- `widgets/others/clipboard/Clipboard.tsx:46` — The "copied" check is invisible for every entry except the first — recopy reorders the row out from under the cursor while the icon is opacity:0 unless hovered

### widget: clock
- `widgets/others/clock/Clock.css:122` — Stacked style hardcodes align-items:center, so the `align` config does not move the seconds and AM/PM lines

### widget: color-swatch
- `widgets/others/color-swatch/index.tsx:11` — Widget picker shows a translated label directly above a hardcoded English description; no description key exists in any locale

### widget: countdown
- `widgets/others/countdown/Countdown.tsx:56` — Format context menu is built from an eager snapshot, so with `keepOpen: true` the active checkmark never moves after a selection

### widget: disk
- `widgets/system/disk/DiskWidget.tsx:234` — `<For each={selected()}>` has no stable item identity — every poll replaces the drive rows, swallowing clicks and killing the bar transition

### widget: email
- `widgets/office/email/Email.css:243` — Row-view column headers are offset ~19px from the columns they label because .rh-inner has no spacer for the per-row envelope icon
- `widgets/office/email/Email.tsx:415` — Relative timestamps are computed once per fetch and never tick, so list/compact rows keep showing "just now" indefinitely

### widget: folder
- `widgets/office/folder/FolderView.css:163` — Git 'modified' status color hardcoded as #e8a317 in five places instead of the theme-aware var(--warning) token

### widget: gallery
- `widgets/others/gallery/Gallery.tsx:160` — Empty catch blocks swallow image-read and open failures with no error state and no log

### widget: github
- `widgets/online/github/GitHub.css:41` — Release badge hardcodes the default accent (#60cdff) as its background instead of --primary-subtle-bg
- `widgets/online/github/GitHub.tsx:252` — view() falls through to 'empty', so a second widget sharing a repo shows the "Select repository" prompt for the whole first fetch
- `widgets/online/github/GitHub.tsx:297` — Pipeline badge falls through to raw untranslated GitHub API strings ('queued', 'cancelled', 'skipped')

### widget: github-assigned-issues
- `widgets/online/github-assigned-issues/GitHubAssignedIssues.css:35` — Count badge hardcodes the default cyan accent instead of `--primary-subtle-bg`
- `widgets/online/github-assigned-issues/GitHubAssignedIssues.tsx:143` — Empty state renders a stray divider line and pushes "No assigned issues." off-center
- `widgets/online/github-assigned-issues/GitHubAssignedIssues.tsx:132` — Display component's user-visible strings are hardcoded English while the widget's name is translated

### widget: github-notifications
- `widgets/online/github-notifications/GitHubNotifications.css:36` — Unread count badge hardcodes the accent color, so it stops matching when the user changes the primary color

### widget: github-review-requested
- `widgets/online/github-review-requested/GitHubReviewRequested.css:35` — Count badge hardcodes the accent color rgba(96,205,255,0.15) instead of the --primary-subtle-bg token, so it ignores the user's chosen accent
- `widgets/online/github-review-requested/GitHubReviewRequested.tsx:121` — Every user-visible string in the display component is hardcoded English while the widget's own label is translated, producing a mixed-language UI

### widget: gpu
- `widgets/system/gpu/GpuConfig.tsx:66` — Unchecking the last selected GPU makes a different GPU tick itself in the picker

### widget: habit-tracker
- `widgets/others/habit-tracker/HabitTracker.tsx:85` — Checkbox tooltip hardcodes "today" regardless of the configured reset cadence

### widget: help
- `widgets/others/help/Help.tsx:97` — Views tab promises Rename and Delete in the view-name right-click menu; that menu only offers Duplicate + View settings, and only in edit mode
- `widgets/others/help/Help.tsx:122` — Customize tab tells users the language choice is "English or Deutsch"; the app ships 9 languages
- `widgets/others/help/Help.tsx:142` — Header title hardcodes the English 'Help & Getting Started' instead of t('widget.help.label'), which is translated in all 9 locales
- `widgets/others/help/index.tsx:11` — Picker description advertises "shortcuts", which SPEC [S-05] explicitly forbids this widget from teaching; picker search matches on description

### widget: music
- `widgets/others/music/Music.tsx:208` — Widget header hardcodes English 'Music' instead of the translated widget.music.label that exists in all 9 locales
- `widgets/others/music/Music.tsx:214` — Empty-state string 'No media playing' is hardcoded English with no locale key, unlike sibling widgets' translated empty states

### widget: net
- `widgets/system/net/NetConfig.tsx:40` — Config panel rows render hardcoded English labels while the same file's section headings and the surrounding ConfigTabs rows are translated, producing a half-translated panel

### widget: notes
- `widgets/others/notes/Notes.tsx:49` — Widget header title hardcodes English "Notes" although the translated key exists and is already used in index.tsx
- `widgets/others/notes/Notes.tsx:23` — Clear notes is undone by the display's in-flight debounce when clicked shortly after typing

### widget: onedrive
- `widgets/office/onedrive/OneDrive.tsx:146` — Switch has no branch for loaded-but-empty, so the widget body renders completely blank
- `widgets/office/onedrive/OneDrive.tsx:140` — Folder navigation renders the previous folder's items under the new folder's breadcrumb while the fetch is in flight
- `widgets/office/onedrive/OneDrive.tsx:33` — Relative timestamps and dates are hardcoded English/en-US in a widget that otherwise uses t()
- `widgets/office/onedrive/OneDriveConfig.tsx:80` — File count input accepts any typed value >= 1, ignoring the documented 5-50 range
- `widgets/office/onedrive/OneDriveConfig.tsx:78` — Un-debounced File count input fires a Graph API fetch and a config write on every keystroke

### widget: quote
- `widgets/others/quote/Quote.tsx:32` — Header title hardcodes the English "Quote of the Day" although the translated registry key t('widget.quote.label') exists

### widget: rss
- `widgets/online/rss/Rss.tsx:247` — view() falls back to 'empty' when feeds ARE selected, showing "Select feed to see your articles" on a configured widget

### widget: scratchpad
- `widgets/others/scratchpad/Scratchpad.tsx:124` — Clear-all persists a transparent PNG data URL, so the "Draw here" placeholder never comes back on an empty pad

### widget: screentime
- `widgets/system/screentime/ScreentimeWidget.tsx:151` — User-visible strings are hardcoded English in a component that localizes two lines away — no `screentime.*` namespace exists in en.ts
- `widgets/system/screentime/ScreentimeWidget.tsx:142` — A day with zero recorded usage renders a large '<1m' total directly above 'No activity yet' — the two metrics contradict each other

### widget: stocks
- `widgets/online/stocks/Stocks.tsx:216` — Header falls back to hardcoded English "Stocks" instead of t('widget.stocks.label')

### widget: themia
- `widgets/others/themia/index.tsx:11` — Picker description advertises 'handy quick links' that the widget does not implement

### widget: weather-current
- `widgets/online/weather-current/WeatherCurrent.tsx:121` — `<Switch>` has no fallback, so a second widget on the same location renders a completely blank body during the shared fetch
- `widgets/online/weather-current/WeatherCurrentConfig.tsx:111` — Enter-to-search does not cancel the pending debounce timer, firing a second geocoding request that blanks the visible results

### widget: weather-forecast
- `widgets/online/weather-forecast/WeatherForecast.css:213` — Grid layout shows a visible scrollbar in the widget body, violating the widget-body convention that Rows and Columns follow
- `widgets/online/weather-forecast/WeatherForecastConfig.tsx:74` — City search has no abort/request-id guard: results from a cancelled query repopulate an emptied search box

### widget: web-element
- `widgets/online/web-element/WebElementConfig.css:13` — The disabled 'Pick element' button lights up on hover as if it were clickable, because the shared .cfg .add-btn:hover rule has no :not(:disabled) guard

### widget: web-search
- `widgets/others/web-search/WebSearch.tsx:34` — Widget header hardcodes the English 'Web Search' although the localized key `widget.webSearch.label` exists and is used by the widget's own registration

---

## Refuted in the gap pass (57 false positives, dropped)

These auditor claims did NOT survive adversarial verification (wrong, already-guarded, unreachable, or SPEC-blessed):

- `widgets/system/cpu/CpuWidget.tsx:149` — Temperature chip's value/N-A branch is chosen once and frozen; when the sensor later disappears the frozen bra
- `widgets/system/cpu/CpuWidget.tsx:198` — <For> over the primitive cpu_cores array combined with a gradient id frozen from a creation-time i() read prod
- `widgets/others/tabs/Tabs.tsx:88` — Tabs' inline ErrorBoundary fallback discards the error, so a child crashing inside a tabs container produces z
- `widgets/others/tabs/Tabs.tsx:95` — Retry button renders the raw '↻' text glyph instead of the lucide RotateCw icon used by every other retry butt
- `widgets/others/tabs/Tabs.tsx:74` — Hardcoded English strings in a file where every other user-visible string goes through t(), including a title 
- `widgets/others/sun-moon/SunMoonConfig.tsx:43` — Latitude/longitude inputs accept out-of-range values; the min/max attributes never clamp the committed value
- `widgets/system/bluetooth/src/lib.rs:175` — The "Signal (RSSI)" metric toggle is dead — the backend never populates `rssi`, so the row can never render
- `widgets/system/bluetooth/BluetoothWidget.css:78` — Connected-device dot hardcodes #22c55e instead of the theme-aware `var(--success)` token
- `widgets/system/bluetooth/BluetoothWidget.tsx:91` — Unchecking every metric renders a column of blank hoverable strips instead of any empty/fallback state
- `widgets/system/battery/BatteryWidget.tsx:134` — Unchecking the "Status" metric does not hide the status line — statusText() never consults shows('status')
- `widgets/system/battery/BatteryWidget.tsx:116` — Body is gated on stats() with no fallback — a failed get_battery_stats leaves a permanently blank widget
- `widgets/system/battery/BatteryWidget.tsx:146` — Sparkline omits vector-effect="non-scaling-stroke" while preserveAspectRatio="none" anisotropically scales the
- `widgets/system/battery/BatteryWidget.css:103` — Charging-bolt responsive sizing is dead CSS — font-size cannot resize a lucide SVG with fixed width/height att
- `widgets/system/battery/BatteryWidget.css:57` — font-weight: 200 on the widget's primary readout is off the documented 400/500/600/700 scale and unique in the
- `widgets/system/disk/DiskWidget.tsx:232` — Multi-drive layout ignores the `space` metric toggle, so unchecking "Space usage" does nothing with 2+ drives 
- `widgets/system/disk/DiskConfig.tsx:57` — Config's "Source" section renders as an empty labeled block until the async disk list resolves (and permanentl
- `widgets/system/volume/Volume.tsx:41` — onInput fires an unthrottled synchronous COM command per pointer move, blocking the WebView2 main event-loop t
- `widgets/system/volume/Volume.tsx:25` — refresh() only checks the `dragging` guard before its await, so an in-flight poll applies stale device state o
- `widgets/system/screentime/ScreentimeWidget.css:180` — The [S-01] '●' back-to-today button is never rendered at the widget's default size — the 220px container-query
- `widgets/system/screentime/ScreentimeConfig.tsx:47` — The 'Apps shown' stepper is not gated behind the 'apps' metric toggle, unlike the sibling CpuConfig it was cop
- `widgets/others/view-switcher/ViewSwitcher.css:66` — Active view tile has an invisible fill in light theme, and hover paints inactive tiles darker than the active 
- `widgets/others/view-switcher/ViewSwitcher.tsx:63` — stopPropagation on the tile context menu makes the widget chrome menu unreachable, leaving the registered 'Ico
- `widgets/others/view-switcher/ViewSwitcher.css:19` — Widget body is painted with --header-bg, so the headerless switcher vanishes when Header opacity is set to 0 a
- `src/ColorPicker.tsx:223` — ColorPicker popover never handles Escape, so Escape closes the entire config window with the picker still open
- `src/ColorPicker.tsx:147` — Gradient drag and alpha slider commit on every mousemove frame, firing a full read-modify-write persist, a cro
- `src/ColorPicker.css:147` — Input labels use a 9px font size, which is off the project's 10/11/12/13px type scale
- `src/components/MultiSelectDropdown.tsx:87` — MultiSelectDropdown's Escape handler doesn't stopPropagation, so Escape closes the whole config window along w
- `src/components/NumberStepper.tsx:126` — NumberStepper's +/- buttons ignore and never clear the pending text draft, desyncing the displayed number from
- `src/components/SettingsModal.tsx:1114` — License key input's Enter handler lacks the loading guard its own Activate button has — holding/double-tapping
- `src/components/SettingsModal.tsx:1189` — Failed update download is a dead end: both action buttons are hidden when updateStatus === 'error', leaving no
- `src/components/SettingsModal.tsx:107` — Animation duration stepper previews on every repeat tick — press-and-hold fires ~20 full-app animation replays
- `src/components/DisplaySettingsModal.tsx:162` — View-bar sub-settings stay fully enabled when their parent 'Show view bar' toggle is off
- `src/App.tsx:247` — activeViewKeyed returns a fresh object on every view-list write, so a keyed <Show> remounts the entire widget 
- `src/App.tsx:815` — Ctrl+1..9 switches to viewStore.list[n-1] while the bar shows the per-display whitelist, stranding the user on
- `src/App.tsx:927` — dragOverIdx is only ever set to null, so the .drag-over drop-target highlight is unreachable dead CSS
- `src/components/AddWidgetPage.tsx:59` — Picker search matches only the untranslated English label/category while the cards render translated labels
- `src/components/WidgetRowPreview.tsx:69` — ErrorBoundary in the preview pane latches permanently — one throwing widget breaks the preview for every widge
- `src/components/AccountToggleList.tsx:16` — AccountToggleList's required `providerBadges` prop is never read — every call site's badge labels are silently
- `src/ConnectWindow.tsx:54` — Pressing Enter in a Connect-window credential field does nothing — the form can only be submitted by mouse
- `src/ContextMenu.tsx:52` — Submenu steals keyboard focus on hover and never returns it when it auto-closes — arrow keys and type-ahead go
- `src/ctxMenuStore.ts:33` — showCtxMenu clamps x/y with Math.min but no lower bound, and .ctx-menu has no max-height/overflow — a tall men
- `widgets/system/net/NetWidget.tsx:126` — Header renders a hardcoded English "Network" that contradicts the widget's own registered label "Network Monit
- `widgets/system/net/NetWidget.tsx:167` — Per-interface list's Show guard tests total interface count while the For filters to *active* interfaces, so t
- `src/input/widgetKeyboard.ts:88` — registerWidgetKeyboard's dispose() clears only the local handler and never calls the existing `deactivate_widg
- `src/input/appBlur.ts:26` — onAppBlur's cleanup races the un-awaited listen() promise, permanently leaking a themia-blur listener that fir
- `src/input/selectionStore.ts:151` — Shift-click is a dead click when the anchor has been filtered out of orderedIds — the guard returns without se
- `src/input/gridDragManager.ts:266` — `widget-cross-drag-end` is broadcast on every single mousemove of an in-window drag, contradicting its own com
- `src/WidgetGrid.tsx:251` — widgetGap is applied in pre-scale coordinates while the rect is computed in post-scale ones, so raising a moni
- `src/components/ConfigTabs.tsx:37` — Every widget's config page renders the whole Header section in hardcoded English; the translations for it exis
- `src/SafeMode.tsx:34` — SafeMode's primary button wipes all persisted state (localStorage.clear()) on a single click with no confirmat
- `src/components/NewsletterRow.tsx:45` — NewsletterRow's Enter key bypasses the validation and in-flight guards the Subscribe button enforces
- `src/components/Toast.css:24` — Toast's `max-width: 70vw` resolves against the viewport but is then multiplied by the app-layer's scale transf
- `src/components/WidgetHeader.tsx:43` — WidgetHeader evaluates each slot prop twice, instantiating a permanently-orphaned second copy of the caller's 
- `src/components/WidgetHeader.tsx:48` — `Show when={props.secondary}` is always truthy — the .wh-secondary span is inserted even when the caller's slo
- `src/viewState.ts:68` — viewStore.activeId is hardcoded to 'default' at module init and never validated against the loaded list — dele
- `src/views.ts:978` — duplicateView reads the source layout from raw localStorage, so duplicating a view whose layout was never pers
- `src/LayoutWindow.css:59` — The layout editor and app-picker windows override the mandated always-visible config scrollbar with overflow-y
