/**
 * Runtime environment validation.
 * Import this at the app entry point to fail fast on missing config.
 */

const IS_PRODUCTION = process.env.NODE_ENV === 'production';

function requireEnv(name: string, value: string | undefined): string {
  if (!value || value.trim() === '') {
    throw new Error(`Missing required environment variable: ${name}`);
  }
  return value;
}

function optionalEnv(name: string, fallback: string): string {
  return process.env[name] || fallback;
}

export const env = {
  // API
  apiBaseUrl: optionalEnv('NEXT_PUBLIC_API_BASE_URL', 'http://localhost:5198').replace(/\/$/, ''),

  // Firebase (required in production)
  firebase: IS_PRODUCTION
    ? {
        apiKey: requireEnv('NEXT_PUBLIC_FIREBASE_API_KEY', process.env.NEXT_PUBLIC_FIREBASE_API_KEY),
        authDomain: requireEnv('NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN', process.env.NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN),
        projectId: requireEnv('NEXT_PUBLIC_FIREBASE_PROJECT_ID', process.env.NEXT_PUBLIC_FIREBASE_PROJECT_ID),
      }
    : {
        apiKey: process.env.NEXT_PUBLIC_FIREBASE_API_KEY ?? '',
        authDomain: process.env.NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN ?? '',
        projectId: process.env.NEXT_PUBLIC_FIREBASE_PROJECT_ID ?? '',
      },

  // Feature flags
  enableMockAuth: process.env.NEXT_PUBLIC_ENABLE_MOCK_AUTH === 'true',
} as const;
