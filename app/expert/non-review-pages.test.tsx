import type { ReactNode } from 'react';
import { screen } from '@testing-library/react';
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
  class ApiError extends Error {
    status: number; code: string; retryable: boolean; userMessage: string; fieldErrors: any[];
    constructor(s: number, c: string, m: string, r = false, f: any[] = []) { super(m); this.name='ApiError'; this.status=s; this.code=c; this.retryable=r; this.userMessage=m; this.fieldErrors=f; }
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
        status: 'completed',
        createdAt: '2026-04-01T08:00:00.000Z',
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
      benchmarkScore: 5,
      status: 'pending',
      createdAt: '2026-04-01T08:00:00.000Z',
      benchmarkLabel: 'Benchmark A',
      difficulty: 'Moderate',
      artifacts: [{ kind: 'prompt', title: 'Prompt', content: 'Review the attached discharge summary.' }],
      benchmarkRubric: [{ criterion: 'purpose', benchmarkScore: 5, rationale: 'Purpose is fully achieved.' }],
      referenceNotes: ['Anchor to clinical purpose first.'],
      existingSubmission: null,
    });

    renderPage(<CalibrationCaseWorkspacePage />);

    expect(await screen.findByRole('heading', { name: /writing benchmark 1/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /back to calibration/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /benchmark evidence/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /reference scoring/i })).toBeInTheDocument();
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
