import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchBilling,
  mockFetchFreezeStatus,
  mockDownloadInvoice,
  mockFetchBillingContent,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchBilling: vi.fn(),
  mockFetchFreezeStatus: vi.fn(),
  mockDownloadInvoice: vi.fn(),
  mockFetchBillingContent: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/app-shell', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div data-testid="app-shell">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/api', () => ({
  fetchBilling: mockFetchBilling,
  fetchFreezeStatus: mockFetchFreezeStatus,
  downloadInvoice: mockDownloadInvoice,
  fetchBillingContent: mockFetchBillingContent,
}));

import BillingPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

const baseBilling = {
  currentPlan: 'Full Condensed Recorded OET Course — Medicine',
  currentPlanId: 'plan-full',
  currentPlanCode: 'full',
  planName: 'Full Condensed Recorded OET Course — Medicine',
  planDescription: 'The flagship recorded course.',
  reviewCredits: 0,
  nextRenewal: '2026-12-20',
  price: '$100',
  interval: 'one_time',
  status: 'Active',
  activeAddOns: [],
  entitlements: {
    productiveSkillReviewsEnabled: false,
    supportedReviewSubtests: [],
    invoiceDownloadsAvailable: true,
  },
  plans: [],
  addOns: [],
  coupons: [],
  quote: null,
  invoices: [
    { id: 'inv_001', date: '2026-06-20', amount: '$100', status: 'Paid' },
  ],
};

describe('Billing page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchBilling.mockResolvedValue(baseBilling);
    mockFetchFreezeStatus.mockResolvedValue(null);
    mockDownloadInvoice.mockResolvedValue('blob:invoice');
    mockFetchBillingContent.mockResolvedValue({});
    // jsdom lacks URL.revokeObjectURL; the download handler calls it.
    if (typeof URL.revokeObjectURL !== 'function') {
      (URL as unknown as { revokeObjectURL: () => void }).revokeObjectURL = vi.fn();
    } else {
      vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    }
  });

  it('renders inside the shared learner dashboard shell', async () => {
    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('shows only Overview and Invoices tabs — no Plans, Credits, or AI Credits', async () => {
    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /overview/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /invoices/i })).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /plans/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /credits & add-ons/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /ai credits/i })).not.toBeInTheDocument();
  });

  it('shows the subscription name and exact end date on the overview', async () => {
    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /full condensed recorded oet course/i }),
    ).toBeInTheDocument();
    // The exact end date is surfaced (locale-formatted 2026-12-20).
    const endDate = new Date('2026-12-20').toLocaleDateString();
    expect(screen.getAllByText(endDate).length).toBeGreaterThan(0);
  });

  it('falls back to overview for an unknown/removed tab in the URL', async () => {
    renderWithRouter(<BillingPage />, { searchParams: new URLSearchParams('tab=credits') });

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /overview/i })).toHaveAttribute('aria-selected', 'true');
  });

  it('switches to the invoices tab when clicking "View invoices"', async () => {
    const user = userEvent.setup();
    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /view invoices/i }));

    expect(screen.getByRole('tab', { name: /invoices/i })).toHaveAttribute('aria-selected', 'true');
  });

  it('downloads an invoice from the invoices tab', async () => {
    const user = userEvent.setup();
    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: /invoices/i }));
    await user.click(screen.getByRole('button', { name: /download/i }));

    await waitFor(() => expect(mockDownloadInvoice).toHaveBeenCalledWith('inv_001'));
  });

  it('shows the "No active freeze" state when the subscription is not frozen', async () => {
    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    expect(screen.getByText(/no active freeze/i)).toBeInTheDocument();
  });

  it('renders freeze details when the subscription is frozen', async () => {
    mockFetchFreezeStatus.mockResolvedValueOnce({
      currentFreeze: {
        id: 'freeze-1',
        userId: 'learner-1',
        status: 'Active',
        requestedAt: '2026-06-01T00:00:00Z',
        scheduledStartAt: null,
        startedAt: '2026-06-15T00:00:00Z',
        endedAt: '2026-07-15T00:00:00Z',
        durationDays: 30,
        reason: 'Exam preparation break',
      },
      policy: {},
      eligibility: { eligible: false },
      history: [],
    });

    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    expect(screen.getByText('Exam preparation break')).toBeInTheDocument();
    expect(screen.getByText(/30 days/i)).toBeInTheDocument();
    expect(screen.queryByText(/no active freeze/i)).not.toBeInTheDocument();
  });

  it('shows a banner when freeze status cannot be verified', async () => {
    mockFetchFreezeStatus.mockRejectedValueOnce(new Error('freeze unavailable'));

    renderWithRouter(<BillingPage />);

    expect(await screen.findByText('Your billing center')).toBeInTheDocument();
    expect(screen.getByText(/freeze status could not be verified/i)).toBeInTheDocument();
  });
});
