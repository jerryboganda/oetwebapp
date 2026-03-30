import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { createDisposableSpeakingReviewRequest, createDisposableWritingReviewRequest } from '../fixtures/api-auth';

type ResolvePathContext = {
  request: APIRequestContext;
};

const expertDetailRoutes = [
  {
    name: 'writing review workspace',
    resolvePath: async ({ request }: ResolvePathContext) => {
      const { reviewRequestId } = await createDisposableWritingReviewRequest(request);
      return `/expert/review/writing/${reviewRequestId}`;
    },
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /review rubric/i })).toBeVisible();
      await expect(page.getByText(/ai reference scores/i).first()).toBeVisible();
      await expect(page.getByRole('button', { name: /submit review/i })).toBeVisible();
    },
  },
  {
    name: 'speaking review workspace',
    resolvePath: async ({ request }: ResolvePathContext) => {
      const { reviewRequestId } = await createDisposableSpeakingReviewRequest(request);
      return `/expert/review/speaking/${reviewRequestId}`;
    },
    assertions: async (page: Page) => {
      await expect(page.getByText(/candidate audio submission/i).first()).toBeVisible();
      await expect(page.getByRole('tab', { name: /role card/i })).toBeVisible();
      await expect(page.getByRole('heading', { name: /review rubric/i })).toBeVisible();
    },
  },
  {
    name: 'learner detail workspace',
    resolvePath: async () => '/expert/learners/mock-user-001',
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /faisal maqsood/i })).toBeVisible();
      await expect(page.getByText(/privacy notice/i).first()).toBeVisible();
      await expect(page.getByText(/sub-test performance/i).first()).toBeVisible();
    },
  },
];

test.describe('Expert detail smoke @expert @smoke', () => {
  for (const route of expertDetailRoutes) {
    test(`expert ${route.name} renders without severe client failures`, async ({ page, request }, testInfo) => {
      if (!testInfo.project.name.includes('expert')) {
        test.skip();
      }

      const diagnostics = observePage(page);
      const path = await route.resolvePath({ request });

      await page.goto(path);
      await expect(page).toHaveURL(new RegExp(path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
      await route.assertions(page);

      expectNoSevereClientIssues(diagnostics);
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    });
  }
});
