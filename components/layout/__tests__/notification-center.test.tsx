import { fireEvent, screen } from '@testing-library/react';
const mockNotificationContext = {
  notifications: [],
  unreadCount: 8,
  totalCount: 8,
  hasMore: false,
  isLoading: false,
  isRefreshing: false,
  error: null,
  connectionStatus: 'connected' as const,
  preferences: null,
  isPreferencesLoading: false,
  preferencesError: null,
  isUpdatingPreferences: false,
  pushSupported: false,
  pushPublicKeyConfigured: false,
  pushPermission: 'default' as const,
  pushEnabled: false,
  isUpdatingPush: false,
  refreshFeed: vi.fn().mockResolvedValue(undefined),
  loadMore: vi.fn().mockResolvedValue(undefined),
  markRead: vi.fn().mockResolvedValue(undefined),
  markAllRead: vi.fn().mockResolvedValue(undefined),
  updatePreferences: vi.fn().mockResolvedValue(undefined),
  subscribeToPush: vi.fn().mockResolvedValue(undefined),
  unsubscribeFromPush: vi.fn().mockResolvedValue(undefined),
};
vi.mock('@/contexts/notification-center-context', () => ({
  useNotificationCenter: () => mockNotificationContext,
  useNotificationState: () => mockNotificationContext,
  cloneNotificationPreferences: () => null,
}));

vi.mock('../notification-preferences-panel', () => ({
  NotificationPreferencesPanel: () => <div>Preferences panel</div>,
}));

import { NotificationCenter } from '../notification-center';
import { renderWithRouter } from '@/tests/test-utils';

describe('NotificationCenter', () => {
  it('opens the notification popover when the desktop bell is clicked', () => {
    renderWithRouter(<NotificationCenter />);

    const [desktopBell] = screen.getAllByRole('button', { name: /notifications/i });
    fireEvent.click(desktopBell);

    expect(screen.getByText(/no notifications yet/i)).toBeInTheDocument();
  });
});
