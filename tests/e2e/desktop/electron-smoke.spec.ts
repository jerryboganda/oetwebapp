import { mkdtemp, rm } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { _electron as electron, expect, test } from '@playwright/test';
import { bootstrapSessionForRole, hydrateSessionStorage } from '../fixtures/auth-bootstrap';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

const baseURL = (process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000').replace(/\/$/, '');

type ElectronApp = Awaited<ReturnType<typeof electron.launch>>;
const warmedRendererRoutes = new Set<string>();

test.setTimeout(180_000);

async function createAppDataRoot() {
  return mkdtemp(path.join(os.tmpdir(), 'oet-desktop-e2e-'));
}

function wait(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function removeAppDataRoot(appDataRoot: string) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    try {
      await rm(appDataRoot, { recursive: true, force: true });
      return;
    } catch (error) {
      const code = error instanceof Error && 'code' in error ? String(error.code) : '';
      if (code !== 'EBUSY' && code !== 'EPERM') {
        throw error;
      }

      cleanupDesktopProcesses(appDataRoot);
      await wait(250);
    }
  }

  try {
    await rm(appDataRoot, { recursive: true, force: true });
  } catch {
    // Temp profile cleanup should never fail the desktop suite on Windows.
  }
}

async function launchDesktop(appDataRoot: string): Promise<ElectronApp> {
  return electron.launch({
    args: ['.'],
    env: {
      ...process.env,
      ELECTRON_ALLOW_BASIC_TEXT_SECRET_STORAGE: 'true',
      ELECTRON_RENDERER_URL: baseURL,
      ELECTRON_APPDATA_ROOT: appDataRoot,
      ELECTRON_RUNTIME_CHANNEL: 'test',
      NODE_ENV: 'test',
    },
  });
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
      { timeout: 15_000, message: 'Expected the desktop renderer to paint visible UI before interaction.' },
    )
    .toBeGreaterThan(20);
  return page;
}

function cleanupDesktopProcesses(appDataRoot: string) {
  if (process.platform !== 'win32') {
    return;
  }

  const normalizedRoot = path.resolve(appDataRoot).replace(/'/g, "''");

  try {
    execFileSync('powershell', [
      '-NoProfile',
      '-Command',
      `
      $target = '${normalizedRoot}'
      Get-CimInstance Win32_Process |
        Where-Object { $_.Name -eq 'electron.exe' -and $_.CommandLine -like "*$target*" } |
        ForEach-Object {
          try {
            Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop
          } catch {
          }
        }
      `,
    ], {
      stdio: 'ignore',
      windowsHide: true,
    });
  } catch {
    // Best-effort cleanup only.
  }
}

function killProcessTree(pid: number) {
  if (process.platform === 'win32') {
    try {
      execFileSync('taskkill.exe', ['/PID', String(pid), '/T', '/F'], {
        stdio: 'ignore',
        windowsHide: true,
      });
    } catch {
      // Best-effort cleanup only.
    }
    return;
  }

  try {
    process.kill(pid, 'SIGKILL');
  } catch {
    // Best-effort cleanup only.
  }
}

async function closeDesktop(app: ElectronApp | null, appDataRoot: string) {
  if (!app) {
    cleanupDesktopProcesses(appDataRoot);
    return;
  }

  const processHandle = typeof app.process === 'function' ? app.process() : null;

  try {
    await Promise.race([
      app.close(),
      wait(5_000),
    ]);
  } catch {
    // Closing a crashed app should not fail cleanup.
  }

  if (processHandle?.pid) {
    for (let attempt = 0; attempt < 10; attempt += 1) {
      try {
        process.kill(processHandle.pid, 0);
        await wait(250);
      } catch {
        return;
      }
    }

    try {
      killProcessTree(processHandle.pid);
    } catch {
      // Best-effort cleanup only.
    }
  }

  cleanupDesktopProcesses(appDataRoot);
}

async function loadAbsolute(page: Parameters<typeof observePage>[0], targetPath: string) {
  const targetUrl = `${baseURL}${targetPath}`;

  if (!warmedRendererRoutes.has(targetUrl)) {
    try {
      await fetch(targetUrl, {
        headers: {
          Accept: 'text/html',
        },
      });
      warmedRendererRoutes.add(targetUrl);
    } catch {
      // If the warm-up probe fails we still let Playwright drive the real navigation.
    }
  }

  // Desktop E2E runs against the local Next.js dev server, so the first hit on a
  // route can include an on-demand compile that is slower than Playwright's default.
  await page.goto(targetUrl, { waitUntil: 'domcontentloaded', timeout: 90_000 });
}

test.describe('Electron desktop shell', () => {
  test.describe.configure({ mode: 'serial' });

  test('boots to the sign-in flow and exposes the secure desktop bridge', async ({}, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      app = await launchDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);

      await expect(page.getByRole('heading', { name: /login to your account|access your workspace/i })).toBeVisible();
      await expect.poll(
        async () => await page.evaluate(() => document.documentElement.dataset.runtimeKind ?? null),
        { timeout: 5_000, message: 'Expected the Electron renderer to mark itself as a desktop runtime.' },
      ).toBe('desktop');

      const runtimeInfo = await page.evaluate(() => window.desktopBridge!.runtime.info());
      expect(runtimeInfo.windowState!.isVisible).toBe(true);
      expect(runtimeInfo.windowState!.isMinimized).toBe(false);
      await expect.poll(
        async () => await page.evaluate(() => document.documentElement.dataset.appActive ?? null),
        { timeout: 5_000, message: 'Expected the desktop lifecycle bridge to mark the window active.' },
      ).toBe('true');

      const bridgeDetails = await page.evaluate(() => ({
        hasBridge: typeof window.desktopBridge === 'object' && window.desktopBridge !== null,
        hasOpenExternal: typeof window.desktopBridge?.openExternal === 'function',
        hasSecureSecrets: typeof window.desktopBridge?.secureSecrets?.status === 'function',
        electronVersion: window.desktopBridge?.versions?.electron ?? null,
      }));

      expect(bridgeDetails.hasBridge).toBe(true);
      expect(bridgeDetails.hasOpenExternal).toBe(true);
      expect(bridgeDetails.hasSecureSecrets).toBe(true);
      expect(bridgeDetails.electronVersion).toBeTruthy();

      expectNoSevereClientIssues(diagnostics);
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('learner session survives renderer reload and app relaunch', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      const session = await bootstrapSessionForRole(request, 'learner');

      app = await launchDesktop(appDataRoot);
      let page = await getDesktopPage(app);
      const diagnostics = observePage(page);

      await hydrateSessionStorage(page, session);
      await loadAbsolute(page, '/');
      await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();

      await page.reload({ waitUntil: 'domcontentloaded' });
      await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();

      await closeDesktop(app, appDataRoot);
      await wait(1_000);
      app = await launchDesktop(appDataRoot);
      page = await getDesktopPage(app);

      await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();

      expectNoSevereClientIssues(diagnostics);
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('learner reading workflow renders and preserves answers inside the desktop shell', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      const session = await bootstrapSessionForRole(request, 'learner');

      app = await launchDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);
      const answer = 'approximately 1 in 10';

      await hydrateSessionStorage(page, session);
      await loadAbsolute(page, '/');
      await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();

      await loadAbsolute(page, '/reading/player/rt-001');
      await expect(page.getByRole('heading', { name: /hospital-acquired infections: prevention strategies/i })).toBeVisible();
      await expect(page.getByText(/question 1 of 3/i)).toBeVisible();

      const answerInput = page.getByPlaceholder('Type your answer here...');
      await answerInput.fill(answer);

      await page.getByRole('button', { name: /flag this question for review/i }).click();
      await expect(page.getByRole('button', { name: /remove flag from this question/i })).toBeVisible();

      await page.getByRole('button', { name: /next/i }).click();
      await expect(page.getByText(/question 2 of 3/i)).toBeVisible();

      await page.getByRole('button', { name: /previous/i }).click();
      await expect(page.getByText(/question 1 of 3/i)).toBeVisible();
      await expect(answerInput).toHaveValue(answer);

      expectNoSevereClientIssues(diagnostics);
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('expert and admin protected routes render inside the desktop shell', async ({ request }, testInfo) => {
    const routeChecks = [
      {
        role: 'expert' as const,
        homePath: '/expert',
        homeHeading: /keep owned reviews and exam signals in view/i,
        entryActionRole: 'button' as const,
        entryActionName: 'Open Queue',
        targetPath: /\/expert\/queue(?:\?|$)/,
        text: /review queue|queue/i,
      },
      {
        role: 'admin' as const,
        homePath: '/admin',
        homeHeading: /keep platform health, review risk, and rollout signals in one place/i,
        entryActionRole: 'link' as const,
        entryActionName: 'Open Content Library',
        targetPath: /\/admin\/content(?:\?|$)/,
        text: /content library|content/i,
      },
    ];

    for (const routeCheck of routeChecks) {
      let app: ElectronApp | null = null;
      const appDataRoot = await createAppDataRoot();

      try {
        const session = await bootstrapSessionForRole(request, routeCheck.role);

        app = await launchDesktop(appDataRoot);
        const page = await getDesktopPage(app);
        const diagnostics = observePage(page);

        await hydrateSessionStorage(page, session);
        await loadAbsolute(page, routeCheck.homePath);

        await expect(page.getByRole('heading', { name: routeCheck.homeHeading })).toBeVisible();

        const navigationAction = page.getByRole(routeCheck.entryActionRole, {
          name: routeCheck.entryActionName,
        }).first();

        await expect(navigationAction).toBeVisible();
        await navigationAction.click();
        await page.waitForURL(routeCheck.targetPath);

        await expect(page.getByRole('main').getByText(routeCheck.text).first()).toBeVisible();

        expectNoSevereClientIssues(diagnostics);
        diagnostics.detach();
        await attachDiagnostics(testInfo, diagnostics);
      } finally {
        await closeDesktop(app, appDataRoot);
        await removeAppDataRoot(appDataRoot);
      }
    }
  });
});
