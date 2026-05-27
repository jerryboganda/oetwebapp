import { expect, test } from '@playwright/test';

/**
 * Writing V2 — tutor portal queue smoke.
 * Tags: @writing-v2 @smoke @tutor
 *
 * Tutor portal routes (/tutor/...) are gated by the platform's `expert` role —
 * there is no separate "tutor" auth fixture; the seeded expert account is the
 * tutor account for Writing review purposes (mirrors /expert/* routing).
 *
 * Validates the queue table renders (even when empty) so we catch a routing
 * or hub-binding regression early.
 *
 * Scope: chromium-expert only.
 */

test.describe('Writing V2 tutor queue @writing-v2 @smoke @tutor', () => {
  test('tutor writing queue renders table shell', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    await page.goto('/tutor/writing/queue', { waitUntil: 'domcontentloaded' });

    // Hero heading.
    await expect(
      page.getByRole('heading', { name: /writing review queue/i }),
    ).toBeVisible({ timeout: 30_000 });

    // Queue table rendered (the table has aria-label="Writing tutor review queue").
    const queueTable = page.getByRole('table', { name: /writing tutor review queue/i });
    await expect(queueTable).toBeVisible({ timeout: 30_000 });

    // Status filter chips exist — pending is selected by default.
    await expect(page.getByRole('button', { name: /^Pending$/, pressed: true })).toBeVisible();

    // The table either lists rows or shows the "Queue is empty." cell.
    const emptyCell = page.getByText(/queue is empty/i);
    const rowCount = await queueTable.locator('tbody tr').count();
    if (rowCount === 0 || (rowCount === 1 && await emptyCell.isVisible().catch(() => false))) {
      // Empty queue is acceptable smoke output.
      return;
    }
    expect(rowCount).toBeGreaterThan(0);
  });
});
