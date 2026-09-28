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
  // The seeded learner account is protected by SingleActiveSession and
  // single-use refresh-token rotation. Keep this focused slice serial so a
  // fresh per-test browser session cannot revoke a concurrently running test.
  fullyParallel: false,
  workers: 1,
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
      testIgnore: /tests\/e2e\/speaking-p0-verify\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        storageState: authStatePathsByProject['chromium-learner'],
      },
    },
    {
      // Keep the real-provider smoke out of the fully parallel shared learner
      // session. It performs its own sign-in and therefore must run only after
      // the deterministic learner project has finished, otherwise the backend's
      // single-active-session policy can revoke that project's session family.
      name: 'speaking-provider',
      dependencies: ['speaking-learner'],
      testMatch: /tests\/e2e\/speaking-p0-verify\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
      },
    },
  ],
});
