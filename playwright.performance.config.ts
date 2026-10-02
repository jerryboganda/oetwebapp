import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000';
// The auth bootstrap (tests/e2e/fixtures/auth-bootstrap.ts) signs in with
// E2E_DEVICE_ID and binds the session's refresh token to it. Every browser
// request must present that same id: any other id fails the first
// /v1/auth/refresh closed (device mismatch, 403) and lands on /sign-in.
const performanceDeviceId = process.env.E2E_DEVICE_ID ?? 'e2e-playwright-harness';

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
    extraHTTPHeaders: {
      'X-OET-Device-Id': performanceDeviceId,
    },
    ignoreHTTPSErrors: baseURL.startsWith('https:'),
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: 'perf-setup',
      testMatch: /tests\/performance\/auth\.setup\.ts/,
      use: {
        extraHTTPHeaders: {
          'X-OET-Device-Id': performanceDeviceId,
        },
      },
    },
    {
      name: 'perf-unauth-chromium',
      testMatch: /tests\/performance\/browser-performance\.spec\.ts/,
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'perf-public-chromium',
      testMatch: /tests\/performance\/browser-performance\.spec\.ts/,
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'perf-public-pixel',
      testMatch: /tests\/performance\/browser-performance\.spec\.ts/,
      use: { ...devices['Pixel 7'] },
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
      name: 'perf-learner-firefox',
      dependencies: ['perf-setup'],
      testMatch: /tests\/performance\/browser-performance\.spec\.ts/,
      use: {
        ...devices['Desktop Firefox'],
        storageState: 'playwright/.auth/perf-learner.json',
      },
    },
    {
      name: 'perf-learner-webkit',
      dependencies: ['perf-setup'],
      testMatch: /tests\/performance\/browser-performance\.spec\.ts/,
      use: {
        ...devices['Desktop Safari'],
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
    {
      name: 'perf-admin-pixel',
      dependencies: ['perf-setup'],
      testMatch: /tests\/performance\/browser-performance\.spec\.ts/,
      use: {
        ...devices['Pixel 7'],
        storageState: 'playwright/.auth/perf-admin.json',
      },
    },
  ],
});
