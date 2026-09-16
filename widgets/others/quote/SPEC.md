# Quote of the Day: SPEC

Experimental widget. A bundled, offline daily quote. Mirror `[S-NN]`
tags in tests.

- **[S-01]** Quotes are bundled in `quotes.ts` (no network). The day's quote is
  `QUOTES[localDayNumber % QUOTES.length]`, deterministic per LOCAL day, stable
  across reloads.
- **[S-02]** The header shuffle button shows a random quote; this override is
  ephemeral and resets to the day's quote on reload.
- **[S-03]** Header toggled via the widget context menu.
