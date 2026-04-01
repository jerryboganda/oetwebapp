import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

const learnerRoutes = [
  { path: '/', text: /current focus|dashboard/i },
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

test.describe('Learner workspace smoke @learner @smoke', () => {
  test.describe.configure({ mode: 'serial' });

  test('learner dashboard loads and session survives a reload', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/', { waitUntil: 'domcontentloaded' });

    await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();
    await page.reload({ waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  for (const route of learnerRoutes) {
    test(`learner route ${route.path} renders without severe client failures`, async ({ page }, testInfo) => {
      if (!testInfo.project.name.includes('learner')) {
        test.skip();
      }

      const diagnostics = observePage(page);
      await page.goto(route.path, { waitUntil: 'domcontentloaded' });
      const main = page.getByRole('main');

      await expect(page).toHaveURL(new RegExp(route.path === '/' ? '/$' : route.path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
      await expect(main.getByText(route.text).first()).toBeVisible();

      if (route.path === '/submissions') {
        await expect(main.getByText(/\d{4}-\d{2}-\d{2}T\d{2}:/)).toHaveCount(0);
      }

      expectNoSevereClientIssues(diagnostics);
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    });
  }
});
