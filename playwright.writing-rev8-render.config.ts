import { defineConfig, devices } from '@playwright/test';

// Owner Rev8 §12.3 / §19.2 Track A: rendered-spacing verification of the
// candidate-facing Grounded Model Answer on production result pages.
export default defineConfig({
  testDir: './tests/e2e',
  testMatch: /writing-rev8-render-verify\.spec\.ts/,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  outputDir: 'output/playwright/writing-rev8-render-results',
  timeout: 600_000,
  expect: { timeout: 20_000 },
  use: {
    baseURL: process.env.WRITING_REV8_BASE_URL || 'https://app.oetwithdrhesham.co.uk',
    trace: 'off',
    screenshot: 'off',
    video: 'off',
    serviceWorkers: 'block',
    viewport: { width: 1366, height: 900 },
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
