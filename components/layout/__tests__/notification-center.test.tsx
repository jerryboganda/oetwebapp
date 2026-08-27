import { fireEvent, screen } from '@testing-library/react';
const { useAdminAlertsMock } = vi.hoisted(() => ({
  useAdminAlertsMock: vi.fn(),
}));
vi.mock('@/hooks/use-admin-alerts', () => ({
  useAdminAlerts: useAdminAlertsMock,
}));
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
  let originalLocation: Location;

  beforeAll(() => {
    originalLocation = window.location;
    Object.defineProperty(window, 'location', {
      configurable: true,
      writable: true,
      value: { assign: vi.fn() } as unknown as Location,
    });
  });

  afterAll(() => {
    Object.defineProperty(window, 'location', {
      configurable: true,
      writable: true,
      value: originalLocation,
    });
  });

  beforeEach(() => {
    vi.clearAllMocks();
    useAdminAlertsMock.mockReturnValue({ status: 'ready', alerts: [], totalAlertCount: 0 });
  });

  it('opens the notification popover when the desktop bell is clicked', () => {
    renderWithRouter(<NotificationCenter />);

    const [desktopBell] = screen.getAllByRole('button', { name: /notifications/i });
    fireEvent.click(desktopBell);

    expect(screen.getByText(/no notifications yet/i)).toBeInTheDocument();
  });

  it('adds the admin alert total to the existing unread pill rather than a new dot', () => {
    useAdminAlertsMock.mockReturnValue({ status: 'ready', alerts: [], totalAlertCount: 3 });
    renderWithRouter(<NotificationCenter />);

    // Inbox unread (8) + admin alerts (3) → 11 inside the single existing pill.
    expect(screen.getAllByRole('button', { name: /notifications \(11 unread\)/i }).length).toBeGreaterThan(0);
  });

  it('renders the Admin alerts section above the inbox and navigates without calling markRead', () => {
    const detectedAt = new Date().toISOString();
    useAdminAlertsMock.mockReturnValue({
      status: 'ready',
      alerts: [
        {
          alertType: 'pending_fulfilment',
          severity: 'warning',
          title: 'Pending Fulfilment',
          description: '3 paid order(s) waiting for fulfilment',
          actionRoute: '/admin/billing/manual-payments?tab=fulfilment',
          detectedAt,
        },
        {
          alertType: 'pending_payment_proofs',
          severity: 'info',
          title: 'Payment Proofs Pending',
          description: '2 payment proof(s) pending review',
          actionRoute: '/admin/billing/manual-payments?tab=proofs',
          detectedAt,
        },
      ],
      totalAlertCount: 5,
    });
    renderWithRouter(<NotificationCenter />);

    const [desktopBell] = screen.getAllByRole('button', { name: /notifications \(13 unread\)/i });
    fireEvent.click(desktopBell);

    // Pinned group header renders with its own count chip.
    expect(screen.getByText(/admin alerts/i)).toBeInTheDocument();
    expect(screen.getByText('Pending Fulfilment')).toBeInTheDocument();
    expect(screen.getByText(/3 paid order\(s\) waiting for fulfilment/i)).toBeInTheDocument();
    expect(screen.getByText('Payment Proofs Pending')).toBeInTheDocument();

    fireEvent.click(screen.getByText(/3 paid order\(s\) waiting for fulfilment/i));

    expect(window.location.assign).toHaveBeenCalledWith('/admin/billing/manual-payments?tab=fulfilment');
    expect(mockNotificationContext.markRead).not.toHaveBeenCalled();
  });
});
