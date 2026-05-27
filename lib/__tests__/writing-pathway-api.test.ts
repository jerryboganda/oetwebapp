import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockGet, mockPost } = vi.hoisted(() => ({
  mockGet: vi.fn(),
  mockPost: vi.fn(),
}));

vi.mock('../api', () => ({
  apiClient: {
    get: mockGet,
    post: mockPost,
  },
}));

export {};

const api = await import('../writing-pathway-api');

describe('writing-pathway-api', () => {
  beforeEach(() => {
    mockGet.mockReset();
    mockPost.mockReset();
  });

  it('submits Writing onboarding with pathway field names', async () => {
    mockPost.mockResolvedValue({ userId: 'u1', currentStage: 'diagnostic' });

    const request = {
      profession: 'medicine',
      targetBand: 'B+',
      examDate: null,
      daysPerWeek: 5,
      minutesPerDay: 45,
      targetCountry: 'GB',
      letterTypeFocus: ['LT-RR', 'LT-DG'],
    };
    await api.submitWritingOnboarding(request);

    expect(mockPost).toHaveBeenCalledWith('/v1/writing-pathway/onboarding', request);
  });

  it('loads today plan through the deterministic pathway endpoint', async () => {
    mockGet.mockResolvedValue({ date: '2026-05-27', items: [], totalMinutes: 0, completedCount: 0 });

    const plan = await api.getWritingTodayPlan();

    expect(mockGet).toHaveBeenCalledWith('/v1/writing-pathway/plan/today');
    expect(plan.date).toBe('2026-05-27');
  });

  it('passes search filters to the canon endpoint', async () => {
    mockGet.mockResolvedValue({ rules: [], recentViolations: [], totalRules: 0, totalRecentViolations: 0 });

    await api.getWritingCanon({ search: 'patient', severity: 'critical' });

    expect(mockGet).toHaveBeenCalledWith('/v1/writing-pathway/canon?search=patient&severity=critical');
  });

  it('marks plan items completed through the shared api client', async () => {
    mockPost.mockResolvedValue(undefined);

    await api.completeWritingPlanItem('item-1');

    expect(mockPost).toHaveBeenCalledWith('/v1/writing-pathway/plan/items/item-1/complete');
  });
});