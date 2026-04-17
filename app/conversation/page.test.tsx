import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
const { mockGetConversationHistory, mockCreateConversation, mockTrack, mockPush } = vi.hoisted(() => ({
  mockGetConversationHistory: vi.fn(),
  mockCreateConversation: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));
vi.mock('next/link', () => ({ default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a> }));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({ getConversationHistory: mockGetConversationHistory, createConversation: mockCreateConversation }));

import ConversationPage from './page';

describe('Conversation page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetConversationHistory.mockResolvedValue({
      items: [{ id: 'sess-1', taskTypeCode: 'oet-roleplay', examTypeCode: 'oet', state: 'evaluated', turnCount: 12, durationSeconds: 320, createdAt: '2026-04-01T10:00:00.000Z', completedAt: '2026-04-01T10:05:20.000Z' }],
    });
    mockCreateConversation.mockResolvedValue({ id: 'sess-new' });
  });

  it('renders through the shared learner dashboard shell', async () => {
    renderWithRouter(<ConversationPage />, { router: { push: mockPush } });
    expect(await screen.findByText('AI Conversation Practice')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('tracks conversation_page_viewed analytics on mount', async () => {
    renderWithRouter(<ConversationPage />, { router: { push: mockPush } });
    await screen.findByText('AI Conversation Practice');
    expect(mockTrack).toHaveBeenCalledWith('conversation_page_viewed');
  });

  it('displays task type options for starting new conversations', async () => {
    renderWithRouter(<ConversationPage />, { router: { push: mockPush } });
    // Wait for the page to fully render (hero loads first)
    await screen.findByText('AI Conversation Practice');
    expect(screen.getByText('OET Clinical Role Play')).toBeInTheDocument();
    expect(screen.getByText('IELTS Part 2 Long Turn')).toBeInTheDocument();
  });
});
