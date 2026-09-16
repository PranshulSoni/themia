// Matching + ranking for the Add-Widget picker's search box
// (WidgetPicker.SPEC [S-01]..[S-08]). Pure and dependency-free so the whole
// behavior is unit-testable without mounting the Solid component.
//
// The old matcher was a plain `label.includes(query)` over four fields: it
// missed every typo ("calender") and every synonym a user reaches for
// ("timer", "wifi", "temperature"). This one normalizes both sides, matches
// per query token against weighted fields (including a curated alias list),
// tolerates typos via a bounded Damerau-Levenshtein distance, and returns a
// SCORE so the list can be ordered best-match-first instead of alphabetically.

/** Field a query token matched in. Weights below encode "how much does a hit
 *  here mean the user meant this widget". */
export type SearchField = 'label' | 'keyword' | 'type' | 'category' | 'provider' | 'description';

const FIELD_WEIGHT: Record<SearchField, number> = {
  label: 1,
  keyword: 0.9,
  type: 0.8,
  category: 0.55,
  provider: 0.55,
  description: 0.45,
};

// Match qualities, best → worst. Gaps are wide enough that a weighted lower
// quality never outranks a higher one in the same field class.
const Q_EXACT = 100;      // whole field equals the token
const Q_WORD = 90;        // a whole word in the field equals the token
const Q_PREFIX = 70;      // field starts with the token ("we" → "weather")
const Q_WORD_PREFIX = 60; // a word inside the field starts with the token
const Q_SUBSTRING = 35;   // token appears anywhere ("core" → "per-core load")
const Q_TYPO = 22;        // within the edit-distance budget ("calender")

/** Extra points when the ENTIRE query is a prefix of the label, so typing "cl"
 *  puts Clock above widgets that merely mention a clock in their description. */
const LABEL_PREFIX_BONUS = 30;

/** One widget as the search sees it. Built by the picker from the *translated*
 *  label/description plus the English originals, so a German user finds
 *  "Wetter" by typing either "wetter" or "weather" ([S-06]). */
export interface WidgetSearchDoc {
  /** Registry key, e.g. `githubNotifications`. */
  type: string;
  /** Display labels (translated first, English fallback second). */
  labels: string[];
  /** Curated synonyms/aliases ([S-04]). */
  keywords?: string[];
  descriptions?: string[];
  /** Category bucket names, translated and/or raw. */
  categories?: string[];
  providers?: string[];
}

/** Lowercase, strip diacritics and ligature-ish spellings, so "Wetter"/"wetter"
 *  and "Größe"/"grosse" compare equal. */
export function normalize(s: string): string {
  return s
    .toLowerCase()
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .replace(/ß/g, 'ss')
    .trim();
}

/** Split into comparable words. camelCase registry keys ("githubNotifications")
 *  are split first so their parts are searchable on their own. Anything that is
 *  not a letter/digit is a separator; CJK text has no separators and simply
 *  stays one token, where substring matching still applies. */
export function tokenize(s: string): string[] {
  return normalize(s.replace(/([a-z0-9])([A-Z])/g, '$1 $2'))
    .split(/[^\p{L}\p{N}]+/u)
    .filter(Boolean);
}

/** Typo budget for a query token. Short tokens get none: at 3 characters a
 *  single edit reaches half the registry, which is noise, not tolerance. */
export function maxTypos(token: string): number {
  if (token.length <= 3) return 0;
  if (token.length <= 6) return 1;
  return 2;
}

/** Damerau-Levenshtein (optimal string alignment) bounded by `max`: returns
 *  `max + 1` as soon as no alignment can stay inside the budget. Bounded so a
 *  long description word never costs a full O(n*m) table. */
export function editDistance(a: string, b: string, max: number): number {
  if (a === b) return 0;
  if (Math.abs(a.length - b.length) > max) return max + 1;
  if (!a.length) return b.length;
  if (!b.length) return a.length;

  let prev2: number[] = [];
  let prev: number[] = Array.from({ length: b.length + 1 }, (_, j) => j);
  let cur: number[] = new Array(b.length + 1);

  for (let i = 1; i <= a.length; i++) {
    cur[0] = i;
    let rowMin = cur[0];
    for (let j = 1; j <= b.length; j++) {
      const cost = a[i - 1] === b[j - 1] ? 0 : 1;
      let v = Math.min(cur[j - 1] + 1, prev[j] + 1, prev[j - 1] + cost);
      // Transposition ("teh" → "the") costs one edit, not two.
      if (i > 1 && j > 1 && a[i - 1] === b[j - 2] && a[i - 2] === b[j - 1]) {
        v = Math.min(v, prev2[j - 2] + 1);
      }
      cur[j] = v;
      if (v < rowMin) rowMin = v;
    }
    if (rowMin > max) return max + 1;
    prev2 = prev;
    prev = cur;
    cur = new Array(b.length + 1);
  }
  return prev[b.length];
}

/** Does `token` reach `word` within its typo budget? Also compared against the
 *  word's leading slice of the same length, so a mistyped PREFIX still lands
 *  ("kalend" → "calendar" via "calend"). */
function typoMatch(token: string, word: string, budget: number): boolean {
  if (word.length < 3) return false;
  // A short token whose FIRST letter is already wrong is noise, not a typo:
  // "mail" would otherwise reach "daily". Longer tokens keep the allowance,
  // which is what lets a German "kalender" find "Calendar" ([S-08]).
  if (token.length <= 5 && token[0] !== word[0]) return false;
  if (editDistance(token, word, budget) <= budget) return true;
  if (word.length > token.length) {
    return editDistance(token, word.slice(0, token.length), budget) <= budget;
  }
  return false;
}

/** Best quality with which `token` matches one field value. 0 = no match. */
function matchField(token: string, f: IndexedField): number {
  const { text, words } = f;
  if (!text) return 0;
  if (text === token) return Q_EXACT;
  if (words.includes(token)) return Q_WORD;
  if (text.startsWith(token)) return Q_PREFIX;
  if (words.some(w => w.startsWith(token))) return Q_WORD_PREFIX;
  if (text.includes(token)) return Q_SUBSTRING;
  if (!f.typo) return 0;
  const budget = maxTypos(token);
  if (budget > 0 && words.some(w => typoMatch(token, w, budget))) return Q_TYPO;
  return 0;
}

interface IndexedField {
  weight: number;
  text: string;
  words: string[];
  /** Fuzzy matching allowed here ([S-11])? Descriptions are prose: they carry a
   *  lot of common words, and one edit away from any of them is mostly noise. */
  typo: boolean;
}

/** Pre-normalized form of a doc. Building it is the expensive half of a query,
 *  so the picker builds it once per widget and reuses it for every keystroke. */
export interface IndexedWidget {
  type: string;
  /** Normalized primary label, for the whole-query prefix bonus. */
  labelText: string;
  fields: IndexedField[];
}

function pushField(out: IndexedField[], kind: SearchField, value: string | undefined) {
  if (!value) return;
  const text = normalize(value);
  if (!text) return;
  out.push({ weight: FIELD_WEIGHT[kind], text, words: tokenize(value), typo: kind !== 'description' });
}

export function indexWidget(doc: WidgetSearchDoc): IndexedWidget {
  const fields: IndexedField[] = [];
  for (const l of doc.labels) pushField(fields, 'label', l);
  for (const k of doc.keywords ?? []) pushField(fields, 'keyword', k);
  pushField(fields, 'type', doc.type.replace(/([a-z0-9])([A-Z])/g, '$1 $2'));
  for (const c of doc.categories ?? []) pushField(fields, 'category', c);
  for (const p of doc.providers ?? []) pushField(fields, 'provider', p);
  for (const d of doc.descriptions ?? []) pushField(fields, 'description', d);
  return { type: doc.type, labelText: normalize(doc.labels[0] ?? ''), fields };
}

/** Score one indexed widget against a raw query. 0 means "does not match" —
 *  EVERY query token has to land somewhere ([S-03]), so "weather forecast"
 *  cannot be satisfied by a widget that only knows "weather". */
export function scoreWidget(w: IndexedWidget, query: string): number {
  const tokens = tokenize(query);
  if (!tokens.length) return 1;

  let total = 0;
  for (const token of tokens) {
    let best = 0;
    for (const f of w.fields) {
      const q = matchField(token, f);
      if (q) best = Math.max(best, q * f.weight);
    }
    if (!best) return 0;
    total += best;
  }

  const nq = normalize(query);
  if (nq && w.labelText.startsWith(nq)) total += LABEL_PREFIX_BONUS;
  return total;
}

/** Filter + rank in one pass. Non-matching entries drop out; the rest come back
 *  best-first, ties broken by label so the order is stable ([S-05]). */
export function rankWidgets<T>(
  items: { indexed: IndexedWidget; value: T }[],
  query: string,
): T[] {
  const scored: { score: number; label: string; value: T }[] = [];
  for (const it of items) {
    const score = scoreWidget(it.indexed, query);
    if (score > 0) scored.push({ score, label: it.indexed.labelText, value: it.value });
  }
  scored.sort((a, b) => b.score - a.score || a.label.localeCompare(b.label));
  return scored.map(s => s.value);
}
