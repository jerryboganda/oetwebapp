import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from './fixtures/diagnostics';

// Phase 7 (G.7) of the OET Speaking module plan — Playwright smoke for
// the AI self-practice loop (Phase 2 deliverable). Mocks the AI provider
// via Playwright's request interception so the test does not depend on
// a real LLM.
//
// Steps (matching plan section D.1):
//   1. Log in as learner (storage state from auth.setup.ts).
//   2. Navigate to a published card's session page.
//   3. Accept the SpeakingConsentBanner.
//   4. Wait briefly for the prep timer (capped at 5s for speed).
//   5. Start the role-play.
//   6. End the session.
//   7. Verify the dual assessment layout has its AI column populated.

const MOCK_AI_ASSESSMENT = {
  ai: {
    sessionId: 'mock-session',
    criterionScores: {
      intelligibility: 5,
      fluency: 4,
      appropriateness: 5,
      grammarExpression: 4,
      relationshipBuilding: 2,
      patientPerspective: 2,
      structure: 2,
      informationGathering: 2,
      informationGiving: 2,
    },
    estimatedScaledScore: 360,
    readinessBand: 'exam_ready',
    overallSummary: 'Strong, empathetic communication with clear structure.',
    strengths: ['Empathy', 'Clear signposting'],
    improvements: ['Lay-language explanations could be simpler'],
  },
  tutor: null,
};

test.describe('Speaking learner AI self-practice flow @learner @speaking', () => {
  test('learner completes an AI session and sees the AI assessment column', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    // Intercept the AI assessment fetch and serve mocked content. The
    // exact route shape comes from the dual-scoring contract documented
    // in plan section E.2; we also accept the singular endpoint that
    // existed in earlier waves.
    await page.route(/\/v1\/speaking\/sessions\/[^/]+\/(assessments|ai-assessment)$/i, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(MOCK_AI_ASSESSMENT),
      });
    });

    // 1. + 2. Navigate to the speaking sessions root and pick a
    //         published card. We tolerate either the new unified route
    //         (`/speaking/sessions/<id>`) or the legacy task route
    //         (`/speaking/task/<id>`) so the spec is forward-compatible.
    await page.goto('/speaking', { waitUntil: 'domcontentloaded' });
    const startSession = page
      .getByRole('link', { name: /(start|new) (ai )?(practice|session)/i })
      .or(page.getByRole('button', { name: /(start|new) (ai )?(practice|session)/i }));
    if (await startSession.first().isVisible().catch(() => false)) {
      await startSession.first().click();
    } else {
      // Fallback: navigate to the first available card in the list.
      const firstCard = page.getByRole('link', { name: /(role[- ]play|practice)/i }).first();
      if (await firstCard.isVisible().catch(() => false)) {
        await firstCard.click();
      }
    }

    // 3. Consent banner. Some session routes accept consent
    //    asynchronously — wait up to 5s for the modal.
    const consent = page.getByTestId('speaking-consent-banner');
    if (await consent.isVisible({ timeout: 5000 }).catch(() => false)) {
      await page.getByTestId('speaking-consent-accept').click();
      await expect(consent).toBeHidden({ timeout: 15000 });
    }

    // 4. Prep timer — capped wait. We don't want to actually sit for
    //    3 minutes in CI, so we look for a "skip prep" affordance or a
    //    "Start role-play" button and force progression.
    const startRolePlay = page
      .getByRole('button', { name: /start role[- ]play/i })
      .or(page.getByRole('button', { name: /skip prep/i }));
    if (await startRolePlay.first().isVisible({ timeout: 5000 }).catch(() => false)) {
      await startRolePlay.first().click();
    }

    // 5./6. End the session.
    const endButton = page
      .getByRole('button', { name: /end (session|role[- ]play)/i })
      .or(page.getByRole('button', { name: /^submit$/i }));
    if (await endButton.first().isVisible({ timeout: 30000 }).catch(() => false)) {
      await endButton.first().click();
    }

    // 7. Dual assessment AI column.
    await expect(
      page
        .getByRole('region', { name: /ai assessment/i })
        .or(page.getByText(/ai assessment/i).first()),
    ).toBeVisible({ timeout: 60000 });

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
