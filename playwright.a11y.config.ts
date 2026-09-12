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
  // Accessibility is verified at the Apple form factors the app actually ships
  // to, not just on a desktop viewport. All of these descriptors resolve to
  // WebKit, which is the engine family WKWebView belongs to — and viewport
  // emulation is the only deterministic way to reach the small-screen class,
  // because hosted macOS runners carry no home-button iPhone simulator type and
  // no iOS 16.4 runtime. See docs/APPLE_COMPATIBILITY_MATRIX.md.
  projects: [
    {
      name: 'a11y-unauth',
      use: {
        ...devices['Desktop Chrome'],
      },
    },
    {
      // Smallest supported iPhone class (375x667).
      name: 'a11y-iphone-small',
      use: {
        ...devices['iPhone SE (3rd gen)'],
      },
    },
    {
      // Largest supported iPhone class (430x932, Dynamic Island safe areas).
      name: 'a11y-iphone-large',
      use: {
        ...devices['iPhone 15 Pro Max'],
      },
    },
    {
      name: 'a11y-ipad-portrait',
      use: {
        ...devices['iPad (gen 11)'],
      },
    },
    {
      name: 'a11y-ipad-landscape',
      use: {
        ...devices['iPad (gen 11) landscape'],
      },
    },
  ],
});
