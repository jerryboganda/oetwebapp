import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type {
  SpeakingSimulationV11AssessmentReport,
  SpeakingSimulationV11AssessmentResponse,
} from '@/lib/api/speaking-simulation-v11';

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

  it('says no audio recording is stored for a live conversation, and never promises source audio', async () => {
    render(<SpeakingSimulationV11ReportView sessionId="s1" response={response('a')} inputKind="live_voice" />);
    await openTranscriptTab();

    expect(screen.getByRole('heading', { name: 'Transcript of your live conversation' })).toBeInTheDocument();
    expect(screen.getByText(/No audio recording is stored for live conversations, so there is nothing to play back\./)).toBeInTheDocument();
    expect(screen.queryByText(/source audio/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Audio playback is available/)).not.toBeInTheDocument();
  });

  it('stays neutral when the input kind is not known yet', async () => {
    render(<SpeakingSimulationV11ReportView sessionId="s1" response={response('a')} inputKind={null} />);
    await openTranscriptTab();

    expect(screen.getByRole('heading', { name: 'Transcript' })).toBeInTheDocument();
    expect(screen.queryByText(/source audio/i)).not.toBeInTheDocument();
    expect(screen.getByText(/only for evidence linked to a recording\. No audio recording is stored for live conversations\./)).toBeInTheDocument();
  });

  it('does not blame audio in the technical-review notice of a live conversation', () => {
    const review = { ...response('a'), status: 'TechnicalReview', report: null, technicalReviewCode: 'transcript_missing' };
    const { rerender } = render(<SpeakingSimulationV11ReportView sessionId="s1" response={review} inputKind="live_voice" />);

    expect(screen.getByText(/because the transcript or the assessment could not be verified/)).toBeInTheDocument();
    expect(screen.queryByText(/authoritative audio/)).not.toBeInTheDocument();

    rerender(<SpeakingSimulationV11ReportView sessionId="s1" response={review} />);
    expect(screen.getByText(/the authoritative audio, transcript, or assessment pipeline could not be verified/)).toBeInTheDocument();
  });
});
