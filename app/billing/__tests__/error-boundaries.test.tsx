import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockTrack } = vi.hoisted(() => ({ mockTrack: vi.fn() }));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

import UpgradeError from '../upgrade/error';
import ReferralError from '../referral/error';
import ScoreGuaranteeError from '../score-guarantee/error';
import BillingError from '../error';
import { renderWithRouter } from '@/tests/test-utils';

describe.each([
  ['billing root', BillingError, "We couldn't load your billing details", 'billing'],
  ['upgrade', UpgradeError, "We couldn't load plan information", 'billing-upgrade'],
  ['referral', ReferralError, "We couldn't load your referral program", 'billing-referral'],
  ['score-guarantee', ScoreGuaranteeError, "We couldn't load your score guarantee", 'billing-score-guarantee'],
] as const)('%s error boundary', (_label, ErrorComp, expectedTitle, expectedPage) => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the title, an alert, and a working retry button', async () => {
    const reset = vi.fn();
    const error = Object.assign(new Error('Network down'), { digest: 'abc123' });
    renderWithRouter(<ErrorComp error={error} reset={reset} />);

    expect(screen.getByText(expectedTitle)).toBeInTheDocument();
    expect(screen.getByText(/Network down/)).toBeInTheDocument();

    const retry = screen.getByRole('button', { name: /try again|retry/i });
    expect(retry).toBeInTheDocument();

    const user = userEvent.setup();
    await user.click(retry);
    expect(reset).toHaveBeenCalledTimes(1);

    expect(mockTrack).toHaveBeenCalledWith(
      'error_view',
      expect.objectContaining({ page: expectedPage, message: 'Network down', digest: 'abc123' }),
    );
  });
});
