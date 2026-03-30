import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000';

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'output/playwright/report', open: 'never' }],
  ],
  outputDir: 'output/playwright/test-results',
  timeout: 60_000,
  expect: {
    timeout: 10_000,
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
      name: 'chromium-unauth',
      use: {
        ...devices['Desktop Chrome'],
      },
    },
    {
      name: 'chromium-learner',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'playwright/.auth/learner.json',
      },
    },
    {
      name: 'chromium-expert',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'playwright/.auth/expert.json',
      },
    },
    {
      name: 'chromium-admin',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'playwright/.auth/admin.json',
      },
    },
    {
      name: 'firefox-learner',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Firefox'],
        storageState: 'playwright/.auth/learner.json',
      },
    },
    {
      name: 'firefox-expert',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Firefox'],
        storageState: 'playwright/.auth/expert.json',
      },
    },
    {
      name: 'firefox-admin',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Firefox'],
        storageState: 'playwright/.auth/admin.json',
      },
    },
    {
      name: 'webkit-learner',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Safari'],
        storageState: 'playwright/.auth/learner.json',
      },
    },
    {
      name: 'webkit-expert',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Safari'],
        storageState: 'playwright/.auth/expert.json',
      },
    },
    {
      name: 'webkit-admin',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Safari'],
        storageState: 'playwright/.auth/admin.json',
      },
    },
    {
      name: 'mobile-chromium-learner',
      dependencies: ['setup'],
      use: {
        ...devices['Pixel 7'],
        storageState: 'playwright/.auth/learner.json',
      },
    },
    {
      name: 'mobile-webkit-learner',
      dependencies: ['setup'],
      use: {
        ...devices['iPhone 14'],
        storageState: 'playwright/.auth/learner.json',
      },
    },
    {
      name: 'sydney-learner',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'playwright/.auth/learner.json',
        timezoneId: 'Australia/Sydney',
        locale: 'en-AU',
      },
    },
  ],
});
