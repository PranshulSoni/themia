/// <reference types="vite/client" />

// Build-time constant injected by vite.config.ts `define`. True for dev / Tauri
// debug builds (incl. the e2e `--debug` build), false in a release binary, so
// code behind it (test-only hooks) is dead-code-eliminated from release.
declare const __THEMIA_TEST_HOOKS__: boolean;

// EyeDropper API (Chromium 95+ / WebView2): powers the color-picker pipette
// (themia-releases#46). Not yet in TypeScript's DOM lib, so declare the minimal
// surface we use. `Window.EyeDropper` is optional: guard before constructing.
interface EyeDropperResult {
  sRGBHex: string;
}
interface EyeDropperOpenOptions {
  signal?: AbortSignal;
}
declare class EyeDropper {
  open(options?: EyeDropperOpenOptions): Promise<EyeDropperResult>;
}
interface Window {
  EyeDropper?: typeof EyeDropper;
}
