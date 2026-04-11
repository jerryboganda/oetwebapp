import { render, screen } from '@testing-library/react';
const { mockFetchForumCategories, mockFetchForumThreads, mockTrack } = vi.hoisted(() => ({
  mockFetchForumCategories: vi.fn(),
  mockFetchForumThreads: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({ default: ({ children, href, ...props }: any) => <a href={href} {...props}>{children}</a> }));

vi.mock('motion/react', () => ({
  motion: {
    div: ({ children, ...props }: any) => <div {...props}>{children}</div>,
    button: ({ children, ...props }: any) => <button {...props}>{children}</button>,
    span: ({ children, ...props }: any) => <span {...props}>{children}</span>,
    section: ({ children, ...props }: any) => <section {...props}>{children}</section>,
    p: ({ children, ...props }: any) => <p {...props}>{children}</p>,
  },
  useReducedMotion: () => false,
  AnimatePresence: ({ children }: any) => <div>{children}</div>,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({ fetchForumCategories: mockFetchForumCategories, fetchForumThreads: mockFetchForumThreads }));

import CommunityPage from './page';

describe('Community page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchForumCategories.mockResolvedValue([
      { id: 'cat-1', name: 'General Discussion', description: null, sortOrder: 1 },
      { id: 'cat-2', name: 'Study Tips', description: null, sortOrder: 2 },
    ]);
    mockFetchForumThreads.mockResolvedValue({
      threads: [{ id: 'thread-1', categoryId: 'cat-1', title: 'How to prepare for OET Reading Part C?', authorDisplayName: 'DrSarah', authorRole: 'learner', isPinned: true, isLocked: false, replyCount: 12, viewCount: 340, likeCount: 8, lastActivityAt: '2026-04-01T09:00:00.000Z' }],
    });
  });

  it('renders through the shared learner dashboard shell', async () => {
    render(<CommunityPage />);
    expect(await screen.findByText('Community')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('displays forum categories and threads from the API', async () => {
    render(<CommunityPage />);
    expect(await screen.findByText('General Discussion')).toBeInTheDocument();
    expect(screen.getByText('How to prepare for OET Reading Part C?')).toBeInTheDocument();
  });
});
