# Themia: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Renders the Themia logo (`/logo.svg`), the name "Themia", and the app version prefixed with `v`.
- [S-02] Widget renders without a header (registry `noHeader: true`).
- [S-03] Version string populates on mount from Tauri `getVersion()`; empty string until resolved.

## Configuration
- [S-04] Config panel displays "No settings available." There are no configurable options.

## Data / backend
- [S-05] Calls `@tauri-apps/api/app` `getVersion()` once on mount.
