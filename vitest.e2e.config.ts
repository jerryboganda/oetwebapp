import { defineConfig } from 'vitest/config';
import path from 'path';

export default defineConfig({
  test: {
    environment: 'node',
    globals: true,
    testTimeout: 20000,
    include: [
      'tests/e2e-tiers/tier1-feature-coverage.test.ts',
      'tests/e2e-tiers/tier2-boundary-corner.test.ts',
      'tests/e2e-tiers/tier3-cross-feature-combinations.test.ts',
      'tests/e2e-tiers/tier4-real-world-scenarios.test.ts',
      'tests/unit/m1-exam-engines.test.ts',
      'tests/unit/m1-challenger-stress.test.ts',
      'tests/unit/m1-challenger-deep-stress.test.ts',
      'tests/unit/m1-challenger-timer-lifecycle.test.ts',
      'tests/unit/toefl-scoring.test.ts',
      'tests/unit/exam-family-scoring.test.ts',
      'tests/unit/m3-challenger-stress.test.ts',
      'tests/unit/m3-challenger-2-zero-deviation.test.ts',
      'tests/unit/m4-challenger-tier5-stress.test.ts',
      'lib/__tests__/toefl-scoring.test.ts',
      'lib/__tests__/exam-family-scoring.test.ts',
    ],
  },
  resolve: {
    alias: {
      '@': path.resolve(__dirname, '.'),
    },
  },
});
