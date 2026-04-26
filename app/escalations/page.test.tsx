import { screen, waitFor, fireEvent } from '@testing-library/react';

const { mockFetchMyEscalations, mockSubmitEscalation, mockTrack, mockPush } = vi.hoisted(() => ({
  mockFetchMyEscalations: vi.fn(),
  mockSubmitEscalation: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));
vi.mock('@/components/layout/app-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/admin-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/expert-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/sponsor-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-workspace-container', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-center', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-preferences-panel', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/top-nav', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/sidebar', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/api', () => ({
  fetchMyEscalations: mockFetchMyEscalations,
  submitEscalation: mockSubmitEscalation,
}));

import EscalationsPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

const MOCK_ESCALATIONS = [
  {
    id: 'esc-1',
    submissionId: 'sub-100',
    reason: 'Score seems incorrect for my writing sample',
    details: 'I believe the criteria was not applied correctly.',
    status: 'Pending',
    createdAt: '2026-04-10T10:00:00Z',
    updatedAt: null,
    resolutionNote: null,
  },
  {
    id: 'esc-2',
    submissionId: 'sub-200',
    reason: 'Review feedback was contradictory',
    details: 'The reviewer comments conflict with the score.',
    status: 'Resolved',
    createdAt: '2026-04-08T14:30:00Z',
    updatedAt: '2026-04-09T09:00:00Z',
    resolutionNote: 'Score adjusted after second review.',
  },
  {
    id: 'esc-3',
    submissionId: 'sub-300',
    reason: 'Technical issue during speaking test',
    details: 'Audio recording had issues.',
    status: 'InReview',
    createdAt: '2026-04-09T12:00:00Z',
    updatedAt: null,
    resolutionNote: null,
  },
  {
    id: 'esc-4',
    submissionId: 'sub-400',
    reason: 'Unfair scoring',
    details: 'Details here.',
    status: 'Rejected',
    createdAt: '2026-04-07T08:00:00Z',
    updatedAt: '2026-04-08T10:00:00Z',
    resolutionNote: 'Scoring was verified as correct.',
  },
];

describe('Escalations list page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchMyEscalations.mockResolvedValue(MOCK_ESCALATIONS);
  });

  it('renders escalation list inside the learner dashboard shell', async () => {
    renderWithRouter(<EscalationsPage />, { router: { push: mockPush } });
    expect(await screen.findByText('sub-100')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('shows color-coded status badges for each status', async () => {
    renderWithRouter(<EscalationsPage />, { router: { push: mockPush } });
    await screen.findByText('sub-100');
    const pendingBadges = screen.getAllByText('Pending');
    expect(pendingBadges.length).toBeGreaterThanOrEqual(1);
    const inReviewBadges = screen.getAllByText('In Review');
    expect(inReviewBadges.length).toBeGreaterThanOrEqual(1);
    const resolvedBadges = screen.getAllByText('Resolved');
    expect(resolvedBadges.length).toBeGreaterThanOrEqual(1);
    const rejectedBadges = screen.getAllByText('Rejected');
    expect(rejectedBadges.length).toBeGreaterThanOrEqual(1);
  });

  it('shows truncated reason text', async () => {
    renderWithRouter(<EscalationsPage />, { router: { push: mockPush } });
    expect(await screen.findByText(/Score seems incorrect/)).toBeInTheDocument();
  });

  it('navigates to detail page when a row is clicked', async () => {
    renderWithRouter(<EscalationsPage />, { router: { push: mockPush } });
    const row = await screen.findByText('sub-100');
    fireEvent.click(row.closest('[data-escalation-id]')!);
    expect(mockPush).toHaveBeenCalledWith('/escalations/esc-1');
  });

  it('shows error state when fetch fails', async () => {
    mockFetchMyEscalations.mockRejectedValueOnce(new Error('Network error'));
    renderWithRouter(<EscalationsPage />, { router: { push: mockPush } });
    expect(await screen.findByText(/Failed to load escalations/)).toBeInTheDocument();
  });

  it('shows empty state when no escalations', async () => {
    mockFetchMyEscalations.mockResolvedValueOnce([]);
    renderWithRouter(<EscalationsPage />, { router: { push: mockPush } });
    expect(await screen.findByText(/no escalations/i)).toBeInTheDocument();
  });

  it('opens submit form and submits escalation', async () => {
    mockSubmitEscalation.mockResolvedValueOnce({ id: 'esc-new' });
    renderWithRouter(<EscalationsPage />, { router: { push: mockPush } });
    await screen.findByText('sub-100');

    const submitBtn = screen.getByRole('button', { name: /submit escalation/i });
    fireEvent.click(submitBtn);

    const submissionInput = screen.getByLabelText(/submission id/i);
    const reasonInput = screen.getByLabelText(/reason/i);
    const detailsInput = screen.getByLabelText(/details/i);

    fireEvent.change(submissionInput, { target: { value: '12345' } });
    fireEvent.change(reasonInput, { target: { value: 'Score dispute' } });
    fireEvent.change(detailsInput, { target: { value: 'I disagree with the score given.' } });

    const confirmBtn = screen.getByRole('button', { name: /submit$/i });
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(mockSubmitEscalation).toHaveBeenCalledWith('12345', 'Score dispute', 'I disagree with the score given.');
    });
  });

  it('tracks analytics on page load', async () => {
    renderWithRouter(<EscalationsPage />, { router: { push: mockPush } });
    await screen.findByText('sub-100');
    expect(mockTrack).toHaveBeenCalledWith('page_viewed', { page: 'escalations' });
  });
});
