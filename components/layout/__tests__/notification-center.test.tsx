import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: vi.fn(),
  }),
}));

vi.mock('@/contexts/notification-center-context', () => ({
  useNotificationCenter: () => ({
    notifications: [],
    unreadCount: 8,
    totalCount: 8,
    hasMore: false,
    isLoading: false,
    isRefreshing: false,
    error: null,
    connectionStatus: 'connected',
    preferences: null,
    isPreferencesLoading: false,
    preferencesError: null,
    isUpdatingPreferences: false,
    pushSupported: false,
    pushPublicKeyConfigured: false,
    pushPermission: 'default',
    pushEnabled: false,
    isUpdatingPush: false,
    refreshFeed: vi.fn().mockResolvedValue(undefined),
    loadMore: vi.fn().mockResolvedValue(undefined),
    markRead: vi.fn().mockResolvedValue(undefined),
    markAllRead: vi.fn().mockResolvedValue(undefined),
    updatePreferences: vi.fn().mockResolvedValue(undefined),
    subscribeToPush: vi.fn().mockResolvedValue(undefined),
    unsubscribeFromPush: vi.fn().mockResolvedValue(undefined),
  }),
  cloneNotificationPreferences: () => null,
}));

vi.mock('../notification-preferences-panel', () => ({
  NotificationPreferencesPanel: () => <div>Preferences panel</div>,
}));

import { NotificationCenter } from '../notification-center';

describe('NotificationCenter', () => {
  it('opens the notification popover when the desktop bell is clicked', () => {
    render(<NotificationCenter />);

    const [desktopBell] = screen.getAllByRole('button', { name: /notifications/i });
    fireEvent.click(desktopBell);

    expect(screen.getByText(/notifications sync live over signalr/i)).toBeInTheDocument();
    expect(screen.getByText(/preferences panel/i)).toBeInTheDocument();
  });
});
