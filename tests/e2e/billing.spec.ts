import { expect, test } from '@playwright/test';
import {
  attachDiagnostics,
  expectNoSevereClientIssues,
  observePage,
} from './fixtures/diagnostics';

/**
 * Slice H — Playwright smoke for the billing upgrade journey.
 *
 * The existing `billing.smoke.spec.ts` covers the billing-center tab surface.
 * This file adds the complementary upgrade-path smoke: visit `/billing`,
 * confirm the upgrade CTA renders, click into `/billing/upgrade`, and assert
 * the comparison surface lands without console / page errors.
 *
 * Tagged `@smoke` so it joins the standard smoke matrix and runs under the
 * learner storage-state projects only (the unauth/admin/expert projects do
 * not have a learner subscription seeded).
 */
test.describe('Billing upgrade journey @smoke @billing', () => {
  test('learner can navigate from billing center into the upgrade compare surface', async ({
    page,
  }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    // ── 1. Land on the billing center.
    await page.goto('/billing');
    await expect(page).toHaveURL(/\/billing/);
    await expect(page.getByText('Your billing center')).toBeVisible();

    // ── 2. The "Plans" tab houses the upgrade CTA. Move there.
    await page.getByRole('tab', { name: /^plans$/i }).click();

    // ── 3. Find the "Compare plans" link that routes to /billing/upgrade.
    //      Multiple links can match (header + tab body), so take the first
    //      visible one.
    const compareLink = page
      .getByRole('link', { name: /compare plans/i })
      .first();
    await expect(compareLink).toBeVisible();

    // ── 4. Navigate to the upgrade surface.
    await Promise.all([
      page.waitForURL(/\/billing\/upgrade/),
      compareLink.click(),
    ]);

    // ── 5. The upgrade page mounts.
    await expect(page.getByRole('heading', { name: /compare plans/i })).toBeVisible();

    // Either the "Available plans" surface or the "Unable to load plan
    // information." fallback is acceptable for a smoke run — both paths
    // confirm the route mounted without throwing.
    const plansHeading = page.getByText(/available plans/i).first();
    const fallback = page.getByText(/unable to load plan information/i);
    await expect(async () => {
      const hasPlans = await plansHeading.isVisible().catch(() => false);
      const hasFallback = await fallback.isVisible().catch(() => false);
      expect(hasPlans || hasFallback).toBe(true);
    }).toPass({ timeout: 10_000 });

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('unauth visitors are redirected away from /billing/upgrade @auth', async ({
    page,
  }, testInfo) => {
    // Only run on unauthenticated projects (no `*-learner` / `*-admin` /
    // `*-expert` storage state attached).
    if (
      testInfo.project.name.includes('learner')
      || testInfo.project.name.includes('expert')
      || testInfo.project.name.includes('admin')
    ) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/billing/upgrade');

    // Middleware must redirect to /sign-in (or otherwise gate the surface).
    await expect.poll(
      () => page.url(),
      { timeout: 10_000, message: 'Expected unauth visit to be redirected away from /billing/upgrade.' },
    ).not.toMatch(/\/billing\/upgrade$/);

    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
