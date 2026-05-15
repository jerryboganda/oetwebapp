import { screen } from '@testing-library/react';

const {
  mockGetScoreGuaranteeData,
  mockActivateScoreGuarantee,
  mockFetchFreezeStatus,
  mockTrack,
} = vi.hoisted(() => ({
  mockGetScoreGuaranteeData: vi.fn(),
  mockActivateScoreGuarantee: vi.fn(),
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

vi.mock('@/lib/learner-data', () => ({
  getScoreGuaranteeData: mockGetScoreGuaranteeData,
}));

vi.mock('@/lib/api', () => ({
  activateScoreGuarantee: mockActivateScoreGuarantee,
  fetchFreezeStatus: mockFetchFreezeStatus,
}));

import ScoreGuaranteePage from './page';
import { renderWithRouter } from '@/tests/test-utils';

const activePledge = {
  id: 'pledge-1',
  userId: 'learner-1',
  subscriptionId: 'sub-1',
  baselineScore: 300,
  guaranteedImprovement: 50,
  actualScore: null,
  status: 'active' as const,
  proofDocumentUrl: null,
  claimNote: null,
  reviewNote: null,
  activatedAt: '2026-01-01T00:00:00Z',
  expiresAt: '2026-07-01T00:00:00Z',
};

describe('Score guarantee page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchFreezeStatus.mockResolvedValue(null);
    mockActivateScoreGuarantee.mockResolvedValue({});
  });

  it('shows the activation form inside the shared shell when no pledge exists', async () => {
    mockGetScoreGuaranteeData.mockResolvedValue(null);

    renderWithRouter(<ScoreGuaranteePage />);

    expect(await screen.findByRole('heading', { name: /score guarantee/i, level: 1 })).toBeInTheDocument();
    const shell = screen.getByTestId('learner-dashboard-shell');
    expect(shell.getAttribute('data-back-href')).toBe('/billing');
    expect(screen.getByLabelText(/baseline oet score/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /back to billing center/i })).toHaveAttribute(
      'href',
      '/billing',
    );
  });

  it('renders the active pledge summary when data is available', async () => {
    mockGetScoreGuaranteeData.mockResolvedValue(activePledge);

    renderWithRouter(<ScoreGuaranteePage />);

    expect(await screen.findByText('Your score guarantee')).toBeInTheDocument();
    // Baseline value rendered (appears in both the hero highlight and the summary dl)
    expect(screen.getAllByText('300').length).toBeGreaterThanOrEqual(1);
    // Target = baseline + improvement (also rendered in both places)
    expect(screen.getAllByText('350').length).toBeGreaterThanOrEqual(1);
    // Direct claim submission stays closed until verifiable evidence upload is available.
    expect(screen.getByText(/claim submission requires official result proof/i)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /direct claim submission unavailable/i }),
    ).toBeDisabled();
  });

  it('surfaces unverifiable freeze status while direct claim submission remains disabled', async () => {
    mockGetScoreGuaranteeData.mockResolvedValue(activePledge);
    mockFetchFreezeStatus.mockRejectedValueOnce(new Error('freeze unavailable'));

    renderWithRouter(<ScoreGuaranteePage />);

    expect(await screen.findByText('Your score guarantee')).toBeInTheDocument();
    expect(screen.getByText(/freeze status could not be verified/i)).toBeInTheDocument();

    const submit = screen.getByRole('button', { name: /direct claim submission unavailable/i });
    expect(submit).toBeDisabled();
  });
});
