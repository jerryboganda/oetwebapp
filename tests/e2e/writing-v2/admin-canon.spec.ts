import { expect, test } from '@playwright/test';

/**
 * Writing V2 — admin canon CRUD smoke + test-detection.
 * Tags: @writing-v2 @smoke @admin
 *
 * Validates that the seeded canon library (≥20 rules) is listed in the admin
 * CRUD table and that the inline test-detection panel exists. We do NOT mutate
 * canon state (delete/create) — that is covered by admin-deep-mutations.
 *
 * Test-detection runs a regex/structural scan via
 * POST /v1/admin/writing/canon/{id}/test. We submit a sample letter and assert
 * the inline test-result element appears with the rule id we requested,
 * regardless of trigger outcome — both "triggered" and "no match" are valid
 * proofs that the endpoint round-tripped.
 *
 * Scope: chromium-admin only.
 */

test.describe('Writing V2 admin canon @writing-v2 @smoke @admin', () => {
  test('admin canon page lists rules + test-detection roundtrip', async ({
    page,
  }, testInfo) => {
    if (testInfo.project.name !== 'chromium-admin') {
      test.skip();
    }

    await page.goto('/admin/writing/canon', { waitUntil: 'domcontentloaded' });

    await expect(
      page.getByRole('heading', { name: /writing canon rules/i }),
    ).toBeVisible({ timeout: 30_000 });

    // Rules table exists.
    const rulesTable = page.getByRole('table', { name: /^canon rules$/i });
    await expect(rulesTable).toBeVisible({ timeout: 30_000 });

    // At least 20 seeded rules (the launch canon ships 25 — allow drift).
    // The rows hydrate asynchronously after the table mounts, so wait for the
    // 20th body row to be present before counting; reading `.count()` one-shot
    // right after the table becomes visible races the data load and sees only
    // the header/first row.
    const ruleRows = rulesTable.locator('tbody tr');
    await expect(ruleRows.nth(19)).toBeVisible({ timeout: 30_000 });
    const rowCount = await ruleRows.count();
    expect(
      rowCount,
      `Expected ≥20 canon rules in the admin table; got ${rowCount}`,
    ).toBeGreaterThanOrEqual(20);

    // Test-detection panel.
    await expect(
      page.getByRole('heading', { name: /test rule detection/i }),
    ).toBeVisible();

    // Pick a rule id from the first table row (avoids hard-coding SC-012 in
    // case the seed renumbers it).
    const firstRuleId = (
      await rulesTable.locator('tbody tr').first().locator('td').first().textContent()
    )?.trim();
    expect(firstRuleId, 'Expected first canon rule row to expose its id cell').toBeTruthy();

    await page.getByLabel(/^rule id$/i).fill(firstRuleId ?? 'SC-012');
    await page
      .getByLabel(/^letter$/i)
      .fill('The patient attended the clinic on 12 May for review of their hypertension.');
    await page.getByRole('button', { name: /^run test$/i }).click();

    // The result line appears as a role=status element with text like
    // "SC-012: 1 violation(s)" or "SC-012: no match". Accept either.
    const testResultStatus = page.getByRole('status');
    await expect(testResultStatus).toBeVisible({ timeout: 30_000 });
    const resultText = (await testResultStatus.textContent())?.trim() ?? '';
    expect(resultText).toMatch(
      new RegExp(`${(firstRuleId ?? 'SC-012').replace(/[.*+?^${}()|[\\]\\\\]/g, '\\$&')}.*(violation|no match)`, 'i'),
    );
  });
});
