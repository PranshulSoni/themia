# Themia

**Beautiful, transparent widgets for your Windows desktop.**

[Website](https://themia.app) · [Download](https://github.com/crazy-rabbit-software/themia/releases/latest) · [Changelog](https://themia.app/changelog) · [Report a bug](https://github.com/crazy-rabbit-software/themia/issues)

<img src=".github/assets/hero.png" alt="Themia widgets on a Windows desktop" width="900" />

<div align="center">

[![version](https://img.shields.io/github/v/release/crazy-rabbit-software/themia?style=flat&labelColor=1e1e1e&color=60cdff&label=version)](https://github.com/crazy-rabbit-software/themia/releases/latest)
[![released](https://img.shields.io/github/release-date/crazy-rabbit-software/themia?style=flat&labelColor=1e1e1e&color=60cdff&label=released)](https://github.com/crazy-rabbit-software/themia/releases/latest)
[![downloads](https://img.shields.io/github/downloads/crazy-rabbit-software/themia/total?style=flat&labelColor=1e1e1e&color=60cdff&label=downloads)](https://github.com/crazy-rabbit-software/themia/releases)
[![stars](https://img.shields.io/github/stars/crazy-rabbit-software/themia?style=flat&labelColor=1e1e1e&color=60cdff)](https://github.com/crazy-rabbit-software/themia/stargazers)
[![open issues](https://img.shields.io/github/issues/crazy-rabbit-software/themia?style=flat&labelColor=1e1e1e&color=60cdff&label=open%20issues)](https://github.com/crazy-rabbit-software/themia/issues)
[![license](https://img.shields.io/badge/license-Elastic%202.0-60cdff?style=flat&labelColor=1e1e1e)](LICENSE)

</div>

---

## Widgets

- **System**: CPU · RAM · GPU · Disk · Network · Battery
- **Productivity**: Email · Calendar · Contacts · Todo · Notes · Folder
- **Information**: Weather · Forecast · Stocks · GitHub · RSS
- **Utilities**: Clock · Music · Color Picker

## Why Themia

- **Lightweight**: under 10 MB, barely touches your RAM
- **Fully customizable**: colors, gaps, corners, transparency
- **Multiple screens**: context layouts for home, work, school
- **Feels native**: drag & drop, global shortcuts, right-click menus

## Download

Get the latest installer from the [**Releases**](https://github.com/crazy-rabbit-software/themia/releases/latest) page. Windows 10 & 11.

- **Free**: up to 10 widgets on 2 screens
- **Pro ($19, one-time)**: unlimited widgets & screens, every widget type, up to 5 devices

## Source available

Themia's source code is public under the [Elastic License 2.0](LICENSE). In short:

- You may read, build, change and share the code, including your modified versions.
- You may not remove, disable or work around the Pro license check, or remove the features it unlocks.
- You may not remove the license or copyright notices, or offer Themia as a hosted service.

That makes Themia source available, not open source. The [LICENSE](LICENSE) file is the binding text; this summary is not.

The Themia name and logo are not covered by the license. If you distribute a modified build, give it a different name and icon.

## Building from source

Themia is a [Tauri 2](https://v2.tauri.app) app: a Rust backend with a SolidJS and TypeScript frontend. Widgets render as transparent, always-on-desktop windows (`WS_EX_NOACTIVATE`).

```bash
# One-time setup
# Rust + Tauri prerequisites: https://v2.tauri.app/start/prerequisites/
npm install

npm run dev     # run the app with hot reload
npm run build   # installer at target/release/bundle/nsis/
```

Account sign-in uses public OAuth client IDs that ship in the source. Google sign-in also needs `THEMIA_GOOGLE_CLIENT_SECRET` set at build time; without it, Google stays unavailable in your build. If you distribute a build, register your own OAuth clients.

### Quality checks

```bash
npm run check           # typecheck + lint + unit tests (fast gate)
npm run lint:rust       # cargo clippy --workspace -- -D warnings
npm run coverage:spec   # SPEC.md [S-NN] tag coverage report
```

### Integration tests

The e2e suite drives the built debug binary through `tauri-driver`, which proxies to `msedgedriver` because Tauri on Windows renders with WebView2. Install `msedgedriver` matching your Edge version on your PATH ([download](https://developer.microsoft.com/microsoft-edge/tools/webdriver/)), then:

```bash
cargo install tauri-driver --locked

npm run test:e2e:build   # build the debug test binary (rerun after Rust changes)
npm run test:e2e         # run all specs
npm run test:e2e:spec -- tests/e2e/specs/04-ctx-menu.spec.ts   # run a subset
```

## Releasing

Maintainers only. Releases need SimplySign Desktop running and signed in, the Windows SDK, and a `.env` with `RELEASE_TOKEN`, `TAURI_SIGNING_PRIVATE_KEY`, `TAURI_SIGNING_PRIVATE_KEY_PASSWORD` and `THEMIA_GOOGLE_CLIENT_SECRET`.

```bash
npm run release -- -Bump patch                # bump, build, sign, tag, publish
npm run release -- -Version 1.2.3             # explicit version
npm run release -- -Bump patch -SkipPublish   # build and sign locally only
```

[`scripts/release-local.ps1`](scripts/release-local.ps1) runs the e2e gate, bumps the version, builds and signs the installer, commits, tags and pushes to `main`, then publishes the installer and the updater's `latest.json` as a GitHub release in this repository. Release notes come from the `feat:` and `fix:` commits since the previous tag.

## Contributing

Bug reports and widget ideas are welcome through the [issue forms](https://github.com/crazy-rabbit-software/themia/issues/new/choose). For code contributions, read [CONTRIBUTING.md](CONTRIBUTING.md) first.

## License

Copyright © 2026 Nathaniel Walser. Licensed under the [Elastic License 2.0](LICENSE).

---

<div align="center">

[Website](https://themia.app) · [Changelog](https://themia.app/changelog) · [Privacy](https://themia.app/legal/privacy) · [Request a feature](https://github.com/crazy-rabbit-software/themia/issues/new/choose)

</div>
