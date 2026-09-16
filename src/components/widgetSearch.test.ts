import { describe, it, expect } from 'vitest';
import {
  normalize, tokenize, maxTypos, editDistance,
  indexWidget, scoreWidget, rankWidgets,
  type WidgetSearchDoc,
} from './widgetSearch.ts';
import { WIDGET_KEYWORDS } from '../widgets/searchKeywords.ts';

// A small stand-in registry covering the shapes the real one has: a
// multi-word label, a localized label with an English twin, camelCase types,
// and widgets that share vocabulary in their descriptions.
const DOCS: WidgetSearchDoc[] = [
  {
    type: 'clock', labels: ['Clock'], keywords: WIDGET_KEYWORDS.clock,
    descriptions: ['Clock for your timezone or any city in the world'],
    categories: ['Others'],
  },
  {
    type: 'calendar', labels: ['Calendar'], keywords: WIDGET_KEYWORDS.calendar,
    descriptions: ['See your upcoming events and meetings'],
    categories: ['Office'], providers: ['Microsoft'],
  },
  {
    type: 'weatherCurrent', labels: ['Wetter', 'Weather'], keywords: WIDGET_KEYWORDS.weatherCurrent,
    descriptions: ['Aktuelle Bedingungen für jeden Ort', 'Current conditions for any location'],
    categories: ['Online'],
  },
  {
    type: 'weatherForecast', labels: ['Weather Forecast'], keywords: WIDGET_KEYWORDS.weatherForecast,
    descriptions: ['Multi-day weather outlook for any location'],
    categories: ['Online'],
  },
  {
    type: 'net', labels: ['Network Monitor'], keywords: WIDGET_KEYWORDS.net,
    descriptions: ['Live upload and download speeds'],
    categories: ['System'],
  },
  {
    type: 'cpu', labels: ['CPU Monitor'], keywords: WIDGET_KEYWORDS.cpu,
    descriptions: ['Live CPU usage and per-core load'],
    categories: ['System'],
  },
  {
    type: 'githubNotifications', labels: ['GitHub Notifications'], keywords: WIDGET_KEYWORDS.githubNotifications,
    descriptions: ['Your GitHub notifications across all selected accounts'],
    categories: ['Online'], providers: ['GitHub'],
  },
  {
    type: 'pomodoro', labels: ['Pomodoro'], keywords: WIDGET_KEYWORDS.pomodoro,
    descriptions: ['Focus timer with work and break cycles'],
    categories: ['Others'],
  },
  {
    type: 'countdown', labels: ['Countdown'], keywords: WIDGET_KEYWORDS.countdown,
    descriptions: ['Count down to an important date'],
    categories: ['Others'],
  },
];

const INDEXED = DOCS.map(d => ({ indexed: indexWidget(d), value: d.type }));
const search = (q: string) => rankWidgets(INDEXED, q);
const top = (q: string) => search(q)[0];

describe('normalization [S-02]', () => {
  it('lowercases, strips diacritics and folds ß', () => {
    expect(normalize('Wetter')).toBe('wetter');
    expect(normalize('Grüße')).toBe('grusse');
    expect(normalize('  Café ')).toBe('cafe');
  });

  it('splits camelCase types and punctuation into words', () => {
    expect(tokenize('githubNotifications')).toEqual(['github', 'notifications']);
    expect(tokenize('Sun & Moon')).toEqual(['sun', 'moon']);
    expect(tokenize('to-do')).toEqual(['to', 'do']);
  });

  it('keeps unsegmented scripts as one token', () => {
    expect(tokenize('天气')).toEqual(['天气']);
  });
});

describe('typo budget [S-07]', () => {
  it('scales with token length; short tokens get none', () => {
    expect(maxTypos('cpu')).toBe(0);
    expect(maxTypos('clok')).toBe(1);
    expect(maxTypos('clock')).toBe(1);
    expect(maxTypos('calendar')).toBe(2);
  });

  it('counts a transposition as one edit, not two', () => {
    expect(editDistance('clcok', 'clock', 2)).toBe(1);
    expect(editDistance('teh', 'the', 2)).toBe(1);
  });

  it('bails out early beyond the budget', () => {
    expect(editDistance('clock', 'calendar', 1)).toBe(2); // max + 1
  });
});

describe('literal matching [S-01] [S-09]', () => {
  it('still finds the exact label (the old behavior)', () => {
    expect(top('Clock')).toBe('clock');
    expect(top('Network Monitor')).toBe('net');
  });

  it('a label prefix beats a description mention', () => {
    // "clock" appears in the clock description only; "countdown" starts with "co".
    expect(top('cou')).toBe('countdown');
    expect(top('cl')).toBe('clock');
  });

  it('matches the registry type key, split on camelCase', () => {
    expect(search('notifications')).toContain('githubNotifications');
  });

  it('matches category and provider names', () => {
    expect(search('system')).toEqual(expect.arrayContaining(['cpu', 'net']));
    expect(search('microsoft')).toContain('calendar');
  });
});

describe('keyword matching [S-04]', () => {
  it('finds a widget by what it shows, not by its label', () => {
    expect(search('wifi')).toContain('net');
    expect(search('temperature')).toEqual(expect.arrayContaining(['weatherCurrent', 'weatherForecast']));
    expect(search('memory' /* not in any label */)).not.toContain('clock');
  });

  it('finds a widget by the job it does', () => {
    expect(search('timer')).toEqual(expect.arrayContaining(['pomodoro', 'countdown']));
    expect(search('appointments')).toContain('calendar');
  });

  it('ranks a label hit above a keyword-only hit', () => {
    expect(top('countdown')).toBe('countdown');
  });
});

describe('typo tolerance [S-07] [S-08]', () => {
  it('survives a single wrong/missing/extra letter', () => {
    expect(search('calender')).toContain('calendar');
    expect(search('clok')).toContain('clock');
    expect(search('wether')).toContain('weatherCurrent');
    expect(search('pomodorro')).toContain('pomodoro');
  });

  it('survives a transposition', () => {
    expect(search('clcok')).toContain('clock');
    expect(search('calnedar')).toContain('calendar');
  });

  it('matches a MISTYPED PREFIX of a longer word [S-08]', () => {
    expect(search('kalend')).toContain('calendar');
  });

  it('does not fuzzy-match tokens of three characters or fewer', () => {
    // "cpx" is one edit from "cpu" but too short to be fuzzy-matched: a 3-letter
    // budget would make almost every widget a hit.
    expect(search('cpx')).toEqual([]);
  });

  it('a literal match still outranks a typo match', () => {
    expect(top('clock')).toBe('clock');
  });

  it('a short token with the wrong FIRST letter is not a typo [S-08]', () => {
    // "mail" is one edit from "daily" (a Countdown/Pomodoro-ish word) but must
    // not drag those in; the literal substring hit in "Email" still works.
    expect(search('dount')).toEqual([]);              // 5 chars, wrong first letter
    expect(search('dountdown')).toContain('countdown'); // long enough to forgive it
    expect(search('cound')).toContain('countdown');     // short, but first letter right
  });

  it('never fuzzy-matches inside a description [S-11]', () => {
    // "cities" is one edit from "city" in the Clock description; prose is
    // matched literally only, so this finds nothing.
    expect(search('citiy')).toEqual([]);
    expect(search('city')).toContain('clock'); // literal substring still matches
  });
});

describe('multi-token queries [S-03]', () => {
  it('requires every token to land somewhere', () => {
    expect(search('clock zzzz')).toEqual([]);
    // Both tokens have to land: Calendar knows "calendar" but nothing about wifi.
    expect(search('calendar wifi')).toEqual([]);
    // Weather Forecast matches both tokens in its label and ranks first; the
    // current-conditions widget matches "forecast" only via its keywords.
    expect(search('weather forecast')[0]).toBe('weatherForecast');
  });

  it('lets tokens match different fields', () => {
    // "github" → label, "inbox" → keyword.
    expect(search('github inbox')).toEqual(['githubNotifications']);
  });
});

describe('localized index [S-06]', () => {
  it('finds a translated widget by its translated OR its English label', () => {
    expect(search('wetter')).toContain('weatherCurrent');
    expect(search('weather')).toContain('weatherCurrent');
  });

  it('tolerates a typo in the translated label too', () => {
    expect(search('wettr')).toContain('weatherCurrent');
  });
});

describe('ranking contract [S-05] [S-10]', () => {
  it('an empty query matches everything and keeps input order', () => {
    expect(rankWidgets(INDEXED, '')).toHaveLength(DOCS.length);
    expect(rankWidgets(INDEXED, '   ')).toHaveLength(DOCS.length);
  });

  it('scores non-matches as 0 so they drop out', () => {
    const idx = indexWidget(DOCS[0]);
    expect(scoreWidget(idx, 'spreadsheet')).toBe(0);
    expect(scoreWidget(idx, 'clock')).toBeGreaterThan(0);
  });

  it('is deterministic: equal scores break ties by label', () => {
    const a = search('monitor');
    expect(a).toEqual([...a]);
    expect(a.indexOf('cpu')).toBeLessThan(a.indexOf('net')); // "CPU Monitor" < "Network Monitor"
  });
});
