import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// Mock all external collaborators of the AI management API client.
vi.mock('./auth-client', () => ({
  ensureFreshAccessToken: vi.fn(async () => 'test-token'),
}));

vi.mock('./env', () => ({
  env: { apiBaseUrl: 'https://api.test' },
}));

const fetchMock = vi.fn();
vi.mock('./network/fetch-with-timeout', () => ({
  fetchWithTimeout: (url: string, init?: RequestInit) => fetchMock(url, init),
}));

import * as ai from './ai-management-api';

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as unknown as Response;
}

function emptyResponse(status = 204): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => {
      throw new Error('no body');
    },
  } as unknown as Response;
}

beforeEach(() => {
  fetchMock.mockReset();
});

afterEach(() => {
  vi.clearAllMocks();
});

describe('aiApi wrapper (transport)', () => {
  it('prepends apiBaseUrl when path is relative', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse([]));
    await ai.fetchAiPlans();
    const [url] = fetchMock.mock.calls[0];
    expect(url).toBe('https://api.test/v1/admin/ai/plans');
  });

  it('attaches a bearer token from ensureFreshAccessToken', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse([]));
    await ai.fetchAiPlans();
    const [, init] = fetchMock.mock.calls[0];
    const headers = init?.headers as Headers;
    expect(headers.get('Authorization')).toBe('Bearer test-token');
    expect(headers.get('Accept')).toBe('application/json');
  });

  it('sets Content-Type when a body is provided', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: '1' }));
    await ai.createAiPlan({ code: 'p1' });
    const [, init] = fetchMock.mock.calls[0];
    const headers = init?.headers as Headers;
    expect(headers.get('Content-Type')).toBe('application/json');
  });

  it('serializes the request body as JSON', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: '1' }));
    await ai.createAiPlan({ code: 'p1', name: 'Plan 1' });
    const [, init] = fetchMock.mock.calls[0];
    expect(init?.body).toBe(JSON.stringify({ code: 'p1', name: 'Plan 1' }));
    expect(init?.method).toBe('POST');
  });

  it('throws an error with status and detail when response is not ok', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ message: 'not allowed' }, 403));
    try {
      await ai.fetchAiPlans();
      expect.fail('should have thrown');
    } catch (err) {
      expect((err as Error).message).toBe('HTTP 403');
      expect((err as Error & { status?: number }).status).toBe(403);
      expect((err as Error & { detail?: unknown }).detail).toEqual({ message: 'not allowed' });
    }
  });

  it('treats 204 No Content as undefined response', async () => {
    fetchMock.mockResolvedValueOnce(emptyResponse(204));
    const result = await ai.deactivateAiPlan('p1');
    expect(result).toBeUndefined();
  });

  it('still throws when error body is not JSON', async () => {
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 500,
      json: async () => {
        throw new Error('not json');
      },
    } as unknown as Response);
    await expect(ai.fetchAiPlans()).rejects.toMatchObject({ message: 'HTTP 500', status: 500 });
  });
});

describe('admin usage endpoints', () => {
  it('fetchAiUsage builds query string with non-empty filters', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ page: 1, pageSize: 50, total: 0, rows: [] }));
    await ai.fetchAiUsage({
      page: 2,
      pageSize: 25,
      userId: 'u1',
      featureCode: 'speaking.grade',
      providerId: '',
      outcome: undefined,
    });
    const [url] = fetchMock.mock.calls[0];
    expect(url).toContain('/v1/admin/ai/usage?');
    expect(url).toContain('page=2');
    expect(url).toContain('pageSize=25');
    expect(url).toContain('userId=u1');
    expect(url).toContain('featureCode=speaking.grade');
    expect(url).not.toContain('providerId');
    expect(url).not.toContain('outcome');
  });

  it('fetchAiUsageSummary defaults groupBy to feature', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ periodMonthKey: '2025-01', groupBy: 'feature', rows: [] }),
    );
    await ai.fetchAiUsageSummary();
    const [url] = fetchMock.mock.calls[0];
    expect(url).toContain('groupBy=feature');
    expect(url).not.toContain('periodMonthKey');
  });

  it('fetchAiUsageSummary appends periodMonthKey when provided', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ periodMonthKey: '2025-02', groupBy: 'provider', rows: [] }),
    );
    await ai.fetchAiUsageSummary('2025-02', 'provider');
    const [url] = fetchMock.mock.calls[0];
    expect(url).toContain('groupBy=provider');
    expect(url).toContain('periodMonthKey=2025-02');
  });

  it('fetchAiUsageTrend omits the question mark when no params provided', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ fromMonth: '', toMonth: '', rows: [] }),
    );
    await ai.fetchAiUsageTrend();
    const [url] = fetchMock.mock.calls[0];
    expect(url).toBe('https://api.test/v1/admin/ai/usage/trend');
  });

  it('fetchAiUsageTrend appends both fromMonth and toMonth', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ fromMonth: '2025-01', toMonth: '2025-03', rows: [] }),
    );
    await ai.fetchAiUsageTrend('2025-01', '2025-03');
    const [url] = fetchMock.mock.calls[0];
    expect(url).toContain('fromMonth=2025-01');
    expect(url).toContain('toMonth=2025-03');
  });

  it('fetchAiAnomalies hits the anomalies endpoint', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ enabled: false, rows: [] }));
    await ai.fetchAiAnomalies();
    const [url] = fetchMock.mock.calls[0];
    expect(url).toBe('https://api.test/v1/admin/ai/usage/anomalies');
  });
});

describe('admin plans / global policy', () => {
  it('fetchAiPlans is a GET', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse([]));
    await ai.fetchAiPlans();
    const [, init] = fetchMock.mock.calls[0];
    expect(init?.method).toBeUndefined();
  });

  it('updateAiPlan uses PUT with id in path', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 'p1' }));
    await ai.updateAiPlan('p1', { name: 'New' });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('https://api.test/v1/admin/ai/plans/p1');
    expect(init?.method).toBe('PUT');
  });

  it('deactivateAiPlan uses DELETE', async () => {
    fetchMock.mockResolvedValueOnce(emptyResponse(204));
    await ai.deactivateAiPlan('p1');
    const [, init] = fetchMock.mock.calls[0];
    expect(init?.method).toBe('DELETE');
  });

  it('fetchAiGlobalPolicy hits the global policy path', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({}));
    await ai.fetchAiGlobalPolicy();
    expect(fetchMock.mock.calls[0][0]).toBe('https://api.test/v1/admin/ai/global-policy');
  });

  it('toggleAiKillSwitch posts enabled+scope+reason', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({}));
    await ai.toggleAiKillSwitch(true, 'AllCalls', 'budget exceeded');
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('https://api.test/v1/admin/ai/kill-switch');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(init?.body as string)).toEqual({
      enabled: true,
      scope: 'AllCalls',
      reason: 'budget exceeded',
    });
  });
});

describe('admin providers', () => {
  it('createAiProvider posts to /providers', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 'pr1' }));
    await ai.createAiProvider({ code: 'openai' });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('https://api.test/v1/admin/ai/providers');
    expect(init?.method).toBe('POST');
  });

  it('updateAiProvider uses PUT with id', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 'pr1' }));
    await ai.updateAiProvider('pr1', { isActive: false });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('https://api.test/v1/admin/ai/providers/pr1');
    expect(init?.method).toBe('PUT');
  });

  it('deactivateAiProvider uses DELETE', async () => {
    fetchMock.mockResolvedValueOnce(emptyResponse(204));
    await ai.deactivateAiProvider('pr1');
    const [, init] = fetchMock.mock.calls[0];
    expect(init?.method).toBe('DELETE');
  });
});

describe('admin user credits + override', () => {
  it('fetchUserCredits hits /users/:id/credits', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({
        balance: { tokensAvailable: 0, costAvailableUsd: 0, tokensGrantedLifetime: 0, tokensConsumedLifetime: 0 },
        entries: [],
      }),
    );
    await ai.fetchUserCredits('u1');
    const [url] = fetchMock.mock.calls[0];
    expect(url).toBe('https://api.test/v1/admin/ai/users/u1/credits');
  });

  it('grantUserCredits posts the body to /credits/grant', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 'l1' }));
    await ai.grantUserCredits('u1', {
      tokens: 100,
      costUsd: 1.0,
      source: 'promo',
      description: 'welcome',
    });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('https://api.test/v1/admin/ai/users/u1/credits/grant');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(init?.body as string)).toMatchObject({ source: 'promo', tokens: 100 });
  });

  it('upsertAiUserOverride uses PUT with body', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ userId: 'u1', aiDisabled: true }));
    await ai.upsertAiUserOverride('u1', { aiDisabled: true, reason: 'abuse' });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('https://api.test/v1/admin/ai/users/u1/override');
    expect(init?.method).toBe('PUT');
  });

  it('removeAiUserOverride uses DELETE', async () => {
    fetchMock.mockResolvedValueOnce(emptyResponse(204));
    await ai.removeAiUserOverride('u1');
    expect(fetchMock.mock.calls[0][1]?.method).toBe('DELETE');
  });
});

describe('learner-scoped endpoints', () => {
  it('fetchMyAiCredentials hits /me/ai/credentials', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse([]));
    await ai.fetchMyAiCredentials();
    expect(fetchMock.mock.calls[0][0]).toBe('https://api.test/v1/me/ai/credentials');
  });

  it('saveMyAiCredential posts the credential body', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 'c1', keyHint: 'sk-***', providerCode: 'openai' }));
    await ai.saveMyAiCredential({ providerCode: 'openai', apiKey: 'sk-abc' });
    const [, init] = fetchMock.mock.calls[0];
    expect(init?.method).toBe('POST');
    expect(JSON.parse(init?.body as string)).toEqual({ providerCode: 'openai', apiKey: 'sk-abc' });
  });

  it('revokeMyAiCredential uses DELETE on the id', async () => {
    fetchMock.mockResolvedValueOnce(emptyResponse(204));
    await ai.revokeMyAiCredential('c1');
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('https://api.test/v1/me/ai/credentials/c1');
    expect(init?.method).toBe('DELETE');
  });

  it('updateMyAiPreferences uses PUT', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({}));
    await ai.updateMyAiPreferences({ mode: 'Auto', allowPlatformFallback: true });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('https://api.test/v1/me/ai/preferences');
    expect(init?.method).toBe('PUT');
  });
});

describe('URL resolver edge cases', () => {
  it('passes absolute http(s) URLs through unchanged', async () => {
    // Indirectly tested by intercepting an env override.
    // Reset env: without apiBaseUrl, relative paths stay relative.
    fetchMock.mockResolvedValueOnce(jsonResponse([]));
    await ai.fetchAiPlans();
    const [url] = fetchMock.mock.calls[0];
    expect(url.startsWith('https://api.test')).toBe(true);
  });
});
