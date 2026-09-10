import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockCheckRecallSpelling,
  mockFetchRecallsAudio,
  mockFetchRecallSpellingSet,
  mockPlayTransientAudio,
  mockTrack,
  mockGuardAudio,
} = vi.hoisted(() => ({
  mockCheckRecallSpelling: vi.fn(),
  mockFetchRecallsAudio: vi.fn(),
  mockFetchRecallSpellingSet: vi.fn(),
  mockPlayTransientAudio: vi.fn(),
  mockTrack: vi.fn(),
  mockGuardAudio: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  checkRecallSpelling: mockCheckRecallSpelling,
  fetchRecallsAudio: mockFetchRecallsAudio,
  fetchRecallSpellingSet: mockFetchRecallSpellingSet,
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

vi.mock('@/components/ui/modal', () => ({
  Modal: ({ open, title, children }: { open: boolean; title?: string; children: React.ReactNode }) =>
    open ? <div role="dialog" aria-label={title}>{children}</div> : null,
}));

vi.mock('@/components/ui/skeleton', () => ({
  Skeleton: ({ className }: { className?: string }) => <div className={className} data-testid="skeleton" />,
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

import { SpellingTest } from './spelling-test';

const THREE_WORDS = [
  { termId: 't1', category: 'symptoms', examFrequencyCount: 0, fromMistakes: false },
  { termId: 't2', category: 'symptoms', examFrequencyCount: 2, fromMistakes: false },
  { termId: 't3', category: 'procedures', examFrequencyCount: 0, fromMistakes: true },
];

beforeEach(() => {
  vi.clearAllMocks();
  mockFetchRecallsAudio.mockResolvedValue({ url: 'blob:oet/audio', provider: 'elevenlabs' });
  mockPlayTransientAudio.mockReturnValue({ addEventListener: vi.fn() });
  mockGuardAudio.mockImplementation(async (action: () => Promise<unknown>) => action());
  mockFetchRecallSpellingSet.mockResolvedValue({
    items: THREE_WORDS,
    total: THREE_WORDS.length,
    source: 'all',
    size: '10',
  });
});

describe('SpellingTest setup (§3C)', () => {
  it('offers the 10 / 20 / 30 / All Words sizes and the Favourites and Review Mistakes quick sets', () => {
    render(<SpellingTest />);

    for (const label of ['10', '20', '30', 'All Words']) {
      expect(screen.getByRole('button', { name: label })).toBeInTheDocument();
    }
    expect(screen.getByRole('button', { name: 'Whole bank' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Favourites' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Review Mistakes' })).toBeInTheDocument();
  });

  it('requests the chosen size and quick set', async () => {
    render(<SpellingTest />);

    await userEvent.click(screen.getByRole('button', { name: '30' }));
    await userEvent.click(screen.getByRole('button', { name: 'Review Mistakes' }));
    await userEvent.click(screen.getByRole('button', { name: 'Start spelling test' }));

    await waitFor(() => {
      expect(mockFetchRecallSpellingSet).toHaveBeenCalledWith('30', 'mistakes');
    });
    expect(mockTrack).toHaveBeenCalledWith(
      'recalls_spelling_test_started',
      expect.objectContaining({ source: 'mistakes', size: '30' }),
    );
  });

  it('explains an empty set instead of opening an empty test', async () => {
    mockFetchRecallSpellingSet.mockResolvedValue({ items: [], total: 0, source: 'mistakes', size: '10' });
    render(<SpellingTest />);

    await userEvent.click(screen.getByRole('button', { name: 'Review Mistakes' }));
    await userEvent.click(screen.getByRole('button', { name: 'Start spelling test' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/No mistakes to review yet/i);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('SpellingTest running (§3C)', () => {
  async function startTest() {
    render(<SpellingTest />);
    await userEvent.click(screen.getByRole('button', { name: 'Start spelling test' }));
    return screen.findByRole('dialog', { name: 'Spelling test' });
  }

  it('auto-plays each word and hides the answer', async () => {
    const dialog = await startTest();

    expect(within(dialog).getByText('Word 1 of 3')).toBeInTheDocument();
    await waitFor(() => expect(mockFetchRecallsAudio).toHaveBeenCalledWith('t1', 'normal'));
    expect(mockPlayTransientAudio).toHaveBeenCalledWith('blob:oet/audio');
    // No canonical spelling anywhere in the running view.
    expect(within(dialog).queryByText('dyspnoea')).not.toBeInTheDocument();
  });

  it('does not allow Check until something is typed', async () => {
    const dialog = await startTest();
    expect(within(dialog).getByRole('button', { name: 'Check' })).toBeDisabled();

    await userEvent.type(within(dialog).getByPlaceholderText('Type the word you hear'), 'x');
    expect(within(dialog).getByRole('button', { name: 'Check' })).toBeEnabled();
  });

  it('gives an immediate result and reveals the canonical spelling only after Check', async () => {
    mockCheckRecallSpelling.mockResolvedValueOnce({
      correct: false,
      canonical: 'dyspnoea',
      wrongAttemptCount: 1,
      addedToMistakes: true,
      removedFromMistakes: false,
    });
    const dialog = await startTest();

    expect(within(dialog).queryByText('dyspnoea')).not.toBeInTheDocument();

    await userEvent.type(within(dialog).getByPlaceholderText('Type the word you hear'), 'dyspnea');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Check' }));

    expect(await within(dialog).findByText('Incorrect')).toBeInTheDocument();
    expect(within(dialog).getByText('dyspnoea')).toBeInTheDocument();
    expect(mockCheckRecallSpelling).toHaveBeenCalledWith('t1', 'dyspnea');
  });

  it('advances to the next word and auto-plays its audio', async () => {
    mockCheckRecallSpelling.mockResolvedValueOnce({
      correct: true,
      canonical: 'dyspnoea',
      wrongAttemptCount: 0,
      addedToMistakes: false,
      removedFromMistakes: false,
    });
    const dialog = await startTest();

    await userEvent.type(within(dialog).getByPlaceholderText('Type the word you hear'), 'dyspnoea');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Check' }));
    await within(dialog).findByText('Correct');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Next Word' }));

    expect(await within(dialog).findByText('Word 2 of 3')).toBeInTheDocument();
    await waitFor(() => expect(mockFetchRecallsAudio).toHaveBeenCalledWith('t2', 'normal'));
  });

  it('flags a word that came from Review Mistakes', async () => {
    mockFetchRecallSpellingSet.mockResolvedValue({
      items: [THREE_WORDS[2]],
      total: 1,
      source: 'mistakes',
      size: '10',
    });
    render(<SpellingTest />);
    await userEvent.click(screen.getByRole('button', { name: 'Start spelling test' }));

    const dialog = await screen.findByRole('dialog', { name: 'Spelling test' });
    expect(within(dialog).getByText('Review mistake')).toBeInTheDocument();
  });

  it('renders no timer', async () => {
    const dialog = await startTest();
    expect(within(dialog).queryByRole('timer')).not.toBeInTheDocument();
  });
});

describe('SpellingTest results (§3C)', () => {
  async function answerAll(correctFlags: boolean[]) {
    render(<SpellingTest />);
    await userEvent.click(screen.getByRole('button', { name: 'Start spelling test' }));
    const dialog = await screen.findByRole('dialog', { name: 'Spelling test' });

    for (let i = 0; i < correctFlags.length; i += 1) {
      const correct = correctFlags[i];
      mockCheckRecallSpelling.mockResolvedValueOnce({
        correct,
        canonical: `word-${i + 1}`,
        wrongAttemptCount: correct ? 0 : 1,
        addedToMistakes: !correct,
        removedFromMistakes: false,
      });

      await userEvent.type(
        within(dialog).getByPlaceholderText('Type the word you hear'),
        correct ? `word-${i + 1}` : 'wrong',
      );
      await userEvent.click(within(dialog).getByRole('button', { name: 'Check' }));
      await within(dialog).findByText(correct ? 'Correct' : 'Incorrect');

      const isLast = i === correctFlags.length - 1;
      await userEvent.click(
        within(dialog).getByRole('button', { name: isLast ? 'See results' : 'Next Word' }),
      );
    }
    return dialog;
  }

  it('reports total correct, total questions and the percentage', async () => {
    const dialog = await answerAll([true, false, true]);

    expect(await within(dialog).findByText('67%')).toBeInTheDocument();
    expect(within(dialog).getByText('2 correct out of 3 questions')).toBeInTheDocument();
    expect(mockTrack).toHaveBeenCalledWith(
      'recalls_spelling_test_completed',
      expect.objectContaining({ questions: 3 }),
    );
  });

  it('reviews the incorrect words on demand', async () => {
    const dialog = await answerAll([true, false, true]);

    await userEvent.click(within(dialog).getByRole('button', { name: /Review incorrect words \(1\)/ }));

    expect(within(dialog).getByText('word-2')).toBeInTheDocument();
    expect(within(dialog).getByText(/You typed:/)).toBeInTheDocument();
  });

  it('congratulates a perfect run and disables the review button', async () => {
    const dialog = await answerAll([true, true, true]);

    expect(await within(dialog).findByText('100%')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /Review incorrect words/ })).toBeDisabled();
    expect(within(dialog).getByText(/Every word was spelled correctly/)).toBeInTheDocument();
  });

  it('can start another test from the results screen', async () => {
    const dialog = await answerAll([true, true, true]);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Start another test' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Start spelling test' })).toBeInTheDocument();
  });
});
