import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockPush,
  mockSearch,
  mockFetchRoleCard,
  mockCreateSession,
  mockRecordConsent,
  mockStartWarmup,
  mockFinishWarmup,
  mockListFreeSamples,
  MockApiError,
} = vi.hoisted(() => {
  class MockApiError extends Error {
    status: number;
    userMessage: string;
    constructor(status: number, message: string) {
      super(message);
      this.status = status;
      this.userMessage = message;
    }
  }
  return {
    mockPush: vi.fn(),
    mockSearch: { value: '' },
    mockFetchRoleCard: vi.fn(),
    mockCreateSession: vi.fn(),
    mockRecordConsent: vi.fn(),
    mockStartWarmup: vi.fn(),
    mockFinishWarmup: vi.fn(),
    mockListFreeSamples: vi.fn(),
    MockApiError,
  };
});

vi.mock('next/navigation', () => ({
  useParams: () => ({ id: 'rpc-1' }),
  useRouter: () => ({ push: mockPush, replace: vi.fn() }),
  useSearchParams: () => new URLSearchParams(mockSearch.value),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/lib/api', () => ({
  ApiError: MockApiError,
  fetchRoleCard: mockFetchRoleCard,
  apiClient: { request: vi.fn().mockResolvedValue({}) },
}));

vi.mock('@/lib/api/speaking-sessions', () => ({
  createSpeakingSession: mockCreateSession,
  recordConsent: mockRecordConsent,
  startSpeakingWarmup: mockStartWarmup,
  finishSpeakingWarmup: mockFinishWarmup,
}));

vi.mock('@/lib/api/free-samples', () => ({ listFreeSamples: mockListFreeSamples }));
vi.mock('@/lib/analytics', () => ({ analytics: { track: vi.fn() } }));
vi.mock('@/lib/credit-feedback', () => ({ showCreditFeedback: vi.fn() }));

import RoleCardPreview from './page';

const CARD = {
  id: 'rpc-1',
  title: 'Chest pain review',
  profession: 'Medicine',
  setting: 'General practice',
  patient: 'Mr Lee, age 54',
  brief: '',
  background: 'Chest pain after exercise.',
  tasks: ['Take a focused history', 'Explain the next steps'],
  prepTimeSeconds: 180,
  roleplayTimeSeconds: 300,
};

describe('Speaking role-play entry: Rules + consent before any timer', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockSearch.value = '';
    mockFetchRoleCard.mockResolvedValue(CARD);
    mockCreateSession.mockResolvedValue({ sessionId: 'sess-1', state: 'WarmUp' });
    mockRecordConsent.mockResolvedValue({ consentVersion: 'recording.v1', acceptedAt: '2026-09-23T00:00:00Z' });
    mockStartWarmup.mockResolvedValue({});
    mockFinishWarmup.mockResolvedValue({ feedbackMessage: null });
    mockListFreeSamples.mockResolvedValue([]);
  });

  it('shows the rules and one task list with no countdown running', async () => {
    render(<RoleCardPreview />);

    expect(await screen.findByTestId('speaking-rules-consent')).toBeInTheDocument();
    expect(screen.getByText(/one blank sheet of paper and a pen/i)).toBeInTheDocument();
    expect(screen.queryByRole('timer')).not.toBeInTheDocument();
    expect(screen.getAllByText('Take a focused history')).toHaveLength(1);
    expect(screen.queryByText(/guided self-practice/i)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /start preparation/i })).toBeDisabled();
    expect(mockCreateSession).not.toHaveBeenCalled();
  });

  it('creates the session, records consent, skips warm-up and opens prep — in that order', async () => {
    const user = userEvent.setup();
    render(<RoleCardPreview />);

    await user.click(await screen.findByRole('checkbox'));
    await user.click(screen.getByRole('button', { name: /start preparation/i }));

    await waitFor(() => expect(mockPush).toHaveBeenCalledWith('/speaking/sessions/sess-1/prep'));
    expect(mockCreateSession).toHaveBeenCalledWith({ rolePlayCardId: 'rpc-1', mode: 'ai_self_practice', consentVersion: 'recording.v1' });
    expect(mockRecordConsent).toHaveBeenCalledWith('sess-1', 'recording.v1');
    const consentOrder = mockRecordConsent.mock.invocationCallOrder[0];
    expect(consentOrder).toBeGreaterThan(mockCreateSession.mock.invocationCallOrder[0]);
    expect(consentOrder).toBeLessThan(mockFinishWarmup.mock.invocationCallOrder[0]);
  });

  it('retries a failed start on the same session instead of creating a second one', async () => {
    const user = userEvent.setup();
    mockRecordConsent.mockRejectedValueOnce(new MockApiError(500, 'Consent could not be saved.'));
    render(<RoleCardPreview />);

    await user.click(await screen.findByRole('checkbox'));
    await user.click(screen.getByRole('button', { name: /start preparation/i }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Consent could not be saved.');

    await user.click(screen.getByRole('button', { name: /start preparation/i }));
    await waitFor(() => expect(mockPush).toHaveBeenCalledWith('/speaking/sessions/sess-1/prep'));
    expect(mockCreateSession).toHaveBeenCalledTimes(1);
  });

  it('free sample (?free=1) uses the shared session engine, never the legacy recorder route', async () => {
    const user = userEvent.setup();
    mockSearch.value = 'free=1';
    mockListFreeSamples.mockResolvedValue([
      { professionId: 'medicine', contentId: 'rpc-1', state: 'available', route: '/speaking/roleplay/rpc-1?free=1' },
    ]);
    render(<RoleCardPreview />);

    expect(await screen.findByText('Free sample includes one full attempt + one free retry.')).toBeInTheDocument();
    expect(screen.getByTestId('speaking-card-credit-cost')).toHaveTextContent(/no ai credit/i);

    await user.click(screen.getByRole('checkbox'));
    await user.click(screen.getByRole('button', { name: /start preparation/i }));

    await waitFor(() => expect(mockPush).toHaveBeenCalledWith('/speaking/sessions/sess-1/prep'));
    // The server decides it is free — no client flag in the create body.
    expect(mockCreateSession).toHaveBeenCalledWith({ rolePlayCardId: 'rpc-1', mode: 'ai_self_practice', consentVersion: 'recording.v1' });
    expect(mockPush).not.toHaveBeenCalledWith(expect.stringContaining('/speaking/task/'));
  });

  it('blocks a third free attempt with "Free sample completed"', async () => {
    mockSearch.value = 'free=1';
    mockListFreeSamples.mockResolvedValue([
      { professionId: 'medicine', contentId: 'rpc-1', state: 'completed', route: null },
    ]);
    render(<RoleCardPreview />);

    expect(await screen.findByText('Free sample completed')).toBeInTheDocument();
    expect(screen.queryByTestId('speaking-rules-consent')).not.toBeInTheDocument();
  });
});
