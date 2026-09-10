import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockCheckRecallSpelling,
  mockFetchRecallsAudio,
  mockPlayTransientAudio,
  mockTrack,
  mockGuardAudio,
} = vi.hoisted(() => ({
  mockCheckRecallSpelling: vi.fn(),
  mockFetchRecallsAudio: vi.fn(),
  mockPlayTransientAudio: vi.fn(),
  mockTrack: vi.fn(),
  mockGuardAudio: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  checkRecallSpelling: mockCheckRecallSpelling,
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
  useRecallsAudioUpgrade: () => ({
    guardAudio: mockGuardAudio,
    modal: null,
  }),
}));

import { PracticeSpelling } from './practice-spelling';

function audioOk() {
  return { url: 'blob:oet/audio-1', provider: 'elevenlabs' };
}

beforeEach(() => {
  vi.clearAllMocks();
  mockFetchRecallsAudio.mockResolvedValue(audioOk());
  mockPlayTransientAudio.mockReturnValue({ addEventListener: vi.fn() });
  // Pass the action straight through — the guard's 402 handling is covered by the
  // page-level tests that own the upgrade modal.
  mockGuardAudio.mockImplementation(async (action: () => Promise<unknown>) => action());
});

describe('PracticeSpelling (§3B)', () => {
  it('stays collapsed until the learner opens it', async () => {
    render(<PracticeSpelling termId="t1" />);

    expect(screen.getByRole('button', { name: 'Practice Spelling' })).toBeInTheDocument();
    // Nothing is requested or played before the panel is opened.
    expect(mockFetchRecallsAudio).not.toHaveBeenCalled();
    expect(mockPlayTransientAudio).not.toHaveBeenCalled();
  });

  it('cannot reveal the answer early: the canonical word only comes from the server response', async () => {
    mockCheckRecallSpelling.mockResolvedValue({
      correct: false,
      canonical: 'dyspnoea',
      wrongAttemptCount: 1,
      addedToMistakes: true,
      removedFromMistakes: false,
    });
    render(<PracticeSpelling termId="t1" />);

    await userEvent.click(screen.getByRole('button', { name: 'Practice Spelling' }));
    // The component is never handed the word — there is no prop that could leak it.
    expect(screen.queryByText('dyspnoea')).not.toBeInTheDocument();

    await userEvent.type(screen.getByPlaceholderText('Type the word you hear'), 'dyspnea');
    await userEvent.click(screen.getByRole('button', { name: 'Check' }));

    // It appears only because the server returned it with the graded result.
    expect(await screen.findByText('dyspnoea')).toBeInTheDocument();
  });

  it('can be driven by the owner so "Next Word" can open the following card', async () => {
    const onOpenChange = vi.fn();
    const { rerender } = render(
      <PracticeSpelling termId="t1" open={false} onOpenChange={onOpenChange} />,
    );
    expect(screen.getByRole('button', { name: 'Practice Spelling' })).toBeInTheDocument();

    // The owner opens it.
    rerender(<PracticeSpelling termId="t1" open onOpenChange={onOpenChange} />);
    expect(await screen.findByText('Practice spelling')).toBeInTheDocument();
    expect(mockFetchRecallsAudio).toHaveBeenCalledWith('t1', 'normal');

    // And the owner closes it again.
    rerender(<PracticeSpelling termId="t1" open={false} onOpenChange={onOpenChange} />);
    expect(screen.queryByText('Practice spelling')).not.toBeInTheDocument();
  });

  it('auto-plays the existing audio when the panel opens', async () => {
    render(<PracticeSpelling termId="t1" />);

    await userEvent.click(screen.getByRole('button', { name: 'Practice Spelling' }));

    await waitFor(() => {
      expect(mockFetchRecallsAudio).toHaveBeenCalledWith('t1', 'normal');
    });
    expect(mockPlayTransientAudio).toHaveBeenCalledWith('blob:oet/audio-1');
    expect(mockTrack).toHaveBeenCalledWith('recalls_word_audio_played', { termId: 't1' });
  });

  it('tells the owner to hide the word while the panel is open', async () => {
    const onOpenChange = vi.fn();
    render(<PracticeSpelling termId="t1" onOpenChange={onOpenChange} />);

    await userEvent.click(screen.getByRole('button', { name: 'Practice Spelling' }));
    expect(onOpenChange).toHaveBeenLastCalledWith(true);

    await userEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(onOpenChange).toHaveBeenLastCalledWith(false);
  });

  it('reports a correct answer and reveals the canonical spelling only after Check', async () => {
    mockCheckRecallSpelling.mockResolvedValue({
      correct: true,
      canonical: 'dyspnoea',
      wrongAttemptCount: 0,
      addedToMistakes: false,
      removedFromMistakes: true,
    });
    const onAnswered = vi.fn();
    render(<PracticeSpelling termId="t1" onAnswered={onAnswered} />);

    await userEvent.click(screen.getByRole('button', { name: 'Practice Spelling' }));
    await userEvent.type(screen.getByPlaceholderText('Type the word you hear'), 'Dyspnoea');
    await userEvent.click(screen.getByRole('button', { name: 'Check' }));

    expect(await screen.findByText('Correct spelling')).toBeInTheDocument();
    expect(screen.getByText('dyspnoea')).toBeInTheDocument();
    expect(screen.getByText(/Removed from Review Mistakes/)).toBeInTheDocument();
    expect(mockCheckRecallSpelling).toHaveBeenCalledWith('t1', 'Dyspnoea');
    expect(onAnswered).toHaveBeenCalledWith(
      expect.objectContaining({ correct: true, removedFromMistakes: true }),
    );
  });

  it('reports an incorrect answer, reveals the canonical spelling, and flags Review Mistakes', async () => {
    mockCheckRecallSpelling.mockResolvedValue({
      correct: false,
      canonical: 'dyspnoea',
      wrongAttemptCount: 1,
      addedToMistakes: true,
      removedFromMistakes: false,
    });
    render(<PracticeSpelling termId="t1" />);

    await userEvent.click(screen.getByRole('button', { name: 'Practice Spelling' }));
    await userEvent.type(screen.getByPlaceholderText('Type the word you hear'), 'dyspnea');
    await userEvent.click(screen.getByRole('button', { name: 'Check' }));

    expect(await screen.findByText('Incorrect')).toBeInTheDocument();
    expect(screen.getByText('dyspnoea')).toBeInTheDocument();
    expect(screen.getByText(/Added to Review Mistakes/)).toBeInTheDocument();
  });

  it('does not send the answer twice for one word', async () => {
    mockCheckRecallSpelling.mockResolvedValue({
      correct: false,
      canonical: 'dyspnoea',
      wrongAttemptCount: 1,
      addedToMistakes: true,
      removedFromMistakes: false,
    });
    render(<PracticeSpelling termId="t1" />);

    await userEvent.click(screen.getByRole('button', { name: 'Practice Spelling' }));
    await userEvent.type(screen.getByPlaceholderText('Type the word you hear'), 'dyspnea');
    await userEvent.click(screen.getByRole('button', { name: 'Check' }));
    await screen.findByText('Incorrect');

    // Check is disabled once a result is shown.
    expect(screen.getByRole('button', { name: 'Check' })).toBeDisabled();
    expect(mockCheckRecallSpelling).toHaveBeenCalledTimes(1);
  });

  it('Try Again clears the answer and replays the audio', async () => {
    mockCheckRecallSpelling.mockResolvedValue({
      correct: false,
      canonical: 'dyspnoea',
      wrongAttemptCount: 1,
      addedToMistakes: true,
      removedFromMistakes: false,
    });
    render(<PracticeSpelling termId="t1" />);

    await userEvent.click(screen.getByRole('button', { name: 'Practice Spelling' }));
    const input = screen.getByPlaceholderText('Type the word you hear');
    await userEvent.type(input, 'dyspnea');
    await userEvent.click(screen.getByRole('button', { name: 'Check' }));
    await screen.findByText('Incorrect');
    mockFetchRecallsAudio.mockClear();

    await userEvent.click(screen.getByRole('button', { name: 'Try Again' }));

    expect(input).toHaveValue('');
    expect(screen.queryByText('Incorrect')).not.toBeInTheDocument();
    expect(input).toBeEnabled();
    // Audio is replayed from the cache — no new TTS generation server-side.
    await waitFor(() => expect(mockFetchRecallsAudio).toHaveBeenCalledWith('t1', 'normal'));
  });

  it('turns browser spell-assistance off so the answer is not given away', async () => {
    render(<PracticeSpelling termId="t1" />);
    await userEvent.click(screen.getByRole('button', { name: 'Practice Spelling' }));

    const input = screen.getByPlaceholderText('Type the word you hear');
    expect(input).toHaveAttribute('autocomplete', 'off');
    expect(input).toHaveAttribute('autocorrect', 'off');
    expect(input).toHaveAttribute('autocapitalize', 'off');
    expect(input).toHaveAttribute('spellcheck', 'false');
  });

  it('offers Next Word only when the owner provides one', async () => {
    const onNext = vi.fn();
    const { unmount } = render(<PracticeSpelling termId="t1" />);
    await userEvent.click(screen.getByRole('button', { name: 'Practice Spelling' }));
    expect(screen.queryByRole('button', { name: 'Next Word' })).not.toBeInTheDocument();
    unmount();

    render(<PracticeSpelling termId="t1" onNext={onNext} />);
    await userEvent.click(screen.getByRole('button', { name: 'Practice Spelling' }));
    await userEvent.click(screen.getByRole('button', { name: 'Next Word' }));
    expect(onNext).toHaveBeenCalledTimes(1);
  });
});
