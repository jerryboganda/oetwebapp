import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockEnsureFreshAccessToken, mockFetchWithTimeout } = vi.hoisted(() => ({
  mockEnsureFreshAccessToken: vi.fn(),
  mockFetchWithTimeout: vi.fn(),
}));

vi.mock('../auth-client', () => ({
  ensureFreshAccessToken: mockEnsureFreshAccessToken,
}));

vi.mock('../env', () => ({
  env: { apiBaseUrl: '' },
}));

vi.mock('../network/fetch-with-timeout', () => ({
  fetchWithTimeout: mockFetchWithTimeout,
}));

export {};

const api = await import('../reading-pathway-api');

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

function lastCall() {
  return mockFetchWithTimeout.mock.calls.at(-1) as [string, RequestInit];
}

describe('reading-pathway-api', () => {
  beforeEach(() => {
    mockEnsureFreshAccessToken.mockResolvedValue('access-token');
    mockFetchWithTimeout.mockReset();
    if (typeof document !== 'undefined') {
      Object.defineProperty(document, 'cookie', {
        configurable: true,
        get: () => 'oet_csrf=csrf-token',
      });
    }
  });

  it('submits onboarding with the backend field names', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ userId: 'u1', currentStage: 'diagnostic' }));

    await api.submitOnboarding({
      targetBand: 'B+',
      examDate: null,
      hoursPerWeek: 8,
      profession: 'Medicine',
      hasTakenBefore: true,
      previousScore: 320,
      selfRatedSpeed: 4,
      selfRatedVocabulary: 3,
    });

    const [url, init] = lastCall();
    expect(url).toBe('/v1/reading-pathway/onboarding');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toMatchObject({
      targetBand: 'B+',
      selfRatedVocabulary: 3,
    });
  });

  it('loads diagnostic questions from the safe session projection route', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse([]));

    await api.getDiagnosticQuestions('session-1');

    expect(lastCall()[0]).toBe('/v1/reading-pathway/diagnostic/sessions/session-1/questions');
  });

  it('maps the daily plan list into the UI plan shape', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse([
      {
        id: 'plan-1',
        itemType: 'drill',
        focusSkill: 'S5',
        estimatedMinutes: 15,
        payloadJson: '{}',
        status: 'pending',
      },
      {
        id: 'plan-2',
        itemType: 'vocab_review',
        focusSkill: null,
        estimatedMinutes: 10,
        payloadJson: '{}',
        status: 'completed',
      },
    ]));

    const plan = await api.getTodayPlan();

    expect(lastCall()[0]).toBe('/v1/reading-pathway/plan/today');
    expect(plan.totalMinutes).toBe(25);
    expect(plan.completedCount).toBe(1);
    expect(plan.items[0]).toMatchObject({ title: 'S5 drill', focusSkill: 'S5' });
  });

  it('uses the practice session submit route for session completion', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ score: 3, totalQuestions: 5 }));

    await api.endPracticeSession('session-2');

    const [url, init] = lastCall();
    expect(url).toBe('/v1/reading-pathway/practice/sessions/session-2/submit');
    expect(init.method).toBe('POST');
  });

  it('loads and normalizes safe practice session questions', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({
      sessionId: 'session-3',
      mode: 'drill',
      focusSkill: 'S2',
      timeLimitSeconds: null,
      questions: [
        {
          id: 'q1',
          passageId: 'p1',
          stem: 'Choose the answer.',
          questionType: 'MultipleChoice3',
          partCode: 2,
          skillCode: 'S2',
          options: [
            { value: 'A', label: 'Check the policy' },
            { value: 'B', label: 'Call reception' },
          ],
        },
      ],
      passages: [
        { id: 'p1', title: 'Clinical notice', bodyHtml: '<p>Read first.</p>', partCode: 2 },
      ],
    }));

    const session = await api.getPracticeSessionQuestions('session-3');

    expect(lastCall()[0]).toBe('/v1/reading-pathway/practice/sessions/session-3/questions');
    expect(session.questions[0].options).toEqual([
      { key: 'A', text: 'Check the policy' },
      { key: 'B', text: 'Call reception' },
    ]);
    expect(session.passages[0]).toMatchObject({ id: 'p1', title: 'Clinical notice' });
  });

  it('adapts skill stats into radar data', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({
      current: { S1: 7, S5: 3 },
      baseline: { S1: 5, S5: 2 },
    }));

    const radar = await api.getSkillRadar();

    expect(radar.skills).toHaveLength(8);
    expect(radar.skills.find((skill) => skill.code === 'S5')).toMatchObject({
      name: 'Inference',
      current: 3,
      baseline: 2,
      target: 8,
    });
  });

  it('adapts mock results from the backend session result route', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({
      score: 31,
      totalQuestions: 42,
      scaledScore: 363,
      durationSeconds: 3500,
    }));

    const result = await api.getMockResults('mock-session');

    expect(lastCall()[0]).toBe('/v1/reading-pathway/mocks/sessions/mock-session/results');
    expect(result).toMatchObject({
      sessionId: 'mock-session',
      rawScore: 31,
      scaledScore: 363,
      grade: 'B',
      timeMap: { total: 3500 },
    });
  });
});
