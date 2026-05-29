import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockGet, mockPost, mockPut, mockDelete } = vi.hoisted(() => ({
  mockGet: vi.fn(),
  mockPost: vi.fn(),
  mockPut: vi.fn(),
  mockDelete: vi.fn(),
}));

vi.mock('../api', () => ({
  apiClient: {
    get: mockGet,
    post: mockPost,
    put: mockPut,
    delete: mockDelete,
  },
}));

export {};

const api = await import('../reading-tutor-api');

describe('reading-tutor-api', () => {
  beforeEach(() => {
    mockGet.mockReset();
    mockPost.mockReset();
    mockPut.mockReset();
    mockDelete.mockReset();
  });

  it('posts a manual score override to the admin route by default', async () => {
    mockPost.mockResolvedValue({ attemptId: 'a1', hasOverride: true });

    const body = { rawScore: 38, scaledScore: 420, reason: 'Mismarked Part B' };
    await api.overrideReadingAttemptScore('a1', body);

    expect(mockPost).toHaveBeenCalledWith(
      '/v1/admin/reading/attempts/a1/override',
      body,
    );
  });

  it('routes the recalc to the expert prefix when area is expert', async () => {
    mockPost.mockResolvedValue({
      recalculatedCount: 3,
      skippedOverrideCount: 1,
      totalConsidered: 4,
    });

    const result = await api.recalcReadingPaper(
      'paper-7',
      { scope: 'allAttemptsForPaper' },
      'expert',
    );

    expect(mockPost).toHaveBeenCalledWith(
      '/v1/expert/reading/papers/paper-7/recalc',
      { scope: 'allAttemptsForPaper' },
    );
    expect(result.recalculatedCount).toBe(3);
  });

  it('reads the learner-facing assignment list from /v1/reading', async () => {
    mockGet.mockResolvedValue([]);

    await api.listMyReadingAssignments();

    expect(mockGet).toHaveBeenCalledWith('/v1/reading/assignments');
  });

  it('encodes paperId and joins userIds for cohort analytics', async () => {
    mockGet.mockResolvedValue({ paperId: 'p1', studentCount: 2 });

    await api.getReadingCohortAnalytics({ paperId: 'p 1', userIds: ['u1', 'u2'] });

    expect(mockGet).toHaveBeenCalledWith(
      '/v1/admin/reading/analytics/cohort?paperId=p+1&userIds=u1%2Cu2',
    );
  });
});
