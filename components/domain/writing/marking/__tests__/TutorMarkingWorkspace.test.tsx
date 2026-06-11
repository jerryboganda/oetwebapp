import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import type { WritingAnnotationCreatePayload } from '@/lib/writing/exam-api';

/**
 * Vitest spec for the shared TutorMarkingWorkspace (WS-F5).
 *
 * Everything renders from `getTutorMarkingContext`. We mock the exam-api
 * contract and the two heavy children that depend on DOM selection / charts:
 *  - AnnotationLayer (real impl drives offsets off a live DOM Range, which is
 *    not reliable in jsdom) is replaced with a stub that surfaces an "Add test
 *    annotation" button calling its onCreate prop, so we can assert the
 *    workspace wires creation to createWritingAnnotation.
 *  - CriteriaRadar (recharts) is stubbed to a no-op.
 * We then assert the six OET criteria render and that adding an annotation /
 * submitting a review reach the mocked exam-api.
 */

const {
  getTutorMarkingContext,
  createWritingAnnotation,
  deleteWritingAnnotation,
  submitWritingTutorReview,
  getWritingModeration,
  finalizeWritingModeration,
} = vi.hoisted(() => ({
  getTutorMarkingContext: vi.fn(),
  createWritingAnnotation: vi.fn(),
  deleteWritingAnnotation: vi.fn(),
  submitWritingTutorReview: vi.fn(),
  getWritingModeration: vi.fn(),
  finalizeWritingModeration: vi.fn(),
}));

vi.mock('@/lib/writing/exam-api', () => ({
  getTutorMarkingContext: (...a: unknown[]) => getTutorMarkingContext(...a),
  createWritingAnnotation: (...a: unknown[]) => createWritingAnnotation(...a),
  deleteWritingAnnotation: (...a: unknown[]) => deleteWritingAnnotation(...a),
  submitWritingTutorReview: (...a: unknown[]) => submitWritingTutorReview(...a),
  getWritingModeration: (...a: unknown[]) => getWritingModeration(...a),
  finalizeWritingModeration: (...a: unknown[]) => finalizeWritingModeration(...a),
}));

// Replace AnnotationLayer with a stub exposing its onCreate wiring (the real
// impl computes offsets from a live DOM Range, unreliable under jsdom).
vi.mock('../AnnotationLayer', () => ({
  AnnotationLayer: ({
    onCreate,
  }: {
    onCreate: (payload: WritingAnnotationCreatePayload) => Promise<void> | void;
  }) => (
    <button
      type="button"
      onClick={() =>
        void onCreate({
          criterion: 'c2',
          highlightedText: 'admitted',
          startOffset: 0,
          endOffset: 8,
          severity: 'medium',
          suggestion: null,
          feedbackText: 'Tense issue',
        })
      }
    >
      Add test annotation
    </button>
  ),
}));

// CriteriaRadar pulls in recharts; stub to keep the test light + deterministic.
vi.mock('@/components/domain/writing/CriteriaRadar', () => ({
  CriteriaRadar: () => <div data-testid="criteria-radar" />,
}));

// ── Mocks required by WritingStimulusViewer (rendered when stimulusPdfDownloadPath is set) ──
vi.mock('@/lib/api', () => ({
  fetchAuthorizedObjectUrl: vi.fn().mockResolvedValue('blob:fake-url'),
  // The marking workspace renders <OverallVoiceNote> -> <VoiceNoteRecorder>,
  // which import the voice-note upload API from '@/lib/api'. Mock every export
  // the rendered subtree pulls in so the component mounts cleanly.
  uploadWritingMarkingVoiceNote: vi.fn().mockResolvedValue(null),
  uploadSpeakingReviewCriterionVoiceNote: vi.fn().mockResolvedValue(null),
  uploadWritingReviewCriterionVoiceNote: vi.fn().mockResolvedValue(null),
  getWritingSubmissionVoiceNote: vi.fn().mockResolvedValue(null),
}));

vi.mock('pdfjs-dist/legacy/build/pdf.mjs', () => {
  const fakePage = {
    getViewport: vi.fn(() => ({ width: 100, height: 100 })),
    render: vi.fn(() => ({ promise: Promise.resolve() })),
  };
  const fakePdf = {
    numPages: 1,
    getPage: vi.fn().mockResolvedValue(fakePage),
    destroy: vi.fn(),
  };
  return {
    GlobalWorkerOptions: { workerSrc: '' },
    version: '3.x',
    getDocument: vi.fn(() => ({ promise: Promise.resolve(fakePdf) })),
  };
});

beforeEach(() => {
  HTMLCanvasElement.prototype.getContext = vi.fn().mockReturnValue({
    clearRect: vi.fn(),
    drawImage: vi.fn(),
    fillRect: vi.fn(),
    putImageData: vi.fn(),
    createImageData: vi.fn(),
    scale: vi.fn(),
    save: vi.fn(),
    restore: vi.fn(),
    transform: vi.fn(),
    fillText: vi.fn(),
  });
});

import { TutorMarkingWorkspace } from '../TutorMarkingWorkspace';

function makeContext(overrides: Record<string, unknown> = {}) {
  return {
    submission: {
      id: 'sub-1',
      userId: 'user-1',
      scenarioId: 'task-1',
      mode: 'timed',
      letterContent: 'The patient was admitted with chest pain.',
      contentHash: 'hash',
      wordCount: 8,
      timeSpentSeconds: 1200,
      startedAt: '2026-05-01T00:00:00Z',
      submittedAt: '2026-05-01T00:40:00Z',
      isRevision: false,
      originalSubmissionId: null,
      status: 'graded',
      gradingTier: 'express',
      inputSource: 'editor',
    },
    task: {
      id: 'task-1',
      internalCode: null,
      title: 'Discharge — Mr Brown',
      profession: 'medicine',
      letterType: 'LT-DG',
      difficulty: 3,
      status: 'published',
      version: 1,
      writerRole: 'the charge nurse',
      todayDate: '14 March 2026',
      taskPromptMarkdown: 'Write a discharge letter.',
      expectedPurpose: null,
      expectedAction: null,
      fixedInstructions: ['Use letter format.'],
      wordGuideMin: 180,
      wordGuideMax: 200,
      readingTimeSeconds: 300,
      writingTimeSeconds: 2400,
      simulationModes: 'both',
      markingMode: 'tutor',
      sourceProvenance: null,
      createdAt: '2026-05-01T00:00:00Z',
      updatedAt: '2026-05-01T00:00:00Z',
    },
    aiGrade: null,
    preAssessment: {
      source: 'heuristic',
      estimatedBands: { c1: 2, c2: 5, c3: 5, c4: 5, c5: 5, c6: 5 },
      estimatedRawTotal: 27,
      estimatedBandLabel: 'B',
      confidence: 'medium',
      wordCount: 8,
      withinWordGuide: false,
      keyContentCoveragePercent: 50,
      missingKeyContent: [],
      detectedIrrelevantContent: [],
      languageNotes: [],
      suggestedCriterionFeedback: {},
    },
    existingReview: null,
    annotations: [],
    moderation: null,
    markerSequence: 'first',
    ...overrides,
  };
}

describe('TutorMarkingWorkspace', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the six OET rubric criteria once context loads', async () => {
    getTutorMarkingContext.mockResolvedValue(makeContext());

    render(<TutorMarkingWorkspace submissionId="sub-1" variant="tutor" />);

    // The criterion *labels* appear in several panels (pre-analysis, comments,
    // rubric). Anchor on the rubric's per-criterion stepper buttons, whose
    // aria-labels are unique and hardcoded (not translated).
    expect(
      await screen.findByRole('button', { name: 'Decrease C1 Purpose' }),
    ).toBeInTheDocument();
    for (const label of [
      'C2 Content',
      'C3 Conciseness & Clarity',
      'C4 Genre & Style',
      'C5 Organisation & Layout',
      'C6 Language',
    ]) {
      expect(
        screen.getByRole('button', { name: `Decrease ${label}` }),
      ).toBeInTheDocument();
    }
  });

  it('invokes createWritingAnnotation when an annotation is added', async () => {
    // Span annotations are a MOCK-marking feature (normal writing uses voice
    // notes instead), so the AnnotationLayer only renders when mode === 'mock'.
    const ctx = makeContext();
    ctx.submission.mode = 'mock';
    getTutorMarkingContext.mockResolvedValue(ctx);
    createWritingAnnotation.mockResolvedValue({
      id: 'ann-1',
      submissionId: 'sub-1',
      reviewId: null,
      tutorId: 'tutor-1',
      criterion: 'c2',
      highlightedText: 'admitted',
      startOffset: 0,
      endOffset: 8,
      severity: 'medium',
      suggestion: null,
      feedbackText: 'Tense issue',
      createdAt: '2026-05-01T01:00:00Z',
    });

    render(<TutorMarkingWorkspace submissionId="sub-1" variant="tutor" />);

    const addBtn = await screen.findByRole('button', { name: 'Add test annotation' });
    fireEvent.click(addBtn);

    await waitFor(() => {
      expect(createWritingAnnotation).toHaveBeenCalledTimes(1);
    });
    expect(createWritingAnnotation.mock.calls[0][0]).toBe('sub-1');
    expect(createWritingAnnotation.mock.calls[0][1]).toMatchObject({
      criterion: 'c2',
      feedbackText: 'Tense issue',
    });
  });

  it('invokes submitWritingTutorReview on Submit review', async () => {
    getTutorMarkingContext.mockResolvedValue(makeContext());
    submitWritingTutorReview.mockResolvedValue({ review: { id: 'rev-1' }, moderation: null });

    render(<TutorMarkingWorkspace submissionId="sub-1" variant="tutor" />);

    const submitBtn = await screen.findByRole('button', { name: /submit review/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(submitWritingTutorReview).toHaveBeenCalledTimes(1);
    });
    expect(submitWritingTutorReview.mock.calls[0][0]).toBe('sub-1');
    // The payload carries the marker sequence from the loaded context.
    expect(submitWritingTutorReview.mock.calls[0][1]).toMatchObject({
      markerSequence: 'first',
    });
    // The success path runs to completion (the confirmation toast renders).
    expect(await screen.findByText('Review submitted.')).toBeInTheDocument();
  });

  it('renders WritingStimulusViewer in the case-notes card when stimulusPdfDownloadPath is set', async () => {
    const { fetchAuthorizedObjectUrl } = await import('@/lib/api');

    getTutorMarkingContext.mockResolvedValue(
      makeContext({
        task: {
          id: 'task-1',
          internalCode: null,
          title: 'Discharge — Mr Brown',
          profession: 'medicine',
          letterType: 'LT-DG',
          difficulty: 3,
          status: 'published',
          version: 1,
          writerRole: 'the charge nurse',
          todayDate: '14 March 2026',
          taskPromptMarkdown: 'Write a discharge letter.',
          expectedPurpose: null,
          expectedAction: null,
          fixedInstructions: ['Use letter format.'],
          wordGuideMin: 180,
          wordGuideMax: 200,
          readingTimeSeconds: 300,
          writingTimeSeconds: 2400,
          simulationModes: 'both',
          markingMode: 'tutor',
          sourceProvenance: null,
          createdAt: '2026-05-01T00:00:00Z',
          updatedAt: '2026-05-01T00:00:00Z',
          stimulusPdfDownloadPath: '/v1/media/pdf-123/content',
        },
      }),
    );

    render(<TutorMarkingWorkspace submissionId="sub-1" variant="tutor" />);

    // Wait for context to load (criteria rubric is a reliable sentinel).
    await screen.findByRole('button', { name: 'Decrease C1 Purpose' });

    // The stimulus viewer toolbar title should be visible.
    expect(screen.getByText('Question paper')).toBeInTheDocument();

    // The viewer should have fetched the authenticated blob URL.
    await waitFor(() => {
      expect(fetchAuthorizedObjectUrl).toHaveBeenCalledWith('/v1/media/pdf-123/content');
    });

    // The printed case-note sections must NOT appear (replaced by PDF viewer).
    expect(screen.queryByText('65yo male')).not.toBeInTheDocument();
  });
});
