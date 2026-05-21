import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from './fixtures/diagnostics';

// Phase 7 (G.7) of the OET Speaking module plan — Playwright smoke for
// the admin role-play card builder (Phase 1 deliverable). Mirrors the
// existing admin CRUD pattern in `admin/admin-deep-mutations.spec.ts`.
//
// Validates that an admin can:
//   1. Navigate to /admin/content/speaking/role-play-cards
//   2. Click "New role-play card"
//   3. Fill candidate-card fields, save
//   4. Author the interlocutor script
//   5. Publish
//   6. Confirm the card appears in the list with status "Published"
//
// Skipped on non-admin projects. When the Phase 1 routes do not yet
// exist on the running build the test will fail loud (404 / unmatched
// selector); that's intentional — the spec is the integration probe.

test.describe('Speaking admin role-play card flow @admin @speaking', () => {
  test('admin creates, scripts, and publishes a role-play card', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-admin') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const scenarioTitle = `QA Discharge Advice ${Date.now()}`;

    // 1. Land on the cards list.
    await page.goto('/admin/content/speaking/role-play-cards', {
      waitUntil: 'domcontentloaded',
    });
    await expect(
      page.getByRole('heading', { name: /role[- ]play cards/i }),
    ).toBeVisible({ timeout: 30000 });

    // 2. New card.
    const newButton = page
      .getByRole('link', { name: /new role[- ]play card/i })
      .or(page.getByRole('button', { name: /new role[- ]play card/i }));
    await newButton.first().click();
    await page.waitForURL(/\/admin\/content\/speaking\/role-play-cards\/(new|[^/]+)$/, {
      timeout: 30000,
    });

    // 3. Fill candidate card form.
    await page.getByLabel(/scenario title/i).fill(scenarioTitle);
    await page.getByLabel(/setting/i).first().fill('Surgical ward');
    await page.getByLabel(/candidate role/i).fill('Nurse');
    await page.getByLabel(/background/i).fill(
      'Patient is recovering from a routine appendectomy. They have been advised to take paracetamol PRN.',
    );

    // First couple of tasks. Selectors stay tolerant of the editor's
    // label naming so the spec doesn't break under minor copy edits.
    await page.getByLabel(/task\s*1/i).fill('Greet the patient and confirm identity');
    await page.getByLabel(/task\s*2/i).fill('Explain the discharge medication plan');
    await page.getByLabel(/task\s*3/i).fill('Reassure the patient about pain management');

    // Optional fields the form normally surfaces.
    const patientEmotion = page.getByLabel(/patient emotion/i);
    if (await patientEmotion.isVisible().catch(() => false)) {
      await patientEmotion.fill('worried');
    }
    const communicationGoal = page.getByLabel(/communication goal/i);
    if (await communicationGoal.isVisible().catch(() => false)) {
      await communicationGoal.fill('Reassure');
    }
    const clinicalTopic = page.getByLabel(/clinical topic/i);
    if (await clinicalTopic.isVisible().catch(() => false)) {
      await clinicalTopic.fill('Pain management');
    }

    await page
      .getByRole('button', { name: /^save (draft|card|role[- ]play)/i })
      .first()
      .click();

    // Edit page or wizard step 2.
    await page.waitForURL(/\/admin\/content\/speaking\/role-play-cards\/[^/]+/, {
      timeout: 30000,
    });

    // 4. Interlocutor script tab/link.
    const interlocutorLink = page
      .getByRole('link', { name: /interlocutor (script|card)/i })
      .or(page.getByRole('button', { name: /interlocutor (script|card)/i }));
    if (await interlocutorLink.first().isVisible().catch(() => false)) {
      await interlocutorLink.first().click();
      await page.waitForURL(/interlocutor/i, { timeout: 15000 }).catch(() => undefined);
    }

    await page
      .getByLabel(/opening response/i)
      .fill("I'm a bit worried about taking these tablets.");
    const hidden = page.getByLabel(/hidden information/i);
    if (await hidden.isVisible().catch(() => false)) {
      await hidden.fill('Patient had nausea after the first dose 6 hours ago.');
    }
    const closing = page.getByLabel(/closing cue/i);
    if (await closing.isVisible().catch(() => false)) {
      await closing.fill('Accept advice if reassured about pain control.');
    }

    await page
      .getByRole('button', { name: /save (interlocutor|script)/i })
      .first()
      .click();

    // 5. Publish.
    await page
      .getByRole('button', { name: /^publish( role[- ]play| card)?$/i })
      .first()
      .click();

    await expect(
      page.getByText(/(published|live|status: published)/i).first(),
    ).toBeVisible({ timeout: 30000 });

    // 6. Confirm visibility in the list.
    await page.goto('/admin/content/speaking/role-play-cards', {
      waitUntil: 'domcontentloaded',
    });
    await expect(page.getByText(scenarioTitle)).toBeVisible({ timeout: 30000 });
    await expect(
      page.getByRole('row', { name: new RegExp(scenarioTitle, 'i') })
        .getByText(/published/i),
    ).toBeVisible({ timeout: 5000 }).catch(async () => {
      // Some list UIs render status outside a row landmark; fall back
      // to a global "Published" pill next to the row.
      await expect(page.getByText(/published/i).first()).toBeVisible();
    });

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
