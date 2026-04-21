import { screen, waitFor } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';

const {
  mockGetConversationHistory,
  mockGetConversationTaskTypes,
  mockGetConversationEntitlement,
  mockCreateConversation,
  mockTrack,
  mockPush,
} = vi.hoisted(() => ({
  mockGetConversationHistory: vi.fn(),
  mockGetConversationTaskTypes: vi.fn(),
  mockGetConversationEntitlement: vi.fn(),
  mockCreateConversation: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => (<a href={href}>{children}</a>),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({
  getConversationHistory: mockGetConversationHistory,
  getConversationTaskTypes: mockGetConversationTaskTypes,
  getConversationEntitlement: mockGetConversationEntitlement,
  createConversation: mockCreateConversation,
}));

import ConversationPage from './page';

describe('Conversation page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetConversationTaskTypes.mockResolvedValue({
      taskTypes: [
        { code: 'oet-roleplay', label: 'OET Clinical Role Play', description: 'Practise 5-minute role plays.' },
        { code: 'oet-handover', label: 'OET Handover', description: 'Practise structured clinical handovers.' },
      ],
      prepDurationSeconds: 120,
      maxSessionDurationSeconds: 360,
      maxTurnDurationSeconds: 60,
    });
    mockGetConversationEntitlement.mockResolvedValue({
      allowed: true, tier: 'free', remaining: 3, limit: 3,
      windowDays: 7, resetAt: null, reason: '3 of 3 free sessions remaining.',
    });
    mockGetConversationHistory.mockResolvedValue({
      items: [{
        id: 'sess-1', taskTypeCode: 'oet-roleplay', examTypeCode: 'oet',
        profession: 'medicine', state: 'evaluated', turnCount: 12, durationSeconds: 320,
        scaledScore: 370, overallGrade: 'B', passed: true,
        createdAt: '2026-04-01T10:00:00.000Z', completedAt: '2026-04-01T10:05:20.000Z',
      }],
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

  it('renders OET task types from the backend catalog without IELTS', async () => {
    renderWithRouter(<ConversationPage />, { router: { push: mockPush } });
    await waitFor(() => expect(screen.getByText('OET Clinical Role Play')).toBeInTheDocument());
    expect(screen.getByText('OET Handover')).toBeInTheDocument();
    expect(screen.queryByText(/IELTS/i)).not.toBeInTheDocument();
  });
});
