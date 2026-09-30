import { render, screen } from '@testing-library/react';
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
