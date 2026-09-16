# Themia e2e tests

End-to-end tests that drive the built Tauri desktop app through WebDriver.

## Stack
- **WebdriverIO** (v9): client
- **Mocha + Chai**: runner + assertions
- **tauri-driver**: bridges WebDriver to Tauri's WebView2 on Windows
- **msedgedriver**: native driver (Tauri on Windows uses WebView2 = Edge)

## One-time setup

```bash
# 1. Rust-side driver
cargo install tauri-driver --locked

# 2. msedgedriver matching your installed Edge version
#    https://developer.microsoft.com/microsoft-edge/tools/webdriver/
#    Place msedgedriver.exe on PATH.

# 3. Node deps
npm install
```

## Run

```bash
# Build the debug binary (once, or after code changes)
npm run test:e2e:build

# Run the suite
npm run test:e2e
```

## Structure

```
tests/
  e2e/
    wdio.conf.ts          WebdriverIO config (spawns tauri-driver, points at built exe)
    tsconfig.json         TS config for tests only
    helpers/
      app.ts              Shared: reset state, picker helpers, ctx menu, grid cells
    specs/
      01-picker.spec.ts           Widget add/remove via picker
      02-drag-resize-persist.spec.ts   Drag, resize, reload persistence
      03-views.spec.ts            Multi-view switching
      04-ctx-menu.spec.ts         Desktop + widget context menu actions
      05-shortcuts.spec.ts        Hotkey dispatch (ctrl+e, ctrl+w, escape, input gating)
      06-focus.spec.ts            focusStore integration (auto-focus, escape-blur)
      41-game-mode.spec.ts        Game-mode suspension toggle + grid unmount on fullscreen event
  fixtures/                 (reserved) HTTP fixture server for Graph/IMAP mocks
```

## Coverage boundaries

- **Covered here**: UI flows, layout persistence, shortcuts via JS keydown, context menu, focus gating.
- **NOT covered here** (test elsewhere):
  - Rust `WH_KEYBOARD_LL` app-key bridge: WebDriver can't trigger global Win32 hooks. Use Rust unit tests.
  - External APIs (MS Graph, IMAP): stub via the fixture server in `tests/fixtures/` (TODO).
  - Multi-window IPC (`postTransient`, storage sync): requires real multi-window launch.

## Known caveats

- `maxInstances: 1`: Tauri windows don't parallelise cleanly.
- Selectors (verified against current source):
  - Grid: `.grid`, widgets: `.widget`, resize handles: `.corner-overlay .rh-{tl,tr,bl,br}` (corners) and `.corner-overlay .rh-{t,r,b,l}` (sides)
  - Picker page: `.add-widget-page`, topbar: `.picker-topbar`, list: `.picker-list`, rows: `.widget-card` (label `.widget-card-label`, desc `.widget-card-desc`), search: `.picker-search`; persistent preview pane: `.picker-preview-pane` (preview box `.widget-pane-preview`, label `.picker-preview-label`, desc `.picker-preview-desc`)
  - Views: `.view-chip`, `.view-chip.active`, `.view-name-text`, `.view-add-btn`, `.view-add-input`
  - Context menu: `.ctx-menu`, items: `.ctx-item` (text in `.ctx-label`), separator: `.ctx-sep`, header: `.ctx-header`
- Storage keys (per `src/layout.ts`, `src/views.ts`):
  - `widget-layout`: default view layout
  - `widget-layout-<viewId>`: per-view layout
  - `widget-global-design`: design state
  - `themia-edit-mode`: edit-mode flag
- `window.confirm` is used by the Remove widget action, so tests must call `stubConfirm(true)` first.
- `pickWidget('Folder')` expects a widget labelled "Folder" in `src/widgets/registry.tsx`. Adjust spec if renamed.

## CI

Run on `windows-latest` (tauri-driver supports Windows + Linux, not macOS). Example:

```yaml
# .github/workflows/e2e.yml
jobs:
  e2e:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: 20 }
      - uses: dtolnay/rust-toolchain@stable
      - run: cargo install tauri-driver --locked
      - run: npm ci
      - run: npm run test:e2e:build
      - run: npm run test:e2e
```

Edge + msedgedriver are pre-installed on GH `windows-latest` runners.
