import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ListenAndType } from '@/components/domain/recalls/listen-and-type';
import { ApiError } from '@/lib/api';
import * as api from '@/lib/api';

describe('ListenAndType', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    Object.defineProperty(window, 'HTMLMediaElement', {
      writable: true,
      value: class {
        play() {
          return Promise.resolve();
        }
      },
    });
  });

  it('submits the typed answer and renders the diff segments', async () => {
    const submit = vi
      .spyOn(api, 'submitRecallsListenType')
      .mockResolvedValue({
        code: 'missing_letter',
        isCorrect: false,
        distance: 1,
        canonical: 'inflammation',
        typed: 'inflamation',
        americanSpelling: null,
        segments: [
          { kind: 'equal', text: 'infla' },
          { kind: 'missing', text: 'm' },
          { kind: 'equal', text: 'mation' },
        ],
      });

    const user = userEvent.setup();
    render(<ListenAndType termId="term-1" termHint="A definition" />);

    const input = screen.getByPlaceholderText(/Type what you hear/i);
    await user.type(input, 'inflamation');
    await user.click(screen.getByRole('button', { name: /Check answer/i }));

    expect(submit).toHaveBeenCalledWith('term-1', 'inflamation');
    expect(await screen.findByText(/Missing letter/i)).toBeInTheDocument();
    expect(screen.getByText('m')).toBeInTheDocument();
  });

  it('shows correct state on a clean match', async () => {
    vi.spyOn(api, 'submitRecallsListenType').mockResolvedValue({
      code: 'correct',
      isCorrect: true,
      distance: 0,
      canonical: 'cardiac',
      typed: 'cardiac',
      americanSpelling: null,
      segments: [{ kind: 'equal', text: 'cardiac' }],
    });

    const user = userEvent.setup();
    render(<ListenAndType termId="term-2" />);
    await user.type(screen.getByPlaceholderText(/Type what you hear/i), 'cardiac');
    await user.click(screen.getByRole('button', { name: /Check answer/i }));

    expect(await screen.findByText(/^Correct$/i)).toBeInTheDocument();
  });

  it('fetches paid-gated audio before playback', async () => {
    const fetchAudio = vi.spyOn(api, 'fetchRecallsAudio').mockResolvedValue({
      url: '/v1/recalls/audio/term-3/file',
      provider: 'mock',
    });
    const play = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('Audio', class {
      play() {
        return play();
      }
    });

    const user = userEvent.setup();
    render(<ListenAndType termId="term-3" />);
    await user.click(screen.getByRole('button', { name: /play british pronunciation/i }));

    expect(fetchAudio).toHaveBeenCalledWith('term-3', 'normal');
    expect(play).toHaveBeenCalledTimes(1);
  });

  it('shows the upgrade modal when the audio gate returns payment required', async () => {
    vi.spyOn(api, 'fetchRecallsAudio').mockRejectedValue(
      new ApiError(402, 'subscription_required', 'Pronunciation audio is available for paid candidates only.', false),
    );

    const user = userEvent.setup();
    render(<ListenAndType termId="term-4" />);
    await user.click(screen.getByRole('button', { name: /play british pronunciation/i }));

    expect(await screen.findByText(/upgrade to hear pronunciations/i)).toBeInTheDocument();
  });
});
