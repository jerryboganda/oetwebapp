import { expect, test } from '@playwright/test';

const sessionPayload = {
  resumeAllowed: true,
  resumeToken: 'resume-token-e2e',
  resumeTokenExpiresAt: new Date(Date.now() + 30 * 60_000).toISOString(),
  session: {
    id: 'cs-e2e-resume',
    state: 'active',
    scenarioJson: JSON.stringify({ title: 'Chest pain role play', context: 'Assess a patient with chest pain.' }),
  },
  turns: [
    { turnNumber: 1, role: 'ai', content: 'Hello, how can I help today?', audioUrl: null, durationMs: 1000, confidence: 1, createdAt: new Date().toISOString() },
    { turnNumber: 2, role: 'learner', content: 'I have chest pain when walking.', audioUrl: null, durationMs: 1800, confidence: 0.91, createdAt: new Date().toISOString() },
  ],
};

test('conversation resume hydrates an active transcript without replaying the opening', async ({ page }) => {
  await page.route('**/v1/conversations/cs-e2e-resume/resume', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(sessionPayload) });
  });

  await page.goto('/conversation/cs-e2e-resume');

  await expect(page.getByText('Chest pain role play')).toBeVisible();
  await expect(page.getByText('Hello, how can I help today?')).toBeVisible();
  await expect(page.getByText('I have chest pain when walking.')).toBeVisible();
});

test('conversation results exposes authenticated transcript exports', async ({ page }) => {
  await page.route('**/v1/conversations/cs-e2e-resume/evaluation', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        sessionId: 'cs-e2e-resume',
        state: 'evaluated',
        ready: true,
        scaledScore: 360,
        scaledMax: 500,
        passScaled: 350,
        passed: true,
        overallGrade: 'B',
        criteria: [],
        strengths: ['Clear empathy'],
        improvements: ['Ask one more safety-netting question'],
        suggestedPractice: [],
        appliedRuleIds: [],
        turnCount: 2,
        durationSeconds: 42,
        turns: sessionPayload.turns,
      }),
    });
  });
  await page.route('**/v1/conversations/cs-e2e-resume/transcript/export?format=txt', async (route) => {
    await route.fulfill({ status: 200, contentType: 'text/plain', body: 'OET AI Conversation Transcript' });
  });

  await page.goto('/conversation/cs-e2e-resume/results');

  await expect(page.getByRole('heading', { name: /Transcript/i })).toBeVisible();
  await expect(page.getByRole('button', { name: /TXT/i })).toBeVisible();
  await expect(page.getByRole('button', { name: /PDF/i })).toBeVisible();
});
