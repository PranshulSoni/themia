# Clipboard History: SPEC

Experimental widget. Event-driven clipboard-text history. Mirror
`[S-NN]` tags in tests.

- **[S-01]** The backend registers a message-only window with
  `AddClipboardFormatListener` (NO polling). Each `WM_CLIPBOARDUPDATE` reads the
  Unicode clipboard text and emits `clipboard://update`. The WndProc is wrapped
  in `catch_unwind`; clipboard handles are opened/closed on every path.
- **[S-02]** `clipboard_start` is idempotent (single listener thread) and emits
  the current clipboard once on start. `clipboard_set(text)` re-copies an entry.
- **[S-03]** The widget keeps history in memory for the session (capped at
  `maxItems`, default 50), most-recent first, de-duplicated (re-copying moves an
  entry to the top). History clears on restart and via the header trash button.
- **[S-04]** Clicking an entry calls `clipboard_set` and shows a brief copied
  check; failures are logged, never thrown.
