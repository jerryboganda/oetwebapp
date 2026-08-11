import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

const learnerProjects = new Set([
  'chromium-learner',
  'mobile-chromium-learner',
  'mobile-webkit-learner',
]);

const routes = [
  { name: 'Listening player', path: '/listening/player/lt-001?mode=exam' },
  { name: 'Reading paper', path: '/reading/paper/rp-001?mode=exam' },
] as const;

async function expectNoHorizontalClipping(page: Page, routeName: string) {
  const overflow = await page.evaluate(() => {
    const viewportRight = window.innerWidth;
    const elements = Array.from(document.querySelectorAll<HTMLElement>('body *'));
    const offenders = elements
      .map((element) => ({
        tag: element.tagName.toLowerCase(),
        testId: element.dataset.testid ?? '',
        right: Math.ceil(element.getBoundingClientRect().right),
        width: Math.ceil(element.getBoundingClientRect().width),
      }))
      .filter((item) => item.width > 0 && item.right > viewportRight + 1)
      .slice(0, 5);

    return {
      documentWidth: document.documentElement.scrollWidth,
      viewportWidth: viewportRight,
      offenders,
    };
  });

  expect(overflow.documentWidth, `${routeName} document overflows viewport: ${JSON.stringify(overflow)}`)
    .toBeLessThanOrEqual(overflow.viewportWidth + 1);
  expect(overflow.offenders, `${routeName} contains clipped content: ${JSON.stringify(overflow)}`).toEqual([]);
}

test.describe('Listening and Reading responsive exam surfaces @learner @responsive', () => {
  for (const route of routes) {
    test(`${route.name} does not clip timer, passage, or controls`, async ({ page }, testInfo) => {
      if (!learnerProjects.has(testInfo.project.name)) test.skip();

      const diagnostics = observePage(page);
      await page.goto(route.path, { waitUntil: 'domcontentloaded' });
      await expect(page.locator('main')).toBeVisible();
      await expectNoHorizontalClipping(page, route.name);
      expectNoSevereClientIssues(diagnostics);
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    });
  }
});
