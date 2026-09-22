import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { PlacementReviewSession } from '@/lib/api/admin-placement';

const api = vi.hoisted(() => ({
  fetchPlacementEngineHealth: vi.fn(),
  fetchPlacementInventory: vi.fn(),
  fetchPlacementReviewQueue: vi.fn(),
  fetchPlacementReviewSession: vi.fn(),
  humanScorePlacementSession: vi.fn(),
  rescorePlacementSession: vi.fn(),
}));

vi.mock('@/lib/api/admin-placement', () => ({
  ...api,
  resolvePlacementReviewAudioUrl: () => '/audio',
}));
vi.mock('@/lib/api/binary', () => ({ fetchAuthorizedObjectUrl: vi.fn() }));
vi.mock('@/lib/hooks/use-admin-auth', () => ({
  useAdminAuth: () => ({ role: 'admin', isAuthenticated: true, isLoading: false }),
}));
vi.mock('@/components/admin/placement-accommodations-card', () => ({ PlacementAccommodationsCard: () => null }));

import AdminPlacementReviewPage from './page';

/**
 * A pending_review rating: the engine sends empty trait maps, which is exactly
 * what a human must fill. `module`, `traits` and `tasks` are empty by default,
 * which is what an engine that predates them sends.
 */
function pendingSession(taskId: string, overrides: Partial<PlacementReviewSession> = {}): PlacementReviewSession {
  return {
    sessionId: 'sess-1',
    taskId,
    module: '',
    taskType: '',
    taskPrompt: null,
    candidateAudioUrl: null,
    candidateDraft: 'Candidate draft',
    transcript: null,
    atLower: {},
    atUpper: {},
    aiRationale: null,
    ratingId: '',
    rater: '',
    flags: ['pending_review'],
    traits: [],
    tasks: [],
    ...overrides,
  };
}

function queueRow(taskId: string | null, module = 'WRT') {
  return {
    sessionId: 'sess-1',
    flagType: 'pending_review',
    module,
    details: 'Needs a human rating',
    timestamp: '2026-09-01T10:00:00.000Z',
    taskId,
  };
}

const RATIONALE = 'Clear evidence at the upper band.';

async function openFirstReview(user: ReturnType<typeof userEvent.setup>) {
  render(<AdminPlacementReviewPage />);
  await user.click(await screen.findByRole('button', { name: 'Review' }));
  return screen.findByLabelText(/human score rationale/i);
}

describe('AdminPlacementReviewPage human score', () => {
  beforeEach(() => {
    Object.values(api).forEach((mock) => mock.mockReset());
    api.fetchPlacementEngineHealth.mockResolvedValue({ ready: true });
    api.fetchPlacementInventory.mockRejectedValue(new Error('Inventory offline'));
    api.fetchPlacementReviewQueue.mockResolvedValue([queueRow(null)]);
    api.humanScorePlacementSession.mockResolvedValue({});
  });

  const cases: Array<[string, string[]]> = [
    ['WRT-B1B2-1', ['task_fulfilment', 'organisation', 'grammar', 'vocabulary', 'mechanics_register']],
    ['SPK-B1B2-ER1', ['intelligibility', 'fluency', 'grammar', 'vocabulary', 'communication']],
  ];

  it.each(cases)(
    'falls back to the rubric lists for %s when an older engine sends no traits',
    async (taskId, traits) => {
      const user = userEvent.setup();
      api.fetchPlacementReviewSession.mockResolvedValue(pendingSession(taskId));

      await user.type(await openFirstReview(user), RATIONALE);
      await user.click(screen.getByRole('button', { name: /record human score/i }));

      const expected = Object.fromEntries(traits.map((trait) => [trait, 3]));
      await waitFor(() =>
        expect(api.humanScorePlacementSession).toHaveBeenCalledWith('sess-1', taskId, expected, expected, RATIONALE),
      );
    },
  );

  it('opens the task the queue row names and scores exactly the traits the engine sends', async () => {
    const user = userEvent.setup();
    api.fetchPlacementReviewQueue.mockResolvedValue([queueRow('SPK-B1B2-ER1', 'SPK')]);
    api.fetchPlacementReviewSession.mockResolvedValue(
      pendingSession('SPK-B1B2-ER1', { module: 'SPK', traits: ['fluency', 'grammar'] }),
    );

    await user.type(await openFirstReview(user), RATIONALE);

    expect(api.fetchPlacementReviewSession).toHaveBeenCalledWith('sess-1', 'SPK-B1B2-ER1');
    // Only the engine's traits are offered - not the hard-coded five.
    expect(screen.getByLabelText(/^fluency/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/^intelligibility/i)).toBeNull();

    await user.click(screen.getByRole('button', { name: /record human score/i }));

    const expected = { fluency: 3, grammar: 3 };
    await waitFor(() =>
      expect(api.humanScorePlacementSession).toHaveBeenCalledWith('sess-1', 'SPK-B1B2-ER1', expected, expected, RATIONALE),
    );
  });

  it('sends one score per trait', async () => {
    const user = userEvent.setup();
    api.fetchPlacementReviewQueue.mockResolvedValue([queueRow('SPK-B1B2-ER1', 'SPK')]);
    api.fetchPlacementReviewSession.mockResolvedValue(
      pendingSession('SPK-B1B2-ER1', { module: 'SPK', traits: ['fluency', 'grammar'] }),
    );

    await user.type(await openFirstReview(user), RATIONALE);
    const fluency = screen.getByLabelText(/^fluency/i);
    await user.clear(fluency);
    await user.type(fluency, '5');
    await user.click(screen.getByRole('button', { name: /record human score/i }));

    const expected = { fluency: 5, grammar: 3 };
    await waitFor(() =>
      expect(api.humanScorePlacementSession).toHaveBeenCalledWith('sess-1', 'SPK-B1B2-ER1', expected, expected, RATIONALE),
    );
  });

  it('refuses a score the engine would reject instead of sending it', async () => {
    const user = userEvent.setup();
    api.fetchPlacementReviewQueue.mockResolvedValue([queueRow('SPK-B1B2-ER1', 'SPK')]);
    api.fetchPlacementReviewSession.mockResolvedValue(
      pendingSession('SPK-B1B2-ER1', { module: 'SPK', traits: ['fluency', 'grammar'] }),
    );

    await user.type(await openFirstReview(user), RATIONALE);
    const grammar = screen.getByLabelText(/^grammar/i);
    await user.clear(grammar);
    await user.type(grammar, '7');
    await user.click(screen.getByRole('button', { name: /record human score/i }));

    expect(await screen.findByText(/grammar needs a whole-number rubric score from 0 to 5/i)).toBeInTheDocument();
    expect(api.humanScorePlacementSession).not.toHaveBeenCalled();
  });

  it('lets the reviewer switch between the tasks of one session', async () => {
    const user = userEvent.setup();
    const tasks = [
      { taskId: 'SPK-B1B2-ER1', module: 'SPK', taskType: 'extended_response', status: 'pending_review', hasRecording: true },
      { taskId: 'SPK-B1B2-ER2', module: 'SPK', taskType: 'extended_response', status: 'human_scored', hasRecording: true },
    ];
    api.fetchPlacementReviewQueue.mockResolvedValue([queueRow('SPK-B1B2-ER1', 'SPK')]);
    api.fetchPlacementReviewSession
      .mockResolvedValueOnce(pendingSession('SPK-B1B2-ER1', { module: 'SPK', traits: ['fluency'], tasks }))
      .mockResolvedValueOnce(pendingSession('SPK-B1B2-ER2', { module: 'SPK', traits: ['fluency'], tasks }));

    await openFirstReview(user);

    const picker = screen.getByRole('group', { name: /tasks in this session/i });
    expect(within(picker).getByText('Pending review')).toBeInTheDocument();
    expect(within(picker).getByText('Human scored')).toBeInTheDocument();

    await user.click(within(picker).getByRole('button', { name: /SPK-B1B2-ER2/ }));

    expect(api.fetchPlacementReviewSession).toHaveBeenLastCalledWith('sess-1', 'SPK-B1B2-ER2');
    expect(await screen.findByText(/task SPK-B1B2-ER2/)).toBeInTheDocument();
  });

  it('shows no task picker for a single-task session', async () => {
    const user = userEvent.setup();
    api.fetchPlacementReviewSession.mockResolvedValue(pendingSession('WRT-B1B2-1'));

    await openFirstReview(user);

    expect(screen.queryByRole('group', { name: /tasks in this session/i })).toBeNull();
  });
});
