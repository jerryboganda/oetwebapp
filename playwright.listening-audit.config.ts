import { defineConfig, devices } from '@playwright/test';

// Prod-privileged pattern (playwright.prod-privileged.config.ts), scoped to the Listening audio-integrity
// learner-flow check. Viewports are set per-test inside the spec (desktop/tablet/mobile), not here.
export default defineConfig({
  testDir: './tests/e2e',
  testMatch: /prod-listening-audio-integrity\.spec\.ts/,
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: 0, // a real failure should surface fast, not double every test's wall-clock and blow the job timeout
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
