// Demo data fixtures used when globalDesign.demoMode is on.
// Keep entries small but realistic; they're shown in screenshots and onboarding.

import type { LayoutNode } from '../../widgets/others/shortcut/layout.ts';
import { DEMO_VIDEO_FRAME_JPEG_BASE64 } from './videoFrameImage.ts';

interface DemoLayout { id: string; name: string; root: LayoutNode }

const now = () => Date.now();
const iso = (msAgo: number) => new Date(now() - msAgo).toISOString();

// ── System ────────────────────────────────────────────────────────────────
/** Percentage jitter: clamped to 0..100. NOT for byte rates — it would flatten
 *  any value above 100 to exactly 100. Use `wobble` for those. */
function jitter(base: number, amp: number): number {
  return Math.max(0, Math.min(100, base + (Math.random() - 0.5) * amp));
}

/** Unbounded jitter for magnitudes (bytes/sec, RPM, …). Floors at zero only. */
function wobble(base: number, amp: number): number {
  return Math.max(0, base + (Math.random() - 0.5) * amp);
}

/** Baseline I/O for the demo C: drive. Shared by the live stats fixture and the
 *  seeded sparkline history so the two are on the same scale — they drifted
 *  apart once and the graph showed a step where the seed ended. */
const DEMO_DISK_C = { read: 1_800_000, write: 600_000 };

export function demoSystemStats() {
  const t = Date.now() / 4000;
  return {
    // CPU
    cpu_usage: jitter(28 + Math.sin(t) * 12, 6),
    cpu_cores: Array.from({ length: 8 }, (_, i) => jitter(25 + Math.sin(t + i) * 18, 10)),
    cpu_freq_mhz: 3800,
    cpu_temp_c: 52 + Math.random() * 6,
    cpu_fan_rpm: 1180 + Math.round(Math.random() * 120),
    cpu_processes_total: 312,
    cpu_threads_total: 4120,
    cpu_top: [
      { name: 'chrome.exe',       pid: 1234, cpu_pct: 18.4, desc: 'Google Chrome' },
      { name: 'code.exe',         pid: 5678, cpu_pct: 9.1,  desc: 'Visual Studio Code' },
      { name: 'discord.exe',      pid: 9012, cpu_pct: 4.7,  desc: 'Discord' },
      { name: 'explorer.exe',     pid: 3456, cpu_pct: 2.9,  desc: 'Windows Explorer' },
      { name: 'MsMpEng.exe',      pid: 7890, cpu_pct: 2.1,  desc: 'Antimalware Service Executable' },
      { name: 'spotify.exe',      pid: 2345, cpu_pct: 1.4,  desc: 'Spotify' },
    ],
    cpu_uptime_s: 86400 * 2 + 3600 * 4,
    // RAM
    ram_used: 11_500_000_000,
    ram_total: 32_000_000_000,
    ram_available: 19_800_000_000,
    swap_used: 1_400_000_000,
    swap_total: 8_000_000_000,
    ram_top: [
      { name: 'chrome.exe',       pid: 1234, mem_bytes: 1_800_000_000 },
      { name: 'code.exe',         pid: 5678, mem_bytes: 980_000_000 },
      { name: 'discord.exe',      pid: 9012, mem_bytes: 420_000_000 },
    ],
    // Disk
    disk_read_per_sec: 2_400_000 + Math.random() * 600_000,
    disk_write_per_sec: 800_000 + Math.random() * 400_000,
    disk_read_ops_per_sec: 80 + Math.round(Math.random() * 40),
    disk_write_ops_per_sec: 30 + Math.round(Math.random() * 20),
    // Per-disk rates jitter like every other demo metric. They used to be
    // CONSTANTS, so the Disk widget's sparkline — which plots the SELECTED
    // disk's rate, not the system-wide one ([S-01]) — flatlined into a straight
    // line the moment the seeded history scrolled past.
    disks: [
      {
        mount: 'C:', used: 412_000_000_000, total: 1_000_000_000_000,
        read_per_sec:  wobble(DEMO_DISK_C.read,  DEMO_DISK_C.read  * 0.35),
        write_per_sec: wobble(DEMO_DISK_C.write, DEMO_DISK_C.write * 0.4),
      },
      {
        mount: 'D:', used: 1_240_000_000_000, total: 2_000_000_000_000,
        read_per_sec:  wobble(600_000, 220_000),
        write_per_sec: wobble(200_000, 90_000),
      },
    ],
    // Net
    net_rx_per_sec: 320_000 + Math.random() * 400_000,
    net_tx_per_sec: 80_000  + Math.random() * 120_000,
    net_interfaces: [
      { name: 'Wi-Fi',     rx_per_sec: 280_000, tx_per_sec: 70_000, is_up: true },
      { name: 'Ethernet',  rx_per_sec:  40_000, tx_per_sec: 10_000, is_up: true },
    ],
    net_session_rx: 3_400_000_000,
    net_session_tx:   840_000_000,
    public_ip: '203.0.113.42',
  };
}

export function demoBatteryStats() {
  return {
    percent: 78,
    is_charging: false,
    is_on_ac: false,
    seconds_remaining: 4 * 3600 + 12 * 60,
    has_battery: true,
    design_capacity_mwh: 50_000,
    full_charge_capacity_mwh: 46_400,
    health_pct: 92.8,
    cycle_count: 184,
    voltage_mv: 11_840,
    temp_c: 31.4,
  };
}

const DEMO_SCREENTIME_APPS = [
  { exe: 'code',    name: 'Visual Studio Code', secs: 2 * 3600 + 41 * 60, path: 'C:/demo/code.exe' },
  { exe: 'chrome',  name: 'Google Chrome',      secs: 1 * 3600 + 52 * 60, path: 'C:/demo/chrome.exe' },
  { exe: 'discord', name: 'Discord',            secs: 34 * 60,            path: 'C:/demo/discord.exe' },
  { exe: 'spotify', name: 'Spotify',            secs: 21 * 60,            path: 'C:/demo/spotify.exe' },
  { exe: 'explorer', name: 'Windows Explorer',  secs: 9 * 60,             path: 'C:/demo/explorer.exe' },
];

function demoDateKey(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export function demoScreentime() {
  const apps = DEMO_SCREENTIME_APPS;
  return {
    date: demoDateKey(new Date()),
    total_secs: apps.reduce((s, a) => s + a.secs, 0),
    apps,
    switch_count: 87, // [S-55]
  };
}

/// [S-41] rolling-window fixture: `days` days ending today, oldest first. The
/// per-day scaling is deterministic (weekday rhythm + a fixed wobble), so the
/// week bars and month heatmap show real shape without a random walk that would
/// change on every render.
export function demoScreentimeRange(days: number) {
  const n = Math.max(1, Math.min(90, Math.floor(days) || 1));
  const today = new Date();
  const out: {
    date: string;
    total_secs: number;
    apps: typeof DEMO_SCREENTIME_APPS;
    switch_count: number;
  }[] = [];
  for (let back = n - 1; back >= 0; back--) {
    const d = new Date(today.getFullYear(), today.getMonth(), today.getDate() - back);
    const dow = d.getDay();
    const weekend = dow === 0 || dow === 6;
    const factor = weekend ? 0.2 + ((back * 7) % 5) / 25 : 0.65 + ((back * 37) % 11) / 20;
    const apps = DEMO_SCREENTIME_APPS.map((a) => ({ ...a, secs: Math.round(a.secs * factor) }));
    out.push({
      date: demoDateKey(d),
      total_secs: apps.reduce((s, a) => s + a.secs, 0),
      apps,
      switch_count: Math.round(90 * factor), // [S-55] busier days switch more
    });
  }
  return out;
}

// Demo app icons: a colored rounded-square SVG per requested path, so the Screen
// Time widget (and the add-widget preview) shows icons without a Tauri backend.
export function demoFileIconsBatch(args: { paths?: string[] }): Record<string, string> {
  const palette = ['#60cdff', '#8b5cf6', '#f472b6', '#34d399', '#fbbf24', '#f87171'];
  const out: Record<string, string> = {};
  for (const p of args.paths ?? []) {
    let h = 0;
    for (let i = 0; i < p.length; i++) h = (h * 31 + p.charCodeAt(i)) | 0;
    const color = palette[Math.abs(h) % palette.length];
    // Carry the file's initial. A bare coloured square reads as a failed icon
    // load rather than as an app, which made the Apps and Screen Time previews
    // look broken; the widget's own initials fallback never fires because this
    // fixture DOES return an icon.
    const base = (p.split(/[\\/]/).pop() ?? '').replace(/\.[^.]+$/, '');
    const initial = (base.trim()[0] ?? '?').toUpperCase();
    const svg =
      `<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32">` +
      `<rect width="32" height="32" rx="7" fill="${color}"/>` +
      `<text x="16" y="17" fill="#ffffff" fill-opacity="0.92" font-family="Segoe UI, Arial, sans-serif" ` +
      `font-size="17" font-weight="600" text-anchor="middle" dominant-baseline="central">${initial}</text>` +
      `</svg>`;
    out[p] = `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
  }
  return out;
}

export function demoGpuStats(_args: { gpuIndex?: number }) {
  const t = Date.now() / 3500;
  return {
    name: 'NVIDIA GeForce RTX 4070',
    utilization: jitter(34 + Math.sin(t) * 14, 8),
    vram_used_mb: 4820,
    vram_total_mb: 12288,
    shared_used_mb: 240,
    shared_total_mb: 16384,
    temp_c: 58 + Math.random() * 4,
    power_w: 124 + Math.random() * 18,
    fan_pct: 42,
    encoder_pct: 0,
    decoder_pct: 0,
    core_clock_mhz: 2310,
    mem_clock_mhz: 10501,
  };
}

export function demoListGpus() {
  return [
    { index: 0, name: 'NVIDIA GeForce RTX 4070' },
  ];
}

export function demoBluetoothDevices() {
  return [
    { name: 'Sony WH-1000XM5', id: 'demo-1', is_connected: true,  device_type: 'audio',    battery_pct: 78, rssi: -52 },
    { name: 'MX Master 3S',    id: 'demo-2', is_connected: true,  device_type: 'mouse',    battery_pct: 64, rssi: -48 },
    { name: 'Pixel 9',         id: 'demo-3', is_connected: false, device_type: 'phone',    battery_pct: null, rssi: null },
    { name: 'Keychron K2',     id: 'demo-4', is_connected: false, device_type: 'keyboard', battery_pct: null, rssi: null },
  ];
}

// Fixed, mid-range level: the slider reads clearly at this position and the
// mute state is off, so the widget shows its normal (not muted) appearance.
// Without this the Volume widget was the one system widget still reaching the
// real backend in demo/preview mode, so previews and docs screenshots showed
// the machine's ACTUAL output level.
export function demoVolumeState() {
  // `volume_get` returns a 0..1 fraction (volume SPEC [S-01]); the widget
  // renders `level * 100`. A whole-number 42 here read as "4200%".
  return { level: 0.42, muted: false };
}

// A believable per-app mixer: a browser standing in for the grouped case the
// widget exists for (three tabs, one slider), a music app, a chat app, and the
// system-sounds row. Same reasoning as demoVolumeState — without a fixture the
// picker preview and docs screenshots would expose whatever the reader's own
// machine happens to be playing, and show nothing at all on a quiet desktop.
export function demoMixerSessions(args: { grouped?: boolean }) {
  const apps = [
    { key: 'c:\\program files\\chrome\\chrome.exe', name: 'Google Chrome', level: 0.75, sessions: 3 },
    { key: 'c:\\users\\demo\\spotify\\spotify.exe', name: 'Spotify', level: 0.55, sessions: 1 },
    { key: 'c:\\users\\demo\\discord\\discord.exe', name: 'Discord', level: 0.3, sessions: 1 },
  ];
  const rows = apps.flatMap((a, i) => {
    // Ungrouped shows every session of an app separately (mixer SPEC [S-03]).
    const n = args.grouped === false ? a.sessions : 1;
    return Array.from({ length: n }, (_, s) => ({
      key: n > 1 ? `${a.key}|${s}` : a.key,
      name: a.name,
      path: a.key,
      level: a.level,
      muted: i === 2,
      active: i === 0 || i === 1,
      is_system: false,
      sessions: args.grouped === false ? 1 : a.sessions,
    }));
  });
  return [
    ...rows,
    {
      key: 'system-sounds',
      name: '',
      path: null,
      level: 1,
      muted: false,
      active: false,
      is_system: true,
      sessions: 1,
    },
  ];
}

export function demoPingHosts(args: { hosts?: { label: string; address: string }[] }) {
  return (args.hosts ?? []).map(() => ({
    ok: true,
    latency_ms: Math.round(12 + Math.random() * 18),
  }));
}

// ── Pre-seeded histories ──────────────────────────────────────────────────
// System graph widgets build their spark history one polled sample at a time,
// so a freshly-mounted widget shows an empty/flat graph for the first few
// seconds. In demo + picker-preview mode that's the wrong impression, so we
// want the graph populated instantly. Each helper returns N synthetic past
// samples using the same shape as the snapshot fixture above, so subsequent
// real polls in demo mode flow seamlessly onto the seeded curve.

export function demoCpuHistory(n: number): number[] {
  const out: number[] = [];
  const t0 = Date.now();
  for (let k = 0; k < n; k++) {
    const t = (t0 - (n - 1 - k) * 1000) / 4000;
    out.push(jitter(28 + Math.sin(t) * 12, 6));
  }
  return out;
}

export function demoCpuCoresHistory(n: number, cores: number = 8): number[][] {
  const out: number[][] = Array.from({ length: cores }, () => []);
  const t0 = Date.now();
  for (let k = 0; k < n; k++) {
    const t = (t0 - (n - 1 - k) * 1000) / 4000;
    for (let i = 0; i < cores; i++) out[i].push(jitter(25 + Math.sin(t + i) * 18, 10));
  }
  return out;
}

export function demoRamPctHistory(n: number): number[] {
  // Snapshot fixture holds ram_used/ram_total constant at ~35.94%. A little
  // jitter keeps the spark visibly alive without changing the headline value.
  return Array.from({ length: n }, () => 35.9 + (Math.random() - 0.5) * 1.5);
}

export function demoDiskHistory(n: number): { read: number[]; write: number[] } {
  // Seeded from the SAME baseline the live samples use (the selected disk's
  // per-mount rate, `DEMO_DISK_C`). Seeding from the system-wide figure instead
  // put the history a third higher than everything appended after it, so the
  // sparkline showed a cliff at the point the seed ran out.
  return {
    read:  Array.from({ length: n }, () => wobble(DEMO_DISK_C.read,  DEMO_DISK_C.read  * 0.35)),
    write: Array.from({ length: n }, () => wobble(DEMO_DISK_C.write, DEMO_DISK_C.write * 0.4)),
  };
}

export function demoNetHistory(n: number): { rx: number[]; tx: number[] } {
  return {
    rx: Array.from({ length: n }, () => 320_000 + Math.random() * 400_000),
    tx: Array.from({ length: n }, () => 80_000  + Math.random() * 120_000),
  };
}

export function demoGpuUtilHistory(n: number): number[] {
  const out: number[] = [];
  const t0 = Date.now();
  for (let k = 0; k < n; k++) {
    const t = (t0 - (n - 1 - k) * 1000) / 3500;
    out.push(jitter(34 + Math.sin(t) * 14, 8));
  }
  return out;
}

export function demoBatteryHistory(n: number): number[] {
  // Slow discharge curve approaching 78% with tiny jitter for motion.
  return Array.from({ length: n }, (_, k) =>
    78 + (n - 1 - k) * 0.04 + (Math.random() - 0.5) * 0.15
  );
}

export function demoPingLatencyHistory(n: number): number[] {
  return Array.from({ length: n }, () => Math.round(12 + Math.random() * 18));
}

// ── Calculator ─────────────────────────────────────────────────────────────
// Calculator history lives in an in-memory signal, not config, so a freshly
// mounted widget (and the picker preview) shows an empty list. In demo +
// picker-preview mode that reads as "broken/blank"; seed a few realistic past
// calculations so the preview shows what the widget is for. Newest-first to
// match the live unshift order, capped well under the 8-entry limit. [S-12]
export function demoCalcHistory(): { expr: string; result: string }[] {
  return [
    { expr: '1280 * 0.15', result: '192' },
    { expr: '(450 + 90) / 3', result: '180' },
    { expr: '2 ^ 10', result: '1024' },
    { expr: '99 * 99', result: '9801' },
    { expr: '12.5% * 5000', result: '625' },
  ];
}

// Clipboard history entries. The widget builds its list from live
// `clipboard://update` events, which never fire in preview/demo, so it seeds
// this fixture instead. A believable mix of a link, a command, prose, a colour,
// an email, a path, a note, and a phone number.
export function demoClipboardItems(): string[] {
  return [
    'https://themia.app/download',
    'npm run tauri build',
    'Let’s move the sync review to Thursday at 2pm.',
    '#60cdff',
    // Reserved example domain (RFC 2606), never a real address. This used to be
    // the maintainer's actual personal email, which shipped into the public
    // documentation screenshots and the Add-Widget picker — a live address on a
    // public page is free food for address harvesters.
    'anna.schmidt@example.com',
    'C:\\Users\\Me\\Documents\\Q3-report.pdf',
    'The quick brown fox jumps over the lazy dog',
    '+1 (555) 013-4477',
  ];
}

// Recent local calendar-day keys (YYYY-MM-DD), today first going back `n` days.
// Matches habit.ts `dateKey` so seeded habit logs line up with today's period
// and produce live streaks in the preview.
function recentDayKeys(n: number): string[] {
  const keys: string[] = [];
  const t = new Date();
  for (let i = 0; i < n; i++) {
    const x = new Date(t.getFullYear(), t.getMonth(), t.getDate() - i);
    const y = x.getFullYear();
    const m = String(x.getMonth() + 1).padStart(2, '0');
    const d = String(x.getDate()).padStart(2, '0');
    keys.push(`${y}-${m}-${d}`);
  }
  return keys;
}

// ── Preview config overrides ──────────────────────────────────────────────
// Per-widget config overrides applied on top of `defaultConfig` when a widget
// is rendered inside the Add-Widget picker preview (or anywhere isDemo() is
// true). Keyed by the widget's registry type. Lets the preview show meaningful
// content for widgets whose data lives in their config (notes text, shortcut
// items, countdown timers) rather than a backend fetch.
const inDays = (n: number): string =>
  new Date(Date.now() + n * 24 * 60 * 60 * 1000).toISOString();

/** ISO timestamp `n` days back, anchored to mid-afternoon so a seeded entry
 *  always lands inside its intended local day rather than sliding across
 *  midnight for viewers at a large UTC offset. */
const daysAgoIso = (n: number): string => {
  const t = new Date();
  const d = new Date(t.getFullYear(), t.getMonth(), t.getDate() - n, 15, 0, 0);
  return d.toISOString();
};

export const demoWidgetConfigs: Record<string, Record<string, unknown>> = {
  notes: {
    content:
      '## Project ideas\n\n- Ship the new widget picker\n- Polish the demo data flow\n- Sketch the onboarding flow\n- Add a screenshot mode',
    fontSize: 12,
  },
  // Widgets below gate their whole body on a configured target, so without a
  // seed the picker preview shows "select a …" instead of the widget. A person
  // choosing from the picker is deciding whether they want the widget at all;
  // an empty-state card is the one thing that cannot help them decide.
  ping: {
    host: { label: 'Cloudflare DNS', address: '1.1.1.1' },
  },
  gallery: {
    source: 'folder',
    // Any non-empty path: `scan_images` is fixtured in demo mode, so the value
    // only has to clear the "no folder configured" gate.
    path: 'C:\\Users\\Demo\\Pictures',
    // Mosaic rather than the uniform grid: the varied tile sizes show what the
    // widget can do with a folder of photos, which a wall of equal squares does
    // not. Matches the eight bundled images in `demoGalleryImages`.
    mode: 'mosaic',
    maxImages: 8,
  },
  moodTracker: {
    storageMode: 'local',
    showNote: true,
    showStats: true,
    entries: [
      { id: 'demo-m1', at: daysAgoIso(0), level: 4, note: 'Shipped the picker rewrite' },
      { id: 'demo-m2', at: daysAgoIso(1), level: 5, note: 'Long walk, good sleep' },
      { id: 'demo-m3', at: daysAgoIso(2), level: 3, note: '' },
      { id: 'demo-m4', at: daysAgoIso(3), level: 4, note: 'Back on track' },
      { id: 'demo-m5', at: daysAgoIso(4), level: 2, note: 'Rough deploy day' },
      { id: 'demo-m6', at: daysAgoIso(6), level: 3, note: '' },
      { id: 'demo-m7', at: daysAgoIso(7), level: 4, note: '' },
      { id: 'demo-m8', at: daysAgoIso(9), level: 5, note: 'Release went out clean' },
      { id: 'demo-m9', at: daysAgoIso(11), level: 3, note: '' },
    ],
  },
  countdown: {
    timers: [
      { id: 'demo-vac', label: 'Vacation', target: inDays(21), format: 'dhm' },
      { id: 'demo-rel', label: 'Release',  target: inDays(5),  format: 'dhms' },
    ],
  },
  shortcut: {
    items: [
      { id: 'demo-gh',  path: 'https://github.com',  label: 'GitHub' },
      { id: 'demo-ln',  path: 'https://linear.app',  label: 'Linear' },
      { id: 'demo-fg',  path: 'https://figma.com',   label: 'Figma' },
      { id: 'demo-nt',  path: 'https://notion.so',   label: 'Notion' },
    ],
    viewMode: 'grid',
    gridItemSize: 'medium',
  },
  // Folder mirrors shortcut in the preview: grid view, medium icons. (Demo
  // entries are supplied by FolderView's isDemo() branch, no path needed.)
  folder: {
    viewMode: 'grid',
    gridItemSize: 'medium',
  },
  // Habit tracker reads habits + log from config. Seed four habits with daily
  // logs anchored to today so the preview shows checkmarks and live streaks
  // (one habit left unchecked today to show the actionable checkbox state).
  habitTracker: {
    resetPeriod: 'daily',
    habits: [
      { id: 'demo-water',    name: 'Drink water' },
      { id: 'demo-read',     name: 'Read 20 minutes' },
      { id: 'demo-exercise', name: 'Exercise' },
      { id: 'demo-meditate', name: 'Meditate' },
    ],
    log: {
      'demo-water':    recentDayKeys(12),          // long streak, done today
      'demo-read':     recentDayKeys(4),           // streak 4, done today
      'demo-exercise': recentDayKeys(6).slice(1),  // streak 5, NOT done today
      'demo-meditate': recentDayKeys(2),           // streak 2, done today
    },
  },
  // Containers read their children from the layout store, and a preview widget
  // has no id there -- so without a composition they render their (invisible)
  // empty state and the preview pane sits blank. `_previewChildren` names the
  // widget types to compose; the ids are `preview-<n>` (containerPreview.ts),
  // which is what the collapsed / active entries below refer to.
  stack: {
    _previewChildren: ['clock', 'notes', 'weatherCurrent'],
    // Two title rows, one open: the accordion's point, in one glance.
    expandedId: 'preview-1',
  },
  tabs: {
    _previewChildren: ['clock', 'notes', 'weatherCurrent'],
    activeChildId: 'preview-1',
  },
  // The area group holds its widgets on the DESKTOP grid, which the preview
  // pane has no grid for, so it can't compose live children like stack/tabs.
  // The count alone drives its stand-in blocks (group [S-06]).
  group: {
    _previewChildren: ['clock', 'notes', 'weatherCurrent'],
  },
};

// (Preview render-span overrides moved to src/demo/previewAspects.ts; they're
// now tunable in a dev build via the picker's inline aspect panel.)

// ── Email / Calendar / Contacts / Todo ────────────────────────────────────
const DEMO_ACCOUNT_ID = 'demo-account';
const DEMO_ACCOUNT = {
  id: DEMO_ACCOUNT_ID,
  email: 'demo@themia.app',
  nickname: 'Demo',
  display_name: 'Demo Account',
  provider: 'demo',
};

export function demoEmailMessages(args: { folder?: string; count?: number }) {
  const folder = args.folder ?? 'inbox';
  const senders = [
    ['Anna Schmidt',   'anna.schmidt@example.com'],
    ['GitHub',         'noreply@github.com'],
    ['Stripe',         'billing@stripe.com'],
    ['Linear',         'notifications@linear.app'],
    ['Liam Walker',    'liam@studio.io'],
    ['Vercel',         'no-reply@vercel.com'],
    ['Sofia Müller',   'sofia.m@designhub.de'],
    ['Anthropic',      'team@anthropic.com'],
    ['Notion',         'team@notion.so'],
    ['Spotify',        'no-reply@spotify.com'],
  ];
  const subjects = [
    'Re: Q2 product roadmap review',
    'Your weekly digest is ready',
    'Invoice #4821: receipt',
    '3 issues assigned to you',
    'Lunch tomorrow?',
    'Build successful: themia@v0.4.2',
    'Mockups for the new dashboard',
    'API usage summary: March',
    'Welcome to your new workspace',
    'Your Discover Weekly is here',
  ];
  const previews = [
    'Hey, sharing the updated deck with the changes we discussed on Friday…',
    'A new version is available with the features you starred last week.',
    'Thanks for your business. Your invoice for the period is attached.',
    'You have 3 new tasks in the Engineering project. Click to triage.',
    "I'm in the area, want to grab something near the office?",
    'Deployment to production succeeded in 1m 23s. View build logs.',
    "Here's the first pass on the components. Let me know what you think.",
    "You've used 62% of your monthly quota. No action needed.",
    'Get started by creating your first page or inviting a teammate.',
    'Fresh playlist based on what you listened to this week.',
  ];
  const n = Math.min(args.count ?? 10, senders.length);
  return Array.from({ length: n }, (_, i) => ({
    id: `demo-msg-${i}`,
    subject: subjects[i],
    from_name: senders[i][0],
    from_email: senders[i][1],
    received_at: iso((i * 47 + 12) * 60_000),
    is_read: i > 2,
    preview: previews[i],
    web_link: '',
    account_id: DEMO_ACCOUNT_ID,
    account_email: DEMO_ACCOUNT.email,
    account_nickname: DEMO_ACCOUNT.nickname,
    folder,
  }));
}

export function demoFolderStatus() {
  return { unread: 3, total: 10 };
}

// Demo calendars for the account [S-48]. The primary is the default; the other
// two carry distinct hues so the per-calendar color path [S-51] is visible.
export const DEMO_CALENDARS = [
  { id: 'demo-cal-primary',  name: 'Calendar', is_default: true,  color: null as string | null },
  { id: 'demo-cal-work',     name: 'Work',     is_default: false, color: '#ff9660' },
  { id: 'demo-cal-personal', name: 'Personal', is_default: false, color: '#a8e6a3' },
] as const;

export function demoListCalendars() {
  return DEMO_CALENDARS.map(c => ({ ...c }));
}

export function demoCalendarEvents(args: { startDt?: string; endDt?: string; calendarId?: string }) {
  const today = new Date(); today.setHours(0, 0, 0, 0);
  const at = (dayOffset: number, hour: number, minute = 0): string => {
    const d = new Date(today); d.setDate(d.getDate() + dayOffset); d.setHours(hour, minute, 0, 0);
    return d.toISOString();
  };
  const events = [
    { title: 'Team standup',           day: 0, start: 9,  end: 9.5,  location: 'Zoom',          cal: 'demo-cal-primary' },
    { title: 'Design review',          day: 0, start: 11, end: 12,   location: 'Studio room',   cal: 'demo-cal-primary' },
    { title: 'Lunch with Liam',        day: 0, start: 13, end: 14,   location: 'Café Flora',    cal: 'demo-cal-personal' },
    { title: '1:1 with Anna',          day: 1, start: 10, end: 10.5, location: 'Zoom',          cal: 'demo-cal-primary' },
    { title: 'Sprint planning',        day: 1, start: 14, end: 15.5, location: 'HQ, Room 4',    cal: 'demo-cal-work' },
    { title: 'Customer call: Acme',    day: 2, start: 11, end: 12,   location: 'Google Meet',   cal: 'demo-cal-work' },
    { title: 'Yoga',                   day: 2, start: 18, end: 19,   location: 'Studio Zen',    cal: 'demo-cal-personal' },
    { title: 'Q2 OKRs review',         day: 3, start: 15, end: 16.5, location: 'HQ, Room 1',    cal: 'demo-cal-work' },
    { title: 'Birthday party: Sofia',  day: 5, start: 19, end: 22,   location: "Sofia's place", cal: 'demo-cal-personal' },
  ];
  // A specific calendar id returns only its events [S-50]; absent → the default
  // (primary) calendar, mirroring the real backend's calendarId-less path [S-49].
  const wanted = args.calendarId ?? 'demo-cal-primary';
  const scoped = events.filter(e => e.cal === wanted);
  return scoped.map((e, i) => ({
    id: `demo-ev-${e.cal}-${i}`,
    start_dt: at(e.day, Math.floor(e.start), (e.start % 1) * 60),
    end_dt:   at(e.day, Math.floor(e.end),   (e.end   % 1) * 60),
    subject: e.title,
    location: e.location,
    organizer: DEMO_ACCOUNT.email,
    is_all_day: false,
    web_link: '',
  }));
}

export function demoBirthdays() {
  return [
    { display_name: 'Sofia Müller', month: new Date().getMonth() + 1, day: Math.min(28, new Date().getDate() + 5) },
    { display_name: 'Liam Walker',  month: ((new Date().getMonth() + 1) % 12) + 1, day: 12 },
  ];
}

export function demoContacts() {
  const ppl = [
    ['Anna Schmidt',   'anna.schmidt@example.com', '+49 30 12345678'],
    ['Liam Walker',    'liam@studio.io',           '+1 415 555 0142'],
    ['Sofia Müller',   'sofia.m@designhub.de',     '+49 89 87654321'],
    ['Marco Rossi',    'marco@acme.com',           '+39 02 11122334'],
    ['Yuki Tanaka',    'yuki.t@example.jp',        '+81 3 1234 5678'],
    ['Priya Singh',    'priya@labs.dev',           '+44 20 7946 0023'],
    ['Noah Bennett',   'noah@hub.io',              '+1 212 555 0199'],
    ['Elena García',   'elena@arte.es',            '+34 91 555 1010'],
  ];
  const companies = ['Acme', 'Studio.io', 'DesignHub', 'Acme', 'Sora Labs', 'BrightLabs', 'Hub Co', 'Arte SL'];
  const titles    = ['Product Manager', 'Designer', 'UX Lead', 'Engineer', 'CTO', 'Researcher', 'Founder', 'Illustrator'];
  return ppl.map(([name, email, phone], i) => ({
    id: `demo-contact-${i}`,
    display_name: name,
    email,
    phone,
    company: companies[i],
    job_title: titles[i],
  }));
}

export function demoTodoTasks() {
  const t = (id: string, title: string, status: string, importance: string, due: string | null, completedAt: string | null) =>
    ({ id, title, status, importance, due_date: due, completed_at: completedAt, list_id: 'demo-list' });
  return [
    t('demo-task-1', 'Polish onboarding screen',         'notStarted', 'high',   null,                       null),
    t('demo-task-2', 'Reply to Anna about the proposal', 'notStarted', 'normal', iso(2 * 86_400_000),        null),
    t('demo-task-3', 'Ship v0.4.2',                      'notStarted', 'high',   iso(1 * 86_400_000),        null),
    t('demo-task-4', 'Renew domain',                     'notStarted', 'normal', null,                       null),
    t('demo-task-5', 'Pick groceries',                   'notStarted', 'low',    null,                       null),
    t('demo-task-6', 'Write blog post',                  'completed',  'normal', null,                       iso(86_400_000)),
  ];
}

export function demoTodoLists() {
  return [
    // `display_name`, not `name`: that is what `todo_get_lists` returns (ipc.ts
    // `TodoTaskList`), and the config panel's picker labels its rows from it —
    // the mismatch drew a row of nameless buttons in demo mode.
    { id: 'demo-list', display_name: 'Tasks', is_default: true, account_id: DEMO_ACCOUNT_ID },
  ];
}

/** Demo RSS feed connections (provider 'rss'); `email` holds the feed URL. */
export const DEMO_RSS_FEEDS = [
  { id: 'rss:demo-verge', email: 'https://demo.feeds/theverge',    display_name: 'The Verge',  nickname: null, provider: 'rss' },
  { id: 'rss:demo-hn',    email: 'https://demo.feeds/hackernews',  display_name: 'Hacker News', nickname: null, provider: 'rss' },
  { id: 'rss:demo-css',   email: 'https://demo.feeds/csstricks',   display_name: 'CSS-Tricks',  nickname: null, provider: 'rss' },
];

export function demoEmailAccounts() {
  return [DEMO_ACCOUNT, ...DEMO_RSS_FEEDS];
}

// ── HTTP fixtures (intercepted via http_get) ─────────────────────────────
// `hint` is Yahoo's `meta.priceHint`, the venue's tick precision (stocks
// [S-39]); it defaults to 2 and drives both the quote and the generated bars, so
// a forex pair does not come out as a flat line of identical rounded closes.
const STOCK_TABLE: Record<string, { price: number; prev: number; hint?: number }> = {
  AAPL: { price: 218.34, prev: 216.92 },
  MSFT: { price: 432.12, prev: 434.30 },
  NVDA: { price: 121.55, prev: 118.48 },
  TSLA: { price: 246.80, prev: 251.21 },
  GOOGL:{ price: 168.05, prev: 167.12 },
  AMZN: { price: 189.44, prev: 187.02 },
  // Forex, commodities and indices ride the same chart endpoint as equities
  // (stocks [S-22]), so one row each is all the picker preview and the
  // screenshot runs need to show those classes (issue #116).
  'EURUSD=X': { price: 1.1567, prev: 1.1531, hint: 4 },
  'GBPUSD=X': { price: 1.3402, prev: 1.3438, hint: 4 },
  'GC=F':     { price: 4429.20, prev: 4420.40 },
  'CL=F':     { price: 62.18, prev: 63.05 },
  '^GSPC':    { price: 7798.99, prev: 7748.50 },
  '^DJI':     { price: 49_712.30, prev: 49_530.11 },
};
const CRYPTO_TABLE: Record<string, { price: number; change: number }> = {
  bitcoin:  { price: 67_842, change:  1.82 },
  ethereum: { price:  3_421, change: -0.74 },
  solana:   { price:    178, change:  3.91 },
};

export function demoHttpGet(args: { url?: string }): string {
  const url = args.url ?? '';

  // Yahoo Finance chart endpoint: /v8/finance/chart/{SYMBOL}
  const yahooMatch = url.match(/finance\.yahoo\.com\/v\d+\/finance\/chart\/([^?]+)/);
  if (yahooMatch) {
    const symbol = decodeURIComponent(yahooMatch[1]);
    const q = STOCK_TABLE[symbol] ?? { price: 100, prev: 99 };
    return JSON.stringify(yahooChartResponse(symbol, q.price, q.prev, q.hint ?? 2));
  }

  // CoinGecko simple/price
  if (url.includes('coingecko.com') && url.includes('/simple/price')) {
    const idsMatch = url.match(/[?&]ids=([^&]+)/);
    const curMatch = url.match(/vs_currencies=([^&]+)/);
    const ids = (idsMatch ? decodeURIComponent(idsMatch[1]) : 'bitcoin').split(',');
    const cur = (curMatch ? curMatch[1] : 'usd').toLowerCase();
    const out: Record<string, Record<string, number>> = {};
    for (const id of ids) {
      const c = CRYPTO_TABLE[id] ?? { price: 1, change: 0 };
      Object.assign(out, coingeckoPrice(id, c.price, c.change, cur));
    }
    return JSON.stringify(out);
  }
  // CoinGecko market_chart
  if (url.includes('coingecko.com') && url.includes('market_chart')) {
    const idMatch = url.match(/coins\/([^/]+)\/market_chart/);
    const id = idMatch ? decodeURIComponent(idMatch[1]) : 'bitcoin';
    const c = CRYPTO_TABLE[id] ?? { price: 1, change: 0 };
    const pts = Array.from({ length: 24 }, (_, i) => c.price * (1 + (Math.sin(i / 3) + (Math.random() - 0.5)) * 0.01));
    return JSON.stringify(coingeckoChart(pts));
  }

  // Web Element: a small static page with pickable elements (demo/preview).
  if (url.includes('demo.web')) return demoWebPage();

  // RSS: return a small feed whose items link to the requested feed's host, so
  // selecting multiple demo feeds aggregates into a visibly mixed-source list.
  return demoRssFeed(url);
}

/** Demo stand-in for the `web_element_render_html` command (post-JS page HTML). */
export function demoWebElementRender(_args: { url?: string }): string {
  return demoWebPage();
}

/** A self-contained styled page for the Web Element widget's demo/preview. */
function demoWebPage(): string {
  return '<!doctype html><html><head><style>' +
    'body{font-family:system-ui,-apple-system,sans-serif;background:#f4f5f7;padding:24px;}' +
    '.card{border:1px solid #e2e4e8;border-radius:14px;padding:18px 20px;max-width:300px;' +
    'background:#fff;box-shadow:0 1px 3px rgba(0,0,0,.08);}' +
    '.label{font-size:11px;letter-spacing:.08em;text-transform:uppercase;color:#8a90a0;}' +
    '.price{font-size:30px;font-weight:700;color:#111;margin:4px 0;}' +
    '.up{color:#0a9d63;font-size:13px;font-weight:600;}' +
    '</style></head><body>' +
    '<div class="card" id="quote">' +
    '<div class="label">BTC / USD</div>' +
    '<div class="price">$63,210</div>' +
    '<div class="up">▲ 2.4% today</div>' +
    '</div></body></html>';
}

/** One of the demo RSS sources, keyed by the feed account's URL. */
const DEMO_RSS_SOURCES: Record<string, { title: string; host: string; items: string[] }> = {
  'https://demo.feeds/theverge': { title: 'The Verge', host: 'theverge.com', items: [
    'Themia ships demo mode for every widget',
    'The desktop is back: why native apps are having a renaissance',
    'Designing for glassmorphism in 2026',
  ] },
  'https://demo.feeds/hackernews': { title: 'Hacker News', host: 'news.ycombinator.com', items: [
    'Tauri 2.5 brings smaller binaries and faster IPC',
    'SolidJS 2.0 enters release candidate',
    'Show HN: I built my desktop in a weekend',
  ] },
  'https://demo.feeds/csstricks': { title: 'CSS-Tricks', host: 'css-tricks.com', items: [
    'A complete guide to container queries',
    'Frosted glass effects with backdrop-filter',
    'Modern scrollbar styling that actually works',
  ] },
};

function demoRssFeed(url: string): string {
  const src = DEMO_RSS_SOURCES[url] ?? {
    title: 'Demo Feed', host: 'example.com',
    items: ['Themia ships demo mode for every widget', 'The desktop is back', 'Designing for glassmorphism in 2026'],
  };
  const items = src.items.map((title, i) =>
    `<item><title>${title}</title><link>https://${src.host}/${i + 1}</link>` +
    `<pubDate>${new Date(now() - (i * 7 + 1) * 3600_000).toUTCString()}</pubDate>` +
    `<description>Sample article from ${src.title}.</description></item>`,
  ).join('');
  return `<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel>
<title>${src.title}</title>
<link>https://${src.host}</link>
<description>Sample headlines</description>
${items}
</channel></rss>`;
}

// ── OneDrive ─────────────────────────────────────────────────────────────
export function demoOneDriveItems() {
  const mk = (name: string, isFolder: boolean, sizeMB: number, msAgo: number, mime: string | null = null) => ({
    id: 'demo-' + name,
    name,
    size: Math.round(sizeMB * 1_048_576),
    last_modified: iso(msAgo),
    web_url: '',
    is_folder: isFolder,
    mime_type: mime,
  });
  return [
    mk('Projects',           true,  0,    60 * 60_000),
    mk('Shared with me',     true,  0,     2 * 3600_000),
    mk('Roadmap Q2.docx',    false, 0.3,  30 * 60_000, 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'),
    mk('Pitch deck.pptx',    false, 4.2,   2 * 3600_000, 'application/vnd.openxmlformats-officedocument.presentationml.presentation'),
    mk('Budget 2026.xlsx',   false, 0.8,  18 * 3600_000, 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'),
    mk('Mockup v2.png',      false, 1.6,   4 * 3600_000, 'image/png'),
    mk('demo-reel.mp4',      false, 82,   86_400_000,   'video/mp4'),
    mk('notes.md',           false, 0.01, 6 * 3600_000, 'text/markdown'),
    mk('invoice-q1.pdf',     false, 0.4,  3 * 86_400_000, 'application/pdf'),
  ];
}

// ── Notes ────────────────────────────────────────────────────────────────
// Ids are the real 36-char UUID shape, because the backend validates it and a
// demo id that could never come back from `notes_create` would let a bug hide.

export const DEMO_NOTE_ID = '00000000-0000-4000-8000-000000000001';
const DEMO_NOTE_ID_2 = '00000000-0000-4000-8000-000000000002';

export function demoNotes() {
  return [
    { id: DEMO_NOTE_ID, title: 'Standup', updated_at: Math.round((now() - 20 * 60_000) / 1000) },
    { id: DEMO_NOTE_ID_2, title: 'Reading list', updated_at: Math.round((now() - 3 * 86_400_000) / 1000) },
  ];
}

/** Body for a demo note. Exercises headings, tasks and inline marks so the
 *  screenshot/preview shows the markdown renderer doing real work. */
export function demoNoteBody(id: string): string {
  if (id === DEMO_NOTE_ID_2) {
    return [
      '## Reading list',
      '',
      '- *Shape Up* — Basecamp',
      '- A Philosophy of Software Design',
      '- `man 7 signal`',
    ].join('\n');
  }
  return [
    '# Standup',
    '',
    '- [x] Ship the **notes** store',
    '- [ ] Review Anna\'s PR',
    '- [ ] Write up the ~~old~~ new sync notes',
    '',
    '> Blocked on the design review until Thursday.',
  ].join('\n');
}

// ── Apps ─────────────────────────────────────────────────────────────────
export function demoAppLayouts(): DemoLayout[] {
  const app = (name: string, exe: string) => ({
    kind: 'path' as const,
    target: `demo://app/${name}`,
    label: name,
    matchHint: exe,
    reuseIfRunning: true,
  });
  return [
    {
      id: 'demo-layout-browse',
      name: 'Browse',
      root: { kind: 'leaf', id: 'l1', assignment: app('Chrome', 'chrome.exe') },
    },
    {
      id: 'demo-layout-code',
      name: 'Code & Browse',
      root: {
        kind: 'split', id: 's1', direction: 'vertical', ratio: 0.6,
        a: { kind: 'leaf', id: 'l1', assignment: app('VS Code', 'Code.exe') },
        b: { kind: 'leaf', id: 'l2', assignment: app('Chrome',  'chrome.exe') },
      },
    },
    {
      id: 'demo-layout-office',
      name: 'Office',
      root: {
        kind: 'split', id: 's1', direction: 'horizontal', ratio: 0.55,
        a: { kind: 'leaf', id: 'l1', assignment: app('Word', 'WINWORD.EXE') },
        b: {
          kind: 'split', id: 's2', direction: 'vertical', ratio: 0.5,
          a: { kind: 'leaf', id: 'l2', assignment: app('Excel',   'EXCEL.EXE') },
          b: { kind: 'leaf', id: 'l3', assignment: app('Outlook', 'OUTLOOK.EXE') },
        },
      },
    },
  ];
}

export function demoStartMenuApps() {
  const mk = (name: string, category: string) => ({ name, path: `demo://app/${name}`, category });
  return [
    mk('Chrome',       'Internet'),
    mk('Firefox',      'Internet'),
    mk('VS Code',      'Development'),
    mk('Terminal',     'Development'),
    mk('Git Bash',     'Development'),
    mk('Notepad',      'Accessories'),
    mk('Calculator',   'Accessories'),
    mk('Paint',        'Accessories'),
    mk('Word',         'Office'),
    mk('Excel',        'Office'),
    mk('PowerPoint',   'Office'),
    mk('Outlook',      'Office'),
    mk('Spotify',      'Media'),
    mk('VLC',          'Media'),
    mk('Photoshop',    'Graphics'),
    mk('Figma',        'Graphics'),
    mk('Discord',      'Communication'),
    mk('Slack',        'Communication'),
    mk('Steam',        'Games'),
  ];
}

// ── Gallery ──────────────────────────────────────────────────────────────
/** Bundled landscape photos under `public/demo-gallery/`.
 *
 *  These used to be picsum.photos URLs. A demo fixture that needs the network
 *  is not a fixture: the screenshot export rendered twelve photos on its dark
 *  pass and a completely empty widget on the light one, because the requests
 *  were rate-limited between the two. Bundled files also mean the picker
 *  preview works offline and on a first run behind a captive portal.
 *
 *  Source: Unsplash, whose licence allows free commercial use with no
 *  attribution required. ~380 KB total for the eight. */
export function demoGalleryImages() {
  return Array.from({ length: 8 }, (_, i) => `/demo-gallery/nature-${i + 1}.jpg`);
}

// ── Video Stream ─────────────────────────────────────────────────────────
/** A still stand-in frame for demo mode and the Add-Widget preview: an inline
 *  SVG data URI, so the preview needs neither a camera nor a network
 *  (video-stream SPEC [S-13]).
 *
 *  The picture is a real photograph rather than a drawn scene — a drawing of a
 *  camera view reads as an illustration, which undersells a widget whose whole
 *  job is showing a live picture. It is base64'd into the SVG because SVG
 *  loaded through `<img>` runs in secure static mode, where external
 *  references (even same-origin ones) are dropped; a bundled-asset URL would
 *  simply not paint. The SVG survives as the wrapper because it is what
 *  carries the camera chrome over the photo. */
export function demoVideoFrame(): string {
  const svg =
    '<svg xmlns="http://www.w3.org/2000/svg" width="480" height="360" viewBox="0 0 480 360">' +
    `<image x="0" y="0" width="480" height="360" preserveAspectRatio="xMidYMid slice" href="data:image/jpeg;base64,${DEMO_VIDEO_FRAME_JPEG_BASE64}"/>` +
    // Scrim under the chrome. The photo behind it is fixed, but a bright frame
    // would otherwise wash the white overlay out entirely.
    '<defs><linearGradient id="scrim" x1="0" y1="0" x2="0" y2="1">' +
    '<stop offset="0" stop-color="#000000" stop-opacity="0.55"/>' +
    '<stop offset="1" stop-color="#000000" stop-opacity="0"/>' +
    '</linearGradient></defs>' +
    '<rect x="0" y="0" width="480" height="64" fill="url(#scrim)"/>' +
    // overlay chrome
    '<circle cx="30" cy="30" r="6" fill="#ef4444"/>' +
    '<text x="46" y="35" font-family="Segoe UI, sans-serif" font-size="16" fill="#ffffff" opacity="0.8">CAM 1</text>' +
    '<text x="342" y="35" font-family="Segoe UI, sans-serif" font-size="15" fill="#ffffff" opacity="0.55">LIVE 1080p</text>' +
    '</svg>';
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}

// ── Music ────────────────────────────────────────────────────────────────
export function demoNowPlaying() {
  return {
    title: 'Midnight Drive',
    artist: 'The Neon Trails',
    album: 'City Lights',
    thumbnail_base64: null,
    position_ms: 72_000,
    duration_ms: 214_000,
    is_playing: true,
    has_next: true,
    has_previous: true,
  };
}

// ── Stocks: proper Yahoo chart-endpoint shape ───────────────────────────
function yahooChartResponse(symbol: string, price: number, prev: number, priceHint: number) {
  // A random *walk*, not white noise around the trend line. Re-rolling an
  // independent offset per sample made adjacent bars jump ±0.4% in both
  // directions, so the demo chart came out a sawtooth that no intraday series
  // resembles; each step now nudges the previous close instead.
  const pts = (() => {
    const n = 40;
    const drift = (price - prev) / (n - 1);
    const out: number[] = [];
    let v = prev;
    for (let i = 0; i < n; i++) {
      out.push(v);
      v += drift + (Math.random() - 0.5) * (price * 0.0016);
    }
    // The last bar is what the chart's right edge shows, so pin it to the
    // quoted price — otherwise the line ends somewhere the big number above it
    // says it should not.
    out[n - 1] = price;
    // Rounded to the instrument's own tick, not always to cents: at 2dp an FX
    // pair's whole 0.3% day fits inside one rounding step and every bar comes
    // out identical, i.e. a dead-flat chart.
    return out.map(p => +p.toFixed(priceHint));
  })();
  // A session window straddling now, so demo shots show a live market rather
  // than the "Market closed" badge ([S-33]).
  const nowSec = Math.floor(Date.now() / 1000);
  const regular = { start: nowSec - 7200, end: nowSec + 7200 };
  // `barsOf` zips `timestamp` against `close` and drops any sample missing
  // either half, so closes alone produce zero bars: the sparkline then fails
  // its `length >= 2` gate and the chart vanishes, leaving a bare price. Both
  // arrays are required. Spacing the stamps across the *elapsed* part of the
  // session (start → now) keeps every bar inside `regular`, so `barSegments`
  // returns one solid run rather than a dashed extended-hours tail.
  const stamps = Array.from({ length: pts.length }, (_, i) =>
    Math.round(regular.start + ((nowSec - regular.start) * i) / (pts.length - 1)),
  );
  return {
    chart: {
      result: [{
        meta: {
          symbol, currency: 'USD', regularMarketPrice: price, priceHint,
          chartPreviousClose: prev, previousClose: prev,
          currentTradingPeriod: {
            pre: { start: regular.start - 19800, end: regular.start },
            regular,
            post: { start: regular.end, end: regular.end + 14400 },
          },
        },
        timestamp: stamps,
        indicators: { quote: [{ close: pts }] },
      }],
    },
  };
}

function coingeckoPrice(id: string, price: number, change: number, cur: string) {
  return { [id]: { [cur.toLowerCase()]: price, [`${cur.toLowerCase()}_24h_change`]: change } };
}

function coingeckoChart(pricePoints: number[]) {
  const now = Date.now();
  return { prices: pricePoints.map((p, i) => [now - (pricePoints.length - i) * 3600_000, p]) };
}

// ── Folder widget ────────────────────────────────────────────────────────
export function demoFolderEntries() {
  return [
    { name: 'Projects',         isDirectory: true,  isFile: false, isSymlink: false, isHidden: false },
    { name: 'Documents',        isDirectory: true,  isFile: false, isSymlink: false, isHidden: false },
    { name: 'Screenshots',      isDirectory: true,  isFile: false, isSymlink: false, isHidden: false },
    { name: 'README.md',        isDirectory: false, isFile: true,  isSymlink: false, isHidden: false },
    { name: 'roadmap.md',       isDirectory: false, isFile: true,  isSymlink: false, isHidden: false },
    { name: 'budget.xlsx',      isDirectory: false, isFile: true,  isSymlink: false, isHidden: false },
    { name: 'pitch-deck.pptx',  isDirectory: false, isFile: true,  isSymlink: false, isHidden: false },
    { name: 'invoice-q1.pdf',   isDirectory: false, isFile: true,  isSymlink: false, isHidden: false },
    { name: 'mockup-v2.png',    isDirectory: false, isFile: true,  isSymlink: false, isHidden: false },
    { name: 'logo.svg',         isDirectory: false, isFile: true,  isSymlink: false, isHidden: false },
    { name: 'notes.txt',        isDirectory: false, isFile: true,  isSymlink: false, isHidden: false },
    { name: 'demo.mp4',         isDirectory: false, isFile: true,  isSymlink: false, isHidden: false },
  ];
}

// Cloud-sync attribute flags for a few demo entries so the folder widget's
// cloud badges render in demo/preview mode. Values mirror the Win32 flags
// FolderView interprets: 0x400 reparse (synced), |0x80000 pinned,
// |0x400000 recall-on-data-access (cloud-only).
const DEMO_CLOUD_ATTRS: Record<string, number> = {
  'Documents':       0x400 | 0x80000,  // pinned: solid green check
  'README.md':       0x400,            // synced: green check
  'pitch-deck.pptx': 0x400,            // synced: green check
  'budget.xlsx':     0x400 | 0x400000, // cloud-only: blue cloud
};

export function demoFileAttributes(paths: string[]) {
  const out: Record<string, number> = {};
  for (const fp of paths) {
    const name = fp.split(/[\\/]/).pop() ?? '';
    const attrs = DEMO_CLOUD_ATTRS[name];
    if (attrs !== undefined) out[fp] = attrs;
  }
  return out;
}

// ── Claude usage widget ──────────────────────────────────────────────────
export function demoClaudeUsage() {
  const inHours = (h: number) => new Date(now() + h * 3_600_000).toISOString();
  return {
    // `utilization` is a percentage, exactly as the live endpoint reports it.
    five_hour: { utilization: 62, resets_at: inHours(2) },
    seven_day: { utilization: 38, resets_at: inHours(56) },
    seven_day_opus: { utilization: 91, resets_at: inHours(56) },
    seven_day_sonnet: { utilization: 22, resets_at: inHours(56) },
  };
}

// ── GitHub widget ────────────────────────────────────────────────────────
export function demoGithub() {
  const t = (msAgo: number) => new Date(now() - msAgo).toISOString();
  return {
    repo: {
      name: 'themia',
      full_name: 'demo-org/themia',
      description: 'Beautiful desktop widgets for Windows.',
      html_url: 'https://example.com/demo-org/themia',
      stargazers_count: 1284,
      forks_count: 73,
      open_issues_count: 14,
      default_branch: 'main',
      language: 'TypeScript',
      pushed_at: t(2 * 3600_000),
    },
    runs: [
      { id: 1, name: 'CI',      status: 'completed', conclusion: 'success', head_branch: 'main',           created_at: t(45 * 60_000),  html_url: '#' },
      { id: 2, name: 'Release', status: 'completed', conclusion: 'success', head_branch: 'main',           created_at: t(3 * 3600_000), html_url: '#' },
      { id: 3, name: 'CI',      status: 'in_progress', conclusion: null,    head_branch: 'feature/demo-mode', created_at: t(8 * 60_000), html_url: '#' },
      { id: 4, name: 'CI',      status: 'completed', conclusion: 'failure', head_branch: 'fix/scrollbar',  created_at: t(20 * 3600_000), html_url: '#' },
    ],
    commits: [
      { sha: 'a1b2c3d', html_url: '#', commit: { message: 'feat: demo mode for every widget',          author: { name: 'Anna Schmidt', date: t(45 * 60_000)  } } },
      { sha: 'e4f5g6h', html_url: '#', commit: { message: 'fix: scrollbar overflow on small widgets',  author: { name: 'Liam Walker',  date: t(3 * 3600_000) } } },
      { sha: 'i7j8k9l', html_url: '#', commit: { message: 'chore: bump tauri to 2.5',                  author: { name: 'Sofia Müller', date: t(20 * 3600_000)} } },
    ],
    prs: [
      { number: 142, title: 'Demo mode for every widget',          state: 'open',   merged_at: null,                       html_url: '#', created_at: t(2 * 3600_000),  user: { login: 'anna' } },
      { number: 141, title: 'Refactor email polling',              state: 'closed', merged_at: t(8 * 3600_000),            html_url: '#', created_at: t(2 * 86_400_000), user: { login: 'liam' } },
      { number: 140, title: 'Tauri 2.5 upgrade',                   state: 'closed', merged_at: t(86_400_000),              html_url: '#', created_at: t(3 * 86_400_000), user: { login: 'sofia' } },
    ],
    issues: [
      { number: 137, title: 'Battery widget shows wrong icon when charging', html_url: '#', created_at: t(5 * 3600_000), labels: [{ name: 'bug',         color: 'd73a4a' }] },
      { number: 134, title: 'Add Spotify widget',                            html_url: '#', created_at: t(2 * 86_400_000), labels: [{ name: 'enhancement', color: 'a2eeef' }] },
      { number: 130, title: 'Folder widget: support breadcrumbs',            html_url: '#', created_at: t(4 * 86_400_000), labels: [{ name: 'enhancement', color: 'a2eeef' }] },
    ],
    release: { tag_name: 'v0.4.2', html_url: '#' },
  };
}

// ── Direct fetch() fixtures ──────────────────────────────────────────────
export function demoWeatherCurrent() {
  return {
    temp: 21,
    feels: 20,
    humidity: 56,
    wind: 9,
    code: 2,
  };
}

export function demoWeatherForecast() {
  const today = new Date(); today.setHours(0, 0, 0, 0);
  const days = Array.from({ length: 7 }, (_, i) => {
    const d = new Date(today); d.setDate(d.getDate() + i);
    return d.toISOString().slice(0, 10);
  });
  return {
    daily: {
      time: days,
      temperature_2m_max: [22, 23, 19, 18, 21, 24, 25],
      temperature_2m_min: [13, 14, 12, 11, 12, 14, 16],
      weather_code:       [ 2,  1,  3, 61,  2,  0,  0],
      precipitation_sum:  [ 0,  0,  0,  4,  0,  0,  0],
    },
  };
}
