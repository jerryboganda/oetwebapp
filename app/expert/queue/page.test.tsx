import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
const { mockFetchReviewQueue, mockFetchExpertQueueFilterMetadata, mockTrack } = vi.hoisted(() => ({
  mockFetchReviewQueue: vi.fn(),
  mockFetchExpertQueueFilterMetadata: vi.fn(),
  mockTrack: vi.fn(),
}));


vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({
  fetchReviewQueue: mockFetchReviewQueue,
  fetchExpertQueueFilterMetadata: mockFetchExpertQueueFilterMetadata,
  claimReview: vi.fn(), releaseReview: vi.fn(), isApiError: () => false,
}));

import ReviewQueuePage from './page';

describe('Tutor queue page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchExpertQueueFilterMetadata.mockResolvedValue({
      types: ['writing', 'speaking'], professions: ['medicine', 'nursing'],
      priorities: ['high', 'normal'], statuses: ['queued', 'assigned', 'in_progress'],
      confidenceBands: ['high', 'medium', 'low'], assignmentStates: ['assigned', 'unassigned'],
    });
    mockFetchReviewQueue.mockResolvedValue({
      items: [{ id: 'rev-1', learnerId: 'learner-1', learnerName: 'Dr Amina Khan', profession: 'medicine', subTest: 'writing', type: 'writing', aiConfidence: 'high', priority: 'high', slaDue: '2026-04-01T10:00:00.000Z', status: 'queued', createdAt: '2026-04-01T06:00:00.000Z', isOverdue: false, assignedTo: null }],
      total: 1, lastUpdatedAt: '2026-04-01T08:00:00.000Z',
    });
  });

  it('renders the tutor review queue with items from the API', async () => {
    renderWithRouter(<ReviewQueuePage />, { pathname: '/expert/queue' });
    const matches = await screen.findAllByText('Dr Amina Khan');
    expect(matches.length).toBeGreaterThan(0);
  });
});
