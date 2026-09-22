# Themia for WinUI

A minimal native rebuild of [Themia](https://github.com/crazy-rabbit-software/themia) — the desktop
widget app — on **WinUI 3 / .NET 10, compiled ahead of time**. The point of the exercise is the
runtime cost: no web view, no JavaScript engine, no JIT.

It is a vertical slice, not a port of the catalogue: **one widget (a clock)**, but all the machinery
around it — edit mode, drag and resize, snapping, the widget picker, per-widget settings, views,
per-display view assignment, a tray icon, and persistence.

![the clock widget in edit mode](docs/edit-mode.png)

## What it costs

Measured on this machine (Windows 11, 3840x2160 at 150%, two displays), one clock widget:

| | |
|---|---|
| Executable | **7.1 MB** single native binary |
| Managed DLLs shipped | **0** — no CoreCLR, no `System.*.dll` |
| Working set | **~90 MB** (two full-screen widget surfaces + WinUI compositor) |
| Idle CPU | **~0.3% of one core** |
| Wakes per minute | **1** (a clock without seconds asks for a 1-minute cadence) |

The working set is dominated by WinUI's own compositor and render thread — a bare WinUI 3 window
with a single `TextBlock` measures ~83 MB here, so the entire app adds ~7 MB on top of the framework
floor. The total on-disk folder is ~123 MB because the Windows App SDK is bundled self-contained;
that is disk, not memory, and it buys not needing the runtime installed.

## Running it

```bash
dotnet build src/Themia.App/Themia.App.csproj -c Release
```

```bash
dotnet test tests/Themia.Core.Tests/Themia.Core.Tests.csproj
```

For the ahead-of-time build (needs the MSVC linker; `scripts/publish.ps1` puts `vswhere` on PATH for
you, which the ILCompiler needs and does not find on its own):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

The app has no main window. It starts in the tray:

- **Double-click the tray icon** (or double-click a widget) to toggle edit mode.
- **Right-click the tray icon** for edit mode, add widget, view switching, settings and exit.
- In edit mode: drag to move, eight handles to resize, **Alt** to bypass snapping, right-click a
  widget to configure, duplicate or delete.

State lives in `%APPDATA%\Themia.WinUI\state.json`; warnings and errors go to `themia.log` beside it.

## How it is put together

```
src/Themia.Core/      pure logic, no UI, no Windows dependency - all of the tests live here
  Geometry/           fractional rects, snapping, clamping, resize
  Model/              AppState / ViewState / WidgetInstance, enums, typed widget config
  Storage/            atomic single-writer persistence + load-time validation
  Time/               shared tick scheduling
  Widgets/            clock formatting
src/Themia.App/       WinUI 3 shell
  Interop/            Win32 (LibraryImport), desktop layering, monitors, tray icon
  Services/           app host, shared ticker, startup registration
  Shell/              widget surface, cards, settings, dialogs
  Widgets/            widget catalogue + the clock view
tests/                xUnit over Themia.Core
```

The split is deliberate: everything with a rule in it lives in `Themia.Core`, which has no UI types,
so the geometry, persistence and scheduling rules are tested without needing a window.

### The two ideas worth knowing

**Geometry is fractional.** A widget stores `x, y, w, h` as fractions of its screen, never pixels
(`[S-01]`/`[S-02]` in [SPEC.md](SPEC.md)). The same layout opens proportionally on any monitor. The
snap grid is an *aid* applied during a drag, never storage.

**One window per display, not one per widget.** All the widgets on a screen share a single
transparent, never-focused, click-through surface. The window is clipped with `SetWindowRgn` to the
union of the widget rects, so clicks anywhere else land on the desktop underneath. That is what keeps
the cost flat as widgets are added: one HWND, one composition target and one render thread per
display, no matter how many widgets you place.

## Deliberate limitations

These are choices or known platform limits, not oversights:

- **One widget type.** The registry (`WidgetCatalog`) and the persisted discriminator (`WidgetKind`)
  are built for more; adding one is an enum member plus a catalogue entry plus a view.
- **Acrylic, not true transparency.** The Windows App SDK ships only Mica and Desktop Acrylic, and
  the `SystemBackdrop` extension point wants a brush from the system compositor that a WinUI window
  does not expose. With no backdrop the window composes opaque black. So the surface uses acrylic and
  the window *region* does the shaping — outside edit mode the window is clipped to the widget rects,
  so acrylic shows only inside the cards, which is the material a Fluent card should sit on anyway.
  In edit mode the clip drops and the whole desktop frosts, which doubles as the edit scrim.
- **`WidgetSurface.Transparent` shows acrylic**, not raw wallpaper, for the same reason.
- **Mixed-DPI multi-monitor is untested.** Placement uses each monitor's physical work area and
  layout uses the window's own rasterization scale, so it should be correct, but this machine runs
  both displays at 150% and I could not exercise the mixed case.
- **No sync, licensing, updater or localization.** Out of scope for a vertical slice.

## Things that only break in the published build

Two failures here were invisible during development and appeared only after `dotnet publish`. Both
are now pinned by a comment at the fix site:

- `dotnet publish` **dropped every compiled XAML file** (`.xbf`). The app started and then threw
  `XamlParseException` at the first `InitializeComponent`. The XAML compiler writes them to the build
  output but never registers them as publish items; the `PublishCompiledXaml` target in the csproj
  does.
- The ILCompiler needs **`vswhere` on PATH** to find the MSVC linker, and does not add it itself.

There is also a DPI trap worth recording, because it cost the most time: `AppWindow.MoveAndResize`
takes **physical** pixels, but XAML lays out in DIPs, and PowerShell is DPI-virtualized unless you
call `SetProcessDpiAwarenessContext` — so a screenshot-based check of a 3840x2160 display at 150%
silently reports 2560x1440 and every coordinate you compare against is wrong by 1.5x.
