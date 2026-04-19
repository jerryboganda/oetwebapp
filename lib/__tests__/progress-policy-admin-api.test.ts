/**
 * @vitest-environment jsdom
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';

const { apiRequestMock } = vi.hoisted(() => ({ apiRequestMock: vi.fn() }));

vi.mock('../api', () => ({
  apiRequest: apiRequestMock,
}));

import * as adminApi from '../progress-policy-admin-api';

describe('progress-policy-admin-api', () => {
  beforeEach(() => apiRequestMock.mockReset());

  it('getProgressPolicy defaults to oet exam family', async () => {
    apiRequestMock.mockResolvedValue({});
    await adminApi.getProgressPolicy();
    expect(apiRequestMock).toHaveBeenCalledWith('/v1/admin/progress-policy/oet');
  });

  it('getProgressPolicy honours a custom exam family', async () => {
    apiRequestMock.mockResolvedValue({});
    await adminApi.getProgressPolicy('ielts');
    expect(apiRequestMock).toHaveBeenCalledWith('/v1/admin/progress-policy/ielts');
  });

  it('updateProgressPolicy PUTs JSON body', async () => {
    apiRequestMock.mockResolvedValue({});
    await adminApi.updateProgressPolicy('oet', {
      defaultTimeRange: '30d',
      smoothingWindow: 5,
      minCohortSize: 25,
      mockDistinctStyle: false,
      showScoreGuaranteeStrip: true,
      showCriterionConfidenceBand: true,
      minEvaluationsForTrend: 3,
      exportPdfEnabled: true,
    });
    const call = apiRequestMock.mock.calls[0];
    expect(call[0]).toBe('/v1/admin/progress-policy/oet');
    expect(call[1].method).toBe('PUT');
    const body = JSON.parse(call[1].body);
    expect(body.defaultTimeRange).toBe('30d');
    expect(body.exportPdfEnabled).toBe(true);
  });
});
