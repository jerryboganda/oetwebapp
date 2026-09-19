import { render, screen, waitFor } from '@testing-library/react';
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

/** A pending_review rating: the engine sends empty trait maps, which is exactly what a human must fill. */
function pendingSession(taskId: string): PlacementReviewSession {
  return {
    sessionId: 'sess-1',
    taskId,
    taskPrompt: null,
    candidateAudioUrl: null,
    candidateDraft: 'Candidate draft',
    transcript: null,
    atLower: {},
    atUpper: {},
    aiRationale: null,
    flags: ['pending_review'],
  };
}

describe('AdminPlacementReviewPage human score', () => {
  beforeEach(() => {
    Object.values(api).forEach((mock) => mock.mockReset());
    api.fetchPlacementEngineHealth.mockResolvedValue({ ready: true });
    api.fetchPlacementInventory.mockRejectedValue(new Error('Inventory offline'));
    api.fetchPlacementReviewQueue.mockResolvedValue([
      {
        sessionId: 'sess-1',
        flagType: 'pending_review',
        module: 'WRT',
        details: 'Needs a human rating',
        timestamp: '2026-09-01T10:00:00.000Z',
      },
    ]);
    api.humanScorePlacementSession.mockResolvedValue({});
  });

  const cases: Array<[string, string[]]> = [
    ['WRT-B1B2-1', ['task_fulfilment', 'organisation', 'grammar', 'vocabulary', 'mechanics_register']],
    ['SPK-B1B2-ER1', ['intelligibility', 'fluency', 'grammar', 'vocabulary', 'communication']],
  ];

  it.each(cases)('scores %s on the named traits the engine reads, not a made-up key', async (taskId, traits) => {
    const user = userEvent.setup();
    api.fetchPlacementReviewSession.mockResolvedValue(pendingSession(taskId));

    render(<AdminPlacementReviewPage />);
    await user.click(await screen.findByRole('button', { name: 'Review' }));
    await user.type(await screen.findByLabelText(/human score rationale/i), 'Clear evidence at the upper band.');
    await user.click(screen.getByRole('button', { name: /record human score/i }));

    const expected = Object.fromEntries(traits.map((trait) => [trait, 3]));
    await waitFor(() =>
      expect(api.humanScorePlacementSession).toHaveBeenCalledWith(
        'sess-1',
        taskId,
        expected,
        expected,
        'Clear evidence at the upper band.',
      ),
    );
  });
});
