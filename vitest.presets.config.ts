// Live preset-health suite ([S-48], src/connect/presets.network.test.ts).
//
// Its own config because the default gate must stay offline: vitest.config.ts
// excludes `**/*.network.test.ts`, and this one includes only that file. Run
// with `npm run test:presets` — see the header of the suite for what it checks.
import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/connect/presets.network.test.ts'],
    // One slow provider must not stall the rest; each test carries its own
    // timeout, and the endpoints are independent.
    testTimeout: 25_000,
  },
});
