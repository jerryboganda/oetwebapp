import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from './fixtures/diagnostics';
import { installFakeRecordingMedia } from './fixtures/media';

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
      // The cold-load hydration burst (features/gamification/notifications)
      // can race the auth manager's first refresh on the throttled stack and
      // 401 before the bearer lands — the same auth-redirect noise class the
      // consent-gdpr suite already tolerates. The AI-assistant negotiate 401
      // is classified as reconnect noise (see diagnostics.ts).
      allowAuthRedirectNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});

// 23 Sep 2026 owner flow (every Speaking mode): Rules + consent with NO timer
// → 3-min prep with the card → 5-min speaking on the recorder fallback (live
// voice is not configured in production) → one Finish & submit → processing →
// result. Hermetic: the session engine is route-mocked so the order of calls
// can be asserted (consent before prep; recording upload before /end).
test.describe('Speaking consent-first flow on the recorder fallback @learner @speaking', () => {
  test('consent before timer → prep → speak (fallback) → submit → result', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) test.skip();
    testInfo.setTimeout(120_000);

    const cardId = 'rpc-e2e-flow';
    const sessionId = 'e2e-flow-session';
    const calls: string[] = [];
    let stage: 'warmup' | 'prep' | 'active' | 'finished' = 'warmup';
    const card = {
      cardId,
      professionId: 'medicine',
      scenarioTitle: 'Chest pain follow-up',
      setting: 'General practice',
      candidateRole: 'Doctor',
      interlocutorRole: 'Patient',
      patientName: 'Mr Lee',
      patientAge: '54',
      background: 'Chest pain after exercise last week; ECG normal.',
      tasks: ['Find out about the pain', 'Explain the plan for a stress test'],
      allowedNotes: false,
      prepTimeSeconds: 180,
      rolePlayTimeSeconds: 300,
      difficulty: 'core',
      criteriaFocus: [],
      disclaimer: 'Practice estimate only. This is not an official OET score or result.',
    };
    const session = () => ({
      sessionId,
      state: stage === 'active' ? 'Active' : stage === 'finished' ? 'Finished' : stage === 'prep' ? 'Prep' : 'WarmUp',
      mode: 'ai_self_practice',
      consentVersion: 'recording.v1',
      consentAccepted: calls.includes('consent'),
      liveVoiceAvailable: false,
      isFreeSample: false,
      prepStartedAt: new Date().toISOString(),
      prepEndsAt: new Date(Date.now() + 180_000).toISOString(),
      rolePlayStartedAt: stage === 'active' ? new Date().toISOString() : null,
      rolePlayEndsAt: new Date(Date.now() + 300_000).toISOString(),
      endedAt: null,
      submittedAt: calls.includes('submit') ? new Date().toISOString() : null,
      card,
    });
    const json = (body: unknown, status = 200) => ({ status, contentType: 'application/json', body: JSON.stringify(body) });

    await installFakeRecordingMedia(page);
    const diagnostics = observePage(page);

    await page.route(new RegExp(`/v1/speaking/role-play-cards/${cardId}$`), (route) => route.fulfill(json(card)));
    await page.route(/\/v1\/speaking\/sessions(\/[^?]*)?(\?.*)?$/, async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname.replace(/^.*\/v1\/speaking\/sessions/, '');
      const method = request.method();
      if (method === 'POST' && path === '') {
        calls.push('create');
        return route.fulfill(json(session()));
      }
      if (!path.startsWith(`/${sessionId}`)) return route.fallback();
      const action = path.slice(sessionId.length + 2);
      if (method === 'GET' && action === '') return route.fulfill(json(session()));
      if (method === 'GET' && action === 'clock') {
        return route.fulfill(json({
          stage,
          roleplayIndex: 0,
          serverNow: new Date().toISOString(),
          secondsRemaining: stage === 'prep' ? 180 : stage === 'active' ? 300 : null,
          expired: false,
          canAdvanceTo: [],
        }));
      }
      if (method === 'GET' && action === 'results') {
        return route.fulfill(json({ assessmentState: 'completed', retryable: false, failureReason: null }));
      }
      if (method === 'GET' && (action === 'assessments' || action === 'ai-assessment')) {
        return route.fulfill(json({ ...MOCK_AI_ASSESSMENT, sessionId, tutorHistory: [], divergence: null }));
      }
      if (method === 'POST') {
        calls.push(action);
        if (action === 'finish-warmup') stage = 'prep';
        if (action === 'start-roleplay') stage = 'active';
        if (action === 'end') stage = 'finished';
        if (action === 'recording') return route.fulfill(json({ status: 'received' }, 202));
        if (action === 'ai-assess') return route.fulfill(json({ state: 'processing' }, 202));
        return route.fulfill(json(session()));
      }
      return route.fallback();
    });

    try {
      // 1. Rules + consent — nothing is timed yet.
      await page.goto(`/speaking/roleplay/${cardId}`, { waitUntil: 'domcontentloaded' });
      await expect(page.getByTestId('speaking-rules-consent')).toBeVisible({ timeout: 60_000 });
      await expect(page.getByText(/one blank sheet of paper and a pen/i)).toBeVisible();
      await expect(page.getByRole('timer')).toHaveCount(0);
      await page.getByRole('checkbox', { name: /i have read the rules/i }).check();
      await page.getByRole('button', { name: 'Start preparation' }).click();

      // 2. Prep: card + countdown; consent landed before the prep clock started.
      await expect(page).toHaveURL(new RegExp(`/speaking/sessions/${sessionId}/prep$`), { timeout: 30_000 });
      await expect(page.getByTestId('speaking-role-card')).toBeVisible();
      await expect(page.getByRole('timer')).toBeVisible();
      expect(calls.indexOf('consent')).toBeGreaterThan(calls.indexOf('create'));
      expect(calls.indexOf('consent')).toBeLessThan(calls.indexOf('finish-warmup'));
      await page.getByRole('button', { name: 'Start speaking now' }).click();

      // 3. Speak on the recorder fallback: one indicator, one submit.
      await expect(page).toHaveURL(new RegExp(`/speaking/sessions/${sessionId}$`), { timeout: 30_000 });
      await expect(page.getByText('Recording — speak to the patient')).toBeVisible({ timeout: 30_000 });
      await expect(page.getByTestId('speaking-mic-indicator')).toHaveCount(1);
      await page.getByRole('button', { name: 'Finish & submit' }).click();
      await page.getByRole('dialog').getByRole('button', { name: 'Submit now' }).click();

      // 4. Upload lands before /end → /submit → /ai-assess, then the result.
      await expect(page).toHaveURL(new RegExp(`/speaking/sessions/${sessionId}/results$`), { timeout: 30_000 });
      const order = ['recording', 'end', 'submit', 'ai-assess'].map((step) => calls.indexOf(step));
      expect(order.every((index) => index >= 0), JSON.stringify(calls)).toBe(true);
      expect(order).toEqual([...order].sort((a, b) => a - b));
      await expect(
        page.getByRole('region', { name: /ai assessment/i }).or(page.getByText(/ai assessment/i).first()),
      ).toBeVisible({ timeout: 30_000 });

      expectNoSevereClientIssues(diagnostics, {
        allowNextDevNoise: true,
        allowAuthRedirectNoise: true,
        allowNotificationReconnectNoise: true,
        allowMockedBackendNoise: true,
      });
    } finally {
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    }
  });
});
