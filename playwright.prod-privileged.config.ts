import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests/e2e',
  testMatch: /prod-smoke-privileged\.spec\.ts/,
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'output/playwright/prod-privileged-report', open: 'never' }],
  ],
  outputDir: 'output/playwright/prod-privileged-results',
  timeout: 60_000,
  expect: {
    timeout: 10_000,
  },
  use: {
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    viewport: { width: 1366, height: 900 },
  },
  projects: [
    {
      name: 'chromium-unauth',
      use: {
        ...devices['Desktop Chrome'],
      },
    },
  ],
});