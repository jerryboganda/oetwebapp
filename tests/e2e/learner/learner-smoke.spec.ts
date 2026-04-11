import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

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
    heading: /manage subscriptions without billing surprises/i,
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

test.describe('Learner workspace smoke @learner @smoke', () => {
  test.describe.configure({ mode: 'serial' });

  test('learner dashboard loads and session survives a reload', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    testInfo.setTimeout(120000);
    const diagnostics = observePage(page);
    const dashboardHeading = page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i });
    const sessionBanner = page.getByText(/checking your session/i);
    await page.goto('/', { waitUntil: 'domcontentloaded' });
    await expect(dashboardHeading).toBeVisible({ timeout: 45000 });
    await page.reload({ waitUntil: 'domcontentloaded' });
    if (await sessionBanner.isVisible().catch(() => false)) {
      await page.goto('/', { waitUntil: 'domcontentloaded' });
    }
    await expect(dashboardHeading).toBeVisible({ timeout: 90000 });

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

      const diagnostics = observePage(page);
      const sessionBanner = page.getByText(/checking your session/i);
      await page.goto(route.path, { waitUntil: 'domcontentloaded' });
      if (await sessionBanner.isVisible().catch(() => false)) {
        await page.goto(route.path, { waitUntil: 'domcontentloaded' });
      }
      const routeHeading = page.getByRole('heading', { name: route.heading });

      await expect(page).toHaveURL(new RegExp(route.path === '/' ? '/$' : route.path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
      await expect(sessionBanner).toBeHidden({ timeout: 90000 });
      await expect(routeHeading).toBeVisible({ timeout: 90000 });

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
