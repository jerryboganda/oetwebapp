/**
 * Tests for the Listening Pathway API client (lib/listening-pathway-api.ts).
 *
 * Mirrors the structure of reading-pathway-api.test.ts:
 *   - Stubs auth-client.ensureFreshAccessToken so we never hit real auth.
 *   - Stubs env.apiBaseUrl so request URLs assert against the bare path.
 *   - Stubs network/fetch-with-timeout so we can introspect the call shape.
 *
 * Each test owns its own mock state via beforeEach.
 */

import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockEnsureFreshAccessToken, mockFetchWithTimeout } = vi.hoisted(() => ({
  mockEnsureFreshAccessToken: vi.fn(),
  mockFetchWithTimeout: vi.fn(),
}));

vi.mock('../auth-client', () => ({
  ensureFreshAccessToken: mockEnsureFreshAccessToken,
}));

vi.mock('../env', () => ({
  env: { apiBaseUrl: '' },
}));

vi.mock('../network/fetch-with-timeout', () => ({
  fetchWithTimeout: mockFetchWithTimeout,
}));

export {};

const api = await import('../listening-pathway-api');

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

function lastCall() {
  return mockFetchWithTimeout.mock.calls.at(-1) as [string, RequestInit];
}

describe('listening-pathway-api', () => {
  beforeEach(() => {
    mockEnsureFreshAccessToken.mockResolvedValue('access-token');
    mockFetchWithTimeout.mockReset();
    if (typeof document !== 'undefined') {
      Object.defineProperty(document, 'cookie', {
        configurable: true,
        get: () => 'oet_csrf=csrf-token',
      });
    }
    if (typeof window !== 'undefined') {
      window.sessionStorage.clear();
    }
  });

  it('loads diagnostic questions from the safe session projection route', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse([]));

    await api.getDiagnosticQuestions('test-session-id');

    const [url, init] = lastCall();
    expect(url).toBe(
      '/v1/listening-pathway/diagnostic/sessions/test-session-id/questions',
    );
    // Default method when none is supplied is GET.
    expect(init.method ?? 'GET').toBe('GET');
  });

  it('posts the diagnostic submission and caches the result in sessionStorage', async () => {
    const stub = {
      sessionId: 'sid',
      submittedAt: '2026-05-26T11:30:00Z',
      hero: {
        rawScore: 18,
        totalQuestions: 23,
        scaledScore: 380,
        gradeLabel: 'B',
        confidenceLowerBound: 370,
        confidenceUpperBound: 395,
        targetBandLabel: 'B',
      },
      skillRadar: [],
      accentChart: [],
      noteTakingStats: {
        charactersTyped: 0,
        typicalRangeLow: 0,
        typicalRangeHigh: 0,
        droppedDetails: [],
      },
      spellingStats: {
        meaningCorrectSpellingWrong: 0,
        examples: [],
      },
      timeAnalysis: {
        partABreakdown: 0,
        partBBreakdown: 0,
        partCBreakdown: 0,
        hesitationFlags: [],
      },
      roadmap: [],
    };
    mockFetchWithTimeout.mockResolvedValue(jsonResponse(stub));

    const result = await api.submitDiagnostic({
      sessionId: 'sid',
      answers: [],
      totalDurationSeconds: 1800,
    });

    const [url, init] = lastCall();
    expect(url).toBe('/v1/listening-pathway/diagnostic/submit');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toMatchObject({
      sessionId: 'sid',
      totalDurationSeconds: 1800,
    });

    expect(result.sessionId).toBe('sid');

    const cached = window.sessionStorage.getItem('listening_diagnostic_result_sid');
    expect(cached).not.toBeNull();
    expect(JSON.parse(cached as string)).toMatchObject({
      sessionId: 'sid',
      hero: expect.objectContaining({ rawScore: 18, gradeLabel: 'B' }),
    });
  });

  it('falls back to the sessionStorage cache when the results API fails', async () => {
    const cached = {
      sessionId: 'sid',
      submittedAt: '2026-05-26T11:30:00Z',
      hero: {
        rawScore: 17,
        totalQuestions: 23,
        scaledScore: 360,
        gradeLabel: 'B',
        confidenceLowerBound: 350,
        confidenceUpperBound: 370,
        targetBandLabel: 'B',
      },
      skillRadar: [],
      accentChart: [],
      noteTakingStats: {
        charactersTyped: 1200,
        typicalRangeLow: 800,
        typicalRangeHigh: 1500,
        droppedDetails: [],
      },
      spellingStats: { meaningCorrectSpellingWrong: 0, examples: [] },
      timeAnalysis: {
        partABreakdown: 600,
        partBBreakdown: 600,
        partCBreakdown: 600,
        hesitationFlags: [],
      },
      roadmap: [],
    };
    window.sessionStorage.setItem(
      'listening_diagnostic_result_sid',
      JSON.stringify(cached),
    );
    mockFetchWithTimeout.mockRejectedValue(new Error('network down'));

    const result = await api.getDiagnosticResults('sid');

    expect(result).toMatchObject({
      sessionId: 'sid',
      hero: expect.objectContaining({ rawScore: 17, scaledScore: 360 }),
    });
  });

  it('adapts the 8-skill score list and fills L1..L8 display labels', async () => {
    mockFetchWithTimeout.mockResolvedValue(
      jsonResponse([
        {
          skillCode: 'L1',
          currentScore: 7.0,
          diagnosticScore: 6.0,
          questionsAttempted: 10,
          questionsCorrect: 7,
        },
        {
          skillCode: 'L2',
          currentScore: 5.5,
          diagnosticScore: 4.5,
          questionsAttempted: 12,
          questionsCorrect: 6,
        },
        {
          skillCode: 'L8',
          currentScore: 6.2,
          diagnosticScore: 5.0,
          questionsAttempted: 8,
          questionsCorrect: 5,
        },
      ]),
    );

    const scores = await api.getSkillScores();

    expect(lastCall()[0]).toBe('/v1/listening-pathway/skills/scores');
    expect(scores).toHaveLength(3);
    expect(scores.find((s) => s.skillCode === 'L1')).toMatchObject({
      label: 'Detail capture',
      currentScore: 7.0,
      diagnosticScore: 6.0,
    });
    expect(scores.find((s) => s.skillCode === 'L2')?.label).toBe(
      'Note-taking speed',
    );
    expect(scores.find((s) => s.skillCode === 'L8')?.label).toBe(
      'Accent adaptation',
    );
  });

  it('adapts the per-accent progress list and fills display labels', async () => {
    mockFetchWithTimeout.mockResolvedValue(
      jsonResponse([
        { accent: 'british', accuracyPercentage: 75.0 },
        { accent: 'australian', accuracyPercentage: 62.5 },
        { accent: 'us', accuracyPercentage: 70.0 },
        { accent: 'non_native', accuracyPercentage: 55.5 },
      ]),
    );

    const accents = await api.getAccentProgress();

    expect(lastCall()[0]).toBe('/v1/listening-pathway/accents/progress');
    expect(accents).toHaveLength(4);
    expect(accents.find((a) => a.accent === 'british')?.label).toBe('British');
    expect(accents.find((a) => a.accent === 'australian')?.label).toBe(
      'Australian',
    );
    expect(accents.find((a) => a.accent === 'us')?.label).toBe(
      'North American',
    );
    expect(accents.find((a) => a.accent === 'non_native')?.label).toBe(
      'Non-native',
    );
    expect(accents.find((a) => a.accent === 'british')).toMatchObject({
      accuracyPercentage: 75.0,
    });
  });
});
