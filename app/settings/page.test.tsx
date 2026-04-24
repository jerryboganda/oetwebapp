import { screen } from '@testing-library/react';
const { mockFetchSettingsData, mockFetchUserProfile, mockUpdateSettingsSection, mockTrack } = vi.hoisted(() => ({
  mockFetchSettingsData: vi.fn(),
  mockFetchUserProfile: vi.fn(),
  mockUpdateSettingsSection: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));


vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: mockTrack,
  },
}));

vi.mock('@/lib/api', () => ({
  fetchSettingsData: mockFetchSettingsData,
  fetchUserProfile: mockFetchUserProfile,
  updateSettingsSection: mockUpdateSettingsSection,
}));

import SettingsPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('Settings page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchSettingsData.mockResolvedValue({
      audio: {
        lowBandwidthMode: false,
      },
    });
    mockFetchUserProfile.mockResolvedValue({
      displayName: 'Learner',
      email: 'learner@example.com',
    });
    mockUpdateSettingsSection.mockResolvedValue({});
  });

  it('renders through the shared learner dashboard shell without a second page-root width wrapper', async () => {
    const { container } = renderWithRouter(<SettingsPage />);

    expect(await screen.findByText('Adjust account and study settings without hunting for them')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
    expect(container.querySelector('[class*="max-w-3xl"][class*="mx-auto"][class*="px-4"]')).not.toBeInTheDocument();
  });
});
