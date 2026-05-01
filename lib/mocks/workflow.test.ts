import type { MockReport, MockSession } from '@/lib/mock-data';
import {
  buildMockRemediationPlan,
  getMockModePolicy,
  getMockReadinessDecision,
  getMockSectionPolicy,
  getMockSubmissionReadiness,
} from './workflow';

describe('mocks workflow policy', () => {
  it('distinguishes strict exam mode from teaching-focused practice mode', () => {
    const exam = getMockModePolicy('exam');
    const practice = getMockModePolicy('practice');

    expect(exam.strictTimerRequired).toBe(true);
    expect(exam.listeningReplayAllowed).toBe(false);
    expect(exam.writingAssistantAllowed).toBe(false);
    expect(exam.speakingCoachingAllowed).toBe(false);

    expect(practice.strictTimerRequired).toBe(false);
    expect(practice.listeningReplayAllowed).toBe(true);
    expect(practice.writingAssistantAllowed).toBe(true);
    expect(practice.speakingCoachingAllowed).toBe(true);
  });

  it('describes sub-test-specific timing and teacher review rules', () => {
    const writing = getMockSectionPolicy('writing', 'exam');
    const speaking = getMockSectionPolicy('speaking', 'exam');
    const reading = getMockSectionPolicy('reading', 'exam');

    expect(writing.timing).toContain('5-minute reading window');
    expect(writing.examRule).toContain('No grammar assistant');
    expect(writing.reviewRule).toContain('Teacher-marked');
    expect(speaking.reviewRule).toContain('Tutor/interlocutor');
    expect(reading.examRule).toContain('Part A locks');
  });

  it('blocks final submission until every mock section is recorded', () => {
    const session: MockSession = {
      sessionId: 'mock-session-1',
      state: 'in_progress',
      resumeRoute: '/mocks/player/mock-session-1',
      reportRoute: null,
      reportId: null,
      config: {
        id: 'mock-session-1',
        title: 'Full OET Mock',
        type: 'full',
        mode: 'exam',
        profession: 'Medicine',
        strictTimer: true,
        includeReview: true,
        reviewSelection: 'writing_and_speaking',
      },
      sectionStates: [
        {
          id: 'listening',
          title: 'Listening',
          subtest: 'listening',
          state: 'completed',
          reviewAvailable: false,
          reviewSelected: false,
          launchRoute: '/listening/player/listening-1',
        },
        {
          id: 'writing',
          title: 'Writing',
          subtest: 'writing',
          state: 'ready',
          reviewAvailable: true,
          reviewSelected: true,
          launchRoute: '/writing/player?taskId=wt-1&mode=exam',
        },
      ],
      reviewReservation: null,
    };

    expect(getMockSubmissionReadiness(session)).toMatchObject({
      canSubmit: false,
      completedCount: 1,
      totalCount: 2,
    });

    const completed = {
      ...session,
      sectionStates: session.sectionStates.map((section) => ({ ...section, state: 'completed' })),
    };

    expect(getMockSubmissionReadiness(completed)).toMatchObject({
      canSubmit: true,
      completedCount: 2,
      totalCount: 2,
    });
  });

  it('turns mock reports into ethical readiness and remediation guidance', () => {
    const report: MockReport = {
      id: 'mock-report-1',
      title: 'Full OET Mock',
      date: '2026-05-01',
      overallScore: '68%',
      summary: 'Borderline mock report.',
      subTests: [],
      weakestCriterion: {
        subtest: 'Writing',
        criterion: 'Content selection',
        description: 'Important care-plan details were missed.',
      },
      priorComparison: {
        exists: false,
        priorMockName: '',
        overallTrend: 'flat',
        details: 'No previous mock.',
      },
    };

    const readiness = getMockReadinessDecision(report);
    const plan = buildMockRemediationPlan(report);

    expect(readiness.level).toBe('amber');
    expect(readiness.description).toContain('retake a mock');
    expect(plan).toHaveLength(5);
    expect(plan[1]).toMatchObject({
      day: 'Day 2',
      title: 'Repair Content selection',
      route: '/writing/library',
    });
  });
});
