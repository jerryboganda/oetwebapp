import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockApiRequest } = vi.hoisted(() => ({ mockApiRequest: vi.fn() }));

// Only the network call is replaced; the record helpers and the row mapper stay real.
vi.mock('../api/client', async () => {
  const actual = await vi.importActual<typeof import('../api/client')>('../api/client');
  return { ...actual, apiRequest: mockApiRequest };
});

import { fetchMyAttemptHistory } from '../api/billing-core';

describe('fetchMyAttemptHistory', () => {
  beforeEach(() => {
    mockApiRequest.mockReset();
  });

  it('keeps the server-built result label on a scored Speaking mock and passes the subtest filter to the server', async () => {
    mockApiRequest.mockResolvedValue({
      items: [
        {
          attemptId: 'spx_1',
          subtest: 'speaking',
          title: 'Full Speaking Mock',
          contentRef: 'spx_1',
          startedAt: '2026-10-01T10:00:00Z',
          submittedAt: '2026-10-01T10:25:00Z',
          status: 'completed',
          balanceSource: 'shared',
          creditsUsed: 4,
          route: '/speaking/exam/spx_1/results',
          resultLabel: '190/500',
          grade: 'D',
        },
      ],
    });

    await expect(fetchMyAttemptHistory(50, 'speaking')).resolves.toEqual([
      {
        attemptId: 'spx_1',
        subtest: 'speaking',
        title: 'Full Speaking Mock',
        contentRef: 'spx_1',
        startedAt: '2026-10-01T10:00:00Z',
        submittedAt: '2026-10-01T10:25:00Z',
        status: 'completed',
        balanceSource: 'shared',
        creditsUsed: 4,
        route: '/speaking/exam/spx_1/results',
        resultLabel: '190/500',
        grade: 'D',
      },
    ]);
    expect(mockApiRequest).toHaveBeenCalledWith('/v1/me/attempts?limit=50&subtest=speaking');
  });

  it('maps a missing grade to null so only scored Speaking rows show a letter', async () => {
    mockApiRequest.mockResolvedValue({
      items: [
        { attemptId: 'att_r1', subtest: 'reading', title: 'Reading Part A paper', startedAt: '2026-09-30T09:00:00Z', status: 'completed', creditsUsed: 0, route: '/reading/paper/rp_1' },
        { attemptId: 'spx_2', subtest: 'speaking', title: 'Full Speaking Mock', startedAt: '2026-10-01T10:00:00Z', status: 'completed', creditsUsed: 4, route: '/speaking/exam/spx_2/results', resultLabel: "Grading didn't finish — retry is free", grade: null },
      ],
    });

    const rows = await fetchMyAttemptHistory();

    expect(rows.map((row) => row.grade)).toEqual([null, null]);
  });

  it('maps a missing, null or empty label to null so rows without a result show none', async () => {
    mockApiRequest.mockResolvedValue({
      items: [
        { attemptId: 'att_r1', subtest: 'reading', title: 'Reading Part A paper', startedAt: '2026-09-30T09:00:00Z', status: 'completed', creditsUsed: 0, route: '/reading/paper/rp_1' },
        { attemptId: 'spx_2', subtest: 'speaking', title: 'Full Speaking Mock', startedAt: '2026-10-01T10:00:00Z', status: 'in_progress', creditsUsed: 2, route: '/speaking/exam/spx_2', resultLabel: null },
        { attemptId: 'spx_3', subtest: 'speaking', title: 'Full Speaking Mock', startedAt: '2026-10-01T11:00:00Z', status: 'completed', creditsUsed: 4, route: '/speaking/exam/spx_3/results', resultLabel: '' },
      ],
    });

    const rows = await fetchMyAttemptHistory();

    expect(rows.map((row) => row.resultLabel)).toEqual([null, null, null]);
    expect(mockApiRequest).toHaveBeenCalledWith('/v1/me/attempts?limit=100');
  });
});
