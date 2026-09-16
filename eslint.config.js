// Flat ESLint config (ESLint 10 + typescript-eslint).
//
// Phase 0.2: the bug-class rules are at `warn` so they don't block the build
// yet, establishing the baseline. Phase 0.5 flips the ones we care about
// to `error` (scoping `no-explicit-any` to storage/sync/config/IPC dirs) and
// grandfathers the existing violations so only NEW ones fail CI.
import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import globals from 'globals';

export default tseslint.config(
  {
    ignores: [
      'dist/**',
      'target/**',
      'node_modules/**',
      'tests/e2e/**',
      'src-tauri/**',
      '**/*.config.*',
      'eslint.config.js',
    ],
  },
  {
    files: ['src/**/*.{ts,tsx}', 'widgets/**/*.{ts,tsx}'],
    extends: [js.configs.recommended],
    languageOptions: {
      parser: tseslint.parser,
      parserOptions: { ecmaVersion: 2022, sourceType: 'module' },
      globals: { ...globals.browser, ...globals.node },
    },
    plugins: { '@typescript-eslint': tseslint.plugin },
    rules: {
      // TS owns these, so turn off the JS-only versions to avoid false positives.
      'no-undef': 'off',
      'no-unused-vars': 'off',
      'no-redeclare': 'off',
      // False positive on SolidJS refs (`let el; <div ref={el}/>`: Solid
      // assigns `el` at runtime, which ESLint can't see).
      'no-unassigned-vars': 'off',
      // 0.5: strict everywhere, every flippable rule is now an ERROR, so any
      // new violation fails the gate. The existing violations were fixed with
      // real types / removed unused / no empty blocks, NOT suppressed.
      'no-useless-escape': 'error',
      'no-useless-assignment': 'error',

      // Bug-class rules (CLAUDE.md non-negotiables).
      'no-empty': ['error', { allowEmptyCatch: false }],
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
    },
  },
);
