import type { MockReportPayloadV1 } from './report-payload';
import {
  MOCK_REPORT_PAYLOAD_SCHEMA_VERSION,
  isMockReportPayloadV1,
} from './report-payload';

function buildMinimalV1Payload(
  overrides: Partial<MockReportPayloadV1> = {},
): MockReportPayloadV1 {
  return {
    payloadSchemaVersion: MOCK_REPORT_PAYLOAD_SCHEMA_VERSION,
    id: 'mock-report-1',
    reportId: 'report-1',
    mockAttemptId: 'attempt-1',
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
    reviewSummary: {
      queued: 0,
      inReview: 0,
      completed: 0,
      pending: 0,
    },
    perModuleReadiness: [],
    partScores: [],
    timingAnalysis: [],
    errorCategories: [],
    teacherReviewState: {
      queued: 0,
      inReview: 0,
      completed: 0,
      pending: 0,
    },
    bookingAdvice: {
      status: 'hold',
      message: 'Strengthen weak areas before booking.',
      route: null,
      score: null,
    },
    retakeAdvice: {
      recommendedWindowDays: 7,
      nextMockType: 'full',
      subtest: 'Writing',
      message: 'Retake a full mock after targeted writing practice.',
    },
    proctoringSummary: {
      totalEvents: 0,
      advisoryOnly: true,
      criticalEvents: 0,
      warningEvents: 0,
      byKind: [],
      message: 'No proctoring incidents detected.',
    },
    remediationPlan: [],
    priorComparison: {
      exists: false,
      priorMockName: '',
      overallTrend: 'flat',
      details: 'No previous mock.',
    },
    weaknessNarrative: null,
    passPrediction: null,
    trend: null,
    perQuestionTiming: null,
    teacherFeedbackFragments: null,
    ...overrides,
  };
}

describe('MOCK_REPORT_PAYLOAD_SCHEMA_VERSION constant', () => {
  it('is the literal string "v1"', () => {
    expect(MOCK_REPORT_PAYLOAD_SCHEMA_VERSION).toBe('v1');
  });
});

describe('isMockReportPayloadV1 type guard', () => {
  it('returns true for a fully populated V1 payload', () => {
    const payload = buildMinimalV1Payload();
    expect(isMockReportPayloadV1(payload)).toBe(true);
  });

  it('returns false when payloadSchemaVersion is missing', () => {
    const { payloadSchemaVersion: _omit, ...rest } = buildMinimalV1Payload();
    void _omit;
    expect(isMockReportPayloadV1(rest)).toBe(false);
  });

  it('returns false when payloadSchemaVersion is the pre-V1 legacy empty string', () => {
    const legacy = { ...buildMinimalV1Payload(), payloadSchemaVersion: '' };
    expect(isMockReportPayloadV1(legacy)).toBe(false);
  });

  it('returns false when payloadSchemaVersion is an unrecognised version marker', () => {
    const future = { ...buildMinimalV1Payload(), payloadSchemaVersion: 'v2' };
    expect(isMockReportPayloadV1(future)).toBe(false);
  });

  it('returns false for null, undefined, and non-object primitives', () => {
    expect(isMockReportPayloadV1(null)).toBe(false);
    expect(isMockReportPayloadV1(undefined)).toBe(false);
    expect(isMockReportPayloadV1('v1')).toBe(false);
    expect(isMockReportPayloadV1(42)).toBe(false);
    expect(isMockReportPayloadV1(true)).toBe(false);
  });
});

describe('MockReportPayloadV1 schema shape integrity', () => {
  it('accepts a synthetic minimal-but-valid V1 payload through the guard', () => {
    const payload = buildMinimalV1Payload();

    expect(isMockReportPayloadV1(payload)).toBe(true);
    expect(payload.payloadSchemaVersion).toBe('v1');
    expect(payload.id).toBe('mock-report-1');
    expect(payload.reportId).toBe('report-1');
    expect(payload.mockAttemptId).toBe('attempt-1');
    expect(payload.title).toBe('Full OET Mock');
    expect(payload.date).toBe('2026-05-01');
    expect(payload.overallScore).toBe('68%');
    expect(payload.summary).toBe('Borderline mock report.');
  });

  it('preserves nested required structures (weakestCriterion, reviewSummary, bookingAdvice, retakeAdvice, proctoringSummary, priorComparison)', () => {
    const payload = buildMinimalV1Payload();

    expect(payload.weakestCriterion).toMatchObject({
      subtest: 'Writing',
      criterion: 'Content selection',
      description: 'Important care-plan details were missed.',
    });
    expect(payload.reviewSummary).toMatchObject({
      queued: 0,
      inReview: 0,
      completed: 0,
      pending: 0,
    });
    expect(payload.bookingAdvice).toMatchObject({
      status: 'hold',
      message: 'Strengthen weak areas before booking.',
      route: null,
      score: null,
    });
    expect(payload.retakeAdvice).toMatchObject({
      recommendedWindowDays: 7,
      nextMockType: 'full',
      subtest: 'Writing',
    });
    expect(payload.proctoringSummary).toMatchObject({
      totalEvents: 0,
      advisoryOnly: true,
      criticalEvents: 0,
      warningEvents: 0,
      byKind: [],
    });
    expect(payload.priorComparison).toMatchObject({
      exists: false,
      priorMockName: '',
      overallTrend: 'flat',
      details: 'No previous mock.',
    });
  });

  it('uses empty arrays for the required list fields', () => {
    const payload = buildMinimalV1Payload();

    expect(payload.subTests).toEqual([]);
    expect(payload.perModuleReadiness).toEqual([]);
    expect(payload.partScores).toEqual([]);
    expect(payload.timingAnalysis).toEqual([]);
    expect(payload.errorCategories).toEqual([]);
    expect(payload.remediationPlan).toEqual([]);
  });

  it('still passes the guard when every optional field is null', () => {
    const payload = buildMinimalV1Payload({
      weaknessNarrative: null,
      passPrediction: null,
      trend: null,
      perQuestionTiming: null,
      teacherFeedbackFragments: null,
    });

    expect(isMockReportPayloadV1(payload)).toBe(true);
    expect(payload.weaknessNarrative).toBeNull();
    expect(payload.passPrediction).toBeNull();
    expect(payload.trend).toBeNull();
    expect(payload.perQuestionTiming).toBeNull();
    expect(payload.teacherFeedbackFragments).toBeNull();
  });

  it('still passes the guard when optional fields are populated with rich values', () => {
    const payload = buildMinimalV1Payload({
      profession: 'Medicine',
      targetCountry: 'UK',
      deliveryMode: 'computer',
      strictness: 'strict',
      releasePolicy: 'after_review',
      overallGrade: 'B',
      weaknessNarrative: {
        headline: 'Content selection needs work',
        body: 'Address discharge summary detail gaps.',
        tags: [
          {
            tag: 'discharge-detail',
            subtest: 'Writing',
            description: 'Missed discharge-plan medications.',
            drillId: 'drill-1',
            drillRouteHref: '/writing/library',
          },
        ],
      },
      passPrediction: {
        confidenceBand: 'medium',
        verdict: 'borderline',
        rationale: 'Writing pulled the overall band down.',
      },
      trend: {
        attemptsConsidered: 3,
        overallTrend: 'up',
        consistentGreen: false,
        message: 'Trending up across the last three mocks.',
      },
      perQuestionTiming: [
        {
          sectionId: 'listening-section-1',
          subtest: 'listening',
          itemId: 'q1',
          secondsSpent: 32,
          correct: true,
        },
      ],
      teacherFeedbackFragments: [
        {
          subtest: 'writing',
          reviewRequestId: 'review-1',
          criterion: 'Content selection',
          comment: 'Strengthen the care-plan section.',
          anchorRef: 'para-2',
        },
      ],
    });

    expect(isMockReportPayloadV1(payload)).toBe(true);
    expect(payload.weaknessNarrative?.tags).toHaveLength(1);
    expect(payload.passPrediction?.confidenceBand).toBe('medium');
    expect(payload.trend?.attemptsConsidered).toBe(3);
    expect(payload.perQuestionTiming).toHaveLength(1);
    expect(payload.teacherFeedbackFragments).toHaveLength(1);
  });
});

describe('MockReportPayloadV1 JSON round-trip', () => {
  it('survives JSON.stringify followed by JSON.parse and still satisfies the guard', () => {
    const original = buildMinimalV1Payload({
      profession: 'Medicine',
      overallGrade: 'B',
      weaknessNarrative: {
        headline: 'Watch content selection',
        body: 'Care-plan detail gaps recurred.',
        tags: [
          {
            tag: 'care-plan',
            subtest: 'Writing',
            description: 'Missing medication detail.',
            drillId: null,
            drillRouteHref: null,
          },
        ],
      },
    });

    const serialized = JSON.stringify(original);
    const parsed: unknown = JSON.parse(serialized);

    expect(isMockReportPayloadV1(parsed)).toBe(true);

    // Narrow to the typed contract via the guard before structural assertions.
    if (!isMockReportPayloadV1(parsed)) {
      throw new Error('Guard narrowing failed in JSON round-trip test.');
    }

    expect(parsed.payloadSchemaVersion).toBe('v1');
    expect(parsed.id).toBe(original.id);
    expect(parsed.reportId).toBe(original.reportId);
    expect(parsed.mockAttemptId).toBe(original.mockAttemptId);
    expect(parsed.title).toBe(original.title);
    expect(parsed.weakestCriterion).toEqual(original.weakestCriterion);
    expect(parsed.priorComparison).toEqual(original.priorComparison);
    expect(parsed.weaknessNarrative).toEqual(original.weaknessNarrative);
  });

  it('survives a round-trip when all optional fields are null', () => {
    const original = buildMinimalV1Payload();
    const parsed: unknown = JSON.parse(JSON.stringify(original));

    expect(isMockReportPayloadV1(parsed)).toBe(true);

    if (!isMockReportPayloadV1(parsed)) {
      throw new Error('Guard narrowing failed in null-optional round-trip test.');
    }

    expect(parsed.weaknessNarrative).toBeNull();
    expect(parsed.passPrediction).toBeNull();
    expect(parsed.trend).toBeNull();
    expect(parsed.perQuestionTiming).toBeNull();
    expect(parsed.teacherFeedbackFragments).toBeNull();
  });
});
