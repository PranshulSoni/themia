// Drop-in replacement for `import { invoke } from '@tauri-apps/api/core'`.
// When globalDesign.demoMode is true, intercepts known data-loading commands
// and returns fixtures instead of hitting the Rust backend.
//
// Action commands (open_url, fs_*, push_windows_to_desktop, mark_read, etc.)
// fall through to the real invoke so the app stays interactive in demo mode.

import { createSignal } from 'solid-js';
import { invoke as realInvoke, type InvokeArgs } from '@tauri-apps/api/core';
import { globalDesign } from '../design.ts';
import type { CommandName, Commands, InvokeRest } from '../ipc.ts';
import * as fx from './fixtures.ts';

// Runtime, non-persisted demo flag scoped to a single window (e.g. the
// Add-Widget picker mounting live previews). OR'd into the `globalDesign.demoMode`
// check below so previews get fixture data without touching the persisted setting.
const [previewDemo, setPreviewDemo] = createSignal(false);
export { previewDemo, setPreviewDemo };

type Args = InvokeArgs | undefined;

// Returns a fixture for the given command, or `undefined` if not handled
// (in which case the call should pass through to the real backend).
function fixture(cmd: string, args: Args): unknown | undefined {
  const a = (args ?? {}) as Record<string, unknown>;
  switch (cmd) {
    // ── System ──
    case 'list_system_fonts':    return ['Arial', 'Calibri', 'Cambria', 'Comic Sans MS', 'Consolas', 'Georgia', 'Segoe UI', 'Tahoma', 'Times New Roman', 'Verdana'];
    case 'get_system_stats':     return fx.demoSystemStats();
    case 'get_battery_stats':    return fx.demoBatteryStats();
    case 'get_gpu_stats':        return fx.demoGpuStats(a);
    case 'list_gpus':            return fx.demoListGpus();
    case 'get_bluetooth_devices': return fx.demoBluetoothDevices();
    case 'screentime_get_day':   return fx.demoScreentime();
    case 'screentime_get_range': return fx.demoScreentimeRange(Number(a.days ?? 7));
    case 'ping_hosts':           return fx.demoPingHosts(a);
    case 'volume_get':           return fx.demoVolumeState();
    // Acked no-ops: dragging the demo slider must not move the machine's real
    // output level (same reasoning as `power_run` below).
    case 'volume_set':
    case 'volume_set_mute':      return null;
    case 'mixer_get_sessions':   return fx.demoMixerSessions(a);
    // Acked no-ops for the same reason as volume_set: dragging a demo slider
    // must not move a real application's output level.
    case 'mixer_set_volume':
    case 'mixer_set_mute':       return null;

    // ── Email ──
    case 'email_get_messages':       return fx.demoEmailMessages(a);
    case 'email_get_folder_status':  return fx.demoFolderStatus();
    case 'email_list_accounts':      return fx.demoEmailAccounts();
    case 'email_add_rss_feed': {
      const url = String(a.url ?? '').trim();
      let host = url;
      try { host = new URL(url).hostname.replace(/^www\./, ''); } catch { /* keep url */ }
      return { id: `rss:${url}`, email: url, display_name: a.nickname || host, nickname: a.nickname ?? null, provider: 'rss' };
    }

    // ── Claude usage ──
    case 'email_get_claude_usage': return fx.demoClaudeUsage();

    // ── Calendar ──
    case 'calendar_get_events':     return fx.demoCalendarEvents(a);
    case 'calendar_list_calendars': return fx.demoListCalendars();
    case 'calendar_get_birthdays':  return fx.demoBirthdays();

    // ── Contacts ──
    case 'contacts_get_list':
    case 'contacts_list':     return fx.demoContacts();

    // ── Todo ──
    case 'todo_get_tasks': return fx.demoTodoTasks();
    case 'todo_get_lists': return fx.demoTodoLists();

    // ── OneDrive ──
    case 'onedrive_list_folder':
    case 'onedrive_recent_files': return fx.demoOneDriveItems();

    // ── Notes (local store) ──
    case 'notes_list': return fx.demoNotes();
    case 'notes_read': return fx.demoNoteBody(String(a.id ?? ''));
    // Writes are acked, not applied: demo mode exists so clicking around is
    // safe, and the fixtures are the point of it.
    case 'notes_write':  return Math.round(Date.now() / 1000);
    case 'notes_rename':
    case 'notes_delete': return null;
    case 'notes_create':
      return { id: fx.DEMO_NOTE_ID, title: String(a.title ?? ''), updated_at: Math.round(Date.now() / 1000) };

    // ── Apps / Gallery / Music ──
    case 'list_start_menu_apps':  return fx.demoStartMenuApps();
    case 'scan_images':           return fx.demoGalleryImages();
    case 'music_get_now_playing': return fx.demoNowPlaying();

    // ── Folder shell stubs (avoid backend round-trip) ──
    case 'get_shell_new_entries':
    case 'get_shell_menu_entries': return [];
    case 'get_file_attributes_batch': return fx.demoFileAttributes((a.paths as string[]) ?? []);
    case 'get_file_icons_batch':   return fx.demoFileIconsBatch(a);
    case 'get_file_icon':          return Object.values(fx.demoFileIconsBatch({ paths: [String(a.path ?? '')] }))[0] ?? '';
    case 'get_git_repo_info_batch': return {};
    case 'get_git_status':         return { is_git: false, branch: '', files: {}, staged: 0, modified: 0, untracked: 0, deleted: 0, conflicted: 0 };

    // ── HTTP proxy used by Stocks / RSS ──
    case 'http_get': return fx.demoHttpGet(a);

    // ── Web Element: JS-rendered page HTML (real cmd uses an off-screen webview) ──
    case 'web_element_render_html': return fx.demoWebElementRender(a);

    // Demo mode is for screenshots and the Add-Widget previews, so the Power
    // widget must NOT reach the real backend here: the whole point of the mode
    // is that clicking around is safe, and the real command turns the machine
    // off (power SPEC [S-07]). Swallowed rather than left to fall through.
    case 'power_run': return null;

    // Acked no-ops so widget mutation actions don't error in demo mode.
    case 'email_mark_message_read':
    case 'email_delete_message':
    case 'email_move_message':
    case 'todo_update_task':
    case 'todo_rename_task':
      return null;
    case 'todo_delete_task':
      return null;
    case 'todo_create_task':
      return { id: `demo-${Date.now()}`, title: a.title ?? '', status: 'notStarted', importance: 'normal', due_date: null, completed_at: null, list_id: a.listId ?? '' };

    default:
      return undefined;
  }
}

// Test-only override. Tauri 2 defines `__TAURI_INTERNALS__.invoke` as
// non-writable, so e2e specs can't stub the IPC at the window level. This
// indirection lets webdriver specs replace the invoke every widget uses.
let invokeOverride: ((cmd: string, args?: Args) => Promise<unknown>) | null = null;

export async function invoke<K extends CommandName>(
  cmd: K,
  ...rest: InvokeRest<K>
): Promise<Commands[K]['result']> {
  const args = rest[0] as Args;
  if (invokeOverride) return invokeOverride(cmd, args) as Promise<Commands[K]['result']>;
  if (globalDesign.demoMode || previewDemo()) {
    const f = fixture(cmd, args);
    // Demo fixtures are intentionally loose, so cast to the typed result.
    if (f !== undefined) return f as Commands[K]['result'];
  }
  return realInvoke<Commands[K]['result']>(cmd, args);
}

declare global {
  interface Window {
    // Test-only hook (webdriver): lets a spec override the invoke transport.
    __themiaInvoke?: { setInvoke: (fn: ((cmd: string, args?: Args) => Promise<unknown>) | null) => void };
  }
}

if (typeof window !== 'undefined' && navigator.webdriver) {
  window.__themiaInvoke = {
    setInvoke: (fn: ((cmd: string, args?: Args) => Promise<unknown>) | null) => {
      invokeOverride = fn;
    },
  };
}

// Synchronous helper for widgets that want an early-return when demo is on
// (e.g. ones that fetch() directly instead of going through invoke).
export function isDemo(): boolean {
  return globalDesign.demoMode || previewDemo();
}
