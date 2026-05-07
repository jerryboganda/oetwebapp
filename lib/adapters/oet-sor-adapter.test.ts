import { describe, it, expect } from 'vitest';
import { isMockReportStatementOfResultsReady, mockReportToStatementOfResults } from '@/lib/adapters/oet-sor-adapter';
import type { MockReport } from '@/lib/mock-data';

const base: MockReport = {
  id: 'mock-abc-123',
  title: 'Mock 1',
  date: '2026-04-18',
  overallScore: 'B',
  summary: 'ok',
  subTests: [
    { id: 'listening', name: 'Listening', score: '430', rawScore: '35/42', color: '', bg: '' },
    { id: 'reading', name: 'Reading', score: '420', rawScore: '34/42', color: '', bg: '' },
    { id: 'speaking', name: 'Speaking', score: '350', rawScore: '', color: '', bg: '' },
    { id: 'writing', name: 'Writing', score: '370', rawScore: '', color: '', bg: '' },
  ],
  weakestCriterion: { subtest: 'Writing', criterion: '', description: '' },
  priorComparison: { exists: false, priorMockName: '', overallTrend: 'flat', details: '' },
};

describe('mockReportToStatementOfResults', () => {
  it('maps four scaled scores correctly', () => {
    const sor = mockReportToStatementOfResults({ report: base });
    expect(sor.scores.listening).toBe(430);
    expect(sor.scores.reading).toBe(420);
    expect(sor.scores.speaking).toBe(350);
    expect(sor.scores.writing).toBe(370);
  });

  it('clamps out-of-range scores to 0–500 and rounds to step 10', () => {
    const sor = mockReportToStatementOfResults({
      report: {
        ...base,
        subTests: [
          { id: 'listening', name: 'Listening', score: '9999', rawScore: '', color: '', bg: '' },
          { id: 'reading', name: 'Reading', score: '-50', rawScore: '', color: '', bg: '' },
          { id: 'speaking', name: 'Speaking', score: '347', rawScore: '', color: '', bg: '' }, // rounds to 350
          { id: 'writing', name: 'Writing', score: 'abc', rawScore: '', color: '', bg: '' }, // non-numeric -> 0
        ],
      },
    });
    expect(sor.scores.listening).toBe(500);
    expect(sor.scores.reading).toBe(0);
    expect(sor.scores.speaking).toBe(350);
    expect(sor.scores.writing).toBe(0);
  });

  it('generates a stable candidate number from the report id', () => {
    const a = mockReportToStatementOfResults({ report: base });
    const b = mockReportToStatementOfResults({ report: { ...base, title: 'different title' } });
    expect(a.candidate.candidateNumber).toMatch(/^OET-\d{6}-\d{6}$/);
    expect(a.candidate.candidateNumber).toBe(b.candidate.candidateNumber);
  });

  it('respects passed-in candidate + country + profession', () => {
    const sor = mockReportToStatementOfResults({
      report: base,
      candidate: { name: 'Dr Tanaka', candidateNumber: 'OET-900000-000001' },
      profession: 'nursing',
      country: 'Australia',
    });
    expect(sor.candidate.name).toBe('Dr Tanaka');
    expect(sor.candidate.candidateNumber).toBe('OET-900000-000001');
    expect(sor.test.profession).toBe('Nursing');
    expect(sor.venue.country).toBe('Australia');
  });

  it('always sets isPractice=true', () => {
    const sor = mockReportToStatementOfResults({ report: base });
    expect(sor.isPractice).toBe(true);
  });
});

describe('isMockReportStatementOfResultsReady', () => {
  it('allows a full report with all four final numeric scores', () => {
    expect(isMockReportStatementOfResultsReady(base)).toBe(true);
  });

  it('accepts legacy generated subtest ids when names are canonical', () => {
    const report: MockReport = {
      ...base,
      subTests: [
        { id: 'g-l', name: 'Listening', score: '430', rawScore: '35/42', color: '', bg: '' },
        { id: 'g-r', name: 'Reading', score: '420', rawScore: '34/42', color: '', bg: '' },
        { id: 'g-s', name: 'Speaking', score: '350', rawScore: '', color: '', bg: '' },
        { id: 'g-w', name: 'Writing', score: '370', rawScore: '', color: '', bg: '' },
      ],
    };

    expect(isMockReportStatementOfResultsReady(report)).toBe(true);
    expect(mockReportToStatementOfResults({ report }).scores).toMatchObject({
      listening: 430,
      reading: 420,
      speaking: 350,
      writing: 370,
    });
  });

  it('blocks reports with pending teacher review scores', () => {
    expect(isMockReportStatementOfResultsReady({
      ...base,
      subTests: base.subTests.map((subtest) => subtest.id === 'writing'
        ? { ...subtest, score: 'Pending', reviewState: 'in_review' }
        : subtest),
    })).toBe(false);
  });

  it('blocks partial subtest reports', () => {
    expect(isMockReportStatementOfResultsReady({
      ...base,
      subTests: base.subTests.filter((subtest) => subtest.id !== 'speaking'),
    })).toBe(false);
  });
});
