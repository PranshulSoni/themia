# Dev Tools Spec

Developer-only surfaces, compiled out of production builds. Behavior tags are
stable IDs. When adding behavior, append a new [D-NN]; never renumber or reuse
existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Development views (`src/dev/labView.ts`)

A set of throwaway views that together hold one instance of every registered
widget, split across multiple views because they don't all fit on one, for
debugging all widgets at once in the real grid with real interactions and
(under `tauri dev`) live backend data.

- [D-01] A "Development views" toggle appears in Settings → General, under the DevTools row. Gated behind `globalDesign.devMode`, hidden unless developer mode is on. (Supersedes the former 🧪 view-bar button.)
- [D-02] Toggling ON regenerates the dev views and switches to the first; toggling OFF removes them. State is persisted in the `themia-dev-views` localStorage flag and mirrored by the reactive `devViewsEnabled()` signal so the toggle reflects current state.
- [D-09] While the flag is set, every app boot (in developer mode) regenerates the dev views from the registry (without switching the active view), so newly added widgets always appear and stale layouts never linger.
- [D-03] On (re)generate, the layout is rebuilt from the registry: every registered widget is tiled, so newly added widgets appear automatically with no hand-built screen.
- [D-04] Widgets are tiled into a 4×3 grid per view (12 per view) as fractional rects (`w=1/4, h=1/3`); when a view fills, the next chunk starts a new view (`dev-1`, `dev-2`, …). No overlaps.
- [D-05] Each placed widget's config is `defaultConfig` merged with a per-type seed (`LAB_CONFIG`): data-driven widgets point at public sources (weather→Zurich, rss→Hacker News, stocks→AAPL/MSFT/NVDA) and content widgets reuse `demoWidgetConfigs` (notes/countdown/shortcut/bookmarks).
- [D-06] Dev views NEVER force demo data; they behave exactly like normal views. Each widget is seeded with a realistic CONFIG (`LAB_CONFIG`, [D-05]) so configurable/data widgets (weather, stocks, github repo, system stats under `tauri dev`, content widgets) show real content; account-gated widgets without a connected account show their normal empty/connect state. `isDemo()` is governed solely by the persisted `demoMode` and the window-scoped `previewDemo`, so dev views do not affect it. (Earlier attempts to inject demo data for dev views, namely full demo and a real-first/demo-fallback variant, were removed: both leaked demo data into normal views and/or behaved inconsistently across widgets.)
- [D-07] Disabling deletes every `dev-*` view's layout key and view-list entry; if a dev view was active, switches to the first remaining view (falling back to a seeded `default` view if none remain).
- [D-08] Rendered through the real `WidgetGrid`, so all production interactions apply unchanged: drag, resize, right-click context menu, and Configure. Otherwise they behave like ordinary views (switchable, persisted, editable).

## Developer-mode disclaimer banner (`ConfigWindow.tsx`)

- [D-10] While `globalDesign.devMode` is on, a disclaimer banner (`.config-dev-banner`) renders at the very top of the config window, full width, ABOVE the whole shell (sidebar tree AND content panel), so it shows on every config page (App Settings, widget/view/display config, Add Widget). It is **primary-accented** (`--primary-subtle-bg` / `--primary-subtle-border`, icon in `--primary`). It warns that Developer Mode unlocks experimental, not-fully-tested features (pre-release widgets, extra providers, demo/dev views) and to use them at your own risk. Hidden entirely when devMode is off. Text is i18n key `settings.devBanner.text` (all shipped locales).

## Notes

- The full app cannot boot in a plain browser (no Tauri): `FolderView.tsx` calls `getCurrentWindow()` at module load. Exercise dev views under `npm run dev`, not browser preview.
