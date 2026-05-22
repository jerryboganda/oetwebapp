// Verifies that reading-authoring-api includes the x-csrf-token header on
// state-changing requests, so the Next.js backend proxy (lib/backend-proxy.ts
// validateProxyCsrf) does not return 403 "missing or invalid CSRF token" when
// the user has an oet_rt refresh cookie present (httpOnly, server-only).

vi.mock('@/lib/auth-client', () => ({
  ensureFreshAccessToken: vi.fn(async () => 'fake.jwt.token'),
}));

vi.mock('@/lib/env', () => ({
  env: { apiBaseUrl: '' },
}));

vi.mock('@/lib/network/fetch-with-timeout', () => ({
  fetchWithTimeout: vi.fn(),
}));

export {};

const { fetchWithTimeout } = await import('../network/fetch-with-timeout');
const mod = await import('../reading-authoring-api');

function setCsrfCookie(value: string) {
  Object.defineProperty(document, 'cookie', {
    configurable: true,
    get: () => `oet_auth=1; oet_csrf=${value}`,
  });
}

describe('reading-authoring-api CSRF token injection', () => {
  beforeEach(() => {
    (fetchWithTimeout as unknown as ReturnType<typeof vi.fn>).mockReset();
    (fetchWithTimeout as unknown as ReturnType<typeof vi.fn>).mockResolvedValue(
      new Response(null, { status: 204 }),
    );
    setCsrfCookie('test-csrf-token-value');
  });

  it('attaches x-csrf-token on POST ensureCanonicalParts', async () => {
    await mod.ensureCanonicalParts('paper-1');
    const call = (fetchWithTimeout as unknown as ReturnType<typeof vi.fn>).mock.calls[0];
    const headers = call[1].headers as Headers;
    expect(headers.get('x-csrf-token')).toBe('test-csrf-token-value');
    expect(headers.get('authorization')).toBe('Bearer fake.jwt.token');
  });

  it('does not attach x-csrf-token on GET getReadingStructureAdmin', async () => {
    (fetchWithTimeout as unknown as ReturnType<typeof vi.fn>).mockResolvedValue(
      new Response(JSON.stringify({ paperId: 'p', parts: [] }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
    await mod.getReadingStructureAdmin('paper-1');
    const call = (fetchWithTimeout as unknown as ReturnType<typeof vi.fn>).mock.calls[0];
    const headers = call[1].headers as Headers;
    expect(headers.get('x-csrf-token')).toBeNull();
  });

  it('attaches x-csrf-token on PUT/PATCH/DELETE wrappers', async () => {
    // upsertReadingPart issues PUT
    await mod.upsertReadingPart('paper-1', 'A', {
      timeLimitMinutes: 15,
      instructions: '',
    });
    const headers = (fetchWithTimeout as unknown as ReturnType<typeof vi.fn>).mock.calls[0][1].headers as Headers;
    expect(headers.get('x-csrf-token')).toBe('test-csrf-token-value');
  });
});
