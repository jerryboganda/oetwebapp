import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { createDisposableSpeakingReviewRequest, createDisposableWritingReviewRequest } from '../fixtures/api-auth';
import { waitForSessionGuardToClear } from '../fixtures/auth';
import { recoverBrowserSession } from '../fixtures/auth-bootstrap';

type ResolvePathContext = {
  request: APIRequestContext;
};

const DETAIL_EXPECT_TIMEOUT_MS = 60_000;

const expertDetailRoutes = [
  {
    name: 'writing review workspace',
    resolvePath: async ({ request }: ResolvePathContext) => {
      const { reviewRequestId } = await createDisposableWritingReviewRequest(request);
      return `/expert/review/writing/${reviewRequestId}`;
    },
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /review rubric/i })).toBeVisible({ timeout: DETAIL_EXPECT_TIMEOUT_MS });
      await expect(page.getByText(/ai reference scores/i).first()).toBeVisible({ timeout: DETAIL_EXPECT_TIMEOUT_MS });
      await expect(page.getByRole('button', { name: /submit review/i })).toBeVisible({ timeout: DETAIL_EXPECT_TIMEOUT_MS });
    },
  },
  {
    name: 'speaking review workspace',
    resolvePath: async ({ request }: ResolvePathContext) => {
      const { reviewRequestId } = await createDisposableSpeakingReviewRequest(request);
      return `/expert/review/speaking/${reviewRequestId}`;
    },
    assertions: async (page: Page) => {
      await expect(page.getByText(/candidate audio submission/i).first()).toBeVisible({ timeout: DETAIL_EXPECT_TIMEOUT_MS });
      await expect(page.getByRole('tab', { name: /role card/i })).toBeVisible({ timeout: DETAIL_EXPECT_TIMEOUT_MS });
      await expect(page.getByRole('heading', { name: /review rubric/i })).toBeVisible({ timeout: DETAIL_EXPECT_TIMEOUT_MS });
    },
  },
  {
    name: 'learner detail workspace',
    resolvePath: async () => '/expert/learners/mock-user-001',
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /faisal maqsood/i })).toBeVisible({ timeout: DETAIL_EXPECT_TIMEOUT_MS });
      await expect(page.getByText(/privacy notice/i).first()).toBeVisible({ timeout: DETAIL_EXPECT_TIMEOUT_MS });
      await expect(page.getByText(/sub-test performance/i).first()).toBeVisible({ timeout: DETAIL_EXPECT_TIMEOUT_MS });
    },
  },
];

test.describe('Expert detail smoke @expert @smoke', () => {
  for (const route of expertDetailRoutes) {
    test(`expert ${route.name} renders without severe client failures`, async ({ page, request }, testInfo) => {
      if (!testInfo.project.name.includes('expert')) {
        test.skip();
      }

      testInfo.setTimeout(150_000);
      const diagnostics = observePage(page);
      const path = await route.resolvePath({ request });

      await page.goto(path, { waitUntil: 'domcontentloaded', timeout: DETAIL_EXPECT_TIMEOUT_MS });
      await expect(page).toHaveURL(new RegExp(path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
      await waitForSessionGuardToClear(page, {
        initialTimeoutMs: 10_000,
        timeoutMs: DETAIL_EXPECT_TIMEOUT_MS,
        recover: () => recoverBrowserSession(page, request, 'expert', path),
      });
      await route.assertions(page);

      expectNoSevereClientIssues(diagnostics, {
        allowNextDevNoise: testInfo.project.name.includes('webkit'),
        // Desktop WebKit also emits "due to access control checks" page
        // errors when Next.js dev fetches /api/backend/v1/* requests during
        // initial hydration; these are benign and the same gate is already
        // used by the mobile-webkit suite.
        allowMobileWebKitReloadNoise: testInfo.project.name.includes('webkit'),
        allowNotificationReconnectNoise: true,
      });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    });
  }
});
