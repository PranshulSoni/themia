# Web Search: SPEC

Experimental widget. A search box that opens a query in the default
browser. Mirror these `[S-NN]` tags in test names.

- **[S-01]** A single text input. Submitting (Enter) opens the selected engine's
  results URL via the `open_url` backend command, then clears the input.
- **[S-02]** Blank/whitespace-only queries are ignored (no navigation).
- **[S-03]** The query is URL-encoded (`engines.ts`). Supported engines are a
  closed set (`SearchEngine`): google, bing, duckduckgo, startpage.
- **[S-04]** An unknown/missing configured engine falls back to google
  (`resolveEngine`); never throws.
- **[S-05]** Header is hidden by default; toggled via the widget context menu.
