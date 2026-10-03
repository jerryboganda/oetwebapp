import { fireEvent, render, screen } from '@testing-library/react';
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
  connectionStatus: 'connected' as string,
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
  useOptionalNotificationState: () => mockNotificationContext,
  cloneNotificationPreferences: () => null,
}));

vi.mock('../notification-preferences-panel', () => ({
  NotificationPreferencesPanel: () => <div>Preferences panel</div>,
}));

import { NotificationCenter } from '../notification-center';
import { NextRouterProvider, renderWithRouter } from '@/tests/test-utils';

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

    // Inbox unread (8) + admin alerts (3) inside the single existing pill — and
    // the accessible name states both parts instead of the old sum-as-unread.
    expect(screen.getAllByRole('button', { name: /notifications \(8 unread, 3 admin alerts\)/i }).length).toBeGreaterThan(0);
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

    const [desktopBell] = screen.getAllByRole('button', { name: /notifications \(8 unread, 5 admin alerts\)/i });
    fireEvent.click(desktopBell);

    // Pinned group header renders with its own count chip. Exact text — the
    // popover header line now also mentions admin alerts ("N admin alerts").
    expect(screen.getByText('Admin alerts')).toBeInTheDocument();
    expect(screen.getByText('Pending Fulfilment')).toBeInTheDocument();
    expect(screen.getByText(/3 paid order\(s\) waiting for fulfilment/i)).toBeInTheDocument();
    expect(screen.getByText('Payment Proofs Pending')).toBeInTheDocument();

    fireEvent.click(screen.getByText(/3 paid order\(s\) waiting for fulfilment/i));

    expect(window.location.assign).toHaveBeenCalledWith('/admin/billing/manual-payments?tab=fulfilment');
    expect(mockNotificationContext.markRead).not.toHaveBeenCalled();
  });

  it('closes the desktop popover and the mobile drawer when the pathname changes', () => {
    const at = (pathname: string) => (
      <NextRouterProvider pathname={pathname}>
        <NotificationCenter />
      </NextRouterProvider>
    );
    const { rerender } = render(at('/admin'));
    const [desktopBell, mobileBell] = screen.getAllByRole('button', { name: /notifications/i });

    fireEvent.click(desktopBell);
    expect(screen.getByText(/no notifications yet/i)).toBeInTheDocument();
    rerender(at('/admin/users'));
    expect(screen.queryByText(/no notifications yet/i)).not.toBeInTheDocument();

    fireEvent.click(mobileBell);
    expect(screen.getByText(/no notifications yet/i)).toBeInTheDocument();
    rerender(at('/admin/billing'));
    expect(screen.queryByText(/no notifications yet/i)).not.toBeInTheDocument();
  });

  describe('bell pill and header counts', () => {
    afterEach(() => {
      mockNotificationContext.unreadCount = 8;
      mockNotificationContext.connectionStatus = 'connected';
    });

    it('caps the pill at 99+', () => {
      mockNotificationContext.unreadCount = 95;
      useAdminAlertsMock.mockReturnValue({ status: 'ready', alerts: [], totalAlertCount: 10 });
      renderWithRouter(<NotificationCenter />);

      // 95 + 10 = 105 → the pill shows the cap, not the raw sum.
      expect(screen.getAllByText('99+').length).toBeGreaterThan(0);
      expect(screen.queryByText('105')).not.toBeInTheDocument();
    });

    it('keeps the popover header in step with the pill when admin alerts are pinned', () => {
      useAdminAlertsMock.mockReturnValue({ status: 'ready', alerts: [], totalAlertCount: 3 });
      renderWithRouter(<NotificationCenter />);

      fireEvent.click(screen.getAllByRole('button', { name: /notifications \(8 unread, 3 admin alerts\)/i })[0]);

      expect(screen.getByText(/8 unread · 3 admin alerts · 8 total/i)).toBeInTheDocument();
    });

    it('treats a fresh mount as connecting — no offline banner or degraded dot until a real failure', () => {
      mockNotificationContext.connectionStatus = 'connecting';
      const fresh = renderWithRouter(<NotificationCenter />);
      expect(fresh.queryByLabelText(/live updates paused/i)).not.toBeInTheDocument();
      fresh.unmount();

      // A real failed attempt is the only thing that may park it offline.
      mockNotificationContext.connectionStatus = 'disconnected';
      renderWithRouter(<NotificationCenter />);
      fireEvent.click(screen.getAllByRole('button', { name: /notifications/i })[0]);

      expect(screen.getByText(/offline\. updates may be delayed/i)).toBeInTheDocument();
      expect(screen.getAllByLabelText(/live updates paused/i).length).toBeGreaterThan(0);
    });
  });
});
