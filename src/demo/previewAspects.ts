// Per-widget preview aspect (cols × rows) overrides for the Add-Widget picker.
//
// The picker renders each widget at a "natural" grid span so the preview looks
// like the widget does on the desktop. Left to their own `defaultSpan`, every
// widget previews at a different aspect ratio, which looks inconsistent. This
// map lets us snap widgets onto a small shared set of aspect ratios.
//
// `previewAspects.json` is the committed source of truth. In a DEV build the
// inline aspect tuner in the picker preview pane (DevAspectPanel) writes changes
// straight back to that JSON via the Vite dev-server endpoint (see
// `dev-preview-aspects` plugin in vite.config.ts), so whatever you dial in
// becomes the new committed default, no copy-paste step.

import { createStore, unwrap } from 'solid-js/store';
import seed from './previewAspects.json';

export interface PreviewSpan { cols: number; rows: number; }

const [aspects, setAspects] = createStore<Record<string, PreviewSpan>>({
  ...(seed as Record<string, PreviewSpan>),
});

export { aspects };

/** Resolved preview span for a widget: tuned override, else its grid default. */
export function previewSpanFor(type: string, fallback: PreviewSpan): PreviewSpan {
  return aspects[type] ?? fallback;
}

// Widget defaults now live in pixels (registry `defaultSize`); the picker preview
// still reasons in a small cols×rows aspect, so derive one from the px size at a
// nominal cell of 110px (the placement base). Only used as the preview fallback
// before any tuned override exists.
const PREVIEW_CELL_PX = 110;
export function spanFromSize(px: { width: number; height: number }): PreviewSpan {
  return {
    cols: Math.max(1, Math.round(px.width / PREVIEW_CELL_PX)),
    rows: Math.max(1, Math.round(px.height / PREVIEW_CELL_PX)),
  };
}

/**
 * Set (and, in dev, persist) a widget's preview aspect. The whole map is
 * written back to previewAspects.json so the file always reflects the live
 * state. No-op on the network side in production builds.
 */
export function setPreviewAspect(type: string, span: PreviewSpan): void {
  setAspects(type, { cols: span.cols, rows: span.rows });
  if (import.meta.env.DEV) {
    void fetch('/__preview-aspects', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(unwrap(aspects)),
    }).catch(() => { /* dev-only convenience; ignore failures */ });
  }
}
