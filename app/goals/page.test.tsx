import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
const { mockFetchExamFamilies, mockFetchUserProfile, mockUpdateUserProfile, mockPush, mockTrack } = vi.hoisted(() => ({
  mockFetchExamFamilies: vi.fn(),
  mockFetchUserProfile: vi.fn(),
  mockUpdateUserProfile: vi.fn(),
  mockPush: vi.fn(),
  mockTrack: vi.fn(),
}));
vi.mock('@/components/layout/app-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/admin-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/expert-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/sponsor-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-workspace-container', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-center', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-preferences-panel', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/top-nav', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/sidebar', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/hooks/use-analytics', () => ({ useAnalytics: () => ({ track: mockTrack }) }));
vi.mock('@/lib/api', () => ({ fetchExamFamilies: mockFetchExamFamilies, fetchUserProfile: mockFetchUserProfile, updateUserProfile: mockUpdateUserProfile }));

import GoalsPage from './page';

describe('Goals setup page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchExamFamilies.mockResolvedValue([{ code: 'oet', name: 'OET', isActive: true }]);
    mockFetchUserProfile.mockResolvedValue({ profession: 'medicine', examFamilyCode: 'oet' });
    mockUpdateUserProfile.mockResolvedValue({});
  });

  it('renders the goals form through the shared learner dashboard shell', () => {
    renderWithRouter(<GoalsPage />, { router: { push: mockPush } });
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('displays exam family selector', () => {
    renderWithRouter(<GoalsPage />, { router: { push: mockPush } });
    expect(screen.getByText('OET')).toBeInTheDocument();
  });
});
