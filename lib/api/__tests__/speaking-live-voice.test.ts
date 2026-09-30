const { mockRequest, mockGet } = vi.hoisted(() => ({ mockRequest: vi.fn(), mockGet: vi.fn() }));

vi.mock('@/lib/api', () => ({
  apiClient: { request: mockRequest, get: mockGet },
}));

import {
  createGeminiLiveToken,
  createOpenAiLiveOffer,
  getLiveVoicePreflight,
} from '../speaking-live-voice';

describe('live voice session API', () => {
  beforeEach(() => {
    mockRequest.mockReset().mockResolvedValue({});
    mockGet.mockReset().mockResolvedValue({});
  });

  // A repeat POST after a post-creation failure could open a second billed provider session, and the
  // hook's failover to the other provider is the retry: the API client must not retry either create call.
  it.each([
    { name: 'the OpenAI offer', call: () => createOpenAiLiveOffer('sps 1', 'v=0 offer'), path: '/v1/speaking/realtime/sessions/sps%201/openai/offer', body: { sdp: 'v=0 offer' } },
    { name: 'the Gemini token', call: () => createGeminiLiveToken('sps 1'), path: '/v1/speaking/realtime/sessions/sps%201/gemini/token', body: {} },
  ])('posts $name once, without automatic retries and with a bounded wait', async ({ call, path, body }) => {
    await call();

    expect(mockRequest).toHaveBeenCalledTimes(1);
    const [requestPath, init, options] = mockRequest.mock.calls[0];
    expect(requestPath).toBe(path);
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body)).toEqual(body);
    expect(options).toEqual({ maxRetries: 0, timeoutMs: 22_000 });
  });

  it('asks the preflight for a provider only when one is forced', async () => {
    await getLiveVoicePreflight('sps 1');
    await getLiveVoicePreflight('sps 1', 'gemini');

    expect(mockGet.mock.calls[0][0]).toBe('/v1/speaking/realtime/sessions/sps%201/preflight');
    expect(mockGet.mock.calls[1][0]).toBe('/v1/speaking/realtime/sessions/sps%201/preflight?provider=gemini');
  });
});
