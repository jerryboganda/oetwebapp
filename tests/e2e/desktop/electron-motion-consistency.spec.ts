import { mkdtemp, rm } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { _electron as electron, expect, test, type Page } from '@playwright/test';
import { bootstrapSessionForRole, hydrateSessionStorage } from '../fixtures/auth-bootstrap';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

/**
 * Desktop-specific motion consistency E2E tests.
 *
 * Validates that the motion system renders correctly, transitions are smooth,
 * and platform-specific tuning (data-runtime-kind="desktop") is active inside
 * the Electron desktop runtime.
 */

const baseURL = (process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000').replace(/\/$/, '');

type ElectronApp = Awaited<ReturnType<typeof electron.launch>>;

test.setTimeout(120_000);

async function waitForMotionSettled(page: Page, timeout = 800) {
  await page.waitForTimeout(timeout);
}

function cleanupDesktopProcesses(appDataRoot: string) {
  if (process.platform !== 'win32') return;
  const normalizedRoot = path.resolve(appDataRoot).replace(/'/g, "''");
  try {
    execFileSync('powershell', [
      '-NoProfile', '-Command',
      `Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'electron.exe' -and $_.CommandLine -like "*${normalizedRoot}*" } | ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch {} }`,
    ], { stdio: 'ignore', windowsHide: true });
  } catch { /* best-effort */ }
}

async function createAppDataRoot() {
  return mkdtemp(path.join(os.tmpdir(), 'oet-desktop-motion-'));
}

async function launchDesktop(appDataRoot: string): Promise<ElectronApp> {
  return electron.launch({
    args: ['.'],
    env: {
      ...process.env,
      ELECTRON_RENDERER_URL: baseURL,
      ELECTRON_APPDATA_ROOT: appDataRoot,
      ELECTRON_RUNTIME_CHANNEL: 'test',
      NODE_ENV: 'test',
    },
  });
}

async function closeDesktop(app: ElectronApp | null, appDataRoot: string) {
  if (!app) {
    cleanupDesktopProcesses(appDataRoot);
    return;
  }
  try {
    await Promise.race([app.close(), new Promise((r) => setTimeout(r, 5_000))]);
  } catch { /* closing a crashed app should not fail cleanup */ }
  cleanupDesktopProcesses(appDataRoot);
}

async function removeAppDataRoot(appDataRoot: string) {
  for (let attempt = 0; attempt < 10; attempt += 1) {
    try {
      await rm(appDataRoot, { recursive: true, force: true });
      return;
    } catch {
      cleanupDesktopProcesses(appDataRoot);
      await new Promise((r) => setTimeout(r, 250));
    }
  }
}

async function getDesktopPage(app: ElectronApp) {
  const page = await app.firstWindow();
  await page.waitForLoadState('domcontentloaded');
  await expect
    .poll(
      async () => {
        const bodyText = await page.locator('body').innerText().catch(() => '');
        return bodyText.replace(/\s+/g, ' ').trim().length;
      },
      { timeout: 15_000, message: 'Expected desktop renderer to paint visible UI.' },
    )
    .toBeGreaterThan(20);
  return page;
}

async function loadAbsolute(page: Page, targetPath: string) {
  await page.goto(`${baseURL}${targetPath}`, { waitUntil: 'domcontentloaded', timeout: 60_000 });
}

test.describe('Desktop motion consistency @desktop-motion', () => {
  test.describe.configure({ mode: 'serial' });

  test('desktop runtime sets data-runtime-kind="desktop" and motion tokens are tighter', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      const session = await bootstrapSessionForRole(request, 'learner');
      app = await launchDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);

      await hydrateSessionStorage(page, session);
      await loadAbsolute(page, '/');
      await waitForMotionSettled(page);

      // Verify desktop runtime kind is set
      const runtimeKind = await page.evaluate(() => document.documentElement.dataset.runtimeKind);
      expect(runtimeKind).toBe('desktop');

      // Verify desktop CSS custom properties are applied (tighter than web)
      const pressableDuration = await page.evaluate(() =>
        getComputedStyle(document.documentElement).getPropertyValue('--pressable-transition-duration').trim(),
      );
      expect(pressableDuration).toBe('160ms');

      const pageEnterDuration = await page.evaluate(() =>
        getComputedStyle(document.documentElement).getPropertyValue('--page-enter-duration').trim(),
      );
      expect(pageEnterDuration).toBe('280ms');

      await expect(page.locator('#main-content')).toBeVisible();
      expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('desktop route transitions render smoothly between pages', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      const session = await bootstrapSessionForRole(request, 'learner');
      app = await launchDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);

      await hydrateSessionStorage(page, session);

      // Dashboard → Settings → Dashboard round-trip
      await loadAbsolute(page, '/');
      await waitForMotionSettled(page);
      await expect(page.locator('#main-content')).toBeVisible();

      await loadAbsolute(page, '/settings/profile');
      await waitForMotionSettled(page);
      await expect(page.locator('#main-content')).toBeVisible();

      await loadAbsolute(page, '/');
      await waitForMotionSettled(page);
      await expect(page.locator('#main-content')).toBeVisible();

      expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('desktop window focus/blur applies lifecycle opacity transition', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      const session = await bootstrapSessionForRole(request, 'learner');
      app = await launchDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);

      await hydrateSessionStorage(page, session);
      await loadAbsolute(page, '/');
      await waitForMotionSettled(page);

      // Verify body has transition property set for desktop
      const bodyTransition = await page.evaluate(() =>
        getComputedStyle(document.body).getPropertyValue('transition').trim(),
      );
      expect(bodyTransition).toContain('opacity');

      expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('desktop reduced-motion collapses spatial movement', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      const session = await bootstrapSessionForRole(request, 'learner');
      app = await launchDesktop(appDataRoot);
      const page = await getDesktopPage(app);

      await page.emulateMedia({ reducedMotion: 'reduce' });

      const diagnostics = observePage(page);
      await hydrateSessionStorage(page, session);
      await loadAbsolute(page, '/');
      await waitForMotionSettled(page, 400);

      await expect(page.locator('#main-content')).toBeVisible();

      // Verify reduced-motion CSS overrides are active
      const pressableDuration = await page.evaluate(() =>
        getComputedStyle(document.documentElement).getPropertyValue('--pressable-transition-duration').trim(),
      );
      expect(pressableDuration).toBe('1ms');

      expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('desktop admin dense surfaces render with motion without errors', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      const session = await bootstrapSessionForRole(request, 'admin');
      app = await launchDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);

      await hydrateSessionStorage(page, session);

      // Navigate through admin dense surfaces
      await loadAbsolute(page, '/admin');
      await waitForMotionSettled(page);
      await expect(page.locator('#main-content')).toBeVisible();

      await loadAbsolute(page, '/admin/users');
      await waitForMotionSettled(page);
      await expect(page.locator('#main-content')).toBeVisible();

      await loadAbsolute(page, '/admin/billing');
      await waitForMotionSettled(page);
      await expect(page.locator('#main-content')).toBeVisible();

      expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('desktop tutor review surfaces render with motion without errors', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      const session = await bootstrapSessionForRole(request, 'expert');
      app = await launchDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);

      await hydrateSessionStorage(page, session);

      await loadAbsolute(page, '/expert');
      await waitForMotionSettled(page);
      await expect(page.locator('#main-content')).toBeVisible();

      await loadAbsolute(page, '/expert/queue');
      await waitForMotionSettled(page);
      await expect(page.locator('#main-content')).toBeVisible();

      expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });
});
