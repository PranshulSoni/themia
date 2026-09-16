/**
 * Portal mount target for popups (context menu, color picker, dropdown, icon
 * picker).
 *
 * In the widget window, `main.tsx` creates a `.popup-layer` element scaled by
 * `--ui-scale` so popups follow the general UI scale rather than the
 * per-monitor widget scale applied to `#app.app-layer`. In the config window
 * there is no `.popup-layer`, so popups fall back to `document.body`, which is
 * itself scaled by `--ui-scale` via the `config-window` class.
 */
export function popupMount(): HTMLElement {
  return (document.querySelector('.popup-layer') as HTMLElement | null) ?? document.body;
}
