import { render, screen } from '@testing-library/react';
import { AiCreditSummary } from './ai-credit-summary';
import type { AiPackageCreditBucket, AiPackageCreditSnapshot } from '@/lib/billing-types';

function bucket(overrides: Partial<AiPackageCreditBucket>): AiPackageCreditBucket {
  return {
    key: 'reading',
    label: 'Reading Credits',
    unlimited: false,
    totalGranted: 0,
    used: 0,
    remaining: 0,
    sourcePackages: null,
    validFrom: null,
    expiresAt: null,
    daysLeft: -1,
    grants: [],
    ...overrides,
  };
}

const baseSnapshot: AiPackageCreditSnapshot = {
  userId: 'learner-1',
  sharedCredits: 0,
  flexibleCredits: 0,
  writingOnlyCredits: 0,
  speakingOnlyCredits: 0,
  listeningTestsRemaining: 0,
  readingTestsRemaining: 0,
  mockExamsRemaining: 0,
  expiredBecausePassed: false,
  transactions: [],
  creditsGranted: 5,
  creditsUsed: 2,
  creditsRemaining: 3,
};

describe('AiCreditSummary', () => {
  it('shows per-bucket Total / Used / Remaining, source, and validity days left', () => {
    render(
      <AiCreditSummary
        snapshot={{
          ...baseSnapshot,
          buckets: [
            bucket({
              key: 'reading',
              label: 'Reading Credits',
              totalGranted: 5,
              used: 2,
              remaining: 3,
              sourcePackages: 'Reading Starter',
              validFrom: '2026-08-01T00:00:00Z',
              expiresAt: '2026-08-31T00:00:00Z',
              daysLeft: 6,
            }),
          ],
        }}
      />,
    );

    const row = screen.getByTestId('admin-bucket-reading');
    expect(row).toHaveTextContent('Reading Credits');
    expect(row).toHaveTextContent('5');
    expect(row).toHaveTextContent('2');
    expect(row).toHaveTextContent('3');
    expect(row).toHaveTextContent('Reading Starter');
    expect(row).toHaveTextContent('6 days left');
  });

  it('renders Unlimited buckets without finite counts', () => {
    render(
      <AiCreditSummary
        snapshot={{
          ...baseSnapshot,
          writingUnlimited: true,
          speakingUnlimited: true,
          buckets: [
            bucket({ key: 'writing', label: 'Writing Credits', unlimited: true }),
            bucket({ key: 'speaking', label: 'Speaking Credits', unlimited: true }),
          ],
        }}
      />,
    );

    expect(screen.getByTestId('admin-bucket-writing')).toHaveTextContent('Unlimited');
    expect(screen.getByTestId('admin-bucket-speaking')).toHaveTextContent('Unlimited');
  });

  it('shows Flexible W/S and Full Mock buckets only when applicable', () => {
    const { unmount } = render(
      <AiCreditSummary
        snapshot={{
          ...baseSnapshot,
          buckets: [bucket({ key: 'reading', label: 'Reading Credits', totalGranted: 3, remaining: 3 })],
        }}
      />,
    );
    expect(screen.queryByTestId('admin-bucket-flexible_ws')).not.toBeInTheDocument();
    expect(screen.queryByTestId('admin-bucket-mock')).not.toBeInTheDocument();
    unmount();

    render(
      <AiCreditSummary
        snapshot={{
          ...baseSnapshot,
          flexibleCredits: 5,
          mockExamsRemaining: 2,
          buckets: [
            bucket({ key: 'flexible_ws', label: 'Flexible W/S Credits', totalGranted: 5, remaining: 5 }),
            bucket({ key: 'mock', label: 'Full Mock Attempts', totalGranted: 2, remaining: 2 }),
          ],
        }}
      />,
    );
    expect(screen.getByTestId('admin-bucket-flexible_ws')).toHaveTextContent('Flexible W/S Credits');
    expect(screen.getByTestId('admin-bucket-mock')).toHaveTextContent('Full Mock Attempts');
  });

  it('shows the empty and loading states', () => {
    const { rerender } = render(<AiCreditSummary snapshot={null} />);
    expect(screen.getByText('No credit data available.')).toBeInTheDocument();

    rerender(<AiCreditSummary snapshot={null} loading />);
    expect(screen.getByText('Loading credit ledger…')).toBeInTheDocument();
  });
});
