import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000';

export default defineConfig({
  testDir: '.',
  testMatch: /tests\/performance\/.*\.spec\.ts/,
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'output/performance/browser-report', open: 'never' }],
  ],
  outputDir: 'output/performance/browser-results',
  timeout: 120_000,
  expect: { timeout: 30_000 },
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: 'perf-setup',
      testMatch: /tests\/performance\/auth\.setup\.ts/,
    },
    {
      name: 'perf-unauth-chromium',
      testMatch: /tests\/performance\/browser-performance\.spec\.ts/,
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'perf-learner-chromium',
      dependencies: ['perf-setup'],
      testMatch: /tests\/performance\/browser-performance\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'playwright/.auth/perf-learner.json',
      },
    },
    {
      name: 'perf-learner-pixel',
      dependencies: ['perf-setup'],
      testMatch: /tests\/performance\/browser-performance\.spec\.ts/,
      use: {
        ...devices['Pixel 7'],
        storageState: 'playwright/.auth/perf-learner.json',
      },
    },
    {
      name: 'perf-learner-iphone',
      dependencies: ['perf-setup'],
      testMatch: /tests\/performance\/browser-performance\.spec\.ts/,
      use: {
        ...devices['iPhone 14'],
        storageState: 'playwright/.auth/perf-learner.json',
      },
    },
    {
      name: 'perf-admin-chromium',
      dependencies: ['perf-setup'],
      testMatch: /tests\/performance\/browser-performance\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'playwright/.auth/perf-admin.json',
      },
    },
  ],
});
