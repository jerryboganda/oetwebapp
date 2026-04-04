import { existsSync } from 'node:fs';
import { mkdtemp, rm } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { _electron as electron, expect, test } from '@playwright/test';
import { bootstrapSessionForRole, hydrateSessionStorage } from '../fixtures/auth-bootstrap';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

type ElectronApp = Awaited<ReturnType<typeof electron.launch>>;
const packagedRendererPort = process.env.ELECTRON_PACKAGED_RENDERER_PORT ?? '3300';
const packagedBackendPort = process.env.ELECTRON_PACKAGED_BACKEND_PORT ?? '5298';

function resolvePackagedExecutablePath() {
  if (process.env.ELECTRON_EXECUTABLE_PATH) {
    return process.env.ELECTRON_EXECUTABLE_PATH;
  }

  return path.join(
    process.cwd(),
    'dist',
    'desktop',
    'win-unpacked',
    process.platform === 'win32' ? 'OET Prep.exe' : 'OET Prep',
  );
}

function wait(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function createAppDataRoot() {
  return mkdtemp(path.join(os.tmpdir(), 'oet-desktop-packaged-e2e-'));
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

      await wait(250);
    }
  }

  try {
    await rm(appDataRoot, { recursive: true, force: true });
  } catch {
    // Best-effort cleanup only for packaged smoke runs on Windows.
  }
}

async function launchPackagedDesktop(appDataRoot: string): Promise<ElectronApp> {
  const executablePath = resolvePackagedExecutablePath();

  if (!existsSync(executablePath)) {
    throw new Error(`Packaged desktop executable was not found at ${executablePath}. Run desktop:dist first or set ELECTRON_EXECUTABLE_PATH.`);
  }

  return electron.launch({
    executablePath,
    env: {
      ...process.env,
      ELECTRON_APPDATA_ROOT: appDataRoot,
      ELECTRON_RUNTIME_CHANNEL: 'packaged-test',
      PORT: packagedRendererPort,
      ELECTRON_BACKEND_PORT: packagedBackendPort,
      NODE_ENV: 'test',
    },
  });
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

async function closeDesktop(app: ElectronApp | null) {
  if (!app) {
    return;
  }

  let processHandle: ReturnType<ElectronApp['process']> | null = null;
  try {
    processHandle = typeof app.process === 'function' ? app.process() : null;
  } catch {
    processHandle = null;
  }

  try {
    await Promise.race([
      app.close(),
      wait(10_000),
    ]);
  } catch {
    // Closing a crashed packaged app should not fail cleanup.
  }

  if (!processHandle?.pid) {
    return;
  }

  for (let attempt = 0; attempt < 20; attempt += 1) {
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

async function getDesktopPage(app: ElectronApp) {
  const page = await app.firstWindow({ timeout: 120_000 });
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

function getAppOrigin(urlString: string) {
  return new URL(urlString).origin;
}

async function loadWithinApp(page: Parameters<typeof observePage>[0], routePath: string) {
  const targetUrl = new URL(routePath, `${getAppOrigin(page.url())}/`).toString();
  await page.goto(targetUrl, { waitUntil: 'domcontentloaded' });
}

test.describe('Packaged Electron desktop shell', () => {
  test.describe.configure({ mode: 'serial' });

  test('boots the packaged renderer, bridge, and bundled backend health endpoints', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      app = await launchPackagedDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);

      await expect(page.getByRole('heading', { name: /login to your account|access your workspace/i })).toBeVisible();

      const origin = getAppOrigin(page.url());
      const [rendererHealth, backendReady] = await Promise.all([
        request.get(`${origin}/api/health`),
        request.get(`http://127.0.0.1:${packagedBackendPort}/health/ready`),
      ]);

      expect(rendererHealth.ok()).toBe(true);
      expect(backendReady.ok()).toBe(true);

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
      await closeDesktop(app);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('preserves learner session across reload and relaunch in packaged mode', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      app = await launchPackagedDesktop(appDataRoot);
      let page = await getDesktopPage(app);
      const diagnostics = observePage(page);
      const apiBaseURL = `${getAppOrigin(page.url())}/api/backend`;
      const session = await bootstrapSessionForRole(request, 'learner', apiBaseURL);

      await hydrateSessionStorage(page, session);
      await loadWithinApp(page, '/');
      await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();

      await page.reload({ waitUntil: 'domcontentloaded' });
      await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();

      await closeDesktop(app);
      await wait(1_000);

      app = await launchPackagedDesktop(appDataRoot);
      page = await getDesktopPage(app);
      await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();

      expectNoSevereClientIssues(diagnostics);
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('renders a learner reading workflow inside the packaged desktop shell', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      app = await launchPackagedDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);
      const apiBaseURL = `${getAppOrigin(page.url())}/api/backend`;
      const session = await bootstrapSessionForRole(request, 'learner', apiBaseURL);
      const answer = 'approximately 1 in 10';

      await hydrateSessionStorage(page, session);
      await loadWithinApp(page, '/');
      await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();

      await loadWithinApp(page, '/reading/player/rt-001');
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
      await closeDesktop(app);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('renders expert and admin protected routes inside the packaged shell', async ({ request }, testInfo) => {
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
        app = await launchPackagedDesktop(appDataRoot);
        const page = await getDesktopPage(app);
        const diagnostics = observePage(page);
        const apiBaseURL = `${getAppOrigin(page.url())}/api/backend`;
        const session = await bootstrapSessionForRole(request, routeCheck.role, apiBaseURL);

        await hydrateSessionStorage(page, session);
        await loadWithinApp(page, routeCheck.homePath);
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
        await closeDesktop(app);
        await removeAppDataRoot(appDataRoot);
      }
    }
  });
});
