/**
 * Runtime environment validation.
 * Import this at the app entry point to fail fast on missing config.
 */

const IS_PRODUCTION = process.env.NODE_ENV === 'production';

function optionalEnv(name: string, fallback: string): string {
  return process.env[name] || fallback;
}

export const env = {
  // API
  apiBaseUrl: optionalEnv('NEXT_PUBLIC_API_BASE_URL', 'http://localhost:5198').replace(/\/$/, ''),

  // Feature flags
  enableMockAuth: IS_PRODUCTION ? false : process.env.NEXT_PUBLIC_ENABLE_MOCK_AUTH === 'true',
} as const;
