// Live brand-mark suite ([S-47], src/connect/brandIcons.network.test.ts).
//
// Its own config for the same reason as vitest.presets.config.ts: the default
// gate must stay offline, so vitest.config.ts excludes `**/*.network.test.ts`
// and this one includes only that file. Run with `npm run test:brands`.
import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/connect/brandIcons.network.test.ts'],
    // One slow CDN must not stall the rest; each test carries its own timeout
    // and the hosts are independent.
    testTimeout: 25_000,
  },
});
