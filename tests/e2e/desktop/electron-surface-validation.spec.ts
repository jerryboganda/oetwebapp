import { mkdtemp, rm } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { _electron as electron, expect, test, type Page } from '@playwright/test';
import { bootstrapSessionForRole, hydrateSessionStorage } from '../fixtures/auth-bootstrap';
import { createDisposableSpeakingReviewRequest, createDisposableWritingReviewRequest } from '../fixtures/api-auth';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { installFakeRecordingMedia } from '../fixtures/media';

const baseURL = (process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000').replace(/\/$/, '');

type ElectronApp = Awaited<ReturnType<typeof electron.launch>>;
const warmedRendererRoutes = new Set<string>();

test.setTimeout(180_000);

const learnerRoutes = [
  { path: '/', text: /keep today'?s priorities and exam signals in view|current focus|dashboard/i },
  { path: '/study-plan', text: /keep today'?s study sequence visible|action plan/i },
  { path: '/progress', text: /progress/i },
  { path: '/readiness', text: /readiness/i },
  { path: '/reading', text: /reading/i },
  { path: '/listening', text: /listening/i },
  { path: '/writing', text: /writing/i },
  { path: '/speaking', text: /speaking/i },
  { path: '/submissions', text: /submission history|history/i },
  { path: '/settings/profile', text: /profile/i },
  { path: '/billing', text: /billing/i },
  { path: '/mocks', text: /mocks/i },
  { path: '/diagnostic', text: /diagnostic/i },
];

const expertRoutes = [
  { path: '/expert', text: /keep owned reviews and exam signals in view|dashboard|expert/i },
  { path: '/expert/queue', text: /review queue|queue/i },
  { path: '/expert/calibration', text: /calibration/i },
  { path: '/expert/metrics', text: /metrics/i },
  { path: '/expert/schedule', text: /schedule/i },
  { path: '/expert/learners', text: /learners/i },
];

const adminRoutes = [
  { path: '/admin', text: /keep platform health, review risk, and rollout signals in one place|operations|admin/i },
  { path: '/admin/content', text: /content library|content/i },
  { path: '/admin/criteria', text: /criteria|rubrics/i },
  { path: '/admin/taxonomy', text: /taxonomy/i },
  { path: '/admin/flags', text: /feature flags|flags/i },
  { path: '/admin/users', text: /user ops|users/i },
  { path: '/admin/billing', text: /billing/i },
  { path: '/admin/audit-logs', text: /audit logs/i },
];

async function createAppDataRoot() {
  return mkdtemp(path.join(os.tmpdir(), 'oet-desktop-surface-'));
}

function wait(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms));
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
    // Best-effort cleanup only.
  }
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
        cleanupDesktopProcesses(appDataRoot);
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

async function loadAbsolute(page: Page, targetPath: string) {
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

function routeRegExp(routePath: string) {
  if (routePath === '/') {
    return /\/$/;
  }

  return new RegExp(routePath.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
}

async function fillAllRubricScores(page: Page, value = '5') {
  const scoreSelects = page.locator('select[aria-label^="Score for "]');
  const count = await scoreSelects.count();

  expect(count, 'Expected rubric score selectors to be present').toBeGreaterThan(0);

  for (let index = 0; index < count; index += 1) {
    await scoreSelects.nth(index).selectOption(value);
  }
}

test.describe('Electron desktop surface validation', () => {
  test.describe.configure({ mode: 'serial' });

  test('learner routes render cleanly inside the desktop shell', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      const session = await bootstrapSessionForRole(request, 'learner');
      app = await launchDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);

      await hydrateSessionStorage(page, session);

      for (const route of learnerRoutes) {
        await loadAbsolute(page, route.path);
        await expect(page).toHaveURL(routeRegExp(route.path));
        await expect(page.getByRole('main').getByText(route.text).first()).toBeVisible();
      }

      expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('learner immersive listening, writing, and speaking flows complete inside the desktop shell', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      const session = await bootstrapSessionForRole(request, 'learner');
      app = await launchDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);
      const client4xx: string[] = [];
      const writingContent = [
        'Dear Dr Patterson,',
        'I am writing to refer Mrs Eleanor Vance following her recent admission after surgery.',
        'She still requires wound monitoring, pain review, and clear escalation advice for community follow-up.',
      ].join(' ');

      page.on('response', (response) => {
        if (response.status() >= 400 && response.status() < 500) {
          client4xx.push(`${response.status()} :: ${response.url()}`);
        }
      });
      page.on('dialog', (dialog) => dialog.accept());
      await hydrateSessionStorage(page, session);

      await loadAbsolute(page, '/listening/player/lt-001');
      await expect(page.getByRole('heading', { name: /ready to start\?/i })).toBeVisible();
      await page.getByRole('button', { name: /start audio & task/i }).click();
      await expect(page.getByRole('button', { name: /submit answers/i })).toBeVisible();
      await page.getByRole('button', { name: /^Increasing breathlessness at night$/i }).click();
      await page.getByLabel('Answer for question 2').fill('3-4 times per week');
      await page.getByRole('button', { name: /^Combination inhaler$/i }).click();
      await page.getByRole('button', { name: /submit answers/i }).click();
      await expect(page).toHaveURL(/\/listening\/results\/lt-001$/);
      await expect(page.getByText(/detailed review/i)).toBeVisible();

      await loadAbsolute(page, '/writing/player?taskId=wt-001');
      await expect(page.getByLabel('Writing editor')).toBeVisible();
      await page.getByLabel('Writing editor').fill(writingContent);
      await expect(page.getByText(/saving\.\.\./i)).toBeVisible();
      await expect(page.getByText(/^Saved$/i)).toBeVisible({ timeout: 15_000 });
      await page.getByRole('button', { name: /^submit$/i }).click();
      await page.getByRole('button', { name: /confirm submit/i }).click();
      await expect(page).toHaveURL(/\/writing\/result\?id=/);
      await expect(page.getByRole('heading', { name: /evaluation summary/i })).toBeVisible();

      await installFakeRecordingMedia(page);
      await loadAbsolute(page, '/speaking/task/st-001?mode=self');
      await expect(page.getByRole('heading', { name: /ready to record/i })).toBeVisible();
      await page.getByRole('button', { name: /start recording/i }).click();
      await expect(page.getByRole('heading', { name: /recording your response/i })).toBeVisible();
      await page.getByRole('button', { name: /pause recording/i }).click();
      await expect(page.getByRole('heading', { name: /recording paused/i })).toBeVisible();
      await page.getByRole('button', { name: /resume recording/i }).click();
      await expect(page.getByRole('heading', { name: /recording your response/i })).toBeVisible();
      await page.getByRole('button', { name: /submit recording/i }).click();
      await page.getByRole('button', { name: /submit for evaluation/i }).click();
      await expect(page).toHaveURL(/\/speaking\/results\//);
      await expect(page.getByRole('heading', { name: /performance summary/i })).toBeVisible();

      await testInfo.attach('client-4xx', {
        body: client4xx.join('\n') || 'none',
        contentType: 'text/plain',
      });
      expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('expert routes and detail workspaces render inside the desktop shell', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      const session = await bootstrapSessionForRole(request, 'expert');
      const writingReview = await createDisposableWritingReviewRequest(request);
      const speakingReview = await createDisposableSpeakingReviewRequest(request);
      app = await launchDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);

      await hydrateSessionStorage(page, session);

      for (const route of expertRoutes) {
        await loadAbsolute(page, route.path);
        await expect(page).toHaveURL(routeRegExp(route.path));
        await expect(page.getByRole('main').getByText(route.text).first()).toBeVisible();
      }

      await loadAbsolute(page, '/expert/learners/mock-user-001');
      await expect(page.getByRole('heading', { name: /faisal maqsood/i })).toBeVisible();
      await expect(page.getByText(/privacy notice/i).first()).toBeVisible();

      await loadAbsolute(page, `/expert/review/writing/${writingReview.reviewRequestId}`);
      await expect(page.getByRole('heading', { name: /review rubric/i })).toBeVisible();
      await expect(page.getByText(/ai reference scores/i).first()).toBeVisible();

      await loadAbsolute(page, `/expert/review/speaking/${speakingReview.reviewRequestId}`);
      await expect(page.getByText(/candidate audio submission/i).first()).toBeVisible();
      await expect(page.getByRole('tab', { name: /role card/i })).toBeVisible();

      expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('expert can save and submit review workspaces inside the desktop shell', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      const session = await bootstrapSessionForRole(request, 'expert');
      const writingReview = await createDisposableWritingReviewRequest(request);
      const speakingReview = await createDisposableSpeakingReviewRequest(request);
      const purposeComment = `Desktop QA writing draft ${Date.now()}`;
      const finalComment = `Desktop QA speaking final ${Date.now()}`;

      app = await launchDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);

      await hydrateSessionStorage(page, session);

      await loadAbsolute(page, `/expert/review/writing/${writingReview.reviewRequestId}`);
      await expect(page.getByRole('heading', { name: /review rubric/i })).toBeVisible();
      await page.getByLabel('Score for Purpose').selectOption('5');
      await page.getByLabel('Comment for Purpose').fill(purposeComment);
      await page.getByRole('button', { name: /save draft/i }).click();
      await expect(page.getByText(/draft saved successfully\./i)).toBeVisible();
      await expect(page.getByLabel('Comment for Purpose')).toHaveValue(purposeComment);

      await loadAbsolute(page, `/expert/review/speaking/${speakingReview.reviewRequestId}`);
      await expect(page.getByText(/candidate audio submission/i).first()).toBeVisible();
      await page.getByRole('tab', { name: /role card/i }).click();
      await expect(page.getByRole('region', { name: /role card details/i })).toContainText(/provide a clinical handover/i);
      await page.getByRole('tab', { name: /ai flags/i }).click();
      await expect(page.getByRole('button', { name: /go to .*s/i }).first()).toBeVisible();
      await fillAllRubricScores(page);
      await page.getByLabel('Final overall comment').fill(finalComment);
      await page.getByRole('button', { name: /submit review/i }).click();
      await expect(page.getByText(/review submitted successfully\./i)).toBeVisible();
      await expect(page).toHaveURL(/\/expert\/queue$/);

      expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('admin routes and detail views render inside the desktop shell', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      const session = await bootstrapSessionForRole(request, 'admin');
      app = await launchDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);

      await hydrateSessionStorage(page, session);

      for (const route of adminRoutes) {
        await loadAbsolute(page, route.path);
        await expect(page).toHaveURL(routeRegExp(route.path));
        await expect(page.getByRole('main').getByText(route.text).first()).toBeVisible();
      }

      await loadAbsolute(page, '/admin/content/lt-001');
      await expect(page.getByRole('heading', { name: /edit consultation: asthma management review/i })).toBeVisible();
      await expect(page.getByRole('heading', { name: /core content metadata/i })).toBeVisible();

      await loadAbsolute(page, '/admin/users/mock-user-001');
      await expect(page.getByRole('heading', { name: /faisal maqsood/i })).toBeVisible();
      await expect(page.getByText(/operational context/i).first()).toBeVisible();

      expectNoSevereClientIssues(diagnostics, { allowNotificationReconnectNoise: true });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });

  test('admin content creation and user credit modal workflows stay stable inside the desktop shell', async ({ request }, testInfo) => {
    let app: ElectronApp | null = null;
    const appDataRoot = await createAppDataRoot();

    try {
      const session = await bootstrapSessionForRole(request, 'admin');
      const title = `Desktop QA Content Draft ${Date.now()}`;
      app = await launchDesktop(appDataRoot);
      const page = await getDesktopPage(app);
      const diagnostics = observePage(page);

      await hydrateSessionStorage(page, session);

      await loadAbsolute(page, '/admin/content/new');
      await expect(page.getByRole('heading', { name: /create content/i })).toBeVisible();
      await page.getByLabel('Content Title').fill(title);
      await page.getByLabel('Learner-Facing Description').fill('Desktop QA-created content draft used to validate the admin content workflow.');
      await page.getByRole('button', { name: /^save draft$/i }).click();
      await expect(page).toHaveURL(/\/admin\/content\/(?!new$)[^/]+$/);
      await expect(page.getByRole('heading', { name: new RegExp(`edit ${title}`, 'i') })).toBeVisible();
      await expect(page.getByRole('button', { name: /revisions/i })).toBeVisible();

      await loadAbsolute(page, '/admin/users/mock-user-001');
      await expect(page.getByRole('heading', { name: /faisal maqsood/i })).toBeVisible();
      const adjustCreditsButton = page.getByRole('button', { name: /adjust credits/i });
      await adjustCreditsButton.click();
      const dialog = page.getByRole('dialog', { name: /adjust credits/i });
      await expect(dialog).toBeVisible();
      await expect(dialog.getByLabel('Credit Adjustment')).toBeVisible();
      await expect(dialog.getByLabel('Reason')).toBeVisible();
      await page.keyboard.press('Escape');
      await expect(dialog).toHaveCount(0);
      await expect(adjustCreditsButton).toBeFocused();

      expectNoSevereClientIssues(diagnostics);
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    } finally {
      await closeDesktop(app, appDataRoot);
      await removeAppDataRoot(appDataRoot);
    }
  });
});
