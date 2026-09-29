import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
const { mockFetchOnboardingState, mockStartOnboarding, mockCompleteOnboarding, mockPush, mockTrack } = vi.hoisted(() => ({
  mockFetchOnboardingState: vi.fn(),
  mockStartOnboarding: vi.fn(),
  mockCompleteOnboarding: vi.fn(),
  mockPush: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/hooks/use-analytics', () => ({ useAnalytics: () => ({ track: mockTrack }) }));
vi.mock('@/lib/api', () => ({ fetchOnboardingState: mockFetchOnboardingState, startOnboarding: mockStartOnboarding, completeOnboarding: mockCompleteOnboarding }));

import OnboardingPage from './page';

describe('Onboarding page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchOnboardingState.mockResolvedValue({ completed: false, currentStep: 1 });
    mockStartOnboarding.mockResolvedValue({});
    mockCompleteOnboarding.mockResolvedValue({});
  });

  it('renders the first onboarding step through the shared learner dashboard shell', async () => {
    renderWithRouter(<OnboardingPage />, { router: { push: mockPush } });
    const matches = await screen.findAllByText('What is the OET?');
    expect(matches.length).toBeGreaterThan(0);
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('tracks onboarding_started analytics when starting fresh', async () => {
    renderWithRouter(<OnboardingPage />, { router: { push: mockPush } });
    await screen.findAllByText('What is the OET?');
    expect(mockTrack).toHaveBeenCalledWith('onboarding_started');
  });

  it('displays stepper progress and navigation buttons', async () => {
    renderWithRouter(<OnboardingPage />, { router: { push: mockPush } });
    expect(await screen.findByText('1 of 3')).toBeInTheDocument();
  });
});
