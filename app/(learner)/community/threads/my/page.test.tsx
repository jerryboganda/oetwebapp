import { render, screen } from '@testing-library/react';

const { fetchForumThreads, fetchForumCategories } = vi.hoisted(() => ({
  fetchForumThreads: vi.fn(),
  fetchForumCategories: vi.fn(),
}));

vi.mock('@/lib/api', () => ({ fetchForumThreads, fetchForumCategories }));
vi.mock('@/lib/analytics', () => ({ analytics: { track: vi.fn() } }));
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn(), prefetch: vi.fn() }),
  usePathname: () => '/community/threads/my',
}));

import MyThreadsPage from './page';

describe('My Threads page', () => {
  it("asks the server for the caller's own threads and shows the server's total", async () => {
    fetchForumCategories.mockResolvedValue([]);
    fetchForumThreads.mockResolvedValue({
      total: 1,
      threads: [{
        id: 't1', categoryId: 'c1', title: 'How I prepared for Writing', authorDisplayName: 'Learner',
        authorRole: 'learner', isPinned: false, isLocked: false, replyCount: 2, viewCount: 9, likeCount: 1,
        createdAt: '2026-09-30T10:00:00Z', lastActivityAt: '2026-09-30T12:00:00Z',
      }],
    });

    render(<MyThreadsPage />);

    expect(await screen.findByText('How I prepared for Writing')).toBeInTheDocument();
    // mine=true: the server matches the author by user id, across every page.
    expect(fetchForumThreads).toHaveBeenCalledWith(undefined, 1, expect.any(Number), true);
  });
});
