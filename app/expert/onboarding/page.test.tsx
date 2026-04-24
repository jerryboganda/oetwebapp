import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockPush, mockReplace, mockTrack } = vi.hoisted(() => ({
  mockPush: vi.fn(),
  mockReplace: vi.fn(),
  mockTrack: vi.fn(),
}));

const api = vi.hoisted(() => ({
  fetchExpertOnboardingStatus: vi.fn(),
  saveExpertOnboardingProfile: vi.fn(),
  saveExpertOnboardingQualifications: vi.fn(),
  saveExpertSchedule: vi.fn(),
  fetchExpertSchedule: vi.fn(),
  saveExpertOnboardingRates: vi.fn(),
  completeExpertOnboarding: vi.fn(),
  isApiError: () => false,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/api', () => ({
  fetchExpertOnboardingStatus: api.fetchExpertOnboardingStatus,
  saveExpertOnboardingProfile: api.saveExpertOnboardingProfile,
  saveExpertOnboardingQualifications: api.saveExpertOnboardingQualifications,
  saveExpertSchedule: api.saveExpertSchedule,
  fetchExpertSchedule: api.fetchExpertSchedule,
  saveExpertOnboardingRates: api.saveExpertOnboardingRates,
  completeExpertOnboarding: api.completeExpertOnboarding,
  isApiError: api.isApiError,
}));

import ExpertOnboardingPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

function renderPage() {
  return renderWithRouter(<ExpertOnboardingPage />, {
    pathname: '/expert/onboarding',
    searchParams: new URLSearchParams(),
    params: {},
    router: { push: mockPush, replace: mockReplace, back: vi.fn(), refresh: vi.fn() },
  });
}

const defaultStatus = {
  isComplete: false,
  completedSteps: [],
  profile: null,
  qualifications: null,
  rates: null,
};

describe('ExpertOnboardingPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.fetchExpertOnboardingStatus.mockResolvedValue(defaultStatus);
    api.fetchExpertSchedule.mockResolvedValue({
      timezone: 'UTC',
      days: {},
      lastUpdatedAt: null,
    });
    api.saveExpertOnboardingProfile.mockResolvedValue({});
    api.saveExpertOnboardingQualifications.mockResolvedValue({});
    api.saveExpertSchedule.mockResolvedValue({});
    api.saveExpertOnboardingRates.mockResolvedValue({});
    api.completeExpertOnboarding.mockResolvedValue({ completed: true });
  });

  it('renders the welcome step with stepper', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Welcome to the Expert Console')).toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: /continue/i })).toBeInTheDocument();
  });

  it('shows progress stepper with all 6 steps', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Welcome to the Expert Console')).toBeInTheDocument();
    });
    expect(screen.getAllByText('Profile').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Qualifications').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Schedule').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Rates').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Review').length).toBeGreaterThanOrEqual(1);
  });

  it('navigates forward on Continue', async () => {
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Welcome to the Expert Console')).toBeInTheDocument();
    });
    await user.click(screen.getByRole('button', { name: /continue/i }));
    await waitFor(() => {
      expect(screen.getByText(/your name/i)).toBeInTheDocument();
    });
  });

  it('navigates backward on Back', async () => {
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Welcome to the Expert Console')).toBeInTheDocument();
    });
    await user.click(screen.getByRole('button', { name: /continue/i }));
    await waitFor(() => {
      expect(screen.getByText(/your name/i)).toBeInTheDocument();
    });
    await user.click(screen.getByRole('button', { name: /back/i }));
    await waitFor(() => {
      expect(screen.getByText('Welcome to the Expert Console')).toBeInTheDocument();
    });
  });

  it('validates required fields on profile step', async () => {
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Welcome to the Expert Console')).toBeInTheDocument();
    });
    // Go to profile step
    await user.click(screen.getByRole('button', { name: /continue/i }));
    await waitFor(() => {
      expect(screen.getByText(/your name/i)).toBeInTheDocument();
    });
    // Try to advance without filling required fields
    await user.click(screen.getByRole('button', { name: /save & continue/i }));
    await waitFor(() => {
      expect(screen.getByText(/display name is required/i)).toBeInTheDocument();
    });
  });

  it('redirects to dashboard if onboarding already complete', async () => {
    api.fetchExpertOnboardingStatus.mockResolvedValue({
      ...defaultStatus,
      isComplete: true,
      completedSteps: ['welcome', 'profile', 'qualifications', 'schedule', 'rates', 'review'],
    });
    renderPage();
    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith('/expert');
    });
  });

  it('calls completeExpertOnboarding and redirects on final step', async () => {
    api.fetchExpertOnboardingStatus.mockResolvedValue({
      isComplete: false,
      completedSteps: ['welcome', 'profile', 'qualifications', 'schedule', 'rates'],
      profile: { displayName: 'Dr Test', bio: 'Expert in OET' },
      qualifications: { qualifications: 'MBBS', certifications: 'OET Trainer', experienceYears: 5 },
      rates: { hourlyRateMinorUnits: 5000, sessionRateMinorUnits: 7500, currency: 'GBP' },
    });
    renderPage();

    // Should land on review step since all prior steps completed
    await waitFor(() => {
      expect(screen.getByText('Review & Complete')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole('button', { name: /complete setup/i }));
    await waitFor(() => {
      expect(api.completeExpertOnboarding).toHaveBeenCalled();
    });
    await waitFor(() => {
      expect(mockPush).toHaveBeenCalledWith('/expert');
    });
  });
});
