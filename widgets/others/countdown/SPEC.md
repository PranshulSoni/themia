# Countdown: Spec

Behavior tags are stable IDs. When adding behavior, append a new [C-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

The Countdown widget holds an ordered list of countdown timers. Each timer points to a target date+time and renders the remaining (or elapsed) duration in a configurable format.

## Display
- [C-01] Header shows a Timer icon + title (`label` override, else "Countdowns").
- [C-02] Empty state shows "No timers. Click ⚙ to add." when `config.timers` is empty.
- [C-03] Each row shows the timer's label (or "Untitled" when blank), the absolute target date/time as a subtitle, and the formatted remaining duration on the right.
- [C-04] Remaining duration uses tabular numerals so digit width does not jitter when ticking.
- [C-05] Hovering a timer row shows the full local date/time string as a `title` tooltip.
- [C-06] `hideHeader` toggles the widget title bar.

## Data model
- [C-10] Widget stores `config.timers: Timer[]`. Each timer has stable `id`, string `label`, ISO-8601 `target`, and a per-timer `format`.
- [C-11] Display formats: `auto` (smart pick) | `days` (whole days only) | `dhm` (Nd Nh Nm) | `dhms` (Nd Nh Nm Ns).

## Tick / refresh
- [C-20] The widget polls via `createPolling`. Tick interval is `1000ms` if any timer uses `dhms`; otherwise `30000ms` (sufficient for minute granularity).
- [C-21] Polling pauses while the app is hibernated (handled by `createPolling`).

## Auto format rules
- [C-30] `auto` rules for remaining time:
  - `< 1 min` → `"in a moment"`
  - `< 1 hour` → `"N min(s)"`
  - `< 1 day` → `"Nh Nm"` (or `"N hr(s)"` when minutes = 0)
  - `< 7 days` → `"Nd Nh"` (or `"N day(s)"` when hours = 0)
  - `≥ 7 days` → `"N days"`

## Elapsed (past target)
- [C-40] When a timer's target is in the past, the widget switches to elapsed mode: the remaining text becomes `"<formatted> ago"` and gets the `--destructive` color accent. The label dims to `--muted-foreground`.
- [C-41] In `auto` mode and elapsed mode, durations under 1 minute render as `"just now"` instead of `"in a moment ago"`.

## Configuration
- [C-50] Each timer row in the config panel has: up/down reorder buttons, label text input, `<input type="datetime-local">` for target (local timezone), `<select>` for display format, and an ✕ button to remove.
- [C-51] "Add timer" button appends a new timer with `target` = now + 24h (seconds zeroed), `label = ""`, `format = 'auto'`.
- [C-52] Timers can be reordered via the up/down chevron buttons (one swap per click). The first row's up button and the last row's down button are disabled. HTML5 drag is intentionally NOT used here because the app's window-level Win32 `IDropTarget` swallows in-modal HTML5 drags.
- [C-53] Datetime values round-trip through local timezone: `isoToLocalInput` strips to local `YYYY-MM-DDTHH:MM`; `localInputToIso` parses local input back to ISO UTC.
- [C-54] The label `<input>` updates on every keystroke (`onInput`); the `<Index>` loop keeps DOM nodes stable so the input retains focus across keystrokes.
- [C-55] Clearing (or entering an unparseable value into) the datetime-local field leaves the timer's existing `target` unchanged: `localInputToIso` returns `null` and the caller skips the write. The field is never silently retargeted to the current instant.

## Context menu
- [C-60] Right-clicking a timer row opens a menu with: a "Display" header, format choices (Auto / Days only / Days·Hours·Min / Days·Hours·Min·Sec) marked active with a check; separator; Remove (destructive).
- [C-61] Format choices use `keepOpen: true` so the user can preview formats without the menu closing.
- [C-62] Widget chrome right-click (registered via `contextActions`) toggles Hide/Show header.
