import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
const { mockFetchReviewQueue, mockFetchExpertQueueFilterMetadata, mockTrack } = vi.hoisted(() => ({
  mockFetchReviewQueue: vi.fn(),
  mockFetchExpertQueueFilterMetadata: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('motion/react', () => ({
  motion: {
    div: ({ children, ...props }: any) => <div {...props}>{children}</div>,
    button: ({ children, ...props }: any) => <button {...props}>{children}</button>,
    span: ({ children, ...props }: any) => <span {...props}>{children}</span>,
    section: ({ children, ...props }: any) => <section {...props}>{children}</section>,
    p: ({ children, ...props }: any) => <p {...props}>{children}</p>,
    tbody: ({ children, ...props }: any) => <tbody {...props}>{children}</tbody>,
  },
  useReducedMotion: () => false,
  AnimatePresence: ({ children }: any) => <div>{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({
  fetchReviewQueue: mockFetchReviewQueue,
  fetchExpertQueueFilterMetadata: mockFetchExpertQueueFilterMetadata,
  claimReview: vi.fn(), releaseReview: vi.fn(), isApiError: () => false,
}));

import ReviewQueuePage from './page';

describe('Expert queue page', () => {
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

  it('renders the expert review queue with items from the API', async () => {
    renderWithRouter(<ReviewQueuePage />, { pathname: '/expert/queue' });
    const matches = await screen.findAllByText('Dr Amina Khan');
    expect(matches.length).toBeGreaterThan(0);
  });
});
