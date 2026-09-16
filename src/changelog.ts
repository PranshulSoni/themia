// End-user changelog shown on the Updates page in App Settings.
// Newest-first. Keep entries short and in plain language, no jargon.
// Workflow: prepend a new entry before bumping the version.

export interface ChangelogEntry {
  version: string;
  date: string;
  // One-sentence summary of the most important change. Shown in the collapsed
  // header so users can scan releases without expanding each one.
  summary?: string;
  added?: string[];
  fixed?: string[];
}

export const CHANGELOG: ChangelogEntry[] = [
  {
    version: '0.14.0',
    date: '2026-07-21',
    summary: 'New Screen Time and Claude widgets, support for corporate proxies, one-click design presets, and a big translation and polish pass.',
    added: [
      'Screen Time widget: see how long you spend on your PC, with day-by-day history.',
      'Claude widget: check your Claude usage at a glance.',
      'Works behind corporate proxies now, with automatic PAC/WPAD detection.',
      'Design presets: pick a whole look in one click, or fine-tune your own.',
      'App is now translated into Simplified Chinese.',
    ],
    fixed: [
      'Much more of the app is translated across every language.',
      'Settings panels no longer lag while you type.',
      'Email (IMAP and CalDAV) is more reliable, with correct counts.',
      'Calendar, multi-monitor and accent-color polish throughout.',
    ],
  },
  {
    version: '0.13.1',
    date: '2026-07-01',
    summary: 'IMAP email reliability fix.',
    fixed: [
      'Email (IMAP) loads reliably on stricter mail servers, with correct message counts and safer actions.',
    ],
  },
  {
    version: '0.13.0',
    date: '2026-07-01',
    summary: 'Calendar polish plus GitHub, Folder, Email, CPU and Gallery fixes.',
    added: [
      'GitHub widget: filter and sort pull requests by state and order.',
      'Folder widget: filter by several keywords at once, separated by commas.',
      'Calendar scales more cleanly across widget sizes, with clearer times and titles.',
    ],
    fixed: [
      'Calculator now evaluates expressions again, instead of showing "Error" for everything.',
      'Calendar no longer shows "Invalid Date" for timed events in some time zones.',
      'Calendar updates right away when you switch months or click Today.',
      'CPU widget shows the real clock speed instead of being stuck at 0 GHz.',
      'Custom accent colors now apply correctly, even from unusual hex values.',
      'Email (IMAP) now shows fetch errors instead of failing silently.',
      'Gallery slideshow no longer swallows the spacebar while you type.',
      'Backspace and other keys pass through correctly to widget text fields.',
    ],
  },
  {
    version: '0.12.2',
    date: '2026-06-11',
    summary: 'New startup animations, steadier right-click menus, and folder icon and network fixes.',
    added: [
      'New widget startup animations, including hyperspace and hacker.',
    ],
    fixed: [
      'Folder icons now load reliably at startup, with no more occasional blank icons.',
      'Right-click menus are steadier and no longer flicker.',
      'Network widget no longer counts packet-capture adapters in its traffic totals.',
      'Performance improvements and visual polish.',
    ],
  },
  {
    version: '0.12.1',
    date: '2026-06-08',
    summary: 'Quit from Settings, Back/Forward in the Settings window, and optional anonymous usage data.',
    added: [
      'Quit Themia directly from Settings.',
      'Back and Forward in the Settings window: use Alt+← / Alt+→ or your mouse side buttons.',
      'Optional anonymous usage data to help improve Themia, anonymous only (no personal data, emails or files), on by default and easy to turn off in Settings.',
    ],
    fixed: [
      'Opening "Manage connections" from a widget now shows all your connected accounts, not just that widget\'s type.',
    ],
  },
  {
    version: '0.12.0',
    date: '2026-06-08',
    summary: 'More resilient startup, reliable per-screen sync, and a smoother Folder drag-out.',
    added: [
      'Different widget startup animations.',
      'Polish across several widgets and the gallery.',
    ],
    fixed: [
      'Recovers gracefully if it fails to start several times in a row.',
      'Pasting or cutting a widget over another no longer drags the one underneath.',
      'Per-screen settings, views and design now sync reliably across windows.',
      'Stale data or errors no longer flash when switching accounts, feeds or locations.',
      'Email, calendar and contacts handle Microsoft rate limits gracefully.',
      'Dragging files out of the Folder widget now moves them, like Explorer.',
    ],
  },
  {
    version: '0.11.0',
    date: '2026-05-17',
    summary: 'German and Spanish translations, desktop "throughshine", and a welcome tour for new users.',
    added: [
      'App is now translated into German and Spanish.',
      'Welcome tour for first-time users.',
      'Desktop "throughshine": widgets can let the desktop show through.',
      'Redesigned settings modal and menus.',
      'Edit-mode right-click menu on widgets.',
      'User-programmable custom widget.',
      'Game mode pauses widgets while a fullscreen app or presentation is active.',
      'Folder widget: choose single- or double-click to open.',
      'Folder widget can hide Windows-hidden files.',
    ],
    fixed: [
      'Reworked widget click and window passthrough handling.',
      'Shortcut widget polish and drag-and-drop improvements.',
      'Edit mode no longer turns itself on at startup.',
      'Widgets on your main monitor survive Windows "Show Desktop".',
      'Undo across multiple monitors is now reliable.',
    ],
  },
  {
    version: '0.10.4',
    date: '2026-04-23',
    summary: 'Faster autostart and a screen-switcher loading fix.',
    fixed: [
      'Faster startup on login.',
      'Screen switcher now loads correctly on app launch.',
    ],
  },
  {
    version: '0.10.3',
    date: '2026-04-22',
    summary: 'Redesigned Settings, Google sign-in and per-screen visual settings.',
    added: [
      'Redesigned Settings window with a clearer layout.',
      'Google sign-in for connected accounts.',
      'Provider logos on widget picker cards.',
      'Contacts widget: header bar, refresh action and brand logos.',
      'GPU widget shows dedicated and shared VRAM, like Task Manager.',
      'Per-screen visual settings (each screen can look different).',
      'Undo support for layout changes.',
      'More generous limits on the Free plan.',
      'Many widgets now resize cleanly at small sizes.',
    ],
    fixed: [
      'First file in the Folder widget no longer shows a blank icon.',
      'Music widget uses noticeably less CPU.',
      'Ctrl + X / C / V / A keep working outside the widget area.',
      'Right-click submenus position correctly when UI scale is changed.',
      'Folder widget refreshes immediately after deleting a file.',
      'Email widget shows an empty state when no account is selected.',
      'Connected accounts now appear in a stable order.',
    ],
  },
  {
    version: '0.10.2',
    date: '2026-04-19',
    summary: 'Folder widget CPU and polling fixes.',
    fixed: [
      'Folder widget no longer spikes CPU when watching busy folders.',
      'Polling and image download issues resolved.',
    ],
  },
  {
    version: '0.10.1',
    date: '2026-04-14',
    summary: 'Per-screen design overrides and new branding widgets.',
    added: [
      'New Themia branding and color swatch widgets.',
      'Per-screen overrides for background, widget background and blur.',
      'Folder widget: choose item size (XS / S / M / L).',
      '"Screen Settings" shortcut in the desktop right-click menu.',
    ],
    fixed: [
      'Theme switch now updates colors instantly.',
      'Upgrade button takes you straight to checkout.',
    ],
  },
  {
    version: '0.10.0',
    date: '2026-04-13',
    summary: 'High-resolution Folder icons and a configurable accent color.',
    added: [
      'Folder widget uses high-resolution Windows icons.',
      'Rename files directly in the Folder grid view.',
      'Configurable primary accent color.',
      'Screen settings moved into the sidebar.',
      'Larger settings modal, easier to read.',
    ],
    fixed: [
      'Memory leak and slowdowns in the Folder widget.',
      'Double-clicking a file now opens it in your default app instead of Explorer.',
      'Widget windows no longer flash above other apps.',
    ],
  },
  {
    version: '0.9.0',
    date: '2026-04-12',
    summary: 'Per-screen wallpaper, blur and drag-from-browser support.',
    added: [
      'Per-screen wallpaper.',
      'Configurable background blur.',
      'Duplicate screen from the context menu.',
      'Drag images from Chrome (and other browsers) straight into the Folder widget.',
      '.lnk and .url shortcuts open their target when double-clicked.',
    ],
    fixed: [
      'Gallery widget no longer freezes the UI on first load.',
      'Removed the broken stocks currency selector.',
    ],
  },
  {
    version: '0.8.1',
    date: '2026-04-12',
    summary: '"Up to date" indicator and a theme fix for the screen switcher.',
    added: [
      '"Up to date" indicator in Settings → Updates.',
    ],
    fixed: [
      'Screen switcher now follows the selected theme.',
    ],
  },
  {
    version: '0.8.0',
    date: '2026-04-12',
    summary: 'Todo widget tasks and a refreshed Settings window.',
    added: [
      'Todo widget: create and delete tasks.',
      'Icon picker for screens.',
      'Demo screen with sample widgets.',
      'Refreshed Settings window.',
    ],
    fixed: [
      'Several widget layout glitches.',
    ],
  },
  {
    version: '0.7.0',
    date: '2026-04-11',
    summary: 'Native Windows drag-and-drop and multi-monitor widget moves.',
    added: [
      'Native Windows drag-and-drop for files, works with Explorer, VS Code and more.',
      'Drag widgets between monitors.',
      'Centralized account management.',
    ],
    fixed: [
      'Edit mode dragging works again.',
      'Settings modal scrolls properly on smaller screens.',
      'Widgets no longer flash while refreshing data.',
    ],
  },
  {
    version: '0.6.1',
    date: '2026-04-07',
    summary: 'Welcome widget and a richer File Explorer view.',
    added: [
      'Welcome widget for new users.',
      'Richer File Explorer view.',
    ],
    fixed: [
      'Gallery widget glitches.',
      'Multi-screen handling improvements.',
      'Update download issues.',
    ],
  },
  {
    version: '0.6.0',
    date: '2026-04-07',
    summary: 'Improved multi-screen handling.',
    added: [
      'Improved multi-screen handling.',
    ],
  },
  {
    version: '0.5.3',
    date: '2026-04-07',
    summary: 'Demo mode in every widget.',
    added: [
      'Demo mode shows sample data in every widget.',
    ],
  },
  {
    version: '0.5.2',
    date: '2026-04-05',
    summary: 'New Gallery widget and more right-click actions.',
    added: [
      'Gallery widget.',
      'More right-click actions across widgets.',
    ],
  },
  {
    version: '0.5.1',
    date: '2026-04-05',
    summary: 'Auto-update download path fix.',
    fixed: [
      'Auto-update download path corrected.',
    ],
  },
  {
    version: '0.5.0',
    date: '2026-04-05',
    summary: 'Built-in auto-update support.',
    added: [
      'Built-in auto-update support.',
    ],
  },
  {
    version: '0.4.2',
    date: '2026-04-05',
    summary: 'License key verification.',
    added: [
      'License key verification.',
    ],
  },
  {
    version: '0.3.0',
    date: '2026-04-05',
    summary: 'Refreshed widget appearance.',
    added: [
      'Refreshed widget appearance.',
      'Separate calculator widgets.',
    ],
    fixed: [
      'GitHub Stats widget.',
    ],
  },
  {
    version: '0.2.0',
    date: '2026-04-05',
    summary: 'More widgets, plus autostart and multi-instance fixes.',
    added: [
      'More built-in widgets.',
      'Improved Git widget.',
    ],
    fixed: [
      'Multiple instances bug.',
      'Autostart now works reliably on login.',
    ],
  },
  {
    version: '0.1.0',
    date: '2026-04-05',
    summary: 'First public release.',
    added: [
      'First public release: configurable desktop widget grid.',
    ],
  },
];
