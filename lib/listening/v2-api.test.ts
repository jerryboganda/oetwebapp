const { mockGet, mockPost, mockPostWithAcceptedStatuses, mockPut, mockDelete } = vi.hoisted(() => ({
  mockGet: vi.fn(),
  mockPost: vi.fn(),
  mockPostWithAcceptedStatuses: vi.fn(),
  mockPut: vi.fn(),
  mockDelete: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: mockGet,
    post: mockPost,
    postWithAcceptedStatuses: mockPostWithAcceptedStatuses,
    put: mockPut,
    delete: mockDelete,
  },
}));

import { listeningV2Api, teacherClassApi } from './v2-api';

describe('listeningV2Api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('accepts the intentional 412 confirm-token response for strict section advance', async () => {
    mockPostWithAcceptedStatuses.mockResolvedValueOnce({
      outcome: 'confirm-required',
      state: null,
      confirmToken: 'token-1',
      confirmTokenTtlMs: 30000,
      rejectionReason: null,
      rejectionDetail: null,
    });

    const result = await listeningV2Api.advance('attempt 1', 'a1_preview', null);

    expect(mockPostWithAcceptedStatuses).toHaveBeenCalledWith(
      '/v1/listening/v2/attempts/attempt%201/advance',
      { toState: 'a1_preview', confirmToken: null },
      [412, 422],
    );
    expect(result).toMatchObject({ outcome: 'confirm-required', confirmToken: 'token-1' });
  });

  it('accepts structured 422 advance rejections such as missing tech readiness', async () => {
    mockPostWithAcceptedStatuses.mockResolvedValueOnce({
      outcome: 'rejected',
      state: null,
      confirmToken: null,
      confirmTokenTtlMs: null,
      rejectionReason: 'tech-readiness-required',
      rejectionDetail: 'Audio readiness check is required before starting this Listening attempt.',
    });

    const result = await listeningV2Api.advance('attempt-1', 'a1_preview', null);

    expect(mockPostWithAcceptedStatuses).toHaveBeenCalledWith(
      '/v1/listening/v2/attempts/attempt-1/advance',
      { toState: 'a1_preview', confirmToken: null },
      [412, 422],
    );
    expect(result).toMatchObject({
      outcome: 'rejected',
      rejectionReason: 'tech-readiness-required',
    });
  });

  it('records R10 tech readiness for an encoded attempt id', async () => {
    mockPost.mockResolvedValueOnce({
      audioOk: true,
      durationMs: 1500,
      checkedAt: '2026-05-12T00:00:00Z',
      ttlMs: 900000,
    });

    const result = await listeningV2Api.recordTechReadiness('attempt 1', {
      audioOk: true,
      durationMs: 1500,
    });

    expect(mockPost).toHaveBeenCalledWith(
      '/v1/listening/v2/attempts/attempt%201/tech-readiness',
      { audioOk: true, durationMs: 1500 },
    );
    expect(result.ttlMs).toBe(900000);
  });

  it('saves one answer through the V2 facade with encoded ids', async () => {
    mockPut.mockResolvedValueOnce(undefined);

    await listeningV2Api.saveAnswer('attempt 1', 'question/1', 'seventy five');

    expect(mockPut).toHaveBeenCalledWith(
      '/v1/listening/v2/attempts/attempt%201/answers/question%2F1',
      { userAnswer: 'seventy five' },
    );
  });

  it('submits final answers through the V2 facade and keeps the review DTO contract', async () => {
    mockPost.mockResolvedValueOnce({
      attemptId: 'attempt-1',
      rawScore: 30,
      maxRawScore: 42,
      scaledScore: 350,
      grade: 'B',
      evaluationId: 'eval-1',
    });

    const result = await listeningV2Api.submit('attempt 1', { 'question/1': 'seventy five' });

    expect(mockPost).toHaveBeenCalledWith(
      '/v1/listening/v2/attempts/attempt%201/submit',
      { answers: { 'question/1': 'seventy five' } },
    );
    expect(result).toMatchObject({ attemptId: 'attempt-1', grade: 'B' });
  });

  it('fetches my pathway and preserves backend-authored launch targets', async () => {
    mockGet.mockResolvedValueOnce([
      {
        stage: 'foundation_partA',
        status: 'Unlocked',
        scaledScore: null,
        completedAt: null,
        actionHref: '/listening/player/lp-001?mode=practice&pathwayStage=foundation_partA',
      },
    ]);

    const result = await listeningV2Api.myPathway();

    expect(mockGet).toHaveBeenCalledWith('/v1/listening/v2/me/pathway');
    expect(result[0].actionHref).toBe('/listening/player/lp-001?mode=practice&pathwayStage=foundation_partA');
  });
});

describe('teacherClassApi', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('requests owner-scoped class analytics with an encoded class id and drops accidental raw fields', async () => {
    mockGet.mockResolvedValueOnce({
      classId: 'class 1',
      className: 'Clinical Group',
      description: null,
      memberCount: 2,
      analytics: {
        days: 90,
        completedAttempts: 1,
        averageScaledScore: 320,
        percentLikelyPassing: 0,
        classPartAverages: [],
        hardestQuestions: [],
        distractorHeat: [
          {
            paperId: 'paper-1',
            questionNumber: 8,
            correctAnswer: 'B',
            wrongAnswerCount: 3,
            wrongAnswerHistogram: { 'patient.email@example.test': 2, C: 1 },
          },
        ],
        commonMisspellings: [{ correct: 'five', wrong: 'fiv', count: 1 }],
      },
    });

    const result = await teacherClassApi.analytics('class 1', 90);

    expect(mockGet).toHaveBeenCalledWith('/v1/listening/v2/teacher/classes/class%201/analytics?days=90');
    expect(result.analytics.distractorHeat).toEqual([
      { paperId: 'paper-1', questionNumber: 8, correctAnswer: 'B', wrongAnswerCount: 3 },
    ]);
    expect(result.analytics).not.toHaveProperty('commonMisspellings');
    expect(result.analytics.distractorHeat[0]).not.toHaveProperty('wrongAnswerHistogram');
  });

  it('accepts a teacher-shaped distractor heat payload', async () => {
    mockGet.mockResolvedValueOnce({
      classId: 'class-1',
      className: 'Clinical Group',
      description: null,
      memberCount: 2,
      analytics: {
        days: 30,
        completedAttempts: 1,
        averageScaledScore: 320,
        percentLikelyPassing: 0,
        classPartAverages: [],
        hardestQuestions: [],
        distractorHeat: [
          {
            paperId: 'paper-1',
            questionNumber: 8,
            correctAnswer: 'B',
            wrongAnswerCount: 4,
          },
        ],
        commonMisspellings: [],
      },
    });

    const result = await teacherClassApi.analytics('class-1');

    expect(result.analytics.distractorHeat[0]?.wrongAnswerCount).toBe(4);
  });
});
