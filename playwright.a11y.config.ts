import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.PLAYWRIGHT_BASE_URL?.trim() || 'http://localhost:3000';

export default defineConfig({
  testDir: './tests',
  testMatch: /tests\/a11y\/.*\.a11y\.spec\.ts/,
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 1,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'output/playwright/a11y-report', open: 'never' }],
  ],
  outputDir: 'output/playwright/a11y-results',
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
      name: 'a11y-unauth',
      use: {
        ...devices['Desktop Chrome'],
      },
    },
  ],
});
