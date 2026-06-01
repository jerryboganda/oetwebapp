import { render, screen, waitFor } from '@testing-library/react';

const {
  mockRouterPush,
  mockRouterReplace,
  mockUseAuth,
  mockUseReadingProfile,
} = vi.hoisted(() => ({
  mockRouterPush: vi.fn(),
  mockRouterReplace: vi.fn(),
  mockUseAuth: vi.fn(),
  mockUseReadingProfile: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: mockRouterPush,
    replace: mockRouterReplace,
  }),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock('@/hooks/useReadingProfile', () => ({
  useReadingProfile: () => mockUseReadingProfile(),
}));

import DiagnosticPage from './page';
import type { LearnerReadingProfileDto } from '@/lib/reading-pathway-api';

function buildProfile(overrides: Partial<LearnerReadingProfileDto> = {}): LearnerReadingProfileDto {
  return {
    userId: 'learner-1',
    currentStage: 'diagnostic',
    targetBand: 'B',
    examDate: null,
    hoursPerWeek: 6,
    profession: 'medicine',
    hasTakenBefore: false,
    previousScore: null,
    selfRatedSpeed: 3,
    selfRatedVocabulary: 3,
    readinessScore: null,
    predictedScore: null,
    onboardingCompletedAt: null,
    pathwayGeneratedAt: null,
    weeksRemaining: null,
    diagnosticCompleted: false,
    ...overrides,
  };
}

describe('Reading diagnostic page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({ isAuthenticated: true, loading: false });
    mockUseReadingProfile.mockReturnValue({
      profile: buildProfile(),
      isLoading: false,
      error: null,
      mutate: vi.fn(),
    });
  });

  it('redirects onboarding learners to profile setup before the diagnostic begins', async () => {
    mockUseReadingProfile.mockReturnValue({
      profile: buildProfile({ currentStage: 'onboarding' }),
      isLoading: false,
      error: null,
      mutate: vi.fn(),
    });

    render(<DiagnosticPage />);

    await waitFor(() => {
      expect(mockRouterReplace).toHaveBeenCalledWith('/reading/profile-setup');
    });

    expect(screen.queryByRole('button', { name: /i'm ready, start diagnostic/i })).not.toBeInTheDocument();
    expect(mockRouterPush).not.toHaveBeenCalled();
  });

  it('keeps diagnostic-stage learners on the diagnostic briefing', () => {
    render(<DiagnosticPage />);

    expect(screen.getByRole('button', { name: /i'm ready, start diagnostic/i })).toBeInTheDocument();
    expect(mockRouterReplace).not.toHaveBeenCalled();
    expect(mockRouterPush).not.toHaveBeenCalled();
  });
});