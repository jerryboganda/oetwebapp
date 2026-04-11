// Test the exported utilities from api.ts by importing them
// We need to mock auth-client before importing api
vi.mock('../auth-client', () => ({
  ensureFreshAccessToken: vi.fn(async () => null),
}));

// Import after mocks
const { ApiError } = await import('../api');

describe('ApiError', () => {
  it('creates an error with all fields', () => {
    const error = new ApiError(400, 'validation_error', 'Invalid input', false, [
      { field: 'email', code: 'required', message: 'Email is required' },
    ]);

    expect(error).toBeInstanceOf(Error);
    expect(error.name).toBe('ApiError');
    expect(error.status).toBe(400);
    expect(error.code).toBe('validation_error');
    expect(error.retryable).toBe(false);
    expect(error.fieldErrors).toHaveLength(1);
    expect(error.fieldErrors[0].field).toBe('email');
  });

  it('maps known error codes to user-friendly messages', () => {
    const error = new ApiError(409, 'draft_version_conflict', 'Conflict', false);
    expect(error.userMessage).toBe('Your draft was updated in another tab. Please refresh and try again.');
  });

  it('maps rate_limited to friendly message', () => {
    const error = new ApiError(429, 'rate_limited', 'Too many requests', true);
    expect(error.userMessage).toBe('Too many requests. Please wait a moment and try again.');
  });

  it('falls back to server message for unknown codes', () => {
    const error = new ApiError(500, 'some_custom_error', 'Server exploded', true);
    expect(error.userMessage).toBe('Server exploded');
  });

  it('defaults fieldErrors to empty array', () => {
    const error = new ApiError(500, 'internal_server_error', 'fail', false);
    expect(error.fieldErrors).toEqual([]);
  });
});

describe('learner route normalization', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.resetModules();
  });

  it('normalizes legacy learner routes returned by API payloads', async () => {
    globalThis.fetch = vi.fn(async () => new Response(JSON.stringify({
      drillGroups: [
        {
          id: 'pronunciation',
          title: 'Pronunciation drills',
          items: [{ id: 'sp-drill-1', route: '/app/speaking/review/sa-001' }],
        },
        {
          id: 'empathy_clarification',
          title: 'Empathy and clarification drills',
          items: [{ id: 'sp-drill-2', route: '/app/speaking/tasks' }],
        },
      ],
      pastAttempts: [
        { route: '/app/speaking/result/se-001' },
        { route: '/app/speaking/attempt/sa-002' },
      ],
      reviewCredits: {
        route: '/app/reviews',
      },
      dashboardRoute: '/app/dashboard',
      historyRoute: '/app/history',
      writingLibraryRoute: '/app/writing/tasks',
      writingTaskRoute: '/app/writing/tasks/wt-001',
      readingTaskRoute: '/app/reading/task/rt-001',
      listeningTaskRoute: '/app/listening/task/lt-001',
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }));

    const { fetchSpeakingHome } = await import('../api');
    const payload = await fetchSpeakingHome();

    expect(payload.drillGroups[0].items[0].route).toBe('/speaking/phrasing/se-001');
    expect(payload.drillGroups[1].items[0].route).toBe('/speaking/selection');
    expect(payload.pastAttempts[0].route).toBe('/speaking/results/se-001');
    expect(payload.pastAttempts[1].route).toBe('/speaking/results/se-002');
    expect(payload.reviewCredits.route).toBe('/submissions');
    expect(payload.dashboardRoute).toBe('/');
    expect(payload.historyRoute).toBe('/submissions');
    expect(payload.writingLibraryRoute).toBe('/writing/library');
    expect(payload.writingTaskRoute).toBe('/writing/player?taskId=wt-001');
    expect(payload.readingTaskRoute).toBe('/reading/player/rt-001');
    expect(payload.listeningTaskRoute).toBe('/listening/player/lt-001');
  });
});

describe('API retry logic', () => {
  const originalFetch = globalThis.fetch;
  const env = process.env as Record<string, string | undefined>;
  const originalMockAuth = env.NEXT_PUBLIC_ENABLE_MOCK_AUTH;
  const originalNodeEnv = env.NODE_ENV;

  afterEach(() => {
    globalThis.fetch = originalFetch;
    env.NEXT_PUBLIC_ENABLE_MOCK_AUTH = originalMockAuth;
    env.NODE_ENV = originalNodeEnv;
    vi.resetModules();
  });

  it('retries on 500 errors up to MAX_RETRIES times', async () => {
    let callCount = 0;
    globalThis.fetch = vi.fn(async () => {
      callCount++;
      return new Response(JSON.stringify({ code: 'internal_server_error', message: 'fail', retryable: true }), {
        status: 500,
        headers: { 'content-type': 'application/json' },
      });
    });

    const { fetchUserProfile } = await import('../api');

    try {
      await fetchUserProfile();
    } catch (e) {
      // expected to fail after all retries exhausted
    }

    // Should have been called 3 times (1 initial + 2 retries)
    expect(callCount).toBe(3);
  }, 15000);

  it('does not retry on 4xx errors', async () => {
    let callCount = 0;
    globalThis.fetch = vi.fn(async () => {
      callCount++;
      return new Response(JSON.stringify({ code: 'not_found', message: 'Not found', retryable: false }), {
        status: 404,
        headers: { 'content-type': 'application/json' },
      });
    });

    const { fetchUserProfile } = await import('../api');

    try {
      await fetchUserProfile();
    } catch (e) {
      expect(e).toMatchObject({ name: 'ApiError', status: 404, code: 'not_found' });
    }

    expect(callCount).toBe(1);
  });

  it('retries on network errors', async () => {
    let callCount = 0;
    globalThis.fetch = vi.fn(async () => {
      callCount++;
      throw new TypeError('Failed to fetch');
    });

    const { fetchUserProfile } = await import('../api');

    try {
      await fetchUserProfile();
    } catch (e) {
      expect(e).toMatchObject({ name: 'ApiError', code: 'network_error' });
    }

    expect(callCount).toBe(3);
  }, 15000);

  it('sends bearer auth without debug headers even if the old mock-auth env flag is set', async () => {
    env.NODE_ENV = 'development';
    env.NEXT_PUBLIC_ENABLE_MOCK_AUTH = 'true';
    vi.resetModules();

    const authClient = await import('../auth-client');
    vi.mocked(authClient.ensureFreshAccessToken).mockResolvedValue('access-token-123');

    let requestHeaders: Headers | null = null;
    globalThis.fetch = vi.fn(async (_input, init) => {
      requestHeaders = new Headers(init?.headers);
      return new Response(JSON.stringify({ code: 'not_found', message: 'Not found', retryable: false }), {
        status: 404,
        headers: { 'content-type': 'application/json' },
      });
    });

    const { fetchUserProfile } = await import('../api');
    await expect(fetchUserProfile()).rejects.toBeInstanceOf(Error);

    expect(requestHeaders).not.toBeNull();
    expect(requestHeaders!.get('Authorization')).toBe('Bearer access-token-123');
    expect(requestHeaders!.get('X-Debug-Role')).toBeNull();
    expect(requestHeaders!.get('X-Debug-UserId')).toBeNull();
  });

  it('persists expert draft scratchpad and checklist fields', async () => {
    let requestBody: Record<string, unknown> | null = null;
    globalThis.fetch = vi.fn(async (_input, init) => {
      requestBody = JSON.parse(String(init?.body));
      return new Response(JSON.stringify({
        version: 3,
        state: 'saved',
        scores: { purpose: 5 },
        criterionComments: { purpose: 'Clear opening.' },
        finalComment: 'Strong response.',
        anchoredComments: [{ id: 'ac-1', text: 'Tighten this line.', startOffset: 3, endOffset: 12, createdAt: new Date().toISOString() }],
        timestampComments: [],
        scratchpad: 'Check discharge tone.',
        checklistItems: [{ id: 'purpose', label: 'Purpose is explicit.', checked: true }],
        savedAt: new Date().toISOString(),
      }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    });

    const { saveDraftReview } = await import('../api');
    const response = await saveDraftReview({
      reviewRequestId: 'review-queue-002',
      scores: { purpose: 5 },
      criterionComments: { purpose: 'Clear opening.' },
      finalComment: 'Strong response.',
      comments: [{ id: 'ac-1', text: 'Tighten this line.', startOffset: 3, endOffset: 12, createdAt: new Date().toISOString() }],
      scratchpad: 'Check discharge tone.',
      checklistItems: [{ id: 'purpose', label: 'Purpose is explicit.', checked: true }],
      savedAt: new Date().toISOString(),
      version: 2,
    });

    expect(requestBody).toMatchObject({
      scratchpad: 'Check discharge tone.',
      checklistItems: [{ id: 'purpose', label: 'Purpose is explicit.', checked: true }],
    });
    expect(response.scratchpad).toBe('Check discharge tone.');
    expect(response.checklistItems).toEqual([{ id: 'purpose', label: 'Purpose is explicit.', checked: true }]);
  });
});
