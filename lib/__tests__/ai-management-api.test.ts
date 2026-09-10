const { mockFetchWithTimeout } = vi.hoisted(() => ({
  mockFetchWithTimeout: vi.fn(),
}));

vi.mock('@/lib/auth-client', () => ({
  ensureFreshAccessToken: vi.fn(async () => 'fake.jwt.token'),
}));

vi.mock('@/lib/env', () => ({
  env: { apiBaseUrl: '' },
}));

vi.mock('@/lib/network/fetch-with-timeout', () => ({
  fetchWithTimeout: mockFetchWithTimeout,
}));

export {};

const { fetchAiProviders, testAiProviderModel } = await import('../ai-management-api');

describe('AI management API', () => {
  beforeEach(() => {
    mockFetchWithTimeout.mockReset();
    mockFetchWithTimeout.mockResolvedValue(new Response(JSON.stringify({
      status: 'ok',
      errorMessage: null,
      latencyMs: 85_000,
      testedAt: '2026-09-10T02:25:38Z',
      model: 'chatgpt_web|GPT-5.6 Sol',
      steps: [],
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }));
  });

  it('allows the UBAG full-pipeline model test to outlive normal API calls', async () => {
    await testAiProviderModel('ubag', 'chatgpt_web|GPT-5.6 Sol');

    expect(mockFetchWithTimeout).toHaveBeenCalledWith(
      '/v1/admin/ai/providers/ubag/test-model',
      expect.objectContaining({ method: 'POST' }),
      270_000,
    );
  });

  it('keeps ordinary AI management calls on the default timeout', async () => {
    mockFetchWithTimeout.mockResolvedValueOnce(new Response('[]', {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }));

    await fetchAiProviders();

    expect(mockFetchWithTimeout).toHaveBeenCalledWith(
      '/v1/admin/ai/providers',
      expect.any(Object),
      undefined,
    );
  });
});