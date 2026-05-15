import { expect, test, type Locator, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { waitForSessionGuardToClear } from '../fixtures/auth';
import { recoverBrowserSession } from '../fixtures/auth-bootstrap';

const learnerRoutes = [
  {
    path: '/',
    heading: /keep today.?s priorities and exam signals in view/i,
  },
  {
    path: '/study-plan',
    heading: /keep today.?s study sequence visible/i,
  },
  {
    path: '/progress',
    heading: /see whether recent effort is turning into better evidence/i,
  },
  {
    path: '/readiness',
    heading: /see what needs to close before your target date/i,
  },
  {
    path: '/reading',
    heading: /build reading accuracy before you validate it in mocks/i,
  },
  {
    path: '/listening',
    heading: /train listening accuracy before you test it under pressure/i,
  },
  {
    path: '/writing',
    heading: /choose the next writing task that moves your score/i,
  },
  {
    path: '/speaking',
    heading: /keep the next speaking move and recent evidence in view/i,
  },
  {
    path: '/submissions',
    heading: /reopen the attempts that need review or comparison/i,
  },
  {
    path: '/settings/profile',
    heading: /keep profile settings clear before you change them/i,
  },
  {
    path: '/billing',
    heading: /your billing center/i,
  },
  {
    path: '/mocks',
    heading: /choose the mock that proves whether practice is transferring/i,
  },
  {
    path: '/diagnostic',
    heading: /build your baseline before the study plan starts/i,
  },
  {
    path: '/private-speaking',
    heading: /private speaking sessions/i,
  },
];

const recoverableRouteLoadErrorPattern =
  /could not load|something went wrong|request failed|internal server error/i;

async function hasRecoverableRouteLoadError(page: Page) {
  await page.waitForLoadState('networkidle', { timeout: 5_000 }).catch(() => undefined);

  return pageHasRecoverableRouteLoadError(page);
}

async function pageHasRecoverableRouteLoadError(page: Page) {
  const bodyText = await page.locator('body').textContent({ timeout: 1_000 }).catch(() => '');
  const documentText = await page
    .evaluate(() => document.documentElement?.textContent ?? document.body?.textContent ?? '')
    .catch(() => '');

  return recoverableRouteLoadErrorPattern.test(`${bodyText ?? ''}\n${documentText}`);
}

async function openLearnerRoute(page: Page, path: string) {
  let recovered = false;

  for (let attempt = 0; attempt < 3; attempt += 1) {
    const response = await page.goto(path, { waitUntil: 'domcontentloaded' });
    await waitForSessionGuardToClear(page, {
      recover: () => page.goto(path, { waitUntil: 'domcontentloaded' }),
      initialTimeoutMs: 15_000,
    });

    if ((response?.status() ?? 200) < 500 && !(await hasRecoverableRouteLoadError(page))) {
      return recovered;
    }

    recovered = true;
    await page.waitForTimeout(500 * (attempt + 1));
  }

  return recovered;
}

async function expectRouteHeading(page: Page, path: string, routeHeading: Locator) {
  let recovered = false;
  const deadline = Date.now() + 90_000;

  while (Date.now() < deadline) {
    if (await routeHeading.isVisible().catch(() => false)) {
      return recovered;
    }

    if (await pageHasRecoverableRouteLoadError(page)) {
      recovered = true;
      await openLearnerRoute(page, path);
      continue;
    }

    await page.waitForTimeout(500);
  }

  await expect(routeHeading).toBeVisible({ timeout: 1_000 });
  return recovered;
}

function hasRecoverableNextDevPageError(diagnostics: ReturnType<typeof observePage>) {
  return diagnostics.pageErrors.some(
    (entry) =>
      entry.includes('literal not terminated before end of script')
      || entry.includes('ChunkLoadError: Loading chunk'),
  );
}

test.describe('Learner workspace smoke @learner @smoke', () => {
  test.describe.configure({ mode: 'serial' });

  test('learner dashboard loads and session survives a reload', async ({ page, request }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    // Two `goto` + `reload` cycles, each waiting up to 90s for the dashboard
    // heading. Under sustained matrix load, dev-mode compile of `/` can soak
    // most of that budget. Allow a 4-minute window so the second wait still
    // has headroom after a slow first cycle.
    testInfo.setTimeout(240000);
    let diagnostics = observePage(page);
    const recover = () => recoverBrowserSession(page, request, 'learner', '/');
    const dashboardHeading = page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i });
    await recover();
    await waitForSessionGuardToClear(page, {
      recover,
      initialTimeoutMs: 15_000,
    });
    await expect(dashboardHeading).toBeVisible({ timeout: 90000 });
    await page.reload({ waitUntil: 'domcontentloaded' });
    await waitForSessionGuardToClear(page, { recover });
    await expect(dashboardHeading).toBeVisible({ timeout: 90000 });

    if (hasRecoverableNextDevPageError(diagnostics)) {
      diagnostics.detach();
      diagnostics = observePage(page);
      await page.goto('/', { waitUntil: 'domcontentloaded' });
      await waitForSessionGuardToClear(page, {
        recover,
        initialTimeoutMs: 15_000,
      });
      await expect(dashboardHeading).toBeVisible({ timeout: 90000 });
      await page.reload({ waitUntil: 'domcontentloaded' });
      await waitForSessionGuardToClear(page, { recover });
      await expect(dashboardHeading).toBeVisible({ timeout: 90000 });
    }

    expectNoSevereClientIssues(diagnostics, {
      allowNotificationReconnectNoise: true,
      allowNextDevNoise: true,
      allowMobileWebKitReloadNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  for (const route of learnerRoutes) {
    test(`learner route ${route.path} renders without severe client failures`, async ({ page }, testInfo) => {
      if (!testInfo.project.name.includes('learner')) {
        test.skip();
      }

      // Per-route smoke can recover from a recoverable dev-page error by
      // re-navigating, so the test may execute two full route loads. Allow a
      // 3-minute window to absorb cold compile under matrix load.
      testInfo.setTimeout(180000);
      let diagnostics = observePage(page);
      const recovered = await openLearnerRoute(page, route.path);
      if (recovered) {
        diagnostics.detach();
        diagnostics = observePage(page);
      }
      const routeHeading = page.getByRole('heading', { name: route.heading });

      await expect(page).toHaveURL(new RegExp(route.path === '/' ? '/$' : route.path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
      if (await expectRouteHeading(page, route.path, routeHeading)) {
        diagnostics.detach();
        diagnostics = observePage(page);
      }

      if (hasRecoverableNextDevPageError(diagnostics)) {
        diagnostics.detach();
        diagnostics = observePage(page);
        await openLearnerRoute(page, route.path);
        await expect(page).toHaveURL(new RegExp(route.path === '/' ? '/$' : route.path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
        await expectRouteHeading(page, route.path, routeHeading);
      }

      if (route.path === '/submissions') {
        await expect(page.getByText(/\d{4}-\d{2}-\d{2}T\d{2}:/)).toHaveCount(0);
      }

      expectNoSevereClientIssues(diagnostics, {
        allowNextDevNoise: true,
        allowNotificationReconnectNoise: true,
      });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    });
  }
});
