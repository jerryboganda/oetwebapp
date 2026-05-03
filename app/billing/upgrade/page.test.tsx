import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockApiRequest, mockFetchFreezeStatus, mockTrack } = vi.hoisted(() => ({
  mockApiRequest: vi.fn(),
  mockFetchFreezeStatus: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({
    children,
    pageTitle,
    backHref,
  }: {
    children: React.ReactNode;
    pageTitle?: string;
    backHref?: string;
  }) => (
    <div
      data-testid="learner-dashboard-shell"
      data-page-title={pageTitle ?? ''}
      data-back-href={backHref ?? ''}
    >
      {children}
    </div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/api', () => ({
  apiClient: { request: mockApiRequest },
  fetchFreezeStatus: mockFetchFreezeStatus,
}));

import BillingUpgradePage from './page';
import { renderWithRouter } from '@/tests/test-utils';

const samplePlans = [
  {
    planId: 'plan-pro',
    planCode: 'pro',
    planName: 'Pro',
    description: 'Current plan.',
    price: 49,
    currency: 'AUD',
    interval: 'month',
    includedCredits: 5,
    trialDays: 0,
    isCurrent: true,
    isUpgrade: false,
    isDowngrade: false,
    entitlements: {},
  },
  {
    planId: 'plan-plus',
    planCode: 'plus',
    planName: 'Plus',
    description: 'Adds Speaking review.',
    price: 79,
    currency: 'AUD',
    interval: 'month',
    includedCredits: 10,
    trialDays: 7,
    isCurrent: false,
    isUpgrade: true,
    isDowngrade: false,
    entitlements: {},
  },
];

const sampleData = {
  currentPlan: { planId: 'plan-pro', planName: 'Pro', price: 49, includedCredits: 5 },
  usage: {
    reviewsUsedThisMonth: 2,
    creditsRemaining: 3,
    subscriptionStarted: '2026-01-01',
    subscriptionEnds: '2026-12-01',
  },
  plans: samplePlans,
  recommendation: 'Plus would unlock Speaking review for your remaining attempts.',
};

describe('Billing upgrade page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockApiRequest.mockResolvedValue(sampleData);
    mockFetchFreezeStatus.mockResolvedValue(null);
  });

  it('renders inside the shared learner shell with backHref to /billing', async () => {
    renderWithRouter(<BillingUpgradePage />);

    expect(await screen.findByText('Compare plans')).toBeInTheDocument();
    const shell = screen.getByTestId('learner-dashboard-shell');
    expect(shell.getAttribute('data-page-title')).toBe('Compare plans');
    expect(shell.getAttribute('data-back-href')).toBe('/billing');
    expect(screen.getByRole('link', { name: /back to billing center/i })).toHaveAttribute(
      'href',
      '/billing',
    );
  });

  it('renders the empty state when the upgrade endpoint fails', async () => {
    mockApiRequest.mockRejectedValueOnce(new Error('upgrade unavailable'));

    renderWithRouter(<BillingUpgradePage />);

    expect(await screen.findByText(/unable to load plan information/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /return to billing center/i })).toHaveAttribute(
      'href',
      '/billing',
    );
  });

  it('renders plan cards with cross-links to the billing plans tab', async () => {
    renderWithRouter(<BillingUpgradePage />);

    expect(await screen.findByRole('heading', { name: 'Pro', level: 3 })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Plus', level: 3 })).toBeInTheDocument();

    const upgradeLink = screen.getByRole('link', { name: /upgrade to plus/i });
    expect(upgradeLink).toHaveAttribute('href', '/billing?tab=plans&planId=plan-plus');
  });

  it('disables upgrade actions when the freeze status cannot be verified', async () => {
    mockFetchFreezeStatus.mockRejectedValueOnce(new Error('freeze unavailable'));

    renderWithRouter(<BillingUpgradePage />);
    const user = userEvent.setup();

    expect(await screen.findByText('Compare plans')).toBeInTheDocument();
    expect(screen.getByText(/freeze status could not be verified/i)).toBeInTheDocument();

    const blockedButton = screen.getByRole('button', { name: /upgrade to plus.*unavailable/i });
    expect(blockedButton).toBeDisabled();

    await user.click(blockedButton);
    // The fallback button does not navigate; assert no link version exists.
    expect(screen.queryByRole('link', { name: /upgrade to plus/i })).not.toBeInTheDocument();
  });
});
