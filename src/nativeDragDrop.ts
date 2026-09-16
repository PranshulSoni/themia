/**
 * Native OLE drag/drop abstraction layer.
 *
 * Listens for "native-drag" Tauri events emitted from the Rust IDropTarget
 * and routes them to registered drop zone handlers.  Provides `startDrag()`
 * to initiate OLE DoDragDrop from the frontend.
 */

import { invoke } from './ipc.ts';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { createSignal, onCleanup, type Accessor } from 'solid-js';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface NativeDragEvent {
  eventType: 'enter' | 'over' | 'leave' | 'drop';
  screenX: number;
  screenY: number;
  clientX: number;
  clientY: number;
  dataTypes: string[];
  effect: string;
  widgetData?: string;
  filePaths?: string[];
  virtualFileNames?: string[];
  urls?: string[];
  ctrlHeld: boolean;
}

export interface DragData {
  type: 'widget' | 'files';
  widgetJson?: string;
  filePaths?: string[];
  effectAllowed: 'copy' | 'move' | 'copyMove';
}

export type DragEventHandler = (event: NativeDragEvent) => void;
export type DropHandler = (event: NativeDragEvent) => boolean;

// Module-local alias so the global Window augmentation below references THIS
// payload shape, not the DOM lib's unrelated global `NativeDragEvent`.
type NativeDragPayload = NativeDragEvent;
declare global {
  interface Window {
    __nativeDragDispatch?: (payload: NativeDragPayload) => void;
  }
}

interface DropZone {
  id: string;
  /** Element getter; when truthy the zone is "mounted". */
  element: () => HTMLElement | undefined;
  onEnter?: DragEventHandler;
  onOver?: DragEventHandler;
  onLeave?: DragEventHandler;
  onDrop?: DropHandler;
}

// ---------------------------------------------------------------------------
// Registry
// ---------------------------------------------------------------------------

const zones: DropZone[] = [];

/** Currently hovered zone ID (for leave detection). */
let activeZoneId: string | null = null;

/** Last zone-presence state reported to Rust (avoids redundant invokes). */
let lastReportedOverZone = false;

/**
 * Data types advertised by the current drag session.
 * Set on "enter", cleared on "leave".
 */
let sessionDataTypes: string[] = [];

/**
 * Register a drop zone. Returns a cleanup function.
 */
export function registerDropZone(
  id: string,
  element: () => HTMLElement | undefined,
  handlers: {
    onEnter?: DragEventHandler;
    onOver?: DragEventHandler;
    onLeave?: DragEventHandler;
    onDrop?: DropHandler;
  },
): () => void {
  const zone: DropZone = { id, element, ...handlers };
  zones.push(zone);
  return () => {
    const idx = zones.indexOf(zone);
    if (idx >= 0) zones.splice(idx, 1);
    if (activeZoneId === id) activeZoneId = null;
  };
}

// ---------------------------------------------------------------------------
// Zone hit-test
// ---------------------------------------------------------------------------

function findZoneAt(clientX: number, clientY: number): DropZone | null {
  // Try element-from-point first (most precise)
  const el = document.elementFromPoint(clientX, clientY);
  if (!el) return null;

  for (const zone of zones) {
    const zoneEl = zone.element();
    if (zoneEl && zoneEl.contains(el)) return zone;
  }
  return null;
}

// ---------------------------------------------------------------------------
// Event router
// ---------------------------------------------------------------------------

function handleNativeDrag(event: NativeDragEvent) {
  // On "enter", capture the data types for the session
  if (event.eventType === 'enter' && event.dataTypes.length > 0) {
    sessionDataTypes = event.dataTypes;
  }

  // "leave" from the window: send leave to active zone
  if (event.eventType === 'leave') {
    if (activeZoneId) {
      const zone = zones.find(z => z.id === activeZoneId);
      zone?.onLeave?.(event);
      activeZoneId = null;
    }
    if (lastReportedOverZone) {
      lastReportedOverZone = false;
      invoke('set_drag_over_zone', { label: getCurrentWindow().label, overZone: false });
    }
    sessionDataTypes = [];
    return;
  }

  // For "over" events, attach the session data types
  const enriched = { ...event };
  if (enriched.dataTypes.length === 0) {
    enriched.dataTypes = sessionDataTypes;
  }

  const zone = findZoneAt(event.clientX, event.clientY);
  const zoneId = zone?.id ?? null;

  // Zone changed: send leave to old, enter to new
  if (zoneId !== activeZoneId) {
    if (activeZoneId) {
      const old = zones.find(z => z.id === activeZoneId);
      old?.onLeave?.(enriched);
    }
    activeZoneId = zoneId;
    if (zone) {
      zone.onEnter?.(enriched);
    }
  }

  // Report zone presence to Rust (only on transitions) so the drop target
  // can forward unhandled drops to the desktop shell.
  const isOverZone = zone !== null;
  if (isOverZone !== lastReportedOverZone) {
    lastReportedOverZone = isOverZone;
    invoke('set_drag_over_zone', { label: getCurrentWindow().label, overZone: isOverZone });
  }

  if (!zone) return;

  if (event.eventType === 'over' || event.eventType === 'enter') {
    zone.onOver?.(enriched);
  } else if (event.eventType === 'drop') {
    zone.onDrop?.(enriched);
    activeZoneId = null;
    sessionDataTypes = [];
  }
}

// ---------------------------------------------------------------------------
// Tauri event listener (auto-starts on import)
// ---------------------------------------------------------------------------

let listenerInitialized = false;

export function initNativeDragListener() {
  if (listenerInitialized) return;
  listenerInitialized = true;

  getCurrentWindow().listen<NativeDragEvent>('native-drag', (ev) => {
    handleNativeDrag(ev.payload);
  });

  // Test-only hook: webdriver specs drive the router by calling this directly
  // since Tauri's event bus isn't available from a detached browser context.
  window.__nativeDragDispatch = (payload: NativeDragEvent) => {
    handleNativeDrag(payload);
  };
}

// ---------------------------------------------------------------------------
// Initiate a drag from the frontend
// ---------------------------------------------------------------------------

export async function startDrag(data: DragData): Promise<'copy' | 'move' | 'none'> {
  const result = await invoke('start_native_drag', {
    request: {
      data_type: data.type,
      file_paths: data.filePaths ?? null,
      widget_json: data.widgetJson ?? null,
      effect_allowed: data.effectAllowed,
    },
  });
  return (result.effect as 'copy' | 'move' | 'none') ?? 'none';
}

// ---------------------------------------------------------------------------
// SolidJS primitive: createDropZone
// ---------------------------------------------------------------------------

/**
 * SolidJS reactive drop zone. Returns signals for UI feedback.
 *
 * Usage:
 * ```tsx
 * const { isOver, dataTypes } = createDropZone(
 *   () => containerRef,
 *   'my-widget-123',
 *   { onDrop: (e) => { ... } }
 * );
 * ```
 */
export function createDropZone(
  element: () => HTMLElement | undefined,
  id: string,
  handlers: {
    onEnter?: DragEventHandler;
    onOver?: DragEventHandler;
    onLeave?: DragEventHandler;
    onDrop?: DropHandler;
  },
): { isOver: Accessor<boolean>; dataTypes: Accessor<string[]> } {
  const [isOver, setIsOver] = createSignal(false);
  const [dataTypes, setDataTypes] = createSignal<string[]>([]);

  const cleanup = registerDropZone(id, element, {
    onEnter(e) {
      setIsOver(true);
      setDataTypes(e.dataTypes);
      handlers.onEnter?.(e);
    },
    onOver(e) {
      handlers.onOver?.(e);
    },
    onLeave(e) {
      setIsOver(false);
      setDataTypes([]);
      handlers.onLeave?.(e);
    },
    onDrop(e) {
      setIsOver(false);
      setDataTypes([]);
      return handlers.onDrop?.(e) ?? false;
    },
  });

  onCleanup(cleanup);
  return { isOver, dataTypes };
}
