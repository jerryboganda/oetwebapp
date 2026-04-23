import type { ReactNode } from 'react';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ApiError } from '@/lib/api';
const navigation = vi.hoisted(() => ({
  pathname: '/expert',
  searchParams: new URLSearchParams(),
  params: {} as Record<string, string>,
  push: vi.fn(),
  replace: vi.fn(),
  back: vi.fn(),
  refresh: vi.fn(),
}));

const api = vi.hoisted(() => ({
  fetchExpertQueueFilterMetadata: vi.fn(),
  fetchReviewQueue: vi.fn(),
  claimReview: vi.fn(),
  releaseReview: vi.fn(),
  fetchCalibrationCases: vi.fn(),
  fetchCalibrationNotes: vi.fn(),
  fetchCalibrationCaseDetail: vi.fn(),
  saveCalibrationDraft: vi.fn(),
  submitCalibrationCase: vi.fn(),
  fetchExpertLearners: vi.fn(),
  fetchLearnerProfile: vi.fn(),
  fetchExpertLearnerReviewContext: vi.fn(),
  fetchExpertMetrics: vi.fn(),
  fetchExpertSchedule: vi.fn(),
  saveExpertSchedule: vi.fn(),
  fetchScheduleExceptions: vi.fn(),
  createScheduleException: vi.fn(),
  deleteScheduleException: vi.fn(),
  track: vi.fn(),
}));

vi.mock('recharts', () => ({
  ResponsiveContainer: ({ children }: { children: ReactNode }) => <div data-testid="responsive-chart">{children}</div>,
  BarChart: ({ children }: { children: ReactNode }) => <div data-testid="bar-chart">{children}</div>,
  Bar: () => null,
  CartesianGrid: () => null,
  Tooltip: () => null,
  XAxis: () => null,
  YAxis: () => null,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: api.track,
  },
}));

vi.mock('@/lib/api', () => {
  type FieldError = { field: string; code: string; message: string };
  class ApiError extends Error {
    status: number; code: string; retryable: boolean; userMessage: string; fieldErrors: FieldError[];
    constructor(s: number, c: string, m: string, r = false, f: FieldError[] = []) { super(m); this.name='ApiError'; this.status=s; this.code=c; this.retryable=r; this.userMessage=m; this.fieldErrors=f; }
  }
  return {
    ApiError,
    fetchExpertQueueFilterMetadata: api.fetchExpertQueueFilterMetadata,
    fetchReviewQueue: api.fetchReviewQueue,
    claimReview: api.claimReview,
    releaseReview: api.releaseReview,
    fetchCalibrationCases: api.fetchCalibrationCases,
    fetchCalibrationNotes: api.fetchCalibrationNotes,
    fetchCalibrationCaseDetail: api.fetchCalibrationCaseDetail,
    saveCalibrationDraft: api.saveCalibrationDraft,
    submitCalibrationCase: api.submitCalibrationCase,
    fetchExpertLearners: api.fetchExpertLearners,
    fetchLearnerProfile: api.fetchLearnerProfile,
    fetchExpertLearnerReviewContext: api.fetchExpertLearnerReviewContext,
    fetchExpertMetrics: api.fetchExpertMetrics,
    fetchExpertSchedule: api.fetchExpertSchedule,
    saveExpertSchedule: api.saveExpertSchedule,
    fetchScheduleExceptions: api.fetchScheduleExceptions,
    createScheduleException: api.createScheduleException,
    deleteScheduleException: api.deleteScheduleException,
    isApiError: (e: unknown) => e instanceof ApiError,
  };
});

import CalibrationCaseWorkspacePage from './calibration/[caseId]/page';
import CalibrationCenterPage from './calibration/page';
import AssignedLearnerPage from './learners/[learnerId]/page';
import LearnersIndexPage from './learners/page';
import PerformanceMetricsPage from './metrics/page';
import ReviewQueuePage from './queue/page';
import SchedulePage from './schedule/page';
import { renderWithRouter } from '@/tests/test-utils';

function renderPage(ui: React.ReactElement) {
  return renderWithRouter(ui, {
    pathname: navigation.pathname,
    searchParams: navigation.searchParams,
    params: navigation.params,
    router: { push: navigation.push, replace: navigation.replace, back: navigation.back, refresh: navigation.refresh },
  });
}

beforeAll(() => {
  class ResizeObserverMock {
    observe() {}
    disconnect() {}
    unobserve() {}
  }

  vi.stubGlobal('ResizeObserver', ResizeObserverMock);
});

beforeEach(() => {
  vi.clearAllMocks();
  Object.values(api).forEach((mock) => mock.mockReset());
  navigation.pathname = '/expert';
  navigation.searchParams = new URLSearchParams();
  navigation.params = {};
  vi.mocked(window.matchMedia).mockImplementation((query: string) => ({
    matches: true,
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    addListener: vi.fn(),
    removeListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }));
});

describe('Expert Non-Review Pages', () => {
  it('renders the queue inside the learner-style route surface', async () => {
    navigation.pathname = '/expert/queue';
    api.fetchExpertQueueFilterMetadata.mockResolvedValue({
      types: ['writing'],
      professions: ['medicine'],
      priorities: ['high'],
      statuses: ['queued'],
      confidenceBands: ['high'],
      assignmentStates: ['unassigned'],
    });
    api.fetchReviewQueue.mockResolvedValue({
      items: [
        {
          id: 'rev-1',
          learnerId: 'learner-1',
          learnerName: 'Dr Amina Khan',
          profession: 'medicine',
          subTest: 'writing',
          type: 'writing',
          aiConfidence: 'high',
          priority: 'high',
          slaDue: '2026-04-01T10:00:00.000Z',
          status: 'queued',
          createdAt: '2026-04-01T08:00:00.000Z',
          isOverdue: false,
          availableActions: {
            canClaim: true,
            canRelease: false,
            canOpen: false,
            canSaveDraft: false,
            canSubmit: false,
            canRequestRework: false,
            readOnly: false,
          },
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 25,
      lastUpdatedAt: '2026-04-01T09:00:00.000Z',
    });

    renderPage(<ReviewQueuePage />);

    expect(await screen.findByRole('heading', { name: /^review queue$/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /find the right work fast/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/review queue table/i)).toBeInTheDocument();
    expect(screen.getAllByText('Dr Amina Khan').length).toBeGreaterThan(0);
  });

  it('renders the calibration center inside the learner-style route surface', async () => {
    api.fetchCalibrationCases.mockResolvedValue([
      {
        id: 'case-1',
        title: 'Writing benchmark 1',
        profession: 'medicine',
        subTest: 'writing',
        type: 'writing',
        benchmarkScore: 5,
        reviewerScore: 5,
        alignmentScore: 96.3,
        status: 'completed',
        createdAt: '2026-04-01T08:00:00.000Z',
      },
      {
        id: 'case-2',
        title: 'Speaking benchmark draft',
        profession: 'medicine',
        subTest: 'speaking',
        type: 'speaking',
        benchmarkScore: 5,
        reviewerScore: undefined,
        alignmentScore: null,
        status: 'draft',
        createdAt: '2026-04-02T08:00:00.000Z',
      },
    ]);
    api.fetchCalibrationNotes.mockResolvedValue([
      {
        id: 'note-1',
        type: 'completed',
        message: 'Benchmark notes recorded.',
        caseId: 'case-1',
        createdAt: '2026-04-01T09:00:00.000Z',
      },
    ]);

    renderPage(<CalibrationCenterPage />);

    expect(await screen.findByRole('heading', { name: /^calibration$/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /open benchmark workspaces/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /calibration activity/i })).toBeInTheDocument();
    expect(screen.getAllByText(/aligned \(96.3%\)/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText('Draft').length).toBeGreaterThan(0);
    expect(screen.getAllByRole('button', { name: /resume draft/i }).length).toBeGreaterThan(0);
    expect(screen.getByText(/benchmark notes recorded/i)).toBeInTheDocument();
  });

  it('renders the calibration case workspace inside the learner-style route surface', async () => {
    navigation.params = { caseId: 'case-1' };
    api.fetchCalibrationCaseDetail.mockResolvedValue({
      id: 'case-1',
      title: 'Writing benchmark 1',
      profession: 'medicine',
      subTest: 'writing',
      type: 'writing',
      benchmarkScore: 3,
      status: 'pending',
      createdAt: '2026-04-01T08:00:00.000Z',
      benchmarkLabel: 'Benchmark A',
      difficulty: 'Moderate',
      artifacts: [{ kind: 'prompt', title: 'Prompt', content: 'Review the attached discharge summary.' }],
      benchmarkRubric: [{ criterion: 'purpose', benchmarkScore: 3, rationale: 'Purpose is fully achieved.' }],
      referenceNotes: ['Anchor to clinical purpose first.'],
      existingSubmission: null,
    });

    renderPage(<CalibrationCaseWorkspacePage />);

    expect(await screen.findByRole('heading', { name: /writing benchmark 1/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /back to calibration/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /benchmark evidence/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /reference scoring/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /save draft/i })).toBeInTheDocument();
  });

  it('prefills draft calibration detail, keeps it editable, and saves partial draft data', async () => {
    const user = userEvent.setup();
    navigation.params = { caseId: 'case-1' };
    const draftDetail = {
      id: 'case-1',
      title: 'Writing benchmark draft',
      profession: 'medicine',
      subTest: 'writing',
      type: 'writing',
      benchmarkScore: 3,
      status: 'draft',
      createdAt: '2026-04-01T08:00:00.000Z',
      benchmarkLabel: 'Benchmark A',
      difficulty: 'Moderate',
      artifacts: [{ kind: 'prompt', title: 'Prompt', content: 'Review the attached discharge summary.' }],
      benchmarkRubric: [{ criterion: 'purpose', benchmarkScore: 3, rationale: 'Purpose is fully achieved.' }],
      referenceNotes: ['Anchor to clinical purpose first.'],
      existingSubmission: {
        reviewerId: 'expert-1',
        reviewerName: 'Expert One',
        reviewerScore: 2,
        alignmentScore: 0,
        disagreementSummary: '',
        notes: 'Borderline purpose.',
        submittedScores: { purpose: 2 },
        submittedAt: '2026-04-01T09:00:00.000Z',
        isDraft: true,
        updatedAt: '2026-04-01T09:30:00.000Z',
      },
    };
    const refreshedDraftDetail = {
      ...draftDetail,
      existingSubmission: {
        ...draftDetail.existingSubmission,
        notes: 'Updated after save.',
        updatedAt: '2026-04-01T10:00:00.000Z',
      },
    };

    api.fetchCalibrationCaseDetail.mockResolvedValueOnce(draftDetail).mockResolvedValueOnce(refreshedDraftDetail);
    api.saveCalibrationDraft.mockResolvedValue({
      success: true,
      caseId: 'case-1',
      isDraft: true,
      scores: { purpose: 2 },
      notes: 'Updated after save.',
      updatedAt: '2026-04-01T09:30:00.000Z',
    });

    renderPage(<CalibrationCaseWorkspacePage />);

    const scoreSelect = await screen.findByLabelText('Calibration score for purpose');
    expect(scoreSelect).toHaveValue('2');
    expect(scoreSelect).not.toBeDisabled();
    expect(screen.getByLabelText(/calibration notes/i)).toHaveValue('Borderline purpose.');
    expect(screen.getAllByText(/draft saved/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/draft \(editable\)/i).length).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: /save draft/i }));
    expect(api.fetchCalibrationCaseDetail).toHaveBeenCalledTimes(2);
    expect(api.fetchCalibrationCaseDetail).toHaveBeenNthCalledWith(1, 'case-1');
    expect(api.fetchCalibrationCaseDetail).toHaveBeenNthCalledWith(2, 'case-1');
    expect(api.saveCalibrationDraft).toHaveBeenCalledWith('case-1', {
      scores: { purpose: 2 },
      notes: 'Borderline purpose.',
    });
    expect(await screen.findByDisplayValue('Updated after save.')).toBeInTheDocument();
  });

  it('submits from a draft, reloads the final submission, and locks the workspace', async () => {
    const user = userEvent.setup();
    navigation.params = { caseId: 'case-1' };
    const draftDetail = {
      id: 'case-1',
      title: 'Writing benchmark draft',
      profession: 'medicine',
      subTest: 'writing',
      type: 'writing',
      benchmarkScore: 3,
      status: 'draft',
      createdAt: '2026-04-01T08:00:00.000Z',
      benchmarkLabel: 'Benchmark A',
      difficulty: 'Moderate',
      artifacts: [{ kind: 'prompt', title: 'Prompt', content: 'Review the attached discharge summary.' }],
      benchmarkRubric: [{ criterion: 'purpose', benchmarkScore: 3, rationale: 'Purpose is fully achieved.' }],
      referenceNotes: ['Anchor to clinical purpose first.'],
      existingSubmission: {
        reviewerId: 'expert-1',
        reviewerName: 'Expert One',
        reviewerScore: 2,
        alignmentScore: 0,
        disagreementSummary: '',
        notes: 'Borderline purpose.',
        submittedScores: { purpose: 2 },
        submittedAt: '2026-04-01T09:00:00.000Z',
        isDraft: true,
        updatedAt: '2026-04-01T09:30:00.000Z',
      },
    };

    const finalDetail = {
      ...draftDetail,
      status: 'completed',
      existingSubmission: {
        ...draftDetail.existingSubmission,
        isDraft: false,
        notes: 'Final aligned submission.',
        reviewerScore: 3,
        alignmentScore: 96.3,
        disagreementSummary: 'Aligned with benchmark.',
      },
    };

    api.fetchCalibrationCaseDetail.mockResolvedValueOnce(draftDetail).mockResolvedValueOnce(finalDetail);
    api.submitCalibrationCase.mockResolvedValue({ success: true, caseId: 'case-1', alignment: 96.3 });

    renderPage(<CalibrationCaseWorkspacePage />);

    await user.click(await screen.findByRole('button', { name: /submit calibration/i }));

    expect(api.submitCalibrationCase).toHaveBeenCalledWith('case-1', {
      scores: { purpose: 2 },
      notes: 'Borderline purpose.',
    });
    expect(api.fetchCalibrationCaseDetail).toHaveBeenCalledTimes(2);
    expect(await screen.findByText(/submitted scores and notes from your completed benchmark attempt/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /save draft/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /submit calibration/i })).not.toBeInTheDocument();
    expect(screen.getByText(/96.3/i)).toBeInTheDocument();
  });

  it('surfaces already-submitted conflicts and switches to locked read-only state', async () => {
    const user = userEvent.setup();
    navigation.params = { caseId: 'case-1' };
    const draftDetail = {
      id: 'case-1',
      title: 'Writing benchmark draft',
      profession: 'medicine',
      subTest: 'writing',
      type: 'writing',
      benchmarkScore: 3,
      status: 'draft',
      createdAt: '2026-04-01T08:00:00.000Z',
      benchmarkLabel: 'Benchmark A',
      difficulty: 'Moderate',
      artifacts: [{ kind: 'prompt', title: 'Prompt', content: 'Review the attached discharge summary.' }],
      benchmarkRubric: [{ criterion: 'purpose', benchmarkScore: 3, rationale: 'Purpose is fully achieved.' }],
      referenceNotes: ['Anchor to clinical purpose first.'],
      existingSubmission: {
        reviewerId: 'expert-1',
        reviewerName: 'Expert One',
        reviewerScore: 2,
        alignmentScore: 0,
        disagreementSummary: '',
        notes: 'Borderline purpose.',
        submittedScores: { purpose: 2 },
        submittedAt: '2026-04-01T09:00:00.000Z',
        isDraft: true,
        updatedAt: '2026-04-01T09:30:00.000Z',
      },
    };

    const completedDetail = {
      ...draftDetail,
      status: 'completed',
      existingSubmission: {
        ...draftDetail.existingSubmission,
        reviewerScore: 3,
        alignmentScore: 96.3,
        isDraft: false,
        disagreementSummary: 'Aligned with benchmark.',
        notes: 'Final aligned submission.',
      },
    };

    api.fetchCalibrationCaseDetail.mockResolvedValueOnce(draftDetail).mockResolvedValueOnce(completedDetail);
    api.submitCalibrationCase.mockRejectedValue(
      new ApiError(
        409,
        'calibration_already_submitted',
        'This calibration case has already been submitted.',
        false,
      ),
    );

    renderPage(<CalibrationCaseWorkspacePage />);

    await user.click(await screen.findByRole('button', { name: /submit calibration/i }));

    expect(api.submitCalibrationCase).toHaveBeenCalledWith('case-1', {
      scores: { purpose: 2 },
      notes: 'Borderline purpose.',
    });
    expect(api.fetchCalibrationCaseDetail).toHaveBeenCalledTimes(2);
    expect(await screen.findByText(/calibration already submitted/i)).toBeInTheDocument();
    expect(await screen.findByText(/already been submitted/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /save draft/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /submit calibration/i })).not.toBeInTheDocument();
    expect(screen.getByText(/submitted scores and notes from your completed benchmark attempt/i)).toBeInTheDocument();
  });

  it('locks the workspace when a draft save hits an already-submitted conflict', async () => {
    const user = userEvent.setup();
    navigation.params = { caseId: 'case-1' };
    const draftDetail = {
      id: 'case-1',
      title: 'Writing benchmark draft',
      profession: 'medicine',
      subTest: 'writing',
      type: 'writing',
      benchmarkScore: 3,
      status: 'draft',
      createdAt: '2026-04-01T08:00:00.000Z',
      benchmarkLabel: 'Benchmark A',
      difficulty: 'Moderate',
      artifacts: [{ kind: 'prompt', title: 'Prompt', content: 'Review the attached discharge summary.' }],
      benchmarkRubric: [{ criterion: 'purpose', benchmarkScore: 3, rationale: 'Purpose is fully achieved.' }],
      referenceNotes: ['Anchor to clinical purpose first.'],
      existingSubmission: {
        reviewerId: 'expert-1',
        reviewerName: 'Expert One',
        reviewerScore: 2,
        alignmentScore: 0,
        disagreementSummary: '',
        notes: 'Borderline purpose.',
        submittedScores: { purpose: 2 },
        submittedAt: '2026-04-01T09:00:00.000Z',
        isDraft: true,
        updatedAt: '2026-04-01T09:30:00.000Z',
      },
    };

    const completedDetail = {
      ...draftDetail,
      status: 'completed',
      existingSubmission: {
        ...draftDetail.existingSubmission,
        reviewerScore: 3,
        alignmentScore: 96.3,
        isDraft: false,
        disagreementSummary: 'Aligned with benchmark.',
        notes: 'Final aligned submission.',
      },
    };

    api.fetchCalibrationCaseDetail.mockResolvedValueOnce(draftDetail).mockResolvedValueOnce(completedDetail);
    api.saveCalibrationDraft.mockRejectedValue(
      new ApiError(
        409,
        'calibration_already_submitted',
        'This calibration case has already been submitted.',
        false,
      ),
    );

    renderPage(<CalibrationCaseWorkspacePage />);

    await user.click(await screen.findByRole('button', { name: /save draft/i }));

    expect(api.saveCalibrationDraft).toHaveBeenCalledWith('case-1', {
      scores: { purpose: 2 },
      notes: 'Borderline purpose.',
    });
    expect(api.fetchCalibrationCaseDetail).toHaveBeenCalledTimes(2);
    expect(await screen.findByText(/already been submitted/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /save draft/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /submit calibration/i })).not.toBeInTheDocument();
    expect(screen.getByText(/submitted scores and notes from your completed benchmark attempt/i)).toBeInTheDocument();
  });

  it('keeps completed calibration detail read-only', async () => {
    navigation.params = { caseId: 'case-1' };
    api.fetchCalibrationCaseDetail.mockResolvedValue({
      id: 'case-1',
      title: 'Writing benchmark completed',
      profession: 'medicine',
      subTest: 'writing',
      type: 'writing',
      benchmarkScore: 3,
      status: 'completed',
      createdAt: '2026-04-01T08:00:00.000Z',
      benchmarkLabel: 'Benchmark A',
      difficulty: 'Moderate',
      artifacts: [{ kind: 'prompt', title: 'Prompt', content: 'Review the attached discharge summary.' }],
      benchmarkRubric: [{ criterion: 'purpose', benchmarkScore: 3, rationale: 'Purpose is fully achieved.' }],
      referenceNotes: ['Anchor to clinical purpose first.'],
      existingSubmission: {
        reviewerId: 'expert-1',
        reviewerName: 'Expert One',
        reviewerScore: 3,
        alignmentScore: 100,
        disagreementSummary: 'Aligned with benchmark.',
        notes: 'Final notes.',
        submittedScores: { purpose: 3 },
        submittedAt: '2026-04-01T09:00:00.000Z',
        isDraft: false,
        updatedAt: '2026-04-01T09:00:00.000Z',
      },
    });

    renderPage(<CalibrationCaseWorkspacePage />);

    const scoreSelect = await screen.findByLabelText('Calibration score for purpose');
    expect(scoreSelect).toBeDisabled();
    expect(screen.getByLabelText(/calibration notes/i)).toBeDisabled();
    expect(screen.queryByRole('button', { name: /save draft/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /submit calibration/i })).not.toBeInTheDocument();
    expect(screen.getByText(/calibration already submitted/i)).toBeInTheDocument();
  });

  it('renders the learners directory inside the learner-style route surface', async () => {
    api.fetchExpertQueueFilterMetadata.mockResolvedValue({
      types: ['writing'],
      professions: ['medicine'],
      priorities: ['high'],
      statuses: ['queued'],
      confidenceBands: ['high'],
      assignmentStates: ['unassigned'],
    });
    api.fetchExpertLearners.mockResolvedValue({
      items: [
        {
          id: 'learner-1',
          name: 'Dr Farah Ali',
          profession: 'medicine',
          goalScore: '350',
          examDate: '2026-06-30T00:00:00.000Z',
          reviewsInScope: 2,
          subTests: ['writing'],
          lastReviewId: 'rev-1',
          lastReviewType: 'writing',
          lastReviewState: 'active',
          lastReviewAt: '2026-04-01T09:00:00.000Z',
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 12,
      lastUpdatedAt: '2026-04-01T09:15:00.000Z',
    });

    renderPage(<LearnersIndexPage />);

    expect(await screen.findByRole('heading', { name: /assigned learners/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /learner cards/i })).toBeInTheDocument();
    expect(screen.getByText('Dr Farah Ali')).toBeInTheDocument();
  });

  it('renders the learner detail page inside the learner-style route surface', async () => {
    navigation.params = { learnerId: 'learner-1' };
    api.fetchLearnerProfile.mockResolvedValue({
      id: 'learner-1',
      name: 'Dr Farah Ali',
      profession: 'medicine',
      goalScore: '350',
      examDate: '2026-06-30T00:00:00.000Z',
      attemptsCount: 3,
      joinedAt: '2025-12-01T00:00:00.000Z',
      totalReviews: 4,
      subTestScores: [{ subTest: 'writing', latestScore: 350, latestGrade: 'B', attempts: 2 }],
      priorReviews: [
        {
          id: 'prior-1',
          type: 'writing',
          reviewerName: 'Expert One',
          date: '2026-03-20T00:00:00.000Z',
          overallComment: 'Progress is improving.',
        },
      ],
      visibilityScope: 'assigned_reviews',
    });
    api.fetchExpertLearnerReviewContext.mockResolvedValue({
      id: 'learner-1',
      name: 'Dr Farah Ali',
      profession: 'medicine',
      goalScore: '350',
      examDate: '2026-06-30T00:00:00.000Z',
      reviewsInScope: 2,
      subTestScores: [],
      priorReviews: [],
    });

    renderPage(<AssignedLearnerPage />);

    expect(await screen.findByRole('heading', { name: /dr farah ali/i })).toBeInTheDocument();
    expect(screen.getByText(/privacy notice/i)).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /sub-test performance/i })).toBeInTheDocument();
  });

  it('renders the metrics page inside the learner-style route surface', async () => {
    api.fetchExpertMetrics.mockResolvedValue({
      metrics: {
        totalReviewsCompleted: 42,
        draftReviews: 6,
        averageSlaCompliance: 97,
        averageCalibrationAlignment: 91,
        reworkRate: 4,
        averageTurnaroundHours: 2.5,
      },
      completionData: [{ day: 'Mon', count: 3 }],
      generatedAt: '2026-04-01T09:30:00.000Z',
    });

    renderPage(<PerformanceMetricsPage />);

    expect(await screen.findByRole('heading', { name: /dashboard signals/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /operational metrics/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/date range filter/i)).toBeInTheDocument();
    expect(screen.getByTestId('responsive-chart')).toBeInTheDocument();
    expect(screen.getByTestId('bar-chart')).toBeInTheDocument();
  });

  it('renders the schedule page inside the learner-style route surface', async () => {
    api.fetchExpertSchedule.mockResolvedValue({
      timezone: 'Asia/Karachi',
      lastUpdatedAt: '2026-04-01T09:45:00.000Z',
      days: {
        monday: { active: true, start: '09:00', end: '17:00' },
        tuesday: { active: true, start: '09:00', end: '17:00' },
        wednesday: { active: true, start: '09:00', end: '17:00' },
        thursday: { active: true, start: '09:00', end: '17:00' },
        friday: { active: true, start: '09:00', end: '17:00' },
        saturday: { active: false, start: '09:00', end: '17:00' },
        sunday: { active: false, start: '09:00', end: '17:00' },
      },
    });
    api.fetchScheduleExceptions.mockResolvedValue({ exceptions: [] });
    api.saveExpertSchedule.mockResolvedValue(null);

    renderPage(<SchedulePage />);

    expect(await screen.findByRole('heading', { name: /schedule \/ availability/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /schedule controls/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /save schedule/i })).toBeInTheDocument();
  });
});
