// Curated search synonyms for the Add-Widget picker (WidgetPicker.SPEC [S-04]).
//
// A widget's label and description are written for READING, not for searching:
// nobody reads "Live upload and download speeds" and types that. These are the
// words users actually reach for — the thing the widget shows ("wifi", "temperature"),
// the vendor or protocol behind it ("outlook", "imap"), the job it does
// ("timer", "launcher"), and the common alternate spelling ("todo"/"tasks").
//
// English only, deliberately: the picker searches the TRANSLATED label and
// description as well as the English ones, so a localized UI keeps working
// without this list having to be re-authored nine times. Keys are registry
// types; an unlisted widget simply has no aliases.
export const WIDGET_KEYWORDS: Record<string, string[]> = {
  folder: ['files', 'explorer', 'directory', 'file browser', 'documents', 'downloads'],
  email: ['mail', 'inbox', 'outlook', 'gmail', 'imap', 'messages', 'unread'],
  contacts: ['address book', 'people', 'phone numbers', 'directory'],
  calendar: ['events', 'meetings', 'appointments', 'agenda', 'schedule', 'outlook', 'google calendar'],
  todo: ['tasks', 'to-do', 'checklist', 'reminders', 'microsoft to do'],
  onedrive: ['cloud files', 'sharepoint', 'microsoft', 'recent files'],

  github: ['git', 'repository', 'repo', 'pull requests', 'ci', 'actions', 'commits', 'code'],
  githubNotifications: ['git', 'repo', 'inbox', 'mentions', 'alerts'],
  githubReviewRequested: ['git', 'repo', 'pull requests', 'pr', 'code review'],
  githubAssignedIssues: ['git', 'repo', 'tickets', 'bugs', 'backlog'],

  weatherCurrent: ['temperature', 'forecast', 'rain', 'wind', 'humidity', 'climate', 'degrees'],
  weatherForecast: ['temperature', 'rain', 'week', 'outlook', 'climate', 'degrees'],
  rss: ['feed', 'news', 'headlines', 'atom', 'blog', 'reader'],
  stocks: ['shares', 'ticker', 'market', 'finance', 'crypto', 'bitcoin', 'forex', 'currency', 'portfolio', 'price'],
  webElement: ['website', 'browser', 'embed', 'iframe', 'scrape', 'clip', 'page'],
  videoStream: ['camera', 'webcam', 'cctv', 'mjpeg', 'live view', 'printer'],
  claudeUsage: ['ai', 'anthropic', 'tokens', 'quota', 'limits', 'claude code'],
  music: ['spotify', 'player', 'playback', 'song', 'track', 'audio', 'media controls', 'now playing'],
  webSearch: ['google', 'bing', 'duckduckgo', 'browser', 'query', 'find'],

  cpu: ['processor', 'load', 'usage', 'performance', 'cores', 'task manager', 'system monitor'],
  ram: ['memory', 'usage', 'performance', 'task manager', 'system monitor'],
  gpu: ['graphics', 'vram', 'nvidia', 'amd', 'video card', 'system monitor'],
  net: ['network', 'internet', 'bandwidth', 'wifi', 'ethernet', 'speed', 'traffic', 'up down'],
  disk: ['storage', 'ssd', 'hdd', 'drive', 'space', 'free space', 'io'],
  ping: ['latency', 'network', 'internet', 'connection', 'packet loss', 'ms'],
  battery: ['power', 'charge', 'laptop', 'percent', 'energy'],
  bluetooth: ['devices', 'headphones', 'wireless', 'pairing'],
  screentime: ['usage', 'productivity', 'time tracking', 'app usage', 'statistics'],
  apps: ['launcher', 'programs', 'software', 'start menu', 'shortcuts', 'open'],
  volume: ['sound', 'audio', 'speaker', 'mute', 'loudness'],
  mixer: ['sound', 'audio', 'per app volume', 'speaker', 'levels', 'faders'],
  power: ['shutdown', 'restart', 'reboot', 'sleep', 'lock', 'log off', 'turn off'],
  clipboard: ['copy', 'paste', 'history', 'snippets'],

  clock: ['time', 'timezone', 'watch', 'world clock', 'date', 'analog', 'digital'],
  shortcut: ['link', 'launcher', 'bookmark', 'url', 'open', 'pin', 'quick access'],
  notes: ['notepad', 'text', 'memo', 'writing', 'markdown', 'sticky note'],
  scratchpad: ['draw', 'sketch', 'drawing', 'canvas', 'whiteboard', 'doodle', 'pen'],
  calculator: ['math', 'calc', 'numbers', 'arithmetic', 'convert'],
  gallery: ['photos', 'pictures', 'images', 'slideshow', 'wallpaper', 'bing'],
  colorSwatch: ['colour', 'block', 'accent', 'spacer', 'fill', 'divider', 'background'],
  countdown: ['timer', 'days until', 'deadline', 'event', 'anniversary', 'birthday'],
  pomodoro: ['timer', 'focus', 'productivity', 'work break', 'tomato', 'session'],
  habitTracker: ['streaks', 'daily', 'routine', 'goals', 'checklist', 'tracker'],
  moodTracker: ['feelings', 'journal', 'wellbeing', 'diary', 'emotions', 'tracker'],
  quote: ['inspiration', 'daily quote', 'saying', 'motivation'],
  sunMoon: ['sunrise', 'sunset', 'moon phase', 'daylight', 'golden hour', 'astronomy'],

  help: ['getting started', 'guide', 'docs', 'documentation', 'tutorial', 'faq', 'support'],
  themia: ['about', 'version', 'update', 'app info', 'license'],
  viewSwitcher: ['views', 'pages', 'desktops', 'workspaces', 'switch'],

  stack: ['container', 'group', 'collapse', 'accordion', 'combine', 'layout'],
  tabs: ['container', 'group', 'switch', 'combine', 'layout'],
  group: ['container', 'area', 'box', 'section', 'frame', 'collapse', 'layout'],
};
