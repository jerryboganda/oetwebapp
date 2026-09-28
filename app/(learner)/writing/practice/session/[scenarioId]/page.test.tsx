import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '@/lib/api/client';

/**
 * Writing Addendum Rev8 §16 — P0 task-loading failure. When the eligibility
 * gate fails the page must show a real, recoverable error state (Retry + a way
 * back to the library), never an endless "Loading scenario…" with "No task
 * prompt available."; entitlement refusals open the credits modal instead of a
 * dead-end banner.
 */

const {
  checkWritingScenarioEligibility,
  createWritingSubmission,
  getWritingDraftV2,
  getWritingHighlights,
  getWritingScenario,
  putWritingDraftV2,
  putWritingHighlights,
  mockPush,
} = vi.hoisted(() => ({
  checkWritingScenarioEligibility: vi.fn(),
  createWritingSubmission: vi.fn(),
  getWritingDraftV2: vi.fn(),
  getWritingHighlights: vi.fn(),
  getWritingScenario: vi.fn(),
  putWritingDraftV2: vi.fn(),
  putWritingHighlights: vi.fn(),
  mockPush: vi.fn(),
}));

vi.mock('@/lib/writing/api', () => ({
  checkWritingScenarioEligibility,
  createWritingSubmission,
  getWritingDraftV2,
  getWritingHighlights,
  getWritingScenario,
  putWritingDraftV2,
  putWritingHighlights,
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ scenarioId: 'scenario-1' }),
  useRouter: () => ({ push: mockPush, replace: vi.fn() }),
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/lib/credit-feedback', () => ({ showCreditFeedback: vi.fn() }));

// The PDF viewer, reading overlay and editor have their own suites; stub them
// so this one stays on the page's load / error behaviour.
vi.mock('@/components/domain/writing/WritingStimulus', () => ({
  WritingStimulus: ({ scenario }: { scenario: { taskPromptMarkdown?: string | null } | null }) => (
    <div data-testid="stimulus">{scenario?.taskPromptMarkdown}</div>
  ),
}));
vi.mock('@/components/domain/writing/WritingReadingWindowOverlay', () => ({
  WritingReadingWindowOverlay: () => null,
}));
vi.mock('@/components/domain/writing/WritingEditorV2', () => ({
  WritingEditorV2: () => null,
}));

import WritingPracticeSessionPage from './page';

const SCENARIO = {
  id: 'scenario-1',
  title: 'Nursing discharge — Mrs Patel',
  letterType: 'LT-DG',
  profession: 'nursing',
  subDiscipline: null,
  topics: [],
  difficulty: 3,
  caseNotesStructured: [{ index: 1, text: 'Case note.', relevance: 'relevant' }],
  isDiagnostic: false,
  status: 'published',
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
  taskPromptMarkdown: 'Write a discharge letter to the community nurse.',
  fixedInstructions: [],
  readingTimeSeconds: 300,
  writingTimeSeconds: 2400,
  wordGuideMin: 180,
  wordGuideMax: 200,
  stimulusPdfMediaAssetId: null,
  stimulusPdfDownloadPath: null,
};

describe('Writing practice session — load failures (Addendum Rev8 §16)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    getWritingScenario.mockResolvedValue(SCENARIO);
    getWritingDraftV2.mockResolvedValue(null);
    getWritingHighlights.mockResolvedValue(null);
  });

  it('shows a real error state with Retry when eligibility fails with a server error, then recovers', async () => {
    checkWritingScenarioEligibility
      .mockRejectedValueOnce(new ApiError(500, 'internal_server_error', 'An unexpected server error occurred.', true))
      .mockResolvedValueOnce({ feedbackMessage: null });
    const user = userEvent.setup();

    render(<WritingPracticeSessionPage />);

    expect(await screen.findByText('writing.practice.session.loadError.title')).toBeInTheDocument();
    // Candidate-safe copy — never the raw 5xx body, never a stuck loader.
    expect(screen.getByText('writing.practice.session.error.load')).toBeInTheDocument();
    expect(screen.queryByText('An unexpected server error occurred.')).not.toBeInTheDocument();
    expect(screen.queryByText('writing.practice.session.scenarioLoading')).not.toBeInTheDocument();
    expect(getWritingScenario).not.toHaveBeenCalled();
    expect(
      screen.getByRole('link', { name: 'writing.practice.session.loadError.backToLibrary' }),
    ).toHaveAttribute('href', '/writing/practice/library');

    await user.click(screen.getByRole('button', { name: 'writing.practice.session.loadError.retry' }));

    expect(await screen.findByText('Nursing discharge — Mrs Patel')).toBeInTheDocument();
    expect(checkWritingScenarioEligibility).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId('stimulus')).toHaveTextContent('Write a discharge letter to the community nurse.');
    expect(screen.queryByText('writing.practice.session.loadError.title')).not.toBeInTheDocument();
  });

  it('opens the credits modal (not a dead-end banner) for premium_required', async () => {
    checkWritingScenarioEligibility.mockRejectedValue(
      new ApiError(402, 'premium_required', 'Writing practice requires an active subscription.', false),
    );

    render(<WritingPracticeSessionPage />);

    const dialog = await screen.findByRole('dialog', { name: /not enough credits/i });
    expect(dialog).toHaveTextContent('Writing practice requires an active subscription.');
    expect(screen.queryByText('writing.practice.session.loadError.title')).not.toBeInTheDocument();
    expect(getWritingScenario).not.toHaveBeenCalled();
  });

  it('explains an incomplete task in candidate-safe copy', async () => {
    checkWritingScenarioEligibility.mockRejectedValue(
      new ApiError(409, 'writing_task_incomplete', 'This writing task is being updated and cannot be opened yet.', false),
    );

    render(<WritingPracticeSessionPage />);

    expect(await screen.findByText('writing.practice.session.loadError.incomplete')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'writing.practice.session.loadError.retry' })).toBeInTheDocument();
  });
});
