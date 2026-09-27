import { defineConfig } from 'vitest/config';

// Sidecar unit tests. Runs on GitHub Actions only (AGENTS.md); no network,
// no real engines, no credentials. better-sqlite3 is a native addon, so tests
// run in forked processes rather than worker threads.
export default defineConfig({
  test: {
    environment: 'node',
    include: ['tests/**/*.test.ts'],
    pool: 'forks',
    testTimeout: 15_000,
    restoreMocks: true,
    unstubEnvs: true,
  },
});
