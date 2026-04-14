const ORIGINAL_ENV = { ...process.env };

function resetEnvironment() {
  vi.resetModules();
  process.env = { ...ORIGINAL_ENV };
  delete (globalThis as Record<string, unknown>).window;
}

describe('resolveApiBaseUrl', () => {
  afterEach(() => {
    resetEnvironment();
  });

  it('prefers NEXT_PUBLIC_API_BASE_URL when configured', async () => {
    process.env.NEXT_PUBLIC_API_BASE_URL = 'https://api.example.com/';
    process.env.PUBLIC_API_BASE_URL = 'https://public.example.com/';
    process.env.API_PROXY_TARGET_URL = 'http://127.0.0.1:5198/';

    const { resolveApiBaseUrl } = await import('./env');

    expect(resolveApiBaseUrl()).toBe('https://api.example.com');
  });

  it('uses the proxy path in the browser when NEXT_PUBLIC_API_BASE_URL is missing', async () => {
    (globalThis as Record<string, unknown>).window = {} as Window & typeof globalThis;
    delete process.env.NEXT_PUBLIC_API_BASE_URL;

    const { resolveApiBaseUrl } = await import('./env');

    expect(resolveApiBaseUrl()).toBe('/api/backend');
  });

  it('uses PUBLIC_API_BASE_URL on the server when available', async () => {
    delete process.env.NEXT_PUBLIC_API_BASE_URL;
    process.env.PUBLIC_API_BASE_URL = 'https://public.example.com/';
    process.env.API_PROXY_TARGET_URL = 'http://127.0.0.1:5198/';

    const { resolveApiBaseUrl } = await import('./env');

    expect(resolveApiBaseUrl()).toBe('https://public.example.com');
  });

  it('falls back to API_PROXY_TARGET_URL on the server when public envs are missing', async () => {
    delete process.env.NEXT_PUBLIC_API_BASE_URL;
    delete process.env.PUBLIC_API_BASE_URL;
    process.env.API_PROXY_TARGET_URL = 'http://api.internal:8080/';

    const { resolveApiBaseUrl } = await import('./env');

    expect(resolveApiBaseUrl()).toBe('http://api.internal:8080');
  });
});
