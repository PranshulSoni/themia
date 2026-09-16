# Add-Widget Picker Search: SPEC

Pins the SEARCH behavior of the Add-Widget page (`src/components/AddWidgetPage.tsx`).
Matching + ranking live in `src/components/widgetSearch.ts` (pure); the curated
synonym list lives in `src/widgets/searchKeywords.ts`. Grouping/bucketing is
specified by `pickerGroups.ts` and is not re-specified here.

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never
renumber or reuse.

## Matching
- [S-01] The query matches against SIX field classes, weighted: the widget's
  label (1.0), its curated keywords (0.9), its registry type key (0.8), its
  category (0.55), its providers (0.55) and its description (0.45). A hit in a
  heavier field outranks the same-quality hit in a lighter one.
- [S-02] Both sides are normalized before comparison: lowercased, diacritics
  stripped (`Wetter` ≡ `wetter`, `Größe` ≡ `grosse`), `ß` → `ss`, and camelCase
  registry keys split into words (`githubNotifications` → `github` +
  `notifications`). Anything that is not a letter or digit is a separator.
- [S-03] A multi-word query is an AND over its tokens: every token must match
  some field of the widget, so `weather forecast` cannot be satisfied by a
  widget that only knows `weather`. Tokens may match different fields.
- [S-04] Widgets carry curated English synonyms (`WIDGET_KEYWORDS`) covering
  what they show (`wifi`, `temperature`), the vendor/protocol behind them
  (`outlook`, `imap`), the job they do (`timer`, `launcher`) and common
  alternate spellings (`todo`/`tasks`). An unlisted widget simply has none.
- [S-06] The index holds the TRANSLATED label/description **and** the English
  original, so on a localized UI both `wetter` and `weather` find the Weather
  widget, and the English-only keyword list keeps working in every language.

## Typo tolerance
- [S-07] A token that matches no field literally still matches a word within a
  bounded Damerau-Levenshtein budget: 0 edits for tokens of ≤3 characters
  (a single edit at that length reaches half the registry), 1 edit for 4–6,
  2 edits for ≥7. Transposition (`clcok`) costs one edit, not two.
- [S-08] A mistyped PREFIX matches too: the token is also compared against the
  candidate word's leading slice of the same length, so `kalend` reaches
  `calendar`. Words shorter than 3 characters are never fuzzy-matched, and a
  token of ≤5 characters must additionally share its FIRST letter with the
  candidate word — at that length a wrong first letter is a different word, not
  a typo (`mail` must not reach `daily`).
- [S-11] Fuzzy matching applies to labels, keywords, type keys, categories and
  providers, but NEVER to descriptions: prose carries too many common words for
  a one-edit neighbourhood to mean anything. Descriptions stay literal
  (substring and prefix) matches.

## Ranking & presentation
- [S-05] With a non-empty query the list is ordered best-match-first, ties
  broken by label, and is presented as ONE flat list — the Group-by choice is
  suspended while searching, since a bucketed list would scatter the best match
  under whichever category header it belongs to. Clearing the query restores
  the grouped, alphabetical library.
- [S-09] Match quality, best to worst: whole field equals the token > whole word
  equals the token > field starts with the token > a word starts with the token
  > token appears anywhere > typo match. A query that is a prefix of the label
  scores an additional bonus, so `cl` puts Clock above widgets that merely
  mention a clock in their description.
- [S-10] Score 0 means "not a match" and the widget is dropped; an empty query
  matches everything.
