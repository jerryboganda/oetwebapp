import { defineConfig, devices } from '@playwright/test';
import { authStatePathsByProject } from './tests/e2e/fixtures/auth';

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000';

export default defineConfig({
  testDir: './tests/e2e',
  testIgnore: [/tests\/e2e\/desktop\//, /tests\/e2e\/prod-smoke-privileged\.spec\.ts/],
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'output/playwright/report', open: 'never' }],
  ],
  outputDir: 'output/playwright/test-results',
  // Bumped from 60s to absorb Next.js dev-mode cold-compile spikes that occur
  // under sustained matrix load. Solo-test runs typically finish in <45s; the
  // larger budget removes flakes when the dev server is recompiling routes.
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
        storageState: authStatePathsByProject['chromium-learner'],
      },
    },
    {
      name: 'chromium-expert',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Chrome'],
        storageState: authStatePathsByProject['chromium-expert'],
      },
    },
    {
      name: 'chromium-admin',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Chrome'],
        storageState: authStatePathsByProject['chromium-admin'],
      },
    },
    {
      name: 'firefox-learner',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Firefox'],
        storageState: authStatePathsByProject['firefox-learner'],
      },
    },
    {
      name: 'firefox-expert',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Firefox'],
        storageState: authStatePathsByProject['firefox-expert'],
      },
    },
    {
      name: 'firefox-admin',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Firefox'],
        storageState: authStatePathsByProject['firefox-admin'],
      },
    },
    {
      name: 'webkit-learner',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Safari'],
        storageState: authStatePathsByProject['webkit-learner'],
      },
    },
    {
      name: 'webkit-expert',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Safari'],
        storageState: authStatePathsByProject['webkit-expert'],
      },
    },
    {
      name: 'webkit-admin',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Safari'],
        storageState: authStatePathsByProject['webkit-admin'],
      },
    },
    {
      name: 'mobile-chromium-learner',
      dependencies: ['setup'],
      use: {
        ...devices['Pixel 7'],
        storageState: authStatePathsByProject['mobile-chromium-learner'],
      },
    },
    {
      name: 'mobile-webkit-learner',
      dependencies: ['setup'],
      use: {
        ...devices['iPhone 14'],
        storageState: authStatePathsByProject['mobile-webkit-learner'],
      },
    },
    {
      name: 'sydney-learner',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Chrome'],
        storageState: authStatePathsByProject['sydney-learner'],
        timezoneId: 'Australia/Sydney',
        locale: 'en-AU',
      },
    },
  ],
});
