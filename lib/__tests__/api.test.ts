// Test the exported utilities from api.ts by importing them
// We need to mock auth-client before importing api
vi.mock('@/lib/auth-client', () => ({
  ensureFreshAccessToken: vi.fn(async () => null),
}));

export {}; // Make this a module for top-level await

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

  it('maps auth failures to sign-in guidance', () => {
    const error = new ApiError(401, 'not_authenticated', 'Unauthorized', false);
    expect(error.userMessage).toBe('Your session expired. Please sign in again.');
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
          items: [{ id: 'sp-drill-1', route: '/speaking/review/sa-001' }],
        },
        {
          id: 'empathy_clarification',
          title: 'Empathy and clarification drills',
          items: [{ id: 'sp-drill-2', route: '/speaking/tasks' }],
        },
      ],
      pastAttempts: [
        { route: '/speaking/result/se-001' },
        { route: '/speaking/attempt/sa-002' },
      ],
      reviewCredits: {
        route: '/reviews',
      },
      dashboardRoute: '/dashboard',
      historyRoute: '/history',
      writingLibraryRoute: '/writing/tasks',
      writingTaskRoute: '/writing/tasks/wt-001',
      readingTaskRoute: '/reading/task/rt-001',
      listeningTaskRoute: '/listening/task/lt-001',
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
    expect(payload.readingTaskRoute).toBe('/reading');
    expect(payload.listeningTaskRoute).toBe('/listening/player/lt-001');
  });
});

describe('mock booking API helpers', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.resetModules();
  });

  it('fetches booking detail and maps learner-safe speaking content', async () => {
    const calls: string[] = [];
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL) => {
      calls.push(String(input));
      return new Response(JSON.stringify({
        id: 'booking-1',
        bookingId: 'booking-1',
        mockBundleId: 'bundle-1',
        scheduledStartAt: '2026-05-13T10:00:00Z',
        timezoneIana: 'UTC',
        status: 'scheduled',
        candidateCardVisible: true,
        interlocutorCardVisible: false,
        speakingContent: {
          candidateCard: {
            candidateRole: 'Practice nurse',
            setting: 'Community clinic',
            patientRole: 'Patient',
            task: 'Agree a follow-up plan.',
            background: 'You are a practice nurse.',
            tasks: ['Acknowledge missed visits'],
          },
          interlocutorCard: { hiddenInformation: 'must not map' },
          prepTimeSeconds: 120,
          roleplayTimeSeconds: 240,
          roleplayCount: 1,
        },
      }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    });

    const { fetchMockBookingDetail } = await import('../api');
    const booking = await fetchMockBookingDetail('booking-1');

    expect(calls[0]).toContain('/v1/mock-bookings/booking-1');
    expect(booking.speakingContent?.candidateCard?.candidateRole).toBe('Practice nurse');
    expect(booking.speakingContent?.prepTimeSeconds).toBe(120);
    expect(booking.speakingContent?.roleplayCount).toBe(1);
    expect('interlocutorCard' in (booking.speakingContent as Record<string, unknown>)).toBe(false);
  });

  it('passes bundle and paper filters to admin item analysis', async () => {
    const calls: string[] = [];
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL) => {
      calls.push(String(input));
      return new Response(JSON.stringify({ items: [], generatedAt: null }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    });

    const { fetchAdminMockItemAnalysis } = await import('../api');
    await fetchAdminMockItemAnalysis({ bundleId: 'bundle-1', paperId: 'paper-1' });

    expect(calls[0]).toContain('/v1/admin/mocks/item-analysis?');
    expect(calls[0]).toContain('bundleId=bundle-1');
    expect(calls[0]).toContain('paperId=paper-1');
  });
});

describe('speaking API helpers', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.resetModules();
  });

  it('uses an existing mock-session attempt id when submitting a speaking recording', async () => {
    const calls: Array<{ url: string; method: string; body?: unknown }> = [];
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = String(init?.method ?? 'GET');
      calls.push({
        url,
        method,
        body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
      });

      if (url.includes('/audio/upload-session')) {
        return new Response(JSON.stringify({ uploadUrl: '/v1/speaking/upload-sessions/us-bound/content', uploadSessionId: 'us-bound' }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }

      if (url.includes('/audio/complete')) {
        return new Response(JSON.stringify({ attemptId: 'sa-bound-mock-1', canSubmit: true }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }

      if (url.includes('/submit')) {
        return new Response(JSON.stringify({ attemptId: 'sa-bound-mock-1', evaluationId: 'se-bound-mock-1', state: 'queued' }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }

      return new Response(null, { status: 204 });
    });

    const { submitSpeakingRecording } = await import('../api');
    const result = await submitSpeakingRecording(
      'st-001',
      new Blob(['audio'], { type: 'audio/webm' }),
      123,
      'exam',
      { accepted: true, text: 'Consent accepted' },
      { attemptId: 'sa-bound-mock-1', mockSessionId: 'sms-bound-mock-1' },
    );

    expect(result.submissionId).toBe('se-bound-mock-1');
    expect(calls.some((call) => call.url.endsWith('/v1/speaking/attempts') && call.method === 'POST')).toBe(false);
    expect(calls.map((call) => call.url)).toEqual(expect.arrayContaining([
      expect.stringContaining('/v1/speaking/attempts/sa-bound-mock-1/audio/upload-session?contentId=st-001&mockSessionId=sms-bound-mock-1'),
      expect.stringContaining('/v1/speaking/attempts/sa-bound-mock-1/audio/complete?contentId=st-001&mockSessionId=sms-bound-mock-1'),
      expect.stringContaining('/v1/speaking/attempts/sa-bound-mock-1/submit?contentId=st-001&mockSessionId=sms-bound-mock-1'),
    ]));
  });

  it('keeps absolute backend speaking upload URLs on the browser proxy path', async () => {
    const calls: Array<{ url: string; method: string }> = [];
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = String(init?.method ?? 'GET');
      calls.push({ url, method });

      if (url.includes('/audio/upload-session')) {
        return new Response(JSON.stringify({
          uploadUrl: 'http://localhost:5198/v1/speaking/upload-sessions/us-absolute/content',
          uploadSessionId: 'us-absolute',
        }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }

      if (url.includes('/audio/complete')) {
        return new Response(JSON.stringify({ attemptId: 'sa-absolute', canSubmit: true }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }

      if (url.includes('/submit')) {
        return new Response(JSON.stringify({ attemptId: 'sa-absolute', evaluationId: 'se-absolute', state: 'queued' }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }

      return new Response(null, { status: 204 });
    });

    const { submitSpeakingRecording } = await import('../api');
    await submitSpeakingRecording(
      'st-001',
      new Blob(['audio'], { type: 'audio/webm' }),
      123,
      'self',
      { accepted: true, text: 'Consent accepted' },
      { attemptId: 'sa-absolute' },
    );

    expect(calls).toEqual(expect.arrayContaining([
      { url: '/api/backend/v1/speaking/upload-sessions/us-absolute/content', method: 'PUT' },
    ]));
    expect(calls.some((call) => call.url.startsWith('http://localhost:5198/'))).toBe(false);
  });

  it('keeps absolute backend object URLs on the browser proxy path', async () => {
    const originalCreateObjectURL = URL.createObjectURL;
    URL.createObjectURL = vi.fn(() => 'blob:expert-audio');
    const calls: Array<{ url: string; method: string }> = [];
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      calls.push({ url: String(input), method: String(init?.method ?? 'GET') });
      return new Response('audio', {
        status: 200,
        headers: { 'content-type': 'audio/webm' },
      });
    });

    try {
      const { fetchAuthorizedObjectUrl } = await import('../api');
      const result = await fetchAuthorizedObjectUrl('http://localhost:5198/v1/expert/reviews/review-001/speaking/audio?download=1');

      expect(result).toBe('blob:expert-audio');
      expect(calls).toEqual([
        { url: '/api/backend/v1/expert/reviews/review-001/speaking/audio?download=1', method: 'GET' },
      ]);
    } finally {
      URL.createObjectURL = originalCreateObjectURL;
    }
  });
});

describe('billing wallet top-up API helper', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.resetModules();
  });

  it('serializes the caller-supplied idempotency key', async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(
      JSON.stringify({ checkoutUrl: 'https://example.test' }),
      {
        status: 200,
        headers: { 'content-type': 'application/json' },
      },
    ));
    globalThis.fetch = fetchMock;

    const { createWalletTopUp } = await import('../api');
    await createWalletTopUp(25, 'stripe', 'idem-wallet-123');

    const init = fetchMock.mock.calls[0]?.[1];
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body))).toEqual({
      amount: 25,
      gateway: 'stripe',
      idempotencyKey: 'idem-wallet-123',
    });
  });
});

describe('admin mock bundle API helpers', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.resetModules();
  });

  it('serializes update, reorder, and booking assignment requests', async () => {
    const calls: Array<{ url: string; method: string; body?: unknown }> = [];
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      calls.push({
        url: String(input),
        method: String(init?.method ?? 'GET'),
        body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
      });
      return new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    });

    const { updateAdminMockBundle, reorderAdminMockBundleSections, assignAdminMockBooking } = await import('../api');

    await updateAdminMockBundle('bundle 1', { title: 'Updated bundle', releasePolicy: 'instant' });
    await reorderAdminMockBundleSections('bundle 1', ['section-2', 'section-1']);
    await assignAdminMockBooking('booking 1', { assignedTutorId: 'tutor-1', status: 'confirmed' });

    expect(calls).toMatchObject([
      {
        method: 'PUT',
        body: { title: 'Updated bundle', releasePolicy: 'instant' },
      },
      {
        method: 'PUT',
        body: { sectionIds: ['section-2', 'section-1'] },
      },
      {
        method: 'PATCH',
        body: { assignedTutorId: 'tutor-1', status: 'confirmed' },
      },
    ]);
    expect(calls[0].url).toContain('/v1/admin/mock-bundles/bundle%201');
    expect(calls[1].url).toContain('/v1/admin/mock-bundles/bundle%201/sections/reorder');
    expect(calls[2].url).toContain('/v1/admin/mock-bookings/booking%201/assign');
  });
});

describe('readiness mapping', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.resetModules();
  });

  it('fills safe evidence defaults when readiness evidence is missing', async () => {
    globalThis.fetch = vi.fn(async () => new Response(JSON.stringify({
      targetDate: '2026-06-27',
      weeksRemaining: 8,
      overallRisk: 'moderate',
      recommendedStudyHours: 8,
      weakestLink: 'Writing - conciseness',
      subTests: [
        { id: 'rd-w', name: 'Writing', readiness: 60, target: 80, status: 'Needs attention', isWeakest: true },
      ],
      blockers: null,
      computedAt: '2026-04-28T04:32:53Z',
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }));

    const { fetchReadiness } = await import('../api');
    const payload = await fetchReadiness();

    expect(payload.blockers).toEqual([]);
    expect(payload.evidence).toEqual({
      mocksCompleted: 0,
      practiceQuestions: 0,
      expertReviews: 0,
      recentTrend: 'Trend data will appear after more practice.',
      lastUpdated: '2026-04-28',
    });
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
    globalThis.fetch = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
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
    globalThis.fetch = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
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

describe('apiClient', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.resetModules();
  });

  it('routes GET requests through the centralized client', async () => {
    let url = '';
    let method = '';
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      url = String(input);
      method = String(init?.method);
      return new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    });

    const { apiClient } = await import('../api');
    const payload = await apiClient.get<{ ok: boolean }>('/v1/admin/example');

    expect(payload.ok).toBe(true);
    expect(url).toContain('/v1/admin/example');
    expect(method).toBe('GET');
  });

  it('serializes JSON bodies for POST, PUT and PATCH helpers', async () => {
    const calls: Array<{ method: string | undefined; body: unknown }> = [];
    globalThis.fetch = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      calls.push({ method: init?.method, body: init?.body });
      return new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    });

    const { apiClient } = await import('../api');
    await apiClient.post('/v1/example', { a: 1 });
    await apiClient.put('/v1/example/1', { b: 2 });
    await apiClient.patch('/v1/example/1', { c: 3 });

    expect(calls.map((call) => call.method)).toEqual(['POST', 'PUT', 'PATCH']);
    expect(calls.map((call) => JSON.parse(String(call.body)))).toEqual([{ a: 1 }, { b: 2 }, { c: 3 }]);
  });

  it('returns JSON for explicitly accepted non-2xx statuses', async () => {
    globalThis.fetch = vi.fn(async () => new Response(JSON.stringify({ outcome: 'confirm-required' }), {
      status: 412,
      headers: { 'content-type': 'application/json' },
    }));

    const { apiClient } = await import('../api');
    const payload = await apiClient.postWithAcceptedStatuses<{ outcome: string }>(
      '/v1/listening/v2/attempts/att-1/advance',
      { toState: 'a1_preview', confirmToken: null },
      [412],
    );

    expect(payload).toEqual({ outcome: 'confirm-required' });
  });

  it('does not force JSON content type for form uploads', async () => {
    let headers: Headers | null = null;
    globalThis.fetch = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      headers = new Headers(init?.headers);
      return new Response(JSON.stringify({ stagedImportId: 'imp-1' }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    });

    const form = new FormData();
    form.set('file', new Blob(['zip']), 'import.zip');

    const { apiClient } = await import('../api');
    await apiClient.postForm('/v1/admin/imports/zip', form);

    expect(headers).not.toBeNull();
    expect(headers!.get('Content-Type')).toBeNull();
  });

  it('sends DELETE requests through the centralized client', async () => {
    let method = '';
    globalThis.fetch = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      method = String(init?.method);
      return new Response(null, { status: 204 });
    });

    const { apiClient } = await import('../api');
    await apiClient.delete('/v1/example/1');

    expect(method).toBe('DELETE');
  });
});

describe('error logging in catch blocks', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.resetModules();
    vi.restoreAllMocks();
  });

  it('logs console.error when apiRequest cannot parse error response body', async () => {
    // The catch block in apiRequest demotes non-JSON error bodies to
    // console.debug under NODE_ENV=development, and stays silent in
    // production. We assert the development-mode debug log here so the
    // test pins the intentional log channel rather than expecting error.
    vi.stubEnv('NODE_ENV', 'development');
    const debugSpy = vi.spyOn(console, 'debug').mockImplementation(() => {});
    globalThis.fetch = vi.fn(async () => new Response('not json', {
      status: 400,
      headers: { 'content-type': 'text/plain' },
    }));

    try {
      const { fetchUserProfile } = await import('../api');
      await expect(fetchUserProfile()).rejects.toThrow();

      expect(debugSpy).toHaveBeenCalledWith(
        expect.stringContaining('[API]'),
        expect.anything(),
      );
    } finally {
      vi.unstubAllEnvs();
    }
  });

  it('logs console.error when fetchAuthorizedObjectUrl cannot parse error response', async () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    globalThis.fetch = vi.fn(async () => new Response('not json', {
      status: 500,
      headers: { 'content-type': 'text/plain' },
    }));

    const { fetchAuthorizedObjectUrl } = await import('../api');
    await expect(fetchAuthorizedObjectUrl('/test-object')).rejects.toThrow();

    expect(errorSpy).toHaveBeenCalledWith(
      expect.stringContaining('[API] fetchAuthorizedObjectUrl'),
      expect.anything(),
    );
  });
});

describe('rulebook and grounded AI API wrappers', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.resetModules();
  });

  it('calls the writing rulebook endpoint', async () => {
    let url = '';
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL) => {
      url = String(input);
      return new Response(JSON.stringify({ kind: 'writing', profession: 'medicine', version: '1.0.0', sections: [], rules: [] }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    });

    const { fetchWritingRulebook } = await import('../api');
    const payload = await fetchWritingRulebook('medicine');

    expect(url).toContain('/v1/rulebooks/writing/medicine');
    expect(payload.kind).toBe('writing');
  });

  it('posts the writing lint payload to the backend', async () => {
    let body: Record<string, unknown> | null = null;
    globalThis.fetch = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      body = JSON.parse(String(init?.body));
      return new Response(JSON.stringify({ findings: [], totals: { critical: 0, major: 0, minor: 0, info: 0 } }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    });

    const { lintWritingViaApi } = await import('../api');
    await lintWritingViaApi({
      letterText: 'Dear Dr Smith',
      letterType: 'routine_referral',
      profession: 'medicine',
      recipientSpecialty: 'Cardiologist',
    });

    expect(body).toMatchObject({
      letterText: 'Dear Dr Smith',
      letterType: 'routine_referral',
      profession: 'medicine',
      recipientSpecialty: 'Cardiologist',
    });
  });

  it('posts the grounded AI request payload to the backend', async () => {
    let body: Record<string, unknown> | null = null;
    globalThis.fetch = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      body = JSON.parse(String(init?.body));
      return new Response(JSON.stringify({
        completion: '{}',
        rulebookVersion: '1.0.0',
        appliedRuleIds: ['R03.4'],
        metadata: {
          rulebookVersion: '1.0.0',
          rulebookKind: 'writing',
          profession: 'medicine',
          scoringPassMark: 350,
          scoringGrade: 'B',
          appliedRulesCount: 1,
        },
      }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    });

    const { completeGroundedAi } = await import('../api');
    await completeGroundedAi(
      { kind: 'writing', profession: 'medicine', task: 'score', letterType: 'routine_referral', candidateCountry: 'UK' },
      'Candidate letter here',
      'digitalocean',
      'anthropic-claude-opus-4.7',
    );

    expect(body).toMatchObject({
      kind: 'writing',
      profession: 'medicine',
      task: 'score',
      letterType: 'routine_referral',
      candidateCountry: 'UK',
      userInput: 'Candidate letter here',
      provider: 'digitalocean',
      model: 'anthropic-claude-opus-4.7',
    });
  });
});
