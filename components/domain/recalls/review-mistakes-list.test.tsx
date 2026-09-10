import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchRecallSpellingMistakes,
  mockFetchRecallsAudio,
  mockPlayTransientAudio,
  mockTrack,
  mockGuardAudio,
} = vi.hoisted(() => ({
  mockFetchRecallSpellingMistakes: vi.fn(),
  mockFetchRecallsAudio: vi.fn(),
  mockPlayTransientAudio: vi.fn(),
  mockTrack: vi.fn(),
  mockGuardAudio: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  fetchRecallSpellingMistakes: mockFetchRecallSpellingMistakes,
  fetchRecallsAudio: mockFetchRecallsAudio,
  isApiError: (error: unknown) => Boolean(error && typeof error === 'object' && 'status' in error),
}));

vi.mock('@/lib/recalls-audio', () => ({
  playTransientAudio: mockPlayTransientAudio,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/components/domain/recalls/audio-upgrade-modal', () => ({
  useRecallsAudioUpgrade: () => ({ guardAudio: mockGuardAudio, modal: null }),
}));

vi.mock('@/components/ui/button', () => ({
  Button: ({
    children,
    loading: _loading,
    variant: _variant,
    size: _size,
    fullWidth: _fullWidth,
    asChild: _asChild,
    ...props
  }: React.ButtonHTMLAttributes<HTMLButtonElement> & {
    loading?: boolean;
    variant?: string;
    size?: string;
    fullWidth?: boolean;
    asChild?: boolean;
  }) => <button {...props}>{children}</button>,
}));

import { ReviewMistakesList } from './review-mistakes-list';

const MISTAKES = {
  items: [
    {
      termId: 't1',
      term: 'dyspnoea',
      category: 'symptoms',
      definition: 'Difficulty breathing.',
      ipa: null,
      hasAudio: true,
      wrongAttemptCount: 3,
      lastWrongAt: '2026-09-10T10:00:00Z',
    },
    {
      termId: 't2',
      term: 'hypertension',
      category: 'conditions',
      definition: 'High blood pressure.',
      ipa: null,
      hasAudio: false,
      wrongAttemptCount: 1,
      lastWrongAt: '2026-09-09T10:00:00Z',
    },
  ],
  total: 2,
};

beforeEach(() => {
  vi.clearAllMocks();
  mockFetchRecallsAudio.mockResolvedValue({ url: 'blob:oet/audio', provider: 'elevenlabs' });
  mockPlayTransientAudio.mockReturnValue({ addEventListener: vi.fn() });
  mockGuardAudio.mockImplementation(async (action: () => Promise<unknown>) => action());
});

describe('ReviewMistakesList (§3C)', () => {
  it('shows the persisted mistakes with their miss counts', async () => {
    mockFetchRecallSpellingMistakes.mockResolvedValue(MISTAKES);
    render(<ReviewMistakesList />);

    expect(await screen.findByText('dyspnoea')).toBeInTheDocument();
    expect(screen.getByText(/missed 3 times/)).toBeInTheDocument();
    expect(screen.getByText('hypertension')).toBeInTheDocument();
    expect(screen.getByText(/missed once/)).toBeInTheDocument();
  });

  it('explains that the list is account-wide when it is empty', async () => {
    mockFetchRecallSpellingMistakes.mockResolvedValue({ items: [], total: 0 });
    render(<ReviewMistakesList />);

    expect(await screen.findByText(/Nothing here yet/)).toBeInTheDocument();
    expect(screen.getByText(/on every device you sign in to/)).toBeInTheDocument();
  });

  it('reuses the stored audio rather than generating anything new', async () => {
    mockFetchRecallSpellingMistakes.mockResolvedValue(MISTAKES);
    render(<ReviewMistakesList />);

    await userEvent.click(
      await screen.findByRole('button', { name: 'Play pronunciation of dyspnoea' }),
    );

    await waitFor(() => expect(mockFetchRecallsAudio).toHaveBeenCalledWith('t1', 'normal'));
    expect(mockPlayTransientAudio).toHaveBeenCalledWith('blob:oet/audio');
    expect(mockTrack).toHaveBeenCalledWith('recalls_word_audio_played', { termId: 't1' });
  });

  it('disables playback for a word with no stored audio', async () => {
    mockFetchRecallSpellingMistakes.mockResolvedValue(MISTAKES);
    render(<ReviewMistakesList />);

    expect(
      await screen.findByRole('button', { name: 'Play pronunciation of hypertension' }),
    ).toBeDisabled();
  });

  it('re-fetches when the owner bumps the refresh token', async () => {
    mockFetchRecallSpellingMistakes.mockResolvedValue(MISTAKES);
    const { rerender } = render(<ReviewMistakesList refreshToken={0} />);
    await screen.findByText('dyspnoea');
    expect(mockFetchRecallSpellingMistakes).toHaveBeenCalledTimes(1);

    // The page bumps this after every graded answer, so a word just spelled
    // correctly disappears without a manual refresh.
    mockFetchRecallSpellingMistakes.mockResolvedValue({ items: [MISTAKES.items[1]], total: 1 });
    rerender(<ReviewMistakesList refreshToken={1} />);

    await waitFor(() => expect(mockFetchRecallSpellingMistakes).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByText('dyspnoea')).not.toBeInTheDocument());
  });

  it('surfaces a load failure instead of pretending the list is empty', async () => {
    mockFetchRecallSpellingMistakes.mockRejectedValue(new Error('boom'));
    render(<ReviewMistakesList />);

    expect(await screen.findByRole('alert')).toHaveTextContent(/Could not load your review mistakes/);
  });
});
