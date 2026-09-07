import { fireEvent, screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';

const {
  mockFetchReviewQueue,
  mockFetchExpertQueueFilterMetadata,
  mockFetchTutorWritingQueue,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchReviewQueue: vi.fn(),
  mockFetchExpertQueueFilterMetadata: vi.fn(),
  mockFetchTutorWritingQueue: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({
  fetchReviewQueue: mockFetchReviewQueue,
  fetchExpertQueueFilterMetadata: mockFetchExpertQueueFilterMetadata,
  fetchTutorWritingQueue: mockFetchTutorWritingQueue,
  claimReview: vi.fn(),
  releaseReview: vi.fn(),
  isApiError: () => false,
  apiClient: {
    get: vi.fn().mockResolvedValue({ items: [] }),
    post: vi.fn().mockResolvedValue({}),
  },
}));

import ReviewQueuePage from './page';

describe('Tutor queue page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchExpertQueueFilterMetadata.mockResolvedValue({
      types: ['writing', 'speaking'],
      professions: ['medicine', 'nursing'],
      priorities: ['high', 'normal'],
      statuses: ['queued', 'assigned', 'in_progress'],
      confidenceBands: ['high', 'medium', 'low'],
      assignmentStates: ['assigned', 'unassigned'],
    });
    mockFetchTutorWritingQueue.mockResolvedValue({
      items: [
        {
          submissionId: 'sub-writing-123',
          userId: 'usr-1',
          profession: 'medicine',
          letterType: 'referral',
          wordCount: 195,
          requestedAt: '2026-04-01T06:00:00.000Z',
          claimedAt: null,
          claimedByTutorId: null,
          status: 'pending',
        },
      ],
    });
    mockFetchReviewQueue.mockResolvedValue({
      items: [
        {
          id: 'rev-1',
          learnerId: 'learner-1',
          learnerName: 'Dr Amina Khan',
          profession: 'medicine',
          subTest: 'speaking',
          type: 'speaking',
          aiConfidence: 'high',
          priority: 'high',
          slaDue: '2026-04-01T10:00:00.000Z',
          status: 'queued',
          createdAt: '2026-04-01T06:00:00.000Z',
          isOverdue: false,
          assignedTo: null,
          availableActions: { canClaim: true, canOpen: false, canRelease: false },
        },
      ],
      totalCount: 1,
      total: 1,
      lastUpdatedAt: '2026-04-01T08:00:00.000Z',
    });
  });

  it('renders the tutor review queue with items from the API', async () => {
    renderWithRouter(<ReviewQueuePage />, { pathname: '/expert/queue' });
    const matches = await screen.findAllByText('Dr Amina Khan');
    expect(matches.length).toBeGreaterThan(0);
    expect(screen.getByRole('heading', { name: /^review queue$/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/review queue table/i)).toBeInTheDocument();
  });

  it('keeps hero and summary cards visible when queue is empty, rendering helpful empty state with actions', async () => {
    mockFetchReviewQueue.mockResolvedValue({
      items: [],
      totalCount: 0,
      total: 0,
      lastUpdatedAt: '2026-04-01T08:00:00.000Z',
    });

    renderWithRouter(<ReviewQueuePage />, { pathname: '/expert/queue' });

    // Hero and cards remain durable
    expect(await screen.findByRole('heading', { name: /^review queue$/i })).toBeInTheDocument();
    expect(screen.getByText('Speaking Queue Items')).toBeInTheDocument();
    expect(screen.getByText('Writing Reviews Pending')).toBeInTheDocument();

    // Empty state rendered with actionable guidance
    expect(screen.getByText(/no reviews in queue/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /view writing reviews/i })).toBeInTheDocument();
  });

  it('switches between speaking and writing reviews tabs', async () => {
    renderWithRouter(<ReviewQueuePage />, { pathname: '/expert/queue' });

    const writingTab = await screen.findByRole('tab', { name: /writing reviews/i });
    expect(writingTab).toBeInTheDocument();

    fireEvent.click(writingTab);

    // Shows writing review queue table
    expect(await screen.findByRole('table', { name: /writing tutor review queue/i })).toBeInTheDocument();

    // Switch back to speaking
    const speakingTab = screen.getByRole('tab', { name: /speaking reviews/i });
    fireEvent.click(speakingTab);
    expect(await screen.findByLabelText(/review queue table/i)).toBeInTheDocument();
  });

  it('displays flash message toast on review-submitted redirect', async () => {
    window.sessionStorage.setItem('expertReviewQueueFlash', 'review-submitted');

    renderWithRouter(<ReviewQueuePage />, { pathname: '/expert/queue' });

    expect(await screen.findByText('Review submitted successfully.')).toBeInTheDocument();
  });
});
