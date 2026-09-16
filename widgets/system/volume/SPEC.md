# Volume: SPEC

Experimental widget. Default-output-device master volume + mute via
WASAPI (`IAudioEndpointVolume`). Mirror `[S-NN]` tags in tests.

- **[S-01]** Backend commands: `volume_get` → `{ level: 0..1, muted }`,
  `volume_set(level)` (clamped 0..1), `volume_set_mute(muted)`.
- **[S-02]** COM is balanced per call (`ComGuard` = CoInitialize/CoUninitialize);
  all interfaces release on drop (RAII); no `.unwrap()`, errors return `Result`.
- **[S-03]** The widget polls `volume_get` every 1.5 s to reflect external changes,
  but suppresses polling while the user is dragging the slider.
- **[S-04]** Dragging the slider calls `volume_set` live; moving above 0 clears
  mute. The icon button toggles mute (`volume_set_mute`).
- **[S-05]** No output device / non-Windows / preview → the call fails silently
  and the widget shows its last/neutral state (never throws).
- **[S-06]** One horizontal row at every size: `[mute] [slider fills width] [%]`,
  the slider `flex: 1`. The body carries both `widget-body` (App.css card fill,
  `flex-direction: column`) and `volume-body`; the layout rule is scoped
  `.volume-widget .volume-body` (specificity 0,2,0) so it wins the cascade even
  though App.css loads after Volume.css. A bare `.volume-body` ties and loses,
  stacking the bar vertically.
