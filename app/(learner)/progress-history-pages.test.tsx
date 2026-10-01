// Progress & history module pages that have no colocated test: each renders with real-shaped API
// data and keeps one h1, its key content, and its empty / error states.
import { screen, within } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';

const { request, fetchStudyPlan, fetchStudyPlanDrift, fetchSubmissionDetail, fetchSubmissionComparison } = vi.hoisted(() => ({
  request: vi.fn(),
  fetchStudyPlan: vi.fn(),
  fetchStudyPlanDrift: vi.fn(),
  fetchSubmissionDetail: vi.fn(),
  fetchSubmissionComparison: vi.fn(),
}));
const learnerData = vi.hoisted(() => ({
  getScoreEquivalencesData: vi.fn(),
  getCertificatesData: vi.fn(),
  getStudyCommitmentData: vi.fn(),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: vi.fn() } }));
vi.mock('@/lib/api', () => ({
  apiClient: { request },
  fetchStudyPlan,
  fetchStudyPlanDrift,
  regenerateStudyPlan: vi.fn(),
  setStudyCommitment: vi.fn(),
  fetchSubmissionDetail,
  fetchSubmissionComparison,
  fetchAuthorizedObjectUrl: vi.fn(),
}));
vi.mock('@/lib/learner-data', () => learnerData);

import Comparative from './progress/comparative/page';
import Certificate from './achievements/certificate/page';
import Certificates from './achievements/certificates/page';
import StudyCommitment from './goals/study-commitment/page';
import Calendar from './study-plan/calendar/page';
import Drift from './study-plan/drift/page';
import LearningPaths from './learning-paths/page';
import NextActions from './next-actions/page';
import Predictions from './predictions/page';
import Remediation from './remediation/page';
import ScoreCalculator from './score-calculator/page';
import DashboardScoreCalculator from './dashboard/score-calculator/page';
import SubmissionDetail from './submissions/[id]/page';
import SubmissionCompare from './submissions/compare/page';

const today = new Date().toISOString().slice(0, 10);

function oneH1() {
  expect(document.querySelectorAll('h1')).toHaveLength(1);
}

describe('progress and history module pages', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    request.mockImplementation(async (url: string) => {
      if (url.includes('comparative')) return { generatedAt: today, subtests: [{ subtestCode: 'writing', yourScore: 345.5, percentile: 80, cohortAverage: 300, cohortMedian: 300, cohortSize: 3, targetScore: 350, gapToTarget: 4.5, tier: 'top25' }] };
      if (url.includes('certificates')) return { certificates: [{ id: 'c1', type: 'mock_exam_passed', title: 'Mock passed', description: 'Well done', downloadUrl: 'https://x/c1.pdf', issuedAt: today }] };
      if (url.includes('learning-path')) return { professionCode: 'medicine', professionLabel: 'Medicine', examTypeCode: 'oet', overallProgress: 12.5, totalContent: 2, nextRecommended: [{ id: 'r1', title: 'Rec one', subtestCode: 'reading', difficulty: 'easy' }], subtestPaths: [{ subtestCode: 'reading', totalItems: 2, completedItems: 1, progressPercent: 50, items: [{ id: 'i1', title: 'Item one', difficulty: 'easy', durationMinutes: 20, completed: false, scenarioType: null }, { id: 'i2', title: 'Item two', difficulty: 'hard', durationMinutes: 30, completed: true, scenarioType: null }] }] };
      if (url.includes('next-actions')) return { generatedAt: today, actions: [{ type: 'overdue_task', priority: 'high', title: 'Do the overdue task', subtitle: 'Due today', actionUrl: '/study-plan', subtestCode: 'writing' }] };
      if (url.includes('/v1/predictions')) return [{ id: 'p1', examTypeCode: 'oet', subtestCode: 'writing', predictedScoreLow: 300, predictedScoreHigh: 360, predictedScoreMid: 330, confidenceLevel: 'good', factorsJson: '{"evaluationCount":3,"recentAverage":331,"trendDirection":"improving","trend":5}', evaluationCount: 3, computedAt: today }];
      if (url.includes('remediation')) return { evaluationsAnalyzed: 4, weakAreas: [{ subtestCode: 'writing', criterionCode: 'purpose', averageScore: 2.5, evaluationCount: 2, trend: 'insufficient_data' }], availableResources: [], recommendations: [] };
      if (url.includes('score-equivalences')) return { equivalences: [{ oetGrade: 'B', oetScoreMin: 350, oetScoreMax: 390, ielts: 7, pte: 65, cefr: 'B2' }], commonRequirements: [{ country: 'UK', body: 'NMC', oetMinGrade: 'B', oetMinScore: 350, ieltsMin: 7 }] };
      throw new Error(`unmocked ${url}`);
    });
  });

  it('comparative analytics', async () => {
    renderWithRouter(<Comparative />);
    expect(await screen.findByText('Top 25%')).toBeInTheDocument();
    expect(screen.getByText('345.5')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: /writing percentile 80%/i })).toBeInTheDocument();
    oneH1();
  });

  it('comparative analytics shows an error state with retry when the request fails', async () => {
    request.mockRejectedValueOnce(new Error('down'));
    renderWithRouter(<Comparative />);
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry this page' })).toBeInTheDocument();
  });

  it('certificate (single route)', async () => {
    renderWithRouter(<Certificate />);
    expect(await screen.findByText('Mock passed')).toBeInTheDocument();
    const link = screen.getByRole('link', { name: /download/i });
    expect(link).toHaveAttribute('href', 'https://x/c1.pdf');
    expect(link.querySelector('button')).toBeNull();
    oneH1();
  });

  it('certificates (list route)', async () => {
    learnerData.getCertificatesData.mockResolvedValue([{ id: 'c1', userId: 'u', certificateType: 'mock_exam', title: 'Mock cert', description: 'd', downloadUrl: 'https://x/c.pdf', metadataJson: null, issuedAt: today }]);
    renderWithRouter(<Certificates />);
    expect(await screen.findByText('Mock cert')).toBeInTheDocument();
    expect(screen.getByText('Mock Exam')).toBeInTheDocument();
    oneH1();
  });

  it('study commitment', async () => {
    learnerData.getStudyCommitmentData.mockResolvedValue({ dailyMinutes: 45, freezeProtections: 3, freezeProtectionsUsed: 1, isActive: true });
    renderWithRouter(<StudyCommitment />);
    expect(await screen.findByText('45m')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '45 min' })).toHaveAttribute('aria-pressed', 'true');
    oneH1();
  });

  it('study calendar renders tasks and the Week/Month tabs', async () => {
    fetchStudyPlan.mockResolvedValue([{ id: 't1', title: 'Calendar task', subTest: 'Writing', duration: '30m', dueDate: today, status: 'not_started', section: 'today', route: '/writing' }]);
    renderWithRouter(<Calendar />);
    expect(await screen.findByText('Calendar task')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Week' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tabpanel')).toBeInTheDocument();
    oneH1();
  });

  it('study calendar shows the empty state with no plan', async () => {
    fetchStudyPlan.mockResolvedValue([]);
    renderWithRouter(<Calendar />);
    expect(await screen.findByText('No study plan yet')).toBeInTheDocument();
  });

  it('plan health', async () => {
    fetchStudyPlanDrift.mockResolvedValue({ hasPlan: true, drift: { level: 'moderate', overdueItems: 2, oldestOverdueDays: 9, completionRate: 40, expectedCompleted: 5, actualCompleted: 2, totalItems: 10, shouldRegenerate: true, recommendation: 'Catch up.' }, subtestDrift: [{ subtestCode: 'writing', total: 4, completed: 1, overdue: 1, completionRate: 25 }], overdueItems: [{ id: 'o1', title: 'Overdue one', subtestCode: 'writing', dueDate: today, daysOverdue: 3 }] });
    renderWithRouter(<Drift />);
    expect(await screen.findByRole('heading', { level: 2, name: 'Moderate Drift Detected' })).toBeInTheDocument();
    expect(screen.getByText('Overdue one')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /recover my plan/i })).toBeInTheDocument();
    oneH1();
  });

  it('plan health shows an error state when the request fails', async () => {
    fetchStudyPlanDrift.mockRejectedValue(new Error('down'));
    renderWithRouter(<Drift />);
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByText('No study plan found')).not.toBeInTheDocument();
  });

  it('learning paths', async () => {
    renderWithRouter(<LearningPaths />);
    expect(await screen.findByText('Rec one')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /rec one/i })).toHaveAttribute('href', '/reading/practice');
    expect(screen.getByRole('link', { name: 'Start' })).toHaveAttribute('href', '/reading/practice');
    oneH1();
  });

  it('next actions links through the router', async () => {
    renderWithRouter(<NextActions />);
    expect(await screen.findByText('Do the overdue task')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Go: Do the overdue task' })).toHaveAttribute('href', '/study-plan');
    expect(screen.getByText('high priority')).toBeInTheDocument();
    oneH1();
  });

  it('predictions', async () => {
    renderWithRouter(<Predictions />);
    expect(await screen.findByText('330')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Generate Prediction' })).toHaveLength(4);
    oneH1();
  });

  it('remediation hides the trend badge without trend data', async () => {
    renderWithRouter(<Remediation />);
    const row = (await screen.findByText(/writing · purpose/i)).closest('div') as HTMLElement;
    expect(row).toBeInTheDocument();
    expect(screen.queryByText('Stable')).not.toBeInTheDocument();
    expect(screen.getByText('Based on 4 recent evaluations')).toBeInTheDocument();
    oneH1();
  });

  it('remediation says when no weak areas were found', async () => {
    request.mockResolvedValueOnce({ evaluationsAnalyzed: 3, weakAreas: [], availableResources: [], recommendations: [] });
    renderWithRouter(<Remediation />);
    expect(await screen.findByText('No weak areas identified')).toBeInTheDocument();
  });

  it('score calculator', async () => {
    learnerData.getScoreEquivalencesData.mockResolvedValue({ equivalences: [{ oetGrade: 'B', oetScore: '350-390', ielts: '7.0', pte: '65', cefr: 'B2' }], institutions: [{ institution: 'NMC', country: 'UK', profession: 'Nursing', minimumOetGrade: 'B' }] });
    renderWithRouter(<ScoreCalculator />);
    const grade = await screen.findByRole('button', { name: 'B' });
    expect(grade).toHaveAttribute('aria-pressed', 'false');
    grade.click();
    expect(await screen.findByRole('button', { name: 'B', pressed: true })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /target this score/i })).toHaveAttribute('href', '/goals?targetGrade=B');
    oneH1();
  });

  it('dashboard score calculator', async () => {
    renderWithRouter(<DashboardScoreCalculator />);
    expect(await screen.findByText('NMC')).toBeInTheDocument();
    expect(screen.getByText('Meets Requirement')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'All' })).toHaveAttribute('aria-pressed', 'true');
    oneH1();
  });

  it('submission evidence detail', async () => {
    fetchSubmissionDetail.mockResolvedValue({
      submission: { id: 's1', subTest: 'Writing', canRequestReview: false, actions: { compareRoute: null }, evaluationId: null },
      evidenceSummary: { title: 'Evidence title', scoreLabel: '350/500', stateLabel: 'Completed', reviewLabel: 'Not requested' },
      strengths: ['Clear purpose'],
      issues: [],
      criteria: [{ name: 'Purpose', score: 3, maxScore: 3, grade: 'A', explanation: 'Clear.', anchoredComments: [], omissions: [], unnecessaryDetails: [], revisionSuggestions: [], strengths: [], issues: [] }],
      questionReview: [{ id: 'q1', number: 1, text: 'Q one', learnerAnswer: 'a', correctAnswer: 'b', isCorrect: false, explanation: 'because' }],
    });
    renderWithRouter(<SubmissionDetail />, { params: { id: 's1' } });
    expect(await screen.findByRole('heading', { level: 1, name: 'Evidence title' })).toBeInTheDocument();
    expect(screen.getByText('3/3')).toBeInTheDocument();
    const question = screen.getByText('Q one').closest('div.rounded-2xl') as HTMLElement;
    expect(within(question).getByText('Review')).toBeInTheDocument();
    oneH1();
  });

  it('submission comparison', async () => {
    fetchSubmissionComparison.mockResolvedValue({ canCompare: true, left: { attemptId: 'a1', subtest: 'writing', scoreRange: '300' }, right: { attemptId: 'a2', subtest: 'writing', scoreRange: '340' }, summary: 'Improved by 40.' });
    renderWithRouter(<SubmissionCompare />);
    expect(await screen.findByText('Improved by 40.')).toBeInTheDocument();
    oneH1();
  });
});
