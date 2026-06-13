import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from './fixtures/diagnostics';

// Phase 7 (G.7) of the OET Speaking module plan — Playwright smoke for the
// admin role-play card builder. Updated for the unified multi-step card wizard
// (/admin/speaking/cards/...). Validates that an admin can:
//   1. Open the unified Speaking hub
//   2. Start a new role-play card (creates a Draft, opens the wizard)
//   3. Walk the steps: classification -> candidate -> tasks -> interlocutor -> scoring
//   4. Publish on the review step
//   5. Confirm the card shows as Published in the hub's Cards tab
//
// Skipped on non-admin projects. When the wizard routes do not exist on the
// running build the test fails loud (404 / unmatched selector); that's
// intentional — the spec is the integration probe.

test.describe('Speaking admin role-play card flow @admin @speaking', () => {
  test('admin creates, scripts, and publishes a role-play card via the wizard', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-admin') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const scenarioTitle = `QA Discharge Advice ${Date.now()}`;

    // 1. Unified Speaking hub.
    await page.goto('/admin/speaking', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /speaking authoring/i })).toBeVisible({ timeout: 30000 });

    // 2. Start a new card -> create draft -> wizard.
    await page.getByRole('link', { name: /new role[- ]play card/i }).first().click();
    await page.waitForURL(/\/admin\/speaking\/cards\/new$/, { timeout: 30000 });
    await page.getByRole('button', { name: /create draft/i }).first().click();
    await page.waitForURL(/\/admin\/speaking\/cards\/[^/]+\/classification$/, { timeout: 30000 });

    // 3a. Classification.
    await page.getByLabel(/scenario title/i).fill(scenarioTitle);
    const clinicalTopic = page.getByLabel(/clinical topic/i);
    if (await clinicalTopic.isVisible().catch(() => false)) {
      await clinicalTopic.fill('Pain management');
    }
    await page.getByRole('button', { name: /next: candidate/i }).click();
    await page.waitForURL(/\/candidate$/, { timeout: 30000 });

    // 3b. Candidate context.
    await page.getByLabel(/^setting$/i).fill('Surgical ward');
    await page.getByLabel(/candidate role/i).fill('Nurse');
    await page.getByLabel(/background/i).fill(
      'Patient is recovering from a routine appendectomy. They have been advised to take paracetamol PRN.',
    );
    await page.getByRole('button', { name: /next: tasks/i }).click();
    await page.waitForURL(/\/tasks$/, { timeout: 30000 });

    // 3c. Tasks.
    await page.getByLabel(/task\s*1/i).fill('Greet the patient and confirm identity');
    await page.getByLabel(/task\s*2/i).fill('Explain the discharge medication plan');
    await page.getByLabel(/task\s*3/i).fill('Reassure the patient about pain management');
    await page.getByRole('button', { name: /next: hidden script/i }).click();
    await page.waitForURL(/\/interlocutor$/, { timeout: 30000 });

    // 3d. Hidden interlocutor script (its own Save advances to scoring).
    await page.getByLabel(/opening response/i).fill("I'm a bit worried about taking these tablets.");
    const hidden = page.getByLabel(/hidden information/i);
    if (await hidden.isVisible().catch(() => false)) {
      await hidden.fill('Patient had nausea after the first dose 6 hours ago.');
    }
    await page.getByRole('button', { name: /save interlocutor script/i }).first().click();
    await page.waitForURL(/\/scoring$/, { timeout: 30000 });

    // 3e. Scoring — pick at least one criterion, then continue to review.
    await page.getByRole('button', { name: /fluency/i }).first().click();
    await page.getByRole('button', { name: /next: review/i }).click();
    await page.waitForURL(/\/review$/, { timeout: 30000 });

    // 4. Publish.
    await page.getByRole('button', { name: /publish card/i }).click();
    await expect(page.getByText(/published and visible to learners/i)).toBeVisible({ timeout: 30000 });

    // 5. Confirm in the hub Cards tab.
    await page.goto('/admin/speaking', { waitUntil: 'domcontentloaded' });
    await expect(page.getByText(scenarioTitle)).toBeVisible({ timeout: 30000 });

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
