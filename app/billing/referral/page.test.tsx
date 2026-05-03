import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchReferralInfo,
  mockGenerateReferralCode,
  mockFetchFreezeStatus,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchReferralInfo: vi.fn(),
  mockGenerateReferralCode: vi.fn(),
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
  fetchReferralInfo: mockFetchReferralInfo,
  generateReferralCode: mockGenerateReferralCode,
  fetchFreezeStatus: mockFetchFreezeStatus,
}));

import ReferralPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

const sampleInfo = {
  referralCode: 'OET-ABC123',
  referralsMade: 4,
  creditsEarned: 40,
  referrerCreditAmount: 10,
  referredDiscountPercent: 15,
};

describe('Referral page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchFreezeStatus.mockResolvedValue(null);
    mockGenerateReferralCode.mockResolvedValue({ referralCode: 'OET-NEW999' });
  });

  it('renders the empty-state generator inside the shared shell when no code exists', async () => {
    mockFetchReferralInfo.mockResolvedValue({
      ...sampleInfo,
      referralCode: null,
      referralsMade: 0,
      creditsEarned: 0,
    });

    renderWithRouter(<ReferralPage />);

    expect(await screen.findByText('Referral program')).toBeInTheDocument();
    const shell = screen.getByTestId('learner-dashboard-shell');
    expect(shell.getAttribute('data-back-href')).toBe('/billing');
    expect(screen.getByRole('button', { name: /generate my referral code/i })).toBeEnabled();
    expect(screen.getByRole('link', { name: /back to billing center/i })).toHaveAttribute(
      'href',
      '/billing',
    );
  });

  it('renders the referral code, stats, and share controls when data is present', async () => {
    mockFetchReferralInfo.mockResolvedValue(sampleInfo);

    renderWithRouter(<ReferralPage />);

    // The visible referral code element (also serves as click-to-select target).
    expect(await screen.findByText('OET-ABC123')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /copy referral code$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /share referral code/i })).toBeInTheDocument();
    // Stats use real values (also surfaced in hero highlights)
    expect(screen.getAllByText('4').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('40').length).toBeGreaterThanOrEqual(1);
    // Friend discount surfaces inside the "How it works" copy.
    expect(screen.getByText(/15% off their first paid plan/i)).toBeInTheDocument();
  });

  it('disables code generation when freeze status cannot be verified', async () => {
    mockFetchReferralInfo.mockResolvedValue({
      ...sampleInfo,
      referralCode: null,
    });
    mockFetchFreezeStatus.mockRejectedValueOnce(new Error('freeze unavailable'));

    renderWithRouter(<ReferralPage />);
    const user = userEvent.setup();

    expect(await screen.findByText('Referral program')).toBeInTheDocument();
    expect(screen.getByText(/freeze status could not be verified/i)).toBeInTheDocument();

    const generate = screen.getByRole('button', { name: /generate referral code.*unavailable/i });
    expect(generate).toBeDisabled();

    await user.click(generate);
    await waitFor(() => {
      expect(mockGenerateReferralCode).not.toHaveBeenCalled();
    });
  });
});
