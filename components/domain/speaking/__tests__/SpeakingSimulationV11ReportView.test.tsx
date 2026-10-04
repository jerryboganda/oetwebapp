import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type {
  SpeakingSimulationV11Criterion,
  SpeakingSimulationV11AssessmentReport,
  SpeakingSimulationV11AssessmentResponse,
} from '@/lib/api/speaking-simulation-v11';
import { fetchAuthorizedObjectUrl } from '@/lib/api';
import { speakingSimulationV11AudioPath } from '@/lib/api/speaking-simulation-v11';

vi.mock('@/lib/api', () => ({
  fetchAuthorizedObjectUrl: vi.fn(),
  apiClient: {},
}));

import { SpeakingSimulationV11ReportView } from '../SpeakingSimulationV11ReportView';

function report(cardSlot: string): SpeakingSimulationV11AssessmentReport {
  return {
    assessmentId: 'a1',
    assessmentKind: cardSlot === 'combined' ? 'combined' : 'card',
    cardSlot,
    specVersion: 'v1.1',
    rubricVersion: 'r1',
    calibrationVersion: 'c1',
    graphDisclaimer: 'AI Estimated Practice Score — not an official OET result',
    estimatedPracticeScore: 380,
    scoreRangeLow: 350,
    scoreRangeHigh: 410,
    confidenceLabel: 'medium',
    confidenceScore: 0.7,
    overallSummary: null,
    criteria: [],
    cardBreakdowns: [],
    strengths: [],
    weaknesses: [],
    taskMap: [],
    timeline: [],
    languageAnalysis: {},
    timeManagement: {},
    topFive: [],
    betterAlternatives: [],
    tips: [],
    practicePlan: [],
    sourceTranscriptId: null,
    sourceRecordingId: null,
    cardVersion: null,
    generatedAt: '2026-09-30T12:00:00Z',
  };
}

function response(cardSlot: string): SpeakingSimulationV11AssessmentResponse {
  return {
    assessmentId: 'a1',
    status: 'Complete',
    assessmentKind: cardSlot === 'combined' ? 'combined' : 'card',
    cardSlot,
    estimatedPracticeScore: 380,
    scoreRangeLow: 350,
    scoreRangeHigh: 410,
    graphDisclaimer: 'AI Estimated Practice Score — not an official OET result',
    confidenceLabel: 'medium',
    confidenceScore: 0.7,
    report: report(cardSlot),
    technicalReviewCode: null,
    generatedAt: '2026-09-30T12:00:00Z',
  };
}

describe('SpeakingSimulationV11ReportView scope label', () => {
  it.each([
    ['a', 'Card A'],
    ['b', 'Card B'],
    ['A', 'Card A'],
    ['combined', 'Full mock'],
  ])('prints the report of slot "%s" as %s', (cardSlot, label) => {
    render(<SpeakingSimulationV11ReportView sessionId="s1" response={response(cardSlot)} />);

    expect(screen.getByText(label)).toBeInTheDocument();
  });

  it('prints no card line for a standalone practice report (it has no card slot)', () => {
    render(<SpeakingSimulationV11ReportView sessionId="s1" response={response('standalone')} />);

    expect(screen.queryByText(/^Card /)).not.toBeInTheDocument();
    expect(screen.queryByText(/standalone/i)).not.toBeInTheDocument();
    expect(screen.queryByText('Full mock')).not.toBeInTheDocument();
    // The rest of the header is unchanged.
    expect(screen.getByText(/Confidence:/)).toBeInTheDocument();
    expect(screen.getByText(/Range:/)).toBeInTheDocument();
  });
});

describe('SpeakingSimulationV11ReportView wording by input kind', () => {
  const openTranscriptTab = () => userEvent.setup().click(screen.getByRole('button', { name: 'transcript' }));

  it('keeps the audio wording on the transcript tab when no input kind is given', async () => {
    render(<SpeakingSimulationV11ReportView sessionId="s1" response={response('a')} />);
    await openTranscriptTab();

    expect(screen.getByRole('heading', { name: 'Transcript and source audio' })).toBeInTheDocument();
    expect(screen.getByText(/Audio playback is available only for source-linked evidence/)).toBeInTheDocument();
  });

  it('keeps the audio wording for a recording', async () => {
    render(<SpeakingSimulationV11ReportView sessionId="s1" response={response('a')} inputKind="recording" />);
    await openTranscriptTab();

    expect(screen.getByRole('heading', { name: 'Transcript and source audio' })).toBeInTheDocument();
  });

  it('identifies live microphone clips without promising full-session playback or perfect isolation', async () => {
    render(<SpeakingSimulationV11ReportView sessionId="s1" response={response('a')} inputKind="live_voice" />);
    await openTranscriptTab();

    expect(screen.getByRole('heading', { name: 'Transcript of your live conversation' })).toBeInTheDocument();
    expect(screen.getByText(/Short microphone clips are available when transcript evidence has a verified source recording\./)).toBeInTheDocument();
    expect(screen.getByText(/speaker audio may still be picked up by your microphone\./)).toBeInTheDocument();
  });

  it('stays neutral when the input kind is not known yet', async () => {
    render(<SpeakingSimulationV11ReportView sessionId="s1" response={response('a')} inputKind={null} />);
    await openTranscriptTab();

    expect(screen.getByRole('heading', { name: 'Transcript' })).toBeInTheDocument();
    expect(screen.queryByText(/source audio/i)).not.toBeInTheDocument();
    expect(screen.getByText('Audio playback is available only for evidence linked to a verified source recording.')).toBeInTheDocument();
  });

  it('does not blame audio in the technical-review notice of a live conversation', () => {
    const review = { ...response('a'), status: 'TechnicalReview', report: null, technicalReviewCode: 'transcript_missing' };
    const { rerender } = render(<SpeakingSimulationV11ReportView sessionId="s1" response={review} inputKind="live_voice" />);

    expect(screen.getByText(/because the transcript or the assessment could not be verified/)).toBeInTheDocument();
    expect(screen.queryByText(/authoritative audio/)).not.toBeInTheDocument();

    rerender(<SpeakingSimulationV11ReportView sessionId="s1" response={review} />);
    expect(screen.getByText(/the authoritative audio, transcript, or assessment pipeline could not be verified/)).toBeInTheDocument();
  });

  it('never shows a human-tutor revision or an internal reason code on an AI report', () => {
    const review = { ...response('a'), status: 'TechnicalReview', report: null, technicalReviewCode: 'transcript_missing' };
    const { container, rerender } = render(<SpeakingSimulationV11ReportView sessionId="s1" response={review} />);

    expect(container.textContent).not.toContain('transcript_missing');
    expect(container.textContent).not.toMatch(/Reason:/);

    rerender(<SpeakingSimulationV11ReportView sessionId="s1" response={response('a')} />);
    expect(container.textContent).not.toMatch(/tutor/i);
  });
});

describe('SpeakingSimulationV11ReportView source audio evidence', () => {
  it('plays a combined-report candidate clip from its own session at its clip-relative offset', async () => {
    const user = userEvent.setup();
    const evidence = {
      evidenceType: 'audio_acoustic',
      evidenceStatus: 'supported' as const,
      primaryCriterionCode: 'intelligibility_pronunciation',
      turnNumber: 3,
      quoteText: 'Please tell me more.',
      startMs: 4_250,
      endMs: 6_000,
      finding: 'Clear articulation.',
      action: 'Keep this pace.',
      confidenceLabel: 'high',
      confidenceScore: 0.9,
      sourceTranscriptId: 'transcript-a',
      sourceRecordingId: 'candidate-clip-a',
      isPrimary: true,
      sourceCardSlot: 'a',
      sourceSpeakingSessionId: 'source-session-a',
      sourceAudioOffsetMs: 0,
    };
    const criterion: SpeakingSimulationV11Criterion = {
      criterionCode: 'intelligibility_pronunciation',
      label: 'Intelligibility & pronunciation',
      weight: 10,
      rawScore: 80,
      weightedScore: 8,
      scoreBand: 'strong',
      rationale: 'Source-linked audio supports this result.',
      evidence: [evidence],
      strength: null,
      weakness: null,
      action: null,
      confidenceLabel: 'high',
      confidenceScore: 0.9,
    };
    const combinedReport = {
      ...report('combined'),
      criteria: [criterion],
    };
    const combinedResponse = {
      ...response('combined'),
      report: combinedReport,
    };
    const audio = {
      currentTime: -1,
      addEventListener: vi.fn(),
      play: vi.fn().mockResolvedValue(undefined),
      pause: vi.fn(),
    } as unknown as HTMLAudioElement;
    const audioConstructor = vi.fn(function () { return audio; });
    vi.stubGlobal('Audio', audioConstructor);
    vi.mocked(fetchAuthorizedObjectUrl).mockResolvedValue('blob:candidate-clip');

    try {
      render(
        <SpeakingSimulationV11ReportView
          sessionId="exam-first-session"
          response={combinedResponse}
          inputKind="live_voice"
        />,
      );
      await user.click(screen.getByRole('button', { name: 'criteria' }));
      await user.click(screen.getByRole('button', { name: 'Play 0:04' }));

      expect(fetchAuthorizedObjectUrl).toHaveBeenCalledWith(
        speakingSimulationV11AudioPath('source-session-a', 'candidate-clip-a'),
      );
      expect(audio.currentTime).toBe(0);
      expect(audio.play).toHaveBeenCalledOnce();
    } finally {
      vi.unstubAllGlobals();
      vi.mocked(fetchAuthorizedObjectUrl).mockReset();
    }
  });
});
