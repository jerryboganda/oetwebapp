import { render, screen } from '@testing-library/react';

const { useAuthMock, useStreakMock, useXpMock, useFeatureFlagMapMock, useIncreaseMock } = vi.hoisted(() => ({
  useAuthMock: vi.fn(),
  useStreakMock: vi.fn(),
  useXpMock: vi.fn(),
  useFeatureFlagMapMock: vi.fn(),
  useIncreaseMock: vi.fn(),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => useAuthMock(),
}));

vi.mock('@/lib/query/hooks', () => ({
  useStreak: (...args: unknown[]) => useStreakMock(...args),
  useXp: (...args: unknown[]) => useXpMock(...args),
}));

vi.mock('@/hooks/use-feature-flag-map', () => ({
  useFeatureFlagMap: (...args: unknown[]) => useFeatureFlagMapMock(...args),
}));

vi.mock('@/hooks/use-increase-since-last-visit', () => ({
  useIncreaseSinceLastVisit: () => useIncreaseMock(),
}));

import { LearnerStreakBadges } from '../learner-streak-badges';

const STREAK_DATA = { currentStreak: 6 };
const XP_DATA = { level: 4, totalXP: 1_500, currentLevelXP: 1_000, nextLevelXP: 2_000 };

describe('LearnerStreakBadges', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAuthMock.mockReturnValue({ user: { userId: 'u1', displayName: 'Aisha Khan' } });
    useStreakMock.mockReturnValue({ data: STREAK_DATA });
    useXpMock.mockReturnValue({ data: XP_DATA });
    useIncreaseMock.mockReturnValue(0);
  });

  it('renders the streak and level chips when the gamification flag is on', () => {
    useFeatureFlagMapMock.mockReturnValue({ gamification: true });
    render(<LearnerStreakBadges />);

    expect(screen.getByLabelText('Current streak: 6 days')).toBeInTheDocument();
    expect(screen.getByLabelText('Level 4, Beginner, 50% to next level')).toBeInTheDocument();
    // The chips link to the achievements area.
    expect(screen.getAllByRole('link', { name: /level 4|current streak/i }).length).toBe(2);
  });

  it('hides the badges only on an explicit gamification=false', () => {
    useFeatureFlagMapMock.mockReturnValue({ gamification: false });
    render(<LearnerStreakBadges />);

    expect(screen.queryByLabelText(/current streak/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/level \d/i)).not.toBeInTheDocument();
  });

  it('stays visible while the flag map is unresolved or the fetch failed (fail-open header)', () => {
    // useFeatureFlagMap returns {} until it resolves and forever on failure.
    useFeatureFlagMapMock.mockReturnValue({});
    render(<LearnerStreakBadges />);

    expect(screen.getByLabelText('Current streak: 6 days')).toBeInTheDocument();
  });

  it('keeps the query hooks disabled until there is a user id', () => {
    useAuthMock.mockReturnValue({ user: null });
    useStreakMock.mockReturnValue({ data: undefined });
    useXpMock.mockReturnValue({ data: undefined });
    useFeatureFlagMapMock.mockReturnValue({});
    render(<LearnerStreakBadges />);

    expect(useStreakMock).toHaveBeenCalledWith('', { enabled: false });
    expect(useXpMock.mock.lastCall?.[1]).toEqual({ enabled: false });
    expect(useFeatureFlagMapMock).toHaveBeenCalledWith(['gamification'], false);
    expect(screen.queryByLabelText(/current streak/i)).not.toBeInTheDocument();
  });
});
