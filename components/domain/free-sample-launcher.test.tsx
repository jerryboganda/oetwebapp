import { render, screen, waitFor } from '@testing-library/react';
import { Mic, PenTool } from 'lucide-react';

const { mockList, mockTrack } = vi.hoisted(() => ({
  mockList: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: React.AnchorHTMLAttributes<HTMLAnchorElement> & { href?: string }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));
vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api/free-samples', () => ({ listFreeSamples: mockList }));
// Resolve against the real English bundle so the exact owner copy is asserted
// (the global setup mock only echoes keys).
vi.mock('next-intl', async () => {
  const en = (await import('@/messages/en/free-samples.json')).default as Record<string, string>;
  return { useTranslations: () => (key: string) => en[key] ?? key };
});

import { FreeSampleLauncher } from './free-sample-launcher';

const WRITING_PROPS = {
  subtest: 'writing' as const,
  icon: PenTool,
  testId: 'free-card',
  title: 'Free Writing Mock',
  description: 'Try one AI-graded OET letter for free.',
};
const SPEAKING_PROPS = {
  subtest: 'speaking' as const,
  icon: Mic,
  testId: 'free-card',
  title: 'Free Speaking Mock',
  description: 'Try one AI-graded role play for free.',
};

// 22 Sep 2026 handoff (item 2, CRITICAL SECURITY): the server offers at most
// ONE row — the caller's own profession. There is no cross-profession picker.
const BASE = {
  professionId: 'medicine',
  limit: 2,
  lastResultRoute: null as string | null,
  lastSubmissionId: null as string | null,
};
const WRITING_AVAILABLE = {
  ...BASE,
  contentId: 'w-med',
  state: 'available',
  route: '/writing/practice/session/w-med' as string | null,
  successfulCount: 0,
  remaining: 2,
};
const SPEAKING_AVAILABLE = {
  ...BASE,
  contentId: 'rpc-med',
  state: 'available',
  route: '/speaking/roleplay/rpc-med?free=1' as string | null,
  successfulCount: 0,
  remaining: 2,
};

describe('FreeSampleLauncher', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockList.mockResolvedValue([WRITING_AVAILABLE]);
  });

  it('renders nothing when the server offers no sample', async () => {
    mockList.mockResolvedValue([]);
    const { container } = render(<FreeSampleLauncher {...WRITING_PROPS} />);

    await waitFor(() => expect(mockList).toHaveBeenCalledWith('writing'));
    expect(container).toBeEmptyDOMElement();
  });

  it('renders nothing when the lookup fails (a bonus entry point never breaks the page)', async () => {
    mockList.mockRejectedValue(new Error('boom'));
    const { container } = render(<FreeSampleLauncher {...WRITING_PROPS} />);

    await waitFor(() => expect(mockList).toHaveBeenCalled());
    expect(container).toBeEmptyDOMElement();
  });

  it('tracks free_sample_click on click', async () => {
    const userEvent = (await import('@testing-library/user-event')).default.setup();
    render(<FreeSampleLauncher {...WRITING_PROPS} />);

    await userEvent.click(await screen.findByTestId('free-card'));

    expect(mockTrack).toHaveBeenCalledWith('free_sample_click', { module: 'writing', professionId: 'medicine', state: 'available' });
  });

  describe('Writing', () => {
    it('available: links to the case note and states the allowance — no picker, no profession switch', async () => {
      render(<FreeSampleLauncher {...WRITING_PROPS} />);

      const card = await screen.findByTestId('free-card');
      expect(card).toHaveAttribute('href', '/writing/practice/session/w-med');
      expect(screen.getByTestId('free-card-status')).toHaveTextContent(
        'Free sample includes two graded submissions.',
      );
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(screen.queryByRole('radio')).not.toBeInTheDocument();
    });

    it('retry_available: the second free result is a fresh attempt on the server start route, never a revise page', async () => {
      mockList.mockResolvedValue([{
        ...WRITING_AVAILABLE,
        state: 'retry_available',
        route: '/writing/practice/session/w-med',
        successfulCount: 1,
        remaining: 1,
        lastResultRoute: '/writing/submissions/sub-1/results',
        lastSubmissionId: 'sub-1',
      }]);
      render(<FreeSampleLauncher {...WRITING_PROPS} />);

      const card = await screen.findByTestId('free-card');
      expect(card).toHaveAttribute('href', '/writing/practice/session/w-med');
      expect(screen.getByTestId('free-card-status')).toHaveTextContent('Practice this again - 1 free attempt remaining');
      expect(card).not.toHaveTextContent(/revise|resubmit/i);
    });

    it('retry_available without a server route is inert: no revise link is ever built from lastSubmissionId', async () => {
      mockList.mockResolvedValue([{
        ...WRITING_AVAILABLE,
        state: 'retry_available',
        route: null,
        successfulCount: 1,
        remaining: 1,
        lastSubmissionId: 'sub-9',
      }]);
      render(<FreeSampleLauncher {...WRITING_PROPS} />);

      const card = await screen.findByTestId('free-card');
      expect(card).not.toHaveAttribute('href');
    });

    it('in_progress: follows the server route (the letter being graded), never the previous result', async () => {
      mockList.mockResolvedValue([{
        ...WRITING_AVAILABLE,
        state: 'in_progress',
        route: '/writing/submissions/sub-2/grading',
        successfulCount: 1,
        remaining: 1,
        lastResultRoute: '/writing/submissions/sub-1/results',
        lastSubmissionId: 'sub-1',
      }]);
      render(<FreeSampleLauncher {...WRITING_PROPS} />);

      const card = await screen.findByTestId('free-card');
      expect(card).toHaveAttribute('href', '/writing/submissions/sub-2/grading');
      expect(card).toHaveTextContent('Your result is being processed');
    });

    it('grading_failed: says the letter is saved and links to Retry on the SAME letter, never a fresh free start', async () => {
      mockList.mockResolvedValue([{
        ...WRITING_AVAILABLE,
        state: 'grading_failed',
        route: '/writing/submissions/sub-3/grading',
      }]);
      render(<FreeSampleLauncher {...WRITING_PROPS} />);

      const card = await screen.findByTestId('free-card');
      expect(card).toHaveAttribute('href', '/writing/submissions/sub-3/grading');
      expect(screen.getByTestId('free-card-status')).toHaveTextContent(
        "Your sample letter is saved but grading didn't finish — retry",
      );
    });

    it('grading_failed without a server route stays inert rather than falling back to the start route', async () => {
      mockList.mockResolvedValue([{ ...WRITING_AVAILABLE, state: 'grading_failed', route: null }]);
      render(<FreeSampleLauncher {...WRITING_PROPS} />);

      const card = await screen.findByTestId('free-card');
      expect(card).toBeDisabled();
      expect(card).not.toHaveAttribute('href');
    });

    it('completed: inert card reading "Free sample completed"', async () => {
      mockList.mockResolvedValue([{
        ...WRITING_AVAILABLE,
        state: 'completed',
        successfulCount: 2,
        remaining: 0,
        lastResultRoute: '/writing/submissions/sub-2/results',
        lastSubmissionId: 'sub-2',
      }]);
      render(<FreeSampleLauncher {...WRITING_PROPS} />);

      const card = await screen.findByTestId('free-card');
      expect(card).toBeDisabled();
      expect(card).not.toHaveAttribute('href');
      expect(card).toHaveTextContent('Free sample completed');
    });
  });

  describe('Speaking', () => {
    beforeEach(() => {
      mockList.mockResolvedValue([SPEAKING_AVAILABLE]);
    });

    it('available: links to the free card and states the allowance', async () => {
      render(<FreeSampleLauncher {...SPEAKING_PROPS} />);

      const card = await screen.findByTestId('free-card');
      expect(mockList).toHaveBeenCalledWith('speaking');
      expect(card).toHaveAttribute('href', '/speaking/roleplay/rpc-med?free=1');
      expect(card).toHaveTextContent('Free sample includes one full attempt. New attempts require Speaking credits.');
      // A completed Speaking attempt has no free retry (owner spec 4 Oct 2026).
      expect(card).not.toHaveTextContent(/free retry/i);
    });

    it('never advertises a free retry for a completed Speaking attempt, even if the server still says retry_available', async () => {
      mockList.mockResolvedValue([{
        ...SPEAKING_AVAILABLE,
        state: 'retry_available',
        successfulCount: 1,
        remaining: 1,
        lastResultRoute: '/speaking/sessions/s-1/results',
      }]);
      render(<FreeSampleLauncher {...SPEAKING_PROPS} />);

      const card = await screen.findByTestId('free-card');
      expect(card).toBeDisabled();
      expect(card).not.toHaveAttribute('href');
      expect(card).toHaveTextContent('Free sample completed');
      expect(card).not.toHaveTextContent(/try again/i);
      expect(card).not.toHaveTextContent(/free retry/i);
    });

    it('grading_failed: re-runs grading of the SAME saved role-play and says no credits are used', async () => {
      mockList.mockResolvedValue([{
        ...SPEAKING_AVAILABLE,
        state: 'grading_failed',
        route: '/speaking/sessions/s-1/results',
      }]);
      render(<FreeSampleLauncher {...SPEAKING_PROPS} />);

      const card = await screen.findByTestId('free-card');
      expect(card).toHaveAttribute('href', '/speaking/sessions/s-1/results');
      expect(card).toHaveTextContent("Your role-play is saved but grading didn't finish — retry (no credits used)");
      expect(card).not.toHaveTextContent(/sample letter/i);
    });

    it('in_progress: "Your result is being processed" links to the result', async () => {
      mockList.mockResolvedValue([{
        ...SPEAKING_AVAILABLE,
        state: 'in_progress',
        lastResultRoute: '/speaking/sessions/s-1/results',
      }]);
      render(<FreeSampleLauncher {...SPEAKING_PROPS} />);

      const card = await screen.findByTestId('free-card');
      expect(card).toHaveAttribute('href', '/speaking/sessions/s-1/results');
      expect(card).toHaveTextContent('Your result is being processed');
    });

    it('completed: inert card reading "Free sample completed"', async () => {
      mockList.mockResolvedValue([{ ...SPEAKING_AVAILABLE, state: 'completed', successfulCount: 2, remaining: 0 }]);
      render(<FreeSampleLauncher {...SPEAKING_PROPS} />);

      const card = await screen.findByTestId('free-card');
      expect(card).toBeDisabled();
      expect(card).toHaveTextContent('Free sample completed');
    });

    it("unavailable: inert card explaining the profession lock, never another profession's card", async () => {
      mockList.mockResolvedValue([{ ...SPEAKING_AVAILABLE, state: 'unavailable', route: null }]);
      render(<FreeSampleLauncher {...SPEAKING_PROPS} />);

      const card = await screen.findByTestId('free-card');
      expect(card).toBeDisabled();
      expect(card).not.toHaveAttribute('href');
      expect(card).toHaveTextContent('Free sample unavailable for your registered profession.');
    });
  });
});
