import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchConfig,
  mockFetchTutors,
  mockFetchAllSlots,
  mockFetchSlots,
  mockCreateBooking,
  mockFetchBookings,
  mockFetchEntitlement,
} = vi.hoisted(() => ({
  mockFetchConfig: vi.fn(),
  mockFetchTutors: vi.fn(),
  mockFetchAllSlots: vi.fn(),
  mockFetchSlots: vi.fn(),
  mockCreateBooking: vi.fn(),
  mockFetchBookings: vi.fn(),
  mockFetchEntitlement: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/billing/paypal-expanded-checkout', () => ({
  PayPalExpandedCheckout: () => null,
}));

vi.mock('@/lib/api/speaking-exams', () => ({ createSpeakingExamFromBooking: vi.fn() }));
vi.mock('@/lib/analytics', () => ({ analytics: { track: vi.fn() } }));

vi.mock('@/lib/api', () => ({
  fetchPrivateSpeakingConfig: mockFetchConfig,
  fetchPrivateSpeakingTutors: mockFetchTutors,
  fetchAllPrivateSpeakingSlots: mockFetchAllSlots,
  fetchPrivateSpeakingSlots: mockFetchSlots,
  createPrivateSpeakingBooking: mockCreateBooking,
  reschedulePrivateSpeakingBooking: vi.fn(),
  fetchLearnerPrivateSpeakingBookings: mockFetchBookings,
  cancelPrivateSpeakingBooking: vi.fn(),
  downloadPrivateSpeakingCalendarInvite: vi.fn(),
  fetchMyEntitlementSnapshot: mockFetchEntitlement,
  ratePrivateSpeakingSession: vi.fn(),
  safePaymentRedirect: (target: string | null | undefined, fallback: string) => target ?? fallback,
  isApiError: (err: unknown) => typeof err === 'object' && err !== null && 'code' in err,
}));

import PrivateSpeakingPage from './page';

const UNAVAILABLE = 'Live tutor sessions are temporarily unavailable.';

const baseConfig = {
  isEnabled: true,
  defaultPriceMinorUnits: 5000,
  currency: 'GBP',
  defaultSlotDurationMinutes: 30,
  cancellationWindowHours: 24,
  allowReschedule: true,
  rescheduleWindowHours: 24,
  reservationTimeoutMinutes: 15,
  liveRoomsAvailable: true,
};

const anySlot = {
  tutorProfileId: 'any',
  tutorDisplayName: 'Any available tutor',
  tutorTimezone: 'UTC',
  date: '2030-01-07',
  startTimeLocal: '09:00',
  startTimeUtc: '2030-01-07T09:00:00Z',
  endTimeUtc: '2030-01-07T09:30:00Z',
  durationMinutes: 30,
  priceMinorUnits: 0,
  currency: 'GBP',
};

describe('Private speaking booking — tutor room availability (B9)', () => {
  beforeAll(() => {
    if (typeof globalThis.crypto?.randomUUID !== 'function') {
      vi.stubGlobal('crypto', { randomUUID: () => 'uuid-test' });
    }
  });

  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchConfig.mockResolvedValue(baseConfig);
    mockFetchTutors.mockResolvedValue([
      { id: 'tutor-1', displayName: 'Tutor One', bio: null, timezone: 'UTC', priceOverrideMinorUnits: null, slotDurationOverrideMinutes: null, specialtiesJson: '[]', averageRating: 0, totalSessions: 0 },
    ]);
    mockFetchAllSlots.mockResolvedValue([]);
    mockFetchSlots.mockResolvedValue([anySlot]);
    mockFetchBookings.mockResolvedValue([]);
    mockFetchEntitlement.mockResolvedValue({ speakingSessionsRemaining: 1, speakingAddonsEnabled: true });
    mockCreateBooking.mockResolvedValue({ bookingId: 'psb-1', entitlementUsed: true, speakingSessionsRemaining: 0 });
  });

  it('shows the unavailable state and hides slot browsing when liveRoomsAvailable is false', async () => {
    mockFetchConfig.mockResolvedValue({ ...baseConfig, liveRoomsAvailable: false });

    render(<PrivateSpeakingPage />);

    expect(await screen.findByTestId('tutor-rooms-unavailable')).toHaveTextContent(UNAVAILABLE);
    expect(screen.queryByRole('tab', { name: 'Browse Slots' })).not.toBeInTheDocument();
    expect(mockFetchAllSlots).not.toHaveBeenCalled();
    expect(mockFetchSlots).not.toHaveBeenCalled();
  });

  it('shows the unavailable state when slot listing answers 503 tutor_rooms_unavailable', async () => {
    mockFetchAllSlots.mockRejectedValue({ code: 'tutor_rooms_unavailable', message: UNAVAILABLE });

    render(<PrivateSpeakingPage />);

    expect(await screen.findByTestId('tutor-rooms-unavailable')).toHaveTextContent(UNAVAILABLE);
    expect(screen.queryByRole('tab', { name: 'Browse Slots' })).not.toBeInTheDocument();
  });

  it('offers "Any available tutor" and books the chosen slot with tutorProfileId "any"', async () => {
    const user = userEvent.setup();
    render(<PrivateSpeakingPage />);

    const tutorFilter = await screen.findByRole('combobox');
    // The control the unavailable-state tests expect to be absent really is this tab.
    expect(screen.getByRole('tab', { name: 'Browse Slots' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Any available tutor' })).toBeInTheDocument();
    await user.selectOptions(tutorFilter, 'any');

    await waitFor(() => expect(mockFetchSlots).toHaveBeenCalledWith('any', expect.any(String), expect.any(String)));
    await user.click(await screen.findByRole('button', { name: /09:00/ }));
    await user.click(screen.getByRole('button', { name: 'Use Session Credit & Book' }));

    await waitFor(() => expect(mockCreateBooking).toHaveBeenCalledWith(
      expect.objectContaining({ tutorProfileId: 'any', sessionStartUtc: anySlot.startTimeUtc }),
    ));
  });

  it('switches to the unavailable state when booking answers 503 tutor_rooms_unavailable', async () => {
    mockCreateBooking.mockRejectedValue({ code: 'tutor_rooms_unavailable', message: UNAVAILABLE });
    const user = userEvent.setup();
    render(<PrivateSpeakingPage />);

    await user.selectOptions(await screen.findByRole('combobox'), 'any');
    await user.click(await screen.findByRole('button', { name: /09:00/ }));
    await user.click(screen.getByRole('button', { name: 'Use Session Credit & Book' }));

    expect(await screen.findByTestId('tutor-rooms-unavailable')).toHaveTextContent(UNAVAILABLE);
  });
});
