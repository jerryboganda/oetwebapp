import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Writing Rule Enforcement Addendum Rev5, §13 — post-submission "What's next?"
 * controls on the AI writing results page.
 *
 * Reopening this page IS the free "Revise / Review the Letter" experience for
 * a completed attempt: it must render the exact saved letter, score, and
 * feedback purely from GET reads, with zero mutation/grading calls and zero
 * "Request tutor review" affordance (removed per this same addendum). A
 * genuinely new attempt only happens through "Practice this again", which
 * must route to the practice-session entitlement gate, not reuse this
 * attempt's id.
 */

const {
  getWritingSubmission,
  getWritingSubmissionGrade,
  getWritingAssessmentV11,
  getTutorReview,
  getWritingAnswerSheet,
  getWritingSubmissionCaseNotes,
  disputeWritingCanonViolation,
  publishToShowcase,
  createWritingSubmission,
  reviseWritingSubmission,
  listFreeSamples,
} = vi.hoisted(() => ({
  getWritingSubmission: vi.fn(),
  getWritingSubmissionGrade: vi.fn(),
  getWritingAssessmentV11: vi.fn(),
  getTutorReview: vi.fn(),
  getWritingAnswerSheet: vi.fn(),
  getWritingSubmissionCaseNotes: vi.fn(),
  disputeWritingCanonViolation: vi.fn(),
  publishToShowcase: vi.fn(),
  // Not imported by this page today — kept as spies so a future regression
  // that wires either into the free review path is caught here too.
  createWritingSubmission: vi.fn(),
  reviseWritingSubmission: vi.fn(),
  listFreeSamples: vi.fn(),
}));

vi.mock('@/lib/api/free-samples', () => ({ listFreeSamples }));

vi.mock('@/lib/writing/api', () => ({
  getWritingSubmission,
  getWritingSubmissionGrade,
  getWritingAssessmentV11,
  getTutorReview,
  getWritingAnswerSheet,
  getWritingSubmissionCaseNotes,
  disputeWritingCanonViolation,
  publishToShowcase,
  createWritingSubmission,
  reviseWritingSubmission,
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ id: 'sub-1' }),
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

// TutorVoiceNotePlayer (rendered unconditionally by the results page) reads
// these two directly from '@/lib/api'. Stub to a no-op note so it renders
// nothing, without touching the rest of that module.
vi.mock('@/lib/api', () => ({
  getWritingSubmissionVoiceNote: vi.fn().mockResolvedValue(null),
  fetchAuthorizedObjectUrl: vi.fn(),
  // The pass celebration's profile query (disabled here: no signed-in user).
  fetchUserProfile: vi.fn(),
}));

import WritingSubmissionResultsPage from './page';
import enWriting from '@/messages/en/writing.json';
import arWriting from '@/messages/ar/writing.json';

const SUBMISSION = {
  id: 'sub-1',
  userId: 'user-1',
  scenarioId: 'scenario-1',
  mode: 'practice',
  letterContent: 'Dear Dr Smith,\n\nI am writing to refer this patient for further review.\n\nYours sincerely,\nCandidate',
  contentHash: 'hash-1',
  wordCount: 190,
  timeSpentSeconds: 2000,
  startedAt: '2026-09-01T00:00:00Z',
  submittedAt: '2026-09-01T00:40:00Z',
  isRevision: false,
  originalSubmissionId: null,
  status: 'graded',
  gradingTier: 'standard',
  inputSource: 'editor',
};

const GRADE = {
  id: 'grade-1',
  submissionId: 'sub-1',
  c1Purpose: 3,
  c2Content: 6,
  c3Conciseness: 6,
  c4Genre: 6,
  c5Organisation: 6,
  c6Language: 6,
  rawTotal: 33,
  estimatedBand: 6,
  bandLabel: 'B',
  perCriterion: {
    c1: { score: 3, feedback: 'Clear purpose.', exemplarFix: null, citedRuleIds: [] },
    c2: { score: 6, feedback: 'Good content.', exemplarFix: null, citedRuleIds: [] },
    c3: { score: 6, feedback: 'Concise.', exemplarFix: null, citedRuleIds: [] },
    c4: { score: 6, feedback: 'Good genre.', exemplarFix: null, citedRuleIds: [] },
    c5: { score: 6, feedback: 'Organised.', exemplarFix: null, citedRuleIds: [] },
    c6: { score: 6, feedback: 'Accurate.', exemplarFix: null, citedRuleIds: [] },
  },
  topThreePriorities: ['Tighten the opening.'],
  confidenceFlag: 'high',
  modelUsed: 'v1',
  canonVersion: 'v1',
  canonViolations: [],
  revisionInvite: { shouldOffer: false, reason: '' },
  gradedAt: '2026-09-01T00:41:00Z',
};


// The pass celebration reads the learner profile through React Query.
function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <WritingSubmissionResultsPage />
    </QueryClientProvider>,
  );
}

describe('Writing results page — free review vs. new attempt (Addendum Rev5 §13)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    listFreeSamples.mockResolvedValue([]);
    getWritingSubmission.mockResolvedValue(SUBMISSION);
    getWritingSubmissionGrade.mockResolvedValue(GRADE);
    getWritingAssessmentV11.mockResolvedValue(null);
    getTutorReview.mockResolvedValue(null);
    getWritingAnswerSheet.mockResolvedValue({ answerSheetPdfDownloadPath: null });
    getWritingSubmissionCaseNotes.mockResolvedValue(null);
  });

  it('reopens the saved report via GET-only reads: shows the exact original letter, score, and criteria, with zero grading/credit calls', async () => {
    renderPage();

    // The exact stored letter is now part of the same report (was previously
    // missing from this page entirely).
    expect(await screen.findByText(/I am writing to refer this patient/)).toBeInTheDocument();
    expect(screen.getByText(/no credit used/i)).toBeInTheDocument();

    // Saved score/grade + all six criteria still render.
    expect(screen.getByText('writing.submissions.results.criteria.perCriterion')).toBeInTheDocument();
    expect(screen.getByText('Clear purpose.')).toBeInTheDocument();
    expect(screen.getByText('Tighten the opening.')).toBeInTheDocument();

    // Only reads fired — no mutation, grading, or credit-consuming call.
    expect(getWritingSubmission).toHaveBeenCalledWith('sub-1');
    expect(disputeWritingCanonViolation).not.toHaveBeenCalled();
    expect(publishToShowcase).not.toHaveBeenCalled();
    expect(createWritingSubmission).not.toHaveBeenCalled();
    expect(reviseWritingSubmission).not.toHaveBeenCalled();
  });

  // Launch handoff UI-1 (2 Oct 2026): "Appeal score" is removed completely —
  // no button, no link to the deleted /appeal route, no appeal copy.
  it('offers neither "Request tutor review" nor any appeal control', async () => {
    const { container } = renderPage();
    await screen.findByText(/I am writing to refer this patient/);

    expect(screen.queryByText(/request tutor review/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /appeal/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /appeal/i })).not.toBeInTheDocument();
    expect(container.querySelector('a[href*="/appeal"]')).toBeNull();
    expect(container.textContent).not.toMatch(/appeal/i);
  });

  it('"Practice this again" starts a distinct new attempt via the practice-session entitlement gate, not this attempt\'s id', async () => {
    renderPage();
    await screen.findByText(/I am writing to refer this patient/);

    const practiceAgainLink = screen.getByRole('link', { name: /practiceAgain/i });
    expect(practiceAgainLink).toHaveAttribute('href', '/writing/practice/session/scenario-1');
  });
});

describe('Writing results page — free sample revise & resubmit (retry addendum, 23 Sep 2026)', () => {
  const FREE_ROW = {
    professionId: 'medicine',
    contentId: 'scenario-1',
    state: 'retry_available',
    route: '/writing/submissions/sub-1/revise',
    limit: 2,
    successfulCount: 1,
    remaining: 1,
    lastResultRoute: '/writing/submissions/sub-1/results',
    lastSubmissionId: 'sub-1',
  };

  beforeEach(() => {
    vi.clearAllMocks();
    getWritingSubmission.mockResolvedValue(SUBMISSION);
    getWritingSubmissionGrade.mockResolvedValue(GRADE);
    getWritingAssessmentV11.mockResolvedValue(null);
    getTutorReview.mockResolvedValue(null);
    getWritingAnswerSheet.mockResolvedValue({ answerSheetPdfDownloadPath: null });
    getWritingSubmissionCaseNotes.mockResolvedValue(null);
  });

  it("retry_available on this letter: primary \"Revise & Resubmit\" CTA links to this submission's revise page", async () => {
    listFreeSamples.mockResolvedValue([FREE_ROW]);
    renderPage();

    const cta = await screen.findByTestId('free-sample-revise-cta');
    expect(cta).toHaveAttribute('href', '/writing/submissions/sub-1/revise');
    expect(cta).toHaveTextContent('freeSample.writing.retryCta');
    expect(listFreeSamples).toHaveBeenCalledWith('writing');
    // Linking only — the revise itself happens on the revise page.
    expect(reviseWritingSubmission).not.toHaveBeenCalled();
    expect(screen.queryByTestId('free-sample-completed')).not.toBeInTheDocument();
  });

  it('completed on this letter: shows "Free sample completed" and no revise CTA', async () => {
    listFreeSamples.mockResolvedValue([{ ...FREE_ROW, state: 'completed', successfulCount: 2, remaining: 0 }]);
    renderPage();

    expect(await screen.findByTestId('free-sample-completed')).toHaveTextContent('freeSample.completed');
    expect(screen.queryByTestId('free-sample-revise-cta')).not.toBeInTheDocument();
  });

  it('a free row for a different scenario never adds the free CTA to this letter', async () => {
    listFreeSamples.mockResolvedValue([{ ...FREE_ROW, contentId: 'other-scenario' }]);
    renderPage();

    await screen.findByText(/I am writing to refer this patient/);
    await waitFor(() => expect(listFreeSamples).toHaveBeenCalled());
    expect(screen.queryByTestId('free-sample-revise-cta')).not.toBeInTheDocument();
    expect(screen.queryByTestId('free-sample-completed')).not.toBeInTheDocument();
  });
});

// Exactly as stored: address block, blank lines, and the salutation / Re: line
// on their own lines (Writing Rule Enforcement Addendum Rev8 §12.3, §19.2).
const MODEL_ANSWER_TEXT =
  'Dr Anna Still\nCardiology Department\nCity Hospital\n\n11 September 2026\n\n'
  + 'Dear Dr Still,\nRe: Mr David Taylor, aged 55\n\n'
  + 'I am writing to refer Mr Taylor, who presented with exertional chest pain, for your assessment.\n\n'
  + "On today's visit, his ECG showed ST depression in the lateral leads.\n\n"
  + 'Yours sincerely,\nDoctor';

const v11Criterion = (criterionCode: string, score: number, maximumScore: number) => ({
  criterionCode,
  score,
  maximumScore,
  strengthObservation: `${criterionCode} strength.`,
  limitationObservation: `${criterionCode} limitation.`,
  evidence: [],
  improvementAction: `${criterionCode} action.`,
});

const ASSESSMENT_V11 = {
  id: 'report-1',
  submissionId: 'sub-1',
  status: 'CandidateReady',
  profession: 'medicine',
  letterType: 'routine_referral',
  rulePackVersion: 'rules-1',
  modelVersion: 'model-1',
  calibrationSetVersion: 'calibration-1',
  estimatedPracticeScore: 382,
  scoreLabel: 'AI Estimated Practice Score — not an official OET result',
  gradeBand: 'Grade B',
  scoreRange: null,
  confidenceLabel: 'moderate',
  confidenceRange: null,
  candidateNumericScoreEnabled: true,
  candidateReportVisible: true,
  blockingCodes: [],
  topPriorities: [],
  strengths: [],
  studyPlan: [],
  criteria: [
    v11Criterion('purpose', 3, 3),
    v11Criterion('content', 6, 7),
    v11Criterion('conciseness_clarity', 6, 7),
    v11Criterion('genre_style', 6, 7),
    v11Criterion('organisation_layout', 6, 7),
    v11Criterion('language', 6, 7),
  ],
  errors: [],
  facts: [],
  modelAnswer: {
    status: 'Ready',
    modelAnswerText: MODEL_ANSWER_TEXT,
    correctedCandidateLetter: null,
    whyThisWorks: [],
    groundedFactReferences: [],
    isCandidateVisible: true,
  },
};

describe('Writing results page — candidate-visible v1.1 report (Addendum Rev8 §12.3/§12.4/§19.2/§19.4)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    listFreeSamples.mockResolvedValue([]);
    getWritingSubmission.mockResolvedValue(SUBMISSION);
    getWritingSubmissionGrade.mockResolvedValue(GRADE);
    getWritingAssessmentV11.mockResolvedValue(ASSESSMENT_V11);
    getTutorReview.mockResolvedValue(null);
    getWritingAnswerSheet.mockResolvedValue({ answerSheetPdfDownloadPath: null });
    getWritingSubmissionCaseNotes.mockResolvedValue(null);
  });

  it('renders the grounded model answer with every line break preserved, the /500 AI score + grade band as the headline, and all six criteria', async () => {
    renderPage();

    // Model Answer text is rendered exactly as stored — no collapsing, trimming, or splitting.
    const modelAnswer = await screen.findByTestId('grounded-model-answer');
    expect(modelAnswer.textContent).toContain('\nRe: Mr David Taylor, aged 55\n\nI am writing');
    expect(modelAnswer.textContent).toBe(MODEL_ANSWER_TEXT);
    expect(modelAnswer).toHaveClass('whitespace-pre-wrap');
    expect(screen.getByTestId('grounded-model-answer-card')).toContainElement(modelAnswer);

    // The AI Estimated Practice Score /500 + grade band is the headline even
    // though the older /grade data also loaded; raw total stays secondary.
    expect(screen.getByTestId('ai-estimated-score')).toHaveTextContent('380/500');
    expect(screen.getByTestId('ai-grade-band')).toHaveTextContent('Grade B');
    expect(screen.getByText('33/38')).toBeInTheDocument();
    expect(screen.queryByText('writing.submissions.results.estimatedBand')).not.toBeInTheDocument();

    // All six criteria render with score/max in ONE list (the v1.1 duplicate list is gone).
    expect(screen.queryByTestId('assessment-criteria-list')).not.toBeInTheDocument();
    const criteria = within(screen.getByTestId('criteria-list')).getAllByRole('listitem');
    expect(criteria).toHaveLength(6);
    const expected: Array<[string, string]> = [
      ['C1 Purpose', '3/3'],
      ['C2 Content', '6/7'],
      ['C3 Conciseness & Clarity', '6/7'],
      ['C4 Genre & Style', '6/7'],
      ['C5 Organisation & Layout', '6/7'],
      ['C6 Language Accuracy', '6/7'],
    ];
    expected.forEach(([name, score], index) => {
      expect(within(criteria[index]).getByText(name)).toBeInTheDocument();
      expect(within(criteria[index]).getByText(score)).toBeInTheDocument();
    });
    // Boilerplate strength/limitation lines are dropped; the next step stays.
    expect(screen.queryByText('purpose strength.')).not.toBeInTheDocument();
    expect(screen.queryByText('purpose limitation.')).not.toBeInTheDocument();
    expect(within(criteria[0]).getByText('purpose action.')).toBeInTheDocument();
    // A visible report with no errors says so honestly.
    expect(screen.getByText('writing.submissions.results.corrections.empty')).toBeInTheDocument();
  });

  it("highlights the grader's per-criterion quote of the candidate's own wording", async () => {
    getWritingSubmissionGrade.mockResolvedValue({
      ...GRADE,
      perCriterion: {
        ...GRADE.perCriterion,
        c6: { ...GRADE.perCriterion.c6, feedback: 'Subject-verb agreement error.', quote: 'the patient have chest pain' },
      },
    });
    renderPage();

    const quote = await screen.findByText('“the patient have chest pain”');
    expect(quote.tagName).toBe('MARK');
    expect(screen.getByText(/Subject-verb agreement error\./)).toBeInTheDocument();
  });
});

// Launch handoff UI-3 (2 Oct 2026): a simplified report in a fixed order with
// no depth lost — every correction stays reachable behind "View all corrections".
describe('Writing results page — simplified report order (launch handoff UI-3)', () => {
  const error = (n: number, criterion: string, severity: string) => ({
    id: `err-${n}`,
    location: null,
    candidateWording: `wording ${n}`,
    correction: `correction ${n}`,
    category: 'language',
    ruleSource: `R12.${n}`,
    whyItMatters: `why ${n}`,
    severity,
    confidence: 'high',
    primaryCriterionCode: criterion,
    secondaryCriterionCodes: [],
    startOffset: n,
    endOffset: n + 4,
  });
  // Server order: severity-first.
  const ERRORS = [
    error(1, 'content', 'critical'),
    error(2, 'language', 'major'),
    error(3, 'content', 'major'),
    error(4, 'language', 'minor'),
    error(5, 'purpose', 'minor'),
    error(6, 'language', 'minor'),
    error(7, 'content', 'minor'),
  ];
  const REPORT = {
    ...ASSESSMENT_V11,
    errors: ERRORS,
    topPriorities: [
      'AI.content: Include the discharge plan.',
      'R05.2: Use passive voice for medications.',
      'AI:OWN-W-030: Avoid contractions.',
      'no_contractions: A fourth priority is never shown.',
    ],
  };
  const CANON = {
    id: 'cv-1',
    submissionId: 'sub-1',
    ruleId: 'R06.1',
    ruleText: 'Use the full name in the Re: line.',
    severity: 'medium',
    snippet: 'Re: patient',
    lineNumber: 3,
    charStart: 0,
    charEnd: 11,
    suggestedFix: null,
    disputed: false,
    disputeResolution: null,
  };

  beforeEach(() => {
    vi.clearAllMocks();
    listFreeSamples.mockResolvedValue([]);
    getWritingSubmission.mockResolvedValue(SUBMISSION);
    getWritingSubmissionGrade.mockResolvedValue({ ...GRADE, canonViolations: [CANON] });
    getWritingAssessmentV11.mockResolvedValue(REPORT);
    getTutorReview.mockResolvedValue({ id: 'r-1', freeTextFeedback: 'Tutor note.' });
    getWritingAnswerSheet.mockResolvedValue({ answerSheetPdfDownloadPath: null });
    getWritingSubmissionCaseNotes.mockResolvedValue(null);
  });

  it('renders score → priorities → model answer → criteria → corrections → reference → next actions', async () => {
    renderPage();
    await screen.findByTestId('grounded-model-answer');

    expect(screen.getAllByTestId('result-section').map((s) => s.getAttribute('data-section'))).toEqual([
      'score', 'priorities', 'model-answer', 'criteria', 'corrections', 'reference', 'next-actions',
    ]);
    // The model answer is visible by default, never tucked in a collapsed block.
    expect(screen.getByTestId('grounded-model-answer').closest('details')).toBeNull();
    // Reference material is collapsed.
    const letter = screen.getByText(/I am writing to refer this patient/);
    expect(letter.closest('details')).not.toHaveAttribute('open');
    expect(screen.getByText('Tutor note.').closest('details')).not.toHaveAttribute('open');
  });

  it('shows the v1.1 report\'s first three priorities without their internal rule labels', async () => {
    renderPage();
    const section = await screen.findByRole('heading', { name: 'writing.submissions.results.priorities.heading' });
    const items = within(section.closest('section')!).getAllByRole('listitem');

    expect(items.map((li) => li.textContent)).toEqual([
      '#1Include the discharge plan.',
      '#2Use passive voice for medications.',
      '#3Avoid contractions.',
    ]);
    expect(screen.queryByText(/fourth priority/)).not.toBeInTheDocument();
  });

  it('falls back to the grade\'s priorities and leaves a plain lead-in intact', async () => {
    getWritingAssessmentV11.mockResolvedValue(null);
    getWritingSubmissionGrade.mockResolvedValue({ ...GRADE, topThreePriorities: ['Purpose: state it first.', 'AI: Tighten the close.'] });
    renderPage();

    expect(await screen.findByText('Purpose: state it first.')).toBeInTheDocument();
    expect(screen.getByText('Tighten the close.')).toBeInTheDocument();
  });

  it('summarises each criterion: correction count, most severe evidence and next step', async () => {
    renderPage();
    const items = within(await screen.findByTestId('criteria-list')).getAllByRole('listitem');

    // C2 Content: errors 1, 3, 7 — error 1 (critical) is the top evidence.
    expect(within(items[1]).getByText('writing.submissions.results.criteria.findings')).toBeInTheDocument();
    expect(within(items[1]).getByText('“wording 1”')).toBeInTheDocument();
    expect(within(items[1]).getByText('correction 1')).toBeInTheDocument();
    expect(within(items[1]).getByText('content action.')).toBeInTheDocument();
    // C3 has no findings: no evidence line.
    expect(within(items[2]).queryByText(/wording/)).not.toBeInTheDocument();
  });

  it('previews the five most severe corrections and "View all corrections" shows every one', async () => {
    const user = userEvent.setup();
    renderPage();

    const preview = await screen.findByTestId('corrections-preview');
    expect(within(preview).getAllByRole('listitem')).toHaveLength(5);
    expect(within(preview).getByText('critical')).toHaveClass('text-danger-strong');

    const toggle = screen.getByTestId('corrections-view-all');
    expect(toggle).toHaveTextContent('writing.submissions.results.corrections.viewAll');
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await user.click(toggle);

    const full = screen.getByTestId('corrections-full-list');
    expect(within(full).getAllByRole('listitem')).toHaveLength(ERRORS.length);
    expect(screen.queryByTestId('corrections-preview')).not.toBeInTheDocument();
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(toggle).toHaveTextContent('writing.submissions.results.corrections.showFewer');

    await user.click(toggle);
    expect(within(screen.getByTestId('corrections-preview')).getAllByRole('listitem')).toHaveLength(5);
  });

  it('lists five or fewer corrections in full with no toggle', async () => {
    getWritingAssessmentV11.mockResolvedValue({ ...REPORT, errors: ERRORS.slice(0, 3) });
    renderPage();

    const full = await screen.findByTestId('corrections-full-list');
    expect(within(full).getAllByRole('listitem')).toHaveLength(3);
    expect(screen.queryByTestId('corrections-view-all')).not.toBeInTheDocument();
  });

  it('keeps the legacy rule checks, still disputable, as a collapsed group inside corrections', async () => {
    renderPage();
    await screen.findByTestId('corrections-preview');

    const corrections = screen.getAllByTestId('result-section').find((s) => s.getAttribute('data-section') === 'corrections')!;
    const ruleChecks = within(corrections).getByText('writing.submissions.results.canon.heading').closest('details');
    expect(ruleChecks).not.toBeNull();
    expect(ruleChecks).not.toHaveAttribute('open');
    expect(within(ruleChecks!).getByRole('button', { name: /mark this detection as incorrect/i, hidden: true })).toBeInTheDocument();
  });
});

// Spec review (2 Oct 2026): "What's next?" offers a paid Revise & Resubmit
// whenever the grade invites a revision of this graded, non-mock letter.
describe('Writing results page — paid Revise & Resubmit', () => {
  const INVITED = { ...GRADE, revisionInvite: { shouldOffer: true, reason: 'Significant gains likely on a focused revision.' } };

  beforeEach(() => {
    vi.clearAllMocks();
    listFreeSamples.mockResolvedValue([]);
    getWritingSubmission.mockResolvedValue(SUBMISSION);
    getWritingSubmissionGrade.mockResolvedValue(INVITED);
    getWritingAssessmentV11.mockResolvedValue(null);
    getTutorReview.mockResolvedValue(null);
    getWritingAnswerSheet.mockResolvedValue({ answerSheetPdfDownloadPath: null });
    getWritingSubmissionCaseNotes.mockResolvedValue(null);
  });

  it('links to this letter\'s revise page and keeps "Practice this again"', async () => {
    renderPage();

    const cta = await screen.findByTestId('revise-and-resubmit');
    expect(cta).toHaveAttribute('href', '/writing/submissions/sub-1/revise');
    expect(cta).toHaveTextContent('writing.submissions.results.actions.reviseResubmit');
    expect(screen.getByRole('link', { name: /practiceAgain/i })).toHaveAttribute('href', '/writing/practice/session/scenario-1');
    expect(reviseWritingSubmission).not.toHaveBeenCalled();
  });

  it('is hidden when the grade does not invite a revision', async () => {
    getWritingSubmissionGrade.mockResolvedValue(GRADE);
    renderPage();

    await screen.findByText(/I am writing to refer this patient/);
    expect(screen.queryByTestId('revise-and-resubmit')).not.toBeInTheDocument();
  });

  it('is hidden on a mock and on a letter that is not graded', async () => {
    getWritingSubmission.mockResolvedValue({ ...SUBMISSION, mode: 'mock' });
    const { unmount } = renderPage();
    await screen.findByText(/I am writing to refer this patient/);
    expect(screen.queryByTestId('revise-and-resubmit')).not.toBeInTheDocument();
    unmount();

    getWritingSubmission.mockResolvedValue({ ...SUBMISSION, status: 'failed' });
    renderPage();
    await screen.findByText(/I am writing to refer this patient/);
    expect(screen.queryByTestId('revise-and-resubmit')).not.toBeInTheDocument();
  });

  it('leaves a free-sample letter to its free retry button', async () => {
    listFreeSamples.mockResolvedValue([{
      professionId: 'medicine',
      contentId: 'scenario-1',
      state: 'retry_available',
      route: '/writing/submissions/sub-1/revise',
      limit: 2,
      successfulCount: 1,
      remaining: 1,
      lastResultRoute: '/writing/submissions/sub-1/results',
      lastSubmissionId: 'sub-1',
    }]);
    renderPage();

    expect(await screen.findByTestId('free-sample-revise-cta')).toHaveAttribute('href', '/writing/submissions/sub-1/revise');
    expect(screen.queryByTestId('revise-and-resubmit')).not.toBeInTheDocument();
  });
});

// WritingGrade.ConfidenceFlag is a grader band OR a review state set after grading
// ('jev_review' = queued for a human review, 'tutor_reviewed'). The learner only
// ever sees neutral copy, never the stored code.
describe('Writing results page — confidence flag copy', () => {
  const LOADED = /I am writing to refer this patient/;

  beforeEach(() => {
    vi.clearAllMocks();
    listFreeSamples.mockResolvedValue([]);
    getWritingSubmission.mockResolvedValue(SUBMISSION);
    getWritingSubmissionGrade.mockResolvedValue(GRADE);
    getWritingAssessmentV11.mockResolvedValue(null);
    getTutorReview.mockResolvedValue(null);
    getWritingAnswerSheet.mockResolvedValue({ answerSheetPdfDownloadPath: null });
    getWritingSubmissionCaseNotes.mockResolvedValue(null);
  });

  it.each([
    ['high', 'writing.submissions.results.confidence.high'],
    ['medium', 'writing.submissions.results.confidence.medium'],
    ['low', 'writing.submissions.results.confidence.low'],
    ['jev_review', 'writing.submissions.results.confidence.awaitingReview'],
    ['tutor_reviewed', 'writing.submissions.results.confidence.tutorReviewed'],
  ])('shows the learner label for %s, never the stored code', async (flag, labelKey) => {
    getWritingSubmissionGrade.mockResolvedValue({ ...GRADE, confidenceFlag: flag });
    const { container } = renderPage();

    expect(await screen.findByText(labelKey)).toBeInTheDocument();
    expect(container.textContent).not.toMatch(/jev_review|tutor_reviewed/);
  });

  it('hides the confidence stat for a flag it does not know instead of printing it', async () => {
    getWritingSubmissionGrade.mockResolvedValue({ ...GRADE, confidenceFlag: 'some_future_state' });
    const { container } = renderPage();

    await screen.findByText(LOADED);
    expect(screen.queryByText('writing.submissions.results.highlights.confidence')).not.toBeInTheDocument();
    expect(container.textContent).not.toContain('some_future_state');
  });

  it('shows no review-state copy on a mock (human-marked, zero AI)', async () => {
    getWritingSubmission.mockResolvedValue({ ...SUBMISSION, mode: 'mock' });
    getWritingSubmissionGrade.mockResolvedValue({ ...GRADE, confidenceFlag: 'jev_review' });
    renderPage();

    await screen.findByText(LOADED);
    expect(screen.queryByText('writing.submissions.results.highlights.confidence')).not.toBeInTheDocument();
    expect(screen.queryByText('writing.submissions.results.confidence.awaitingReview')).not.toBeInTheDocument();
  });

  it('ships neutral copy for the two review states in every locale', () => {
    for (const bundle of [enWriting, arWriting] as Array<Record<string, string>>) {
      for (const key of [
        'writing.submissions.results.confidence.awaitingReview',
        'writing.submissions.results.confidence.tutorReviewed',
      ]) {
        expect(bundle[key], key).toBeTruthy();
        expect(bundle[key], key).not.toMatch(/jev|typesafe|claude|codex|anthropic|openai|\bai\b/i);
      }
    }
  });
});
