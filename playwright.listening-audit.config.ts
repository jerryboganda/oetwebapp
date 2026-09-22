import { defineConfig, devices } from '@playwright/test';

// Prod-privileged pattern (playwright.prod-privileged.config.ts), scoped to the Listening audio-integrity
// learner-flow check. Viewports are set per-test inside the spec (desktop/tablet/mobile), not here.
export default defineConfig({
  testDir: './tests/e2e',
  testMatch: /prod-listening-audio-integrity\.spec\.ts/,
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'output/playwright/listening-audit-report', open: 'never' }],
  ],
  outputDir: 'output/playwright/listening-audit-results',
  timeout: 120_000,
  expect: { timeout: 10_000 },
  use: {
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
    serviceWorkers: 'block',
  },
  projects: [{ name: 'chromium-unauth', use: { ...devices['Desktop Chrome'] } }],
});
