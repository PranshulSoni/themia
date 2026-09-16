# System-Tray Widgets: Spec

Widgets that live in the Windows notification area instead of on the desktop.
Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never
renumber or reuse existing tags. When behavior is removed, delete the bullet but
don't reuse its tag.

Frontend: `src/tray.ts` (collection + sync), `src/trayLayout.ts` (flyout sizing),
`src/trayIcon.ts` (rasterization), `src/TrayFlyout.tsx` (+ `.css`, the panel),
the tray group in `src/components/ConfigTree.tsx`, the tray section in
`src/components/ConfigTabs.tsx`.
Backend: `src-tauri/src/tray.rs`.

## The collection

- [S-01] A tray widget is a normal widget instance in the `tray` layout
  collection (`localStorage` key `widget-layout-tray`, `TRAY_COLLECTION_ID` in
  `layout.ts`) — the same store type a view uses, so widget config, labels, the
  config tree and the config panels all work on it unchanged. It is NOT a view:
  it has no monitor binding, it never becomes `viewStore.activeId`, and nothing
  renders it on the desktop. Its widgets' `x/y/w/h` stay at the placeholder
  `addWidget` assigns and are never read. Every registry type may live there
  except containers (stack / tabs / group), which exist to arrange sibling tiles
  a one-widget flyout does not have. Tray widgets count toward the free-tier
  ceiling like any other (`totalWidgetCount`).
- [S-07] A tray widget's flyout opens at a per-instance size in logical px,
  stored on its config as `trayWidth` / `trayHeight` — the same place
  `headerMode` and `transparentBg` live, being app-level per-instance settings
  rather than widget options. It is edited in the same place too: on the
  widget's OWN config page, in a section `ConfigTabs` renders only when the
  widget lives in the tray. Absent or non-numeric (NaN / Infinity included, all
  of which mean "corrupt", never "zero") falls back to the widget type's
  registry `defaultSize`, so a new tray widget opens at the size its author
  designed for. Always clamped to [`FLYOUT_MIN`, `FLYOUT_MAX`].

- [S-14] A tray widget's ICON and NAME are per-instance, on the same config page
  as its panel size. Both are overrides with a documented fallback chain, not
  copies: the icon falls back to the widget type's registry icon (an
  unrecognised stored key resolves to it too, so retiring a catalogue entry
  degrades instead of drawing nothing), and the name falls back to the widget's
  header title, then to its type's label. The name is its OWN field rather than
  a reuse of the header title, because a tray widget can be configured with no
  header at all — and then there would be nothing to name it with. Both are
  stored raw: the config surface shows what the user set, with the fallback as
  the placeholder beside it, so an inherited value never masquerades as an
  explicit one and clearing back to the default stays possible. The config tree
  shows the resolved icon and name, so the row and the notification area agree.
- [S-17] The collection starts EMPTY. `defaultLayout` seeds a fresh *view* with
  clock + help + shortcut, and the tray is not a view — a seeded tray would put
  three icons in the notification area on every fresh profile, which is also what
  made an e2e run (a `localStorage.clear()` per spec, each app force-killed
  before it could hand its icons back) pile up dozens of them.

## Sync

- [S-02] The frontend owns the list and pushes the WHOLE desired icon set
  through `tray_sync`; Rust diffs it against the live tray. Unchanged entries
  are left untouched (icon bitmaps are compared by fingerprint), widgets that
  left the collection have their icon dropped, and new ones get one. Because the
  push is a diff, EVERY window runs the sync: they read the same localStorage so
  they compute the same set, and a redundant push costs a handful of
  comparisons. Electing one owner window would instead stop the tray updating as
  soon as that window closed.
- [S-18] Removing an icon means removing BOTH handles: the one `tray.rs` owns
  and the clone `TrayIconBuilder::build` parks in Tauri's resource table
  (`remove_tray_by_id`). `tray_icon` deletes the shell notification only when the
  last clone dies, so dropping just ours left the icon on screen — a removed tray
  widget kept its icon, and a language switch (which rebuilds every menu) drew a
  second icon per widget beside the first. The same teardown runs on
  `RunEvent::Exit`, because the live set is a `static` that process exit never
  drops; without it a clean quit leaves ghost icons the shell only reaps when the
  user next hovers the notification area.
- [S-03] Icons are rasterized from the widget's own registry icon, never from a
  second hand-drawn icon set that could drift from the picker. They are drawn at
  exactly `SM_CXSMICON` physical px so the shell never resamples them, in white
  on a dark taskbar and black on a light one (`SystemUsesLightTheme`, the value
  the taskbar follows — not `AppsUseLightTheme`). Stroke width is raised to at
  least 2 for the size. An icon that cannot be rasterized is logged and left OUT
  of the sync: a wrong-sized buffer is rejected anyway, and an invisible icon is
  worse than no icon.
  KNOWN LIMITATION: a taskbar theme switch is picked up at the next sync (a
  layout edit, a language change, a window boot), not the instant it happens —
  there is no `WM_SETTINGCHANGE` listener yet.

## The flyout

- [S-04] Rust owns the live `TrayIcon` handles, the click→widget mapping and the
  flyout window. Tray icons are message-window resources, so every create /
  update / destroy is marshalled onto the main thread; building the flyout
  WebView is the opposite and runs on a worker, because
  `WebviewWindowBuilder::build` waits on the main thread's message pump.
- [S-05] Left-clicking a tray icon opens that widget in ONE shared borderless,
  always-on-top, taskbar-skipping window (`tray-flyout`), so N icons cost at
  most one WebView and only once the user has opened one. It is anchored off
  whichever work-area edge the click fell outside — which IS the taskbar edge,
  so a top / left / right taskbar works without a separate probe — centred on
  the click along the other axis, and clamped inside the work area. It takes
  focus, unlike every other Themia window, because it is a keyboard surface.
- [S-10] The flyout's target comes from its URL and only from its URL.
  Retargeting it to another icon RELOADS it with the new `widget=`, which is the
  same path the first open takes. It deliberately does not tell the live page to
  swap targets over an event: that gave the panel a second source of truth,
  which raced the throttled renderer of a hidden webview and left the flyout
  showing the PREVIOUS widget after a switch. Reopening the same icon skips the
  reload (`LOADED_FOR`), so the cost lands only on an actual switch.
- [S-11] Once the flyout window exists, showing it runs on the event-loop
  thread, where the blur-to-hide handler also runs. A click that blurs the open
  flyout raises both, and running the show on a worker raced that hide — the
  panel could be hidden a moment after being shown. Only the first-ever build is
  handed to a worker, because `WebviewWindowBuilder::build` waits on the event
  loop's own message pump.
- [S-12] When a load is needed, the window stays HIDDEN until the new document
  reports itself ready (`tray_flyout_ready`, carrying the widget it mounted).
  Showing first and loading after meant the panel appeared with the PREVIOUS
  widget for as long as the navigation took, which is what the user sees as a
  flash. Only the request a document was loaded FOR may show it: a superseded
  load (the user clicked another icon meanwhile) reports ready too, and acting
  on that would put the wrong widget on screen. A click for a widget already
  loading re-aims the pending request rather than starting a second load, and
  reopening a widget whose document is already live skips the wait entirely.
  KNOWN LIMITATION: a document that never boots never shows. There is
  deliberately no timeout fallback — a wall-clock guess is not a correctness
  mechanism — and the case is the same total failure that would break any other
  window ([WV-01] covers the WebView2 crash half of it).
- [S-16] The panel stands off every edge it is clamped against — the work-area
  edge it hangs from and the screen edge it is pushed against — by a margin
  authored in LOGICAL px (`FLYOUT_GAP`, 12) and converted through the target
  monitor's scale alongside the size. A margin fixed in physical px thins out as
  the display scales, so on a 150% display it read as about five pixels and the
  panel looked stuck in the corner.
- [S-15] The flyout's position and its size are derived TOGETHER, both in
  PHYSICAL px. The placement clamps the panel against the work-area edges, so it
  is only correct if the window really is the size the clamp assumed. Sizing
  logically while placing physically broke that: a logical size resolves against
  whatever monitor the window is currently on, not the one it is about to move
  to, so on a scaled display the panel came out wider than allowed for and hung
  off the right edge — most visibly in a settings preview, which is always
  clamped flush against that edge. Position is applied before size, because
  moving a window across a DPI boundary can rescale it.
- [S-13] While a tray widget's config page is open in Settings, its flyout is
  PINNED open as a live preview: at its real size, anchored where a real tray
  click would put it, updating as the settings change — including its size, so
  the size rows move the panel they describe. Configuring a panel you cannot see
  is guesswork, and the panel is the thing being configured. A pin suppresses
  blur-to-hide (the settings window is what took the focus). It is released by
  selecting anything else, by closing the settings window, by Esc (an explicit
  dismissal must not bounce back), and by the widget leaving the tray. The
  anchor is derived from the taskbar's docked edge — the band the work area does
  not cover — so it needs no click to aim at.
- [S-06] The flyout closes on Esc, when it loses focus, and on a second click of
  the same tray icon. The third is the subtle one: that click's own blur fires
  first, so the blur handler checks whether the cursor is over the owning icon
  (tracked from the tray Enter/Leave events) and, if so, arms a one-shot
  suppression that the click then spends. A click anywhere else leaves the
  suppression unarmed, so the next click on that icon opens normally.
- [S-09] A widget removed from the tray while its flyout is showing takes the
  flyout down with it, rather than leaving an orphaned panel on screen.

## Settings

- [S-08] The config tree carries a flat `TRAY` group — like `DISPLAYS`, not like
  a view. Its widgets are listed directly under the group header, always visible
  and never behind a chevron: it is one short list that IS the section, so there
  is nothing worth collapsing, and a "Tray" row inside a "TRAY" header was the
  same word twice. An "Add widget" button closes the group (the way "Add view"
  closes the views group) and opens the standard picker bound to the tray. A
  row's right-click menu moves it up/down — its position IS the icon's position,
  and the tree is where that order is visible — or removes it. Selecting a row
  opens the widget's own config page, the same page a view's widget gets: the
  tray is a placement, not a different kind of widget, so it has no settings
  page of its own. The tray icon's right-click menu offers Open / widget
  settings / Themia settings / Quit, all translated frontend-side and re-pushed
  when the language changes; removal is deliberately absent from it, because the
  list is frontend-owned and has one editor.

## Non-goals

- No second "mirror this desktop widget into the tray" flag. A widget is either
  placed on a view or in the tray; two placements for one instance would mean
  two code paths keeping one widget alive.
- No tray icon for the app itself. Themia's presence is its widgets; the
  [EX-01] "window count reached zero" guard is what keeps the process alive, not
  a tray icon.
- No persistence of the icon set in Rust. The frontend is the source of truth,
  and it re-syncs whenever a window boots.
