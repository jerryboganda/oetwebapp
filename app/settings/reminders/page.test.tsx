import { screen } from '@testing-library/react';

const { mockTrack } = vi.hoisted(() => ({
  mockTrack: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-preferences-panel', () => ({
  NotificationPreferencesPanel: ({ title, description }: { title: string; description: string }) => (
    <section data-testid="notification-preferences-panel">
      <h2>{title}</h2>
      <p>{description}</p>
    </section>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: mockTrack,
  },
}));

import SmartRemindersPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('Smart reminders page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('uses the shared notification preferences panel instead of fake local-only reminder settings', () => {
    renderWithRouter(<SmartRemindersPage />);

    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
    expect(screen.getByText('Smart Study Reminders')).toBeInTheDocument();
    expect(screen.getByText('Production reminder controls')).toBeInTheDocument();
    expect(screen.getByTestId('notification-preferences-panel')).toBeInTheDocument();
    expect(screen.getByText('Reminder notification preferences')).toBeInTheDocument();
    expect(screen.getByText(/\/v1\/notifications\/preferences/i)).toBeInTheDocument();
    expect(screen.queryByText('Preferred Study Time')).not.toBeInTheDocument();
    expect(screen.queryByText('Reminder preferences saved!')).not.toBeInTheDocument();
    expect(mockTrack).toHaveBeenCalledWith('content_view', { page: 'smart-reminders' });
  });
});
