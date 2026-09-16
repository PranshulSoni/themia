// ---------------------------------------------------------------------------
// Tray icon rasterization (Tray.SPEC [S-03])
// ---------------------------------------------------------------------------
//
// A tray icon is a bitmap, but a widget's icon is a lucide-solid component in
// the registry. Rather than maintain a second, hand-drawn icon set that would
// drift from the picker, we render the registry's own icon and rasterize it:
// mount → serialize → <img> → canvas → RGBA. One icon source, always in step.
//
// The bitmap is produced at exactly the size the shell asks for (`SM_CXSMICON`)
// so Windows never resamples it, and in whichever colour the taskbar theme
// leaves visible.

import { render } from 'solid-js/web';
import type { JSX } from 'solid-js';
import type { TrayEnv } from './ipc.ts';

/**
 * Minimum stroke width for a tray-sized icon. The registry draws at 20px with a
 * 1.5 stroke, which at 16 physical px lands under one device pixel and reads as
 * a grey smudge. Thickening is a rasterization concern only — the registry icon
 * itself is untouched.
 */
const MIN_STROKE = 2;

/** The palette a tray icon may use: the taskbar is one flat tone, so a single
 *  opaque colour is both what reads best and what the shell's own icons do. */
export function trayIconColor(lightTheme: boolean): string {
  return lightTheme ? '#000000' : '#ffffff';
}

/**
 * Is this a usable answer from `tray_env`?
 *
 * It crosses the IPC boundary, so it is checked rather than trusted: the
 * preview/demo seam resolves `undefined`, and an unvalidated read of that blew
 * up mid-sync with an unhandled rejection instead of degrading to "no tray
 * icons this pass". A zero or fractional icon size is equally unusable — it
 * would produce a bitmap the backend rejects.
 */
export function isTrayEnv(v: unknown): v is TrayEnv {
  if (!v || typeof v !== 'object') return false;
  const e = v as Partial<TrayEnv>;
  return typeof e.lightTheme === 'boolean'
    && typeof e.iconSize === 'number'
    && Number.isInteger(e.iconSize)
    && e.iconSize > 0;
}

/**
 * Normalize a rendered lucide SVG into a standalone document that resolves to
 * the same drawing outside the app's CSS: an explicit size, an explicit colour
 * (lucide strokes with `currentColor`, which has no inherited value once the
 * element leaves the DOM), and a stroke heavy enough to survive at icon size.
 *
 * Mutates and returns markup for a CLONE, so the caller's element is untouched.
 * Pure with respect to the app: given the same element it always produces the
 * same string.
 */
export function prepareTrayIconSvg(source: SVGElement, size: number, color: string): string {
  const svg = source.cloneNode(true) as SVGElement;
  svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
  svg.setAttribute('width', String(size));
  svg.setAttribute('height', String(size));
  // `currentColor` has nothing to inherit from in a standalone document, so it
  // would fall back to black — invisible on the default dark taskbar.
  svg.setAttribute('color', color);
  svg.setAttribute('stroke', color);

  const stroke = Number(svg.getAttribute('stroke-width'));
  if (!Number.isFinite(stroke) || stroke < MIN_STROKE) {
    svg.setAttribute('stroke-width', String(MIN_STROKE));
  }
  return new XMLSerializer().serializeToString(svg);
}

/** Mount a registry icon off-document and hand back its `<svg>`, plus the
 *  disposer for the reactive root it lives in. */
function mountIcon(icon: () => JSX.Element): { svg: SVGElement | null; dispose: () => void } {
  const host = document.createElement('div');
  const dispose = render(() => icon(), host);
  return { svg: host.querySelector('svg'), dispose };
}

/**
 * Rasterize a registry icon to an RGBA8 bitmap of `size × size`, row-major, as
 * a plain number array (the shape the IPC boundary carries).
 *
 * Returns null rather than a blank bitmap when anything fails: `tray_sync`
 * rejects a wrong-sized buffer, and a widget whose icon could not be drawn is
 * better left out of that sync than pushed as an invisible square.
 */
export async function rasterizeTrayIcon(
  icon: () => JSX.Element,
  size: number,
  color: string,
): Promise<number[] | null> {
  if (!Number.isInteger(size) || size <= 0) return null;

  const { svg, dispose } = mountIcon(icon);
  let markup: string;
  try {
    if (!svg) return null;
    markup = prepareTrayIconSvg(svg, size, color);
  } finally {
    dispose();
  }

  const url = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(markup)}`;
  const image = new Image(size, size);
  image.src = url;
  try {
    await image.decode();
  } catch {
    // A malformed SVG (or a browser that refuses the data URI) resolves here.
    return null;
  }

  const canvas = document.createElement('canvas');
  canvas.width = size;
  canvas.height = size;
  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  if (!ctx) return null;
  ctx.clearRect(0, 0, size, size);
  ctx.drawImage(image, 0, 0, size, size);
  // A data-URI source is same-origin, so this never taints the canvas.
  return Array.from(ctx.getImageData(0, 0, size, size).data);
}
