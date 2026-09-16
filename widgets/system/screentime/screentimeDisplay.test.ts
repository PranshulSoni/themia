// Pure display helpers for the Screen Time widget (SPEC.md [S-03]..[S-05]).
// Kept lucide-free and node-importable, like cpu/fanDisplay.test.ts.
import { describe, it, expect } from 'vitest';
import {
  formatDuration, topApps, barFraction, dateForOffset, shiftDateKey, dayDescriptor,
  parseDateKey, MAX_HISTORY_OFFSET, appColor, appInitial, donutSegments, uniquePaths,
  parseHidden, filterHidden, sumSecs, parsePeriod, clampStyle, resolveView, stylesFor,
  isRangePeriod, rangeLength,
  parseRange, dayTotals, maxDaySecs, totalSecs, heatLevel, heatmapCells,
  heatmapColumnDates, HEATMAP_COLS, WEEK_DAYS, MONTH_DAYS,
  aggregateApps, stackForDay, migrateMetrics, dailyAverage,
  showsApps, chartCap, DEFAULT_MAX_APPS, totalSwitches,
  applyLegacyMetrics, ghostRows, MAX_APPS_LIMIT,
  shareOf, parseGoalSecs, parseAppGoals, goalState,
} from './screentimeDisplay.ts';

describe('formatDuration [S-04]', () => {
  it('formats hours and minutes at >= 1h', () => {
    expect(formatDuration(3 * 3600 + 24 * 60)).toBe('3h 24m');
    expect(formatDuration(3600)).toBe('1h 0m');
    expect(formatDuration(10 * 3600 + 59 * 60 + 59)).toBe('10h 59m');
  });

  it('formats whole minutes between 1m and 1h', () => {
    expect(formatDuration(60)).toBe('1m');
    expect(formatDuration(59 * 60 + 59)).toBe('59m');
  });

  it('collapses under a minute to <1m, including zero and negatives', () => {
    expect(formatDuration(59)).toBe('<1m');
    expect(formatDuration(0)).toBe('<1m');
    expect(formatDuration(-5)).toBe('<1m'); // defensive: bad backend data
  });
});

describe('topApps [S-03]', () => {
  const apps = [
    { exe: 'chrome', name: 'Google Chrome', secs: 5000 },
    { exe: 'code', name: 'Visual Studio Code', secs: 9000 },
    { exe: 'discord', name: 'Discord', secs: 1200 },
    { exe: 'terminal', name: 'Windows Terminal', secs: 300 },
  ];

  it('sorts descending by seconds and caps at maxApps', () => {
    const top = topApps(apps, 2);
    expect(top.map((a) => a.exe)).toEqual(['code', 'chrome']);
  });

  it('defends against unsorted backend data and bad maxApps', () => {
    expect(topApps(apps, 0).length).toBe(0); // [S-53] 0 lists nothing
    expect(topApps(apps, -3).length).toBe(0); // corrupt config, still not negative
    expect(topApps([], 5)).toEqual([]);
    expect(topApps(apps, 99).length).toBe(4);
    // Non-finite config (corrupt store, undefined coerced) → default cap,
    // never an empty list.
    expect(topApps(apps, NaN).length).toBe(4);
    expect(topApps(apps, Infinity).length).toBe(4);
  });
});

describe('dateForOffset [S-11]', () => {
  // Fixed reference: Thursday 2026-07-02.
  const now = new Date(2026, 6, 2, 15, 30);

  it('offset 0 is the local date today', () => {
    expect(dateForOffset(0, now)).toBe('2026-07-02');
  });

  it('steps back across month and year boundaries', () => {
    expect(dateForOffset(1, now)).toBe('2026-07-01');
    expect(dateForOffset(2, now)).toBe('2026-06-30');
    expect(dateForOffset(33, now)).toBe('2026-05-30');
    expect(dateForOffset(183, new Date(2026, 0, 2))).toBe('2025-07-03');
  });

  it('pads month and day to two digits', () => {
    expect(dateForOffset(0, new Date(2026, 0, 5))).toBe('2026-01-05');
  });

  it('clamps negative and non-finite offsets to today', () => {
    expect(dateForOffset(-3, now)).toBe('2026-07-02');
    expect(dateForOffset(NaN, now)).toBe('2026-07-02');
  });
});

describe('shiftDateKey [S-11]', () => {
  it('steps across month, year, and leap boundaries', () => {
    expect(shiftDateKey('2026-07-01', -1)).toBe('2026-06-30');
    expect(shiftDateKey('2026-06-30', 1)).toBe('2026-07-01');
    expect(shiftDateKey('2026-01-01', -1)).toBe('2025-12-31');
    expect(shiftDateKey('2024-03-01', -1)).toBe('2024-02-29'); // leap
    expect(shiftDateKey('2026-07-02', 0)).toBe('2026-07-02');
  });

  it('round-trips through parseDateKey at local midnight', () => {
    const d = parseDateKey('2026-07-02');
    expect(d.getFullYear()).toBe(2026);
    expect(d.getMonth()).toBe(6);
    expect(d.getDate()).toBe(2);
    expect(d.getHours()).toBe(0);
  });

  it('date keys compare chronologically as strings (cap logic relies on it)', () => {
    expect('2026-06-30' < '2026-07-01').toBe(true);
    expect(shiftDateKey('2026-07-02', -MAX_HISTORY_OFFSET) < '2026-07-02').toBe(true);
  });
});

describe('dayDescriptor [S-12]', () => {
  const today = '2026-07-02';

  it('null selection follows today', () => {
    expect(dayDescriptor(null, today)).toEqual({ kind: 'today' });
  });

  it('the day before today is yesterday; older days are dates', () => {
    expect(dayDescriptor('2026-07-01', today)).toEqual({ kind: 'yesterday' });
    expect(dayDescriptor('2026-06-30', today)).toEqual({ kind: 'date' });
  });

  it('a pinned date stays a date after midnight moves today forward', () => {
    // Pinned "yesterday" (Jul 1) becomes a plain date once today is Jul 3:
    // the descriptor derives from BOTH keys, so the label self-heals.
    expect(dayDescriptor('2026-07-01', '2026-07-03')).toEqual({ kind: 'date' });
    // A pinned key equal to (or newer than) today reads as today.
    expect(dayDescriptor('2026-07-03', '2026-07-03')).toEqual({ kind: 'today' });
  });

  it('MAX_HISTORY_OFFSET caps the UI at 90 calendar days', () => {
    // Note: the backend retains the 90 most recent dates WITH USAGE ([S-28]),
    // which can span more than 90 calendar days; the UI window is the last
    // 90 calendar days ([S-11]).
    expect(MAX_HISTORY_OFFSET).toBe(89);
  });
});

describe('appColor [S-36]', () => {
  it('is stable for a given exe (same app keeps its color across days)', () => {
    expect(appColor('chrome')).toBe(appColor('chrome'));
    expect(appColor('code')).toBe(appColor('code'));
  });

  it('returns a valid hsl() string', () => {
    expect(appColor('chrome')).toMatch(/^hsl\(\d+(\.\d+)? \d+% \d+%\)$/);
  });

  it('gives different apps different hues (spot check)', () => {
    expect(appColor('chrome')).not.toBe(appColor('code'));
    expect(appColor('discord')).not.toBe(appColor('spotify'));
  });

  it('never throws on empty or unicode keys', () => {
    expect(appColor('')).toMatch(/^hsl\(/);
    expect(appColor('日本語')).toMatch(/^hsl\(/);
  });
});

describe('appInitial [S-37]', () => {
  it('is the first character, uppercased', () => {
    expect(appInitial('Google Chrome')).toBe('G');
    expect(appInitial('code')).toBe('C');
  });

  it('falls back to "?" for empty or whitespace names', () => {
    expect(appInitial('')).toBe('?');
    expect(appInitial('   ')).toBe('?');
  });
});

describe('donutSegments [S-36]', () => {
  const OTHER = 'Other';
  const OTHER_COLOR = 'var(--muted)';

  it('maps each app to a share of the total with cumulative offsets', () => {
    const apps = [
      { exe: 'a', name: 'A', secs: 60 },
      { exe: 'b', name: 'B', secs: 30 },
      { exe: 'c', name: 'C', secs: 10 },
    ];
    const segs = donutSegments(apps, 100, 5, OTHER, OTHER_COLOR);
    expect(segs.map((s) => s.key)).toEqual(['a', 'b', 'c']);
    expect(segs.map((s) => s.fraction)).toEqual([0.6, 0.3, 0.1]);
    expect(segs.map((s) => s.startFraction)).toEqual([0, 0.6, 0.9]);
    expect(segs.every((s) => !s.isOther)).toBe(true);
    // Each carries its stable per-app color.
    expect(segs[0].color).toBe(appColor('a'));
  });

  it('folds apps beyond the cap into a single "Other" remainder segment', () => {
    const apps = [
      { exe: 'a', name: 'A', secs: 60 },
      { exe: 'b', name: 'B', secs: 30 },
      { exe: 'c', name: 'C', secs: 8 },
      { exe: 'd', name: 'D', secs: 2 },
    ];
    const segs = donutSegments(apps, 100, 2, OTHER, OTHER_COLOR);
    expect(segs.map((s) => s.key)).toEqual(['a', 'b', '__other__']);
    const other = segs[2];
    expect(other.isOther).toBe(true);
    expect(other.name).toBe(OTHER);
    expect(other.color).toBe(OTHER_COLOR);
    expect(other.secs).toBe(10); // 100 - (60 + 30)
    expect(other.fraction).toBeCloseTo(0.1, 6);
    expect(other.startFraction).toBeCloseTo(0.9, 6);
  });

  it('adds no "Other" when the visible apps already cover the total', () => {
    const apps = [{ exe: 'a', name: 'A', secs: 100 }];
    const segs = donutSegments(apps, 100, 5, OTHER, OTHER_COLOR);
    expect(segs.length).toBe(1);
    expect(segs[0].fraction).toBe(1);
  });

  it('returns nothing for a zero or negative total (no ring to draw)', () => {
    expect(donutSegments([{ exe: 'a', name: 'A', secs: 0 }], 0, 5, OTHER, OTHER_COLOR)).toEqual([]);
    expect(donutSegments([], 0, 5, OTHER, OTHER_COLOR)).toEqual([]);
  });

  it('clamps so overreported app seconds never yield a negative "Other"', () => {
    // Defensive: live-interval rounding could make an app exceed the total.
    const apps = [{ exe: 'a', name: 'A', secs: 120 }];
    const segs = donutSegments(apps, 100, 5, OTHER, OTHER_COLOR);
    expect(segs.length).toBe(1);
    expect(segs[0].fraction).toBe(1); // clamped, never > 1
    expect(segs.some((s) => s.isOther)).toBe(false); // no negative remainder
  });
});

describe('uniquePaths [S-37]', () => {
  it('collects distinct non-empty exe paths', () => {
    const apps = [
      { exe: 'a', name: 'A', secs: 1, path: 'C:/a.exe' },
      { exe: 'b', name: 'B', secs: 1, path: 'C:/b.exe' },
      { exe: 'c', name: 'C', secs: 1, path: 'C:/a.exe' }, // dup path
      { exe: 'd', name: 'D', secs: 1, path: null },
      { exe: 'e', name: 'E', secs: 1 }, // no path
    ];
    expect(uniquePaths(apps).sort()).toEqual(['C:/a.exe', 'C:/b.exe']);
  });
});

describe('hide-app helpers [S-39]', () => {
  const apps = [
    { exe: 'code', name: 'Visual Studio Code', secs: 100 },
    { exe: 'chrome', name: 'Google Chrome', secs: 60 },
    { exe: 'explorer', name: 'Windows Explorer', secs: 20 },
  ];

  it('parseHidden coerces arbitrary persisted config to a clean string[]', () => {
    expect(parseHidden(['a', 'b'])).toEqual(['a', 'b']);
    expect(parseHidden(undefined)).toEqual([]);
    expect(parseHidden(null)).toEqual([]);
    expect(parseHidden('chrome')).toEqual([]); // not an array
    // Drops non-strings and empties (corrupt store defense).
    expect(parseHidden(['a', 1, '', null, 'b'])).toEqual(['a', 'b']);
  });

  it('filterHidden removes buckets by exe key, accepting an array or a Set', () => {
    expect(filterHidden(apps, ['explorer']).map((a) => a.exe)).toEqual(['code', 'chrome']);
    expect(filterHidden(apps, new Set(['code', 'chrome'])).map((a) => a.exe)).toEqual(['explorer']);
    expect(filterHidden(apps, []).length).toBe(3);
  });

  it('sumSecs totals the shown apps, ignoring negatives', () => {
    expect(sumSecs(apps)).toBe(180);
    expect(sumSecs(filterHidden(apps, ['explorer']))).toBe(160); // total drops with the hidden app
    expect(sumSecs([])).toBe(0);
    expect(sumSecs([{ exe: 'x', name: 'X', secs: -5 }])).toBe(0);
  });

  it('a doughnut built from the filtered apps re-bases to the shown total', () => {
    const shown = filterHidden(apps, ['explorer']);
    const segs = donutSegments(shown, sumSecs(shown), 5, 'Other', 'var(--m)');
    expect(segs.map((s) => s.key)).toEqual(['code', 'chrome']); // explorer gone, no Other
    expect(segs[0].fraction).toBeCloseTo(100 / 160, 6); // re-based to 160, not 180
  });
});

describe('period × style [S-46]', () => {
  it('a period offers only the styles that can draw it', () => {
    // `bars` draws both ([S-50] horizontal for a day, [S-48] stacked per day for
    // a window); `list`/`doughnut` need a single day and `heatmap` needs many.
    // [S-53] `none` draws nothing, so it fits either period.
    expect(stylesFor('day')).toEqual(['doughnut', 'bars', 'none']);
    expect(stylesFor('week')).toEqual(['bars', 'heatmap', 'none']);
    expect(stylesFor('month')).toEqual(['bars', 'heatmap', 'none']);
  });

  it('parsePeriod tells absent/corrupt apart from an explicit day', () => {
    expect(parsePeriod('day')).toBe('day');
    expect(parsePeriod('week')).toBe('week');
    expect(parsePeriod('month')).toBe('month');
    expect(parsePeriod(undefined)).toBe(null);
    expect(parsePeriod('year')).toBe(null);
    expect(parsePeriod(30)).toBe(null);
  });

  it('clampStyle keeps a valid pair, falling back inside the period', () => {
    expect(clampStyle('day', 'doughnut')).toBe('doughnut');
    expect(clampStyle('week', 'heatmap')).toBe('heatmap');
    expect(clampStyle('month', 'bars')).toBe('bars'); // a month of bars is valid
    // A style from the other axis (or junk) becomes the period's first style.
    expect(clampStyle('day', 'heatmap')).toBe('doughnut');
    expect(clampStyle('week', 'doughnut')).toBe('bars');
    // [S-56] the retired `list` style clamps to the doughnut, whose legend
    // shows the same rows.
    expect(clampStyle('day', 'list')).toBe('doughnut');
    expect(clampStyle('month', undefined)).toBe('bars');
    expect(clampStyle('day', 42)).toBe('doughnut');
  });

  it('resolveView pairs the two fields, defaulting to day + list', () => {
    expect(resolveView('week', 'heatmap')).toEqual({ period: 'week', style: 'heatmap' });
    expect(resolveView(undefined, undefined)).toEqual({ period: 'day', style: 'doughnut' });
    expect(resolveView('day', 'doughnut')).toEqual({ period: 'day', style: 'doughnut' });
    // An impossible stored pair is repaired, never rendered as an empty body.
    expect(resolveView('month', 'doughnut')).toEqual({ period: 'month', style: 'bars' });
    expect(resolveView('day', 'heatmap')).toEqual({ period: 'day', style: 'doughnut' });
    // `bars` is valid on both axes, so it survives either way.
    expect(resolveView('day', 'bars')).toEqual({ period: 'day', style: 'bars' });
  });

  it('resolveView absorbs the pre-S-46 config where displayStyle held the period', () => {
    expect(resolveView(undefined, 'week')).toEqual({ period: 'week', style: 'bars' });
    expect(resolveView(undefined, 'month')).toEqual({ period: 'month', style: 'heatmap' });
    expect(resolveView(undefined, 'doughnut')).toEqual({ period: 'day', style: 'doughnut' });
    // An explicit period always wins, so once both fields are written the
    // legacy reading can never come back — only the legacy STYLE survives, and
    // then only if the period can draw it ('week' → bars, valid on a day too).
    expect(resolveView('day', 'week')).toEqual({ period: 'day', style: 'bars' });
    expect(resolveView('day', 'month')).toEqual({ period: 'day', style: 'doughnut' }); // heatmap can't
    expect(resolveView('month', 'week')).toEqual({ period: 'month', style: 'bars' });
  });

  it('S-41_ only week/month read a window, of 7 and 30 days', () => {
    expect(isRangePeriod('week')).toBe(true);
    expect(isRangePeriod('month')).toBe(true);
    expect(isRangePeriod('day')).toBe(false);
    expect(rangeLength('week')).toBe(WEEK_DAYS);
    expect(rangeLength('month')).toBe(MONTH_DAYS);
    expect(WEEK_DAYS).toBe(7);
    expect(MONTH_DAYS).toBe(30);
    expect(rangeLength('day')).toBe(1);
  });
});

describe('rolling window parsing and totals [S-41]', () => {
  const range = [
    { date: '2026-07-01', apps: [{ exe: 'code', name: 'Code', secs: 3600 }] },
    { date: '2026-07-02', apps: [] },
    {
      date: '2026-07-03',
      apps: [
        { exe: 'code', name: 'Code', secs: 1800 },
        { exe: 'chrome', name: 'Chrome', secs: 600 },
      ],
    },
  ];

  it('parseRange coerces the IPC response, dropping unusable entries', () => {
    // [S-55] a response without switch counts reads as zero, never NaN.
    expect(parseRange(range)).toEqual(range.map((d) => ({ ...d, switches: 0 })));
    expect(parseRange([{ date: 'd', apps: [], switch_count: 12 }])[0].switches).toBe(12);
    expect(parseRange([{ date: 'd', apps: [], switch_count: -4 }])[0].switches).toBe(0);
    expect(parseRange([{ date: 'd', apps: [], switch_count: 'lots' }])[0].switches).toBe(0);
    expect(totalSwitches(parseRange([
      { date: 'a', apps: [], switch_count: 3 },
      { date: 'b', apps: [], switch_count: 4 },
    ]))).toBe(7);
    expect(totalSwitches([])).toBe(0);
    // A non-array response is an empty window, never a half-built one.
    expect(parseRange(null)).toEqual([]);
    expect(parseRange({ days: range })).toEqual([]);
    expect(parseRange('nope')).toEqual([]);
    // Entries without a usable date key are dropped; junk apps are filtered out
    // but the day itself survives as a zero.
    expect(parseRange([
      { date: '2026-07-01', apps: [{ exe: 'code', name: 'Code', secs: 60 }] },
      { date: '', apps: [] },
      { apps: [] },
      null,
      { date: '2026-07-02', apps: ['junk', { exe: 'x' }, { exe: 'y', name: 'Y', secs: NaN }] },
      { date: '2026-07-03' }, // apps missing entirely
    ])).toEqual([
      { date: '2026-07-01', apps: [{ exe: 'code', name: 'Code', secs: 60 }], switches: 0 },
      { date: '2026-07-02', apps: [], switches: 0 },
      { date: '2026-07-03', apps: [], switches: 0 },
    ]);
  });

  it('dayTotals re-derives each day from the shown apps, keeping zero days', () => {
    expect(dayTotals(range, [])).toEqual([
      { date: '2026-07-01', secs: 3600 },
      { date: '2026-07-02', secs: 0 }, // a gap day stays in the window
      { date: '2026-07-03', secs: 2400 },
    ]);
  });

  it('S-39_ a hidden app shrinks the days it appears in, and the window total', () => {
    const shown = dayTotals(range, ['code']);
    expect(shown.map((d) => d.secs)).toEqual([0, 0, 600]);
    expect(totalSecs(shown)).toBe(600);
    expect(maxDaySecs(shown)).toBe(600);
    // Unfiltered, for contrast.
    expect(totalSecs(dayTotals(range, []))).toBe(6000);
    expect(maxDaySecs(dayTotals(range, []))).toBe(3600);
  });

  it('maxDaySecs and totalSecs handle an empty window and bad seconds', () => {
    expect(maxDaySecs([])).toBe(0);
    expect(totalSecs([])).toBe(0);
    expect(maxDaySecs([{ date: 'd', secs: -5 }])).toBe(0);
    expect(totalSecs([{ date: 'd', secs: -5 }])).toBe(0);
  });
});

describe('stacked bars and the window legend [S-48]', () => {
  const range = [
    {
      date: '2026-07-01',
      apps: [
        { exe: 'code', name: 'Code', secs: 3600 },
        { exe: 'chrome', name: 'Chrome', secs: 1800 },
      ],
    },
    { date: '2026-07-02', apps: [{ exe: 'chrome', name: 'Chrome', secs: 600 }] },
    {
      date: '2026-07-03',
      apps: [
        { exe: 'code', name: 'Code', secs: 1200 },
        { exe: 'spotify', name: 'Spotify', secs: 300 },
      ],
    },
  ];

  it('aggregateApps sums each app across the window, busiest first', () => {
    expect(aggregateApps(range, [])).toEqual([
      { exe: 'code', name: 'Code', secs: 4800, path: undefined },
      { exe: 'chrome', name: 'Chrome', secs: 2400, path: undefined },
      { exe: 'spotify', name: 'Spotify', secs: 300, path: undefined },
    ]);
    // Hidden apps never reach the legend [S-39].
    expect(aggregateApps(range, ['code']).map((a) => a.exe)).toEqual(['chrome', 'spotify']);
    expect(aggregateApps([], [])).toEqual([]);
  });

  it('aggregateApps keeps the first known icon path for a bucket', () => {
    const withPaths = [
      { date: 'd1', apps: [{ exe: 'code', name: 'Code', secs: 10 }] },
      { date: 'd2', apps: [{ exe: 'code', name: 'Code', secs: 20, path: 'C:/code.exe' }] },
    ];
    expect(aggregateApps(withPaths, [])[0]).toEqual(
      { exe: 'code', name: 'Code', secs: 30, path: 'C:/code.exe' },
    );
  });

  it('stackForDay splits a day in LEGEND order, so a colour is one app', () => {
    const legend = donutSegments(aggregateApps(range, []), 7500, 5, 'Other', 'var(--o)');
    const day = range[0].apps;
    const parts = stackForDay(day, legend, 5400); // busiest day = 1h + 30m
    expect(parts.map((p) => p.key)).toEqual(['code', 'chrome']); // legend order, not day order
    expect(parts.map((p) => p.secs)).toEqual([3600, 1800]);
    // Fractions are of the BUSIEST day, so this day's parts sum to a full bar.
    expect(parts.reduce((s, p) => s + p.fraction, 0)).toBeCloseTo(1, 6);
    // A quieter day sums to its share of the busiest one.
    const quiet = stackForDay(range[1].apps, legend, 5400);
    expect(quiet.map((p) => p.key)).toEqual(['chrome']);
    expect(quiet[0].fraction).toBeCloseTo(600 / 5400, 6);
    // Every part carries the app's stable colour, matching the legend row.
    expect(parts[0].color).toBe(appColor('code'));
  });

  it('stackForDay folds apps below the cap into the legend Other part', () => {
    // maxApps 1 → legend is [code, Other]; chrome/spotify land in Other.
    const legend = donutSegments(aggregateApps(range, []), 7500, 1, 'Other', 'var(--o)');
    expect(legend.map((s) => s.key)).toEqual(['code', '__other__']);
    const parts = stackForDay(range[0].apps, legend, 5400);
    expect(parts.map((p) => p.key)).toEqual(['code', '__other__']);
    expect(parts[1].secs).toBe(1800); // chrome, aggregated into Other
    // Still a full bar for the busiest day: nothing is silently dropped.
    expect(parts.reduce((s, p) => s + p.fraction, 0)).toBeCloseTo(1, 6);
  });

  it('stackForDay is empty for an empty day or a zero scale', () => {
    const legend = donutSegments(aggregateApps(range, []), 7500, 5, 'Other', 'var(--o)');
    expect(stackForDay([], legend, 5400)).toEqual([]);
    expect(stackForDay(range[0].apps, legend, 0)).toEqual([]); // no scale to draw against
  });
});

describe('daily average [S-52]', () => {
  it('divides by the WINDOW length, so idle days count as zero', () => {
    expect(dailyAverage(7 * 3600, 7)).toBe(3600); // 7h over a week = 1h/day
    expect(dailyAverage(3600, 7)).toBeCloseTo(3600 / 7, 6); // used once, still /7
    expect(dailyAverage(30 * 60, 30)).toBe(60);
  });

  it('per-app averages sum to the window average', () => {
    const perApp = [4800, 2400, 300];
    const summed = perApp.reduce((s, secs) => s + dailyAverage(secs, 3), 0);
    expect(summed).toBeCloseTo(dailyAverage(7500, 3), 6);
  });

  it('is zero for an empty window or no usage', () => {
    expect(dailyAverage(3600, 0)).toBe(0);
    expect(dailyAverage(0, 7)).toBe(0);
    expect(dailyAverage(-60, 7)).toBe(0);
  });
});

describe('no-chart and no-app-list switches [S-53]', () => {
  it('showsApps: 0 lists nothing, a corrupt value keeps the default', () => {
    expect(showsApps(7)).toBe(true);
    expect(showsApps(1)).toBe(true);
    expect(showsApps(0)).toBe(false);
    expect(showsApps(-2)).toBe(false);
    expect(showsApps(NaN)).toBe(true); // falls back to the default cap
  });

  it('chartCap: the drawing keeps a sensible cap when the list is off', () => {
    expect(chartCap(3)).toBe(3);
    expect(chartCap(15)).toBe(15);
    // 0 would fold every app into one grey "Other" segment.
    expect(chartCap(0)).toBe(DEFAULT_MAX_APPS);
    expect(chartCap(-1)).toBe(DEFAULT_MAX_APPS);
    expect(chartCap(NaN)).toBe(DEFAULT_MAX_APPS);
  });

  it('`none` survives clamping on both axes, and is never the fallback', () => {
    expect(clampStyle('day', 'none')).toBe('none');
    expect(clampStyle('month', 'none')).toBe('none');
    expect(resolveView('week', 'none')).toEqual({ period: 'week', style: 'none' });
    // The fallback stays the period's first REAL style, so a corrupt config
    // shows something rather than an empty body.
    expect(clampStyle('day', 'heatmap')).toBe('doughnut');
    expect(clampStyle('week', 'doughnut')).toBe('bars');
  });
});

describe('share of total [S-58]', () => {
  it('is the app\'s slice of the shown total, clamped to [0, 1]', () => {
    expect(shareOf(3600, 7200)).toBe(0.5);
    expect(shareOf(7200, 7200)).toBe(1);
    expect(shareOf(9000, 7200)).toBe(1); // defensive: never over 100%
  });

  it('is zero when there is nothing to divide by', () => {
    expect(shareOf(3600, 0)).toBe(0);
    expect(shareOf(0, 7200)).toBe(0);
    expect(shareOf(-60, 7200)).toBe(0);
  });
});

describe('goals [S-59]', () => {
  it('parseGoalSecs treats anything but a positive number as no goal', () => {
    expect(parseGoalSecs(3600)).toBe(3600);
    expect(parseGoalSecs(90.7)).toBe(90);
    // A zero-second goal would be exceeded by everything, so it is "no goal".
    expect(parseGoalSecs(0)).toBe(null);
    expect(parseGoalSecs(-60)).toBe(null);
    expect(parseGoalSecs('3600')).toBe(null);
    expect(parseGoalSecs(undefined)).toBe(null);
    expect(parseGoalSecs(NaN)).toBe(null);
    expect(parseGoalSecs(Infinity)).toBe(null);
  });

  it('parseAppGoals drops entries without a usable key or limit', () => {
    expect(parseAppGoals({ chrome: 3600, code: 1800 }))
      .toEqual({ chrome: 3600, code: 1800 });
    expect(parseAppGoals({ chrome: 0, code: -5, '': 60, spotify: 'lots' }))
      .toEqual({});
    expect(parseAppGoals(null)).toEqual({});
    expect(parseAppGoals(['chrome'])).toEqual({});
    expect(parseAppGoals('chrome')).toEqual({});
  });

  it('goalState reports the ratio and the over-limit flag', () => {
    expect(goalState(1800, 3600)).toEqual({ goal: 3600, used: 1800, ratio: 0.5, over: false });
    // Exactly at the limit is not over it.
    expect(goalState(3600, 3600)).toEqual({ goal: 3600, used: 3600, ratio: 1, over: false });
    expect(goalState(5040, 3600)).toEqual({ goal: 3600, used: 5040, ratio: 1.4, over: true });
    // Negative usage (bad data) counts as none.
    expect(goalState(-60, 3600)?.used).toBe(0);
    // No goal set → nothing to render.
    expect(goalState(3600, null)).toBe(null);
  });

  it('a window judges its DAILY AVERAGE against the daily limit', () => {
    // 14h over a 7-day window is 2h/day: under a 3h daily limit, even though
    // the window total is far above it.
    const avg = dailyAverage(14 * 3600, 7);
    expect(goalState(avg, 3 * 3600)?.over).toBe(false);
    expect(goalState(14 * 3600, 3 * 3600)?.over).toBe(true); // the wrong reading
  });
});

describe('metric set migration [S-57]', () => {
  it('drops the keys that became settings, keeping `average` at its default', () => {
    // A legacy set predates `average`, so it can't have switched it off.
    expect(migrateMetrics(['total', 'apps'])).toEqual(['average']);
    expect(migrateMetrics(['graphic', 'apps'])).toEqual(['average']);
    expect(migrateMetrics(['apps'])).toEqual(['average']);
    // A set written after the change is left exactly as it is.
    expect(migrateMetrics(['average'])).toEqual(['average']);
    expect(migrateMetrics(['switches'])).toEqual(['switches']);
    expect(migrateMetrics([])).toEqual([]); // everything off, deliberately
    // Non-arrays pass through untouched, so resolveMetrics still sees its
    // object/absent forms.
    expect(migrateMetrics(undefined)).toBe(undefined);
    expect(migrateMetrics({ total: true })).toEqual({ total: true });
  });

  it('carries a legacy set\'s two switches onto the settings that replaced them', () => {
    // Both on: the settings are untouched.
    expect(applyLegacyMetrics(['total', 'apps'], 'doughnut', 7))
      .toEqual({ style: 'doughnut', maxApps: 7 });
    // Drawing was off → no chart; list was off → no apps.
    expect(applyLegacyMetrics(['apps'], 'doughnut', 7))
      .toEqual({ style: 'none', maxApps: 7 });
    expect(applyLegacyMetrics(['total'], 'bars', 7))
      .toEqual({ style: 'bars', maxApps: 0 });
    expect(applyLegacyMetrics(['graphic'], 'bars', 7))
      .toEqual({ style: 'bars', maxApps: 0 });
    // A post-change set (or no set at all) says nothing about either setting.
    expect(applyLegacyMetrics(['average', 'switches'], 'heatmap', 5))
      .toEqual({ style: 'heatmap', maxApps: 5 });
    expect(applyLegacyMetrics(undefined, 'heatmap', 5))
      .toEqual({ style: 'heatmap', maxApps: 5 });
    expect(applyLegacyMetrics([], 'heatmap', 5))
      .toEqual({ style: 'heatmap', maxApps: 5 });
  });
});

describe('filler rows [S-56]', () => {
  it('pads the list up to the configured app count, never past it', () => {
    expect(ghostRows(5, 10)).toBe(5);
    expect(ghostRows(0, 7)).toBe(7);
    expect(ghostRows(7, 7)).toBe(0);
    // The "Other" entry can push the list one row past the cap: no filler then.
    expect(ghostRows(8, 7)).toBe(0);
    expect(ghostRows(3, 0)).toBe(0); // list switched off entirely
    expect(ghostRows(3, NaN)).toBe(0); // corrupt config adds no phantom rows
  });

  it('MAX_APPS_LIMIT is the stepper ceiling', () => {
    expect(MAX_APPS_LIMIT).toBe(50);
  });
});

describe('heatmap [S-43]', () => {
  it('heatLevel: 0 only for no usage, 4 for the busiest day', () => {
    expect(heatLevel(0, 100)).toBe(0);
    expect(heatLevel(-5, 100)).toBe(0);
    expect(heatLevel(100, 100)).toBe(4);
    // Any usage at all is at least level 1, however small the share.
    expect(heatLevel(1, 100_000)).toBe(1);
    expect(heatLevel(25, 100)).toBe(1);
    expect(heatLevel(26, 100)).toBe(2);
    expect(heatLevel(50, 100)).toBe(2);
    expect(heatLevel(75, 100)).toBe(3);
    expect(heatLevel(76, 100)).toBe(4);
    // A zero scale (empty window) can't rank anything.
    expect(heatLevel(10, 0)).toBe(0);
    // Defensive: a day above the scale still clamps to the top bucket.
    expect(heatLevel(200, 100)).toBe(4);
  });

  it('pads leading blanks so the window ENDS on the last cell', () => {
    const totals = Array.from({ length: MONTH_DAYS }, (_, i) => ({
      date: `2026-07-${String(i + 1).padStart(2, '0')}`,
      secs: 60,
    }));
    const cells = heatmapCells(totals);
    expect(cells.length).toBe(35); // 5 full rows of 7
    expect(cells.length % HEATMAP_COLS).toBe(0);
    expect(cells.slice(0, 5).every((c) => c === null)).toBe(true);
    expect(cells[5]).toEqual(totals[0]);
    expect(cells[cells.length - 1]).toEqual(totals[MONTH_DAYS - 1]); // today, last
    // A window that already fills whole rows gets no padding.
    expect(heatmapCells(totals.slice(0, 14)).length).toBe(14);
    expect(heatmapCells([]).length).toBe(0);
  });

  it('every column holds one fixed weekday, sampled for the header row', () => {
    const totals = Array.from({ length: MONTH_DAYS }, (_, i) => ({
      date: `2026-07-${String(i + 1).padStart(2, '0')}`,
      secs: 60,
    }));
    const cells = heatmapCells(totals);
    // Cells 7 apart are 7 days apart, so a column is a weekday by construction.
    for (let i = HEATMAP_COLS; i < cells.length; i++) {
      const prev = cells[i - HEATMAP_COLS];
      const cur = cells[i];
      if (!prev || !cur) continue;
      expect(shiftDateKey(prev.date, 7)).toBe(cur.date);
    }
    const cols = heatmapColumnDates(cells);
    expect(cols.length).toBe(HEATMAP_COLS);
    // The padded columns sample the first day that lands in them (row 2).
    expect(cols[0]).toBe('2026-07-03');
    expect(cols[5]).toBe('2026-07-01');
    expect(cols.every((c) => c !== null)).toBe(true);
    expect(heatmapColumnDates([])).toEqual(new Array(HEATMAP_COLS).fill(null));
  });
});

describe('barFraction [S-05]', () => {
  it('is the share of the LARGEST app, so the top app fills the bar', () => {
    expect(barFraction(9000, 9000)).toBe(1);
    expect(barFraction(4500, 9000)).toBe(0.5);
  });

  it('never divides by zero or exceeds 1', () => {
    expect(barFraction(0, 0)).toBe(0);
    expect(barFraction(50, 0)).toBe(0);
    expect(barFraction(200, 100)).toBe(1);
  });
});
