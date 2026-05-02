import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ListenAndType } from '@/components/domain/recalls/listen-and-type';
import * as api from '@/lib/api';

describe('ListenAndType', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
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
});
