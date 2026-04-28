import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchFreezeStatus,
  mockRequestFreeze,
  mockCancelFreeze,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchFreezeStatus: vi.fn(),
  mockRequestFreeze: vi.fn(),
  mockCancelFreeze: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: mockTrack,
  },
}));

vi.mock('@/lib/api', () => ({
  fetchFreezeStatus: mockFetchFreezeStatus,
  requestFreeze: mockRequestFreeze,
  cancelFreeze: mockCancelFreeze,
}));

import FreezePage from './page';
import { renderWithRouter } from '@/tests/test-utils';

const basePolicy = {
  isEnabled: true,
  selfServiceEnabled: true,
  approvalMode: 'AdminApprovalRequired',
  accessMode: 'ReadOnly',
  minDurationDays: 1,
  maxDurationDays: 30,
  allowScheduling: true,
  entitlementPauseMode: 'InternalClock',
  requireReason: true,
  requireInternalNotes: false,
  allowActivePaid: true,
  allowGracePeriod: true,
  allowTrial: false,
  allowComplimentary: false,
  allowCancelled: false,
  allowExpired: false,
  allowReviewOnly: false,
  allowPastDue: false,
  allowSuspended: false,
};

describe('Freeze page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockRequestFreeze.mockResolvedValue({});
    mockCancelFreeze.mockResolvedValue({});
    mockFetchFreezeStatus.mockResolvedValue({
      policy: basePolicy,
      currentFreeze: null,
      entitlement: null,
      eligibility: { eligible: true, canRequest: true, canSchedule: true, reasonCodes: [] },
      history: [],
    });
  });

  it('does not expose learner confirm for admin-approval requests', async () => {
    mockFetchFreezeStatus.mockResolvedValueOnce({
      policy: basePolicy,
      currentFreeze: {
        id: 'freeze-1',
        userId: 'learner-1',
        status: 'PendingApproval',
        requestedAt: '2026-01-01T00:00:00Z',
        startedAt: null,
        endedAt: '2026-01-08T00:00:00Z',
      },
      entitlement: null,
      eligibility: { eligible: false, canRequest: false, canSchedule: true, reasonCodes: ['current_freeze_exists'] },
      history: [],
    }).mockResolvedValueOnce({
      policy: basePolicy,
      currentFreeze: null,
      entitlement: null,
      eligibility: { eligible: true, canRequest: true, canSchedule: true, reasonCodes: [] },
      history: [],
    });

    const user = userEvent.setup();
    renderWithRouter(<FreezePage />);

    expect(await screen.findByText(/waiting for admin approval/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^confirm$/i })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /^cancel$/i }));

    await waitFor(() => expect(mockCancelFreeze).toHaveBeenCalledWith('freeze-1'));
  });

  it('disables future start selection when scheduling is disabled', async () => {
    mockFetchFreezeStatus.mockResolvedValueOnce({
      policy: { ...basePolicy, allowScheduling: false },
      currentFreeze: null,
      entitlement: null,
      eligibility: { eligible: true, canRequest: true, canSchedule: false, reasonCodes: [] },
      history: [],
    });

    renderWithRouter(<FreezePage />);

    expect(await screen.findByLabelText(/start at/i)).toBeDisabled();
    expect(screen.getByText(/future-dated scheduling is disabled/i)).toBeInTheDocument();
  });
});