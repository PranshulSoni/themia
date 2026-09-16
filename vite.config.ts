import { defineConfig, type Plugin } from 'vite';
import solid from 'vite-plugin-solid';
import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

// Dev-only: lets the Add-Widget picker's inline aspect tuner persist widget
// preview aspect ratios straight into the committed source (previewAspects.json)
// so tuning becomes the new default. Only mounted under `vite serve`, never in
// a production build, so there's no way to write files from a shipped app.
function devPreviewAspects(): Plugin {
  return {
    name: 'dev-preview-aspects',
    apply: 'serve',
    configureServer(server) {
      server.middlewares.use('/__preview-aspects', (req, res) => {
        if (req.method !== 'POST') { res.statusCode = 405; res.end(); return; }
        let body = '';
        req.on('data', (c) => { body += c; });
        req.on('end', async () => {
          try {
            const parsed = JSON.parse(body);
            const file = resolve(process.cwd(), 'src/demo/previewAspects.json');
            await writeFile(file, JSON.stringify(parsed, null, 2) + '\n', 'utf8');
            res.statusCode = 200; res.end('ok');
          } catch (e) {
            res.statusCode = 400; res.end(String(e));
          }
        });
      });
    },
  };
}

export default defineConfig(({ command }) => ({
  // Build-time gate for test-only hooks (1.8/B-sec). True for `vite serve` (dev +
  // browser harnesses) and for Tauri debug builds: `tauri dev` and the e2e
  // `tauri build --debug` both export TAURI_ENV_DEBUG=true. A real release
  // (`tauri build`, TAURI_ENV_DEBUG=false) makes it `false`, so any code behind
  // it (e.g. the WebDriver license pro-unlock) is dead-code-eliminated and never
  // ships in a release binary.
  define: {
    __THEMIA_TEST_HOOKS__: JSON.stringify(
      command === 'serve' || process.env.TAURI_ENV_DEBUG === 'true',
    ),
  },
  plugins: [
    solid({ extensions: ['.tsx'] }),
    devPreviewAspects(),
  ],
  clearScreen: false,
  server: {
    strictPort: true,
    watch: {
      // Vite watches the project root and only auto-ignores node_modules/.git.
      // It has no idea about Rust's target/ dir (~90k files for this workspace),
      // so on startup it registers an OS watch handle per directory, which
      // blocks the dev server ~40s (the blank-webview hang). Ignore build dirs.
      ignored: [
        '**/target/**',
        '**/repro/**',
        '**/dist/**',
        '**/node_modules/**',
      ],
    },
  },
  build: {
    outDir: 'dist',
  },
}));
