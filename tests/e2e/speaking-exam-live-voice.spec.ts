import { expect, test } from '@playwright/test';
import { recoverBrowserSession } from './fixtures/auth-bootstrap';

const EXAM_ID = 'smoke-ai-speaking-exam';
const SESSION_ID = 'smoke-ai-speaking-session';

type ExamState = 'intro' | 'prep_a' | 'active_a';

const candidateCard = {
  cardId: 'smoke-card-a',
  professionId: 'nursing',
  scenarioTitle: 'Discharge planning',
  setting: 'Community health centre',
  candidateRole: 'Nurse',
  interlocutorRole: 'Patient',
  patientName: 'Alex Morgan',
  patientAge: '58',
  background: 'The patient is preparing for discharge after treatment.',
  tasks: [
    'Explain the discharge plan clearly.',
    'Check the patient understands the follow-up arrangements.',
  ],
  allowedNotes: true,
  prepTimeSeconds: 180,
  rolePlayTimeSeconds: 300,
  difficulty: 'standard',
  disclaimer: 'Practice scenario for learner training.',
  displayCardNumber: 1,
};

test.describe('Speaking candidate AI live voice route @learner @speaking', () => {
  test('candidate reaches the real AI-patient conversation surface', async ({ page, request }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    let state: ExamState = 'intro';

    const examDetail = () => {
      const active = state === 'active_a';
      const hasCard = state !== 'intro';
      const now = new Date();
      const stageEndsAt = new Date(now.getTime() + (state === 'prep_a' ? 180_000 : active ? 300_000 : 0));

      return {
        examId: EXAM_ID,
        mode: 'ai',
        state,
        professionId: 'nursing',
        currentCardNumber: hasCard ? 1 : 0,
        currentSessionId: active ? SESSION_ID : null,
        currentCard: hasCard ? candidateCard : null,
        clock: {
          stage: state,
          serverNow: now.toISOString(),
          stageStartedAt: hasCard ? now.toISOString() : null,
          stageEndsAt: hasCard ? stageEndsAt.toISOString() : null,
          secondsRemaining: state === 'prep_a' ? 180 : active ? 300 : null,
          expired: false,
        },
        completedAt: null,
        mockAttemptId: null,
        mockSectionId: null,
      };
    };

    await page.route(`**/v1/speaking/exams/${EXAM_ID}**`, async (route) => {
      const requestUrl = new URL(route.request().url());
      const method = route.request().method();

      if (method === 'POST' && requestUrl.pathname.endsWith('/finish-intro')) {
        state = 'prep_a';
      } else if (method === 'POST' && requestUrl.pathname.endsWith('/start-card')) {
        state = 'active_a';
      }

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(examDetail()),
      });
    });

    // This deterministic route test verifies the production candidate page and
    // conversation wiring without consuming Whisper/LLM/TTS provider quota.
    await page.route('**/v1/conversations/hub**', async (route) => {
      await route.abort();
    });

    await recoverBrowserSession(page, request, 'learner', `/speaking/exam/${EXAM_ID}`, { freshSession: true });

    await expect(page.getByRole('heading', { name: 'Speaking exam' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Introduction' })).toBeVisible();
    await page.getByRole('button', { name: 'Begin Part 2 (Card A)' }).click();

    await expect(page.getByText('Community health centre')).toBeVisible();
    await expect(page.getByRole('button', { name: /start the discussion now/i })).toBeVisible();
    await page.getByRole('button', { name: /start the discussion now/i }).click();

    await expect(page.getByText('AI patient', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: /start talking/i })).toBeVisible();
    await expect(page.getByText(/Tap .*Start talking.* to begin the conversation with the patient/i)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Type instead' })).toBeVisible();
  });
});
