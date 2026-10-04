import { render, screen } from '@testing-library/react';
import type { AiAssessment, TutorAssessment } from '@/lib/api/speaking-assessments';

import { DualAssessmentColumn } from '../DualAssessmentColumn';

/** What /v1/speaking/sessions/{id}/assessments sends for the AI side today: flat criterion scores. */
function aiAssessment(overrides: Partial<Record<string, unknown>> = {}): AiAssessment {
  return {
    assessmentId: 'ai-1',
    provider: 'provider',
    modelId: 'model',
    promptTemplateId: 'speaking.score.v2',
    criterionScores: {},
    estimatedScaledScore: 310,
    readinessBand: 'borderline',
    overallSummary: 'Clear and kind.',
    confidenceBand: 'medium',
    generatedAt: '2026-10-04T10:00:00Z',
    isAdvisory: true,
    grade: 'C+',
    scoreLabel: 'provisional',
    ...overrides,
  } as AiAssessment;
}

function tutorAssessment(estimatedScaledScore: number): TutorAssessment {
  return {
    assessmentId: 't-1',
    tutorId: 'tutor',
    intelligibility: 4, fluency: 4, appropriateness: 4, grammarExpression: 4,
    relationshipBuilding: 2, patientPerspective: 2, structure: 2, informationGathering: 2, informationGiving: 2,
    estimatedScaledScore,
    readinessBand: 'exam_ready',
    strengths: [], improvements: [], recommendedDrills: [],
    isFinal: true,
  };
}

describe('DualAssessmentColumn — one reported score, one OET grade', () => {
  it('shows the AI score in ten-point steps with the grade the server sent', () => {
    render(<DualAssessmentColumn kind="ai" title="AI Assessment" assessment={aiAssessment()} />);

    expect(screen.getByText('310')).toBeInTheDocument();
    expect(screen.getByTestId('dual-grade-ai')).toHaveTextContent('Grade C+');
  });

  it('rounds a legacy unrounded score to the ten-point number the grade is derived from', () => {
    render(<DualAssessmentColumn kind="ai" title="AI Assessment" assessment={aiAssessment({ estimatedScaledScore: 345, grade: undefined })} />);

    // 345 is shown as 350 and is therefore Grade B, never "C+" and never a score ending in 5.
    expect(screen.getByText('350')).toBeInTheDocument();
    expect(screen.queryByText('345')).not.toBeInTheDocument();
    expect(screen.getByTestId('dual-grade-ai')).toHaveTextContent('Grade B');
  });

  it.each<[number, string]>([
    [500, 'A'], [450, 'A'], [440, 'B'], [430, 'B'], [350, 'B'], [340, 'C+'], [300, 'C+'], [290, 'C'], [190, 'D'], [90, 'E'],
  ])('a reported %i is Grade %s and never B+', (score, letter) => {
    render(<DualAssessmentColumn kind="ai" title="AI Assessment" assessment={aiAssessment({ estimatedScaledScore: score, grade: undefined })} />);

    expect(screen.getByTestId('dual-grade-ai')).toHaveTextContent(`Grade ${letter}`);
    expect(document.body.textContent).not.toContain('B+');
  });

  it('labels the AI score provisional until the grader has been calibrated', () => {
    render(<DualAssessmentColumn kind="ai" title="AI Assessment" assessment={aiAssessment()} />);

    expect(screen.getByTestId('speaking-score-provisional')).toHaveTextContent('Provisional score — calibration in progress');
  });

  it('treats an AI payload with no label as provisional', () => {
    render(<DualAssessmentColumn kind="ai" title="AI Assessment" assessment={aiAssessment({ scoreLabel: undefined })} />);

    expect(screen.getByTestId('speaking-score-provisional')).toBeInTheDocument();
  });

  it('drops the provisional label once the server marks the score a calibrated practice estimate', () => {
    render(<DualAssessmentColumn kind="ai" title="AI Assessment" assessment={aiAssessment({ scoreLabel: 'ai_practice_estimate' })} />);

    expect(screen.queryByTestId('speaking-score-provisional')).not.toBeInTheDocument();
    expect(screen.getByTestId('dual-grade-ai')).toHaveTextContent('Grade C+');
  });

  it('can leave its score box out when the page already leads with the score', () => {
    render(<DualAssessmentColumn kind="ai" title="Your whole test" assessment={aiAssessment()} showScore={false} />);

    expect(screen.queryByText('Estimated scaled score')).not.toBeInTheDocument();
    expect(screen.queryByTestId('dual-grade-ai')).not.toBeInTheDocument();
    expect(screen.getByText('Clear and kind.')).toBeInTheDocument();
  });

  it('shows the tutor score the same way, without the AI calibration label', () => {
    render(<DualAssessmentColumn kind="tutor" title="Tutor Assessment" assessment={tutorAssessment(390)} />);

    expect(screen.getByText('390')).toBeInTheDocument();
    expect(screen.getByTestId('dual-grade-tutor')).toHaveTextContent('Grade B');
    expect(screen.queryByTestId('speaking-score-provisional')).not.toBeInTheDocument();
  });
});

describe('DualAssessmentColumn — what Intelligibility was judged from', () => {
  const withIntelligibility = (intelligibilityEvidence: unknown) =>
    aiAssessment({
      criterionScores: { intelligibility: { score: 4, maxScore: 6, rationale: 'Mostly clear.', evidenceQuotes: [] } },
      intelligibilityEvidence,
    });

  it('says plainly that the number is estimated from the transcript when no audio could be used', () => {
    render(
      <DualAssessmentColumn
        kind="ai"
        title="AI Assessment"
        assessment={withIntelligibility({
          source: 'transcript_only',
          reason: 'no_audio',
          reasonText: 'no audio recording was kept for this attempt',
          confidence: 'low',
          observations: [],
        })}
      />,
    );

    expect(screen.getByTestId('intelligibility-evidence-label')).toHaveTextContent(
      'Estimated from the transcript only (limited evidence) — no audio recording was kept for this attempt.',
    );
    expect(document.body.textContent).not.toContain('no_audio');
  });

  it('shows what was heard when Intelligibility was judged from the recording', () => {
    render(
      <DualAssessmentColumn
        kind="ai"
        title="AI Assessment"
        assessment={withIntelligibility({
          source: 'audio',
          confidence: 'high',
          observations: [{ clip: 2, approxSecond: 31, issue: 'The ending of "inhaler" was dropped.', example: 'inha-uh' }],
        })}
      />,
    );

    expect(screen.getByTestId('intelligibility-evidence-label')).toHaveTextContent('Judged from your recording');
    expect(screen.getByTestId('intelligibility-observations')).toHaveTextContent('About 31 s: The ending of "inhaler" was dropped.');
    expect(screen.getByTestId('intelligibility-observations')).toHaveTextContent('inha-uh');
    expect(screen.queryByTestId('intelligibility-evidence-limited')).not.toBeInTheDocument();
    expect(document.body.textContent).not.toContain('Estimated from the transcript only');
  });

  it('says so when the recording was hard to judge', () => {
    render(
      <DualAssessmentColumn
        kind="ai"
        title="AI Assessment"
        assessment={withIntelligibility({ source: 'audio', confidence: 'low', observations: [] })}
      />,
    );

    expect(screen.getByTestId('intelligibility-evidence-limited')).toBeInTheDocument();
    expect(screen.queryByTestId('intelligibility-observations')).not.toBeInTheDocument();
  });

  it('shows nothing for a payload that carries no evidence (an older API)', () => {
    render(<DualAssessmentColumn kind="ai" title="AI Assessment" assessment={withIntelligibility(undefined)} />);

    expect(screen.queryByTestId('intelligibility-evidence-label')).not.toBeInTheDocument();
  });

  it('never puts the note on the tutor side', () => {
    render(<DualAssessmentColumn kind="tutor" title="Tutor Assessment" assessment={tutorAssessment(390)} />);

    expect(screen.queryByTestId('intelligibility-evidence-label')).not.toBeInTheDocument();
  });
});
