import { screen } from '@testing-library/react';

const { mockFetchEscalationDetails, mockTrack } = vi.hoisted(() => ({
  mockFetchEscalationDetails: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/api', () => ({
  fetchEscalationDetails: mockFetchEscalationDetails,
}));

let mockParamsValue: Record<string, string> | null = { id: 'esc-1' };
vi.mock('next/navigation', async () => {
  const actual = await vi.importActual('next/navigation');
  return {
    ...actual,
    useParams: () => mockParamsValue,
    useRouter: () => ({ push: vi.fn(), back: vi.fn() }),
  };
});

import EscalationDetailPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

const MOCK_DETAIL = {
  id: 'esc-1',
  submissionId: 'sub-100',
  reason: 'Score seems incorrect for my writing sample',
  details: 'I believe the criteria was not applied correctly. The feedback mentions good structure but the score does not reflect that.',
  status: 'Pending',
  createdAt: '2026-04-10T10:00:00Z',
  updatedAt: null,
  resolutionNote: null,
};

describe('Escalation detail page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockParamsValue = { id: 'esc-1' };
    mockFetchEscalationDetails.mockResolvedValue(MOCK_DETAIL);
  });

  it('renders escalation details inside the learner dashboard shell', async () => {
    renderWithRouter(<EscalationDetailPage />);
    expect(await screen.findByText('sub-100')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('displays full reason and details', async () => {
    renderWithRouter(<EscalationDetailPage />);
    await screen.findByText('sub-100');
    const reasonElements = screen.getAllByText('Score seems incorrect for my writing sample');
    expect(reasonElements.length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText(/I believe the criteria was not applied correctly/)).toBeTruthy();
  });

  it('shows status badge', async () => {
    renderWithRouter(<EscalationDetailPage />);
    await screen.findByText('sub-100');
    const pendingElements = screen.getAllByText('Pending');
    expect(pendingElements.length).toBeGreaterThanOrEqual(1);
  });

  it('shows resolution note when resolved', async () => {
    mockFetchEscalationDetails.mockResolvedValueOnce({
      ...MOCK_DETAIL,
      status: 'Resolved',
      resolutionNote: 'Score adjusted after second review.',
    });
    renderWithRouter(<EscalationDetailPage />);
    expect(await screen.findByText('Score adjusted after second review.')).toBeInTheDocument();
  });

  it('shows error state on fetch failure', async () => {
    mockFetchEscalationDetails.mockRejectedValueOnce(new Error('Not found'));
    renderWithRouter(<EscalationDetailPage />);
    expect(await screen.findByText(/Failed to load escalation/)).toBeInTheDocument();
  });
});
