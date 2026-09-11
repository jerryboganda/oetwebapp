import { render, screen, within } from '@testing-library/react';
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
  appealWritingSubmission,
  disputeWritingCanonViolation,
  publishToShowcase,
  createWritingSubmission,
  reviseWritingSubmission,
} = vi.hoisted(() => ({
  getWritingSubmission: vi.fn(),
  getWritingSubmissionGrade: vi.fn(),
  getWritingAssessmentV11: vi.fn(),
  getTutorReview: vi.fn(),
  getWritingAnswerSheet: vi.fn(),
  getWritingSubmissionCaseNotes: vi.fn(),
  appealWritingSubmission: vi.fn(),
  disputeWritingCanonViolation: vi.fn(),
  publishToShowcase: vi.fn(),
  // Not imported by this page today — kept as spies so a future regression
  // that wires either into the free review path is caught here too.
  createWritingSubmission: vi.fn(),
  reviseWritingSubmission: vi.fn(),
}));

vi.mock('@/lib/writing/api', () => ({
  getWritingSubmission,
  getWritingSubmissionGrade,
  getWritingAssessmentV11,
  getTutorReview,
  getWritingAnswerSheet,
  getWritingSubmissionCaseNotes,
  appealWritingSubmission,
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
}));

import WritingSubmissionResultsPage from './page';

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

describe('Writing results page — free review vs. new attempt (Addendum Rev5 §13)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getWritingSubmission.mockResolvedValue(SUBMISSION);
    getWritingSubmissionGrade.mockResolvedValue(GRADE);
    getWritingAssessmentV11.mockResolvedValue(null);
    getTutorReview.mockResolvedValue(null);
    getWritingAnswerSheet.mockResolvedValue({ answerSheetPdfDownloadPath: null });
    getWritingSubmissionCaseNotes.mockResolvedValue(null);
  });

  it('reopens the saved report via GET-only reads: shows the exact original letter, score, and criteria, with zero grading/credit calls', async () => {
    render(<WritingSubmissionResultsPage />);

    // The exact stored letter is now part of the same report (was previously
    // missing from this page entirely).
    expect(await screen.findByText(/I am writing to refer this patient/)).toBeInTheDocument();
    expect(screen.getByText(/no credit used/i)).toBeInTheDocument();

    // Saved score/grade + all six criteria still render.
    expect(screen.getByText(/writing\.submissions\.results\.criteria\.heading/i)).toBeInTheDocument();
    expect(screen.getByText('Clear purpose.')).toBeInTheDocument();
    expect(screen.getByText('Tighten the opening.')).toBeInTheDocument();

    // Only reads fired — no mutation, grading, or credit-consuming call.
    expect(getWritingSubmission).toHaveBeenCalledWith('sub-1');
    expect(appealWritingSubmission).not.toHaveBeenCalled();
    expect(disputeWritingCanonViolation).not.toHaveBeenCalled();
    expect(publishToShowcase).not.toHaveBeenCalled();
    expect(createWritingSubmission).not.toHaveBeenCalled();
    expect(reviseWritingSubmission).not.toHaveBeenCalled();
  });

  it('keeps "Request tutor review" removed from this AI result flow while "Appeal score" stays', async () => {
    render(<WritingSubmissionResultsPage />);
    await screen.findByText(/I am writing to refer this patient/);

    expect(screen.queryByText(/request tutor review/i)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /appeal/i })).toBeInTheDocument();
  });

  it('"Practice this again" starts a distinct new attempt via the practice-session entitlement gate, not this attempt\'s id', async () => {
    render(<WritingSubmissionResultsPage />);
    await screen.findByText(/I am writing to refer this patient/);

    const practiceAgainLink = screen.getByRole('link', { name: /practiceAgain/i });
    expect(practiceAgainLink).toHaveAttribute('href', '/writing/practice/session/scenario-1');
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
    getWritingSubmission.mockResolvedValue(SUBMISSION);
    getWritingSubmissionGrade.mockResolvedValue(GRADE);
    getWritingAssessmentV11.mockResolvedValue(ASSESSMENT_V11);
    getTutorReview.mockResolvedValue(null);
    getWritingAnswerSheet.mockResolvedValue({ answerSheetPdfDownloadPath: null });
    getWritingSubmissionCaseNotes.mockResolvedValue(null);
  });

  it('renders the grounded model answer with every line break preserved, the /500 AI score + grade band as the headline, and all six criteria', async () => {
    render(<WritingSubmissionResultsPage />);

    // Model Answer text is rendered exactly as stored — no collapsing, trimming, or splitting.
    const modelAnswer = await screen.findByTestId('grounded-model-answer');
    expect(modelAnswer.textContent).toContain('\nRe: Mr David Taylor, aged 55\n\nI am writing');
    expect(modelAnswer.textContent).toBe(MODEL_ANSWER_TEXT);
    expect(modelAnswer).toHaveClass('whitespace-pre-wrap');
    expect(screen.getByTestId('grounded-model-answer-card')).toContainElement(modelAnswer);

    // The AI Estimated Practice Score /500 + grade band is the headline even
    // though the older /grade data also loaded; raw total stays secondary.
    expect(screen.getByTestId('ai-estimated-score')).toHaveTextContent('382/500');
    expect(screen.getByTestId('ai-grade-band')).toHaveTextContent('Grade B');
    expect(screen.getByText('33/38')).toBeInTheDocument();
    expect(screen.queryByText('writing.submissions.results.estimatedBand')).not.toBeInTheDocument();

    // All six criteria render with score/max.
    const criteria = within(screen.getByTestId('assessment-criteria-list')).getAllByRole('article');
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
      expect(within(criteria[index]).getByRole('heading', { name })).toBeInTheDocument();
      expect(within(criteria[index]).getByText(score)).toBeInTheDocument();
    });
    expect(within(screen.getByTestId('criteria-list')).getAllByRole('listitem')).toHaveLength(6);
  });

  it("highlights the grader's per-criterion quote of the candidate's own wording", async () => {
    getWritingSubmissionGrade.mockResolvedValue({
      ...GRADE,
      perCriterion: {
        ...GRADE.perCriterion,
        c6: { ...GRADE.perCriterion.c6, feedback: 'Subject-verb agreement error.', quote: 'the patient have chest pain' },
      },
    });
    render(<WritingSubmissionResultsPage />);

    const quote = await screen.findByText('“the patient have chest pain”');
    expect(quote.tagName).toBe('MARK');
    expect(screen.getByText(/Subject-verb agreement error\./)).toBeInTheDocument();
  });
});
