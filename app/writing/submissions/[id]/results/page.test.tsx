import { render, screen } from '@testing-library/react';
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
