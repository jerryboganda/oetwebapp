import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
const { mockFetchSettingsData, mockFetchUserProfile, mockFetchFreezeStatus, mockUpdateSettingsSection, mockTrack } = vi.hoisted(() => ({
  mockFetchSettingsData: vi.fn(),
  mockFetchUserProfile: vi.fn(),
  mockFetchFreezeStatus: vi.fn(),
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
  fetchFreezeStatus: mockFetchFreezeStatus,
  updateSettingsSection: mockUpdateSettingsSection,
}));

import SettingsPage from './page';
import { renderWithRouter } from '@/tests/test-utils';
import { queryKeys } from '@/lib/query/hooks';

describe('Settings page', () => {
  function renderSettings(client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })) {
    return {
      client,
      ...renderWithRouter(
        <QueryClientProvider client={client}>
          <SettingsPage />
        </QueryClientProvider>,
      ),
    };
  }

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
    mockFetchFreezeStatus.mockResolvedValue({ currentFreeze: null });
    mockUpdateSettingsSection.mockResolvedValue({});
  });

  it('renders through the shared learner dashboard shell without a second page-root width wrapper', async () => {
    const { container } = renderSettings();

    expect(await screen.findByText('Adjust account and study settings without hunting for them')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
    expect(container.querySelector('[class*="max-w-3xl"][class*="mx-auto"][class*="px-4"]')).not.toBeInTheDocument();
  });

  it('invalidates cached settings after a preference mutation succeeds', async () => {
    const user = userEvent.setup();
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    const invalidateSpy = vi.spyOn(client, 'invalidateQueries');
    renderSettings(client);

    await user.click(await screen.findByRole('switch', { name: 'Toggle Low-Bandwidth Mode' }));

    await waitFor(() => {
      expect(mockUpdateSettingsSection).toHaveBeenCalledWith('audio', { lowBandwidthMode: true });
    });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.settings.home('current') });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: queryKeys.settings.section('current', 'audio'),
    });
  });
});
