import { defineConfig, devices } from '@playwright/test';
import { authStatePathsByProject } from './tests/e2e/fixtures/auth';

const baseURL = process.env.PLAYWRIGHT_BASE_URL?.trim() || 'http://localhost:3000';

/**
 * Speaking-only E2E slice. Runs the same auth.setup bootstrap as the full
 * matrix (playwright.config.ts) but only the speaking-*.spec.ts suites, on
 * Chromium, to keep the nightly/dispatch workflow fast.
 */
export default defineConfig({
  testDir: './tests/e2e',
  testMatch: /tests\/e2e\/speaking-.*\.spec\.ts/,
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 1,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'output/playwright/speaking-e2e-report', open: 'never' }],
  ],
  outputDir: 'output/playwright/speaking-e2e-results',
  timeout: 120_000,
  expect: {
    timeout: 15_000,
  },
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    viewport: { width: 1366, height: 900 },
  },
  projects: [
    {
      name: 'setup',
      testMatch: /tests\/e2e\/setup\/auth\.setup\.ts/,
    },
    {
      name: 'speaking-learner',
      dependencies: ['setup'],
      testMatch: /tests\/e2e\/speaking-.*\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        storageState: authStatePathsByProject['chromium-learner'],
      },
    },
  ],
});
